using ClipperLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Path = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;

namespace LaserGRBL
{
	/// <summary>
	/// Moves closed SVG contours by half the kerf so cut parts keep their drawn size:
	/// outlines grow and holes shrink. Each contour block is one marker-delimited range from the SVG import.
	/// Blocks that are open, nested in an unsupported way, or not plain absolute XY moves are copied unchanged.
	/// </summary>
	internal static class KerfCompensation
	{
		private const double Scale = 1000; // Clipper works on integers; 1 unit = 1 µm.
		private const double CloseTolerance = 0.01;
		private const double ArcToleranceMm = 0.005;

		internal sealed class Result
		{
			internal GrblCommand[] Commands;
			internal int[] Contours;
			internal int Compensated;
			internal int Skipped;
		}

		private sealed class Block
		{
			internal int Start, End;
			internal readonly List<GrblCommand> Pre = new List<GrblCommand>();
			internal int RapidIndex = -1;
			internal readonly List<GrblCommand> Post = new List<GrblCommand>();
			internal readonly List<DoublePoint> Points = new List<DoublePoint>();
			internal string Feed, Power;
			internal bool Supported = true;
			internal bool Closed;
			internal Path Polygon;
			internal Path Offset;
		}

		internal static Result Apply(GrblCommand[] source, int[] contours, double kerfMm)
		{
			if (kerfMm < 0 || kerfMm > 5) throw new ArgumentOutOfRangeException("kerfMm", "Kerf must be between 0 and 5 mm.");
			if (kerfMm == 0 || contours == null || contours.Length == 0)
				return new Result { Commands = source, Contours = contours };

			var blocks = Parse(source, contours);
			var closed = blocks.Where(b => b.Supported && b.Closed).ToList();
			foreach (var block in closed)
			{
				var probe = block.Polygon[0];
				int depth = closed.Count(other => other != block && Clipper.PointInPolygon(probe, other.Polygon) != 0);
				// Even depth is material outside the part's outline; odd depth is a hole inside a part.
				double delta = (depth % 2 == 0 ? 1 : -1) * kerfMm / 2 * Scale;
				block.Offset = OffsetPolygon(block.Polygon, delta, block.Points[0]);
			}

			var output = new List<GrblCommand>(source.Length);
			var starts = new List<int>(contours.Length);
			for (int i = 0; i < contours[0]; i++) output.Add(source[i]);
			int compensated = 0, skipped = 0;
			foreach (var block in blocks)
			{
				starts.Add(output.Count);
				if (block.Offset == null)
				{
					if (block.Points.Count > 1) skipped++;
					for (int i = block.Start; i < block.End; i++) output.Add(source[i]);
					continue;
				}
				compensated++;
				Emit(block, output);
			}
			return new Result { Commands = output.ToArray(), Contours = starts.ToArray(), Compensated = compensated, Skipped = skipped };
		}

		private static List<Block> Parse(GrblCommand[] source, int[] contours)
		{
			var blocks = new List<Block>();
			double x = 0, y = 0;
			int motion = 0;
			bool absolute = true;
			// The prefix sets modal state and the start position; walk it without collecting points.
			for (int i = 0; i < contours[0]; i++) Track(source[i], ref x, ref y, ref motion, ref absolute, null);
			for (int b = 0; b < contours.Length; b++)
			{
				var block = new Block { Start = contours[b], End = b + 1 < contours.Length ? contours[b + 1] : source.Length };
				for (int i = block.Start; i < block.End; i++)
					Track(source[i], ref x, ref y, ref motion, ref absolute, block);
				if (block.Supported && block.Points.Count >= 4)
				{
					var first = block.Points[0];
					var last = block.Points[block.Points.Count - 1];
					block.Closed = Math.Abs(first.X - last.X) <= CloseTolerance && Math.Abs(first.Y - last.Y) <= CloseTolerance;
					if (block.Closed)
					{
						block.Polygon = block.Points.Take(block.Points.Count - 1).Select(p => new IntPoint(p.X * Scale, p.Y * Scale)).ToList();
						if (Math.Abs(Clipper.Area(block.Polygon)) < 1) block.Closed = false; // degenerate, nothing to offset
					}
				}
				blocks.Add(block);
			}
			return blocks;
		}

