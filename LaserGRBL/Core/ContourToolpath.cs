using ClipperLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Path = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;

namespace LaserGRBL
{
	internal enum CutOrder { AsDrawn, InsideFirst }
	internal enum FillPattern { None, Hatch, CrossHatch, Offset }

	/// <summary>How an imported SVG operation's contours are rewritten before it runs. Saved with projects.</summary>
	[Serializable]
	internal sealed class ToolpathOptions
	{
		internal CutOrder Order = CutOrder.AsDrawn;
		/// <summary>Length of a straight lead-in on the waste side of closed contours, in mm.</summary>
		internal decimal LeadInMm;
		/// <summary>How far to keep cutting past the start of a closed contour, in mm.</summary>
		internal decimal OvercutMm;
		/// <summary>Uncut bridges left on each outer outline so parts stay in the sheet.</summary>
		internal int TabCount;
		internal decimal TabWidthMm = 1;
		internal FillPattern Fill = FillPattern.None;
		internal decimal FillSpacingMm = 0.1m;
		internal decimal FillAngle;
		/// <summary>Drop the closed outlines after filling them.</summary>
		internal bool FillOnly;
		/// <summary>Lower Z by this much before each repeat, then return. Positive moves the head down (Z−).</summary>
		internal decimal ZStepMm;

		internal void Validate()
		{
			if (LeadInMm < 0 || LeadInMm > 20) throw new ArgumentOutOfRangeException("LeadInMm", "Lead-in must be between 0 and 20 mm.");
			if (OvercutMm < 0 || OvercutMm > 20) throw new ArgumentOutOfRangeException("OvercutMm", "Overcut must be between 0 and 20 mm.");
			if (TabCount < 0 || TabCount > 50) throw new ArgumentOutOfRangeException("TabCount", "Tabs must be between 0 and 50 per outline.");
			if (TabCount > 0 && (TabWidthMm < 0.1m || TabWidthMm > 20)) throw new ArgumentOutOfRangeException("TabWidthMm", "Tab width must be between 0.1 and 20 mm.");
			if (Fill != FillPattern.None && (FillSpacingMm < 0.02m || FillSpacingMm > 20)) throw new ArgumentOutOfRangeException("FillSpacingMm", "Fill spacing must be between 0.02 and 20 mm.");
			if (FillAngle < -360 || FillAngle > 360) throw new ArgumentOutOfRangeException("FillAngle", "Fill angle must be between -360 and 360 degrees.");
			if (ZStepMm < 0 || ZStepMm > 50) throw new ArgumentOutOfRangeException("ZStepMm", "Z step must be between 0 and 50 mm.");
		}

		internal bool ChangesContours
		{
			get { return Order != CutOrder.AsDrawn || LeadInMm > 0 || OvercutMm > 0 || TabCount > 0 || Fill != FillPattern.None; }
		}

		internal bool IsEmpty { get { return !ChangesContours && ZStepMm == 0; } }

		internal ToolpathOptions Clone() { return (ToolpathOptions)MemberwiseClone(); }

		public override string ToString()
		{
			var parts = new List<string>();
			var c = CultureInfo.CurrentCulture;
			if (Order == CutOrder.InsideFirst) parts.Add("inside first");
			if (Fill != FillPattern.None) parts.Add(string.Format(c, "{0} {1:0.###} mm{2}", Fill == FillPattern.CrossHatch ? "cross-hatch" : Fill.ToString().ToLowerInvariant(), FillSpacingMm, FillOnly ? " only" : ""));
			if (LeadInMm > 0) parts.Add(string.Format(c, "lead-in {0:0.##}", LeadInMm));
			if (OvercutMm > 0) parts.Add(string.Format(c, "overcut {0:0.##}", OvercutMm));
			if (TabCount > 0) parts.Add(string.Format(c, "{0}×{1:0.##} mm tabs", TabCount, TabWidthMm));
			if (ZStepMm > 0) parts.Add(string.Format(c, "Z −{0:0.##}/pass", ZStepMm));
			return parts.Count == 0 ? "As drawn" : string.Join(", ", parts);
		}
	}

