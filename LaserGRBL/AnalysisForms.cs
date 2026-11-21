using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL
{
	internal static class JobTools
	{
		internal static double RapidFeed()
		{
			try { return (double)Math.Min(GrblCore.Configuration.MaxRateX, GrblCore.Configuration.MaxRateY); }
			catch (Exception) { return 4000; }
		}

		internal static void ThemeForm(Form form)
		{
			form.Load += (sender, args) =>
			{
				UserControls.ThemeMgr.SetTheme(form, true);
				form.BackColor = ColorScheme.FormBackColor;
				form.ForeColor = ColorScheme.FormForeColor;
			};
		}

		internal static string Time(TimeSpan t)
		{
			return string.Format(CultureInfo.CurrentCulture, "{0:0}:{1:00}:{2:00}", Math.Floor(t.TotalHours), t.Minutes, t.Seconds);
		}

		/// <summary>Blue → green → yellow → red for 0..1.</summary>
		internal static Color Heat(double t)
		{
			t = Math.Max(0, Math.Min(1, t));
			Color[] stops = { Color.FromArgb(40, 60, 200), Color.FromArgb(30, 180, 90), Color.FromArgb(240, 220, 40), Color.FromArgb(220, 30, 30) };
			double p = t * (stops.Length - 1);
			int i = Math.Min(stops.Length - 2, (int)p);
			double f = p - i;
			Func<int, int, int> mix = (a, b) => (int)Math.Round(a + (b - a) * f);
			return Color.FromArgb(mix(stops[i].R, stops[i + 1].R), mix(stops[i].G, stops[i + 1].G), mix(stops[i].B, stops[i + 1].B));
		}
	}

	/// <summary>Shows where the job puts the most laser energy per area.</summary>
	internal sealed class HeatmapForm : Form
	{
		private readonly GrblCommand[] commands;
		private readonly PictureBox picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
		private readonly Label info = new Label { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), MaximumSize = new Size(700, 0) };
		private readonly NumericUpDown cell = new NumericUpDown { Minimum = 0.1m, Maximum = 20, DecimalPlaces = 1, Increment = 0.5m, Value = 1, Width = 70 };

		private HeatmapForm(GrblCommand[] commands)
		{
			this.commands = commands;
			Text = "Energy heatmap";
			Size = new Size(760, 640);
			StartPosition = FormStartPosition.CenterParent;
			var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
			top.Controls.Add(new Label { Text = "Cell size (mm)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
			top.Controls.Add(cell);
			var refresh = new UserControls.GrblButton { Text = "Refresh", AutoSize = true };
			refresh.Click += (sender, args) => Render();
			top.Controls.Add(refresh);
			Controls.Add(picture);
			Controls.Add(info);
			Controls.Add(top);
			JobTools.ThemeForm(this);
			Shown += (sender, args) => Render();
			FormClosed += (sender, args) => picture.Image?.Dispose();
		}

		private void Render()
		{
			try
			{
				var map = EnergyMap.Build(commands, (double)cell.Value);
				var bitmap = new Bitmap(map.Columns, map.Rows);
				var burned = new List<double>();
				for (int x = 0; x < map.Columns; x++)
					for (int y = 0; y < map.Rows; y++)
					{
						double v = map.Cells[x, y];
						if (v > 0) burned.Add(v);
						// Square root spreads the low end, so light engraving still shows against the hot spots.
						bitmap.SetPixel(x, map.Rows - 1 - y, v <= 0 ? Color.White : JobTools.Heat(Math.Sqrt(v / map.Max)));
					}
				burned.Sort();
				double median = burned.Count > 0 ? burned[burned.Count / 2] : 0;
				picture.Image?.Dispose();
				picture.Image = bitmap;
				picture.Paint -= PaintNearest;
				picture.Paint += PaintNearest;
				info.Text = string.Format(CultureInfo.CurrentCulture,
					"Each pixel is {0:0.#} mm. Blue is the least energy, red the most. The hottest cell gets {1:0.#}× the energy of a typical burned cell; " +
					"overlapping passes, slow corners and repeated lines show up red, which is where material scorches or burns through.",
					map.CellMm, median > 0 ? map.Max / median : 1);
			}
			catch (InvalidOperationException ex) { info.Text = ex.Message; }
		}

		private void PaintNearest(object sender, PaintEventArgs e)
		{
			// Redraw without smoothing so cells stay sharp when zoomed.
			if (picture.Image == null) return;
			e.Graphics.Clear(picture.BackColor);
			e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
			e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
			var image = picture.Image;
			float scale = Math.Min((float)picture.Width / image.Width, (float)picture.Height / image.Height);
			float w = image.Width * scale, h = image.Height * scale;
			e.Graphics.DrawImage(image, (picture.Width - w) / 2, (picture.Height - h) / 2, w, h);
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			using (var form = new HeatmapForm(core.LoadedFile.ToArray())) form.ShowDialog(owner);
		}
	}

	/// <summary>Price of the loaded job from machine time, laser energy and material.</summary>
	internal sealed class CostForm : SimpleDialog
	{
		private CostForm(GrblCore core) : base("Cost and time estimate")
		{
			var rates = CostRates.Load();
			var stats = JobStats.Measure(core.LoadedFile.ToList(), JobTools.RapidFeed());
			TimeSpan jobTime = core.LoadedFile.EstimatedTime > TimeSpan.Zero ? core.LoadedFile.EstimatedTime : stats.Total;
			AddNote(string.Format(CultureInfo.CurrentCulture, "This job: {0} estimated, laser on for {1}, {2:0.#} × {3:0.#} mm.",
				JobTools.Time(jobTime), JobTools.Time(stats.BurnTime), stats.Width, stats.Height), 6);
			var machine = AddNumber("Machine rate per hour", 0, 100000, rates.MachinePerHour, 2, 1);
			var watts = AddNumber("Laser power draw (W)", 0, 100000, rates.LaserWatts, 0, 5);
			var energy = AddNumber("Electricity per kWh", 0, 1000, rates.EnergyPerKwh, 3, 0.05m);
			var material = AddNumber("Material per m²", 0, 100000, rates.MaterialPerM2, 2, 1);
			var margin = AddNumber("Material margin (mm)", 0, 1000, rates.MarginMm, 1, 1);
			var setup = AddNumber("Setup cost (whole batch)", 0, 100000, rates.SetupCost, 2, 1);
			var quantity = AddNumber("Quantity", 1, 100000, rates.Quantity);
			var currency = AddField("Currency symbol", new TextBox { Text = rates.Currency, Width = 60 });
			var result = AddNote("");
			Action calculate = () =>
			{
				rates = new CostRates
				{
					MachinePerHour = machine.Value, LaserWatts = watts.Value, EnergyPerKwh = energy.Value, MaterialPerM2 = material.Value,
					MarginMm = margin.Value, SetupCost = setup.Value, Quantity = (int)quantity.Value, Currency = currency.Text.Trim()
				};
				rates.Save();
				result.Text = CostEstimate.For(jobTime, stats.BurnTime, stats.Width, stats.Height, rates).Format(rates.Currency);
			};
			AddButton("Close", Close, cancel: true);
			AddButton("Calculate", calculate, accept: true);
			Load += (sender, args) => calculate();
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			using (var form = new CostForm(core)) form.ShowDialog(owner);
		}
	}

	/// <summary>Side-by-side totals and an overlay of the loaded job and another G-code file.</summary>
	internal sealed class CompareForm : Form
	{
		private readonly List<ToolpathSegment> current, other;
		private readonly string otherName;

		private CompareForm(GrblCommand[] loaded, GrblCommand[] compared, string comparedName)
		{
			otherName = comparedName;
			current = ToolpathWalker.Segments(loaded).Where(s => s.Burn).ToList();
			other = ToolpathWalker.Segments(compared).Where(s => s.Burn).ToList();
			Text = "Compare · " + comparedName;
			Size = new Size(900, 640);
			StartPosition = FormStartPosition.CenterParent;
			double rapid = JobTools.RapidFeed();
			var a = JobStats.Measure(loaded, rapid);
			var b = JobStats.Measure(compared, rapid);
			var grid = new DataGridView
			{
				Dock = DockStyle.Left, Width = 380, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
				AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect
			};
			grid.Columns.Add("What", "");
			grid.Columns.Add("Loaded", "Loaded job");
			grid.Columns.Add("Other", comparedName);
			var c = CultureInfo.CurrentCulture;
			grid.Rows.Add("Lines", a.Lines.ToString("N0", c), b.Lines.ToString("N0", c));
			grid.Rows.Add("Burn length (m)", (a.BurnMm / 1000).ToString("0.##", c), (b.BurnMm / 1000).ToString("0.##", c));
			grid.Rows.Add("Travel length (m)", (a.TravelMm / 1000).ToString("0.##", c), (b.TravelMm / 1000).ToString("0.##", c));
			grid.Rows.Add("Burn time", JobTools.Time(a.BurnTime), JobTools.Time(b.BurnTime));
			grid.Rows.Add("Travel time", JobTools.Time(a.TravelTime), JobTools.Time(b.TravelTime));
			grid.Rows.Add("Total (approx.)", JobTools.Time(a.Total), JobTools.Time(b.Total));
			grid.Rows.Add("Size (mm)", string.Format(c, "{0:0.#} × {1:0.#}", a.Width, a.Height), string.Format(c, "{0:0.#} × {1:0.#}", b.Width, b.Height));
			grid.Rows.Add("Origin (mm)", string.Format(c, "X{0:0.#} Y{1:0.#}", a.MinX, a.MinY), string.Format(c, "X{0:0.#} Y{1:0.#}", b.MinX, b.MinY));
			grid.Rows.Add("Highest S", a.MaxPower.ToString("0.##", c), b.MaxPower.ToString("0.##", c));
			var canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.White };
			canvas.Paint += PaintOverlay;
			canvas.Resize += (sender, args) => canvas.Invalidate();
			var legend = new Label { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8),
				Text = "Blue: loaded job. Orange: " + comparedName + ". Times use feed rates only, without acceleration." };
			Controls.Add(canvas);
			Controls.Add(grid);
			Controls.Add(legend);
			JobTools.ThemeForm(this);
		}

		private sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; } }

		private void PaintOverlay(object sender, PaintEventArgs e)
		{
			var panel = (Control)sender;
			var all = current.Concat(other).ToList();
			if (all.Count == 0) return;
			double minX = all.Min(s => Math.Min(s.X1, s.X2)), maxX = all.Max(s => Math.Max(s.X1, s.X2));
			double minY = all.Min(s => Math.Min(s.Y1, s.Y2)), maxY = all.Max(s => Math.Max(s.Y1, s.Y2));
			double scale = Math.Min((panel.Width - 20) / Math.Max(1e-6, maxX - minX), (panel.Height - 20) / Math.Max(1e-6, maxY - minY));
			Func<double, double, PointF> map = (x, y) => new PointF((float)(10 + (x - minX) * scale), (float)(panel.Height - 10 - (y - minY) * scale));
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (var blue = new Pen(Color.FromArgb(160, 30, 90, 200), 1))
			using (var orange = new Pen(Color.FromArgb(160, 230, 120, 20), 1))
			{
				foreach (var s in current) e.Graphics.DrawLine(blue, map(s.X1, s.Y1), map(s.X2, s.Y2));
				foreach (var s in other) e.Graphics.DrawLine(orange, map(s.X1, s.Y1), map(s.X2, s.Y2));
			}
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			using (var dialog = new OpenFileDialog { Filter = "G-code|*.nc;*.cnc;*.tap;*.gcode;*.ngc|All files|*.*", CheckFileExists = true })
			{
				if (dialog.ShowDialog(owner) != DialogResult.OK) return;
				var lines = File.ReadAllLines(dialog.FileName).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => new GrblCommand(l.Trim())).ToArray();
				using (var form = new CompareForm(core.LoadedFile.ToArray(), lines, Path.GetFileName(dialog.FileName))) form.ShowDialog(owner);
			}
		}
	}

	/// <summary>Lays the job's operations out on a sheet, optionally adding copies of one of them.</summary>
	internal sealed class NestForm : SimpleDialog
	{
		private NestForm(GrblCore core) : base("Nest on sheet")
		{
			var operations = core.LoadedFile.Operations.ToList();
			decimal bedW = 300, bedH = 200;
			try { bedW = GrblCore.Configuration.TableWidth; bedH = GrblCore.Configuration.TableHeight; } catch (Exception) { }
			AddNote("Moves each enabled operation so their bounding boxes sit in rows on the sheet without overlapping. " +
				"Shapes are packed by their boxes, so irregular parts keep some space between them.", 6);
			var width = AddNumber("Sheet width (mm)", 1, 5000, Settings.GetObject("Nest.Width", bedW), 1, 10);
			var height = AddNumber("Sheet height (mm)", 1, 5000, Settings.GetObject("Nest.Height", bedH), 1, 10);
			var gap = AddNumber("Gap between parts (mm)", 0, 100, Settings.GetObject("Nest.Gap", 3m), 1, 0.5m);
			var margin = AddNumber("Sheet margin (mm)", 0, 100, Settings.GetObject("Nest.Margin", 5m), 1, 0.5m);
			var rotate = AddField("", new CheckBox { Text = "Allow turning parts 90°", Checked = Settings.GetObject("Nest.Rotate", true), AutoSize = true });
			var source = AddChoice("Add copies of", new object[] { "(none)" }.Concat(operations.Select(o => (object)o.Name)));
			var copies = AddNumber("Extra copies", 0, 500, 0);
			AddButton("Cancel", Close, cancel: true);
			AddButton("Nest", () =>
			{
				Settings.SetObject("Nest.Width", width.Value);
				Settings.SetObject("Nest.Height", height.Value);
				Settings.SetObject("Nest.Gap", gap.Value);
				Settings.SetObject("Nest.Margin", margin.Value);
				Settings.SetObject("Nest.Rotate", rotate.Checked);
				var list = operations.Select(o => o.WithSettings(o.Name, o.Repeats, o.Settings, o.Enabled)).ToList();
				if (source.SelectedIndex > 0)
				{
					var original = operations[source.SelectedIndex - 1];
					for (int i = 0; i < copies.Value; i++)
						list.Add(original.WithSettings(string.Format(CultureInfo.CurrentCulture, "{0} ({1})", original.Name, i + 2), original.Repeats, original.Settings, original.Enabled));
				}
				var enabled = list.Where(o => o.Enabled).ToList();
				if (enabled.Count == 0) throw new InvalidOperationException("Enable at least one operation to nest.");
				var bounds = enabled.Select(Nesting.Bounds).ToList();
				var scales = enabled.Select(o => o.Placement?.Scale ?? 1).ToList();
				var sizes = bounds.Select((b, i) => new SizeF((float)(b.Width * scales[i]), (float)(b.Height * scales[i]))).ToList();
				var placed = Nesting.Pack(sizes, (double)width.Value, (double)height.Value, (double)gap.Value, (double)margin.Value, rotate.Checked);
				foreach (var nest in placed) enabled[nest.Item].Placement = Nesting.PlacementFor(bounds[nest.Item], nest, scales[nest.Item]);
				Enabled = false;
				core.RebuildOperations(list, error =>
				{
					Enabled = true;
					if (error == null) { DialogResult = DialogResult.OK; Close(); }
					else ShowError(error.Message);
				});
			}, accept: true);
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			if (!core.HasJobOperations || !core.CanConfigurePasses)
			{
				MessageBox.Show(owner, "Nesting works on imported operations (images and SVGs). Load one, and stop any running job first.", "Nest on sheet",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			using (var form = new NestForm(core)) form.ShowDialog(owner);
		}
	}

	/// <summary>Adds a serial number, date or QR code to the job as its own operation.</summary>
	internal sealed class MarkingForm : SimpleDialog
	{
		private MarkingForm(GrblCore core) : base("Serial number and QR code")
		{
			var kind = AddChoice("Mark", new object[] { "Text", "QR code" }, Settings.GetObject("Marking.Kind", "Text"));
			var template = AddField("Template", new TextBox { Text = Settings.GetObject("Marking.Template", "SN {n:0000}"), Width = 240 });
			AddNote("Use {n} for the serial counter ({n:0000} pads to four digits), {date}, {date:dd/MM/yy} and {time}.", 6);
			var counter = AddNumber("Next serial", 0, 99999999, SerialTemplate.Counter);
			var x = AddNumber("X (mm, lower left)", -5000, 5000, Settings.GetObject("Marking.X", 0m), 2, 1);
			var y = AddNumber("Y (mm, lower left)", -5000, 5000, Settings.GetObject("Marking.Y", 0m), 2, 1);
			var size = AddNumber("Text height / QR module (mm)", 0.1m, 20, Settings.GetObject("Marking.Size", 5m), 2, 0.1m);
			var lines = AddNumber("QR lines per module", 1, 50, Settings.GetObject("Marking.Lines", 5));
			var ecc = AddChoice("QR error correction", new object[] { "L", "M", "Q", "H" }, Settings.GetObject("Marking.Ecc", "M"));
			var power = AddNumber("Power (S)", 1, 100000, Settings.GetObject("Marking.Power", 800));
			var speed = AddNumber("Speed (mm/min)", 1, 100000, Settings.GetObject("Marking.Speed", 1500));
			var mode = AddChoice("Laser mode", LaserModes, Settings.GetObject("Marking.Mode", "M4"));
			var preview = AddNote("");
			Func<string> render = () => SerialTemplate.Render(template.Text, (int)counter.Value, DateTime.Now);
			EventHandler update = (sender, args) =>
			{
				try { preview.Text = "Will mark: " + render(); }
				catch (FormatException ex) { preview.Text = ex.Message; }
			};
			template.TextChanged += update;
			counter.ValueChanged += update;
			update(null, EventArgs.Empty);
			AddButton("Close", Close, cancel: true);
			AddButton("Add to job", () =>
			{
				if (!core.CanLoadNewFile) throw new InvalidOperationException("Stop the job before adding a marking.");
				string text = render();
				bool qr = (string)kind.SelectedItem == "QR code";
				GrblCommand[] commands = qr
					? MarkingGenerator.Qr(QrCode.Encode(text, (QrErrorCorrection)ecc.SelectedIndex), (double)x.Value, (double)y.Value, (double)size.Value, (int)lines.Value, (int)speed.Value, (int)power.Value, (string)mode.SelectedItem)
					: MarkingGenerator.Text(text, (double)x.Value, (double)y.Value, (double)size.Value, (int)speed.Value, (int)power.Value, (string)mode.SelectedItem);
				core.LoadedFile.AppendGeneratedOperation(new JobOperation((qr ? "QR " : "Text ") + text, commands));
				Settings.SetObject("Marking.Kind", (string)kind.SelectedItem);
				Settings.SetObject("Marking.Template", template.Text);
				Settings.SetObject("Marking.X", x.Value);
				Settings.SetObject("Marking.Y", y.Value);
				Settings.SetObject("Marking.Size", size.Value);
				Settings.SetObject("Marking.Lines", (int)lines.Value);
				Settings.SetObject("Marking.Ecc", (string)ecc.SelectedItem);
				Settings.SetObject("Marking.Power", (int)power.Value);
				Settings.SetObject("Marking.Speed", (int)speed.Value);
				Settings.SetObject("Marking.Mode", (string)mode.SelectedItem);
				if (template.Text.Contains("{n")) counter.Value = Math.Min(counter.Maximum, counter.Value + 1);
				SerialTemplate.Counter = (int)counter.Value;
				ShowInfo("Added \"" + text + "\" as a new operation.");
			}, accept: true);
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			using (var form = new MarkingForm(core)) form.ShowDialog(owner);
		}
	}

	/// <summary>Stretches the loaded job's Y axis for a rotary attachment wired in place of Y.</summary>
	internal sealed class RotaryForm : SimpleDialog
	{
		private RotaryForm(GrblCore core) : base("Rotary")
		{
			AddNote("For a rotary attachment plugged into the Y motor. Measure the Y travel that turns the roller or chuck exactly once " +
				"(jog Y and count a full turn), then enter it below. The job's Y moves are scaled so the design wraps around the object at its true size.", 6);
			var kind = AddChoice("Type", new object[] { "Roller", "Chuck" }, Settings.GetObject("Rotary.Kind", "Roller"));
			var perRev = AddNumber("Y mm per full turn", 0.1m, 10000, Settings.GetObject("Rotary.PerRev", 100m), 2, 1);
			var roller = AddNumber("Roller diameter (mm)", 0.1m, 1000, Settings.GetObject("Rotary.Roller", 30m), 2, 0.5m);
			var objectDiameter = AddNumber("Object diameter (mm)", 0.1m, 1000, Settings.GetObject("Rotary.Object", 70m), 2, 0.5m);
			var result = AddNote("");
			Func<double> scale = () => Rotary.Scale((string)kind.SelectedItem == "Chuck" ? RotaryKind.Chuck : RotaryKind.Roller,
				(double)perRev.Value, (double)roller.Value, (double)objectDiameter.Value);
			EventHandler update = (sender, args) =>
			{
				bool chuck = (string)kind.SelectedItem == "Chuck";
				roller.Enabled = !chuck;
				objectDiameter.Enabled = chuck;
				result.Text = string.Format(CultureInfo.CurrentCulture, "Y is multiplied by {0:0.####}. {1}", scale(),
					chuck ? "A chuck holds the object, so its diameter sets the scale." : "A roller turns the object by friction, so only the roller's size matters.");
			};
			kind.SelectedIndexChanged += update;
			perRev.ValueChanged += update;
			roller.ValueChanged += update;
			objectDiameter.ValueChanged += update;
			update(null, EventArgs.Empty);
			AddNote("This replaces the loaded job with the scaled one, named \"(rotary)\". Arcs become short lines. Reload the file to undo.");
			AddButton("Cancel", Close, cancel: true);
			AddButton("Apply to job", () =>
			{
				if (!core.CanLoadNewFile) throw new InvalidOperationException("Stop the job before scaling it.");
				Settings.SetObject("Rotary.Kind", (string)kind.SelectedItem);
				Settings.SetObject("Rotary.PerRev", perRev.Value);
				Settings.SetObject("Rotary.Roller", roller.Value);
				Settings.SetObject("Rotary.Object", objectDiameter.Value);
				var scaled = Rotary.Apply(core.LoadedFile, scale());
				string name = Path.GetFileNameWithoutExtension(Settings.GetObject<string>("Core.LastOpenFile", null) ?? "Job") + " (rotary)";
				core.LoadedFile.LoadCalibrationPattern(name, scaled);
				DialogResult = DialogResult.OK;
			}, accept: true);
		}

		internal static void ShowFor(IWin32Window owner, GrblCore core)
		{
			using (var form = new RotaryForm(core)) form.ShowDialog(owner);
		}
	}
}
