using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LaserGRBL
{
	internal sealed class JobPassForm : Form
	{
		private readonly JobPassEditor editor;
		internal JobPassPlan Plan { get { return editor.Plan; } }

		public JobPassForm(JobPassPlan plan, int repeatCount, bool exporting = false,
			JobPass rasterDefaults = null, decimal maxResolution = 0, bool supportsPower = true)
		{
			Text = "Job passes";
			ClientSize = new Size(rasterDefaults == null ? 480 : 660, 450);
			MinimumSize = new Size(rasterDefaults == null ? 450 : 640, 400);
			StartPosition = FormStartPosition.CenterParent;
			MinimizeBox = MaximizeBox = false;
			ShowInTaskbar = false;
			editor = new JobPassEditor(plan, repeatCount, exporting, rasterDefaults, maxResolution, supportsPower)
			{
				Dock = DockStyle.Fill
			};
			editor.ApplyRequested += (sender, args) => DialogResult = DialogResult.OK;
			editor.CancelRequested += (sender, args) => DialogResult = DialogResult.Cancel;
			Controls.Add(editor);
			AcceptButton = editor.ApplyButton;
			CancelButton = editor.CancelButton;
		}
	}

	internal sealed class JobPassEditor : UserControl
	{
		private readonly CheckBox usePasses;
		private readonly NumericUpDown count;
		private readonly TableLayoutPanel rows;
		private readonly List<NumericUpDown> powers = new List<NumericUpDown>();
		private readonly List<NumericUpDown> speeds = new List<NumericUpDown>();
		private readonly List<ComboBox> directions = new List<ComboBox>();
		private readonly List<NumericUpDown> resolutions = new List<NumericUpDown>();
		private readonly JobPass rasterDefaults;
		private readonly decimal maxResolution;
		private readonly bool supportsPower;
		private readonly JobPassProfiles profiles;
		private readonly ComboBox profilePicker;
		private static readonly RasterConverter.ImageProcessor.Direction[] ScanDirections =
		{
			RasterConverter.ImageProcessor.Direction.Horizontal, RasterConverter.ImageProcessor.Direction.Vertical,
			RasterConverter.ImageProcessor.Direction.Diagonal, RasterConverter.ImageProcessor.Direction.NewReverseDiagonal
		};
		internal JobPassPlan Plan { get; private set; }
		internal Button ApplyButton { get; private set; }
		internal Button CancelButton { get; private set; }
		internal event EventHandler ApplyRequested;
		internal event EventHandler CancelRequested;

		internal JobPassEditor(JobPassPlan plan, int repeatCount, bool exporting = false,
			JobPass rasterDefaults = null, decimal maxResolution = 0, bool supportsPower = true)
		{
			this.rasterDefaults = rasterDefaults;
			this.maxResolution = maxResolution;
			this.supportsPower = supportsPower;
			profiles = new JobPassProfiles(rasterDefaults != null);
			Text = "Job passes";
			Font = new Font("Segoe UI", 9F);
			AutoScaleMode = AutoScaleMode.Font;
			BackColor = ColorScheme.FormBackColor;
			ForeColor = ColorScheme.FormForeColor;

			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(layout);

			usePasses = new CheckBox { Text = rasterDefaults == null ? "Set power and speed for each pass" : "Use custom raster passes", AutoSize = true, Checked = plan != null };
			layout.Controls.Add(usePasses, 0, 0);
			var description = new Label
			{
				Text = rasterDefaults != null
					? "Power and speed are relative to the import settings. Each pass uses the same image size and origin. Change direction to cross the previous scan lines, or change lines/mm to vary their spacing."
					: "Power and speed are percentages of the imported settings. 100% keeps the original value. For example, 200% speed turns 600 mm/min into 1200 mm/min. Prepare the file at full power for the 50 / 100 / 50 power preset.\n\n" + (exporting ? "Save writes all passes into one G-code file. This does not change the loaded job." : "Applying changes starts the next job from the beginning. Loading another file clears the sequence."),
				AutoSize = true, MaximumSize = new Size(rasterDefaults == null ? 440 : 600, 0), Margin = new Padding(3, 8, 3, 8)
			};
			layout.Controls.Add(description, 0, 1);
			layout.SizeChanged += (sender, args) =>
				description.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal - description.Margin.Horizontal), 0);

			var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
			options.Controls.Add(new Label { Text = "Passes:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
			count = new NumericUpDown { Minimum = 1, Maximum = 99, Value = plan == null ? repeatCount : plan.Count, Width = 60, AccessibleName = "Number of passes" };
			options.Controls.Add(count);
			var add = new UserControls.GrblButton { Text = "Add pass", AutoSize = true };
			add.Click += (sender, args) => { usePasses.Checked = true; if (count.Value < count.Maximum) count.Value++; };
			options.Controls.Add(add);
			var preset = new UserControls.GrblButton { Text = "50% / 100% / 50%", AutoSize = true };
			preset.Enabled = supportsPower;
			options.Controls.Add(preset);
			layout.Controls.Add(options, 0, 2);

			var profileControls = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
			profileControls.Controls.Add(new Label { Text = "Profile", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
			profilePicker = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList, Width = 153, DropDownWidth = 220, AccessibleName = "Saved pass profiles",
				BackColor = ColorScheme.LogBackColor, ForeColor = ColorScheme.FormForeColor
			};
			profileControls.Controls.Add(profilePicker);
			var loadProfile = new UserControls.GrblButton { Text = "Load", AutoSize = true, Enabled = false };
			var saveProfile = new UserControls.GrblButton { Text = "Save as...", AutoSize = true, Enabled = usePasses.Checked };
			var deleteProfile = new UserControls.GrblButton { Text = "Delete", AutoSize = true, Enabled = false };
			profileControls.Controls.AddRange(new Control[] { loadProfile, saveProfile, deleteProfile });
			profilePicker.SelectedIndexChanged += (sender, args) => loadProfile.Enabled = deleteProfile.Enabled = profilePicker.SelectedItem != null;
			loadProfile.Click += (sender, args) => LoadProfile();
			var namePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Visible = false };
			var profileName = new TextBox { Width = 153, MaxLength = 60, AccessibleName = "Profile name" };
			var confirmName = new UserControls.GrblButton { Text = "Save", AutoSize = true };
			var cancelName = new UserControls.GrblButton { Text = "Cancel", AutoSize = true };
			namePanel.Controls.AddRange(new Control[]
			{
				new Label { Text = "Profile name", AutoSize = true, Margin = new Padding(3, 6, 3, 3) },
				profileName, confirmName, cancelName
			});
			profileControls.Controls.Add(namePanel);
			profileControls.SetFlowBreak(deleteProfile, true);
			saveProfile.Click += (sender, args) =>
			{
				profileName.Text = profilePicker.SelectedItem as string ?? "";
				confirmName.Enabled = !string.IsNullOrWhiteSpace(profileName.Text);
				namePanel.Visible = true;
				profileName.Focus();
				profileName.SelectAll();
			};
			profileName.TextChanged += (sender, args) => confirmName.Enabled = !string.IsNullOrWhiteSpace(profileName.Text);
			confirmName.Click += (sender, args) => { if (SaveProfile(profileName.Text.Trim())) namePanel.Hide(); };
			cancelName.Click += (sender, args) => namePanel.Hide();
			profileName.KeyDown += (sender, args) =>
			{
				if (args.KeyCode == Keys.Enter)
				{
					confirmName.PerformClick();
					args.SuppressKeyPress = true;
				}
				else if (args.KeyCode == Keys.Escape)
				{
					namePanel.Hide();
					args.SuppressKeyPress = true;
				}
			};
			deleteProfile.Click += (sender, args) => DeleteProfile();
			RefreshProfiles();
			layout.Controls.Add(profileControls, 0, 3);

			var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(3, 8, 3, 8) };
			rows = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = rasterDefaults == null ? 3 : 5 };
			rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
			for (int i = 1; i < rows.ColumnCount; i++) rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / (rows.ColumnCount - 1)));
			rows.Controls.Add(new Label { Text = "Pass", AutoSize = true }, 0, 0);
			rows.Controls.Add(new Label { Text = "Power (%)", AutoSize = true }, 1, 0);
			rows.Controls.Add(new Label { Text = "Speed (%)", AutoSize = true }, 2, 0);
			if (rasterDefaults != null)
			{
				rows.Controls.Add(new Label { Text = "Direction", AutoSize = true }, 3, 0);
				rows.Controls.Add(new Label { Text = "Lines/mm", AutoSize = true }, 4, 0);
			}
			scroll.Controls.Add(rows);
			layout.Controls.Add(scroll, 0, 4);
			ResizePasses();
			if (plan != null) PopulatePasses(plan);

			count.ValueChanged += (sender, args) => ResizePasses();
			usePasses.CheckedChanged += (sender, args) =>
			{
				saveProfile.Enabled = usePasses.Checked;
				if (!usePasses.Checked) namePanel.Hide();
				UpdatePassInputs();
			};
			preset.Click += (sender, args) =>
			{
				usePasses.Checked = true;
				count.Value = 3;
				powers[0].Value = 50;
				powers[1].Value = 100;
				powers[2].Value = 50;
				foreach (var speed in speeds) speed.Value = 100;
			};

			var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
			CancelButton = new UserControls.GrblButton { Text = "Cancel", AutoSize = true };
			ApplyButton = new UserControls.GrblButton { Text = "Apply", AutoSize = true };
			CancelButton.Click += (sender, args) => CancelRequested?.Invoke(this, EventArgs.Empty);
			ApplyButton.Click += (sender, args) =>
			{
				Plan = usePasses.Checked ? ReadPasses() : null;
				ApplyRequested?.Invoke(this, EventArgs.Empty);
			};
			buttons.Controls.Add(CancelButton);
			buttons.Controls.Add(ApplyButton);
			layout.Controls.Add(buttons, 0, 5);
		}

		internal void RefreshTheme()
		{
			BackColor = ColorScheme.FormBackColor;
			ForeColor = ColorScheme.FormForeColor;
			profilePicker.BackColor = ColorScheme.LogBackColor;
			profilePicker.ForeColor = ColorScheme.FormForeColor;
			Invalidate(true);
		}

		private void UpdatePassInputs()
		{
			bool editing = usePasses.Checked;
			count.Enabled = editing;
			foreach (var power in powers) power.Enabled = editing && supportsPower;
			foreach (var speed in speeds) speed.Enabled = editing;
			foreach (var direction in directions) direction.Enabled = editing;
			foreach (var resolution in resolutions) resolution.Enabled = editing;
		}

		private JobPassPlan ReadPasses()
		{
			var values = new List<JobPass>();
			for (int i = 0; i < powers.Count; i++)
				values.Add(rasterDefaults == null ? new JobPass(powers[i].Value, speeds[i].Value) :
					new JobPass(powers[i].Value, speeds[i].Value, ScanDirections[directions[i].SelectedIndex], resolutions[i].Value));
			return new JobPassPlan(values);
		}

		private void PopulatePasses(JobPassPlan plan)
		{
			count.Value = plan.Count;
			for (int i = 0; i < plan.Count; i++)
			{
				powers[i].Value = supportsPower ? plan[i].PowerPercent : 100;
				speeds[i].Value = plan[i].SpeedPercent;
				if (rasterDefaults != null)
				{
					int direction = Array.IndexOf(ScanDirections, plan[i].Direction);
					if (direction >= 0) directions[i].SelectedIndex = direction;
					if (plan[i].LinesPerMm > 0)
						resolutions[i].Value = Math.Max(resolutions[i].Minimum, Math.Min(maxResolution, plan[i].LinesPerMm));
				}
			}
		}

		private void RefreshProfiles(string selected = null)
		{
			profilePicker.BeginUpdate();
			profilePicker.Items.Clear();
			foreach (string name in profiles.Names)
			{
				int index = profilePicker.Items.Add(name);
				if (StringComparer.OrdinalIgnoreCase.Equals(name, selected))
					profilePicker.SelectedIndex = index;
			}
			profilePicker.EndUpdate();
		}

		private void LoadProfile()
		{
			JobPassPlan profile;
			if (!(profilePicker.SelectedItem is string name) || !profiles.TryGet(name, out profile)) return;
			for (int i = 0; i < profile.Count; i++)
			{
				string error = null;
				if (!supportsPower && profile[i].PowerPercent != 100)
					error = "This profile requires power modulation, which is disabled for this job.";
				else if (rasterDefaults != null && profile[i].LinesPerMm > maxResolution)
					error = "This profile exceeds the current resolution limit. Enable Raster Hi-Res to load it.";
				if (error != null)
				{
					MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
			}
			usePasses.Checked = true;
			PopulatePasses(profile);
		}

		private bool SaveProfile(string name)
		{
			JobPassPlan existing;
			if (profiles.TryGet(name, out existing) && MessageBox.Show(this, "Replace the profile “" + name + "”?",
				"Save pass profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
				return false;
			profiles.Save(name, ReadPasses());
			RefreshProfiles(name);
			return true;
		}

		private void DeleteProfile()
		{
			string name = profilePicker.SelectedItem as string;
			if (name == null || MessageBox.Show(this, "Delete the profile “" + name + "”?", Text, MessageBoxButtons.YesNo,
				MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
			profiles.Remove(name);
			RefreshProfiles();
		}

		private void ResizePasses()
		{
			rows.SuspendLayout();
			while (powers.Count > count.Value)
			{
				int index = powers.Count - 1;
				Control label = rows.GetControlFromPosition(0, index + 1);
				rows.Controls.Remove(label);
				label.Dispose();
				rows.Controls.Remove(powers[index]);
				powers[index].Dispose();
				powers.RemoveAt(index);
				rows.Controls.Remove(speeds[index]);
				speeds[index].Dispose();
				speeds.RemoveAt(index);
				if (rasterDefaults != null)
				{
					directions[index].Dispose();
					resolutions[index].Dispose();
					directions.RemoveAt(index);
					resolutions.RemoveAt(index);
				}
			}
			while (powers.Count < count.Value)
			{
				int index = powers.Count;
				var power = new NumericUpDown { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Value = 100, Dock = DockStyle.Top, MaximumSize = new Size(110, 0), AccessibleName = "Pass " + (index + 1) + " power percent" };
				var speed = new NumericUpDown { Minimum = 1, Maximum = 1000, DecimalPlaces = 1, Value = 100, Dock = DockStyle.Top, MaximumSize = new Size(110, 0), AccessibleName = "Pass " + (index + 1) + " speed percent" };
				rows.RowCount = index + 2;
				rows.Controls.Add(new Label { Text = "Pass " + (index + 1), AutoSize = true, Anchor = AnchorStyles.Left }, 0, index + 1);
				rows.Controls.Add(power, 1, index + 1);
				rows.Controls.Add(speed, 2, index + 1);
				powers.Add(power);
				speeds.Add(speed);
				if (rasterDefaults != null)
				{
					var direction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Pass " + (index + 1) + " direction" };
					direction.Items.AddRange(new object[] { "Horizontal", "Vertical", "Diagonal", "Reverse diagonal" });
					direction.SelectedIndex = Math.Max(0, Array.IndexOf(ScanDirections, rasterDefaults.Direction));
					var resolution = new NumericUpDown { Minimum = 0.1m, Maximum = maxResolution, DecimalPlaces = 3, Increment = 0.1m, Value = Math.Max(0.1m, Math.Min(maxResolution, rasterDefaults.LinesPerMm)), Dock = DockStyle.Top, MaximumSize = new Size(110, 0), AccessibleName = "Pass " + (index + 1) + " lines per millimeter" };
					rows.Controls.Add(direction, 3, index + 1);
					rows.Controls.Add(resolution, 4, index + 1);
					directions.Add(direction);
					resolutions.Add(resolution);
				}
			}
			rows.RowCount = powers.Count + 1;
			UpdatePassInputs();
			rows.ResumeLayout(true);
		}
	}
}
