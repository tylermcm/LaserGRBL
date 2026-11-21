using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL
{
	internal sealed class JobPlacementView : UserControl
	{
		private readonly NumericUpDown x = Coordinate();
		private readonly NumericUpDown y = Coordinate();
		private readonly NumericUpDown scale = new NumericUpDown { Minimum = 0.1m, Maximum = 1000, DecimalPlaces = 4, Value = 100, Width = 84 };
		private readonly NumericUpDown angle = new NumericUpDown { Minimum = -360, Maximum = 360, DecimalPlaces = 2, Width = 84 };
		private readonly NumericUpDown width = Dimension();
		private readonly NumericUpDown height = Dimension();
		private bool updatingSize;
		private readonly PlacementSurface surface;
		private readonly PointF anchor;
		internal JobPlacement Placement { get; private set; }
		internal event EventHandler ApplyRequested;
		internal event EventHandler CancelRequested;

		internal JobPlacementView(GrblFile file, JobOperation operation, JobPlacement current, bool allowRotation = true)
		{
			Text = "Place " + operation.Name;
			var original = new GrblFile();
			original.Commands.AddRange(operation.Source.Select(command => new GrblCommand(command.Command, 0, true)));
			original.Analyze();
			var trace = new JobTimeline(original, 0, original.Count);
			var paths = TracePaths(trace);
			if (paths.Count == 0) throw new InvalidOperationException("This operation has no engraved XY path to place.");
			anchor = current?.Anchor ?? new PointF((float)original.Range.DrawingRange.X.Min, (float)original.Range.DrawingRange.Y.Min);
			SetPosition(current?.Position ?? anchor);
			scale.Value = (decimal)(current?.Scale ?? 1) * 100;
			angle.Value = (decimal)(current?.Rotation ?? 0);
			decimal nativeWidth = original.Range.DrawingRange.Width;
			decimal nativeHeight = original.Range.DrawingRange.Height;
			BindSize(nativeWidth, nativeHeight);

			var context = new List<Tuple<PointF, PointF>>();
			if (file.Operations.Count > 1)
			{
				var whole = new JobTimeline(file, 0, file.Count);
				context = TracePaths(whole, operation.Start, operation.End);
			}
			surface = new PlacementSurface(paths, context, Current, SetPosition) { Dock = DockStyle.Fill };
			var fields = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
			AddField(fields, "Anchor X (mm)", x);
			AddField(fields, "Anchor Y (mm)", y);
			AddField(fields, "Scale (%)", scale);
			AddField(fields, "Width (mm)", width);
			AddField(fields, "Height (mm)", height);
			if (allowRotation) AddField(fields, "Rotation (°)", angle);
			var apply = new UserControls.GrblButton { Text = "Use placement", AutoSize = true };
			apply.Click += (sender, args) => { Placement = Current(); ApplyRequested?.Invoke(this, EventArgs.Empty); };
			var cancel = new UserControls.GrblButton { Text = "Cancel", AutoSize = true };
			cancel.Click += (sender, args) => CancelRequested?.Invoke(this, EventArgs.Empty);
			fields.Controls.Add(apply);
			fields.Controls.Add(cancel);
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 3, ColumnCount = 1 };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(new Label { AutoSize = true, Text = "Drag the gold artwork or enter exact values. Gray paths are other operations." }, 0, 0);
			layout.Controls.Add(surface, 0, 1);
			layout.Controls.Add(fields, 0, 2);
			Controls.Add(layout);
			foreach (var field in new[] { x, y, scale, angle })
				field.ValueChanged += (sender, args) => surface.Invalidate();
		}

		internal void RefreshTheme()
		{
			BackColor = ColorScheme.FormBackColor;
			ForeColor = ColorScheme.FormForeColor;
			Invalidate(true);
		}

		private void BindSize(decimal nativeWidth, decimal nativeHeight)
		{
			Action syncDimensions = () =>
			{
				updatingSize = true;
				width.Value = Math.Min(width.Maximum, nativeWidth * scale.Value / 100);
				height.Value = Math.Min(height.Maximum, nativeHeight * scale.Value / 100);
				updatingSize = false;
			};
			width.Enabled = nativeWidth > 0;
			height.Enabled = nativeHeight > 0;
			scale.ValueChanged += (sender, args) => syncDimensions();
			width.ValueChanged += (sender, args) =>
			{
				if (!updatingSize && nativeWidth > 0)
				{
					scale.Value = Math.Max(scale.Minimum, Math.Min(scale.Maximum, width.Value / nativeWidth * 100));
					syncDimensions();
				}
			};
			height.ValueChanged += (sender, args) =>
			{
				if (!updatingSize && nativeHeight > 0)
				{
					scale.Value = Math.Max(scale.Minimum, Math.Min(scale.Maximum, height.Value / nativeHeight * 100));
					syncDimensions();
				}
			};
			syncDimensions();
		}

		private static NumericUpDown Dimension()
		{
			return new NumericUpDown { Minimum = 0, Maximum = 100000, DecimalPlaces = 3, Increment = 0.1m, Width = 88 };
		}

		private static NumericUpDown Coordinate()
		{
			return new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 3, Increment = 0.1m, Width = 98 };
		}

		private void SetPosition(PointF point)
		{
			x.Value = Math.Max(x.Minimum, Math.Min(x.Maximum, (decimal)point.X));
			y.Value = Math.Max(y.Minimum, Math.Min(y.Maximum, (decimal)point.Y));
		}

		private JobPlacement Current()
		{
			return new JobPlacement(anchor, new PointF((float)x.Value, (float)y.Value), (double)scale.Value / 100, (double)angle.Value);
		}

		private static void AddField(FlowLayoutPanel panel, string name, NumericUpDown value)
		{
			value.AccessibleName = name;
			panel.Controls.Add(new Label { AutoSize = true, Text = name, Padding = new Padding(0, 5, 0, 0) });
			panel.Controls.Add(value);
		}

		private static List<Tuple<PointF, PointF>> TracePaths(JobTimeline trace, int excludeStart = -1, int excludeEnd = -1)
		{
			var paths = new List<Tuple<PointF, PointF>>();
			int stride = Math.Max(1, (trace.Moves.Count + 11999) / 12000);
			int eligible = 0;
			foreach (var move in trace.Moves)
			{
				if (!move.Burning || (move.CommandIndex >= excludeStart && move.CommandIndex < excludeEnd) || eligible++ % stride != 0)
					continue;
				int steps = move.Arc == null ? 1 : Math.Max(4, (int)(Math.Abs(move.Arc.AngularWidth) * 12));
				for (int step = 0; step < steps; step++)
					paths.Add(Tuple.Create(move.At((double)step / steps), move.At((double)(step + 1) / steps)));
			}
			return paths;
		}

		private sealed class PlacementSurface : Panel
		{
			private readonly IList<Tuple<PointF, PointF>> paths, context;
			private readonly Func<JobPlacement> current;
			private readonly Action<PointF> setPosition;
			private readonly RectangleF viewport;
			private Point dragStart;
			private PointF initialPosition;
			private bool dragging;
			private float pixelsPerMm;

			internal PlacementSurface(IList<Tuple<PointF, PointF>> paths, IList<Tuple<PointF, PointF>> context,
				Func<JobPlacement> current, Action<PointF> setPosition)
			{
				this.paths = paths;
				this.context = context;
				this.current = current;
				this.setPosition = setPosition;
				DoubleBuffered = true;
				BackColor = Color.FromArgb(25, 30, 38);
				Cursor = Cursors.SizeAll;
				var all = context.SelectMany(line => new[] { line.Item1, line.Item2 })
					.Concat(paths.SelectMany(line => new[] { current().Map(line.Item1), current().Map(line.Item2) })).ToArray();
				float left = Math.Min(0, all.Min(pt => pt.X)), bottom = Math.Min(0, all.Min(pt => pt.Y));
				float right = Math.Max((float)GrblCore.Configuration.TableWidth, all.Max(pt => pt.X));
				float top = Math.Max((float)GrblCore.Configuration.TableHeight, all.Max(pt => pt.Y));
				viewport = RectangleF.FromLTRB(left, bottom, right, top);
			}

			protected override void OnMouseDown(MouseEventArgs e)
			{
				base.OnMouseDown(e);
				if (e.Button != MouseButtons.Left) return;
				dragStart = e.Location;
				initialPosition = current().Position;
				dragging = Capture = true;
			}

			protected override void OnMouseMove(MouseEventArgs e)
			{
				base.OnMouseMove(e);
				if (dragging && Capture && pixelsPerMm > 0)
					setPosition(new PointF(initialPosition.X + (e.X - dragStart.X) / pixelsPerMm,
						initialPosition.Y - (e.Y - dragStart.Y) / pixelsPerMm));
			}

			protected override void OnMouseUp(MouseEventArgs e)
			{
				base.OnMouseUp(e);
				dragging = Capture = false;
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				base.OnPaint(e);
				pixelsPerMm = Math.Min(Math.Max(1, Width - 32) / Math.Max(1, viewport.Width), Math.Max(1, Height - 32) / Math.Max(1, viewport.Height));
				var placement = current();
				using (var background = new Pen(Color.DimGray))
				using (var foreground = new Pen(Color.Gold))
				{
					foreach (var segment in context)
						e.Graphics.DrawLine(background, Map(segment.Item1), Map(segment.Item2));
					foreach (var segment in paths)
						e.Graphics.DrawLine(foreground, Map(placement.Map(segment.Item1)), Map(placement.Map(segment.Item2)));
					var anchor = Map(placement.Position);
					e.Graphics.FillEllipse(Brushes.DeepSkyBlue, anchor.X - 3, anchor.Y - 3, 6, 6);
				}
			}

			private PointF Map(PointF point)
			{
				return new PointF(16 + (point.X - viewport.Left) * pixelsPerMm,
					16 + (viewport.Bottom - point.Y) * pixelsPerMm);
			}
		}
	}
}
