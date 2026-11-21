using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LaserGRBL
{
	/// <summary>Where one item lands on the sheet: the centre of its box and whether it is turned 90°.</summary>
	internal struct NestPlacement
	{
		internal int Item;
		internal double CenterX, CenterY;
		internal bool Rotated;
	}

	/// <summary>
	/// Packs rectangular boxes onto a sheet in rows (shelf packing), tallest first, turning an item 90° when that
	/// makes it fit or keeps its row lower. It works on bounding boxes, so irregular shapes keep their box apart.
	/// </summary>
	internal static class Nesting
	{
		internal static List<NestPlacement> Pack(IList<SizeF> items, double sheetWidth, double sheetHeight, double gap, double margin, bool allowRotate)
		{
			if (sheetWidth <= 0 || sheetHeight <= 0) throw new ArgumentOutOfRangeException("sheet", "Enter the sheet size.");
			if (gap < 0 || margin < 0) throw new ArgumentOutOfRangeException("gap", "Gap and margin can't be negative.");
			double usableW = sheetWidth - 2 * margin, usableH = sheetHeight - 2 * margin;
			var order = Enumerable.Range(0, items.Count)
				.OrderByDescending(i => Math.Max(items[i].Width, items[i].Height)).ThenByDescending(i => items[i].Width * items[i].Height).ToList();
			var result = new List<NestPlacement>();
			double x = 0, y = 0, rowHeight = 0;
			foreach (int i in order)
			{
				double w = items[i].Width, h = items[i].Height;
				bool rotated = false;
				// Lay items flat (wide side along the row) so rows stay low, unless only the other way fits.
				if (allowRotate && h > w && h <= usableW && w <= usableH) { rotated = true; Swap(ref w, ref h); }
				if (w > usableW || h > usableH)
				{
					if (allowRotate && h <= usableW && w <= usableH) { rotated = !rotated; Swap(ref w, ref h); }
					else throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
						"Item {0} ({1:0.#} × {2:0.#} mm) is bigger than the sheet.", i + 1, items[i].Width, items[i].Height));
				}
				if (x > 0 && x + w > usableW + 1e-9)
				{
					x = 0;
					y += rowHeight + gap;
					rowHeight = 0;
				}
				if (y + h > usableH + 1e-9)
					throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
						"Only {0} of {1} items fit on a {2:0.#} × {3:0.#} mm sheet.", result.Count, items.Count, sheetWidth, sheetHeight));
				result.Add(new NestPlacement { Item = i, CenterX = margin + x + w / 2, CenterY = margin + y + h / 2, Rotated = rotated });
				x += w + gap;
				rowHeight = Math.Max(rowHeight, h);
			}
			return result.OrderBy(p => p.Item).ToList();
		}

		private static void Swap(ref double a, ref double b) { double t = a; a = b; b = t; }

		/// <summary>The box around everything an operation moves to while burning, after its current scale.</summary>
		internal static RectangleF Bounds(JobOperation operation)
		{
			var source = operation.Source;
			var stats = JobStats.Measure(source, 1000);
			if (!stats.HasBounds) throw new InvalidOperationException(operation.Name + " doesn't burn anything to place.");
			return new RectangleF((float)stats.MinX, (float)stats.MinY, (float)stats.Width, (float)stats.Height);
		}

		/// <summary>Placement that moves the box centre of <paramref name="bounds"/> to the nest position, keeping its scale.</summary>
		internal static JobPlacement PlacementFor(RectangleF bounds, NestPlacement nest, double scale)
		{
			var anchor = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
			return new JobPlacement(anchor, new PointF((float)nest.CenterX, (float)nest.CenterY), scale, nest.Rotated ? 90 : 0);
		}
	}

	internal enum RotaryKind { Roller, Chuck }

	/// <summary>
	/// Rescales the Y axis for a rotary attachment wired in place of Y. A roller turns the object by friction, so the
	/// scale depends only on the roller; a chuck holds the object, so it depends on the object's diameter.
	/// </summary>
	internal static class Rotary
	{
		/// <param name="mmPerRevolution">The Y distance GRBL thinks it moved when the roller or chuck turns exactly once.</param>
		internal static double Scale(RotaryKind kind, double mmPerRevolution, double rollerDiameter, double objectDiameter)
		{
			if (mmPerRevolution <= 0) throw new ArgumentOutOfRangeException("mmPerRevolution", "Enter the Y travel for one full turn.");
			double diameter = kind == RotaryKind.Roller ? rollerDiameter : objectDiameter;
			if (diameter <= 0) throw new ArgumentOutOfRangeException("diameter", kind == RotaryKind.Roller ? "Enter the roller diameter." : "Enter the object's diameter.");
			// One turn moves the surface π·d; GRBL needs mmPerRevolution of Y for that.
			return mmPerRevolution / (Math.PI * diameter);
		}

		/// <summary>Multiplies every Y move by <paramref name="scale"/>. Arcs become short lines, since a stretched arc is an ellipse.</summary>
		internal static List<GrblCommand> Apply(IEnumerable<GrblCommand> commands, double scale)
		{
			if (scale <= 0 || scale > 100) throw new ArgumentOutOfRangeException("scale", "The rotary scale must be between 0 and 100.");
			var output = new List<GrblCommand>();
			double x = 0, y = 0;
			int motion = 0;
			bool absolute = true;
			foreach (var original in commands)
			{
				var command = new GrblCommand(original.Command);
				command.BuildHelper();
				var g = command.G;
				if (g != null)
				{
					if (g.Number == 90) absolute = true;
					else if (g.Number == 91) absolute = false;
					else if (g.Number == 20) throw new InvalidOperationException("Rotary scaling needs millimetre G-code (G21).");
					else if (g.Number >= 0 && g.Number <= 3) motion = (int)g.Number;
				}
				if (!command.IsMovement) { output.Add(original); continue; }
				double nx = command.X == null ? x : absolute ? (double)command.X.Number : x + (double)command.X.Number;
				double ny = command.Y == null ? y : absolute ? (double)command.Y.Number : y + (double)command.Y.Number;
				string extra = Words(command);
				if (motion == 2 || motion == 3)
				{
					double px = x, py = y;
					bool first = true;
					foreach (var point in ToolpathWalker.Arc(command, x, y, nx, ny, motion == 2))
					{
						double ox = absolute ? point[0] : point[0] - px, oy = absolute ? point[1] : point[1] - py;
						output.Add(new GrblCommand("G1 X" + F(ox) + " Y" + F(oy * scale) + (first ? extra : "")));
						first = false;
						px = point[0];
						py = point[1];
					}
				}
				else
				{
					var line = new StringBuilder();
					if (g != null) line.Append("G").Append(g.Number.ToString(CultureInfo.InvariantCulture));
					if (command.X != null) line.Append(" X").Append(F((double)command.X.Number));
					if (command.Y != null) line.Append(" Y").Append(F((double)command.Y.Number * scale));
					if (command.Z != null) line.Append(" Z").Append(F((double)command.Z.Number));
					line.Append(extra);
					output.Add(new GrblCommand(line.ToString().Trim()));
				}
				x = nx;
				y = ny;
			}
			return output;
		}

		private static string Words(GrblCommand command)
		{
			var words = new StringBuilder();
			if (command.M != null) words.Append(" ").Append(command.M);
			if (command.S != null) words.Append(" ").Append(command.S);
			if (command.F != null) words.Append(" ").Append(command.F);
			return words.ToString();
		}

		private static string F(double value) { return value.ToString("0.####", CultureInfo.InvariantCulture); }
	}
}