	/// <summary>
	/// Rewrites the marker-delimited contour blocks of an SVG import: kerf offset, cut order, lead-ins, overcut,
	/// tabs and fills. Blocks it can't parse safely are copied unchanged, and the result says how many.
	/// </summary>
	internal static class ContourToolpath
	{
		private const double Scale = 1000; // Clipper works on integers; 1 unit = 1 µm.
		private const double CloseTolerance = 0.01;
		private const double ArcToleranceMm = 0.005;
		private const int MaxFillSegments = 500000;

		internal sealed class Result
		{
			internal GrblCommand[] Commands;
			internal int[] Contours;
			internal int KerfCompensated, KerfSkipped;
			internal bool Reordered;
			internal int LeadIns, LeadSkipped, Tabbed, FillPaths;
			internal readonly List<string> Notes = new List<string>();
		}

		private struct Move
		{
			internal DoublePoint P;
			internal bool On;
			internal Move(DoublePoint p, bool on) { P = p; On = on; }
		}

		private sealed class State
		{
			internal double X, Y;
			internal int Motion;
			internal bool Absolute = true;
			internal string Feed, Power;
		}

		private sealed class Block
		{
			internal int Start, End;
			internal readonly List<GrblCommand> Pre = new List<GrblCommand>();
			internal int RapidIndex = -1;
			internal readonly List<GrblCommand> Post = new List<GrblCommand>();
			internal readonly List<DoublePoint> Points = new List<DoublePoint>();
			internal string Feed, Power;           // words on the first burn move
			internal string ModalFeed, ModalPower; // words in effect when burning starts
			internal bool Supported = true;
			internal bool Closed;
			internal Path Polygon;
			internal int Depth;
			// Reordering needs every block to start with its own rapid and use absolute XY. Blocks that rely on the
			// previous path having switched the laser off get the file's own pen-up command in front of them.
			internal bool Reorderable = true;
			internal bool Burned, LaserOff = true, StartsOff;
			internal int FirstRapid = -1, FirstBurn = -1, LastBurn = -1;
			internal DoublePoint StartPos, EndPos;
			internal bool KerfApplied;
			internal List<DoublePoint> Loop; // rewritten closed path, start point first, not repeated at the end
			internal List<GrblCommand> Output;
		}

