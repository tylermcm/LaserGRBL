using System;
using System.Collections.Generic;
using System.Drawing;

namespace LaserGRBL
{
	internal sealed class JobTimeline
	{
		internal struct Move
		{
			internal int CommandIndex;
			internal PointF From;
			internal PointF To;
			internal double Start;
			internal double End;
			internal bool Burning;
			internal bool Waiting;
			internal GrblCommand.G2G3Helper Arc;

			internal PointF At(double fraction)
			{
				if (fraction <= 0) return From;
				if (fraction >= 1) return To;
				if (Arc != null)
				{
					double angle = Arc.StartAngle + fraction * Arc.AngularWidth;
					return new PointF((float)(Arc.CenterX + Arc.Ray * Math.Cos(angle)),
						(float)(Arc.CenterY + Arc.Ray * Math.Sin(angle)));
				}
				return new PointF(From.X + (To.X - From.X) * (float)fraction, From.Y + (To.Y - From.Y) * (float)fraction);
			}
		}

		internal struct Timing
		{
			internal double Engraving;
			internal double Travel;
			internal double Waits;
		}

		internal List<Move> Moves { get; } = new List<Move>();
		internal double Duration { get; private set; }

		internal JobTimeline(GrblFile file, int firstCommand, int lastCommand)
			: this(file.Commands, firstCommand, lastCommand)
		{
		}

		internal JobTimeline(IList<GrblCommand> commands, int firstCommand, int lastCommand)
		{
			var parser = new GrblCommand.StatePositionBuilder();
			double origin = firstCommand == 0 ? 0 : commands[firstCommand - 1].TimeOffset.TotalSeconds;
			for (int index = 0; index < lastCommand; index++)
			{
				var command = (GrblCommand)commands[index].Clone();
				var before = new PointF((float)parser.X.Number, (float)parser.Y.Number);
				parser.AnalyzeCommand(command, false);
				if (index < firstCommand) continue;
				var after = new PointF((float)parser.X.Number, (float)parser.Y.Number);
				command.BuildHelper();
				try
				{
					double start = index == 0 ? 0 : commands[index - 1].TimeOffset.TotalSeconds - origin;
					double end = command.TimeOffset.TotalSeconds - origin;
					bool moving = command.IsMovement && (before != after || parser.G2G3);
					if (!moving && end <= start) continue;
					Moves.Add(new Move
					{
						CommandIndex = index, From = before, To = after,
						Start = start, End = end, Waiting = command.IsPause,
						Burning = moving && parser.LaserBurning,
						Arc = moving && parser.G2G3 && command.IsArcMovement ? parser.GetArcHelper(command) : null
					});
				}
				finally { command.DeleteHelper(); }
			}
			Duration = lastCommand == 0 ? 0 : commands[lastCommand - 1].TimeOffset.TotalSeconds - origin;
		}

		internal Timing Measure()
		{
			var timing = new Timing();
			foreach (var move in Moves)
			{
				double elapsed = Math.Max(0, move.End - move.Start);
				if (move.Waiting) timing.Waits += elapsed;
				else if (move.Burning) timing.Engraving += elapsed;
				else timing.Travel += elapsed;
			}
			return timing;
		}

		internal int Seek(double time)
		{
			int low = 0, high = Moves.Count;
			while (low < high)
			{
				int middle = low + (high - low) / 2;
				if (Moves[middle].End <= time) low = middle + 1;
				else high = middle;
			}
			return low;
		}
	}
}
