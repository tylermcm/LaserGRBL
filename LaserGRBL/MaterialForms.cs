using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL
{
	/// <summary>Shared layout for the code-built material and calibration dialogs: a label/field table above a button row.</summary>
	internal class SimpleDialog : Form
	{
		protected readonly TableLayoutPanel Fields;
		protected readonly FlowLayoutPanel Buttons;
		protected readonly Label Status;

		internal SimpleDialog(string title)
		{
			Text = title;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			Padding = new Padding(12);
			var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
			Fields = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
			Status = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Padding = new Padding(0, 6, 0, 0) };
			Buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
			root.Controls.Add(Fields);
			root.Controls.Add(Status);
			root.Controls.Add(Buttons);
			Controls.Add(root);
			Load += (sender, args) =>
			{
				UserControls.ThemeMgr.SetTheme(this, true);
				BackColor = ColorScheme.FormBackColor;
				ForeColor = ColorScheme.FormForeColor;
			};
		}

		protected T AddField<T>(string label, T control) where T : Control
		{
			int row = Fields.RowCount++;
			Fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 8, 0) }, 0, row);
			control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
			if (control.Width < 160) control.Width = 160;
			Fields.Controls.Add(control, 1, row);
			return control;
		}

		protected Label AddNote(string text, int bottomPadding = 0)
		{
			var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(420, 0), Padding = new Padding(0, 4, 0, bottomPadding) };
			int row = Fields.RowCount++;
			Fields.Controls.Add(label, 0, row);
			Fields.SetColumnSpan(label, 2);
			return label;
		}

		protected NumericUpDown AddNumber(string label, decimal min, decimal max, decimal value, int decimals = 0, decimal increment = 1)
		{
			return AddField(label, new NumericUpDown
			{
				Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = increment,
				Value = Math.Max(min, Math.Min(max, value))
			});
		}

		protected ComboBox AddChoice(string label, IEnumerable<object> items, object selected = null)
		{
			var combo = AddField(label, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
			foreach (var item in items) combo.Items.Add(item);
			if (selected != null && combo.Items.Contains(selected)) combo.SelectedItem = selected;
			else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
			return combo;
		}

		protected UserControls.GrblButton AddButton(string text, Action action, bool accept = false, bool cancel = false)
		{
			var button = new UserControls.GrblButton { Text = text, AutoSize = true, MinimumSize = new Size(80, 0) };
			button.Click += (sender, args) =>
			{
				try { action(); }
				catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidOperationException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
				{ ShowError(ex.Message); }
			};
			Buttons.Controls.Add(button);
			if (accept) AcceptButton = button;
			if (cancel) CancelButton = button;
			return button;
		}

		protected void ShowError(string message)
		{
			Status.ForeColor = Color.Firebrick;
			Status.Text = message;
		}

		protected void ShowInfo(string message)
		{
			Status.ForeColor = ForeColor;
			Status.Text = message;
		}

		protected static string[] LaserModes = { "M3", "M4" };
	}

	/// <summary>Turns one cell of the last burned test into a Material DB row.</summary>
	internal sealed class RecordTestResultForm : SimpleDialog
	{
		private readonly MaterialTestRecord record = MaterialTestRecord.Last;
		private readonly ComboBox model, material, action, thickness, power, speed, passes, lines;
		private readonly NumericUpDown kerf;
		private readonly TextBox notes;

		internal RecordTestResultForm() : base("Save test result to Material DB")
		{
			if (record == null)
			{
				ShowInfo("Generate a power/speed, cutting or line interval test first. The values it burned are remembered here.");
				AddButton("Close", Close, cancel: true);
				return;
			}
			var rows = GrblCore.MaterialDB.Materials.Where(r => r.RowState != System.Data.DataRowState.Deleted).ToList();
			AddNote(record + " · " + record.Created.ToString("g", CultureInfo.CurrentCulture), 8);
			model = AddEditable("Laser model", rows.Select(r => r.Model), Settings.GetObject("Materials.LastModel", ""));
			material = AddEditable("Material", rows.Select(r => r.Material), record.Title);
			action = AddEditable("Action", new[] { "Engrave", "Cut" }.Concat(rows.Select(r => r.Action)),
				record.Kind == MaterialTestKind.Cutting ? "Cut" : "Engrave");
			thickness = AddEditable("Thickness", rows.Select(r => r.Thickness), "-");
			power = AddChoice("Power (S)", record.Powers.Cast<object>());
			speed = AddChoice("Speed (F)", record.Speeds.Cast<object>());
			passes = AddChoice("Passes", record.Passes.Cast<object>());
			lines = AddChoice("Lines/mm", record.LinesPerMm.Cast<object>());
			foreach (var combo in new[] { power, speed, passes, lines }) combo.Enabled = combo.Items.Count > 1;
			kerf = AddNumber("Kerf mm", 0, 5, 0, 3, 0.01m);
			notes = AddField("Notes", new TextBox());
			AddNote("Pick the cell you liked: the labels beside it on the burned test are the values listed here. " +
				"The Material DB stores power as a percentage of $30, so the exact S also goes in the remarks.");
			AddButton("Cancel", Close, cancel: true);
			AddButton("Save", SaveResult, accept: true);
		}

		private ComboBox AddEditable(string label, IEnumerable<string> known, string text)
		{
			var combo = AddField(label, new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems });
			combo.Items.AddRange(known.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.CurrentCulture).OrderBy(v => v, StringComparer.CurrentCulture).Cast<object>().ToArray());
			combo.Text = text ?? "";
			return combo;
		}

		private void SaveResult()
		{
			var row = record.AddTo(GrblCore.MaterialDB, model.Text, material.Text, thickness.Text, action.Text, (int)power.SelectedItem,
				(int)speed.SelectedItem, (int)passes.SelectedItem, (decimal)lines.SelectedItem, kerf.Value, notes.Text, FocusTestForm.SafeMaxPower());
			GrblCore.MaterialDB.SaveChanges();
			Settings.SetObject("Materials.LastModel", row.Model);
			DialogResult = DialogResult.OK;
			Close();
		}
	}

	internal sealed class FocusTestForm : SimpleDialog
	{
		private readonly CheckBox useZ;
		private readonly NumericUpDown steps, zStep, length, ramp, power, speed;
		private readonly ComboBox mode;
		internal CalibrationPatterns.FocusOptions Options { get; private set; }

		internal FocusTestForm() : base("Focus test")
		{
			int maxPower = SafeMaxPower();
			useZ = AddField("Method", new CheckBox { Text = "Use the Z axis", Checked = Settings.GetObject("FocusTest.UseZ", true), AutoSize = true });
			steps = AddNumber("Lines", 1, 41, Settings.GetObject("FocusTest.Steps", 9));
			zStep = AddNumber("Z step mm", 0.05m, 5, Settings.GetObject("FocusTest.ZStep", 0.5m), 2, 0.05m);
			length = AddNumber("Line length mm", 5, 200, 20);
			ramp = AddNumber("Ramp length mm", 20, 1000, Settings.GetObject("FocusTest.Ramp", 100m));
			power = AddNumber("Power (S)", 0, maxPower, Settings.GetObject("FocusTest.Power", maxPower));
			speed = AddNumber("Speed (F)", 1, 100000, Settings.GetObject("FocusTest.Speed", 1000));
			mode = AddChoice("Laser mode", LaserModes, Settings.GetObject("FocusTest.Mode", "M3"));
			EventHandler sync = (sender, args) =>
			{
				steps.Enabled = zStep.Enabled = length.Enabled = useZ.Checked;
				ramp.Enabled = !useZ.Checked;
				ShowInfo(useZ.Checked
					? "Burns one line per Z offset, centred on the current height. Set focus by eye first; the thinnest line is the best offset. Z moves are relative, so no Z home is needed."
					: "For lasers without Z: raise one end of the material about 10 mm, put the low end at the origin, and burn one line. The thinnest point is in focus.");
			};
			useZ.CheckedChanged += sync;
			sync(null, EventArgs.Empty);
			AddButton("Cancel", Close, cancel: true);
			AddButton("Create", Create, accept: true);
		}

		private void Create()
		{
			Options = new CalibrationPatterns.FocusOptions
			{
				UseZAxis = useZ.Checked, Steps = (int)steps.Value, ZStepMm = (double)zStep.Value, LineLengthMm = (double)length.Value,
				RampLengthMm = (double)ramp.Value, Power = (int)power.Value, Speed = (int)speed.Value, LaserMode = (string)mode.SelectedItem
			};
			Settings.SetObject("FocusTest.UseZ", useZ.Checked);
			Settings.SetObject("FocusTest.Steps", (int)steps.Value);
			Settings.SetObject("FocusTest.ZStep", zStep.Value);
			Settings.SetObject("FocusTest.Ramp", ramp.Value);
			Settings.SetObject("FocusTest.Power", (int)power.Value);
			Settings.SetObject("FocusTest.Speed", (int)speed.Value);
			Settings.SetObject("FocusTest.Mode", (string)mode.SelectedItem);
			DialogResult = DialogResult.OK;
			Close();
		}

		internal static int SafeMaxPower()
		{
			try { return Math.Max(1, (int)GrblCore.Configuration.MaxPWM); }
			catch (Exception) { return 1000; }
		}
	}

	internal sealed class IntervalTestForm : SimpleDialog
	{
		private readonly TextBox values, title;
		private readonly NumericUpDown size, power, speed;
		private readonly ComboBox mode;
		internal CalibrationPatterns.IntervalOptions Options { get; private set; }
		internal string TestTitle { get { return title.Text.Trim(); } }

		internal IntervalTestForm() : base("Line interval test")
		{
			int maxPower = FocusTestForm.SafeMaxPower();
			values = AddField("Lines/mm", new TextBox { Text = Settings.GetObject("IntervalTest.Values", "5, 8, 10, 12, 15, 20") });
			size = AddNumber("Patch size mm", 2, 50, Settings.GetObject("IntervalTest.Size", 10m));
			power = AddNumber("Power (S)", 0, maxPower, Settings.GetObject("IntervalTest.Power", Math.Min(maxPower, 600)));
			speed = AddNumber("Speed (F)", 1, 100000, Settings.GetObject("IntervalTest.Speed", 3000));
			mode = AddChoice("Laser mode", LaserModes, Settings.GetObject("IntervalTest.Mode", "M4"));
			title = AddField("Material (label)", new TextBox());
			ShowInfo("Fills one patch per line density at the same power and speed. Use your power/speed result here, then save the best patch with Save test result to Material DB.");
			AddButton("Cancel", Close, cancel: true);
			AddButton("Create", Create, accept: true);
		}

		internal static decimal[] ParseValues(string text)
		{
			var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			var result = new List<decimal>();
			foreach (var part in parts)
			{
				decimal value;
				if (!decimal.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out value) &&
					!decimal.TryParse(part, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
					throw new FormatException("\"" + part + "\" isn't a number. Separate values with commas, e.g. 5, 8, 10.");
				result.Add(value);
			}
			if (result.Count == 0) throw new FormatException("Enter at least one lines/mm value.");
			return result.Distinct().ToArray();
		}

		private void Create()
		{
			Options = new CalibrationPatterns.IntervalOptions
			{
				LinesPerMm = ParseValues(values.Text), PatchWidthMm = (double)size.Value, PatchHeightMm = (double)size.Value,
				Power = (int)power.Value, Speed = (int)speed.Value, LaserMode = (string)mode.SelectedItem
			};
			Settings.SetObject("IntervalTest.Values", values.Text);
			Settings.SetObject("IntervalTest.Size", size.Value);
			Settings.SetObject("IntervalTest.Power", (int)power.Value);
			Settings.SetObject("IntervalTest.Speed", (int)speed.Value);
			Settings.SetObject("IntervalTest.Mode", (string)mode.SelectedItem);
			DialogResult = DialogResult.OK;
			Close();
		}
	}
}