		internal static Result Apply(GrblCommand[] source, int[] contours, ToolpathOptions options, double kerfMm)
		{
			if (kerfMm < 0 || kerfMm > 5) throw new ArgumentOutOfRangeException("kerfMm", "Kerf must be between 0 and 5 mm.");
			options = options ?? new ToolpathOptions();
			options.Validate();
			var result = new Result();
			if (contours == null || contours.Length == 0 || kerfMm == 0 && !options.ChangesContours)
			{
				result.Commands = source;
				result.Contours = contours;
				return result;
			}

			int footerStart;
			var blocks = Parse(source, contours, out footerStart);
			var closed = blocks.Where(b => b.Supported && b.Closed).ToList();
			foreach (var block in closed)
			{
				var probe = block.Polygon[0];
				// Even depth is an outline with material outside it; odd depth is a hole inside a part.
				block.Depth = closed.Count(other => other != block && Clipper.PointInPolygon(probe, other.Polygon) != 0);
			}

			// Fills follow the drawn shape, before kerf moves the outlines.
			List<GrblCommand> fill = options.Fill == FillPattern.None ? null : BuildFill(closed, options, result);

			foreach (var block in closed)
			{
				if (kerfMm > 0)
				{
					double delta = (block.Depth % 2 == 0 ? 1 : -1) * kerfMm / 2 * Scale;
					block.Loop = OffsetPolygon(block.Polygon, delta, block.Points[0]);
					block.KerfApplied = block.Loop != null;
				}
				if (block.Loop == null && (options.LeadInMm > 0 || options.OvercutMm > 0 || options.TabCount > 0))
					block.Loop = block.Points.Take(block.Points.Count - 1).ToList();
			}
			if (kerfMm > 0)
			{
				result.KerfCompensated = blocks.Count(b => b.KerfApplied);
				result.KerfSkipped = blocks.Count(b => b.Points.Count > 1 && !b.KerfApplied);
			}

			bool dropOutlines = fill != null && fill.Count > 0 && options.FillOnly;
			foreach (var block in blocks)
			{
				if (dropOutlines && block.Supported && block.Closed) continue;
				if (block.Loop != null) block.Output = Emit(block, options, result);
			}

			var emitted = blocks.Where(b => !(dropOutlines && b.Supported && b.Closed)).ToList();
			bool reorder = options.Order == CutOrder.InsideFirst;
			GrblCommand penUp = null;
			if (reorder)
			{
				penUp = PenUp(source, blocks);
				var blocker = emitted.FirstOrDefault(b => !b.Reorderable || !b.StartsOff && penUp == null);
				if (blocker != null)
				{
					reorder = false;
					result.Notes.Add("Cut order was left as drawn: a path uses relative moves, Z or inches, doesn't start with its own travel move, or travels with the laser on.");
				}
			}

			var output = new List<GrblCommand>(source.Length + (fill?.Count ?? 0));
			var starts = new List<int>();
			int prefixEnd = contours[0];
			for (int i = 0; i < prefixEnd; i++) output.Add(source[i]);
			var position = blocks.Count > 0 ? PrefixEnd(source, prefixEnd) : new DoublePoint(0, 0);
			if (fill != null && fill.Count > 0)
			{
				starts.Add(output.Count);
				output.AddRange(fill);
				position = LastPoint(fill, position);
			}
			IEnumerable<Block> order = reorder ? Order(emitted, position) : emitted;
			foreach (var block in order)
			{
				starts.Add(output.Count);
				if (reorder && !block.StartsOff) output.Add(penUp);
				if (block.Output != null) output.AddRange(block.Output);
				else if (reorder) AddRaw(block, source, output);
				else for (int i = block.Start; i < block.End; i++) output.Add(source[i]);
			}
			for (int i = footerStart; i < source.Length; i++) output.Add(source[i]);
			result.Reordered = reorder;
			result.Commands = output.ToArray();
			result.Contours = starts.Count > 0 ? starts.ToArray() : new[] { output.Count };
			return result;
		}

		private static List<Block> Parse(GrblCommand[] source, int[] contours, out int footerStart)
		{
			var blocks = new List<Block>();
			var state = new State();
			// The prefix sets modal state and the start position; walk it without collecting points.
			for (int i = 0; i < contours[0]; i++) Track(source, i, state, null);
			for (int b = 0; b < contours.Length; b++)
			{
				var block = new Block { Start = contours[b], End = b + 1 < contours.Length ? contours[b + 1] : source.Length };
				for (int i = block.Start; i < block.End; i++)
					Track(source, i, state, block);
				block.EndPos = new DoublePoint(state.X, state.Y);
				if (block.FirstRapid < 0) block.Reorderable = false;
				blocks.Add(block);
			}

			// The import's closing commands (laser off, end of program) sit in the last block. Split them off
			// after the first laser-off that follows its last burn, so they stay at the end whatever the order.
			footerStart = source.Length;
			var last = blocks[blocks.Count - 1];
			if (last.LastBurn >= 0)
			{
				for (int i = last.LastBurn + 1; i < last.End; i++)
				{
					var command = Helper(source[i]);
					if (IsLaserOff(command)) { footerStart = i + 1; break; }
					if (command.IsMovement) break;
				}
				if (footerStart < last.End)
				{
					int keep = last.Post.Count - (last.End - footerStart);
					if (keep >= 0) last.Post.RemoveRange(keep, last.Post.Count - keep);
					last.End = footerStart;
				}
			}
			else last.Reorderable = false;

			foreach (var block in blocks)
			{
				if (!block.Supported || block.Points.Count < 4) continue;
				var first = block.Points[0];
				var end = block.Points[block.Points.Count - 1];
				block.Closed = Math.Abs(first.X - end.X) <= CloseTolerance && Math.Abs(first.Y - end.Y) <= CloseTolerance;
				if (block.Closed)
				{
					block.Polygon = block.Points.Take(block.Points.Count - 1).Select(p => new IntPoint(p.X * Scale, p.Y * Scale)).ToList();
					if (Math.Abs(Clipper.Area(block.Polygon)) < 1) block.Closed = false; // degenerate, nothing to offset
				}
			}
			return blocks;
		}

