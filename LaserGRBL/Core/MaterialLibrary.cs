using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LaserGRBL
{
	/// <summary>
	/// Settings the Material DB schema has no columns for, keyed by the material row's id.
	/// Kept apart so UserMaterials.psh and StandardMaterials.psh stay in the format other LaserGRBL versions read.
	/// </summary>
	[Serializable]
	public sealed class MaterialExtras
	{
		public decimal LinesPerMm { get; private set; }
		public decimal KerfMm { get; private set; }
		public decimal ZStepMm { get; private set; }
		/// <summary>M3 or M4, or null when the row doesn't say.</summary>
		public string LaserMode { get; private set; }

		public MaterialExtras(decimal linesPerMm = 0, decimal kerfMm = 0, decimal zStepMm = 0, string laserMode = null)
		{
			if (linesPerMm < 0 || linesPerMm > 100) throw new ArgumentOutOfRangeException("linesPerMm", "Lines per mm must be between 0 and 100.");
			if (kerfMm < 0 || kerfMm > 5) throw new ArgumentOutOfRangeException("kerfMm", "Kerf must be between 0 and 5 mm.");
			if (zStepMm < 0 || zStepMm > 50) throw new ArgumentOutOfRangeException("zStepMm", "Z step must be between 0 and 50 mm.");
			if (laserMode != null && laserMode != "M3" && laserMode != "M4") throw new ArgumentException("Laser mode must be M3 or M4.", "laserMode");
			LinesPerMm = linesPerMm;
			KerfMm = kerfMm;
			ZStepMm = zStepMm;
			LaserMode = laserMode;
		}

		internal bool IsEmpty { get { return LinesPerMm == 0 && KerfMm == 0 && ZStepMm == 0 && LaserMode == null; } }

		public override string ToString()
		{
			var parts = new List<string>();
			if (LinesPerMm > 0) parts.Add(LinesPerMm.ToString("0.##", CultureInfo.CurrentCulture) + " lines/mm");
			if (KerfMm > 0) parts.Add("kerf " + KerfMm.ToString("0.###", CultureInfo.CurrentCulture) + " mm");
			if (ZStepMm > 0) parts.Add("Z step " + ZStepMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm");
			if (LaserMode != null) parts.Add(LaserMode);
			return string.Join(", ", parts);
		}
	}

	internal static class MaterialExtrasStore
	{
		internal const string SettingsKey = "Materials.Extras";

		internal static MaterialExtras Get(Guid id)
		{
			MaterialExtras extras;
			return Load().TryGetValue(id, out extras) ? extras : new MaterialExtras();
		}

		internal static void Set(Guid id, MaterialExtras extras)
		{
			var all = Load();
			if (extras == null || extras.IsEmpty) all.Remove(id);
			else all[id] = extras;
			Settings.SetObject(SettingsKey, all, true);
		}

		private static Dictionary<Guid, MaterialExtras> Load()
		{
			return new Dictionary<Guid, MaterialExtras>(Settings.GetObject<Dictionary<Guid, MaterialExtras>>(SettingsKey, null) ?? new Dictionary<Guid, MaterialExtras>());
		}
	}

	/// <summary>One material row in machine units: absolute S and F, plus any extras.</summary>
	internal sealed class MaterialSetting
	{
		internal string Name;
		internal int Power;
		internal int Speed;
		internal int Cycles = 1;
		internal MaterialExtras Extras = new MaterialExtras();

		/// <summary>Material DB power is a percentage of the machine's maximum S ($30).</summary>
		internal static MaterialSetting FromRow(PSHelper.MaterialDB.MaterialsRow row, int maxPwm)
		{
			string thickness = row.Thickness == "-" || string.IsNullOrWhiteSpace(row.Thickness) ? "" : " " + row.Thickness;
			return new MaterialSetting
			{
				Name = string.Format("{0}{1} · {2}", row.Material, thickness, row.Action),
				Power = (int)Math.Round(maxPwm * row.Power / 100.0),
				Speed = row.Speed,
				Cycles = row.Cycles,
				Extras = MaterialExtrasStore.Get(row.id)
			};
		}

		internal static int PowerPercent(int power, int maxPwm)
		{
			if (maxPwm < 1) throw new ArgumentOutOfRangeException("maxPwm");
			return Math.Max(1, Math.Min(100, (int)Math.Round(power * 100.0 / maxPwm)));
		}
	}

	/// <summary>Converts an absolute material setting into the percentage settings an existing toolpath uses.</summary>
	internal sealed class MaterialApplication
	{
		internal JobPass Pass { get; private set; }
		internal int Repeats { get; private set; }
		internal decimal KerfMm { get; private set; }
		internal decimal ZStepMm { get; private set; }
		/// <summary>Fill line spacing from the material's lines/mm, or 0 when it has none.</summary>
		internal decimal FillSpacingMm { get; private set; }
		internal string Warning { get; private set; }

		internal static MaterialApplication For(MaterialSetting setting, IEnumerable<GrblCommand> source, bool supportsKerf)
		{
			var stats = ToolpathStats.Measure(source);
			var warnings = new List<string>();
			decimal power = 100, speed = 100;
			if (stats.MaxPower > 0)
			{
				power = Math.Round(setting.Power * 100m / stats.MaxPower, 1);
				if (power > 100)
				{
					warnings.Add(string.Format(CultureInfo.CurrentCulture,
						"This toolpath peaks at S{0}, so S{1} can't be reached by scaling. Power is set to 100%; re-import at S{1} or higher to match.",
						stats.MaxPower, setting.Power));
					power = 100;
				}
			}
			else warnings.Add("This toolpath has no laser-on S values, so power is left at 100%.");
			if (stats.MainFeed > 0)
			{
				speed = Math.Round(setting.Speed * 100m / stats.MainFeed, 1);
				if (speed < 1 || speed > 1000)
				{
					warnings.Add(string.Format(CultureInfo.CurrentCulture, "F{0} is outside 1-1000% of the toolpath's F{1}; speed was limited.", setting.Speed, stats.MainFeed));
					speed = Math.Max(1, Math.Min(1000, speed));
				}
			}
			else warnings.Add("This toolpath has no laser-on F values, so speed is left at 100%.");
			if (setting.Extras.KerfMm > 0 && !supportsKerf)
				warnings.Add("Kerf only applies to imported SVG outlines, so it was skipped.");
			return new MaterialApplication
			{
				Pass = new JobPass(power, speed),
				Repeats = Math.Max(1, Math.Min(99, setting.Cycles)),
				KerfMm = supportsKerf ? setting.Extras.KerfMm : 0,
				ZStepMm = setting.Extras.ZStepMm,
				FillSpacingMm = setting.Extras.LinesPerMm > 0 ? Math.Round(1 / setting.Extras.LinesPerMm, 4) : 0,
				Warning = string.Join(" ", warnings)
			};
		}
	}

	internal struct ToolpathStats
	{
		/// <summary>The highest S used while the laser is on and moving.</summary>
		internal int MaxPower;
		/// <summary>The feed rate used by the most laser-on motion commands.</summary>
		internal int MainFeed;

		internal static ToolpathStats Measure(IEnumerable<GrblCommand> source)
		{
			decimal maxPower = 0, feed = 0, power = 0, motion = 0;
			bool? laser = null; // files that never switch the laser rely on the header, so treat them as on
			var feedUse = new Dictionary<decimal, int>();
			foreach (var original in source)
			{
				var command = new GrblCommand(original.Command);
				command.BuildHelper();
				if (command.S != null) power = command.S.Number;
				if (command.F != null) feed = command.F.Number;
				var g = command.G;
				if (g != null && g.Number >= 0 && g.Number <= 3) motion = g.Number;
				if (command.IsM3 || command.IsM4) laser = true;
				else if (command.IsM5) laser = false;
				if (command.IsMovement && motion != 0 && (laser ?? true) && power > 0 && feed > 0)
				{
					maxPower = Math.Max(maxPower, power);
					int count;
					feedUse.TryGetValue(feed, out count);
					feedUse[feed] = count + 1;
				}
			}
			return new ToolpathStats
			{
				MaxPower = (int)Math.Round(maxPower),
				MainFeed = feedUse.Count == 0 ? 0 : (int)Math.Round(feedUse.OrderByDescending(entry => entry.Value).First().Key)
			};
		}
	}
}
