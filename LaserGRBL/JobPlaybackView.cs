using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LaserGRBL
{
	internal sealed class JobPlaybackView : UserControl
	{
		private readonly GrblCommand[] commands;
		private readonly Canvas canvas;
		private readonly TrackBar progress;
		private readonly Button play;
		private readonly Label timestamp;
		private readonly Label breakdown;
		private readonly Label currentCommand;
		private readonly ComboBox speeds;
		private readonly Timer timer;
		private readonly Stopwatch clock = new Stopwatch();
		private JobTimeline timeline;
		private double elapsed;
		private bool updatingSlider;
		private static readonly int[] Rates = { 1, 5, 10, 25, 50, 100, 250 };

		internal JobPlaybackView(GrblFile file, int first = 0, int end = -1)
		{
			commands = file.Commands.ToArray();
			Text = "Job playback";
			canvas = new Canvas { Dock = DockStyle.Fill };
			progress = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 10000, TickStyle = TickStyle.None };
			play = new UserControls.GrblButton { Text = "Play", AutoSize = true, Enabled = false };
			timestamp = new Label { AutoSize = true, Padding = new Padding(4, 6, 4, 0) };
			breakdown = new Label { AutoSize = true, Padding = new Padding(4, 6, 4, 0) };
			currentCommand = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(4, 3, 4, 0) };
			speeds = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
			foreach (int rate in Rates) speeds.Items.Add(rate + "×");
			speeds.SelectedIndex = 2;
			var operations = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, DropDownWidth = 480 };
			var sections = new List<JobPassSection>();
			bool selectedOnly = end >= 0;
			operations.Items.Add(selectedOnly ? "Selected operation" : "Entire job");
			foreach (var operation in file.Operations)
			{
				if (!operation.Enabled || (selectedOnly && (operation.Start != first || operation.End != end))) continue;
				if (!selectedOnly)
				{
					sections.Add(new JobPassSection(operation.Name, operation.Start, operation.End));
					operations.Items.Add(operation.Name);
				}
				sections.AddRange(operation.CompiledPasses);
				foreach (var pass in operation.CompiledPasses) operations.Items.Add(operation.Name + " · " + pass.Name);
			}
			operations.SelectedIndex = 0;
			var travel = new CheckBox { Text = "Show travel", Checked = true, AutoSize = true };
			var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
			bar.Controls.AddRange(new Control[] { play, speeds, operations, travel, timestamp });
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 5 };
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
			layout.Controls.Add(canvas, 0, 0);
			layout.Controls.Add(progress, 0, 1);
			layout.Controls.Add(bar, 0, 2);
			layout.Controls.Add(breakdown, 0, 3);
			layout.Controls.Add(currentCommand, 0, 4);
			Controls.Add(layout);
			timer = new Timer { Interval = 25 };
			timer.Tick += (sender, args) =>
			{
				elapsed = Math.Min(timeline.Duration, elapsed + clock.Elapsed.TotalSeconds * Rates[speeds.SelectedIndex]);
				clock.Restart();
				UpdatePosition();
				if (elapsed >= timeline.Duration) Pause();
			};
			play.Click += (sender, args) =>
			{
				if (timer.Enabled) { Pause(); return; }
				if (elapsed >= timeline.Duration) elapsed = 0;
				clock.Restart();
				timer.Start();
				play.Text = "Pause";
			};
			progress.ValueChanged += (sender, args) =>
			{
				if (updatingSlider || timeline == null) return;
				elapsed = timeline.Duration * progress.Value / progress.Maximum;
				clock.Restart();
				UpdatePosition();
			};
			travel.CheckedChanged += (sender, args) => { canvas.ShowTravel = travel.Checked; canvas.ResetImage(); };
			operations.SelectedIndexChanged += (sender, args) =>
			{
				if (operations.SelectedIndex == 0) { LoadTimeline(first, end < 0 ? commands.Length : end, operations); return; }
				var section = sections[operations.SelectedIndex - 1];
				LoadTimeline(section.Start, section.End, operations);
			};
			Load += (sender, args) => LoadTimeline(first, end < 0 ? commands.Length : end, operations);
		}

		internal void RefreshTheme()
		{
			BackColor = ColorScheme.FormBackColor;
			ForeColor = ColorScheme.FormForeColor;
			Invalidate(true);
		}

		private void LoadTimeline(int first, int end, ComboBox selection)
		{
			Pause();
			play.Enabled = progress.Enabled = selection.Enabled = false;
			UseWaitCursor = true;
			breakdown.Text = "Preparing playback...";
			var worker = new BackgroundWorker();
			worker.DoWork += (sender, args) => args.Result = new JobTimeline(commands, first, end);
			worker.RunWorkerCompleted += (sender, args) =>
			{
				worker.Dispose();
				if (IsDisposed) return;
				UseWaitCursor = false;
				progress.Enabled = selection.Enabled = true;
				if (args.Error != null)
				{
					breakdown.Text = args.Error.Message;
					return;
				}
				timeline = (JobTimeline)args.Result;
				elapsed = 0;
				var timing = timeline.Measure();
				breakdown.Text = string.Format("Estimated • Laser on {0}    Travel {1}    Waits {2}",
					Format(timing.Engraving), Format(timing.Travel), Format(timing.Waits));
				canvas.Timeline = timeline;
				canvas.ResetImage();
				play.Enabled = timeline.Duration > 0;
				UpdatePosition();
			};
			worker.RunWorkerAsync();
		}

		private static string Format(double seconds) { return JobOperationsEditor.FormatTime(TimeSpan.FromSeconds(seconds)); }

		private void UpdatePosition()
		{
			updatingSlider = true;
			progress.Value = timeline.Duration == 0 ? 0 : (int)(elapsed / timeline.Duration * progress.Maximum);
			updatingSlider = false;
			timestamp.Text = Format(elapsed) + " / " + Format(timeline.Duration);
			if (timeline.Moves.Count > 0)
			{
				int index = timeline.Moves[Math.Min(timeline.Seek(elapsed), timeline.Moves.Count - 1)].CommandIndex;
				currentCommand.Text = string.Format("Line {0:N0}    {1}", index + 1, commands[index].Command);
			}
			else currentCommand.Text = "";
			canvas.Elapsed = elapsed;
			canvas.Invalidate();
		}

		private void Pause()
		{
			timer.Stop();
			clock.Stop();
			play.Text = "Play";
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) timer?.Dispose();
			base.Dispose(disposing);
		}

		private sealed class Canvas : Panel
		{
			internal JobTimeline Timeline;
			internal double Elapsed;
			internal bool ShowTravel = true;
			private Bitmap image;
			private RectangleF bounds;
			private float scale;

			internal Canvas()
			{
				DoubleBuffered = true;
				BackColor = Color.FromArgb(25, 30, 38);
				Resize += (sender, args) => ResetImage();
			}

			internal void ResetImage()
			{
				image?.Dispose();
				image = null;
				Invalidate();
			}

			private PointF Map(PointF pt)
			{
				return new PointF((Width - bounds.Width * scale) / 2 + (pt.X - bounds.Left) * scale,
					(Height - bounds.Height * scale) / 2 + (bounds.Bottom - pt.Y) * scale);
			}

			private void CreateImage()
			{
				float left = float.MaxValue, bottom = float.MaxValue, right = float.MinValue, top = float.MinValue;
				foreach (var move in Timeline.Moves)
				{
					left = Math.Min(left, Math.Min(move.From.X, move.To.X));
					right = Math.Max(right, Math.Max(move.From.X, move.To.X));
					bottom = Math.Min(bottom, Math.Min(move.From.Y, move.To.Y));
					top = Math.Max(top, Math.Max(move.From.Y, move.To.Y));
					if (move.Arc != null)
					{
						left = Math.Min(left, (float)move.Arc.RectX);
						right = Math.Max(right, (float)(move.Arc.RectX + move.Arc.RectW));
						bottom = Math.Min(bottom, (float)move.Arc.RectY);
						top = Math.Max(top, (float)(move.Arc.RectY + move.Arc.RectH));
					}
				}
				bounds = RectangleF.FromLTRB(left, bottom, right, top);
				scale = Math.Min(Math.Max(1, Width - 40) / Math.Max(bounds.Width, 1), Math.Max(1, Height - 40) / Math.Max(bounds.Height, 1));
				image = new Bitmap(Width, Height);
				using (var graphics = Graphics.FromImage(image))
				using (var burn = new Pen(Color.FromArgb(133, 148, 164)))
				using (var travel = new Pen(Color.FromArgb(106, 71, 61)))
				{
					graphics.Clear(BackColor);
					int stride = Math.Max(1, (Timeline.Moves.Count + 29999) / 30000);
					int burnCount = 0, travelCount = 0;
					foreach (var move in Timeline.Moves)
					{
						int index = move.Burning ? burnCount++ : travelCount++;
						if (index % stride == 0) DrawMove(graphics, move, burn, travel, 1);
					}
				}
			}

			private void DrawMove(Graphics graphics, JobTimeline.Move move, Pen burn, Pen travel, double fraction)
			{
				if (move.Waiting || (!move.Burning && !ShowTravel)) return;
				Pen pen = move.Burning ? burn : travel;
				if (move.Arc == null)
					graphics.DrawLine(pen, Map(move.From), Map(move.At(fraction)));
				else
				{
					var previous = Map(move.From);
					int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(move.Arc.AngularWidth) * fraction * 8));
					for (int i = 1; i <= steps; i++)
					{
						var next = Map(move.At(fraction * i / steps));
						graphics.DrawLine(pen, previous, next);
						previous = next;
					}
				}
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				base.OnPaint(e);
				if (Timeline == null || Timeline.Moves.Count == 0 || Width <= 0 || Height <= 0) return;
				if (image == null) CreateImage();
				e.Graphics.DrawImageUnscaled(image, 0, 0);
				int next = Timeline.Seek(Elapsed);
				using (var burn = new Pen(Color.Gold))
				using (var travel = new Pen(Color.OrangeRed))
				{
					int stride = Math.Max(1, (next + 3999) / 4000);
					for (int i = 0; i < next; i += stride) DrawMove(e.Graphics, Timeline.Moves[i], burn, travel, 1);
					int current = Math.Min(next, Timeline.Moves.Count - 1);
					var move = Timeline.Moves[current];
					double fraction = move.End <= move.Start ? 1 : Math.Max(0, Math.Min(1, (Elapsed - move.Start) / (move.End - move.Start)));
					DrawMove(e.Graphics, move, burn, travel, fraction);
					PointF marker = Map(move.At(fraction));
					using (var brush = new SolidBrush(Color.FromArgb(120, Color.DeepSkyBlue)))
						e.Graphics.FillEllipse(brush, marker.X - 8, marker.Y - 8, 16, 16);
					e.Graphics.DrawEllipse(Pens.White, marker.X - 8, marker.Y - 8, 16, 16);
					e.Graphics.FillEllipse(Brushes.White, marker.X - 2, marker.Y - 2, 4, 4);
				}
			}

			protected override void Dispose(bool disposing)
			{
				if (disposing) image?.Dispose();
				base.Dispose(disposing);
			}
		}
	}
}