		/// <summary>The first plain laser-off line (S0 or M5) in the contours, the way this file lifts the pen.</summary>
		private static GrblCommand PenUp(GrblCommand[] source, List<Block> blocks)
		{
			for (int i = blocks[0].Start; i < blocks[blocks.Count - 1].End; i++)
			{
				var helper = Helper(source[i]);
				if (!helper.IsMovement && IsLaserOff(helper)) return source[i];
			}
			return null;
		}

		private static GrblCommand Helper(GrblCommand original)
		{
			var command = new GrblCommand(original.Command);
			command.BuildHelper();
			return command;
		}

		private static bool IsLaserOff(GrblCommand command)
		{
			return command.S != null ? command.S.Number == 0 : command.IsM5;
		}

		private static void Track(GrblCommand[] source, int index, State state, Block block)
		{
			var original = source[index];
			var command = Helper(original);
			var g = command.G;
			if (g != null)
			{
				if (g.Number == 90) state.Absolute = true;
				else if (g.Number == 91) state.Absolute = false;
				else if (g.Number >= 0 && g.Number <= 3) state.Motion = (int)g.Number;
				else if (g.Number == 20 && block != null) block.Supported = block.Reorderable = false;
			}
			string feedBefore = state.Feed, powerBefore = state.Power;
			if (command.F != null) state.Feed = command.F.ToString();
			if (command.S != null) state.Power = command.S.ToString();
			if (block != null)
			{
				if (command.S != null || command.IsM3 || command.IsM4 || command.IsM5)
				{
					block.LaserOff = IsLaserOff(command);
					if (block.LaserOff && block.FirstRapid < 0 && !block.Burned) block.StartsOff = true;
				}
				if (!state.Absolute || command.Z != null) block.Supported = block.Reorderable = false;
			}

			bool moves = command.IsMovement;
			if (!moves)
			{
				if (block != null) (block.Points.Count == 0 ? block.Pre : block.Post).Add(original);
				return;
			}
			double startX = state.X, startY = state.Y;
			if (state.Absolute)
			{
				if (command.X != null) state.X = (double)command.X.Number;
				if (command.Y != null) state.Y = (double)command.Y.Number;
			}
			else
			{
				if (command.X != null) state.X += (double)command.X.Number;
				if (command.Y != null) state.Y += (double)command.Y.Number;
			}
			if (block == null) return;

			if (state.Motion == 0)
			{
				if (!block.Burned && block.FirstRapid < 0) block.FirstRapid = index;
				if (!block.Burned) block.StartPos = new DoublePoint(state.X, state.Y);
			}
			else
			{
				if (!block.Burned)
				{
					block.Burned = true;
					block.FirstBurn = index;
					block.ModalFeed = feedBefore;
					block.ModalPower = powerBefore;
					if (block.FirstRapid < 0) block.Reorderable = false; // starts wherever the previous path ended
				}
				block.LastBurn = index;
			}

			if (!block.Supported) return;
			if (state.Motion == 0)
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
			if (state.Motion == 1)
				block.Points.Add(new DoublePoint(state.X, state.Y));
			else
				AddArc(block, command, startX, startY, state.X, state.Y, state.Motion == 2);
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

		private static List<DoublePoint> OffsetPolygon(Path polygon, double delta, DoublePoint start)
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
			return path.Skip(nearest).Concat(path.Take(nearest)).Select(p => new DoublePoint(p.X / Scale, p.Y / Scale)).ToList();
		}

