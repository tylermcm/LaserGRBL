using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LaserGRBL
{
	internal enum PreflightSeverity { Warning, Error }

	internal sealed class PreflightFinding
	{
		internal readonly PreflightSeverity Severity;
		internal readonly string Text;
		internal PreflightFinding(PreflightSeverity severity, string text) { Severity = severity; Text = text; }
		public override string ToString() { return (Severity == PreflightSeverity.Error ? "✖ " : "⚠ ") + Text; }
	}

	/// <summary>What the pre-flight check knows about the machine and the job. Plain values, so it can be tested without a machine.</summary>
	internal sealed class PreflightInput
	{
		internal int Lines;
		internal bool InAlarm;
		internal bool HasBounds;
		internal decimal MinX, MinY, MaxX, MaxY;
		internal bool HasWorkOffset;
		internal decimal BedWidth, BedHeight;
		internal bool? LaserMode;
		internal decimal MaxPwm;
		internal decimal MaxS;
		internal bool UsesZ;
		internal bool HomingEnabled, Homed;
	}

	internal static class PreflightCheck
	{
		internal const string SettingsKey = "Safety.Preflight";

		internal static bool Enabled
		{
			get { return Settings.GetObject(SettingsKey, true); }
			set { Settings.SetObject(SettingsKey, value); }
		}

		internal static List<PreflightFinding> Run(PreflightInput input)
		{
			var findings = new List<PreflightFinding>();
			var c = CultureInfo.CurrentCulture;
			if (input.Lines == 0) findings.Add(new PreflightFinding(PreflightSeverity.Error, "No job is loaded."));
			if (input.InAlarm) findings.Add(new PreflightFinding(PreflightSeverity.Error, "The machine is in alarm. Unlock or home it first."));
			if (input.HasBounds && input.BedWidth > 0 && input.BedHeight > 0)
			{
				decimal width = input.MaxX - input.MinX, height = input.MaxY - input.MinY;
				if (width > input.BedWidth || height > input.BedHeight)
					findings.Add(new PreflightFinding(PreflightSeverity.Error, string.Format(c,
						"The job is {0:0.#} × {1:0.#} mm but the work area ($130/$131) is {2:0.#} × {3:0.#} mm.", width, height, input.BedWidth, input.BedHeight)));
				else if (!input.HasWorkOffset && (input.MaxX > input.BedWidth || input.MaxY > input.BedHeight))
					findings.Add(new PreflightFinding(PreflightSeverity.Warning, string.Format(c,
						"The job reaches X{0:0.#} Y{1:0.#}, past the {2:0.#} × {3:0.#} mm work area.", input.MaxX, input.MaxY, input.BedWidth, input.BedHeight)));
			}
			if (input.HasBounds && !input.HasWorkOffset && (input.MinX < 0 || input.MinY < 0))
				findings.Add(new PreflightFinding(PreflightSeverity.Warning, string.Format(c,
					"The job has negative coordinates (down to X{0:0.#} Y{1:0.#}) and no work origin is set, so it may run past the machine's corner.", input.MinX, input.MinY)));
			if (input.LaserMode == false)
				findings.Add(new PreflightFinding(PreflightSeverity.Warning, "Laser mode ($32) is off: the laser stays on during travel and pauses at corners, so it burns deeper there."));
			if (input.MaxPwm > 0 && input.MaxS > input.MaxPwm)
				findings.Add(new PreflightFinding(PreflightSeverity.Warning, string.Format(c,
					"The job uses up to S{0:0.##}, but $30 is {1:0.##}. Power above $30 is cut to full power.", input.MaxS, input.MaxPwm)));
			if (input.UsesZ)
				findings.Add(new PreflightFinding(PreflightSeverity.Warning, "The job moves Z. Check the head has room to move."));
			if (input.HomingEnabled && !input.Homed)
				findings.Add(new PreflightFinding(PreflightSeverity.Warning, "The machine hasn't been homed since connecting."));
			return findings;
		}

		/// <summary>Highest S and whether any line moves Z.</summary>
		internal static void Scan(IEnumerable<GrblCommand> commands, out decimal maxS, out bool usesZ)
		{
			maxS = 0;
			usesZ = false;
			foreach (var original in commands)
			{
				var command = new GrblCommand(original.Command);
				command.BuildHelper();
				if (command.S != null) maxS = Math.Max(maxS, command.S.Number);
				if (command.Z != null) usesZ = true;
			}
		}
	}

	/// <summary>Traces the job's bounding box so you can see where it will land before burning.</summary>
	internal static class Framing
	{
		internal static decimal Feed
		{
			get { return Math.Max(100, Math.Min(20000, Settings.GetObject("Framing.Feed", 3000m))); }
			set { Settings.SetObject("Framing.Feed", value); }
		}

		/// <summary>S used while framing. Zero moves with the laser off, for machines with a pointer.</summary>
		internal static decimal Power
		{
			get { return Math.Max(0, Settings.GetObject("Framing.Power", 0m)); }
			set { Settings.SetObject("Framing.Power", value); }
		}

		internal static List<string> Commands(decimal minX, decimal minY, decimal maxX, decimal maxY, decimal feed, decimal power, decimal maxPwm)
		{
			if (maxX < minX || maxY < minY) throw new ArgumentException("The job has no size to frame.");
			if (feed <= 0) throw new ArgumentOutOfRangeException("feed", "Framing speed must be above zero.");
			if (power < 0 || maxPwm > 0 && power > maxPwm * 0.1m)
				throw new ArgumentOutOfRangeException("power", "Framing power must be between 0 and 10% of $30, so it marks without burning.");
			Func<decimal, string> f = v => v.ToString("0.###", CultureInfo.InvariantCulture);
			var lines = new List<string> { "M5", "G90", "G0 X" + f(minX) + " Y" + f(minY) };
			string laser = power > 0 ? "M3 S" + f(power) : "M5";
			lines.Add(laser);
			lines.Add("G1 X" + f(maxX) + " Y" + f(minY) + " F" + f(feed));
			lines.Add("G1 X" + f(maxX) + " Y" + f(maxY));
			lines.Add("G1 X" + f(minX) + " Y" + f(maxY));
			lines.Add("G1 X" + f(minX) + " Y" + f(minY));
			lines.Add("M5 S0");
			return lines;
		}
	}

	/// <summary>
	/// Progress written to disk while a job runs, so a crash, power cut or lost connection can be resumed
	/// after LaserGRBL restarts. Plain text, written to a temporary file then swapped in, so it is never half written.
	/// </summary>
	internal sealed class JobCheckpoint
	{
		/// <summary>How many lines back from the last acknowledged one to resume, because GRBL buffers lines it hasn't run yet.</summary>
		internal const int SafetyLines = 20;

		internal string Name;
		internal ulong Signature;
		internal int Lines;
		internal int Executed;
		internal int PassIndex;
		internal int LoopCount;
		internal float WcoX, WcoY, WcoZ;
		internal DateTime Saved;

		internal int ResumeLine { get { return Math.Max(0, Math.Min(Lines - 1, Executed - SafetyLines)); } }

		internal bool HasWorkOffset { get { return WcoX != 0 || WcoY != 0 || WcoZ != 0; } }

		internal bool Matches(ulong signature, int lines)
		{
			return Signature == signature && Lines == lines && Executed > 0 && Executed < Lines;
		}

		/// <summary>FNV-1a over every line, so a different or edited file never offers someone else's progress.</summary>
		internal static ulong Sign(IEnumerable<GrblCommand> commands)
		{
			ulong hash = 14695981039346656037UL;
			foreach (var command in commands)
			{
				foreach (char ch in command.Command)
				{
					hash ^= ch;
					hash *= 1099511628211UL;
				}
				hash ^= '\n';
				hash *= 1099511628211UL;
			}
			return hash;
		}

		internal static string DefaultPath { get { return Path.Combine(GrblCore.DataPath, "checkpoint.txt"); } }

		internal void Save(string path)
		{
			var i = CultureInfo.InvariantCulture;
			var text = new StringBuilder()
				.AppendLine("name=" + (Name ?? "").Replace('\n', ' ').Replace('\r', ' '))
				.AppendLine("signature=" + Signature.ToString(i))
				.AppendLine("lines=" + Lines.ToString(i))
				.AppendLine("executed=" + Executed.ToString(i))
				.AppendLine("pass=" + PassIndex.ToString(i))
				.AppendLine("loops=" + LoopCount.ToString(i))
				.AppendLine("wco=" + string.Join(",", new[] { WcoX, WcoY, WcoZ }.Select(v => v.ToString("R", i))))
				.AppendLine("saved=" + Saved.ToUniversalTime().ToString("o", i));
			string temp = path + ".tmp";
			File.WriteAllText(temp, text.ToString(), Encoding.UTF8);
			if (File.Exists(path)) File.Delete(path);
			File.Move(temp, path);
		}

		internal static JobCheckpoint Load(string path)
		{
			try
			{
				if (!File.Exists(path)) return null;
				var values = File.ReadAllLines(path, Encoding.UTF8).Where(l => l.Contains('='))
					.ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
				var i = CultureInfo.InvariantCulture;
				var wco = values["wco"].Split(',').Select(v => float.Parse(v, i)).ToArray();
				return new JobCheckpoint
				{
					Name = values["name"],
					Signature = ulong.Parse(values["signature"], i),
					Lines = int.Parse(values["lines"], i),
					Executed = int.Parse(values["executed"], i),
					PassIndex = int.Parse(values["pass"], i),
					LoopCount = int.Parse(values["loops"], i),
					WcoX = wco[0], WcoY = wco[1], WcoZ = wco[2],
					Saved = DateTime.Parse(values["saved"], i, DateTimeStyles.RoundtripKind).ToLocalTime()
				};
			}
			catch (Exception ex) when (ex is IOException || ex is FormatException || ex is KeyNotFoundException || ex is IndexOutOfRangeException || ex is UnauthorizedAccessException || ex is OverflowException)
			{
				return null; // unreadable progress is treated as none
			}
		}

		internal static void Clear(string path)
		{
			try { if (File.Exists(path)) File.Delete(path); }
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}
	}
}
