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
				Contours = Contours, RepeatContours = RepeatContours, Placement = Placement, Passes = Passes, KerfMm = KerfMm
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
				if (KerfMm > 0 && SupportsKerf)
				{
					// After placement, so the offset is in bed millimetres whatever the scale.
					var kerf = KerfCompensation.Apply(source, contours, (double)KerfMm);
					source = kerf.Commands;
					contours = kerf.Contours;
					KerfCompensated = kerf.Compensated;
					KerfSkipped = kerf.Skipped;
				}
				if (RepeatContours && contours != null && contours.Length > 0)
				{
					for (int contour = 0; contour < contours.Length; contour++)
					{
						int end = contour + 1 < contours.Length ? contours[contour + 1] : source.Length;
						for (int repeat = 0; repeat < Repeats; repeat++)
							AddBlock(commands, source, contours[0], contours[contour], end);
					}
				}
				else
					for (int repeat = 0; repeat < Repeats; repeat++)
					{
						int start = commands.Count;
						AddBlock(commands, source, 0, 0, source.Length);
						if (Passes != null)
							foreach (var pass in Passes)
								CompiledPasses.Add(new JobPassSection(
									Repeats == 1 ? pass.Name : string.Format("Repeat {0} · {1}", repeat + 1, pass.Name),
									start + 3 + pass.Start, start + 3 + pass.End));
					}
			}
			End = commands.Count;
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
			}

			internal JobOperation Restore()
			{
				return new JobOperation(Name, Commands.Select(line => new GrblCommand(line)).ToArray(), Repeats, Settings, Enabled)
				{
					Contours = Contours, RepeatContours = RepeatContours, Placement = Placement, Passes = Passes, KerfMm = KerfMm
				};
			}
		}
	}
}
