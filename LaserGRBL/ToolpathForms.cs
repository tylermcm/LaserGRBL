using System;
using System.Linq;
using System.Windows.Forms;

namespace LaserGRBL
{
	/// <summary>Edits one operation's <see cref="ToolpathOptions"/>. Contour settings are disabled for operations that aren't SVG outlines.</summary>
	internal sealed class ToolpathOptionsForm : SimpleDialog
	{
		private sealed class Choice<T>
		{
			internal readonly T Value;
			private readonly string text;
			internal Choice(T value, string text) { Value = value; this.text = text; }
			public override string ToString() { return text; }
		}

		internal ToolpathOptions Result { get; private set; }

		private ToolpathOptionsForm(string operation, ToolpathOptions current, bool contours) : base("Toolpath · " + operation)
		{
			var options = current?.Clone() ?? new ToolpathOptions();
			var orders = new[] { new Choice<CutOrder>(CutOrder.AsDrawn, "As drawn"), new Choice<CutOrder>(CutOrder.InsideFirst, "Inside first, then nearest") };
			var fills = new[]
			{
				new Choice<FillPattern>(FillPattern.None, "None"), new Choice<FillPattern>(FillPattern.Hatch, "Hatch"),
				new Choice<FillPattern>(FillPattern.CrossHatch, "Cross-hatch"), new Choice<FillPattern>(FillPattern.Offset, "Offset (concentric)")
			};

			if (!contours) AddNote("This operation isn't an SVG import, so only the Z step applies.", 6);
			var order = AddChoice("Cut order", orders, orders.First(o => o.Value == options.Order));
			AddNote("Inside first cuts holes and details before the outline around them, so parts don't shift or drop before they're finished.", 6);
			var leadIn = AddNumber("Lead-in (mm)", 0, 20, options.LeadInMm, 2, 0.5m);
			var overcut = AddNumber("Overcut (mm)", 0, 20, options.OvercutMm, 2, 0.5m);
			AddNote("Lead-ins start each closed cut on the waste side so the pierce mark isn't on the part. Overcut runs past the start to close the seam.", 6);
			var tabs = AddNumber("Tabs per outline", 0, 50, options.TabCount);
			var tabWidth = AddNumber("Tab width (mm)", 0.1m, 20, options.TabWidthMm, 2, 0.25m);
			AddNote("Tabs leave short uncut bridges on outer outlines only. Holes are cut fully.", 6);
			var fill = AddChoice("Fill", fills, fills.First(f => f.Value == options.Fill));
			var spacing = AddNumber("Fill spacing (mm)", 0.02m, 20, options.FillSpacingMm, 3, 0.01m);
			var angle = AddNumber("Fill angle (°)", -360, 360, options.FillAngle, 1, 15);
			var fillOnly = AddField("", new CheckBox { Text = "Fill only (don't trace the outlines)", Checked = options.FillOnly, AutoSize = true });
			AddNote("Fills closed shapes with lines at the outline's power and speed, before any outline is traced.", 6);
			var zStep = AddNumber("Z step per repeat (mm)", 0, 50, options.ZStepMm, 2, 0.1m);
			AddNote("Lowers the head by this much before each repeat after the first, then returns Z when the operation ends. Needs a Z axis.");

			foreach (Control control in new Control[] { order, leadIn, overcut, tabs, tabWidth, fill, spacing, angle, fillOnly })
				control.Enabled = contours;

			AddButton("Cancel", Close, cancel: true);
			AddButton("OK", () =>
			{
				var edited = new ToolpathOptions
				{
					Order = ((Choice<CutOrder>)order.SelectedItem).Value,
					LeadInMm = leadIn.Value, OvercutMm = overcut.Value,
					TabCount = (int)tabs.Value, TabWidthMm = tabWidth.Value,
					Fill = ((Choice<FillPattern>)fill.SelectedItem).Value,
					FillSpacingMm = spacing.Value, FillAngle = angle.Value, FillOnly = fillOnly.Checked,
					ZStepMm = zStep.Value
				};
				edited.Validate();
				Result = edited.IsEmpty ? null : edited;
				DialogResult = DialogResult.OK;
			}, accept: true);
		}

		/// <summary>Returns false when cancelled; otherwise <paramref name="result"/> is the new options, or null for none.</summary>
		internal static bool Edit(IWin32Window owner, string operation, ToolpathOptions current, bool contours, out ToolpathOptions result)
		{
			using (var form = new ToolpathOptionsForm(operation, current, contours))
			{
				bool ok = form.ShowDialog(owner) == DialogResult.OK;
				result = ok ? form.Result : current;
				return ok;
			}
		}
	}

	/// <summary>Raster overscan: run each scan line past the image with the laser off so speed is steady while burning.</summary>
	internal sealed class RasterOverscanForm : SimpleDialog
	{
		internal const string SettingsKey = "Raster.OverscanMm";

		internal static decimal OverscanMm
		{
			get { return Math.Max(0, Math.Min(50, Settings.GetObject(SettingsKey, 0m))); }
			set { Settings.SetObject(SettingsKey, value, true); }
		}

		private RasterOverscanForm() : base("Raster overscan")
		{
			AddNote("Each horizontal or vertical scan line starts and ends this far outside the image with the laser off, " +
				"so the head is at full speed across the whole image. Edges stop coming out darker.", 6);
			var value = AddNumber("Overscan (mm)", 0, 50, OverscanMm, 1, 0.5m);
			AddNote("The head travels past the image on both sides, so leave room: an image placed at X0 will move to negative X. " +
				"Applies to new raster imports. Diagonal scans don't use it.");
			AddButton("Cancel", Close, cancel: true);
			AddButton("OK", () => { OverscanMm = value.Value; DialogResult = DialogResult.OK; }, accept: true);
		}

		internal static void ShowFor(IWin32Window owner)
		{
			using (var form = new RasterOverscanForm()) form.ShowDialog(owner);
		}
	}
}
