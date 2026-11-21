using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL
{
	internal sealed class JobOperationsEditor : UserControl
	{
		private readonly GrblCore core;
		private readonly PreviewForm host;
		private readonly DataGridView grid;
		private readonly FlowLayoutPanel actions;
		internal event EventHandler CloseRequested;
		internal event Action<string> StatusChanged;

		internal JobOperationsEditor(GrblCore core, PreviewForm host)
		{
			this.core = core;
			this.host = host;
			Text = "Job operations";
			var description = new Label { AutoSize = true, Text = "Operations run top to bottom. Use File → Append File to add an outline." };
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(layout);
			layout.Controls.Add(description, 0, 0);
			layout.SizeChanged += (sender, args) => description.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 24), 0);
			grid = new DataGridView
			{
				Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
				AllowUserToResizeRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
				MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false
			};
			grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Run", FillWeight = 35 });
			grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Operation", FillWeight = 200 });
			grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Repeats", FillWeight = 55 });
			grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Power %", FillWeight = 65 });
			grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Speed %", FillWeight = 65 });
			grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Current estimate", ReadOnly = true, FillWeight = 105 });
			grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Per contour", FillWeight = 70 });
			grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Kerf mm", FillWeight = 60, ToolTipText = "Half of this moves closed SVG outlines outward and holes inward, so cut parts keep their drawn size." });
			grid.Columns[3].ReadOnly = !Settings.GetObject("Support Hardware PWM", true);
			foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
			foreach (var operation in core.LoadedFile.Operations)
			{
				TimeSpan duration = operation.Enabled
					? Offset(core.LoadedFile, operation.End) - Offset(core.LoadedFile, operation.Start) : TimeSpan.Zero;
				int row = grid.Rows.Add(operation.Enabled, operation.Name, operation.Repeats,
					operation.Settings?.PowerPercent ?? 100, operation.Settings?.SpeedPercent ?? 100, FormatTime(duration), operation.RepeatContours,
					operation.KerfMm.ToString("0.###", CultureInfo.CurrentCulture));
				grid.Rows[row].Tag = operation;
				grid.Rows[row].Cells[6].ReadOnly = !operation.SupportsKerf;
				grid.Rows[row].Cells[7].ReadOnly = !operation.SupportsKerf;
			}
			layout.Controls.Add(grid, 0, 1);
			actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
			AddButton("Move up", () => MoveRow(-1));
			AddButton("Move down", () => MoveRow(1));
			AddButton("Preview selected", PreviewSelected);
			AddButton("Place selected", PlaceSelected);
			AddButton("Apply material...", ApplyMaterial);
			AddButton("Apply", Apply);
			AddButton("Cancel", () => CloseRequested?.Invoke(this, EventArgs.Empty));
			layout.Controls.Add(actions, 0, 2);
			RefreshTheme();
		}

		internal void RefreshTheme()
		{
			BackColor = ColorScheme.FormBackColor;
			ForeColor = ColorScheme.FormForeColor;
			grid.BackgroundColor = ColorScheme.FormBackColor;
			grid.GridColor = ColorScheme.ControlsBorder;
			grid.EnableHeadersVisualStyles = false;
			grid.ColumnHeadersDefaultCellStyle.BackColor = ColorScheme.FormBackColor;
			grid.ColumnHeadersDefaultCellStyle.ForeColor = ColorScheme.FormForeColor;
			grid.DefaultCellStyle.BackColor = ColorScheme.LogBackColor;
			grid.DefaultCellStyle.ForeColor = ColorScheme.FormForeColor;
			Invalidate(true);
		}

		private void PlaceSelected()
		{
			var row = grid.CurrentRow;
			var operation = row?.Tag as JobOperation;
			if (operation == null) return;
			try
			{
				var view = new JobPlacementView(core.LoadedFile, operation, operation.Placement);
				view.CancelRequested += (sender, args) => host.PopJobView();
				view.ApplyRequested += (sender, args) =>
				{
					var draft = operation.WithSettings(operation.Name, operation.Repeats, operation.Settings, operation.Enabled);
					draft.Start = operation.Start;
					draft.End = operation.End;
					draft.Placement = view.Placement;
					row.Tag = draft;
					row.Cells[5].Value = "Apply to update";
					host.PopJobView();
				};
				host.PushJobView(view, view.Text);
			}
			catch (Exception ex) { ShowError(ex); }
		}

		private void ApplyMaterial()
		{
			var row = grid.CurrentRow;
			var operation = row?.Tag as JobOperation;
			if (operation == null || !grid.EndEdit()) return;
			var chosen = PSHelper.PSHelperForm.CreateAndShowDialog(FindForm());
			if (chosen == null) return;
			var setting = MaterialSetting.FromRow(chosen, FocusTestForm.SafeMaxPower());
			var applied = MaterialApplication.For(setting, operation.Source, operation.SupportsKerf);
			row.Cells[2].Value = applied.Repeats;
			if (!grid.Columns[3].ReadOnly) row.Cells[3].Value = applied.Pass.PowerPercent;
			row.Cells[4].Value = applied.Pass.SpeedPercent;
			if (operation.SupportsKerf) row.Cells[7].Value = applied.KerfMm.ToString("0.###", CultureInfo.CurrentCulture);
			row.Cells[5].Value = "Apply to update";
			StatusChanged?.Invoke(setting.Name + " set on " + row.Cells[1].Value + ". " + applied.Warning);
		}

		private void PreviewSelected()
		{
			var operation = grid.CurrentRow?.Tag as JobOperation;
			if (operation == null || !operation.Enabled) return;
			host.PushJobView(new JobPlaybackView(core.LoadedFile, operation.Start, operation.End),
				"Playback · " + operation.Name, showBack: true);
		}

		internal static TimeSpan Offset(GrblFile file, int exclusiveEnd)
		{
			return exclusiveEnd == 0 ? TimeSpan.Zero : file[exclusiveEnd - 1].TimeOffset;
		}

		internal static string FormatTime(TimeSpan value)
		{
			return string.Format("{0:0}:{1:00}:{2:00}", Math.Floor(value.TotalHours), value.Minutes, value.Seconds);
		}

		private void AddButton(string text, Action action)
		{
			var button = new UserControls.GrblButton { Text = text, AutoSize = true };
			button.Click += (sender, args) => action();
			actions.Controls.Add(button);
		}

		private void MoveRow(int delta)
		{
			if (grid.CurrentRow == null || !grid.EndEdit()) return;
			int current = grid.CurrentRow.Index;
			int destination = current + delta;
			if (destination < 0 || destination >= grid.Rows.Count) return;
			var row = grid.Rows[current];
			grid.Rows.RemoveAt(current);
			grid.Rows.Insert(destination, row);
			grid.CurrentCell = row.Cells[1];
		}

		private void Apply()
		{
			if (!grid.EndEdit()) return;
			try
			{
				var operations = new List<JobOperation>();
				foreach (DataGridViewRow row in grid.Rows)
				{
					var operation = (JobOperation)row.Tag;
					int repeats = int.Parse(Convert.ToString(row.Cells[2].Value), CultureInfo.CurrentCulture);
					decimal power = decimal.Parse(Convert.ToString(row.Cells[3].Value), CultureInfo.CurrentCulture);
					decimal speed = decimal.Parse(Convert.ToString(row.Cells[4].Value), CultureInfo.CurrentCulture);
					var draft = operation.WithSettings(Convert.ToString(row.Cells[1].Value), repeats,
						new JobPass(power, speed), Convert.ToBoolean(row.Cells[0].Value));
					draft.RepeatContours = draft.SupportsKerf && Convert.ToBoolean(row.Cells[6].Value);
					string kerf = Convert.ToString(row.Cells[7].Value);
					draft.KerfMm = draft.SupportsKerf && !string.IsNullOrWhiteSpace(kerf) ? decimal.Parse(kerf, CultureInfo.CurrentCulture) : 0;
					if (draft.KerfMm < 0 || draft.KerfMm > 5) throw new ArgumentOutOfRangeException("Kerf", "Kerf must be between 0 and 5 mm.");
					operations.Add(draft);
				}
				grid.Enabled = actions.Enabled = false;
				UseWaitCursor = true;
				StatusChanged?.Invoke("Building operations...");
				core.RebuildOperations(operations, error =>
				{
					if (IsDisposed) return;
					UseWaitCursor = false;
					grid.Enabled = actions.Enabled = true;
					if (error == null)
					{
						int skipped = core.LoadedFile.Operations.Where(o => o.Enabled && o.KerfMm > 0).Sum(o => o.KerfSkipped);
						if (skipped > 0)
							StatusChanged?.Invoke(string.Format(CultureInfo.CurrentCulture,
								"Kerf left {0} contour(s) as drawn: open paths, holes smaller than the kerf, or paths that change speed or power partway.", skipped));
						CloseRequested?.Invoke(this, EventArgs.Empty);
					}
					else ShowError(error);
				});
			}
			catch (Exception ex)
			{
				UseWaitCursor = false;
				grid.Enabled = actions.Enabled = true;
				ShowError(ex);
			}
		}

		private void ShowError(Exception error)
		{
			StatusChanged?.Invoke(error.Message);
		}
	}
}