		private static List<GrblCommand> Emit(Block block, ToolpathOptions options, Result result)
		{
			var loop = block.Loop;
			var cumulative = Cumulative(loop);
			double perimeter = cumulative[loop.Count];
			string onPower = block.Power ?? block.ModalPower;

			var gaps = new List<double[]>();
			if (options.TabCount > 0 && block.Depth % 2 == 0)
			{
				double width = (double)options.TabWidthMm;
				if (onPower != null && perimeter >= options.TabCount * width * 3)
				{
					for (int k = 0; k < options.TabCount; k++)
					{
						double centre = (k + 0.5) * perimeter / options.TabCount;
						gaps.Add(new[] { centre - width / 2, centre + width / 2 });
					}
					result.Tabbed++;
				}
			}

			var moves = new List<Move>();
			DoublePoint? lead = null;
			if (options.LeadInMm > 0)
			{
				lead = LeadPoint(block, loop, (double)options.LeadInMm);
				if (lead.HasValue) { moves.Add(new Move(loop[0], true)); result.LeadIns++; }
				else result.LeadSkipped++;
			}
			moves.AddRange(Walk(loop, cumulative, 0, perimeter, gaps));
			if (options.OvercutMm > 0)
			{
				double overcut = Math.Min((double)options.OvercutMm, gaps.Count > 0 ? gaps[0][0] : perimeter);
				if (overcut > 0) moves.AddRange(Walk(loop, cumulative, 0, overcut, gaps).Select(m => new Move(m.P, true)));
			}

			var output = new List<GrblCommand>();
			string start = Point(lead ?? loop[0]);
			if (block.RapidIndex < 0) output.Add(new GrblCommand("G0 " + start));
			for (int i = 0; i < block.Pre.Count; i++)
				output.Add(i == block.RapidIndex ? new GrblCommand("G0 " + start) : block.Pre[i]);
			string feed = block.Feed ?? block.ModalFeed;
			string words = (feed != null ? " " + feed : "") + (block.Power != null ? " " + block.Power : "");
			bool on = true, first = true;
			foreach (var move in moves)
			{
				string extra = first ? words : "";
				first = false;
				if (move.On != on)
				{
					extra += move.On ? " " + onPower : " S0";
					on = move.On;
				}
				output.Add(new GrblCommand("G1 " + Point(move.P) + extra));
			}
			if (!on && onPower != null) output.Add(new GrblCommand(onPower));
			output.AddRange(block.Post);
			return output;
		}

		private static double[] Cumulative(List<DoublePoint> loop)
		{
			var cumulative = new double[loop.Count + 1];
			for (int i = 0; i < loop.Count; i++)
				cumulative[i + 1] = cumulative[i] + Distance(loop[i], loop[(i + 1) % loop.Count]);
			return cumulative;
		}

		/// <summary>Moves along the closed loop from distance <paramref name="from"/> to <paramref name="to"/>, splitting at tab edges.</summary>
		private static IEnumerable<Move> Walk(List<DoublePoint> loop, double[] cumulative, double from, double to, List<double[]> gaps)
		{
			var stops = new SortedSet<double>();
			for (int i = 1; i <= loop.Count; i++) if (cumulative[i] > from && cumulative[i] < to) stops.Add(cumulative[i]);
			foreach (var gap in gaps)
			{
				if (gap[0] > from && gap[0] < to) stops.Add(gap[0]);
				if (gap[1] > from && gap[1] < to) stops.Add(gap[1]);
			}
			stops.Add(to);
			double previous = from;
			foreach (double stop in stops)
			{
				double middle = (previous + stop) / 2;
				bool on = !gaps.Any(gap => middle > gap[0] && middle < gap[1]);
				yield return new Move(PointAt(loop, cumulative, stop), on);
				previous = stop;
			}
		}

		private static DoublePoint PointAt(List<DoublePoint> loop, double[] cumulative, double distance)
		{
			double perimeter = cumulative[loop.Count];
			if (distance >= perimeter - 1e-9) return loop[0];
			int segment = Array.BinarySearch(cumulative, distance);
			if (segment >= 0) return loop[segment % loop.Count];
			segment = ~segment - 1;
			var a = loop[segment];
			var b = loop[(segment + 1) % loop.Count];
			double length = cumulative[segment + 1] - cumulative[segment];
			double t = length <= 0 ? 0 : (distance - cumulative[segment]) / length;
			return new DoublePoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
		}

