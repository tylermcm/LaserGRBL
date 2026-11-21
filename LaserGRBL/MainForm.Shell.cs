using LaserGRBL.RasterConverter;
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
	/// The modern layout (mockup "J"): top bar, icon rail, Project panel, LaserGRBL's own grid preview,
	/// Process panel and bottom bar. Logic stays in the existing forms and Core; this file only presents it.
	/// Menu → Job → Classic layout switches back after a restart.
	/// </summary>
	public partial class MainForm
	{
		private enum ShellMode { Design, Prepare, Run }

		internal const string ClassicLayoutKey = "UI.ClassicLayout";
		private const int TopHeight = 44, RailWidth = 44, LeftWidth = 224, RightWidth = 288, BottomHeight = 48;

		private bool shellApplied, shellWasRunning, shellBusy;
		private ShellMode shellMode = ShellMode.Prepare;

		// chrome
		private ShellPanel topBar, rail, bottomBar, projectPanel, processPanel, consoleDrawer;
		private ShellButton menuButton, modeDesign, modePrepare, modeRun, machineChip, settingsButton;
		private ShellButton railLayers, railOpen, railMaterial, railMachine;
		private ShellLabel crumbFolder, crumbFile, crumbState;
		private ContextMenuStrip mainMenu;

		// bottom bar
		private ShellButton consoleButton, statusChip, powerButton, speedButton, frameButton, pauseButton, mainButton, saveArrow, connectButton;
		private ShellLabel estimateLabel, offlineNote;

		// project panel
		private StackPanel projectStack, projectFooter;
		private SectionHeader objectsHeader, selectionHeader;
		private readonly List<Control> projectRows = new List<Control>();
		private readonly List<Control> objectRows = new List<Control>();
		private FieldBox fieldX, fieldY, fieldW, fieldH;
		private Control fieldGrid, rotateRow, imageCaps, imageTools;
		private CheckBox lockRatio;
		private ShellLabel rotateLabel;
		private SettingRow methodRow;
		private FlatSlider brightness, contrast, whiteClip;
		private FlatCombo materialCombo;
		private ShellLabel headLabel;
		private ShellButton moveButton;
		private int selectedObject;

		// process panel
		private StackPanel processStack, runStack;
		private FlatCombo presetCombo;
		private InfoCard boardCard;
		private SectionHeader passesHeader;
		private readonly List<PassCard> passCards = new List<PassCard>();
		private int selectedPass;
		private SettingRow laserModeRow, intervalRow, overscanRow, repeatsRow;
		private readonly List<CheckRow> checkRows = new List<CheckRow>();
		private Control checksAnchor;
		private ShellLabel processFooter, runPercent, runDetail;
		private ProgressStrip runProgress;

		private const long ConnectTimeoutMs = 12000;
		private long connectingSince, connectFailedUntil;
		private string connectFailedPort;

		private decimal? scannedMaxS;
		private bool? scannedUsesZ;
		private long lastChecks;
		private bool refreshingContent;

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

			// Keep the old left column alive but out of sight: the new panels drive it.
			splitContainer1.Panel1.Controls.Remove(JogForm);
			splitContainer1.Panel1Collapsed = true;
			ConnectionForm.Visible = false;
			PreviewForm.ShowButtonBar(false);
			MMn.Visible = false;
			StatusBar.Visible = false;
			BackColor = ShellPalette.Window;

			BuildTopBar();
			BuildRail();
			BuildBottomBar();
			BuildConsoleDrawer();
			BuildProjectPanel();
			BuildProcessPanel();

			foreach (var c in new Control[] { consoleDrawer, projectPanel, processPanel, rail, bottomBar, topBar }) Controls.Add(c);
			// Docking runs from the back of the z-order: the bars span the width, then the rail and panels, then the grid fills the rest.
			projectPanel.SendToBack();
			rail.SendToBack();
			processPanel.SendToBack();
			consoleDrawer.SendToBack();
			bottomBar.SendToBack();
			topBar.SendToBack();
			splitContainer1.BringToFront();

			Core.OnFileLoaded += (elapsed, filename) => { if (IsHandleCreated) BeginInvoke(new Action(ShellOnFileLoaded)); };
			ResumeLayout(true);
			SetShellMode(ShellMode.Prepare);
			ShellOnFileLoaded();
		}

		// =====================================================================
		// Top bar
		// =====================================================================

		private void BuildTopBar()
		{
			topBar = NewBar(DockStyle.Top, TopHeight, AnchorStyles.Bottom);

			mainMenu = new ContextMenuStrip { Renderer = MMn.Renderer };
			foreach (ToolStripItem item in MMn.Items.Cast<ToolStripItem>().ToList()) mainMenu.Items.Add(item);

			var logo = new ShellButton { Text = "LaserGRBL", Icon = ShellIcon.Logo, ButtonStyle = ShellButtonStyle.Ghost, Font = new Font("Segoe UI Semibold", 10f), Height = 30 };
			logo.FitWidth(6);
			logo.Location = new Point(8, 7);
			logo.Click += (s, e) => mainMenu.Show(logo, new Point(0, logo.Height));
			logo.AccessibleName = "Menu";
			menuButton = logo;

			var divider = new Panel { BackColor = ShellPalette.Border, Size = new Size(1, 18), Location = new Point(logo.Right + 10, 13) };
			crumbFolder = new ShellLabel("", ShellPalette.UiFont, ShellPalette.Muted) { AutoSize = true, Location = new Point(divider.Right + 12, 14) };
			crumbFile = new ShellLabel("No file loaded", ShellPalette.UiBold) { AutoSize = true, Location = new Point(divider.Right + 12, 14) };
			crumbState = new ShellLabel("", ShellPalette.UiFont, ShellPalette.Muted) { AutoSize = true, Location = new Point(divider.Right + 12, 14) };

			var pill = new PillPanel { Size = new Size(3 * 72 + 6, 30) };
			modeDesign = Segment("Design", ShellMode.Design);
			modePrepare = Segment("Prepare", ShellMode.Prepare);
			modeRun = Segment("Run", ShellMode.Run);
			modeDesign.Location = new Point(3, 2);
			modePrepare.Location = new Point(75, 2);
			modeRun.Location = new Point(147, 2);
			pill.Controls.AddRange(new Control[] { modeDesign, modePrepare, modeRun });

			settingsButton = new ShellButton { Icon = ShellIcon.Gear, ButtonStyle = ShellButtonStyle.Ghost, Size = new Size(30, 30), AccessibleName = "Settings" };
			settingsButton.Click += (s, e) => settingsToolStripMenuItem_Click(s, e);
			machineChip = new ShellButton { ButtonStyle = ShellButtonStyle.Outline, TrailingIcon = ShellIcon.Chevron, Height = 30, Dot = ShellPalette.Faint };
			machineChip.Click += (s, e) => ShowConnectionPopup(machineChip);

			topBar.Controls.AddRange(new Control[] { logo, divider, crumbFolder, crumbFile, crumbState, pill, machineChip, settingsButton });
			topBar.Resize += (s, e) => LayoutTopBar(pill);
		}

		private void LayoutTopBar(Control pill)
		{
			pill.Location = new Point((topBar.Width - pill.Width) / 2, 7);
			settingsButton.Location = new Point(topBar.Width - settingsButton.Width - 10, 7);
			machineChip.Location = new Point(settingsButton.Left - machineChip.Width - 8, 7);
			crumbFile.Left = crumbFolder.Text.Length > 0 ? crumbFolder.Right + 2 : crumbFolder.Left;
			crumbState.Left = crumbFile.Right + 8;
		}

		private ShellButton Segment(string text, ShellMode mode)
		{
			var b = new ShellButton { Text = text, ButtonStyle = ShellButtonStyle.Segment, Size = new Size(70, 26), Margin = Padding.Empty, Radius = 5 };
			b.Click += (s, e) => OnModeClicked(mode);
			return b;
		}

		private void OnModeClicked(ShellMode mode)
		{
			CloseSideEditors();
			SetShellMode(mode);
		}

		/// <summary>
		/// Closes the older full-screen editors (passes, operations, playback, camera) so a mode tab always takes you there.
		/// The image converter is left alone while it is generating.
		/// </summary>
		private void CloseSideEditors()
		{
			var editor = PreviewForm.JobEditor;
			if (editor is JobPassEditor || editor is JobOperationsEditor || editor is JobPlaybackView || editor is CameraView || editor is JobPlacementView)
				PreviewForm.CloseJobEditor();
		}

		// =====================================================================
		// Rail
		// =====================================================================

		private void BuildRail()
		{
			rail = NewBar(DockStyle.Left, RailWidth, AnchorStyles.Right);
			railLayers = RailButton(ShellIcon.Layers, "Project panel", 8, (s, e) => { bool show = !railLayers.Active; projectPanel.Visible = show; railLayers.Active = show; });
			railOpen = RailButton(ShellIcon.Folder, "Open file", 44, (s, e) => MnFileOpen_Click(s, e));
			railMaterial = RailButton(ShellIcon.Pencil, "Material database", 80, (s, e) => MnMaterialDB_Click(s, e));
			railMachine = RailButton(ShellIcon.Console, "Console", 116, (s, e) => ShowConsole(!consoleDrawer.Visible));
			railLayers.ButtonStyle = ShellButtonStyle.Segment;
			railLayers.Active = true;
		}

		private ShellButton RailButton(ShellIcon icon, string name, int top, EventHandler click)
		{
			var b = new ShellButton { Icon = icon, ButtonStyle = ShellButtonStyle.Ghost, Size = new Size(32, 32), Location = new Point(6, top), AccessibleName = name, Radius = 7 };
			b.Click += click;
			rail.Controls.Add(b);
			new ToolTip().SetToolTip(b, name);
			return b;
		}

		// =====================================================================
		// Bottom bar and console
		// =====================================================================

		private void BuildBottomBar()
		{
			bottomBar = NewBar(DockStyle.Bottom, BottomHeight, AnchorStyles.Top);
			consoleButton = new ShellButton { Text = "Console", Icon = ShellIcon.Console, ButtonStyle = ShellButtonStyle.Ghost, Location = new Point(8, 10) };
			consoleButton.FitWidth(8);
			consoleButton.Click += (s, e) => ShowConsole(!consoleDrawer.Visible);
			var divider = new Panel { BackColor = ShellPalette.Border, Size = new Size(1, 20), Location = new Point(consoleButton.Right + 8, 14) };
			statusChip = new ShellButton { ButtonStyle = ShellButtonStyle.Ghost, Location = new Point(divider.Right + 6, 10), Cursor = Cursors.Default, TabStop = false };
			estimateLabel = new ShellLabel("", ShellPalette.UiFont, ShellPalette.TextSoft) { AutoSize = true, Location = new Point(statusChip.Right + 8, 16) };
			offlineNote = new ShellLabel("Save the job to run it from the machine's SD card or USB.", ShellPalette.UiFont, ShellPalette.Muted) { AutoSize = true };

			saveArrow = new ShellButton { Icon = ShellIcon.Chevron, ButtonStyle = ShellButtonStyle.Accent, Size = new Size(26, 32), AccessibleName = "More ways to save", Radius = 6 };
			saveArrow.Click += (s, e) => ShowSaveMenu();
			mainButton = new ShellButton { ButtonStyle = ShellButtonStyle.Accent, Height = 32, Font = new Font("Segoe UI Semibold", 9.5f) };
			mainButton.Click += (s, e) => OnMainButton();
			connectButton = new ShellButton { Text = "Connect laser", ButtonStyle = ShellButtonStyle.Outline, Height = 32 };
			connectButton.FitWidth();
			connectButton.Click += (s, e) => ShowConnectionPopup(connectButton);
			pauseButton = new ShellButton { Text = "Pause", ButtonStyle = ShellButtonStyle.Outline, Height = 32 };
			pauseButton.Click += (s, e) => OnPauseButton();
			frameButton = new ShellButton { Text = "Frame board", Icon = ShellIcon.Frame, ButtonStyle = ShellButtonStyle.Outline, Height = 32 };
			frameButton.FitWidth();
			frameButton.Click += (s, e) => RunSafely(() => Core.FrameJob());
			powerButton = new ShellButton { ButtonStyle = ShellButtonStyle.Ghost, Height = 30, TrailingIcon = ShellIcon.Chevron };
			speedButton = new ShellButton { ButtonStyle = ShellButtonStyle.Ghost, Height = 30, TrailingIcon = ShellIcon.Chevron };
			powerButton.Click += (s, e) => GetOvMenu().Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
			speedButton.Click += (s, e) => GetOvMenu().Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);

			// Connecting lives in one place, the machine chip at the top right.
			connectButton.Visible = false;
			bottomBar.Controls.AddRange(new Control[] { consoleButton, divider, statusChip, estimateLabel, offlineNote, powerButton, speedButton, frameButton, pauseButton, mainButton, saveArrow });
			bottomBar.Resize += (s, e) => LayoutBottomBar();
		}

		private void LayoutBottomBar()
		{
			int x = bottomBar.Width - 10;
			Action<Control, int> place = (c, gap) =>
			{
				if (!c.Visible) return;
				x -= c.Width;
				c.Location = new Point(x, (BottomHeight - c.Height) / 2);
				x -= gap;
			};
			place(saveArrow, 1);
			place(mainButton, 10);
			place(connectButton, 8);
			place(pauseButton, 8);
			place(frameButton, 12);
			place(speedButton, 4);
			place(powerButton, 12);
			if (offlineNote.Visible)
			{
				x -= offlineNote.PreferredWidth;
				offlineNote.Location = new Point(Math.Max(estimateLabel.Right + 16, x), 16);
			}
			estimateLabel.Left = statusChip.Right + 6;
		}

		private void BuildConsoleDrawer()
		{
			consoleDrawer = NewBar(DockStyle.Bottom, 220, AnchorStyles.Top);
			var console = ConnectionForm.ConsoleControls;
			console.Parent?.Controls.Remove(console);
			console.Dock = DockStyle.Fill;
			consoleDrawer.Padding = new Padding(8, 6, 8, 6);
			consoleDrawer.Controls.Add(console);
			consoleDrawer.Visible = false;
			ConnectionForm.StyleConsole(ShellPalette.Window, ShellPalette.Card, ShellPalette.Text, ShellPalette.Muted);
		}

		private void ShowConsole(bool show)
		{
			consoleDrawer.Visible = show;
			consoleButton.Active = show;
			railMachine.Active = show;
		}

		private void ShowConnectionPopup(Control anchor)
		{
			var panel = new StackPanel { Width = 260, Padding = new Padding(12, 10, 12, 12), BackColor = ShellPalette.Surface };
			panel.Controls.Add(new ShellLabel("Laser connection", ShellPalette.Heading));
			var port = new FlatCombo { Height = 26 };
			var baud = new FlatCombo { Height = 26 };
			if (ConnectionForm.UsesSerialPort)
			{
				ConnectionForm.RefreshPorts();
				port.Items.AddRange(ConnectionForm.PortNames);
				if (port.Items.Contains(ConnectionForm.ConnectionAddress)) port.SelectedItem = ConnectionForm.ConnectionAddress;
				else if (port.Items.Count > 0) port.SelectedIndex = 0;
				baud.Items.AddRange(ConnectionForm.BaudRates);
				baud.SelectedItem = ConnectionForm.SelectedBaud;
				panel.Controls.Add(new ShellLabel("Port", ShellPalette.Small, ShellPalette.Muted) { Margin = new Padding(0, 8, 0, 0) });
				panel.Controls.Add(port);
				panel.Controls.Add(new ShellLabel("Baud rate", ShellPalette.Small, ShellPalette.Muted) { Margin = new Padding(0, 6, 0, 0) });
				panel.Controls.Add(baud);
			}
			else panel.Controls.Add(new ShellLabel("Address: " + ConnectionForm.ConnectionAddress, ShellPalette.UiFont, ShellPalette.Muted) { Margin = new Padding(0, 8, 0, 0) });
			bool connected = Core.IsConnected || Core.MachineStatus == GrblCore.MacStatus.Connecting;
			var go = new ShellButton { Text = connected ? "Disconnect" : "Connect", ButtonStyle = connected ? ShellButtonStyle.Outline : ShellButtonStyle.Accent, Height = 32, Margin = new Padding(0, 12, 0, 0) };
			var more = new ShellButton { Text = "Connection settings and console", ButtonStyle = ShellButtonStyle.Link, Height = 26, Margin = new Padding(0, 4, 0, 0) };
			panel.Controls.Add(go);
			panel.Controls.Add(more);
			panel.Height = panel.ContentHeight + 8;

			var host = new ToolStripControlHost(panel) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = panel.Size };
			var drop = new ToolStripDropDown { Padding = new Padding(1), BackColor = ShellPalette.Border };
			drop.Items.Add(host);
			go.Click += (s, e) =>
			{
				if (ConnectionForm.UsesSerialPort && !connected)
				{
					if (port.SelectedItem != null) ConnectionForm.SelectPort(port.SelectedItem.ToString());
					if (baud.SelectedItem != null) ConnectionForm.SelectBaud(baud.SelectedItem);
				}
				drop.Close();
				ConnectionForm.ToggleConnection();
				RefreshShell();
			};
			more.Click += (s, e) => { drop.Close(); ShowClassicConnection(); };
			drop.Show(anchor, new Point(anchor.Width - panel.Width - 2, anchor.Height + 2));
		}

		/// <summary>The full connection form (protocol, address, file and loop controls) in a window, for anything the popup doesn't cover.</summary>
		private void ShowClassicConnection()
		{
			var form = new Form { Text = "Connection", Size = new Size(320, 520), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, BackColor = ColorScheme.FormBackColor, FormBorderStyle = FormBorderStyle.SizableToolWindow };
			ConnectionForm.Parent?.Controls.Remove(ConnectionForm);
			ConnectionForm.Visible = true;
			ConnectionForm.Dock = DockStyle.Fill;
			form.Controls.Add(ConnectionForm);
			form.FormClosing += (s, e) =>
			{
				form.Controls.Remove(ConnectionForm);
				ConnectionForm.Visible = false;
				splitContainer1.Panel1.Controls.Add(ConnectionForm);
			};
			form.Show(this);
		}

		private void ShowSaveMenu()
		{
			var menu = new ContextMenuStrip { Renderer = MMn.Renderer };
			menu.Items.Add("Save G-code for SD card or USB...", null, (s, e) => MnSaveProgramClick(s, e)).Enabled = Core.HasProgram;
			menu.Items.Add("Save with options...", null, (s, e) => MnAdvancedSave_Click(s, e)).Enabled = Core.HasProgram;
			menu.Items.Add("Save project...", null, (s, e) => MnSaveProject_Click(s, e)).Enabled = Core.HasProgram;
			menu.Show(saveArrow, new Point(saveArrow.Width, 0), ToolStripDropDownDirection.AboveLeft);
		}

		private void OnMainButton()
		{
			if (Core.InProgram) Core.AbortProgram();
			else if (Core.IsConnected) Core.RunProgram(this);
			else MnSaveProgramClick(this, EventArgs.Empty);
		}

		private void OnPauseButton()
		{
			if (Core.CanResumeHold) Core.CycleStartResume(false);
			else if (Core.CanFeedHold) Core.FeedHold(false);
		}

		// =====================================================================
		// Project panel (left)
		// =====================================================================

		private void BuildProjectPanel()
		{
			projectPanel = NewBar(DockStyle.Left, LeftWidth, AnchorStyles.Right);
			projectFooter = new StackPanel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(8, 8, 8, 8) };
			projectFooter.Paint += (s, e) => { using (var p = new Pen(ShellPalette.Border)) e.Graphics.DrawLine(p, 0, 0, projectFooter.Width, 0); };
			projectStack = new StackPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8, 2, 8, 8) };

			projectStack.Controls.Add(new SectionHeader("Project", ShellIcon.Plus, (s, e) => ShowProjectMenu(s as Control), "Add a file") { Margin = new Padding(4, 6, 0, 2) });
			// board rows are inserted after the header on refresh
			projectStack.Controls.Add(Divider());
			objectsHeader = new SectionHeader("Objects") { Margin = new Padding(4, 2, 0, 2) };
			projectStack.Controls.Add(objectsHeader);
			// object rows inserted after the Objects header
			projectStack.Controls.Add(Divider());
			selectionHeader = new SectionHeader("Selection") { Margin = new Padding(4, 2, 0, 4) };
			projectStack.Controls.Add(selectionHeader);

			fieldX = new FieldBox("X", "X position in millimetres");
			fieldY = new FieldBox("Y", "Y position in millimetres");
			fieldW = new FieldBox("W", "Width in millimetres");
			fieldH = new FieldBox("H", "Height in millimetres");
			foreach (var f in new[] { fieldX, fieldY, fieldW, fieldH }) f.Committed += (s, e) => ApplyGeometry(s as FieldBox);
			fieldGrid = new GridOf2(fieldX, fieldY, fieldW, fieldH) { Height = 62, Margin = new Padding(4, 0, 0, 4) };
			projectStack.Controls.Add(fieldGrid);

			rotateRow = new Panel { Height = 26, Margin = new Padding(4, 0, 0, 2), BackColor = ShellPalette.Surface };
			rotateLabel = new ShellLabel("Rotate  0°", ShellPalette.UiFont, ShellPalette.TextSoft) { AutoSize = true, Location = new Point(0, 5) };
			lockRatio = new CheckBox { Text = "Lock ratio", Checked = true, AutoSize = true, ForeColor = ShellPalette.TextSoft, BackColor = ShellPalette.Surface, FlatStyle = FlatStyle.Flat, Location = new Point(96, 4), Font = ShellPalette.UiFont };
			lockRatio.FlatAppearance.BorderColor = ShellPalette.BorderStrong;
			lockRatio.FlatAppearance.CheckedBackColor = ShellPalette.Accent;
			rotateRow.Controls.Add(rotateLabel);
			rotateRow.Controls.Add(lockRatio);
			projectStack.Controls.Add(rotateRow);

			imageCaps = new ShellLabel("IMAGE", ShellPalette.Small, ShellPalette.Muted) { Margin = new Padding(4, 10, 0, 2) };
			projectStack.Controls.Add(imageCaps);
			methodRow = new SettingRow("Method") { Margin = new Padding(4, 0, 0, 0), Clickable = true };
			methodRow.Click += (s, e) => { if (Core.LoadedFile.RasterSource != null) ConnectionForm.EditImage(); };
			projectStack.Controls.Add(methodRow);
			brightness = new FlatSlider("Brightness", 40, 160, 100) { Margin = new Padding(4, 1, 0, 1), Format = v => (v - 100).ToString("+0;-0;0") };
			contrast = new FlatSlider("Contrast", 40, 160, 100) { Margin = new Padding(4, 1, 0, 1), Format = v => (v - 100).ToString("+0;-0;0") };
			whiteClip = new FlatSlider("White clip", 0, 20, 5) { Margin = new Padding(4, 1, 0, 1) };
			brightness.Committed += (s, e) => ApplyImage(d => d.Brightness = brightness.Value, "GrayScaleConversion.Parameters.Brightness", brightness.Value);
			contrast.Committed += (s, e) => ApplyImage(d => d.Contrast = contrast.Value, "GrayScaleConversion.Parameters.Contrast", contrast.Value);
			whiteClip.Committed += (s, e) => ApplyImage(d => d.WhiteClip = whiteClip.Value, "GrayScaleConversion.Parameters.WhiteClip", whiteClip.Value);
			projectStack.Controls.Add(brightness);
			projectStack.Controls.Add(contrast);
			projectStack.Controls.Add(whiteClip);
			imageTools = new FlowLayoutPanel { Height = 30, Margin = new Padding(0, 6, 0, 0), BackColor = ShellPalette.Surface, WrapContents = false };
			foreach (var tool in new[] {
				Tool("Edit image...", (s, e) => ConnectionForm.EditImage()),
				Tool("Undo", (s, e) => ConnectionForm.UndoImageEdit()),
				Tool("Redo", (s, e) => ConnectionForm.RedoImageEdit()) })
				imageTools.Controls.Add(tool);
			projectStack.Controls.Add(imageTools);
			var viewTools = new FlowLayoutPanel { Height = 30, Margin = new Padding(0, 2, 0, 0), BackColor = ShellPalette.Surface, WrapContents = false };
			viewTools.Controls.Add(Tool("Playback", (s, e) => ConnectionForm.ShowPlayback()));
			viewTools.Controls.Add(Tool("Camera", (s, e) => ConnectionForm.ToggleCamera()));
			projectStack.Controls.Add(viewTools);

			var materialRow = new Panel { Height = 28, Margin = new Padding(4, 0, 0, 6), BackColor = ShellPalette.Surface };
			materialRow.Controls.Add(new ShellLabel("Material", ShellPalette.UiFont, ShellPalette.Muted) { AutoSize = true, Location = new Point(0, 6) });
			materialCombo = new FlatCombo { Location = new Point(58, 2), Width = 140 };
			materialCombo.SelectionChangeCommitted += (s, e) => ApplyMaterialChoice();
			materialRow.Controls.Add(materialCombo);
			materialRow.Resize += (s, e) => materialCombo.Width = Math.Max(60, materialRow.Width - 58);
			var headRow = new Panel { Height = 28, Margin = new Padding(4, 0, 0, 0), BackColor = ShellPalette.Surface };
			headLabel = new ShellLabel("Head X 0.00 · Y 0.00", ShellPalette.UiFont, ShellPalette.TextSoft) { AutoSize = true, Location = new Point(0, 6) };
			moveButton = new ShellButton { Text = "Move", ButtonStyle = ShellButtonStyle.Outline, Height = 26 };
			moveButton.FitWidth(10);
			moveButton.Click += (s, e) => ShowJogPopup(moveButton);
			headRow.Controls.Add(headLabel);
			headRow.Controls.Add(moveButton);
			headRow.Resize += (s, e) => moveButton.Location = new Point(headRow.Width - moveButton.Width, 1);
			projectFooter.Controls.Add(materialRow);
			projectFooter.Controls.Add(headRow);

			projectPanel.Controls.Add(projectStack);
			projectPanel.Controls.Add(projectFooter);
		}

		private ShellButton Tool(string text, EventHandler click)
		{
			var b = new ShellButton { Text = text, ButtonStyle = ShellButtonStyle.Outline, Height = 26, Margin = new Padding(4, 0, 0, 0), Font = ShellPalette.Small };
			b.FitWidth(8);
			b.Click += click;
			return b;
		}

		private void ShowProjectMenu(Control anchor)
		{
			var menu = new ContextMenuStrip { Renderer = MMn.Renderer };
			menu.Items.Add("Open file...", null, (s, e) => MnFileOpen_Click(s, e)).Enabled = Core.CanLoadNewFile;
			menu.Items.Add("Add file to this job...", null, (s, e) => MnFileAppend_Click(s, e)).Enabled = Core.CanLoadNewFile && Core.HasProgram;
			menu.Items.Add("Job queue...", null, (s, e) => JobQueueForm.ShowFor(this, Core));
			menu.Show(anchor ?? projectStack, new Point(0, anchor?.Height ?? 0));
		}

		private void ShowJogPopup(Control anchor)
		{
			JogForm.Parent?.Controls.Remove(JogForm);
			var host = new ToolStripControlHost(JogForm) { AutoSize = false, Size = new Size(240, Math.Max(150, JogForm.Height)), Padding = Padding.Empty, Margin = Padding.Empty };
			JogForm.Dock = DockStyle.Fill;
			var drop = new ToolStripDropDown { Padding = new Padding(1), BackColor = ShellPalette.Border };
			drop.Items.Add(host);
			drop.Closed += (s, e) => drop.Items.Remove(host);
			drop.Show(anchor, new Point(0, -host.Height - 4));
		}

		// =====================================================================
		// Process panel (right)
		// =====================================================================

		private void BuildProcessPanel()
		{
			processPanel = NewBar(DockStyle.Right, RightWidth, AnchorStyles.Left);
			processFooter = new ShellLabel("", ShellPalette.UiFont, ShellPalette.Muted) { AutoSize = false, Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(10, 7, 10, 0) };
			processFooter.Paint += (s, e) => { using (var p = new Pen(ShellPalette.Border)) e.Graphics.DrawLine(p, 0, 0, processFooter.Width, 0); };

			processStack = new StackPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8, 2, 8, 8) };
			var head = new Panel { Height = 34, Margin = new Padding(0, 6, 0, 0), BackColor = ShellPalette.Surface };
			head.Controls.Add(new ShellLabel("Process", ShellPalette.Heading) { AutoSize = true, Location = new Point(4, 8) });
			presetCombo = new FlatCombo { Width = 120 };
			presetCombo.SelectionChangeCommitted += (s, e) => ApplyPreset();
			head.Controls.Add(presetCombo);
			head.Resize += (s, e) => presetCombo.Location = new Point(head.Width - presetCombo.Width, 5);
			processStack.Controls.Add(head);
			boardCard = new InfoCard { Margin = new Padding(0, 2, 0, 6) };
			boardCard.EditClicked += (s, e) => EditPlacement();
			processStack.Controls.Add(boardCard);
			passesHeader = new SectionHeader("Passes", ShellIcon.Plus, (s, e) => AddPass(), "Add a pass") { Margin = new Padding(4, 2, 0, 2) };
			processStack.Controls.Add(passesHeader);
			// pass cards are inserted after the header on refresh
			processStack.Controls.Add(Divider());
			laserModeRow = new SettingRow("Laser mode") { Margin = new Padding(4, 0, 0, 0) };
			intervalRow = new SettingRow("Line interval") { Margin = new Padding(4, 0, 0, 0), Clickable = true };
			intervalRow.Click += (s, e) => ShowIntervalMenu();
			overscanRow = new SettingRow("Overscan") { Margin = new Padding(4, 0, 0, 0), Clickable = true };
			overscanRow.Click += (s, e) => { RasterOverscanForm.ShowFor(this); RefreshShell(); };
			repeatsRow = new SettingRow("Repeats") { Margin = new Padding(4, 0, 0, 0), Clickable = true };
			repeatsRow.Click += (s, e) => ShowRepeatsMenu();
			processStack.Controls.AddRange(new Control[] { laserModeRow, intervalRow, overscanRow, repeatsRow });
			processStack.Controls.Add(Divider());
			checksAnchor = new SectionHeader("Before starting") { Margin = new Padding(4, 2, 0, 2) };
			processStack.Controls.Add(checksAnchor);

			runStack = new StackPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), Visible = false };
			runStack.Controls.Add(new SectionHeader("Running") { Margin = new Padding(0, 6, 0, 0) });
			runPercent = new ShellLabel("0%", ShellPalette.Big) { Margin = new Padding(0, 4, 0, 2) };
			runProgress = new ProgressStrip { Height = 8, Margin = new Padding(0, 4, 0, 10) };
			runDetail = new ShellLabel("", ShellPalette.UiFont, ShellPalette.TextSoft);
			runStack.Controls.AddRange(new Control[] { runPercent, runProgress, runDetail });

			processPanel.Controls.Add(processStack);
			processPanel.Controls.Add(runStack);
			processPanel.Controls.Add(processFooter);
		}

		// =====================================================================
		// Modes and refresh
		// =====================================================================

		private void SetShellMode(ShellMode mode)
		{
			shellMode = mode;
			modeDesign.Active = mode == ShellMode.Design;
			modePrepare.Active = mode == ShellMode.Prepare;
			modeRun.Active = mode == ShellMode.Run;
			// Design: lay objects out (project panel, more room for the grid). Prepare: both panels. Run: progress only.
			projectPanel.Visible = mode != ShellMode.Run;
			// Visible reads false until the window is shown, so use the value just set.
			railLayers.Active = mode != ShellMode.Run;
			processPanel.Visible = mode != ShellMode.Design;
			processStack.Visible = mode == ShellMode.Prepare;
			runStack.Visible = mode == ShellMode.Run;
			RefreshShell();
		}

		private void ShellOnFileLoaded()
		{
			if (!shellApplied) return;
			scannedMaxS = null;
			scannedUsesZ = null;
			var commands = Core.LoadedFile.ToList();
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
			if (selectedObject >= ObjectCount()) selectedObject = 0;
			RebuildShellContent();
			RefreshShell();
		}

		private int ObjectCount()
		{
			return Core.HasJobOperations ? Core.LoadedFile.Operations.Count : Core.HasProgram ? 1 : 0;
		}

		/// <summary>Rebuilds the lists (boards, objects, passes, materials) after a file loads or an edit lands.</summary>
		private void RebuildShellContent()
		{
			if (!shellApplied || refreshingContent) return;
			refreshingContent = true;
			projectStack.SuspendLayout();
			processStack.SuspendLayout();
			try
			{
				var file = Core.LoadedFile;
				string path = Settings.GetObject<string>("Core.LastOpenFile", "");
				bool hasProgram = Core.HasProgram;

				// Project: the loaded file.
				foreach (var row in projectRows) { projectStack.Controls.Remove(row); row.Dispose(); }
				projectRows.Clear();
				if (hasProgram)
				{
					var row = new ListRow(System.IO.Path.GetFileNameWithoutExtension(path)) { Swatch = Color.FromArgb(227, 192, 141), Selected = true, Margin = new Padding(0, 0, 0, 1) };
					row.Click += (s, e) => MnFileOpen_Click(s, e);
					projectRows.Add(row);
				}
				else
				{
					var row = new ListRow("Open a file...") { Icon = ShellIcon.Folder, Margin = new Padding(0, 0, 0, 1) };
					row.Click += (s, e) => MnFileOpen_Click(s, e);
					projectRows.Add(row);
				}
				InsertAfter(projectStack, projectStack.Controls[0], projectRows);

				// Objects.
				foreach (var row in objectRows) { projectStack.Controls.Remove(row); row.Dispose(); }
				objectRows.Clear();
				if (Core.HasJobOperations)
				{
					int i = 0;
					foreach (var operation in file.Operations)
					{
						int index = i++;
						var row = new ListRow(operation.Name)
						{
							Icon = operation.Contours != null && operation.Contours.Length > 0 ? ShellIcon.Dashed : ShellIcon.Image,
							Toggle = operation.Enabled, Selected = index == selectedObject, Dimmed = !operation.Enabled,
							Detail = operation.Repeats > 1 ? "×" + operation.Repeats : null
						};
						row.Click += (s, e) => { selectedObject = index; RebuildShellContent(); RefreshShell(); };
						row.ToggleClicked += (s, e) => ToggleOperation(index);
						objectRows.Add(row);
					}
				}
				else if (hasProgram)
				{
					// A single object can't be hidden from itself, so it has no eye.
					var row = new ListRow(file.RasterSource != null ? "Image" : "G-code") { Icon = file.RasterSource != null ? ShellIcon.Image : ShellIcon.Dashed, Selected = true };
					objectRows.Add(row);
				}
				InsertAfter(projectStack, objectsHeader, objectRows);
				selectionHeader.Text = objectRows.Count > 0 && selectedObject < objectRows.Count ? objectRows[selectedObject].Text : "Selection";

				// Passes.
				RebuildPassCards();

				// Materials and presets.
				FillMaterials();
				FillPresets();
			}
			finally
			{
				projectStack.ResumeLayout(true);
				processStack.ResumeLayout(true);
				refreshingContent = false;
			}
		}

		private void RefreshShell()
		{
			if (!shellApplied) return;
			var c = CultureInfo.CurrentCulture;
			var file = Core.LoadedFile;
			bool hasProgram = Core.HasProgram, connected = Core.IsConnected, running = Core.InProgram;
			var status = Core.MachineStatus;

			if (running && !shellWasRunning) SetShellMode(ShellMode.Run);
			else if (!running && shellWasRunning) SetShellMode(ShellMode.Prepare);
			shellWasRunning = running;

			// Top bar.
			string path = hasProgram ? Settings.GetObject<string>("Core.LastOpenFile", "") : "";
			string folder = path.Length > 0 ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) : "";
			crumbFolder.Text = folder.Length > 0 ? folder + " /" : "";
			crumbFile.Text = hasProgram ? System.IO.Path.GetFileNameWithoutExtension(path) : "No file loaded";
			crumbState.Text = hasProgram ? string.Format(c, "{0:N0} lines", file.Count) : "";
			// Give up on a connection that never answers, instead of showing "Connecting..." forever.
			if (status == GrblCore.MacStatus.Connecting)
			{
				if (connectingSince == 0) connectingSince = HiResTimer.TotalMilliseconds;
				else if (HiResTimer.TotalMilliseconds - connectingSince > ConnectTimeoutMs)
				{
					connectingSince = 0;
					connectFailedUntil = HiResTimer.TotalMilliseconds + 8000;
					connectFailedPort = ConnectionForm.ConnectionAddress;
					Core.CloseCom(true);
					status = Core.MachineStatus;
				}
			}
			else connectingSince = 0;
			bool justFailed = !connected && status != GrblCore.MacStatus.Connecting && HiResTimer.TotalMilliseconds < connectFailedUntil;
			machineChip.Text = connected ? Core.Type + "  ·  " + GrblCore.TranslateEnum(status)
				: status == GrblCore.MacStatus.Connecting ? "Connecting to " + ConnectionForm.ConnectionAddress + "..."
				: justFailed ? "No answer on " + connectFailedPort + "  ·  Retry"
				: "No laser connected  ·  Connect";
			machineChip.Dot = connected ? ShellPalette.Good : status == GrblCore.MacStatus.Connecting ? ShellPalette.Warn : justFailed ? ShellPalette.Bad : ShellPalette.Faint;
			machineChip.FitWidth(10);
			LayoutTopBar(modeDesign.Parent);

			// Bottom bar.
			statusChip.Text = connected ? GrblCore.TranslateEnum(status) : "Offline";
			statusChip.Dot = !connected ? ShellPalette.Faint : status == GrblCore.MacStatus.Alarm ? ShellPalette.Bad : status == GrblCore.MacStatus.Idle ? ShellPalette.Good : ShellPalette.Warn;
			statusChip.FitWidth(6);
			TimeSpan estimate = running ? Core.ProjectedTime : file.EstimatedTime;
			estimateLabel.Text = hasProgram ? (running ? "Projected " : "Est. ") + Utils.TimeSpanToString(estimate, Utils.TimePrecision.Second, Utils.TimePrecision.Second, " ", true) : "";
			offlineNote.Visible = !connected && hasProgram;
			powerButton.Visible = speedButton.Visible = connected;
			powerButton.Text = "Power " + Core.OverrideS + "%";
			speedButton.Text = "Speed " + Core.OverrideG1 + "%";
			powerButton.FitWidth(6);
			speedButton.FitWidth(6);
			frameButton.Visible = connected && !running;
			frameButton.Enabled = Core.CanSendFile;
			pauseButton.Visible = connected && (Core.CanFeedHold || Core.CanResumeHold);
			pauseButton.Text = Core.CanResumeHold ? "Resume" : "Pause";
			pauseButton.FitWidth();
			if (running) { mainButton.Text = "Stop"; mainButton.Icon = ShellIcon.None; mainButton.ButtonStyle = ShellButtonStyle.Danger; mainButton.Enabled = Core.CanAbortProgram; }
			else if (connected) { mainButton.Text = "Start engraving"; mainButton.Icon = ShellIcon.Play; mainButton.ButtonStyle = ShellButtonStyle.Accent; mainButton.Enabled = Core.CanSendFile; }
			else { mainButton.Text = "Save to SD card"; mainButton.Icon = ShellIcon.Save; mainButton.ButtonStyle = ShellButtonStyle.Accent; mainButton.Enabled = hasProgram; }
			mainButton.FitWidth(16);
			saveArrow.Visible = !running;
			saveArrow.Enabled = hasProgram;
			consoleButton.Active = consoleDrawer.Visible;
			LayoutBottomBar();

			// Project panel.
			bool canEdit = Core.CanConfigurePasses && !Core.JobEditorOpen && !shellBusy;
			var raster = file.RasterSource;
			RectangleF? box = SelectedBounds();
			fieldX.Value = box.HasValue ? box.Value.X.ToString("0.00", c) : "";
			fieldY.Value = box.HasValue ? box.Value.Y.ToString("0.00", c) : "";
			fieldW.Value = box.HasValue ? box.Value.Width.ToString("0.00", c) : "";
			fieldH.Value = box.HasValue ? box.Value.Height.ToString("0.00", c) : "";
			bool geometryEditable = canEdit && hasProgram && (!Core.HasJobOperations || (SelectedOperation()?.Placement?.Rotation ?? 0) == 0);
			foreach (var f in new[] { fieldX, fieldY, fieldW, fieldH }) f.ReadOnly = !geometryEditable;
			rotateLabel.Text = "Rotate  " + (SelectedOperation()?.Placement?.Rotation ?? 0).ToString("0.#", c) + "°";
			bool showImage = raster != null;
			imageCaps.Visible = methodRow.Visible = brightness.Visible = contrast.Visible = whiteClip.Visible = imageTools.Visible = showImage;
			if (showImage)
			{
				methodRow.Value = GrblCore.TranslateEnum(raster.SelectedTool);
				brightness.Value = raster.Brightness;
				contrast.Value = raster.Contrast;
				whiteClip.Value = raster.WhiteClip;
				brightness.Enabled = contrast.Enabled = whiteClip.Enabled = canEdit;
				foreach (Control b in imageTools.Controls) b.Enabled = canEdit;
				imageTools.Controls[1].Enabled = canEdit && file.GetRasterHistory(GrblFile.RasterEdit.Undo) != null;
				imageTools.Controls[2].Enabled = canEdit && file.GetRasterHistory(GrblFile.RasterEdit.Redo) != null;
			}
			var pos = Core.MachinePosition;
			headLabel.Text = string.Format(c, "Head X {0:0.00} · Y {1:0.00}", pos.X, pos.Y);
			moveButton.Enabled = connected && Core.JogEnabled;
			materialCombo.Enabled = canEdit;

			// Process panel.
			if (shellMode == ShellMode.Prepare)
			{
				var range = file.Range.DrawingRange;
				boardCard.Title = !hasProgram ? "No job" : range.ValidRange ? string.Format(c, "Job · {0:0.##} × {1:0.##} mm", range.X.Max - range.X.Min, range.Y.Max - range.Y.Min) : "Job";
				boardCard.Subtitle = !hasProgram ? "Open an image, SVG or G-code file." : range.ValidRange
					? string.Format(c, "At X {0:0.##}, Y {1:0.##} · {2:N0} lines", range.X.Min, range.Y.Min, file.Count) : string.Format(c, "{0:N0} lines", file.Count);
				boardCard.CanEdit = canEdit;
				boardCard.Invalidate();
				passesHeader.Action.Enabled = canEdit && !Core.HasJobOperations;
				foreach (var card in passCards)
				{
					card.Power.Enabled = card.Speed.Enabled = card.Menu.Enabled = canEdit;
					card.Scan.Enabled = canEdit && card.Scan.Items.Count > 1;
				}

				var plan = CurrentPlan();
				var pass = plan != null && selectedPass < plan.Count ? plan[selectedPass] : null;
				laserModeRow.Value = raster != null ? (raster.LaserOn ?? "M3").Trim() + (raster.LaserOn != null && raster.LaserOn.Contains("4") ? " dynamic" : " constant") : "From file";
				intervalRow.Value = pass != null && pass.LinesPerMm > 0 ? (1 / pass.LinesPerMm).ToString("0.###", c) + " mm" : raster != null ? (1 / Math.Max(0.1m, raster.Quality)).ToString("0.###", c) + " mm" : "—";
				intervalRow.Clickable = raster != null && canEdit;
				overscanRow.Value = RasterOverscanForm.OverscanMm > 0 ? RasterOverscanForm.OverscanMm.ToString("0.#", c) + " mm" : "Off";
				repeatsRow.Value = Core.LoopCount.ToString("0", c);
				repeatsRow.Clickable = canEdit && Core.Passes == null && raster == null;
				foreach (Control row in new Control[] { laserModeRow, intervalRow, overscanRow, repeatsRow }) row.Invalidate();

				if (HiResTimer.TotalMilliseconds - lastChecks > 2000)
				{
					lastChecks = HiResTimer.TotalMilliseconds;
					RefreshChecks();
				}
				int passes = plan?.Count ?? 1;
				processFooter.Text = !hasProgram ? "" : string.Format(c, "{0} pass{1} · {2}", passes, passes == 1 ? "" : "es",
					materialCombo.SelectedIndex > 0 ? materialCombo.SelectedItem.ToString() : Utils.TimeSpanToString(file.EstimatedTime, Utils.TimePrecision.Minute, Utils.TimePrecision.Second, " ", true));
			}
			else if (shellMode == ShellMode.Run)
			{
				TimeSpan elapsed = Core.ProgramTime, projected = Core.ProjectedTime;
				double percent = projected.TotalSeconds > 0 ? Math.Min(100, elapsed.TotalSeconds * 100 / projected.TotalSeconds) : 0;
				runPercent.Text = running ? string.Format(c, "{0:0}%", percent) : "Not running";
				runProgress.Value = running ? percent / 100 : 0;
				var lines = new List<string>();
				if (running)
				{
					lines.Add("Elapsed  " + Utils.TimeSpanToString(elapsed, Utils.TimePrecision.Second, Utils.TimePrecision.Second, " ", true));
					lines.Add("Remaining  " + Utils.TimeSpanToString(projected > elapsed ? projected - elapsed : TimeSpan.Zero, Utils.TimePrecision.Second, Utils.TimePrecision.Second, " ", true));
					if (Core.Passes != null) lines.Add(string.Format(c, "Pass {0} of {1}: {2}", Core.CurrentPassNumber, Core.Passes.Count, Core.CurrentPassSettings));
					else if (Core.LoopCount > 1) lines.Add(string.Format(c, "{0} repeats left", Core.LoopCount));
					lines.Add("");
					lines.Add("Progress is saved as it runs, so the job can resume after a crash.");
				}
				else lines.Add(hasProgram ? "Press Start engraving to run the job." : "Load a job first.");
				runDetail.Text = string.Join(Environment.NewLine, lines);
				runStack.PerformLayout();
				processFooter.Text = "";
			}
		}

		private void RefreshChecks()
		{
			var lines = new List<Tuple<string, string, Color>>();
			if (Core.HasProgram)
			{
				if (!scannedMaxS.HasValue) lines.Add(Tuple.Create("…", "Checking the job...", ShellPalette.Muted));
				else
				{
					var findings = PreflightCheck.Run(Core.BuildPreflightInput(scannedMaxS, scannedUsesZ));
					foreach (var f in findings)
						lines.Add(Tuple.Create(f.Severity == PreflightSeverity.Error ? "✕" : "!", f.Text, f.Severity == PreflightSeverity.Error ? ShellPalette.Bad : ShellPalette.Warn));
					if (!findings.Any(f => f.Text.Contains("work area"))) lines.Insert(0, Tuple.Create("✓", "Fits the work area", ShellPalette.Good));
					if (scannedMaxS.Value > 0 && !findings.Any(f => f.Text.Contains("$30"))) lines.Add(Tuple.Create("✓", "Power within $30", ShellPalette.Good));
					if (!Core.IsConnected) lines.Add(Tuple.Create("–", "Machine checks need a connection", ShellPalette.Muted));
				}
			}
			else lines.Add(Tuple.Create("–", "Load a job to check it", ShellPalette.Muted));

			processStack.SuspendLayout();
			while (checkRows.Count < lines.Count)
			{
				var row = new CheckRow { Margin = new Padding(4, 1, 0, 1) };
				checkRows.Add(row);
				processStack.Controls.Add(row);
			}
			int width = processStack.ClientSize.Width - processStack.Padding.Horizontal - 4;
			for (int i = 0; i < checkRows.Count; i++)
			{
				var row = checkRows[i];
				row.Visible = i < lines.Count;
				if (!row.Visible) continue;
				row.Mark = lines[i].Item1;
				row.Text = lines[i].Item2;
				row.MarkColor = lines[i].Item3;
				row.Height = row.Measure(width);
				row.Invalidate();
			}
			processStack.ResumeLayout(true);
		}

		// =====================================================================
		// Objects and geometry
		// =====================================================================

		private JobOperation SelectedOperation()
		{
			if (!Core.HasJobOperations) return null;
			var ops = Core.LoadedFile.Operations;
			return selectedObject < ops.Count ? ops[selectedObject] : null;
		}

		private readonly Dictionary<JobOperation, RectangleF> boundsCache = new Dictionary<JobOperation, RectangleF>();

		/// <summary>Where the selected object lands on the bed, in mm.</summary>
		private RectangleF? SelectedBounds()
		{
			var file = Core.LoadedFile;
			if (!Core.HasProgram) return null;
			var operation = SelectedOperation();
			if (operation != null)
			{
				RectangleF cached;
				if (boundsCache.TryGetValue(operation, out cached)) return cached;
				try
				{
					var stats = JobStats.Measure(operation.Placement == null ? operation.Source : operation.Placement.Transform(operation.Source), 1000);
					if (!stats.HasBounds) return null;
					var box = new RectangleF((float)stats.MinX, (float)stats.MinY, (float)stats.Width, (float)stats.Height);
					if (boundsCache.Count > 64) boundsCache.Clear();
					boundsCache[operation] = box;
					return box;
				}
				catch (InvalidOperationException) { return null; }
			}
			if (file.RasterSource != null) return new RectangleF(file.RasterSource.TargetOffset, file.RasterSource.TargetSize);
			var range = file.Range.DrawingRange;
			return range.ValidRange ? new RectangleF((float)range.X.Min, (float)range.Y.Min, (float)(range.X.Max - range.X.Min), (float)(range.Y.Max - range.Y.Min)) : (RectangleF?)null;
		}

		private void ApplyGeometry(FieldBox changed)
		{
			var c = CultureInfo.CurrentCulture;
			var box = SelectedBounds();
			if (!box.HasValue || changed.ReadOnly) return;
			float x, y, w, h;
			if (!float.TryParse(fieldX.Box.Text, NumberStyles.Float, c, out x) || !float.TryParse(fieldY.Box.Text, NumberStyles.Float, c, out y)
				|| !float.TryParse(fieldW.Box.Text, NumberStyles.Float, c, out w) || !float.TryParse(fieldH.Box.Text, NumberStyles.Float, c, out h) || w <= 0 || h <= 0)
			{
				RefreshShell();
				return;
			}
			var old = box.Value;
			if (lockRatio.Checked)
			{
				if (changed == fieldW) h = old.Height * w / old.Width;
				else if (changed == fieldH) w = old.Width * h / old.Height;
			}
			var file = Core.LoadedFile;
			var operation = SelectedOperation();
			if (operation == null && file.RasterSource == null)
			{
				// Plain G-code: make it an operation first, which keeps it as-is but lets placement move and scale it.
				var passes = Core.Passes;
				string name = System.IO.Path.GetFileNameWithoutExtension(Settings.GetObject<string>("Core.LastOpenFile", "G-code"));
				try
				{
					file.ConvertToOperation(name);
					if (passes != null && Core.CanConfigurePasses) Core.ConfigurePasses(passes);
				}
				catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { ShowShellError(ex.Message); return; }
				selectedObject = 0;
				operation = SelectedOperation();
				if (operation == null) return;
			}
			if (operation != null)
			{
				var source = Nesting.Bounds(operation);
				try { new JobPlacement(new PointF(0, 0), new PointF(0, 0), 1, 0).Transform(operation.Source); }
				catch (InvalidOperationException)
				{
					ShowShellError("This G-code uses relative moves or Z, so it can't be moved or resized here.");
					RefreshShell();
					return;
				}
				double scale = w / source.Width;
				if (scale <= 0 || scale > 10) { ShowShellError("Scale must stay between 0 and 10 times the original size."); return; }
				var draft = operation.WithSettings(operation.Name, operation.Repeats, operation.Settings, operation.Enabled);
				draft.Placement = new JobPlacement(new PointF(source.X, source.Y), new PointF(x, y), scale, 0);
				RebuildWith(selectedObject, draft, "Move or resize " + operation.Name);
			}
			else if (file.RasterSource != null)
			{
				ApplyRasterDraft(d =>
				{
					d.TargetOffset = new PointF(x, y);
					d.TargetSize = new SizeF(w, h);
				}, new Dictionary<string, object>
				{
					{ "GrayScaleConversion.Gcode.ImageSize.W", w }, { "GrayScaleConversion.Gcode.ImageSize.H", h },
					{ "GrayScaleConversion.Gcode.Offset.X", x }, { "GrayScaleConversion.Gcode.Offset.Y", y }
				});
			}
		}

		private void ToggleOperation(int index)
		{
			var ops = Core.LoadedFile.Operations;
			if (index >= ops.Count) return;
			var op = ops[index];
			RebuildWith(index, op.WithSettings(op.Name, op.Repeats, op.Settings, !op.Enabled), (op.Enabled ? "Hide " : "Show ") + op.Name);
		}

		private void RebuildWith(int index, JobOperation replacement, string change)
		{
			if (!Core.CanConfigurePasses || Core.JobEditorOpen || shellBusy) return;
			var list = Core.LoadedFile.Operations.Select((o, i) =>
			{
				if (i == index) return replacement;
				var copy = o.WithSettings(o.Name, o.Repeats, o.Settings, o.Enabled);
				return copy;
			}).ToList();
			shellBusy = true;
			boundsCache.Clear();
			try
			{
				Core.RebuildOperations(list, error =>
				{
					shellBusy = false;
					if (error != null) ShowShellError(error.Message);
					RebuildShellContent();
					RefreshShell();
				});
			}
			catch (InvalidOperationException ex) { shellBusy = false; ShowShellError(ex.Message); }
		}

		private void ApplyImage(Action<ImageProcessor> change, string key, object value)
		{
			ApplyRasterDraft(change, new Dictionary<string, object> { { key, value } });
		}

		/// <summary>Regenerates the raster job from a changed copy of its source image settings. Undo/Redo keeps working.</summary>
		private void ApplyRasterDraft(Action<ImageProcessor> change, Dictionary<string, object> settings)
		{
			var file = Core.LoadedFile;
			var source = file.RasterSource;
			if (source == null || !Core.CanConfigurePasses || Core.JobEditorOpen || shellBusy) { RefreshShell(); return; }
			ImageProcessor draft = null;
			try
			{
				draft = source.CopyRasterSource();
				change(draft);
				if (draft.RasterSettings != null)
				{
					draft.RasterSettings = new Dictionary<string, object>(draft.RasterSettings);
					foreach (var entry in settings)
						if (draft.RasterSettings.ContainsKey(entry.Key) || entry.Key.Contains("ImageSize") || entry.Key.Contains("Offset"))
							draft.RasterSettings[entry.Key] = entry.Value;
				}
				shellBusy = true;
				crumbState.Text = "Rebuilding...";
				Core.ApplyRasterEdit(source, draft, GrblFile.RasterEdit.Apply, error =>
				{
					shellBusy = false;
					if (error != null) ShowShellError(error.Message);
					RebuildShellContent();
					RefreshShell();
				});
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
			{
				shellBusy = false;
				draft?.Dispose();
				ShowShellError(ex.Message);
			}
		}

		/// <summary>The job card's pencil: jump to the selected object's position fields.</summary>
		private void EditPlacement()
		{
			if (!projectPanel.Visible) SetShellMode(ShellMode.Prepare);
			fieldX.Box.Focus();
			fieldX.Box.SelectAll();
		}

		// =====================================================================
		// Passes
		// =====================================================================

		/// <summary>The passes the cards edit: the image's passes, the job's custom passes, or the file's single pass.</summary>
		private JobPassPlan CurrentPlan()
		{
			var file = Core.LoadedFile;
			if (Core.HasJobOperations && file.RasterSource == null) return null;
			if (file.RasterSource != null) return file.RasterSource.RasterPasses ?? new JobPassPlan(new[] { file.RasterSource.DefaultRasterPass });
			if (!Core.HasProgram) return null;
			return Core.Passes ?? new JobPassPlan(new[] { new JobPass(100, 100) });
		}

		private static readonly ImageProcessor.Direction[] ScanDirections =
		{
			ImageProcessor.Direction.Horizontal, ImageProcessor.Direction.Vertical, ImageProcessor.Direction.Diagonal, ImageProcessor.Direction.NewReverseDiagonal
		};

		private static string ScanLabel(ImageProcessor.Direction d)
		{
			switch (d)
			{
				case ImageProcessor.Direction.Horizontal: case ImageProcessor.Direction.NewHorizontal: return "0° scan";
				case ImageProcessor.Direction.Vertical: case ImageProcessor.Direction.NewVertical: return "90° scan";
				case ImageProcessor.Direction.Diagonal: case ImageProcessor.Direction.NewDiagonal: return "45° scan";
				case ImageProcessor.Direction.NewReverseDiagonal: return "135° scan";
				default: return GrblCore.TranslateEnum(d);
			}
		}

		private sealed class Choice
		{
			internal readonly decimal Value;
			internal readonly object Tag;
			private readonly string text;
			internal Choice(decimal value, string text, object tag = null) { Value = value; this.text = text; Tag = tag; }
			public override string ToString() { return text; }
		}

		private void RebuildPassCards()
		{
			foreach (var card in passCards) { processStack.Controls.Remove(card); card.Dispose(); }
			passCards.Clear();
			var c = CultureInfo.CurrentCulture;
			var file = Core.LoadedFile;
			var raster = file.RasterSource;

			if (Core.HasJobOperations && raster == null)
			{
				// One card per operation: power, speed and repeats.
				int i = 0;
				foreach (var op in file.Operations)
				{
					int index = i++;
					var card = new PassCard { Number = (index + 1).ToString("00"), Title = op.Name, Selected = index == selectedObject, Time = OperationTime(op) };
					FillPercent(card.Power, op.Settings?.PowerPercent ?? 100, 5, 100, 5);
					FillPercent(card.Speed, op.Settings?.SpeedPercent ?? 100, 10, 300, 10);
					for (int r = 1; r <= 20; r++) card.Scan.Items.Add(new Choice(r, "×" + r + " repeat" + (r == 1 ? "" : "s")));
					card.Scan.SelectedIndex = Math.Max(0, Math.Min(19, op.Repeats - 1));
					EventHandler apply = (s, e) =>
					{
						var power = ((Choice)card.Power.SelectedItem).Value;
						var speed = ((Choice)card.Speed.SelectedItem).Value;
						int repeats = (int)((Choice)card.Scan.SelectedItem).Value;
						RebuildWith(index, op.WithSettings(op.Name, repeats, new JobPass(power, speed), op.Enabled), "Change " + op.Name);
					};
					card.Power.SelectionChangeCommitted += apply;
					card.Speed.SelectionChangeCommitted += apply;
					card.Scan.SelectionChangeCommitted += apply;
					card.Click += (s, e) => { selectedObject = index; RebuildShellContent(); RefreshShell(); };
					card.Menu.Click += (s, e) => ShowOperationMenu(card.Menu, index);
					passCards.Add(card);
				}
			}
			else
			{
				var plan = CurrentPlan();
				if (plan != null)
				{
					if (selectedPass >= plan.Count) selectedPass = 0;
					var times = PassTimes(plan.Count);
					for (int index = 0; index < plan.Count; index++)
					{
						int at = index;
						var pass = plan[index];
						var card = new PassCard
						{
							Number = (index + 1).ToString("00"),
							Title = plan.Count == 1 ? "Engraving" : index == 0 ? "Main engraving" : index == plan.Count - 1 ? "Cleanup" : "Pass " + (index + 1),
							Selected = index == selectedPass, Time = times != null ? times[index] : null
						};
						FillPercent(card.Power, pass.PowerPercent, 5, 100, 5);
						if (raster != null)
						{
							foreach (var pct in new decimal[] { 25, 50, 75, 100, 125, 150, 200, 300 }.Union(new[] { pass.SpeedPercent }).OrderBy(v => v))
								card.Speed.Items.Add(new Choice(pct, Math.Round(raster.MarkSpeed * pct / 100).ToString("0", c)));
							card.Speed.SelectedItem = card.Speed.Items.Cast<Choice>().First(ch => ch.Value == pass.SpeedPercent);
							foreach (var d in ScanDirections.Union(new[] { pass.Direction }).Where(d => d != ImageProcessor.Direction.None))
								card.Scan.Items.Add(new Choice(0, ScanLabel(d), d));
							card.Scan.SelectedItem = card.Scan.Items.Cast<Choice>().FirstOrDefault(ch => Equals(ch.Tag, pass.Direction)) ?? card.Scan.Items[0];
						}
						else
						{
							FillPercent(card.Speed, pass.SpeedPercent, 10, 300, 10);
							card.Scan.Visible = false; // G-code keeps the scan the file already has
						}
						EventHandler apply = (s, e) => ReplacePass(at, plan, ((Choice)card.Power.SelectedItem).Value, ((Choice)card.Speed.SelectedItem).Value,
							card.Scan.SelectedItem is Choice ch && ch.Tag is ImageProcessor.Direction dir ? dir : pass.Direction, pass.LinesPerMm);
						card.Power.SelectionChangeCommitted += apply;
						card.Speed.SelectionChangeCommitted += apply;
						card.Scan.SelectionChangeCommitted += apply;
						card.Click += (s, e) => { selectedPass = at; foreach (var other in passCards) { other.Selected = other == passCards[at]; other.Invalidate(); } RefreshShell(); };
						card.Menu.Click += (s, e) => ShowPassMenu(card.Menu, at, plan);
						passCards.Add(card);
					}
				}
			}
			InsertAfter(processStack, passesHeader, passCards.Cast<Control>().ToList());
		}

		private static void FillPercent(ComboBox combo, decimal current, int from, int to, int step)
		{
			var values = Enumerable.Range(0, (to - from) / step + 1).Select(i => (decimal)(from + i * step)).Union(new[] { current }).OrderBy(v => v);
			foreach (var v in values) combo.Items.Add(new Choice(v, v.ToString("0.#", CultureInfo.CurrentCulture) + "%"));
			combo.SelectedItem = combo.Items.Cast<Choice>().First(ch => ch.Value == current);
		}

		private string OperationTime(JobOperation op)
		{
			var file = Core.LoadedFile;
			if (!op.Enabled || op.End <= op.Start || op.End > file.Count) return null;
			return ShortTime(JobOperationsEditor.Offset(file, op.End) - JobOperationsEditor.Offset(file, op.Start));
		}

		private string[] PassTimes(int count)
		{
			var file = Core.LoadedFile;
			var sections = file.Operations.Count == 1 ? file.Operations[0].CompiledPasses : null;
			if (sections == null || sections.Count != count) return null;
			return sections.Select(s => s.End <= file.Count && s.End > s.Start ? ShortTime(JobOperationsEditor.Offset(file, s.End) - JobOperationsEditor.Offset(file, s.Start)) : null).ToArray();
		}

		private static string ShortTime(TimeSpan t)
		{
			return t.TotalHours >= 1 ? string.Format("{0}h {1}m", (int)t.TotalHours, t.Minutes) : string.Format("{0}m {1:00}s", t.Minutes, t.Seconds);
		}

		private void ReplacePass(int index, JobPassPlan plan, decimal power, decimal speed, ImageProcessor.Direction direction, decimal linesPerMm)
		{
			var passes = Enumerable.Range(0, plan.Count).Select(i => plan[i]).ToList();
			passes[index] = new JobPass(power, speed, direction, linesPerMm);
			ApplyPlan(passes);
		}

		private void ApplyPlan(List<JobPass> passes)
		{
			if (!Core.CanConfigurePasses || Core.JobEditorOpen || shellBusy || passes.Count == 0) return;
			var plan = new JobPassPlan(passes);
			var source = Core.LoadedFile.RasterSource;
			try
			{
				if (source == null)
				{
					Core.ConfigurePasses(passes.Count == 1 && passes[0].PowerPercent == 100 && passes[0].SpeedPercent == 100 ? null : plan);
					RebuildShellContent();
					RefreshShell();
					return;
				}
				shellBusy = true;
				crumbState.Text = "Rebuilding...";
				Core.RebuildRasterPasses(source, plan, error =>
				{
					shellBusy = false;
					if (error != null) ShowShellError(error.Message);
					RebuildShellContent();
					RefreshShell();
				});
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
			{
				shellBusy = false;
				ShowShellError(ex.Message);
			}
		}

		private void AddPass()
		{
			var plan = CurrentPlan();
			if (plan == null) return;
			var passes = Enumerable.Range(0, plan.Count).Select(i => plan[i]).ToList();
			var template = passes[Math.Min(selectedPass, passes.Count - 1)];
			passes.Add(new JobPass(template.PowerPercent, template.SpeedPercent, template.Direction, template.LinesPerMm));
			selectedPass = passes.Count - 1;
			ApplyPlan(passes);
		}

		private void ShowPassMenu(Control anchor, int index, JobPassPlan plan)
		{
			var passes = Enumerable.Range(0, plan.Count).Select(i => plan[i]).ToList();
			var menu = new ContextMenuStrip { Renderer = MMn.Renderer };
			menu.Items.Add("Duplicate", null, (s, e) => { passes.Insert(index + 1, passes[index]); ApplyPlan(passes); });
			menu.Items.Add("Move up", null, (s, e) => { var p = passes[index]; passes.RemoveAt(index); passes.Insert(index - 1, p); selectedPass = index - 1; ApplyPlan(passes); }).Enabled = index > 0;
			menu.Items.Add("Move down", null, (s, e) => { var p = passes[index]; passes.RemoveAt(index); passes.Insert(index + 1, p); selectedPass = index + 1; ApplyPlan(passes); }).Enabled = index < passes.Count - 1;
			menu.Items.Add("Remove", null, (s, e) => { passes.RemoveAt(index); selectedPass = 0; ApplyPlan(passes); }).Enabled = passes.Count > 1;
			menu.Show(anchor, new Point(0, anchor.Height));
		}

		/// <summary>Reorder, duplicate or remove an operation; the job rebuilds straight away.</summary>
		private void ShowOperationMenu(Control anchor, int index)
		{
			var ops = Core.LoadedFile.Operations.Select(o => o.WithSettings(o.Name, o.Repeats, o.Settings, o.Enabled)).ToList();
			Action<List<JobOperation>, int, string> rebuild = (list, select, change) =>
			{
				if (!Core.CanConfigurePasses || Core.JobEditorOpen || shellBusy) return;
				shellBusy = true;
				boundsCache.Clear();
				try
				{
					Core.RebuildOperations(list, error =>
					{
						shellBusy = false;
						if (error != null) ShowShellError(error.Message);
						else selectedObject = select;
						RebuildShellContent();
						RefreshShell();
					});
				}
				catch (InvalidOperationException ex) { shellBusy = false; ShowShellError(ex.Message); }
			};
			var menu = new ContextMenuStrip { Renderer = MMn.Renderer };
			menu.Items.Add("Move up", null, (s, e) => { var o = ops[index]; ops.RemoveAt(index); ops.Insert(index - 1, o); rebuild(ops, index - 1, "Move " + o.Name); }).Enabled = index > 0;
			menu.Items.Add("Move down", null, (s, e) => { var o = ops[index]; ops.RemoveAt(index); ops.Insert(index + 1, o); rebuild(ops, index + 1, "Move " + o.Name); }).Enabled = index < ops.Count - 1;
			menu.Items.Add("Duplicate", null, (s, e) => { var o = ops[index]; ops.Insert(index + 1, o.WithSettings(o.Name + " copy", o.Repeats, o.Settings, o.Enabled)); rebuild(ops, index + 1, "Duplicate " + o.Name); });
			menu.Items.Add("Remove", null, (s, e) => { var o = ops[index]; ops.RemoveAt(index); rebuild(ops, 0, "Remove " + o.Name); }).Enabled = ops.Count > 1;
			menu.Show(anchor, new Point(0, anchor.Height));
		}

		private void ShowIntervalMenu()
		{
			var plan = CurrentPlan();
			var raster = Core.LoadedFile.RasterSource;
			if (plan == null || raster == null || !intervalRow.Clickable) return;
			var passes = Enumerable.Range(0, plan.Count).Select(i => plan[i]).ToList();
			int at = Math.Min(selectedPass, passes.Count - 1);
			var menu = new ContextMenuStrip { Renderer = MMn.Renderer };
			foreach (var lpm in new decimal[] { 5, 6, 8, 10, 12, 15, 20 })
			{
				decimal value = lpm;
				var item = menu.Items.Add(string.Format(CultureInfo.CurrentCulture, "{0:0.###} mm  ({1} lines/mm)", 1 / value, value), null, (s, e) =>
				{
					var p = passes[at];
					passes[at] = new JobPass(p.PowerPercent, p.SpeedPercent, p.Direction == ImageProcessor.Direction.None ? raster.LineDirection : p.Direction, value);
					ApplyPlan(passes);
				}) as ToolStripMenuItem;
				item.Checked = passes[at].LinesPerMm == value;
			}
			menu.Show(intervalRow, new Point(intervalRow.Width, intervalRow.Height), ToolStripDropDownDirection.BelowLeft);
		}

		private void ShowRepeatsMenu()
		{
			if (!repeatsRow.Clickable) return;
			var menu = new ContextMenuStrip { Renderer = MMn.Renderer };
			for (int r = 1; r <= 10; r++)
			{
				int value = r;
				var item = menu.Items.Add(value.ToString(), null, (s, e) => { Core.LoopCount = value; RefreshShell(); }) as ToolStripMenuItem;
				item.Checked = Core.LoopCount == value;
			}
			menu.Show(repeatsRow, new Point(repeatsRow.Width, repeatsRow.Height), ToolStripDropDownDirection.BelowLeft);
		}

		private void FillPresets()
		{
			presetCombo.Items.Clear();
			presetCombo.Items.Add("Saved preset");
			var raster = Core.LoadedFile.RasterSource != null;
			try { foreach (var name in new JobPassProfiles(raster).Names) presetCombo.Items.Add(name); }
			catch (Exception) { }
			presetCombo.Items.Add("Save these passes...");
			presetCombo.SelectedIndex = 0;
			presetCombo.Enabled = CurrentPlan() != null;
		}

		private void ApplyPreset()
		{
			int index = presetCombo.SelectedIndex;
			var raster = Core.LoadedFile.RasterSource != null;
			var profiles = new JobPassProfiles(raster);
			if (index == presetCombo.Items.Count - 1)
			{
				var plan = CurrentPlan();
				string name = NamePrompt.Ask(this, "Save preset", "Name for this set of passes", "My preset");
				if (plan != null && !string.IsNullOrWhiteSpace(name)) profiles.Save(name.Trim(), plan);
				FillPresets();
				return;
			}
			if (index <= 0) return;
			JobPassPlan chosen;
			if (profiles.TryGet(presetCombo.SelectedItem.ToString(), out chosen))
				ApplyPlan(Enumerable.Range(0, chosen.Count).Select(i => chosen[i]).ToList());
		}

		// =====================================================================
		// Material
		// =====================================================================

		private sealed class MaterialChoice
		{
			internal readonly PSHelper.MaterialDB.MaterialsRow Row;
			private readonly string text;
			internal MaterialChoice(PSHelper.MaterialDB.MaterialsRow row, string text) { Row = row; this.text = text; }
			public override string ToString() { return text; }
		}

		private void FillMaterials()
		{
			var keep = materialCombo.SelectedItem?.ToString();
			materialCombo.Items.Clear();
			materialCombo.Items.Add("Choose...");
			try
			{
				int maxPwm = FocusTestForm.SafeMaxPower();
				foreach (var row in GrblCore.MaterialDB.Materials.Where(r => r.RowState != System.Data.DataRowState.Deleted && r.Visible)
					.OrderBy(r => r.Material).ThenBy(r => r.Thickness))
					materialCombo.Items.Add(new MaterialChoice(row, MaterialSetting.FromRow(row, maxPwm).Name));
			}
			catch (Exception) { }
			int found = keep == null ? -1 : materialCombo.Items.Cast<object>().Select(o => o.ToString()).ToList().IndexOf(keep);
			materialCombo.SelectedIndex = found > 0 ? found : 0;
		}

		private void ApplyMaterialChoice()
		{
			var choice = materialCombo.SelectedItem as MaterialChoice;
			if (choice == null) return;
			var setting = MaterialSetting.FromRow(choice.Row, FocusTestForm.SafeMaxPower());
			var file = Core.LoadedFile;
			if (file.RasterSource != null)
			{
				ApplyRasterDraft(d => { d.MaxPower = setting.Power; d.MarkSpeed = setting.Speed; }, new Dictionary<string, object>
				{
					{ "GrayScaleConversion.Gcode.LaserOptions.PowerMax", setting.Power }, { "GrayScaleConversion.Gcode.Speed.Mark", setting.Speed }
				});
			}
			else if (SelectedOperation() is JobOperation op)
			{
				var applied = MaterialApplication.For(setting, op.Source, op.SupportsKerf);
				var draft = op.WithSettings(op.Name, applied.Repeats, applied.Pass, op.Enabled);
				if (op.SupportsKerf) draft.KerfMm = applied.KerfMm;
				RebuildWith(selectedObject, draft, "Apply " + setting.Name);
			}
			else if (Core.HasProgram)
			{
				var applied = MaterialApplication.For(setting, file.ToList(), false);
				ApplyPlan(new List<JobPass> { applied.Pass });
			}
		}

		// =====================================================================
		// Helpers
		// =====================================================================

		private void ShowShellError(string message)
		{
			MessageBox.Show(this, message, "LaserGRBL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		private static void InsertAfter(StackPanel stack, Control anchor, IList<Control> items)
		{
			int at = stack.Controls.GetChildIndex(anchor) + 1;
			foreach (var item in items)
			{
				stack.Controls.Add(item);
				stack.Controls.SetChildIndex(item, at++);
			}
		}

		private ShellPanel NewBar(DockStyle dock, int size, AnchorStyles lines)
		{
			var panel = new ShellPanel { Dock = dock, Lines = lines };
			if (dock == DockStyle.Top || dock == DockStyle.Bottom) panel.Height = size;
			else panel.Width = size;
			return panel;
		}

		private static Control Divider()
		{
			return new Panel { Height = 1, BackColor = ShellPalette.Border, Margin = new Padding(4, 8, 4, 6) };
		}

		private void ApplyShellColors() { }

		/// <summary>Asks for a short name.</summary>
		private sealed class NamePrompt : SimpleDialog
		{
			private readonly TextBox box;

			private NamePrompt(string title, string label, string value) : base(title)
			{
				box = AddField(label, new TextBox { Text = value, Width = 220 });
				AddButton("Cancel", Close, cancel: true);
				AddButton("Save", () => { DialogResult = DialogResult.OK; }, accept: true);
			}

			internal static string Ask(IWin32Window owner, string title, string label, string value)
			{
				using (var form = new NamePrompt(title, label, value))
					return form.ShowDialog(owner) == DialogResult.OK ? form.box.Text : null;
			}
		}

		/// <summary>Two-by-two grid of equal cells with a small gap.</summary>
		private sealed class GridOf2 : Panel
		{
			internal GridOf2(params Control[] cells)
			{
				BackColor = ShellPalette.Surface;
				Controls.AddRange(cells);
			}

			protected override void OnLayout(LayoutEventArgs levent)
			{
				int gap = 6, w = (Width - gap) / 2, h = (Height - gap) / 2;
				for (int i = 0; i < Controls.Count; i++)
					Controls[i].SetBounds(i % 2 * (w + gap), i / 2 * (h + gap), w, h);
			}
		}

		/// <summary>The segmented control's rounded outline.</summary>
		private sealed class PillPanel : Panel
		{
			internal PillPanel()
			{
				SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
				BackColor = ShellPalette.Window;
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				e.Graphics.Clear(ShellPalette.Surface);
				e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				using (var path = Graph.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 7))
				using (var b = new SolidBrush(ShellPalette.Window))
				using (var p = new Pen(ShellPalette.Border))
				{
					e.Graphics.FillPath(b, path);
					e.Graphics.DrawPath(p, path);
				}
			}
		}

		/// <summary>Title, subtitle and an edit pencil, with a hairline underneath.</summary>
		private sealed class InfoCard : Control
		{
			internal string Title { get; set; }
			internal string Subtitle { get; set; }
			internal bool CanEdit { get; set; }
			internal event EventHandler EditClicked;

			internal InfoCard()
			{
				SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
				Height = 44;
			}

			protected override void OnMouseClick(MouseEventArgs e)
			{
				if (CanEdit && e.X > Width - 28) EditClicked?.Invoke(this, EventArgs.Empty);
				base.OnMouseClick(e);
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				var g = e.Graphics;
				g.Clear(ShellPalette.Surface);
				TextRenderer.DrawText(g, Title, ShellPalette.UiBold, new Rectangle(4, 2, Width - 32, 20), ShellPalette.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
				TextRenderer.DrawText(g, Subtitle, ShellPalette.UiFont, new Rectangle(4, 21, Width - 32, 20), ShellPalette.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
				ShellIcons.Draw(g, ShellIcon.Pencil, new Rectangle(Width - 22, 12, 14, 14), CanEdit ? ShellPalette.Muted : ShellPalette.Border);
			}
		}

		/// <summary>A thin progress bar.</summary>
		private sealed class ProgressStrip : Control
		{
			private double value;
			internal double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

			internal ProgressStrip()
			{
				SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				e.Graphics.Clear(ShellPalette.Surface);
				using (var b = new SolidBrush(ShellPalette.BorderStrong)) e.Graphics.FillRectangle(b, 0, 0, Width, Height);
				using (var b = new SolidBrush(ShellPalette.Accent)) e.Graphics.FillRectangle(b, 0, 0, (int)(Width * value), Height);
			}
		}
	}
}
