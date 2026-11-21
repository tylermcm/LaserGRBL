using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL
{
	/// <summary>Framing speed and power, plus a button to trace the job outline now.</summary>
	internal sealed class FramingForm : SimpleDialog
	{
		private FramingForm(GrblCore core) : base("Frame job")
		{
			AddNote("Traces the rectangle around everything the job burns, so you can check where it lands on the material.", 6);
			var feed = AddNumber("Speed (mm/min)", 100, 20000, Framing.Feed, 0, 100);
			var power = AddNumber("Power (S)", 0, 100, Framing.Power, 0, 1);
			AddNote("Leave power at 0 to move with the laser off, for a pointer or watching the head. A low S makes a faint mark; " +
				"it is limited to 10% of $30 so it can't burn.");
			AddButton("Close", Close, cancel: true);
			AddButton("Frame now", () =>
			{
				Framing.Feed = feed.Value;
				Framing.Power = power.Value;
				core.FrameJob();
				ShowInfo("Framing sent.");
			}, accept: true);
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			using (var form = new FramingForm(core)) form.ShowDialog(owner);
		}
	}

	/// <summary>Files to run in order. When a job finishes, LaserGRBL offers to load the next one; you still press Run.</summary>
	internal sealed class JobQueueForm : SimpleDialog
	{
		private readonly ListBox list = new ListBox { Width = 420, Height = 200, HorizontalScrollbar = true };

		private JobQueueForm(GrblCore core) : base("Job queue")
		{
			AddNote("When a job finishes, LaserGRBL offers to load the next file here. It never starts it on its own, so you can swap the material first.", 6);
			int row = Fields.RowCount++;
			Fields.Controls.Add(list, 0, row);
			Fields.SetColumnSpan(list, 2);
			Reload(0);
			AddButton("Close", Close, cancel: true);
			AddButton("Load next now", () =>
			{
				if (!core.CanLoadNewFile) throw new InvalidOperationException("Wait until the current job finishes to load the next one.");
				string next = JobQueue.TakeNext();
				if (next == null) throw new InvalidOperationException("The queue is empty.");
				if (!File.Exists(next)) throw new IOException("This file no longer exists: " + next);
				core.OpenFile(next);
				Reload(0);
			});
			AddButton("Clear", () => { JobQueue.Clear(); Reload(0); });
			AddButton("Remove", () => { int i = list.SelectedIndex; JobQueue.RemoveAt(i); Reload(i); });
			AddButton("Down", () => Reload(JobQueue.Move(list.SelectedIndex, 1)));
			AddButton("Up", () => Reload(JobQueue.Move(list.SelectedIndex, -1)));
			AddButton("Add files...", () =>
			{
				using (var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true,
					Filter = "Any supported file|*.nc;*.cnc;*.tap;*.gcode;*.ngc;*.bmp;*.png;*.jpg;*.jpeg;*.gif;*.svg;*.lps|All files|*.*" })
				{
					if (dialog.ShowDialog(this) != DialogResult.OK) return;
					JobQueue.Add(dialog.FileNames);
					Reload(list.Items.Count);
				}
			});
		}

		private void Reload(int select)
		{
			list.Items.Clear();
			foreach (var item in JobQueue.Items) list.Items.Add(item);
			if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, select));
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			using (var form = new JobQueueForm(core)) form.ShowDialog(owner);
		}

		/// <summary>Called when a job finishes: offers the next queued file.</summary>
		internal static void OfferNext(IWin32Window owner, GrblCore core)
		{
			var items = JobQueue.Items;
			if (items.Count == 0 || !core.CanLoadNewFile) return;
			var answer = MessageBox.Show(owner,
				"Job finished. Load the next queued job?" + Environment.NewLine + Environment.NewLine + Path.GetFileName(items[0]) +
				Environment.NewLine + Environment.NewLine + "It won't start until you press Run, so swap the material first." +
				(items.Count > 1 ? Environment.NewLine + (items.Count - 1) + " more after it." : ""),
				"Job queue", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (answer != DialogResult.Yes) return;
			string next = JobQueue.TakeNext();
			if (File.Exists(next)) core.OpenFile(next);
			else MessageBox.Show(owner, "This file no longer exists:" + Environment.NewLine + next, "Job queue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	/// <summary>Save and switch between named sets of LaserGRBL settings, one per machine.</summary>
	internal sealed class MachinePresetsForm : SimpleDialog
	{
		private readonly ComboBox names;

		private MachinePresetsForm() : base("Machine presets")
		{
			AddNote("A preset keeps this machine's LaserGRBL settings: connection type and speed, firmware, laser on/off commands, " +
				"custom header and footer, raster and jog defaults, and safety options. GRBL's own $ settings stay on the machine.", 6);
			names = AddField("Preset", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 240 });
			Reload(MachinePresets.Active);
			AddNote("Type a new name and Save to keep the current settings. Load applies a preset; reconnect afterwards so connection settings take effect.");
			AddButton("Close", Close, cancel: true);
			AddButton("Delete", () =>
			{
				string name = names.Text.Trim();
				if (!MachinePresets.Names.Contains(name)) throw new ArgumentException("Choose a saved preset to delete.");
				MachinePresets.Delete(name);
				Reload(null);
				ShowInfo("Deleted " + name + ".");
			});
			AddButton("Load", () =>
			{
				int count = MachinePresets.Apply(names.Text.Trim());
				ShowInfo(string.Format("Loaded {0} setting(s) from {1}. Reconnect to use the connection settings.", count, names.Text.Trim()));
			});
			AddButton("Save", () =>
			{
				int count = MachinePresets.Save(names.Text);
				Reload(names.Text.Trim());
				ShowInfo(string.Format("Saved {0} setting(s) as {1}.", count, names.Text.Trim()));
			}, accept: true);
		}

		private void Reload(string select)
		{
			names.Items.Clear();
			foreach (var name in MachinePresets.Names) names.Items.Add(name);
			names.Text = select ?? (names.Items.Count > 0 ? (string)names.Items[0] : "");
		}

		internal static void ShowFor(IWin32Window owner)
		{
			using (var form = new MachinePresetsForm()) form.ShowDialog(owner);
		}
	}
}