		/// <summary>A point on the waste side of the contour start: outside an outline, inside a hole.</summary>
		private static DoublePoint? LeadPoint(Block block, List<DoublePoint> loop, double length)
		{
			var s = loop[0];
			var next = loop.Skip(1).FirstOrDefault(p => Distance(p, s) > 1e-6);
			var previous = Enumerable.Reverse(loop).FirstOrDefault(p => Distance(p, s) > 1e-6);
			double d = Distance(next, s), e = Distance(previous, s);
			if (d <= 1e-6 || e <= 1e-6) return null;
			double dx = (next.X - s.X) / d, dy = (next.Y - s.Y) / d;
			double ix = (s.X - previous.X) / e, iy = (s.Y - previous.Y) / e;
			var polygon = loop.Select(p => new IntPoint(p.X * Scale, p.Y * Scale)).ToList();
			bool wantInside = block.Depth % 2 == 1;
			// At a corner the normal of one edge runs along the other, so try the corner's bisector first.
			var candidates = new List<DoublePoint>();
			foreach (int side in new[] { 1, -1 })
			{
				double bx = side * (-dy - iy), by = side * (dx + ix), b = Math.Sqrt(bx * bx + by * by);
				if (b > 1e-6) candidates.Add(new DoublePoint(bx / b, by / b));
			}
			candidates.Add(new DoublePoint(-dy, dx));
			candidates.Add(new DoublePoint(dy, -dx));
			foreach (var normal in candidates)
			{
				bool ok = true;
				foreach (double fraction in new[] { 0.25, 0.5, 1.0 })
				{
					var probe = new IntPoint((s.X + normal.X * length * fraction) * Scale, (s.Y + normal.Y * length * fraction) * Scale);
					int inside = Clipper.PointInPolygon(probe, polygon);
					if (inside == -1 || (inside == 1) != wantInside) { ok = false; break; }
				}
				if (ok) return new DoublePoint(s.X + normal.X * length, s.Y + normal.Y * length);
			}
			return null; // a sharp corner or a hole narrower than the lead-in
		}

		private static List<GrblCommand> BuildFill(List<Block> closed, ToolpathOptions options, Result result)
		{
			var reference = closed.FirstOrDefault(b => (b.Power ?? b.ModalPower) != null);
			if (reference == null)
			{
				if (closed.Count == 0) result.Notes.Add("Fill needs closed shapes; this operation has none.");
				else result.Notes.Add("Fill was skipped: the outlines don't set a laser power.");
				return null;
			}
			double spacing = (double)options.FillSpacingMm;
			var polygons = closed.Select(b => b.Points.Take(b.Points.Count - 1).ToList()).ToList();
			var paths = new List<List<DoublePoint>>();
			if (options.Fill == FillPattern.Offset) paths.AddRange(OffsetFill(closed, spacing));
			else
			{
				double angle = (double)options.FillAngle;
				paths.AddRange(Hatch(polygons, spacing, angle));
				if (options.Fill == FillPattern.CrossHatch) paths.AddRange(Hatch(polygons, spacing, angle + 90));
			}
			if (paths.Count == 0) { result.Notes.Add("Fill found nothing to fill at this spacing."); return null; }
			result.FillPaths = paths.Count;

			string power = reference.Power ?? reference.ModalPower;
			string feed = reference.Feed ?? reference.ModalFeed;
			// Without PWM the laser is switched with M3/M4 rather than S, so turn it on the way the outlines do.
			string laserOn = reference.Pre.Skip(reference.RapidIndex + 1).Select(Helper).Where(c => c.IsM3 || c.IsM4).Select(c => c.IsM4 ? "M4" : "M3").FirstOrDefault();
			var penUp = reference.Post.Select(c => new { Original = c, Helper = Helper(c) }).FirstOrDefault(c => IsLaserOff(c.Helper));

			var output = new List<GrblCommand>();
			bool first = true;
			foreach (var path in paths)
			{
				output.Add(new GrblCommand("G0 " + Point(path[0]) + " S0"));
				if (first && laserOn != null) output.Add(new GrblCommand(laserOn));
				for (int i = 1; i < path.Count; i++)
					output.Add(new GrblCommand("G1 " + Point(path[i]) + (i == 1 ? " " + power + (first && feed != null ? " " + feed : "") : "")));
				first = false;
			}
			output.Add(new GrblCommand("S0"));
			if (penUp != null && penUp.Helper.S == null) output.Add(penUp.Original);
			return output;
		}

