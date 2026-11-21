using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LaserGRBL
{
	/// <summary>One straight move of the toolpath in absolute millimetres, with the power and feed it runs at.</summary>
	internal struct ToolpathSegment
	{
		internal double X1, Y1, X2, Y2;
		internal bool Burn;
		internal double Power, Feed;
		internal double Length { get { double dx = X2 - X1, dy = Y2 - Y1; return Math.Sqrt(dx * dx + dy * dy); } }
	}

	/// <summary>Walks G-code into straight segments. Arcs are split into short lines; Z is ignored.</summary>
	internal static class ToolpathWalker
	{
		private const double ArcStepMm = 0.25;

		internal static IEnumerable<ToolpathSegment> Segments(IEnumerable<GrblCommand> commands)
		{
			double x = 0, y = 0, power = 0, feed = 0;
			int motion = 0;
			bool absolute = true, metric = true;
			bool? laser = null; // files that never switch the laser rely on the header, so treat them as on
			foreach (var original in commands)
			{
				var command = new GrblCommand(original.Command);
				command.BuildHelper();
				var g = command.G;
				if (g != null)
				{
					if (g.Number == 90) absolute = true;
					else if (g.Number == 91) absolute = false;
					else if (g.Number == 20) metric = false;
					else if (g.Number == 21) metric = true;
					else if (g.Number >= 0 && g.Number <= 3) motion = (int)g.Number;
				}
				double unit = metric ? 1 : 25.4;
				if (command.S != null) power = (double)command.S.Number;
				if (command.F != null) feed = (double)command.F.Number * unit;
				if (command.IsM3 || command.IsM4) laser = true;
				else if (command.IsM5) laser = false;
				if (!command.IsMovement) continue;

				double nx = x, ny = y;
				if (command.X != null) nx = absolute ? (double)command.X.Number * unit : x + (double)command.X.Number * unit;
				if (command.Y != null) ny = absolute ? (double)command.Y.Number * unit : y + (double)command.Y.Number * unit;
				bool burn = motion != 0 && (laser ?? true) && power > 0;
				if (motion == 2 || motion == 3)
				{
					foreach (var point in Arc(command, x, y, nx, ny, motion == 2, unit))
					{
						yield return new ToolpathSegment { X1 = x, Y1 = y, X2 = point[0], Y2 = point[1], Burn = burn, Power = power, Feed = feed };
						x = point[0];
						y = point[1];
					}
				}
				else if (nx != x || ny != y)
					yield return new ToolpathSegment { X1 = x, Y1 = y, X2 = nx, Y2 = ny, Burn = burn, Power = power, Feed = motion == 0 ? 0 : feed };
				x = nx;
				y = ny;
			}
		}

		/// <summary>Points along an arc, ending exactly at the end point.</summary>
		internal static List<double[]> Arc(GrblCommand command, double sx, double sy, double ex, double ey, bool clockwise, double unit = 1)
		{
			var points = new List<double[]>();
			double cx, cy;
			if (command.R != null)
			{
				double r = (double)command.R.Number * unit;
				double dx = ex - sx, dy = ey - sy, chord = Math.Sqrt(dx * dx + dy * dy);
				if (chord < 1e-9 || chord > 2 * Math.Abs(r) + 1e-6) { points.Add(new[] { ex, ey }); return points; }
				double h = Math.Sqrt(Math.Max(0, r * r - chord * chord / 4));
				double sign = (clockwise ? -1 : 1) * (r < 0 ? -1 : 1);
				cx = (sx + ex) / 2 - sign * h * dy / chord;
				cy = (sy + ey) / 2 + sign * h * dx / chord;
			}
			else
			{
				cx = sx + (double)(command.I?.Number ?? 0) * unit;
				cy = sy + (double)(command.J?.Number ?? 0) * unit;
			}
			double radius = Math.Sqrt((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy));
			if (radius < 1e-6) { points.Add(new[] { ex, ey }); return points; }
			double a0 = Math.Atan2(sy - cy, sx - cx), a1 = Math.Atan2(ey - cy, ex - cx);
			double sweep = a1 - a0;
			if (clockwise) { if (sweep >= -1e-9) sweep -= 2 * Math.PI; }
			else if (sweep <= 1e-9) sweep += 2 * Math.PI;
			int steps = Math.Max(4, Math.Min(2000, (int)Math.Ceiling(Math.Abs(sweep) * radius / ArcStepMm)));
			for (int i = 1; i < steps; i++)
			{
				double a = a0 + sweep * i / steps;
				points.Add(new[] { cx + radius * Math.Cos(a), cy + radius * Math.Sin(a) });
			}
			points.Add(new[] { ex, ey });
			return points;
		}
	}

	/// <summary>Totals for one job: how far it burns and travels, how long that takes, and where it burns.</summary>
	internal sealed class JobStats
	{
		internal int Lines;
		internal double BurnMm, TravelMm;
		internal TimeSpan BurnTime, TravelTime;
		internal double MaxPower;
		internal bool HasBounds;
		internal double MinX, MinY, MaxX, MaxY;

		internal TimeSpan Total { get { return BurnTime + TravelTime; } }
		internal double Width { get { return HasBounds ? MaxX - MinX : 0; } }
		internal double Height { get { return HasBounds ? MaxY - MinY : 0; } }

		/// <param name="rapidFeed">Travel speed for G0 moves in mm/min, usually the machine's $110/$111.</param>
		internal static JobStats Measure(IList<GrblCommand> commands, double rapidFeed)
		{
			if (rapidFeed <= 0) throw new ArgumentOutOfRangeException("rapidFeed");
			var stats = new JobStats { Lines = commands.Count, MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue };
			double burnMinutes = 0, travelMinutes = 0;
			foreach (var s in ToolpathWalker.Segments(commands))
			{
				double length = s.Length;
				if (s.Burn)
				{
					stats.BurnMm += length;
					burnMinutes += s.Feed > 0 ? length / s.Feed : 0;
					stats.MaxPower = Math.Max(stats.MaxPower, s.Power);
					stats.HasBounds = true;
					stats.MinX = Math.Min(stats.MinX, Math.Min(s.X1, s.X2)); stats.MaxX = Math.Max(stats.MaxX, Math.Max(s.X1, s.X2));
					stats.MinY = Math.Min(stats.MinY, Math.Min(s.Y1, s.Y2)); stats.MaxY = Math.Max(stats.MaxY, Math.Max(s.Y1, s.Y2));
				}
				else
				{
					stats.TravelMm += length;
					travelMinutes += length / (s.Feed > 0 ? s.Feed : rapidFeed);
				}
			}
			if (!stats.HasBounds) stats.MinX = stats.MinY = stats.MaxX = stats.MaxY = 0;
			stats.BurnTime = TimeSpan.FromMinutes(burnMinutes);
			stats.TravelTime = TimeSpan.FromMinutes(travelMinutes);
			return stats;
		}
	}

	/// <summary>
	/// Laser energy per area: power × time spent over each cell. Overlapping passes and slow corners show up hot,
	/// which is where material scorches or burns through.
	/// </summary>
	internal sealed class EnergyMap
	{
		internal readonly double[,] Cells;
		internal readonly double OriginX, OriginY, CellMm;
		internal double Max { get; private set; }
		internal int Columns { get { return Cells.GetLength(0); } }
		internal int Rows { get { return Cells.GetLength(1); } }

		private EnergyMap(int columns, int rows, double originX, double originY, double cellMm)
		{
			Cells = new double[columns, rows];
			OriginX = originX;
			OriginY = originY;
			CellMm = cellMm;
		}

		/// <summary>Builds the map; cells grow so neither side exceeds <paramref name="maxCells"/>.</summary>
		internal static EnergyMap Build(IList<GrblCommand> commands, double cellMm, int maxCells = 400)
		{
			if (cellMm <= 0) throw new ArgumentOutOfRangeException("cellMm");
			var burns = ToolpathWalker.Segments(commands).Where(s => s.Burn && s.Feed > 0).ToList();
			if (burns.Count == 0) throw new InvalidOperationException("This job doesn't burn anything.");
			double minX = burns.Min(s => Math.Min(s.X1, s.X2)), maxX = burns.Max(s => Math.Max(s.X1, s.X2));
			double minY = burns.Min(s => Math.Min(s.Y1, s.Y2)), maxY = burns.Max(s => Math.Max(s.Y1, s.Y2));
			cellMm = Math.Max(cellMm, Math.Max(maxX - minX, maxY - minY) / maxCells);
			int columns = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cellMm + 1e-9));
			int rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) / cellMm + 1e-9));
			var map = new EnergyMap(columns, rows, minX, minY, cellMm);
			foreach (var s in burns)
			{
				double length = s.Length;
				int steps = Math.Max(1, (int)Math.Ceiling(length / (cellMm / 2)));
				double energy = s.Power * (length / s.Feed) / steps; // S·minutes, shared over the samples
				for (int i = 0; i < steps; i++)
				{
					double t = (i + 0.5) / steps;
					int cx = Math.Min(columns - 1, (int)((s.X1 + (s.X2 - s.X1) * t - minX) / cellMm));
					int cy = Math.Min(rows - 1, (int)((s.Y1 + (s.Y2 - s.Y1) * t - minY) / cellMm));
					map.Cells[cx, cy] += energy;
				}
			}
			double max = 0;
			foreach (var value in map.Cells) max = Math.Max(max, value);
			map.Max = max;
			return map;
		}
	}

	/// <summary>Rates used to price a job. Saved between sessions.</summary>
	[Serializable]
	internal sealed class CostRates
	{
		internal decimal MachinePerHour = 10;
		internal decimal LaserWatts = 40;
		internal decimal EnergyPerKwh = 0.3m;
		internal decimal MaterialPerM2 = 20;
		internal decimal MarginMm = 5;
		internal decimal SetupCost;
		internal int Quantity = 1;
		internal string Currency = "";

		internal const string SettingsKey = "Cost.Rates";

		internal static CostRates Load() { return Settings.GetObject<CostRates>(SettingsKey, null) ?? new CostRates(); }
		internal void Save() { Settings.SetObject(SettingsKey, this, true); }
	}

	internal sealed class CostEstimate
	{
		internal decimal Machine, Energy, Material, Setup;
		internal decimal Total { get { return Machine + Energy + Material + Setup; } }
		internal decimal PerPiece;
		internal TimeSpan Time;
		internal double AreaM2;

		/// <param name="jobTime">Estimated run time for one piece.</param>
		/// <param name="laserOnTime">Time the laser is burning, for energy.</param>
		internal static CostEstimate For(TimeSpan jobTime, TimeSpan laserOnTime, double widthMm, double heightMm, CostRates rates)
		{
			if (rates.Quantity < 1) throw new ArgumentOutOfRangeException("Quantity", "Quantity must be at least 1.");
			if (rates.MachinePerHour < 0 || rates.LaserWatts < 0 || rates.EnergyPerKwh < 0 || rates.MaterialPerM2 < 0 || rates.MarginMm < 0 || rates.SetupCost < 0)
				throw new ArgumentOutOfRangeException("rates", "Rates can't be negative.");
			int n = rates.Quantity;
			double margin = (double)rates.MarginMm * 2;
			double area = (widthMm + margin) * (heightMm + margin) / 1e6;
			var estimate = new CostEstimate
			{
				Time = TimeSpan.FromTicks(jobTime.Ticks * n),
				AreaM2 = area * n,
				Machine = (decimal)jobTime.TotalHours * rates.MachinePerHour * n,
				Energy = (decimal)laserOnTime.TotalHours * rates.LaserWatts / 1000 * rates.EnergyPerKwh * n,
				Material = (decimal)area * rates.MaterialPerM2 * n,
				Setup = rates.SetupCost
			};
			estimate.PerPiece = estimate.Total / n;
			return estimate;
		}

		internal string Format(string currency)
		{
			var c = CultureInfo.CurrentCulture;
			Func<decimal, string> m = v => (currency ?? "") + v.ToString("0.00", c);
			return string.Join(Environment.NewLine, new[]
			{
				string.Format(c, "Run time: {0:0}:{1:00}:{2:00}", Math.Floor(Time.TotalHours), Time.Minutes, Time.Seconds),
				"Machine time: " + m(Machine),
				"Laser energy: " + m(Energy),
				string.Format(c, "Material ({0:0.####} m²): {1}", AreaM2, m(Material)),
				"Setup: " + m(Setup),
				"Total: " + m(Total),
				"Per piece: " + m(PerPiece)
			});
		}
	}
}