		private static void Track(GrblCommand original, ref double x, ref double y, ref int motion, ref bool absolute, Block block)
		{
			var command = new GrblCommand(original.Command);
			command.BuildHelper();
			var g = command.G;
			if (g != null)
			{
				if (g.Number == 90) absolute = true;
				else if (g.Number == 91) absolute = false;
				else if (g.Number >= 0 && g.Number <= 3) motion = (int)g.Number;
				else if (g.Number == 20 && block != null) block.Supported = false;
			}
			bool moves = command.IsMovement;
			if (block != null && (!absolute || command.Z != null)) block.Supported = false;
			if (!moves)
			{
				if (block != null) (block.Points.Count == 0 ? block.Pre : block.Post).Add(original);
				return;
			}
			double startX = x, startY = y;
			if (absolute)
			{
				if (command.X != null) x = (double)command.X.Number;
				if (command.Y != null) y = (double)command.Y.Number;
			}
			else
			{
				if (command.X != null) x += (double)command.X.Number;
				if (command.Y != null) y += (double)command.Y.Number;
			}
			if (block == null || !block.Supported) return;
			if (motion == 0)
			{
				// A second rapid inside one block means it holds more than one path.
				if (block.Points.Count > 0) { block.Supported = false; return; }
				block.RapidIndex = block.Pre.Count;
				block.Pre.Add(original);
				return;
			}
			if (block.Post.Count > 0 || command.M != null) { block.Supported = false; return; }
			if (block.Points.Count == 0)
			{
				block.Points.Add(new DoublePoint(startX, startY));
				block.Feed = command.F?.ToString();
				block.Power = command.S?.ToString();
			}
			else if (command.F != null && command.F.ToString() != block.Feed || command.S != null && command.S.ToString() != block.Power)
			{
				// Feed or power changes partway through; rewriting the points would lose where it changes.
				block.Supported = false;
				return;
			}
			if (motion == 1)
				block.Points.Add(new DoublePoint(x, y));
			else
				AddArc(block, command, startX, startY, x, y, motion == 2);
		}

		private static void AddArc(Block block, GrblCommand command, double sx, double sy, double ex, double ey, bool clockwise)
		{
			double cx, cy;
			if (command.R != null)
			{
				double r = (double)command.R.Number;
				double dx = ex - sx, dy = ey - sy, chord = Math.Sqrt(dx * dx + dy * dy);
				if (chord < 1e-9 || chord > 2 * Math.Abs(r) + 1e-6) { block.Supported = false; return; }
				double h = Math.Sqrt(Math.Max(0, r * r - chord * chord / 4));
				// G-code: positive R takes the short arc, negative R the long one.
				double sign = (clockwise ? -1 : 1) * (r < 0 ? -1 : 1);
				cx = (sx + ex) / 2 - sign * h * dy / chord;
				cy = (sy + ey) / 2 + sign * h * dx / chord;
			}
			else
			{
				cx = sx + (double)(command.I?.Number ?? 0);
				cy = sy + (double)(command.J?.Number ?? 0);
			}
			double radius = Math.Sqrt((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy));
			if (radius < 1e-6) { block.Points.Add(new DoublePoint(ex, ey)); return; }
			double a0 = Math.Atan2(sy - cy, sx - cx), a1 = Math.Atan2(ey - cy, ex - cx);
			double sweep = a1 - a0;
			if (clockwise) { if (sweep >= -1e-9) sweep -= 2 * Math.PI; }
			else if (sweep <= 1e-9) sweep += 2 * Math.PI;
			double maxStep = 2 * Math.Acos(Math.Max(-1, 1 - ArcToleranceMm / radius));
			int segments = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / Math.Max(maxStep, 1e-3)));
			for (int i = 1; i < segments; i++)
			{
				double a = a0 + sweep * i / segments;
				block.Points.Add(new DoublePoint(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
			}
			block.Points.Add(new DoublePoint(ex, ey));
		}

		private static Path OffsetPolygon(Path polygon, double delta, DoublePoint start)
		{
			var offset = new ClipperOffset(2, ArcToleranceMm * Scale);
			offset.AddPath(polygon, JoinType.jtRound, EndType.etClosedPolygon);
			var solution = new Paths();
			offset.Execute(ref solution, delta);
			// A hole smaller than the kerf vanishes, and a thin neck can split into pieces. Both are left as drawn.
			if (solution.Count != 1 || solution[0].Count < 3) return null;
			var path = solution[0];
			if (Clipper.Orientation(path) != Clipper.Orientation(polygon)) path.Reverse();
			int nearest = 0;
			double best = double.MaxValue;
			for (int i = 0; i < path.Count; i++)
			{
				double dx = path[i].X / Scale - start.X, dy = path[i].Y / Scale - start.Y, d = dx * dx + dy * dy;
				if (d < best) { best = d; nearest = i; }
			}
			// Start where the original contour started so the cut order and pierce point barely move.
			return path.Skip(nearest).Concat(path.Take(nearest)).ToList();
		}

		private static void Emit(Block block, List<GrblCommand> output)
		{
			var path = block.Offset;
			string start = Point(path[0]);
			if (block.RapidIndex < 0) output.Add(new GrblCommand("G0 " + start));
			for (int i = 0; i < block.Pre.Count; i++)
				output.Add(i == block.RapidIndex ? new GrblCommand("G0 " + start) : block.Pre[i]);
			string words = (block.Feed != null ? " " + block.Feed : "") + (block.Power != null ? " " + block.Power : "");
			for (int i = 1; i <= path.Count; i++)
				output.Add(new GrblCommand("G1 " + Point(path[i % path.Count]) + (i == 1 ? words : "")));
			output.AddRange(block.Post);
		}

		private static string Point(IntPoint point)
		{
			return string.Format(CultureInfo.InvariantCulture, "X{0:0.###} Y{1:0.###}", point.X / Scale, point.Y / Scale);
		}
	}
}