		/// <summary>Even-odd scanlines at <paramref name="angleDegrees"/>, alternating direction so travel stays short.</summary>
		private static IEnumerable<List<DoublePoint>> Hatch(List<List<DoublePoint>> polygons, double spacing, double angleDegrees)
		{
			double a = angleDegrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
			// Rotate so the hatch lines are horizontal, scan, then rotate the segments back.
			var rotated = polygons.Select(poly => poly.Select(p => new DoublePoint(p.X * cos + p.Y * sin, -p.X * sin + p.Y * cos)).ToList()).ToList();
			double minY = rotated.SelectMany(p => p).Min(p => p.Y), maxY = rotated.SelectMany(p => p).Max(p => p.Y);
			if ((maxY - minY) / spacing > MaxFillSegments) throw new ArgumentException("Fill spacing is too fine for a shape this size.");
			var lines = new List<List<DoublePoint>>();
			int row = 0, count = 0;
			for (double y = minY + spacing / 2; y < maxY; y += spacing, row++)
			{
				var xs = new List<double>();
				foreach (var poly in rotated)
					for (int i = 0; i < poly.Count; i++)
					{
						var p = poly[i];
						var q = poly[(i + 1) % poly.Count];
						if ((p.Y <= y && q.Y > y) || (q.Y <= y && p.Y > y))
							xs.Add(p.X + (y - p.Y) * (q.X - p.X) / (q.Y - p.Y));
					}
				xs.Sort();
				var segments = new List<List<DoublePoint>>();
				for (int i = 0; i + 1 < xs.Count; i += 2)
				{
					if (xs[i + 1] - xs[i] < 1e-6) continue;
					segments.Add(new List<DoublePoint> { Unrotate(xs[i], y, cos, sin), Unrotate(xs[i + 1], y, cos, sin) });
				}
				if (row % 2 == 1)
				{
					segments.Reverse();
					foreach (var segment in segments) segment.Reverse();
				}
				count += segments.Count;
				if (count > MaxFillSegments) throw new ArgumentException("Fill spacing is too fine for a shape this size.");
				lines.AddRange(segments);
			}
			return lines;
		}

		private static DoublePoint Unrotate(double x, double y, double cos, double sin)
		{
			return new DoublePoint(x * cos - y * sin, x * sin + y * cos);
		}

		/// <summary>Concentric loops inset by the spacing until the shape is used up.</summary>
		private static IEnumerable<List<DoublePoint>> OffsetFill(List<Block> closed, double spacing)
		{
			var region = Clipper.SimplifyPolygons(closed.Select(b => b.Polygon).ToList(), PolyFillType.pftEvenOdd);
			var loops = new List<List<DoublePoint>>();
			for (int k = 0; k < 100000; k++)
			{
				var offset = new ClipperOffset(2, ArcToleranceMm * Scale);
				offset.AddPaths(region, JoinType.jtRound, EndType.etClosedPolygon);
				var solution = new Paths();
				offset.Execute(ref solution, -(spacing / 2 + k * spacing) * Scale);
				if (solution.Count == 0) break;
				foreach (var path in solution)
				{
					var points = path.Select(p => new DoublePoint(p.X / Scale, p.Y / Scale)).ToList();
					points.Add(points[0]);
					loops.Add(points);
				}
				if (loops.Sum(l => l.Count) > MaxFillSegments) throw new ArgumentException("Fill spacing is too fine for a shape this size.");
			}
			return loops;
		}

