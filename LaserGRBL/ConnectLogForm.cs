//Copyright (c) 2016-2021 Diego Settimi - https://github.com/arkypita/

// This program is free software; you can redistribute it and/or modify  it under the terms of the GPLv3 General Public License as published by  the Free Software Foundation; either version 3 of the License, or (at  your option) any later version.
// This program is distributed in the hope that it will be useful, but  WITHOUT ANY WARRANTY; without even the implied warranty of  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GPLv3  General Public License for more details.
// You should have received a copy of the GPLv3 General Public License  along with this program; if not, write to the Free Software  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307,  USA. using System;

using LaserGRBL.Icons;
using LaserGRBL.UserControls;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL
{
	/// <summary>
	/// Description of ConnectLogForm.
	/// </summary>
	public partial class ConnectLogForm : System.Windows.Forms.UserControl
	{
		private object[] baudRates = { 4800, 9600, 19200, 38400, 57600, 115200, 230400, 256000, 460800, 921600 };
		public ComWrapper.WrapperType currentWrapper;

		GrblCore Core;
		private PreviewForm jobPreview;
		private string mLoadedFileName;
		private Button BtnJobPasses;
		private Button BtnEditRaster;
		private Button BtnUndoRaster;
		private Button BtnRedoRaster;
		private Button BtnPlayback;
		private Button BtnPlaceRaster;
		private Button BtnCamera;
		private Label LblPassSummary;

		public ConnectLogForm()
		{
			currentWrapper = Settings.GetObject("ComWrapper Protocol", ComWrapper.WrapperType.UsbSerial);
			InitializeComponent();
			InitializePassControls();
        }

		private void InitializePassControls()
		{
			BtnJobPasses = CreateJobButton("Job passes");
			LblPassSummary = new Label { Text = "Same settings each pass", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
			var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
			panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			panel.Controls.Add(BtnJobPasses, 0, 0);
			panel.Controls.Add(LblPassSummary, 1, 0);
			tableLayoutPanel5.RowCount = 4;
			tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutPanel5.Controls.Add(panel, 0, 2);
			tableLayoutPanel5.SetColumnSpan(panel, tableLayoutPanel5.ColumnCount);
			var edits = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
			BtnEditRaster = CreateJobButton("Edit image");
			BtnUndoRaster = CreateJobButton("Undo");
			BtnRedoRaster = CreateJobButton("Redo");
			BtnPlayback = CreateJobButton("Playback");
			BtnPlaceRaster = CreateJobButton("Place");
			BtnCamera = CreateJobButton("Camera", true);
			edits.Controls.AddRange(new Control[] { BtnEditRaster, BtnUndoRaster, BtnRedoRaster, BtnPlaceRaster, BtnPlayback, BtnCamera });
			BtnPlaceRaster.Click += (sender, args) => PlaceRaster();
			BtnCamera.Click += (sender, args) =>
			{
				if (jobPreview.JobEditor is CameraView) CloseJobEditor();
				else if (jobPreview.JobEditor == null)
				{
					jobPreview.ShowJobEditor(new CameraView(), "Live camera", showBack: true, blocksRun: false);
					TimerUpdate();
				}
			};
			PassControls = panel;
			EditControls = edits;
			tableLayoutPanel5.Controls.Add(edits, 0, 3);
			tableLayoutPanel5.SetColumnSpan(edits, tableLayoutPanel5.ColumnCount);
			tableLayoutPanel5.Layout += (sender, args) => FitJobControls();
			FitJobControls();
			BtnEditRaster.Click += (sender, args) => RasterConverter.RasterToLaserForm.EditLoadedJob(jobPreview, Core);
			BtnUndoRaster.Click += (sender, args) => NavigateRasterHistory(GrblFile.RasterEdit.Undo);
			BtnRedoRaster.Click += (sender, args) => NavigateRasterHistory(GrblFile.RasterEdit.Redo);
			BtnPlayback.Click += (sender, args) =>
			{
				if (!Core.CanConfigurePasses || jobPreview.JobEditor is JobPlaybackView) return;
				jobPreview.ShowJobEditor(new JobPlaybackView(Core.LoadedFile), "Job playback", showBack: true);
				TimerUpdate();
			};
			BtnJobPasses.Click += (sender, args) => OpenJobEditor();
		}

		// ---- Used by the modern layout, which keeps this form hidden and drives it instead. ----

		/// <summary>The command log and manual command box.</summary>
		internal Control ConsoleControls { get { return GBCommands; } }

		internal bool ConsoleStyled { get; private set; }

		/// <summary>Dark console colours for the modern layout.</summary>
		internal void StyleConsole(Color log, Color input, Color text, Color hint)
		{
			ConsoleStyled = true;
			CmdLog.BackOverride = log;
			CmdLog.Invalidate();
			GBCommands.BackColor = tableLayoutPanel6.BackColor = log;
			TxtManualCommand.BackColor = input;
			TxtManualCommand.ForeColor = text;
			TxtManualCommand.WaterMarkColor = hint;
			TxtManualCommand.WaterMarkActiveColor = hint;
			TimerUpdate();
		}

		internal bool UsesSerialPort
		{
			get { return currentWrapper == ComWrapper.WrapperType.UsbSerial || currentWrapper == ComWrapper.WrapperType.UsbSerial2 || currentWrapper == ComWrapper.WrapperType.RJCPSerial; }
		}

		internal string ConnectionAddress { get { return UsesSerialPort ? CBPort.Text : TxtAddress.Text; } }

		internal object[] PortNames { get { return CBPort.Items.Cast<object>().ToArray(); } }

		internal object[] BaudRates { get { return CBSpeed.Items.Cast<object>().ToArray(); } }

		internal object SelectedBaud { get { return CBSpeed.SelectedItem; } }

		internal void SelectPort(string port)
		{
			if (CBPort.Items.Contains(port)) CBPort.SelectedItem = port;
			else CBPort.Text = port;
		}

		internal void SelectBaud(object baud)
		{
			if (CBSpeed.Items.Contains(baud)) CBSpeed.SelectedItem = baud;
		}

		internal void RefreshPorts() { InitPortCB(); }

		internal void EditImage() { RasterConverter.RasterToLaserForm.EditLoadedJob(jobPreview, Core); }
		internal void UndoImageEdit() { NavigateRasterHistory(GrblFile.RasterEdit.Undo); }
		internal void RedoImageEdit() { NavigateRasterHistory(GrblFile.RasterEdit.Redo); }
		internal void ShowPlayback()
		{
			if (!Core.CanConfigurePasses || jobPreview.JobEditor is JobPlaybackView) return;
			jobPreview.ShowJobEditor(new JobPlaybackView(Core.LoadedFile), "Job playback", showBack: true);
			TimerUpdate();
		}

		internal void ToggleCamera()
		{
			if (jobPreview.JobEditor is CameraView) CloseJobEditor();
			else if (jobPreview.JobEditor == null)
			{
				jobPreview.ShowJobEditor(new CameraView(), "Live camera", showBack: true, blocksRun: false);
				TimerUpdate();
			}
		}
		internal void PlaceImage() { PlaceRaster(); }

		/// <summary>Connects with the selected port, or disconnects (asking first if a job is running).</summary>
		internal void ToggleConnection()
		{
			ApplyConfig();
			BtnConnectDisconnectClick(this, EventArgs.Empty);
		}

		/// <summary>The job passes button and its summary, so the modern layout can show them in its own panel.</summary>
		internal Control PassControls { get; private set; }
		/// <summary>The image and job edit buttons (edit image, undo, redo, place, playback, camera).</summary>
		internal Control EditControls { get; private set; }

		internal void OpenJobEditor()
		{
			if (!Core.CanConfigurePasses || Core.JobEditorOpen) return;
			if (Core.HasJobOperations)
			{
				var operations = new JobOperationsEditor(Core, jobPreview);
				operations.CloseRequested += (sender, args) => CloseJobEditor();
				operations.StatusChanged += jobPreview.SetJobEditorStatus;
				jobPreview.ShowJobEditor(operations, "Job operations");
			}
			else
			{
				var source = Core.LoadedFile.RasterSource;
				decimal maxResolution = Settings.GetObject("Raster Hi-Res", false) ? 50 : 20;
				var editor = new JobPassEditor(source == null ? Core.Passes : source.RasterPasses,
					(int)Core.LoopCount, rasterDefaults: source?.DefaultRasterPass, maxResolution: maxResolution,
					supportsPower: source == null || source.RasterSupportsPower);
				editor.CancelRequested += (sender, args) => CloseJobEditor();
				editor.ApplyRequested += (sender, args) => ApplyJobPasses(editor, source);
				jobPreview.ShowJobEditor(editor, "Job passes");
			}
			TimerUpdate();
		}

		private void CloseJobEditor()
		{
			jobPreview.CloseJobEditor();
			TimerUpdate();
		}

		private void ApplyJobPasses(JobPassEditor editor, RasterConverter.ImageProcessor source)
		{
			if (!Core.CanConfigurePasses || source != Core.LoadedFile.RasterSource)
			{
				jobPreview.SetJobEditorStatus("Wait until the loaded job is ready before applying changes.");
				return;
			}

			try
			{
				if (source == null)
				{
					Core.ConfigurePasses(editor.Plan);
					CloseJobEditor();
					return;
				}
				editor.Enabled = false;
				jobPreview.SetJobEditorStatus("Building passes...");
				Core.RebuildRasterPasses(source, editor.Plan, error =>
				{
					if (IsDisposed || editor.IsDisposed) return;
					if (error == null) CloseJobEditor();
					else
					{
						editor.Enabled = true;
						jobPreview.SetJobEditorStatus(error.Message);
						TimerUpdate();
					}
				});
			}
			catch (Exception ex)
			{
				editor.Enabled = true;
				jobPreview.SetJobEditorStatus(ex.Message);
			}
		}

		private void FitJobControls()
		{
			int width = tableLayoutPanel5.ClientSize.Width;
			if (width <= 0) return;
			int height = tableLayoutPanel5.GetPreferredSize(new Size(width, 0)).Height;
			if (GBFile.MinimumSize.Height != height)
				GBFile.MinimumSize = new Size(0, height);
		}

		private static GrblButton CreateJobButton(string caption, bool enabled = false)
		{
			return new GrblButton
			{
				Text = caption, Enabled = enabled, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Margin = new Padding(1), Padding = new Padding(4, 0, 4, 0), MinimumSize = new Size(0, 23)
			};
		}

		private void PlaceRaster()
		{
			var file = Core.LoadedFile;
			var source = file.RasterSource;
			if (!Core.CanConfigurePasses || source == null || file.Operations.Count != 1) return;
			try
			{
				var view = new JobPlacementView(file, file.Operations[0], null, false);
				view.CancelRequested += (sender, args) => CloseJobEditor();
				view.ApplyRequested += (sender, args) =>
				{
					var placement = view.Placement;
					var draft = source.CopyRasterSource();
					draft.TargetOffset = placement.Map(source.TargetOffset);
					draft.TargetSize = new SizeF((float)(source.TargetSize.Width * placement.Scale), (float)(source.TargetSize.Height * placement.Scale));
					if (draft.RasterSettings != null)
					{
						draft.RasterSettings = new System.Collections.Generic.Dictionary<string, object>(draft.RasterSettings);
						draft.RasterSettings["GrayScaleConversion.Gcode.ImageSize.W"] = draft.TargetSize.Width;
						draft.RasterSettings["GrayScaleConversion.Gcode.ImageSize.H"] = draft.TargetSize.Height;
						draft.RasterSettings["GrayScaleConversion.Gcode.Offset.X"] = draft.TargetOffset.X;
						draft.RasterSettings["GrayScaleConversion.Gcode.Offset.Y"] = draft.TargetOffset.Y;
					}
					view.Enabled = false;
					jobPreview.SetJobEditorStatus("Rebuilding job...");
					Core.ApplyRasterEdit(source, draft, GrblFile.RasterEdit.Apply, error =>
					{
						if (view.IsDisposed) return;
						if (error == null) CloseJobEditor();
						else
						{
							view.Enabled = true;
							jobPreview.SetJobEditorStatus(error.Message);
						}
					});
				};
				jobPreview.ShowJobEditor(view, view.Text);
				TimerUpdate();
			}
			catch (Exception ex) { OnRasterEditCompleted(ex); }
		}

		private void NavigateRasterHistory(GrblFile.RasterEdit edit)
		{
			if (!Core.CanConfigurePasses) return;
			var file = Core.LoadedFile;
			var target = file.GetRasterHistory(edit);
			if (target == null) return;
			BtnJobPasses.Text = "Rebuilding job...";
			Core.ApplyRasterEdit(file.RasterSource, target, edit, OnRasterEditCompleted);
			TimerUpdate();
		}

		private void OnRasterEditCompleted(Exception error)
		{
			if (IsDisposed) return;
			BtnJobPasses.Text = "Job passes...";
			if (error != null) MessageBox.Show(ParentForm, error.Message, "Edit raster job", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			TimerUpdate();
		}

		public void SetCore(GrblCore core, PreviewForm preview)
		{
			Core = core;
			jobPreview = preview;
			Core.OnFileLoaded += OnFileLoaded;
			Core.OnLoopCountChange += OnLoopCountChanged;
			CmdLog.SetCom(core);

			PB.Bars.Add(new LaserGRBL.UserControls.DoubleProgressBar.Bar(ColorScheme.PreviewCommandWait));
			PB.Bars.Add(new LaserGRBL.UserControls.DoubleProgressBar.Bar(ColorScheme.PreviewCommandOK));
			Size btnSize = new Size(20, 20);
			IconsMgr.PrepareButton(BtnRunProgram, "mdi-play-circle", btnSize);
            IconsMgr.PrepareButton(BtnAbortProgram, "mdi-stop-circle", btnSize);
            IconsMgr.PrepareButton(BtnConnectDisconnect, "mdi-power-plug", btnSize, "mdi-power-plug-off");
            IconsMgr.PrepareButton(BtnOpen, "mdi-folder", btnSize);

            InitSpeedCB();
			InitPortCB();

			RestoreConf();

			TimerUpdate();
		}

		void OnLoopCountChanged(decimal current)
		{
			if (InvokeRequired)
			{
				Invoke(new GrblCore.dlgOnLoopCountChange(OnLoopCountChanged), current);
			}
			else
			{
				if (UDLoopCounter.Value != current)
					UDLoopCounter.Value = current;
			}
		}

		private void RestoreConf()
		{
			CBSpeed.SelectedItem = Settings.GetObject("Serial Speed", 115200);

			if (currentWrapper == ComWrapper.WrapperType.Telnet)
				TxtAddress.Text = Settings.GetObject("Telnet Address", "127.0.0.1:23");	
			else if (currentWrapper == ComWrapper.WrapperType.LaserWebESP8266)
				TxtAddress.Text = Settings.GetObject("Websocket URL", "ws://127.0.0.1:81/"); 
		}

		void OnFileLoaded(long elapsed, string filename)
		{
			if (InvokeRequired)
			{
				Invoke(new GrblFile.OnFileLoadedDlg(OnFileLoaded), elapsed, filename);
			}
			else
			{
				jobPreview.CloseJobEditorOnFileLoad();
				mLoadedFileName = filename;
				TbFileName.Text = System.IO.Path.GetFileName(filename);
			}
		}

		private void InitSpeedCB() //Baud Rates combo box
		{
			CBSpeed.BeginUpdate();
			CBSpeed.Items.AddRange(baudRates);
			CBSpeed.EndUpdate();
		}

		private void InitPortCB() //Availabe Ports combo box
		{
			string currentport = CBPort.SelectedItem as string;
			CBPort.BeginUpdate();
			CBPort.Items.Clear();

			foreach (string portname in System.IO.Ports.SerialPort.GetPortNames())
			{
				string purgename = portname;

				//FIX https://github.com/arkypita/LaserGRBL/issues/31

				if (!char.IsDigit(purgename[purgename.Length - 1]))
					purgename = purgename.Substring(0, purgename.Length - 1);

				CBPort.Items.Add(purgename);
			}

			if (currentport != null && CBPort.Items.Contains(currentport))
				CBPort.SelectedItem = currentport;
			else if (CBPort.Items.Count > 0)
				CBPort.SelectedIndex = CBPort.Items.Count -1;
			CBPort.EndUpdate();
		}

		//private static System.Text.RegularExpressions.Regex ComRX = new System.Text.RegularExpressions.Regex(@"(?'wholecom'(?:^|[ (])COM(?'comno'\d+)(?:[) ]|$))", System.Text.RegularExpressions.RegexOptions.Compiled);
		//private System.Collections.Generic.SortedDictionary<int, string> GetPortDictionary()
		//{
		//	System.Collections.Generic.SortedDictionary<int, string> rv = new System.Collections.Generic.SortedDictionary<int, string>();
		//
		//	try //add using managment object
		//	{
		//		using (System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher(@"\\.\root\cimv2", "SELECT * FROM Win32_PnPEntity"))
		//		{
		//			System.Management.ManagementObjectCollection moc = searcher.Get();
		//			foreach (System.Management.ManagementObject mo in moc)
		//			{
		//				string caption = (string)mo["Caption"];
		//				if (caption != null && ComRX.IsMatch(caption))
		//				{
		//					System.Text.RegularExpressions.Match m = ComRX.Match(caption);
		//					if (m != null && m.Groups["comno"] != null)
		//					{
		//						int no = int.Parse(m.Groups["comno"].Value);
		//						string wholecom = m.Groups["wholecom"].Value;
		//						if (!rv.ContainsKey(no))
		//							rv.Add(int.Parse(m.Groups["comno"].Value), caption.Replace(wholecom, "").Trim());
		//					}
		//				}
		//			}
		//
		//		}
		//	}
		//	catch { }
		//
		//	try //add using SerialPort.GetPortNames 
		//	{
		//		foreach (string dirty in System.IO.Ports.SerialPort.GetPortNames())
		//		{
		//			string comno = dirty;
		//			if (!char.IsDigit(comno[comno.Length - 1]))
		//				comno = comno.Substring(0, comno.Length - 1);
		//
		//			string caption = comno;
		//			if (caption != null && ComRX.IsMatch(caption))
		//			{
		//				System.Text.RegularExpressions.Match m = ComRX.Match(caption);
		//				if (m != null && m.Groups["comno"] != null)
		//				{
		//					int no = int.Parse(m.Groups["comno"].Value);
		//					if (!rv.ContainsKey(no))
		//						rv.Add(int.Parse(m.Groups["comno"].Value), "Generic COM Port");
		//				}
		//			}
		//		}
		//	}
		//	catch { }
		//
		//	return rv;
		//}


		void BtnConnectDisconnectClick(object sender, EventArgs e)
		{
			if (Core.MachineStatus == GrblCore.MacStatus.Disconnected)
				Core.OpenCom();
			else if (!(Core.InProgram && System.Windows.Forms.MessageBox.Show(Strings.DisconnectAnyway, Strings.WarnMessageBoxHeader, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != System.Windows.Forms.DialogResult.Yes))
				Core.CloseCom(true);

			TimerUpdate();
		}


		void ApplyConfig()
		{
			if ((currentWrapper == ComWrapper.WrapperType.UsbSerial || currentWrapper == ComWrapper.WrapperType.UsbSerial2 || currentWrapper == ComWrapper.WrapperType.RJCPSerial) && CBPort.Text != null && CBSpeed.SelectedItem != null)
				Core.Configure(currentWrapper, CBPort.Text, (int)CBSpeed.SelectedItem);
			else if (currentWrapper == ComWrapper.WrapperType.Telnet || currentWrapper == ComWrapper.WrapperType.LaserWebESP8266)
				Core.Configure(currentWrapper, (string)TxtAddress.Text);
			else if (currentWrapper == ComWrapper.WrapperType.Emulator)
				Core.Configure(currentWrapper);
		}

		void BtnOpenClick(object sender, EventArgs e)
		{
			Core.OpenFile();
		}

		void BtnRunProgramClick(object sender, EventArgs e)
		{
			BtnRunProgram.Enabled = false;
			Core.RunProgram(ParentForm);
		}
		void TxtManualCommandCommandEntered(string command)
		{
			Core.EnqueueCommand(new GrblCommand(command, 0, true));
		}
		
		public void TimerUpdate()
		{
			SuspendLayout();

			if (!Core.IsConnected && System.IO.Ports.SerialPort.GetPortNames().Length != CBPort.Items.Count)
				InitPortCB();
			
			PB.Maximum = Core.ProgramTarget;
			PB.Bars[0].Value = Core.ProgramSent;
			PB.Bars[1].Value = Core.ProgramExecuted;

			string val = Tools.Utils.TimeSpanToString(Core.ProgramTime, Tools.Utils.TimePrecision.Minute, Tools.Utils.TimePrecision.Second, " ,", true);

			if (val != "now")
				PB.PercString = val;
			else if (Core.InProgram)
				PB.PercString = "0 sec";
			else
				PB.PercString = "";
			
			PB.Invalidate();

			/*
			Idle: All systems are go, no motions queued, and it's ready for anything.
			Run: Indicates a cycle is running.
			Hold: A feed hold is in process of executing, or slowing down to a stop. After the hold is complete, Grbl will remain in Hold and wait for a cycle start to resume the program.
			Door: (New in v0.9i) This compile-option causes Grbl to feed hold, shut-down the spindle and coolant, and wait until the door switch has been closed and the user has issued a cycle start. Useful for OEM that need safety doors.
			Home: In the middle of a homing cycle. NOTE: Positions are not updated live during the homing cycle, but they'll be set to the home position once done.
			Alarm: This indicates something has gone wrong or Grbl doesn't know its position. This state locks out all G-code commands, but allows you to interact with Grbl's settings if you need to. '$X' kill alarm lock releases this state and puts Grbl in the Idle state, which will let you move things again. As said before, be cautious of what you are doing after an alarm.
			Check: Grbl is in check G-code mode. It will process and respond to all G-code commands, but not motion or turn on anything. Once toggled off with another '$C' command, Grbl will reset itself.
			*/

			TT.SetToolTip(BtnConnectDisconnect, Core.IsConnected ? Strings.BtnDisconnectTT : Strings.BtnConnectTT);
			
			BtnConnectDisconnect.UseAltImage = Core.IsConnected;
			BtnRunProgram.Enabled = Core.CanSendFile;
            BtnRunProgram.Visible = !Core.CanAbortProgram;
            BtnAbortProgram.Visible = Core.CanAbortProgram;
			BtnOpen.Enabled = Core.CanOpenJobFile;
			bool canOpenEditor = Core.CanConfigurePasses && !Core.JobEditorOpen;
			BtnJobPasses.Enabled = canOpenEditor;
			BtnEditRaster.Enabled = canOpenEditor && Core.LoadedFile.RasterSource != null;
			BtnPlaceRaster.Enabled = BtnEditRaster.Enabled && Core.LoadedFile.Operations.Count == 1;
			BtnUndoRaster.Enabled = BtnEditRaster.Enabled && Core.LoadedFile.CanUndoRasterEdit;
			BtnRedoRaster.Enabled = BtnEditRaster.Enabled && Core.LoadedFile.CanRedoRasterEdit;
			BtnPlayback.Enabled = canOpenEditor;
			BtnCamera.Enabled = !Core.JobEditorOpen;
			var rasterPasses = Core.LoadedFile.RasterSource?.RasterPasses;
			UDLoopCounter.Enabled = !Core.JobEditorOpen && Core.Passes == null && rasterPasses == null && !Core.HasJobOperations;
			string powerSummary = "Same settings each pass";
			if (Core.HasJobOperations)
			{
				BtnJobPasses.Text = "Operations";
				powerSummary = string.Format("{0} operations in this job", Core.LoadedFile.Operations.Count);
			}
			else if (Core.CanConfigurePasses)
				BtnJobPasses.Text = "Job passes";
			if (rasterPasses != null)
				powerSummary = string.Format("{0} raster passes in this job", rasterPasses.Count);
			if (Core.Passes != null)
			{
				var values = new System.Collections.Generic.List<string>();
				for (int i = 0; i < Core.Passes.Count; i++) values.Add((i + 1) + ": " + Core.Passes[i]);
				powerSummary = Core.InProgram
					? string.Format("Pass {0}/{1}: {2}", Core.CurrentPassNumber, Core.Passes.Count, Core.CurrentPassSettings)
					: string.Join("; ", values.ToArray());
			}
			if (LblPassSummary.Text != powerSummary)
			{
				LblPassSummary.Text = powerSummary;
				TT.SetToolTip(LblPassSummary, powerSummary + " (of imported settings)");
			}

			bool old = TxtManualCommand.Enabled;
			if (ConsoleStyled)
			{
				// Disabled text boxes paint light grey; stay enabled and read-only so the dark style holds.
				TxtManualCommand.Enabled = true;
				TxtManualCommand.ReadOnly = !Core.CanSendManualCommand;
				TxtManualCommand.WaterMark = Core.CanSendManualCommand ? "Type a G-code command and press Enter" : "Connect a laser to send commands";
			}
			else
				TxtManualCommand.Enabled = Core.CanSendManualCommand;
			//if (old == false && TxtManualCommand.Enabled == true)
			//	TxtManualCommand.Focus();

			//CBProtocol.Enabled = !Core.IsOpen;
			CBPort.Enabled = !Core.IsConnected;
			CBSpeed.Enabled = !Core.IsConnected;
			TxtAddress.Enabled = !Core.IsConnected;

			CmdLog.TimerUpdate();

			if (!Core.IsConnected)
			{
				ComWrapper.WrapperType actualWrapper = Settings.GetObject("ComWrapper Protocol", ComWrapper.WrapperType.UsbSerial);
				if (actualWrapper != currentWrapper)
				{
					currentWrapper = actualWrapper;
					UpdateConf();
				}
			}

			ResumeLayout();
		}

		private void CBPort_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateConf();
		}

		private void CBPort_TextChanged(object sender, EventArgs e)
		{
			UpdateConf();
		}

		private void CBSpeed_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateConf();
		}

		
		private void UpdateConf()
		{
			tableLayoutPanel4.SuspendLayout();
			CBPort.Visible = CBSpeed.Visible = LblComPort.Visible = LblBaudRate.Visible = (currentWrapper == ComWrapper.WrapperType.UsbSerial || currentWrapper == ComWrapper.WrapperType.UsbSerial2 || currentWrapper == ComWrapper.WrapperType.RJCPSerial);
			TxtAddress.Visible = LblAddress.Visible = (currentWrapper == ComWrapper.WrapperType.Telnet || currentWrapper == ComWrapper.WrapperType.LaserWebESP8266);
			LblAddress.Text = (currentWrapper == ComWrapper.WrapperType.Telnet ? "IP:PORT" : "Socket URL");
			TxtEmulator.Visible = LblEmulator.Visible = (currentWrapper == ComWrapper.WrapperType.Emulator);
			tableLayoutPanel4.ResumeLayout();

			if (CBSpeed.SelectedItem != null)
				Settings.SetObject("Serial Speed", CBSpeed.SelectedItem);

			if (!string.IsNullOrWhiteSpace(TxtAddress.Text))
			{
				if (currentWrapper == ComWrapper.WrapperType.Telnet)
					Settings.SetObject("Telnet Address", TxtAddress.Text);
				else if (currentWrapper == ComWrapper.WrapperType.LaserWebESP8266)
					Settings.SetObject("Websocket URL", TxtAddress.Text);
			}

			ApplyConfig();
		}

		private void CBProtocol_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateConf();
		}

		private void TxtHostName_TextChanged(object sender, EventArgs e)
		{
			UpdateConf();
		}

		private void ITcpPort_CurrentValueChanged(object sender, int NewValue, bool ByUser)
		{
			UpdateConf();
		}

		private void UDLoopCounter_ValueChanged(object sender, EventArgs e)
		{
			Core.LoopCount = UDLoopCounter.Value;
		}

		internal void OnColorChange()
		{
			tableLayoutPanel5.Invalidate(true);
			TbFileName.BackColor = ColorScheme.LogBackColor;
            TbFileName.ForeColor = ColorScheme.FormForeColor;

            TbFileName.BorderColor = ColorScheme.ControlsBorder;
            TbFileName.ForeColor = ColorScheme.FormForeColor;

            TxtManualCommand.WaterMarkColor = ColorScheme.ControlsBorder;
            TxtManualCommand.WaterMarkActiveColor = ColorScheme.ControlsBorder;
            TxtManualCommand.BackColor = ColorScheme.LogBackColor;
			TxtManualCommand.ForeColor = ColorScheme.FormForeColor;

			PB.ForeColor = ColorScheme.FormForeColor;
            PB.FillColor = ColorScheme.LogBackColor;
            PB.Bars.Clear();
			PB.Bars.Add(new LaserGRBL.UserControls.DoubleProgressBar.Bar(ColorScheme.PreviewCommandWait));
			PB.Bars.Add(new LaserGRBL.UserControls.DoubleProgressBar.Bar(ColorScheme.PreviewCommandOK));
            CmdLog.OnColorChange();
            CmdLog.Invalidate();
		}

		private void TxtManualCommand_Enter(object sender, EventArgs e)
		{
			Core.SuspendHK = true;
		}

		private void TxtManualCommand_Leave(object sender, EventArgs e)
		{
			Core.SuspendHK = false;
		}

		private void TbFileName_MouseEnter(object sender, EventArgs e)
		{
			if (mLoadedFileName != null)
				TT.Show(mLoadedFileName, TbFileName, 5000);
		}

		private void TbFileName_MouseLeave(object sender, EventArgs e)
		{
			TT.Hide(TbFileName);
		}

        private void BtnAbortProgram_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Strings.BoxAbortProgramConfirm, Strings.WarnMessageBoxHeader, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == System.Windows.Forms.DialogResult.Yes)
                Core.AbortProgram();
        }

		internal void ConfigFromDiscovery(string config)
		{
			if (TxtAddress.Visible && TxtAddress.Enabled && config != null)
			{
				TxtAddress.Text = config;
				Application.DoEvents();

				if (BtnConnectDisconnect.Enabled && Core.MachineStatus == GrblCore.MacStatus.Disconnected)
					BtnConnectDisconnectClick(null, null);
			}
		}

		internal void ConfigFromWiFiForm(string config)
		{
			if (config != null)
			{
				TxtAddress.Text = config;
				Application.DoEvents();

				//if (BtnConnectDisconnect.Enabled && Core.MachineStatus == GrblCore.MacStatus.Disconnected)
				//	BtnConnectDisconnectClick(null, null);
			}
        }

        private void CmdLog_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            ControlPaint.DrawBorder(e.Graphics, e.ClipRectangle, ColorScheme.ControlsBorder, ButtonBorderStyle.Solid);
        }

    }
}
