using LaserGRBL.UserControls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools;

namespace LaserGRBL
{
	/// <summary>
	/// The modern layout: a top bar, a Project panel on the left, the original grid preview in the middle,
	/// a Process panel on the right and a bottom bar with the main action. It rearranges the existing controls
	/// rather than replacing them, so every feature still works; the classic layout stays one click away.
	/// </summary>
	public partial class MainForm
	{
		private enum ShellMode { Prepare, Run }

		internal const string ClassicLayoutKey = "UI.ClassicLayout";

		private bool shellApplied;
		private ShellMode shellMode = ShellMode.Prepare;
		private bool shellWasRunning;
		private readonly List<ShellLabel> shellLabels = new List<ShellLabel>();
		private readonly List<Control> shellSurfaces = new List<Control>();

		private ShellPanel topBar, bottomBar, projectPanel, processPanel, consolePanel;
		private ShellButton menuButton, modePrepare, modeRun, machineChip, settingsButton;
		private ShellButton consoleButton, statusChip, powerButton, speedButton, frameButton, pauseButton, mainButton, saveMenuButton;
		private ShellLabel fileLabel, fileDetail, estimateLabel;
		private ListBox operationsList;
		private TableLayoutPanel processPrepare, processRun;
		private ShellLabel jobSummary, passesText, checksText, runHeadline, runDetail;
		private ShellButton openButton, appendButton, materialButton, editPassesButton;
		private ContextMenuStrip mainMenu;

		private decimal? scannedMaxS;
		private bool? scannedUsesZ;
		private long lastChecks;

		internal static bool UseClassicLayout
		{
			get { return Settings.GetObject(ClassicLayoutKey, false); }
			set { Settings.SetObject(ClassicLayoutKey, value); }
		}

		private void ApplyModernShell()
		{
			if (UseClassicLayout || shellApplied) return;
			shellApplied = true;
			SuspendLayout();

			// The original pieces: the grid preview fills the middle; the connection and console column becomes a side drawer.
			splitContainer1.Panel1.Controls.Remove(ConnectionForm);
			splitContainer1.Panel1.Controls.Remove(JogForm);
			splitContainer1.Panel1Collapsed = true;
			MMn.Visible = false;
			StatusBar.Visible = false;

			BuildTopBar();
			BuildBottomBar();
			BuildProjectPanel();
			BuildProcessPanel();
			BuildConsolePanel();

			Controls.Add(consolePanel);
			Controls.Add(projectPanel);
			Controls.Add(processPanel);
			Controls.Add(bottomBar);
			Controls.Add(topBar);
			// Docking runs from the back: the bars span the full width, then the side panels, then the preview fills what's left.
			consolePanel.SendToBack();
			projectPanel.SendToBack();
			processPanel.SendToBack();
			bottomBar.SendToBack();
			topBar.SendToBack();
			splitContainer1.BringToFront();

			Core.OnFileLoaded += (elapsed, filename) => { if (IsHandleCreated) BeginInvoke(new Action(ShellOnFileLoaded)); };
			ApplyShellColors();
			ResumeLayout(true);
			ShellOnFileLoaded();
		}

		// ---------- Top bar ----------

		private void BuildTopBar()
		{
			topBar = NewPanel(DockStyle.Top, 44, AnchorStyles.Bottom);
			var row = NewRow();
			row.Padding = new Padding(8, 7, 8, 0);

			mainMenu = new ContextMenuStrip { Renderer = MMn.Renderer };
			foreach (ToolStripItem item in MMn.Items.Cast<ToolStripItem>().ToList())
				mainMenu.Items.Add(item); // moves the original menus, with all their handlers, into one dropdown
			menuButton = NewButton("Menu", ShellButtonStyle.Ghost, (s, e) => mainMenu.Show(menuButton, new Point(0, menuButton.Height)));
			menuButton.Suffix = "▾";
			var logo = AddLabel(row, "LaserGRBL", heading: true);
			logo.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
			logo.Margin = new Padding(4, 5, 10, 0);
			row.Controls.Add(menuButton);
			fileLabel = AddLabel(row, "No file loaded");
			fileLabel.Margin = new Padding(12, 6, 4, 0);
			fileDetail = AddLabel(row, "", muted: true);
			fileDetail.Margin = new Padding(4, 6, 4, 0);

			var right = NewRow(FlowDirection.RightToLeft);
			right.Dock = DockStyle.Right;
			right.AutoSize = true;
			right.Padding = new Padding(0, 7, 8, 0);
			settingsButton = NewButton("Settings", ShellButtonStyle.Ghost, (s, e) => settingsToolStripMenuItem_Click(s, e));
			machineChip = NewButton("No laser connected", ShellButtonStyle.Outline, (s, e) => OnMachineChip());
			machineChip.Suffix = "▾";
			right.Controls.Add(settingsButton);
			right.Controls.Add(machineChip);

			var modes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = new Padding(2), Margin = new Padding(0) };
			modePrepare = NewButton("Prepare", ShellButtonStyle.Segment, (s, e) => SetShellMode(ShellMode.Prepare));
			modeRun = NewButton("Run", ShellButtonStyle.Segment, (s, e) => SetShellMode(ShellMode.Run));
			modePrepare.Margin = modeRun.Margin = new Padding(0);
			modePrepare.Width = modeRun.Width = 84;
			modePrepare.Height = modeRun.Height = 26;
			modes.Controls.Add(modePrepare);
			modes.Controls.Add(modeRun);
			var center = new Panel { Dock = DockStyle.Fill };
			center.Controls.Add(modes);
			center.Resize += (s, e) => modes.Location = new Point(Math.Max(0, (center.Width - modes.Width) / 2), 7);
			shellSurfaces.Add(modes);