		/// <summary>
		/// Nearest next path, but never cut a closed shape before everything inside it,
		/// so holes and details are done while the part is still held by the sheet.
		/// </summary>
		private static List<Block> Order(List<Block> blocks, DoublePoint position)
		{
			var containers = blocks.Where(b => b.Supported && b.Closed).ToList();
			var waitingOn = new Dictionary<Block, int>();
			var inside = new Dictionary<Block, List<Block>>();
			foreach (var block in blocks)
			{
				var probe = block.Supported && block.Closed ? block.Polygon[0] : new IntPoint(block.StartPos.X * Scale, block.StartPos.Y * Scale);
				var holders = containers.Where(c => c != block && Clipper.PointInPolygon(probe, c.Polygon) != 0).ToList();
				inside[block] = holders;
				foreach (var holder in holders)
				{
					int count;
					waitingOn.TryGetValue(holder, out count);
					waitingOn[holder] = count + 1;
				}
			}
			var remaining = new List<Block>(blocks);
			var ordered = new List<Block>(blocks.Count);
			while (remaining.Count > 0)
			{
				Block best = null;
				double bestDistance = double.MaxValue;
				foreach (var block in remaining)
				{
					int count;
					if (waitingOn.TryGetValue(block, out count) && count > 0) continue;
					double d = Distance(position, Start(block));
					if (d < bestDistance) { bestDistance = d; best = block; }
				}
				if (best == null) best = remaining[0]; // shapes that contain each other; keep drawn order
				remaining.Remove(best);
				ordered.Add(best);
				foreach (var holder in inside[best]) waitingOn[holder]--;
				position = best.Output != null ? LastPoint(best.Output, best.EndPos) : best.EndPos;
			}
			return ordered;
		}

		private static DoublePoint Start(Block block)
		{
			if (block.Output != null)
			{
				var rapid = block.Output.Select(Helper).FirstOrDefault(c => c.IsMovement);
				if (rapid != null && rapid.X != null && rapid.Y != null) return new DoublePoint((double)rapid.X.Number, (double)rapid.Y.Number);
			}
			return block.StartPos;
		}

		private static void AddRaw(Block block, GrblCommand[] source, List<GrblCommand> output)
		{
			for (int i = block.Start; i < block.End; i++)
			{
				if (i == block.FirstRapid)
				{
					// The original rapid may leave out an axis that didn't change; spell both out so any order works.
					var helper = Helper(source[i]);
					output.Add(new GrblCommand("G0 " + Point(block.StartPos) + (helper.S != null ? " " + helper.S : "")));
				}
				else if (i == block.FirstBurn && block.Feed == null && block.ModalFeed != null)
					output.Add(new GrblCommand(WithWord(source[i].Command, block.ModalFeed)));
				else output.Add(source[i]);
			}
		}

		private static string WithWord(string line, string word)
		{
			int comment = line.IndexOf(';');
			return comment < 0 ? line.TrimEnd() + " " + word : line.Substring(0, comment).TrimEnd() + " " + word + " " + line.Substring(comment);
		}

		private static DoublePoint PrefixEnd(GrblCommand[] source, int end)
		{
			var state = new State();
			for (int i = 0; i < end; i++) Track(source, i, state, null);
			return new DoublePoint(state.X, state.Y);
		}

		private static DoublePoint LastPoint(List<GrblCommand> commands, DoublePoint fallback)
		{
			for (int i = commands.Count - 1; i >= 0; i--)
			{
				var helper = Helper(commands[i]);
				if (helper.IsMovement && helper.X != null && helper.Y != null) return new DoublePoint((double)helper.X.Number, (double)helper.Y.Number);
			}
			return fallback;
		}

		private static double Distance(DoublePoint a, DoublePoint b)
		{
			double dx = a.X - b.X, dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		private static string Point(DoublePoint point)
		{
			return string.Format(CultureInfo.InvariantCulture, "X{0:0.###} Y{1:0.###}", point.X, point.Y);
		}
	}
}
