using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace LaserGRBL
{
	[Serializable]
	internal sealed class JobPassSection
	{
		internal readonly string Name;
		internal readonly int Start;
		internal readonly int End;

		internal JobPassSection(string name, int start, int end)
		{
			Name = name;
			Start = start;
			End = end;
		}
	}

	internal sealed class JobOperation
	{
		internal string Name { get; private set; }
		internal GrblCommand[] Source { get; private set; }
		internal int Repeats { get; private set; }
		internal JobPass Settings { get; private set; }
		internal bool Enabled { get; private set; }
		internal int[] Contours { get; set; }
		internal bool RepeatContours { get; set; }
		internal JobPlacement Placement { get; set; }
		internal JobPassSection[] Passes { get; set; }
		/// <summary>Beam width to compensate on closed SVG contours, in mm. Zero leaves paths as drawn.</summary>
		internal decimal KerfMm { get; set; }
		/// <summary>Set by the last AppendTo: how many contours kerf compensation moved and left unchanged.</summary>
		internal int KerfCompensated { get; private set; }
		internal int KerfSkipped { get; private set; }
		/// <summary>Cut order, lead-ins, tabs, fill and Z step. Null runs the contours as drawn.</summary>
		internal ToolpathOptions Toolpath { get; set; }
		/// <summary>What the last AppendTo did with <see cref="Toolpath"/>, for the status line.</summary>
		internal string ToolpathReport { get; private set; }
		internal List<JobPassSection> CompiledPasses { get; } = new List<JobPassSection>();
		internal int Start { get; set; }
		internal int End { get; set; }

		internal JobOperation(string name, GrblCommand[] source, int repeats = 1, JobPass settings = null, bool enabled = true)
		{
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a name for each operation.");
			if (repeats < 1 || repeats > 99) throw new ArgumentOutOfRangeException("repeats");
			Name = name.Trim();
			Source = source;
			Repeats = repeats;
			Settings = settings;
			Enabled = enabled;
		}

		internal static JobOperation FromImport(string filename, IEnumerable<GrblCommand> commands)
		{
			return new JobOperation(Path.GetFileNameWithoutExtension(filename), commands.ToArray());
		}

		internal JobOperation WithSettings(string name, int repeat, JobPass settings, bool enabled)
		{
			return new JobOperation(name, Source, repeat, settings, enabled)
			{
				Contours = Contours, RepeatContours = RepeatContours, Placement = Placement, Passes = Passes, KerfMm = KerfMm,
				Toolpath = Toolpath?.Clone()
			};
		}

		internal bool SupportsKerf { get { return Contours != null && Contours.Length > 0; } }

		internal void AppendTo(List<GrblCommand> commands)
		{
			Start = commands.Count;
			if (Enabled)
			{
				var source = Placement == null ? Source : Placement.Transform(Source);
				var contours = Contours;
				KerfCompensated = KerfSkipped = 0;
				ToolpathReport = null;
				bool contourWork = SupportsKerf && (KerfMm > 0 || Toolpath != null && Toolpath.ChangesContours);
				if (contourWork)
				{
					// After placement, so offsets, lead-ins and tabs are in bed millimetres whatever the scale.
					var rewritten = ContourToolpath.Apply(source, contours, Toolpath, (double)KerfMm);
					source = rewritten.Commands;
					contours = rewritten.Contours;
					KerfCompensated = rewritten.KerfCompensated;
					KerfSkipped = rewritten.KerfSkipped;
					ToolpathReport = Report(rewritten);
				}
				decimal zStep = Toolpath?.ZStepMm ?? 0;
				if (RepeatContours && contours != null && contours.Length > 0)
				{
					for (int contour = 0; contour < contours.Length; contour++)
					{
						int end = contour + 1 < contours.Length ? contours[contour + 1] : source.Length;
						for (int repeat = 0; repeat < Repeats; repeat++)
						{
							StepZ(commands, zStep, repeat);
							AddBlock(commands, source, contours[0], contours[contour], end);
						}
						ReturnZ(commands, zStep, Repeats);
					}
				}
				else
				{
					for (int repeat = 0; repeat < Repeats; repeat++)
					{
						StepZ(commands, zStep, repeat);
						int start = commands.Count;
						AddBlock(commands, source, 0, 0, source.Length);
						if (Passes != null)
							foreach (var pass in Passes)
								CompiledPasses.Add(new JobPassSection(
									Repeats == 1 ? pass.Name : string.Format("Repeat {0} · {1}", repeat + 1, pass.Name),
									start + 3 + pass.Start, start + 3 + pass.End));
					}
					ReturnZ(commands, zStep, Repeats);
				}
			}
			End = commands.Count;
		}

		/// <summary>Lowers the head by one Z step before every repeat after the first; the laser is already off (M5).</summary>
		private static void StepZ(List<GrblCommand> commands, decimal step, int repeat)
		{
			if (step <= 0 || repeat == 0) return;
			commands.Add(new GrblCommand("G91"));
			commands.Add(new GrblCommand("G0 Z" + (-step).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
			commands.Add(new GrblCommand("G90"));
		}

		/// <summary>Puts Z back where the operation started, so the next operation and the next job keep their focus.</summary>
		private static void ReturnZ(List<GrblCommand> commands, decimal step, int repeats)
		{
			if (step <= 0 || repeats < 2) return;
			commands.Add(new GrblCommand("G91"));
			commands.Add(new GrblCommand("G0 Z" + (step * (repeats - 1)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
			commands.Add(new GrblCommand("G90"));
		}

		private static string Report(ContourToolpath.Result result)
		{
			var parts = new List<string>();
			if (result.FillPaths > 0) parts.Add(result.FillPaths + " fill line(s)");
			if (result.Reordered) parts.Add("reordered inside first");
			if (result.LeadIns > 0) parts.Add(result.LeadIns + " lead-in(s)");
			if (result.LeadSkipped > 0) parts.Add(result.LeadSkipped + " contour(s) had no room for a lead-in");
			if (result.Tabbed > 0) parts.Add(result.Tabbed + " outline(s) tabbed");
			parts.AddRange(result.Notes);
			return parts.Count == 0 ? null : string.Join("; ", parts);
		}

		private void AddBlock(List<GrblCommand> commands, GrblCommand[] source, int prefix, int start, int end)
		{
			commands.Add(new GrblCommand("M5"));
			commands.Add(new GrblCommand("G21"));
			commands.Add(new GrblCommand("G90"));
			for (int index = 0; index < prefix; index++) AddCommand(commands, source[index]);
			for (int index = start; index < end; index++) AddCommand(commands, source[index]);
			commands.Add(new GrblCommand("M5"));
		}

		private void AddCommand(List<GrblCommand> commands, GrblCommand original)
		{
			var command = original.CloneForPass(Settings);
			command.LinkedDisplayList = null;
			command.ClearResult();
			commands.Add(command);
		}

		[Serializable]
		internal sealed class Saved
		{
			internal string Name;
			internal string[] Commands;
			internal int Repeats;
			internal JobPass Settings;
			internal bool Enabled;
			internal int[] Contours;
			internal bool RepeatContours;
			internal JobPlacement Placement;
			internal JobPassSection[] Passes;
			[OptionalField] internal decimal KerfMm;
			[OptionalField] internal ToolpathOptions Toolpath;

			internal Saved(JobOperation operation)
			{
				Name = operation.Name;
				Commands = operation.Source.Select(command => command.Command).ToArray();
				Repeats = operation.Repeats;
				Settings = operation.Settings;
				Enabled = operation.Enabled;
				Contours = operation.Contours;
				RepeatContours = operation.RepeatContours;
				Placement = operation.Placement;
				Passes = operation.Passes;
				KerfMm = operation.KerfMm;
				Toolpath = operation.Toolpath;
			}

			internal JobOperation Restore()
			{
				return new JobOperation(Name, Commands.Select(line => new GrblCommand(line)).ToArray(), Repeats, Settings, Enabled)
				{
					Contours = Contours, RepeatContours = RepeatContours, Placement = Placement, Passes = Passes, KerfMm = KerfMm, Toolpath = Toolpath
				};
			}
		}
	}
}