			topBar.Controls.Add(center);
			topBar.Controls.Add(right);
			topBar.Controls.Add(row);
			row.Dock = DockStyle.Left;
			row.AutoSize = true;
			center.BringToFront();
			shellSurfaces.AddRange(new Control[] { row, right, center });
		}

		private void OnMachineChip()
		{
			if (!Core.IsConnected && Core.MachineStatus != GrblCore.MacStatus.Connecting)
			{
				ShowConsole(true);
				MnConnect_Click(this, EventArgs.Empty);
			}
			else ShowConsole(!consolePanel.Visible);
		}

		// ---------- Bottom bar ----------

		private void BuildBottomBar()
		{
			bottomBar = NewPanel(DockStyle.Bottom, 48, AnchorStyles.Top);
			var left = NewRow();
			left.Dock = DockStyle.Left;
			left.AutoSize = true;
			left.Padding = new Padding(8, 9, 0, 0);
			consoleButton = NewButton("Console", ShellButtonStyle.Ghost, (s, e) => ShowConsole(!consolePanel.Visible));
			statusChip = NewButton("Offline", ShellButtonStyle.Ghost, null);
			statusChip.Cursor = Cursors.Default;
			statusChip.TabStop = false;
			left.Controls.Add(consoleButton);
			left.Controls.Add(statusChip);
			estimateLabel = AddLabel(left, "", muted: true);
			estimateLabel.Margin = new Padding(8, 6, 0, 0);

			var right = NewRow(FlowDirection.RightToLeft);
			right.Dock = DockStyle.Right;
			right.AutoSize = true;
			right.Padding = new Padding(0, 8, 8, 0);
			saveMenuButton = NewButton("▾", ShellButtonStyle.Accent, (s, e) => ShowSaveMenu());
			saveMenuButton.Width = 28;
			saveMenuButton.Margin = new Padding(1, 3, 3, 3);
			mainButton = NewButton("Start engraving", ShellButtonStyle.Accent, (s, e) => OnMainButton());
			mainButton.Margin = new Padding(3, 3, 0, 3);
			mainButton.Height = saveMenuButton.Height = 32;
			mainButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
			pauseButton = NewButton("Pause", ShellButtonStyle.Outline, (s, e) => OnPauseButton());
			frameButton = NewButton("Frame board", ShellButtonStyle.Outline, (s, e) => RunSafely(() => Core.FrameJob()));
			speedButton = NewButton("Speed 100%", ShellButtonStyle.Ghost, (s, e) => GetOvMenu().Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft));
			powerButton = NewButton("Power 100%", ShellButtonStyle.Ghost, (s, e) => GetOvMenu().Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft));
			pauseButton.Height = frameButton.Height = 32;
			right.Controls.AddRange(new Control[] { saveMenuButton, mainButton, pauseButton, frameButton, speedButton, powerButton });

			bottomBar.Controls.Add(right);
			bottomBar.Controls.Add(left);
			shellSurfaces.AddRange(new Control[] { left, right });
		}

		private void ShowSaveMenu()
		{
			var menu = new ContextMenuStrip { Renderer = MMn.Renderer };
			menu.Items.Add("Save G-code for SD card or USB...", null, (s, e) => MnSaveProgramClick(s, e)).Enabled = Core.HasProgram;
			menu.Items.Add("Save with options...", null, (s, e) => MnAdvancedSave_Click(s, e)).Enabled = Core.HasProgram;
			menu.Items.Add("Save project...", null, (s, e) => MnSaveProject_Click(s, e)).Enabled = Core.HasProgram;
			menu.Show(saveMenuButton, new Point(saveMenuButton.Width, 0), ToolStripDropDownDirection.AboveLeft);
		}

		private void OnMainButton()
		{
			if (Core.InProgram || Core.CanAbortProgram && Core.IsConnected && Core.MachineStatus != GrblCore.MacStatus.Idle)
				Core.AbortProgram();
			else if (Core.IsConnected)
				Core.RunProgram(this);
			else
				MnSaveProgramClick(this, EventArgs.Empty);
		}

		private void OnPauseButton()
		{
			if (Core.CanResumeHold) Core.CycleStartResume(false);
			else if (Core.CanFeedHold) Core.FeedHold(false);
		}

		// ---------- Project panel (left) ----------

		private void BuildProjectPanel()
		{
			projectPanel = NewPanel(DockStyle.Left, 248, AnchorStyles.Right);
			var table = NewColumn();
			table.Padding = new Padding(10, 8, 10, 8);

			table.Controls.Add(Heading("Project"));
			var fileRow = NewRow();
			openButton = NewButton("Open...", ShellButtonStyle.Outline, (s, e) => MnFileOpen_Click(s, e));
			appendButton = NewButton("Add file...", ShellButtonStyle.Outline, (s, e) => MnFileAppend_Click(s, e));
			fileRow.Controls.Add(openButton);
			fileRow.Controls.Add(appendButton);
			table.Controls.Add(fileRow);

			table.Controls.Add(Heading("Objects"));
			operationsList = new ListBox
			{
				DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, BorderStyle = BorderStyle.None, IntegralHeight = false,
				Height = 26 * 4, Width = 220, Margin = new Padding(0, 2, 0, 6), Font = new Font("Segoe UI", 9f)
			};
			operationsList.DrawItem += DrawOperation;
			operationsList.DoubleClick += (s, e) => ConnectionForm.OpenJobEditor();
			table.Controls.Add(operationsList);

			table.Controls.Add(Heading("Job"));
			var pass = ConnectionForm.PassControls;
			var edits = ConnectionForm.EditControls;
			pass.Parent?.Controls.Remove(pass);
			edits.Parent?.Controls.Remove(edits);
			pass.Dock = edits.Dock = DockStyle.None;
			pass.Width = edits.Width = 226;
			edits.Margin = new Padding(0, 4, 0, 0);
			table.Controls.Add(pass);
			table.Controls.Add(edits);

			table.Controls.Add(Heading("Material"));
			materialButton = NewButton("Material DB...", ShellButtonStyle.Outline, (s, e) => MnMaterialDB_Click(s, e));
			table.Controls.Add(materialButton);

			JogForm.Dock = DockStyle.Bottom;
			projectPanel.Controls.Add(table);
			projectPanel.Controls.Add(JogForm);
			table.BringToFront();
			shellSurfaces.Add(table);
		}

		private void DrawOperation(object sender, DrawItemEventArgs e)
		{
			if (e.Index < 0) return;
			bool selected = (e.State & DrawItemState.Selected) != 0;
			using (var back = new SolidBrush(selected ? ShellPalette.Selected : ShellPalette.Surface))
				e.Graphics.FillRectangle(back, e.Bounds);
			var item = operationsList.Items[e.Index] as string ?? "";
			int split = item.IndexOf('\t');
			string name = split < 0 ? item : item.Substring(0, split), detail = split < 0 ? "" : item.Substring(split + 1);
			var nameRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 70, e.Bounds.Height);
			TextRenderer.DrawText(e.Graphics, name, operationsList.Font, nameRect, ShellPalette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			var detailRect = new Rectangle(e.Bounds.Right - 70, e.Bounds.Y, 66, e.Bounds.Height);
			TextRenderer.DrawText(e.Graphics, detail, operationsList.Font, detailRect, ShellPalette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
		}

		// ---------- Process panel (right) ----------

		private void BuildProcessPanel()
		{
			processPanel = NewPanel(DockStyle.Right, 288, AnchorStyles.Left);

			processPrepare = NewColumn();
			processPrepare.Padding = new Padding(12, 8, 12, 8);
			processPrepare.Controls.Add(Heading("Process"));
			jobSummary = AddColumnLabel(processPrepare, "", muted: true);
			processPrepare.Controls.Add(Heading("Passes"));
			passesText = AddColumnLabel(processPrepare, "");
			editPassesButton = NewButton("Edit passes...", ShellButtonStyle.Outline, (s, e) => ConnectionForm.OpenJobEditor());
			processPrepare.Controls.Add(editPassesButton);
			processPrepare.Controls.Add(Heading("Before starting"));
			checksText = AddColumnLabel(processPrepare, "");

			processRun = NewColumn();
			processRun.Padding = new Padding(12, 8, 12, 8);
			processRun.Controls.Add(Heading("Running"));
			runHeadline = AddColumnLabel(processRun, "");
			runHeadline.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
			runDetail = AddColumnLabel(processRun, "", muted: true);
			processRun.Visible = false;

			processPanel.Controls.Add(processPrepare);
			processPanel.Controls.Add(processRun);
			shellSurfaces.AddRange(new Control[] { processPrepare, processRun });
		}

		// ---------- Console drawer ----------

		private void BuildConsolePanel()
		{
			consolePanel = NewPanel(DockStyle.Left, Math.Max(260, Settings.GetObject("MainForm Splitter Position", 280)), AnchorStyles.Right);
			ConnectionForm.Dock = DockStyle.Fill;
			consolePanel.Controls.Add(ConnectionForm);
			// Start with it open until the machine connects, so the connection settings are easy to find.
			consolePanel.Visible = true;
		}

		private void ShowConsole(bool show)
		{
			consolePanel.Visible = show;
			consoleButton.Active = show;
		}

		private void SetShellMode(ShellMode mode)
		{
			shellMode = mode;
			modePrepare.Active = mode == ShellMode.Prepare;
			modeRun.Active = mode == ShellMode.Run;
			projectPanel.Visible = mode == ShellMode.Prepare;
			processPrepare.Visible = mode == ShellMode.Prepare;
			processRun.Visible = mode == ShellMode.Run;
			RefreshShell();
		}

		// ---------- Refresh ----------

		private void ShellOnFileLoaded()
		{
			if (!shellApplied) return;
			scannedMaxS = null;
			scannedUsesZ = null;
			var file = Core.LoadedFile;
			var commands = file.ToList();
			Task.Factory.StartNew(() =>
			{
				decimal maxS;
				bool usesZ;
				PreflightCheck.Scan(commands, out maxS, out usesZ);
				return Tuple.Create(maxS, usesZ);
			}).ContinueWith(task =>
			{
				if (task.Exception != null || IsDisposed || !IsHandleCreated) return;
				BeginInvoke(new Action(() =>
				{
					scannedMaxS = task.Result.Item1;
					scannedUsesZ = task.Result.Item2;
					lastChecks = 0;
					RefreshShell();
				}));
			});

			operationsList.BeginUpdate();
			operationsList.Items.Clear();
			if (Core.HasJobOperations)
				foreach (var operation in file.Operations)
					operationsList.Items.Add(operation.Name + "\t" + (operation.Enabled ? (operation.Repeats > 1 ? "×" + operation.Repeats : "") : "off"));
			else if (Core.HasProgram)
				operationsList.Items.Add(System.IO.Path.GetFileName(Settings.GetObject<string>("Core.LastOpenFile", "Job")) + "\t");
			operationsList.EndUpdate();
			RefreshShell();
		}

		private void RefreshShell()
		{
			if (!shellApplied) return;
			var c = CultureInfo.CurrentCulture;
			var file = Core.LoadedFile;
			bool hasProgram = Core.HasProgram;
			bool connected = Core.IsConnected;
			bool running = Core.InProgram;

			if (running && !shellWasRunning) SetShellMode(ShellMode.Run);
			else if (!running && shellWasRunning) SetShellMode(ShellMode.Prepare);
			shellWasRunning = running;
			modePrepare.Active = shellMode == ShellMode.Prepare;
			modeRun.Active = shellMode == ShellMode.Run;

			string name = hasProgram ? System.IO.Path.GetFileName(Settings.GetObject<string>("Core.LastOpenFile", "")) : "";
			fileLabel.Text = hasProgram && name.Length > 0 ? name : hasProgram ? "Untitled job" : "No file loaded";
			fileDetail.Text = hasProgram ? string.Format(c, "{0:N0} lines", file.Count) : "";

			var status = Core.MachineStatus;
			machineChip.Text = connected ? Core.Type + " · " + GrblCore.TranslateEnum(status)
				: status == GrblCore.MacStatus.Connecting ? "Connecting..." : "No laser connected · Connect";
			machineChip.Dot = connected ? ShellPalette.Good : status == GrblCore.MacStatus.Connecting ? ShellPalette.Warn : ShellPalette.Muted;
			machineChip.FitWidth(12);
			if (connected && consolePanel.Visible && !consoleButton.Active) consoleButton.Active = true;

			statusChip.Text = connected ? GrblCore.TranslateEnum(status) : "Offline";
			statusChip.Dot = !connected ? ShellPalette.Muted : status == GrblCore.MacStatus.Alarm ? ShellPalette.Bad
				: status == GrblCore.MacStatus.Idle ? ShellPalette.Good : ShellPalette.Warn;
			statusChip.FitWidth(8);
			consoleButton.Active = consolePanel.Visible;

			TimeSpan estimate = running ? Core.ProjectedTime : file.EstimatedTime;
			estimateLabel.Text = hasProgram ? (running ? "Projected " : "Est. ") + Utils.TimeSpanToString(estimate, Utils.TimePrecision.Second, Utils.TimePrecision.Second, " ", true) : "";

			powerButton.Visible = speedButton.Visible = connected;
			powerButton.Text = "Power " + Core.OverrideS + "%";
			speedButton.Text = "Speed " + Core.OverrideG1 + "%";
			powerButton.FitWidth(8);
			speedButton.FitWidth(8);

			frameButton.Visible = connected && !running;
			frameButton.Enabled = Core.CanSendFile;
			frameButton.FitWidth();
			pauseButton.Visible = connected && (Core.CanFeedHold || Core.CanResumeHold);
			pauseButton.Text = Core.CanResumeHold ? "Resume" : "Pause";
			pauseButton.FitWidth();

			if (running)
			{
				mainButton.Text = "Stop";
				mainButton.ButtonStyle = ShellButtonStyle.Danger;
				mainButton.Enabled = Core.CanAbortProgram;
			}
			else if (connected)
			{
				mainButton.Text = "▶  Start engraving";
				mainButton.ButtonStyle = ShellButtonStyle.Accent;
				mainButton.Enabled = Core.CanSendFile;
			}
			else
			{
				mainButton.Text = "Save to SD card";
				mainButton.ButtonStyle = ShellButtonStyle.Accent;
				mainButton.Enabled = hasProgram;
			}
			saveMenuButton.Visible = !running;
			saveMenuButton.Enabled = hasProgram;
			mainButton.FitWidth(18);

			bool canEdit = Core.CanLoadNewFile;
			openButton.Enabled = appendButton.Enabled = canEdit;
			editPassesButton.Enabled = Core.CanConfigurePasses && hasProgram && !Core.JobEditorOpen;

			if (shellMode == ShellMode.Prepare)
			{
				var range = file.Range.DrawingRange;
				jobSummary.Text = !hasProgram ? "Open an image, SVG or G-code file to start."
					: (range.ValidRange ? string.Format(c, "{0:0.##} × {1:0.##} mm at X {2:0.##} Y {3:0.##}\n", range.X.Max - range.X.Min, range.Y.Max - range.Y.Min, range.X.Min, range.Y.Min) : "")
						+ string.Format(c, "{0:N0} lines · {1}", file.Count, Utils.TimeSpanToString(file.EstimatedTime, Utils.TimePrecision.Second, Utils.TimePrecision.Second, " ", true));
				passesText.Text = DescribePasses();
				if (Tools.HiResTimer.TotalMilliseconds - lastChecks > 2000)
				{
					lastChecks = Tools.HiResTimer.TotalMilliseconds;
					checksText.Text = DescribeChecks();
				}
			}
			else
			{
				TimeSpan elapsed = Core.ProgramTime, projected = Core.ProjectedTime;
				double percent = projected.TotalSeconds > 0 ? Math.Min(100, elapsed.TotalSeconds * 100 / projected.TotalSeconds) : 0;
				runHeadline.Text = running ? string.Format(c, "{0:0}%", percent) : "Not running";
				var lines = new List<string>();
				if (running)
				{
					lines.Add("Elapsed " + Utils.TimeSpanToString(elapsed, Utils.TimePrecision.Second, Utils.TimePrecision.Second, " ", true));
					lines.Add("Remaining " + Utils.TimeSpanToString(projected > elapsed ? projected - elapsed : TimeSpan.Zero, Utils.TimePrecision.Second, Utils.TimePrecision.Second, " ", true));
					if (Core.Passes != null) lines.Add(string.Format(c, "Pass {0} of {1}: {2}", Core.CurrentPassNumber, Core.Passes.Count, Core.CurrentPassSettings));
					else if (Core.LoopCount > 1) lines.Add(string.Format(c, "{0} repeats left", Core.LoopCount));
					lines.Add("Progress is saved, so the job can resume after a crash.");
				}
				else lines.Add(hasProgram ? "Press Start engraving to run the job." : "Load a job first.");
				runDetail.Text = string.Join("\n", lines);
			}
		}

		private string DescribePasses()
		{
			var c = CultureInfo.CurrentCulture;
			if (!Core.HasProgram) return "—";
			if (Core.HasJobOperations)
				return string.Join("\n", Core.LoadedFile.Operations.Select((o, i) => string.Format(c, "{0:00}  {1}{2}{3}",
					i + 1, o.Name, o.Repeats > 1 ? " ×" + o.Repeats : "", o.Enabled ? (o.Settings != null ? " · " + o.Settings : "") : " · off")));
			if (Core.Passes != null)
				return string.Join("\n", Enumerable.Range(0, Core.Passes.Count).Select(i => string.Format(c, "{0:00}  {1}", i + 1, Core.Passes[i])));
			return Core.LoopCount > 1 ? string.Format(c, "Same settings, {0} times", Core.LoopCount) : "One pass at the file's settings";
		}

		private string DescribeChecks()
		{
			if (!Core.HasProgram) return "—";
			if (!scannedMaxS.HasValue) return "Checking...";
			var findings = PreflightCheck.Run(Core.BuildPreflightInput(scannedMaxS, scannedUsesZ));
			var lines = findings.Select(f => f.ToString()).ToList();
			if (!Core.IsConnected) lines.Add("–  Machine checks run once a laser is connected.");
			return lines.Count == 0 ? "✓  Everything looks ready." : string.Join("\n", lines);
		}

		private void ApplyShellColors()
		{
			if (!shellApplied) return;
			foreach (var panel in new Control[] { topBar, bottomBar, projectPanel, processPanel, consolePanel })
			{
				panel.BackColor = ShellPalette.Surface;
				panel.ForeColor = ShellPalette.Text;
				panel.Invalidate(true);
			}
			foreach (var surface in shellSurfaces) surface.BackColor = ShellPalette.Surface;
			foreach (var label in shellLabels) label.ApplyColors();
			operationsList.BackColor = ShellPalette.Surface;
			operationsList.ForeColor = ShellPalette.Text;
			mainMenu.BackColor = ShellPalette.Surface;
			mainMenu.ForeColor = ShellPalette.Text;
		}

		// ---------- Small builders ----------

		private ShellPanel NewPanel(DockStyle dock, int size, AnchorStyles lines)
		{
			var panel = new ShellPanel { Dock = dock, Lines = lines };
			if (dock == DockStyle.Top || dock == DockStyle.Bottom) panel.Height = size;
			else panel.Width = size;
			return panel;
		}

		private FlowLayoutPanel NewRow(FlowDirection direction = FlowDirection.LeftToRight)
		{
			return new FlowLayoutPanel { FlowDirection = direction, WrapContents = false, AutoSize = true, Margin = new Padding(0), Padding = new Padding(0) };
		}

		private TableLayoutPanel NewColumn()
		{
			return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
		}

		private ShellButton NewButton(string text, ShellButtonStyle style, EventHandler click)
		{
			var button = new ShellButton { Text = text, ButtonStyle = style };
			if (click != null) button.Click += click;
			button.FitWidth();
			return button;
		}

		private ShellLabel AddLabel(Control parent, string text, bool heading = false, bool muted = false)
		{
			var label = new ShellLabel(text, heading, muted);
			shellLabels.Add(label);
			parent.Controls.Add(label);
			return label;
		}

		private ShellLabel AddColumnLabel(TableLayoutPanel column, string text, bool muted = false)
		{
			var label = AddLabel(column, text, muted: muted);
			label.MaximumSize = new Size(260, 0);
			label.Margin = new Padding(0, 2, 0, 8);
			return label;
		}

		private ShellLabel Heading(string text)
		{
			var label = new ShellLabel(text, heading: true) { Margin = new Padding(0, 10, 0, 4) };
			shellLabels.Add(label);
			return label;
		}
	}
}
