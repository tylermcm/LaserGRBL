using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LaserGRBL
{
	internal sealed class CameraView : UserControl
	{
		private const int CaptureConnect = 0x40a;
		private const int CaptureDisconnect = 0x40b;
		private const int CapturePreview = 0x432;
		private const int CapturePreviewRate = 0x434;
		private const int CaptureScale = 0x435;
		private const int ChildVisible = 0x50000000;
		private readonly ComboBox devices;
		private readonly Label status;
		private readonly Panel viewport;
		private readonly Button toggle;
		private IntPtr capture;

		internal CameraView()
		{
			Text = "Live camera";
			devices = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Camera device" };
			toggle = new UserControls.GrblButton { Text = "Start camera", AutoSize = true };
			var refresh = new UserControls.GrblButton { Text = "Refresh devices", AutoSize = true };
			status = new Label { AutoSize = true, Padding = new Padding(4) };
			viewport = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
			var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
			bar.Controls.AddRange(new Control[] { devices, toggle, refresh });
			var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill, Padding = new Padding(8) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(bar, 0, 0);
			layout.Controls.Add(viewport, 0, 1);
			layout.Controls.Add(status, 0, 2);
			Controls.Add(layout);
			Load += (sender, args) => RefreshDevices();
			refresh.Click += (sender, args) => { StopCamera(); RefreshDevices(); };
			toggle.Click += (sender, args) => { if (capture == IntPtr.Zero) StartCamera(); else StopCamera(); };
			viewport.Resize += (sender, args) => ResizeCapture();
		}

		private void RefreshDevices()
		{
			devices.Items.Clear();
			for (int index = 0; index < 10; index++)
			{
				var name = new StringBuilder(128);
				var version = new StringBuilder(128);
				if (capGetDriverDescription(index, name, name.Capacity, version, version.Capacity))
					devices.Items.Add(new CaptureDevice { Index = index, Name = name.ToString() });
			}
			if (devices.Items.Count > 0) devices.SelectedIndex = 0;
			toggle.Enabled = devices.Items.Count > 0;
			status.Text = toggle.Enabled ? "Select a camera, then start the local preview." : "No compatible Windows capture driver found.";
		}

		private void StartCamera()
		{
			var device = devices.SelectedItem as CaptureDevice;
			if (device == null) return;
			capture = capCreateCaptureWindow("LaserGRBL camera", ChildVisible, 0, 0,
				Math.Max(1, viewport.Width), Math.Max(1, viewport.Height), viewport.Handle, 0);
			if (capture == IntPtr.Zero || SendMessage(capture, CaptureConnect, (IntPtr)device.Index, IntPtr.Zero) == IntPtr.Zero)
			{
				StopCamera();
				status.Text = "Could not open the camera. Check that the driver is available and the camera is free.";
				return;
			}
			SendMessage(capture, CaptureScale, (IntPtr)1, IntPtr.Zero);
			SendMessage(capture, CapturePreviewRate, (IntPtr)67, IntPtr.Zero);
			if (SendMessage(capture, CapturePreview, (IntPtr)1, IntPtr.Zero) == IntPtr.Zero)
			{
				StopCamera();
				status.Text = "The camera driver could not start preview.";
				return;
			}
			devices.Enabled = false;
			toggle.Text = "Stop camera";
			status.Text = "Live local preview";
			ResizeCapture();
		}

		private void ResizeCapture()
		{
			if (capture != IntPtr.Zero)
				MoveWindow(capture, 0, 0, Math.Max(1, viewport.Width), Math.Max(1, viewport.Height), true);
		}

		private void StopCamera()
		{
			if (capture != IntPtr.Zero)
			{
				SendMessage(capture, CapturePreview, IntPtr.Zero, IntPtr.Zero);
				SendMessage(capture, CaptureDisconnect, IntPtr.Zero, IntPtr.Zero);
				DestroyWindow(capture);
				capture = IntPtr.Zero;
			}
			devices.Enabled = true;
			toggle.Text = "Start camera";
			status.Text = "Camera stopped";
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && capture != IntPtr.Zero) StopCamera();
			base.Dispose(disposing);
		}

		internal void RefreshTheme()
		{
			BackColor = ColorScheme.FormBackColor;
			ForeColor = ColorScheme.FormForeColor;
			Invalidate(true);
		}

		private sealed class CaptureDevice
		{
			internal int Index;
			internal string Name;
			public override string ToString() { return Name; }
		}

		[DllImport("avicap32.dll", CharSet = CharSet.Ansi)]
		private static extern bool capGetDriverDescription(int index, StringBuilder name, int nameSize, StringBuilder version, int versionSize);
		[DllImport("avicap32.dll", CharSet = CharSet.Ansi)]
		private static extern IntPtr capCreateCaptureWindow(string name, int styles, int x, int y, int width, int height, IntPtr parent, int id);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll")]
		private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
		[DllImport("user32.dll")]
		private static extern bool DestroyWindow(IntPtr window);
	}
}
