using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Tools;

namespace LaserGRBL.UserControls
{
	/// <summary>Colours for the modern window chrome, taken from the active colour scheme so every scheme still works.</summary>
	internal static class ShellPalette
	{
		internal static readonly Color Accent = Color.FromArgb(37, 99, 235);
		internal static readonly Color AccentText = Color.White;
		internal static readonly Color Good = Color.FromArgb(74, 222, 128);
		internal static readonly Color Warn = Color.FromArgb(245, 185, 113);
		internal static readonly Color Bad = Color.FromArgb(240, 80, 80);

		internal static Color Surface { get { return ColorScheme.FormBackColor; } }
		internal static Color Text { get { return ColorScheme.FormForeColor; } }
		internal static Color Border { get { return ColorScheme.ControlsBorder; } }
		internal static Color Muted { get { return Mix(ColorScheme.FormForeColor, ColorScheme.FormBackColor, 0.62); } }
		internal static Color Raised { get { return ColorScheme.ChangeColorBrightness(ColorScheme.FormBackColor, ColorScheme.DarkScheme ? 0.08f : -0.05f); } }
		internal static Color Selected { get { return Mix(Accent, ColorScheme.FormBackColor, 0.28); } }

		internal static Color Mix(Color a, Color b, double amountOfA)
		{
			Func<int, int, int> m = (x, y) => (int)Math.Round(x * amountOfA + y * (1 - amountOfA));
			return Color.FromArgb(m(a.R, b.R), m(a.G, b.G), m(a.B, b.B));
		}
	}

	internal enum ShellButtonStyle { Ghost, Outline, Accent, Segment, Danger }

	/// <summary>Flat rounded button for the window chrome. Paints itself, so it looks the same in every scheme.</summary>
	internal class ShellButton : Control
	{
		private bool hover, pressed;
		private ShellButtonStyle style = ShellButtonStyle.Outline;
		private bool active;

		internal ShellButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
			SetStyle(ControlStyles.Selectable, true);
			Cursor = Cursors.Hand;
			Height = 28;
			Font = new Font("Segoe UI", 9f);
			Margin = new Padding(3);
			TabStop = true;
		}

		internal ShellButtonStyle ButtonStyle { get { return style; } set { style = value; Invalidate(); } }

		/// <summary>For segments and toggles: drawn as selected.</summary>
		internal bool Active { get { return active; } set { if (active != value) { active = value; Invalidate(); } } }

		/// <summary>Optional small text drawn after the caption, such as a dropdown arrow.</summary>
		internal string Suffix { get; set; }

		/// <summary>Optional dot drawn before the caption, for status.</summary>
		internal Color? Dot { get; set; }

		internal int Radius { get; set; } = 6;

		internal void FitWidth(int padding = 14)
		{
			using (var g = CreateGraphics())
			{
				var size = TextRenderer.MeasureText(g, Text + (Suffix ?? ""), Font);
				Width = size.Width + padding * 2 + (Dot.HasValue ? 14 : 0);
			}
		}

		protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
		protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
		protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
		protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
		protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
		protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

		protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData); }

		protected override void OnKeyUp(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
			base.OnKeyUp(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			Color parent = Parent?.BackColor ?? ShellPalette.Surface;
			g.Clear(parent);

			Color back, fore, border;
			switch (style)
			{
				case ShellButtonStyle.Accent:
					back = ShellPalette.Accent; fore = ShellPalette.AccentText; border = back; break;
				case ShellButtonStyle.Danger:
					back = Color.FromArgb(180, 35, 24); fore = Color.White; border = back; break;
				case ShellButtonStyle.Segment:
					back = active ? ShellPalette.Accent : parent; fore = active ? ShellPalette.AccentText : ShellPalette.Text; border = back; break;
				case ShellButtonStyle.Ghost:
					back = active ? ShellPalette.Selected : parent; fore = ShellPalette.Text; border = back; break;
				default:
					back = ShellPalette.Raised; fore = ShellPalette.Text; border = ShellPalette.Border; break;
			}
			if (!Enabled) { fore = ShellPalette.Muted; if (style == ShellButtonStyle.Accent || style == ShellButtonStyle.Danger) back = border = ShellPalette.Mix(back, parent, 0.45); }
			else if (pressed) back = ColorScheme.ChangeColorBrightness(back, -0.12f);
			else if (hover) back = style == ShellButtonStyle.Ghost || style == ShellButtonStyle.Segment && !active
				? ShellPalette.Raised : ColorScheme.ChangeColorBrightness(back, ColorScheme.DarkScheme ? 0.1f : -0.06f);

			var rect = new Rectangle(0, 0, Width - 1, Height - 1);
			using (var path = Graph.RoundedRect(rect, Radius))
			using (var fill = new SolidBrush(back))
			using (var pen = new Pen(border))
			{
				g.FillPath(fill, path);
				g.DrawPath(pen, path);
				if (Focused && ShowFocusCues)
					using (var focus = new Pen(ShellPalette.Accent, 2)) g.DrawPath(focus, path);
			}

			string text = Text + (string.IsNullOrEmpty(Suffix) ? "" : "  " + Suffix);
			var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
			var textRect = ClientRectangle;
			if (Dot.HasValue)
			{
				var size = TextRenderer.MeasureText(g, text, Font);
				int x = (Width - size.Width - 14) / 2;
				using (var dot = new SolidBrush(Dot.Value)) g.FillEllipse(dot, x, Height / 2 - 4, 8, 8);
				textRect = new Rectangle(x + 14, 0, size.Width + 2, Height);
				flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
			}
			TextRenderer.DrawText(g, text, Font, textRect, fore, flags);
		}
	}

	/// <summary>A panel with a hairline on one or more edges, for the chrome's dividers.</summary>
	internal class ShellPanel : Panel
	{
		internal AnchorStyles Lines { get; set; }

		internal ShellPanel()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			using (var pen = new Pen(ShellPalette.Border))
			{
				if ((Lines & AnchorStyles.Top) != 0) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
				if ((Lines & AnchorStyles.Bottom) != 0) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
				if ((Lines & AnchorStyles.Left) != 0) e.Graphics.DrawLine(pen, 0, 0, 0, Height);
				if ((Lines & AnchorStyles.Right) != 0) e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
			}
		}
	}

	/// <summary>Plain label in the chrome's text colours.</summary>
	internal class ShellLabel : Label
	{
		internal bool IsMuted { get; set; }
		internal bool IsHeading { get; set; }

		internal ShellLabel(string text = "", bool heading = false, bool muted = false)
		{
			Text = text;
			IsHeading = heading;
			IsMuted = muted;
			AutoSize = true;
			Font = new Font("Segoe UI", 9f, heading ? FontStyle.Bold : FontStyle.Regular);
			Margin = new Padding(0, 3, 0, 3);
			BackColor = Color.Transparent;
		}

		internal void ApplyColors()
		{
			ForeColor = IsMuted ? ShellPalette.Muted : ShellPalette.Text;
		}
	}
}
