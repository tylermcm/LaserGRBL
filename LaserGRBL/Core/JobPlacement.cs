using System;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace LaserGRBL
{
	[Serializable]
	internal sealed class JobPlacement
	{
		internal PointF Anchor { get; private set; }
		internal PointF Position { get; private set; }
		internal double Scale { get; private set; }
		internal double Rotation { get; private set; }

		internal JobPlacement(PointF anchor, PointF position, double scale, double rotation)
		{
			if (scale <= 0 || scale > 10) throw new ArgumentOutOfRangeException("scale");
			Anchor = anchor;
			Position = position;
			Scale = scale;
			Rotation = rotation;
		}

		internal PointF Map(PointF point)
		{
			var xy = Rotate(point.X - Anchor.X, point.Y - Anchor.Y);
			return new PointF(Position.X + xy.X, Position.Y + xy.Y);
		}

		private PointF Rotate(double x, double y)
		{
			double radians = Rotation * Math.PI / 180;
			double c = Math.Cos(radians), s = Math.Sin(radians);
			return new PointF((float)((x * c - y * s) * Scale), (float)((x * s + y * c) * Scale));
		}

		internal GrblCommand[] Transform(GrblCommand[] source)
		{
			var parser = new GrblCommand.StatePositionBuilder();
			var output = new GrblCommand[source.Length];
			for (int i = 0; i < source.Length; i++)
			{
				var command = new GrblCommand(source[i].Command, 0, true);
				command.BuildHelper();
				parser.AnalyzeCommand(command, false);
				if (!command.IsMovement)
				{
					output[i] = source[i];
					continue;
				}
				if (!parser.ABS || !parser.G0 && !parser.G1G2G3 || command.Z != null)
					throw new InvalidOperationException("Placement requires an absolute XY toolpath.");

				var point = Map(new PointF((float)parser.X.Number, (float)parser.Y.Number));
				var line = new StringBuilder();
				line.Append(parser.MotionMode);
				AddWord(line, 'X', point.X);
				AddWord(line, 'Y', point.Y);
				if (parser.G2G3)
				{
					if (command.R != null)
						AddWord(line, 'R', (double)command.R.Number * Scale);
					else
					{
						var center = Rotate((double)(command.I?.Number ?? 0), (double)(command.J?.Number ?? 0));
						AddWord(line, 'I', center.X);
						AddWord(line, 'J', center.Y);
					}
				}
				if (command.M != null) line.Append(" ").Append(command.M);
				if (command.S != null) line.Append(" ").Append(command.S);
				if (command.F != null) line.Append(" ").Append(command.F);
				output[i] = new GrblCommand(line.ToString());
			}
			return output;
		}

		private static void AddWord(StringBuilder line, char letter, double value)
		{
			line.Append(' ').Append(letter).Append(value.ToString("0.####", CultureInfo.InvariantCulture));
		}
	}
}
