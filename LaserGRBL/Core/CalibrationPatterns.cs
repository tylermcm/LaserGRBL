using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LaserGRBL
{
	public enum MaterialTestKind { PowerSpeed, Cutting, LineInterval }

	/// <summary>
	/// The values a generated test burned, kept so a chosen cell can be saved as a material preset.
	/// Each array holds the candidates for one setting; a fixed setting has a single entry.
	/// </summary>
	[Serializable]
	public sealed class MaterialTestRecord
	{
		internal const string SettingsKey = "Materials.LastTest";

		public MaterialTestKind Kind { get; private set; }
		public string Title { get; private set; }
		public string LaserMode { get; private set; }
		public int[] Powers { get; private set; }
		public int[] Speeds { get; private set; }
		public int[] Passes { get; private set; }
		public decimal[] LinesPerMm { get; private set; }
		public DateTime Created { get; private set; }

		public MaterialTestRecord(MaterialTestKind kind, string title, string laserMode, int[] powers, int[] speeds, int[] passes, decimal[] linesPerMm)
		{
			if (powers == null || powers.Length == 0) throw new ArgumentException("A test needs at least one power value.", "powers");
			if (speeds == null || speeds.Length == 0) throw new ArgumentException("A test needs at least one speed value.", "speeds");
			if (passes == null || passes.Length == 0) throw new ArgumentException("A test needs at least one pass count.", "passes");
			if (linesPerMm == null || linesPerMm.Length == 0) throw new ArgumentException("A test needs at least one line interval.", "linesPerMm");
			Kind = kind;
			Title = title ?? "";
			LaserMode = laserMode == "M3" ? "M3" : "M4";
			Powers = powers;
			Speeds = speeds;
			Passes = passes;
			LinesPerMm = linesPerMm;
			Created = DateTime.Now;
		}

		internal static MaterialTestRecord Last
		{
			get { return Settings.GetObject<MaterialTestRecord>(SettingsKey, null); }
			set { Settings.SetObject(SettingsKey, value, true); }
		}

		internal static MaterialTestRecord ForPowerSpeed(int speedSteps, int powerSteps, int speedStart, int speedEnd, int powerStart, int powerEnd, double linesPerMm, string title, string mode)
		{
			return new MaterialTestRecord(MaterialTestKind.PowerSpeed, title, mode,
				Steps(powerStart, powerEnd, powerSteps), Steps(speedStart, speedEnd, speedSteps), new[] { 1 }, new[] { (decimal)linesPerMm });
		}

		internal static MaterialTestRecord ForCutting(int speedSteps, int speedStart, int speedEnd, int passStart, int passEnd, int power, string title, string mode)
		{
			return new MaterialTestRecord(MaterialTestKind.Cutting, title, mode,
				new[] { power }, Steps(speedStart, speedEnd, speedSteps), Enumerable.Range(passStart, passEnd - passStart + 1).ToArray(), new[] { 0m });
		}

		/// <summary>Matches the labels GrblFile prints under each grid row and column.</summary>
		internal static int[] Steps(int start, int end, int count)
		{
			double delta = count > 1 ? (end - start) / (double)(count - 1) : 0;
			return Enumerable.Range(0, Math.Max(1, count)).Select(i => (int)(start + i * delta)).ToArray();
		}

		/// <summary>
		/// Adds the chosen cell to the Material DB, replacing a row with the same model, material, action and thickness.
		/// The DB stores power as a percentage, so the exact S tested is also kept in the remarks.
		/// </summary>
		internal PSHelper.MaterialDB.MaterialsRow AddTo(PSHelper.MaterialDB db, string model, string material, string thickness, string action,
			int power, int speed, int passes, decimal linesPerMm, decimal kerfMm, string notes, int maxPwm)
		{
			if (!Powers.Contains(power) || !Speeds.Contains(speed) || !Passes.Contains(passes) || !LinesPerMm.Contains(linesPerMm))
				throw new ArgumentException("Choose a cell from the burned test.");
			if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Enter the laser model this test was burned on.");
			if (string.IsNullOrWhiteSpace(material)) throw new ArgumentException("Enter a material name.");
			if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Choose an action, such as Engrave or Cut.");
			if (speed < 1 || speed > 100000) throw new ArgumentException("Material DB speeds must be 1 to 100000 mm/min.");
			model = model.Trim(); material = material.Trim(); action = action.Trim();
			thickness = string.IsNullOrWhiteSpace(thickness) ? "-" : thickness.Trim();
			var extras = new MaterialExtras(linesPerMm, kerfMm, 0, LaserMode);
			string remarks = string.Format(CultureInfo.InvariantCulture, "{0} {1:yyyy-MM-dd}: S{2} F{3}{4}, {5}.{6}",
				this, Created, power, speed, passes > 1 ? " x" + passes : "", extras,
				string.IsNullOrWhiteSpace(notes) ? "" : " " + notes.Trim());

			var row = db.Materials.FirstOrDefault(r => r.RowState != System.Data.DataRowState.Deleted &&
				r.Model == model && r.Material == material && r.Action == action && r.Thickness == thickness);
			if (row == null)
			{
				row = db.Materials.NewMaterialsRow();
				row.Model = model;
				row.Material = material;
				row.Action = action;
				row.Thickness = thickness;
				db.Materials.AddMaterialsRow(row);
			}
			row.Visible = true;
			row.Power = MaterialSetting.PowerPercent(power, maxPwm);
			row.Speed = speed;
			row.Cycles = passes;
			row.Remarks = remarks;
			MaterialExtrasStore.Set(row.id, extras);
			return row;
		}

		public override string ToString()
		{
			string name = Kind == MaterialTestKind.PowerSpeed ? "Power/speed test" : Kind == MaterialTestKind.Cutting ? "Cutting test" : "Line interval test";
			return string.IsNullOrEmpty(Title) ? name : name + " · " + Title;
		}
	}

	internal static class CalibrationPatterns
	{
		internal const double Origin = 3;

		internal sealed class FocusOptions
		{
			internal bool UseZAxis = true;
			internal int Steps = 9;
			internal double ZStepMm = 0.5;
			internal double LineLengthMm = 20;
			internal double SpacingMm = 6;
			internal double RampLengthMm = 100;
			internal int Power = 1000;
			internal int Speed = 1000;
			internal string LaserMode = "M3";
			internal int TextPower = 500;
			internal int TextSpeed = 1000;
		}

		internal sealed class IntervalOptions
		{
			internal decimal[] LinesPerMm = { 5, 8, 10, 12, 15, 20 };
			internal double PatchWidthMm = 10;
			internal double PatchHeightMm = 10;
			internal double GapMm = 4;
			internal int Power = 600;
			internal int Speed = 3000;
			internal string LaserMode = "M4";
			internal int TextPower = 500;
			internal int TextSpeed = 1000;
		}

		/// <summary>
		/// Relative Z offsets centred on the current height, so the middle line is burned at today's focus.
		/// Returned in burn order, for labelling and for the user to pick from afterwards.
		/// </summary>
		internal static double[] FocusOffsets(int steps, double step)
		{
			if (steps < 1 || steps > 41) throw new ArgumentOutOfRangeException("steps", "Use 1 to 41 focus steps.");
			if (step <= 0 || step > 5) throw new ArgumentOutOfRangeException("step", "Z step must be between 0 and 5 mm.");
			double middle = (steps - 1) / 2.0;
			return Enumerable.Range(0, steps).Select(i => Math.Round((i - middle) * step, 4)).ToArray();
		}

		internal static List<GrblCommand> FocusTest(FocusOptions o)
		{
			var list = new List<GrblCommand> { new GrblCommand("G90"), new GrblCommand("G21"), new GrblCommand("M5 S0") };
			if (o.UseZAxis)
			{
				double current = 0;
				var offsets = FocusOffsets(o.Steps, o.ZStepMm);
				double labelX = Origin + 6, lineX = labelX + 8;
				for (int i = 0; i < offsets.Length; i++)
				{
					double y = Origin + i * o.SpacingMm;
					// Relative Z keeps the test centred on whatever focus the user has set, without needing a known Z zero.
					list.Add(new GrblCommand("G91"));
					list.Add(new GrblCommand("G0 Z" + Fmt(offsets[i] - current)));
					list.Add(new GrblCommand("G90"));
					current = offsets[i];
					list.Add(new GrblCommand(string.Format("G0 X{0} Y{1}", Fmt(lineX), Fmt(y))));
					list.Add(new GrblCommand(string.Format("G1 X{0} {1} S{2} F{3}", Fmt(lineX + o.LineLengthMm), o.LaserMode, o.Power, o.Speed)));
					list.Add(new GrblCommand("M5"));
				}
				list.Add(new GrblCommand("G91"));
				list.Add(new GrblCommand("G0 Z" + Fmt(-current)));
				list.Add(new GrblCommand("G90"));
				for (int i = 0; i < offsets.Length; i++)
					list.AddRange(Hershey.Hershey.CreateString(SignedLabel(offsets[i]), labelX, Origin + i * o.SpacingMm, o.TextSpeed, o.TextPower, true, "M4"));
				list.AddRange(Hershey.Hershey.CreateString("Z focus offset (mm)", lineX + o.LineLengthMm / 2, Origin + offsets.Length * o.SpacingMm, o.TextSpeed, o.TextPower, true, "M4"));
			}
			else
			{
				// Ramp test: prop one end of the material up and burn one long line. The thinnest part of the line marks the focus distance.
				if (o.RampLengthMm < 20 || o.RampLengthMm > 1000) throw new ArgumentOutOfRangeException("RampLengthMm", "Ramp length must be between 20 and 1000 mm.");
				double y = Origin + 8;
				list.Add(new GrblCommand(string.Format("G0 X{0} Y{1}", Fmt(Origin), Fmt(y))));
				list.Add(new GrblCommand(string.Format("G1 X{0} {1} S{2} F{3}", Fmt(Origin + o.RampLengthMm), o.LaserMode, o.Power, o.Speed)));
				list.Add(new GrblCommand("M5"));
				for (int mm = 0; mm <= (int)o.RampLengthMm; mm += 5)
				{
					double tick = mm % 10 == 0 ? 4 : 2;
					list.Add(new GrblCommand(string.Format("G0 X{0} Y{1}", Fmt(Origin + mm), Fmt(y - 1.5))));
					list.Add(new GrblCommand(string.Format("G1 Y{0} M4 S{1} F{2}", Fmt(y - 1.5 - tick), o.TextPower, o.TextSpeed)));
					list.Add(new GrblCommand("M5"));
				}
				for (int mm = 0; mm <= (int)o.RampLengthMm; mm += 10)
					list.AddRange(Hershey.Hershey.CreateString(mm.ToString(CultureInfo.InvariantCulture), Origin + mm, y - 9, o.TextSpeed, o.TextPower, true, "M4"));
				list.AddRange(Hershey.Hershey.CreateString("Ramp focus test (mm from raised end)", Origin + o.RampLengthMm / 2, y + 5, o.TextSpeed, o.TextPower, true, "M4"));
			}
			list.Add(new GrblCommand("M5 S0"));
			return list;
		}

		internal static List<GrblCommand> IntervalTest(IntervalOptions o)
		{
			if (o.LinesPerMm == null || o.LinesPerMm.Length == 0 || o.LinesPerMm.Length > 20)
				throw new ArgumentException("Enter 1 to 20 line intervals.");
			if (o.LinesPerMm.Any(v => v < 1 || v > 50)) throw new ArgumentOutOfRangeException("LinesPerMm", "Lines per mm must be between 1 and 50.");
			if (o.PatchWidthMm < 2 || o.PatchHeightMm < 2) throw new ArgumentOutOfRangeException("PatchWidthMm", "Patches must be at least 2 mm.");
			var list = new List<GrblCommand> { new GrblCommand("G90"), new GrblCommand("G21"), new GrblCommand("M5 S0") };
			for (int i = 0; i < o.LinesPerMm.Length; i++)
			{
				double x0 = Origin + i * (o.PatchWidthMm + o.GapMm), x1 = x0 + o.PatchWidthMm;
				double y0 = Origin + 6, step = 1 / (double)o.LinesPerMm[i];
				int lines = (int)Math.Floor(o.PatchHeightMm / step + 1e-9) + 1;
				list.Add(new GrblCommand(string.Format("G0 X{0} Y{1}", Fmt(x0), Fmt(y0))));
				list.Add(new GrblCommand(string.Format("G1 {0} S0 F{1}", o.LaserMode, o.Speed)));
				for (int line = 0; line < lines; line++)
				{
					bool forward = line % 2 == 0;
					list.Add(new GrblCommand(string.Format("G1 Y{0} S0", Fmt(y0 + line * step))));
					list.Add(new GrblCommand(string.Format("G1 X{0} S{1}", Fmt(forward ? x1 : x0), o.Power)));
				}
				list.Add(new GrblCommand("M5 S0"));
				list.AddRange(Hershey.Hershey.CreateString(Fmt((double)o.LinesPerMm[i]), x0 + o.PatchWidthMm / 2, Origin + 1.5, o.TextSpeed, o.TextPower, true, "M4"));
			}
			double width = o.LinesPerMm.Length * (o.PatchWidthMm + o.GapMm) - o.GapMm;
			list.AddRange(Hershey.Hershey.CreateString(string.Format("Lines/mm at S{0} F{1}", o.Power, o.Speed),
				Origin + width / 2, Origin + 6 + o.PatchHeightMm + 4, o.TextSpeed, o.TextPower, true, "M4"));
			list.Add(new GrblCommand("M5 S0"));
			return list;
		}

		internal static MaterialTestRecord IntervalRecord(IntervalOptions o, string title)
		{
			return new MaterialTestRecord(MaterialTestKind.LineInterval, title, o.LaserMode, new[] { o.Power }, new[] { o.Speed }, new[] { 1 }, o.LinesPerMm.ToArray());
		}

		internal static string SignedLabel(double offset)
		{
			return offset > 0 ? "+" + Fmt(offset) : Fmt(offset);
		}

		internal static string Fmt(double value)
		{
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}
	}
}
