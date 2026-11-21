using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Tools;

namespace LaserGRBL.UserControls
{
	/// <summary>The modern layout's palette: a fixed dark theme, separate from the grid's colour schemes.</summary>
	internal static class ShellPalette
	{
		internal static readonly Color Window = Color.FromArgb(15, 17, 21);
		internal static readonly Color Surface = Color.FromArgb(19, 22, 27);
		internal static readonly Color Card = Color.FromArgb(26, 30, 36);
		internal static readonly Color Input = Color.FromArgb(15, 17, 21);
		internal static readonly Color Border = Color.FromArgb(38, 42, 49);
		internal static readonly Color BorderStrong = Color.FromArgb(44, 49, 58);
		internal static readonly Color Text = Color.FromArgb(232, 234, 237);
		internal static readonly Color TextSoft = Color.FromArgb(196, 200, 206);
		internal static readonly Color Muted = Color.FromArgb(155, 161, 169);
		internal static readonly Color Faint = Color.FromArgb(125, 132, 141);
		internal static readonly Color Accent = Color.FromArgb(37, 99, 235);
		internal static readonly Color AccentText = Color.White;
		internal static readonly Color AccentSoft = Color.FromArgb(31, 42, 61);
		internal static readonly Color SelectedCard = Color.FromArgb(23, 34, 58);
		internal static readonly Color Link = Color.FromArgb(91, 155, 255);
		internal static readonly Color Good = Color.FromArgb(74, 222, 128);
		internal static readonly Color Warn = Color.FromArgb(245, 185, 113);
		internal static readonly Color Bad = Color.FromArgb(240, 90, 90);
		internal static readonly Color Danger = Color.FromArgb(180, 35, 24);

		internal static readonly Font UiFont = new Font("Segoe UI", 9f);
		internal static readonly Font UiBold = new Font("Segoe UI Semibold", 9f);
		internal static readonly Font Heading = new Font("Segoe UI Semibold", 9.5f);
		internal static readonly Font Small = new Font("Segoe UI", 8.25f);
		internal static readonly Font Big = new Font("Segoe UI Semibold", 22f);

		internal static Color Mix(Color a, Color b, double amountOfA)
		{
			Func<int, int, int> m = (x, y) => (int)Math.Round(x * amountOfA + y * (1 - amountOfA));
			return Color.FromArgb(m(a.R, b.R), m(a.G, b.G), m(a.B, b.B));
		}
	}

	internal enum ShellIcon { None, Menu, Gear, Layers, Image, Dashed, Eye, EyeOff, Plus, Dots, Console, Frame, Play, Save, Chevron, Move, Pencil, Logo, Folder, Grip }

	/// <summary>Stroke icons drawn in code, so no image files are needed and they follow the text colour.</summary>
	internal static class ShellIcons
	{
		internal static void Draw(Graphics g, ShellIcon icon, Rectangle r, Color color, float width = 1.5f)
		{
			if (icon == ShellIcon.None) return;
			var old = g.SmoothingMode;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
			using (var brush = new SolidBrush(color))
			{
				float x = r.X, y = r.Y, w = r.Width, h = r.Height, cx = x + w / 2, cy = y + h / 2;
				switch (icon)
				{
					case ShellIcon.Menu:
						for (int i = -1; i <= 1; i++) g.DrawLine(pen, x + w * .2f, cy + i * h * .25f, x + w * .8f, cy + i * h * .25f);
						break;
					case ShellIcon.Gear:
						g.DrawEllipse(pen, cx - w * .17f, cy - h * .17f, w * .34f, h * .34f);
						for (int i = 0; i < 8; i++)
						{
							double a = i * Math.PI / 4;
							g.DrawLine(pen, cx + (float)Math.Cos(a) * w * .28f, cy + (float)Math.Sin(a) * h * .28f, cx + (float)Math.Cos(a) * w * .42f, cy + (float)Math.Sin(a) * h * .42f);
						}
						break;
					case ShellIcon.Layers:
						g.DrawPolygon(pen, new[] { new PointF(cx, y + h * .18f), new PointF(x + w * .88f, cy - h * .06f), new PointF(cx, cy + h * .2f), new PointF(x + w * .12f, cy - h * .06f) });
						g.DrawLines(pen, new[] { new PointF(x + w * .12f, cy + h * .14f), new PointF(cx, cy + h * .4f), new PointF(x + w * .88f, cy + h * .14f) });
						break;
					case ShellIcon.Image:
						g.DrawRectangle(pen, x + w * .12f, y + h * .2f, w * .76f, h * .6f);
						g.DrawLines(pen, new[] { new PointF(x + w * .12f, y + h * .72f), new PointF(x + w * .36f, y + h * .48f), new PointF(x + w * .56f, y + h * .68f), new PointF(x + w * .68f, y + h * .56f), new PointF(x + w * .88f, y + h * .76f) });
						break;
					case ShellIcon.Dashed:
						pen.DashStyle = DashStyle.Dash;
						g.DrawRectangle(pen, x + w * .14f, y + h * .2f, w * .72f, h * .6f);
						break;
					case ShellIcon.Eye:
					case ShellIcon.EyeOff:
						g.DrawEllipse(pen, x + w * .1f, cy - h * .2f, w * .8f, h * .4f);
						if (icon == ShellIcon.Eye) g.FillEllipse(brush, cx - w * .1f, cy - h * .1f, w * .2f, h * .2f);
						else g.DrawLine(pen, x + w * .15f, y + h * .85f, x + w * .85f, y + h * .15f);
						break;
					case ShellIcon.Plus:
						g.DrawLine(pen, cx, y + h * .2f, cx, y + h * .8f);
						g.DrawLine(pen, x + w * .2f, cy, x + w * .8f, cy);
						break;
					case ShellIcon.Dots:
						for (int i = -1; i <= 1; i++) g.FillEllipse(brush, cx + i * w * .22f - 1.5f, cy - 1.5f, 3, 3);
						break;
					case ShellIcon.Grip:
						for (int i = -1; i <= 1; i++) { g.FillEllipse(brush, cx - 3, cy + i * 4 - 1, 2.2f, 2.2f); g.FillEllipse(brush, cx + 1, cy + i * 4 - 1, 2.2f, 2.2f); }
						break;
					case ShellIcon.Console:
						g.DrawLines(pen, new[] { new PointF(x + w * .2f, y + h * .32f), new PointF(x + w * .42f, cy), new PointF(x + w * .2f, y + h * .68f) });
						g.DrawLine(pen, x + w * .5f, y + h * .7f, x + w * .8f, y + h * .7f);
						break;
					case ShellIcon.Frame:
						foreach (var c in new[] { new[] { 0f, 0f, 1f, 1f }, new[] { 1f, 0f, -1f, 1f }, new[] { 0f, 1f, 1f, -1f }, new[] { 1f, 1f, -1f, -1f } })
						{
							float px = x + w * (.18f + c[0] * .64f), py = y + h * (.18f + c[1] * .64f);
							g.DrawLines(pen, new[] { new PointF(px, py + c[3] * h * .22f), new PointF(px, py), new PointF(px + c[2] * w * .22f, py) });
						}
						break;
					case ShellIcon.Play:
						g.FillPolygon(brush, new[] { new PointF(x + w * .3f, y + h * .2f), new PointF(x + w * .8f, cy), new PointF(x + w * .3f, y + h * .8f) });
						break;
					case ShellIcon.Save:
						g.DrawLine(pen, cx, y + h * .15f, cx, y + h * .62f);
						g.DrawLines(pen, new[] { new PointF(x + w * .3f, y + h * .44f), new PointF(cx, y + h * .64f), new PointF(x + w * .7f, y + h * .44f) });
						g.DrawLine(pen, x + w * .18f, y + h * .82f, x + w * .82f, y + h * .82f);
						break;
					case ShellIcon.Chevron:
						g.DrawLines(pen, new[] { new PointF(x + w * .3f, y + h * .4f), new PointF(cx, y + h * .62f), new PointF(x + w * .7f, y + h * .4f) });
						break;
					case ShellIcon.Move:
						g.DrawLine(pen, cx, y + h * .12f, cx, y + h * .88f);
						g.DrawLine(pen, x + w * .12f, cy, x + w * .88f, cy);
						foreach (var d in new[] { new[] { 0, -1 }, new[] { 0, 1 }, new[] { -1, 0 }, new[] { 1, 0 } })
						{
							float tx = cx + d[0] * w * .38f, ty = cy + d[1] * h * .38f;
							g.DrawLines(pen, new[] { new PointF(tx - d[1] * w * .12f - d[0] * w * .12f, ty - d[0] * h * .12f - d[1] * h * .12f), new PointF(tx, ty), new PointF(tx + d[1] * w * .12f - d[0] * w * .12f, ty + d[0] * h * .12f - d[1] * h * .12f) });
						}
						break;
					case ShellIcon.Pencil:
						g.DrawPolygon(pen, new[] { new PointF(x + w * .2f, y + h * .8f), new PointF(x + w * .24f, y + h * .62f), new PointF(x + w * .66f, y + h * .2f), new PointF(x + w * .8f, y + h * .34f), new PointF(x + w * .38f, y + h * .76f) });
						break;
					case ShellIcon.Folder:
						g.DrawPolygon(pen, new[] { new PointF(x + w * .12f, y + h * .26f), new PointF(x + w * .42f, y + h * .26f), new PointF(x + w * .5f, y + h * .36f), new PointF(x + w * .88f, y + h * .36f), new PointF(x + w * .88f, y + h * .78f), new PointF(x + w * .12f, y + h * .78f) });
						break;
					case ShellIcon.Logo:
						g.DrawLine(pen, cx, y, cx, y + h);
						g.DrawLine(pen, x, cy, x + w, cy);
						g.DrawLine(pen, x + w * .18f, y + h * .18f, x + w * .82f, y + h * .82f);
						g.DrawLine(pen, x + w * .82f, y + h * .18f, x + w * .18f, y + h * .82f);
						break;
				}
			}
			g.SmoothingMode = old;
		}
	}

	internal enum ShellButtonStyle { Ghost, Outline, Accent, Segment, Danger, Link }

	/// <summary>Flat rounded button in the modern palette, with an optional icon and status dot.</summary>
	internal class ShellButton : Control
	{
		private bool hover, pressed, active;
		private ShellButtonStyle style = ShellButtonStyle.Outline;

		internal ShellButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
			Cursor = Cursors.Hand;
			Height = 28;
			Font = ShellPalette.UiFont;
			Margin = new Padding(3);
			TabStop = true;
		}

		internal ShellButtonStyle ButtonStyle { get { return style; } set { style = value; Invalidate(); } }
		internal bool Active { get { return active; } set { if (active != value) { active = value; Invalidate(); } } }
		internal ShellIcon Icon { get; set; }
		internal ShellIcon TrailingIcon { get; set; }
		internal Color? Dot { get; set; }
		internal int Radius { get; set; } = 6;
		internal bool AlignLeft { get; set; }

		internal void FitWidth(int padding = 12)
		{
			int text = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
			int extra = (Icon != ShellIcon.None ? 20 : 0) + (TrailingIcon != ShellIcon.None ? 16 : 0) + (Dot.HasValue ? 14 : 0);
			Width = text + padding * 2 + extra - (text == 0 && extra > 0 ? padding * 2 - 12 : 0);
		}

		protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
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
				case ShellButtonStyle.Accent: back = ShellPalette.Accent; fore = ShellPalette.AccentText; border = back; break;
				case ShellButtonStyle.Danger: back = ShellPalette.Danger; fore = Color.White; border = back; break;
				case ShellButtonStyle.Segment: back = active ? ShellPalette.Accent : parent; fore = active ? ShellPalette.AccentText : ShellPalette.TextSoft; border = back; break;
				case ShellButtonStyle.Ghost: back = active ? ShellPalette.AccentSoft : parent; fore = ShellPalette.TextSoft; border = back; break;
				case ShellButtonStyle.Link: back = parent; fore = ShellPalette.Link; border = back; break;
				default: back = ShellPalette.Card; fore = ShellPalette.Text; border = ShellPalette.BorderStrong; break;
			}
			if (!Enabled)
			{
				fore = ShellPalette.Faint;
				if (style == ShellButtonStyle.Accent || style == ShellButtonStyle.Danger) back = border = ShellPalette.Mix(back, parent, 0.35);
			}
			else if (pressed) back = ShellPalette.Mix(back, Color.Black, 0.85);
			else if (hover)
				back = style == ShellButtonStyle.Ghost || style == ShellButtonStyle.Link || (style == ShellButtonStyle.Segment && !active) ? ShellPalette.Card : ShellPalette.Mix(back, Color.White, 0.9);

			var rect = new Rectangle(0, 0, Width - 1, Height - 1);
			using (var path = Graph.RoundedRect(rect, Radius))
			using (var fill = new SolidBrush(back))
			using (var pen = new Pen(border))
			{
				g.FillPath(fill, path);
				g.DrawPath(pen, path);
				if (Focused && ShowFocusCues) using (var focus = new Pen(ShellPalette.Link, 1.5f)) g.DrawPath(focus, path);
			}

			int textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, Font).Width;
			int total = textWidth + (Icon != ShellIcon.None ? (textWidth > 0 ? 20 : 16) : 0) + (Dot.HasValue ? 14 : 0) + (TrailingIcon != ShellIcon.None ? 16 : 0);
			int xPos = AlignLeft ? 10 : (Width - total) / 2;
			if (Dot.HasValue)
			{
				using (var dot = new SolidBrush(Dot.Value)) g.FillEllipse(dot, xPos, Height / 2 - 4, 8, 8);
				xPos += 14;
			}
			if (Icon != ShellIcon.None)
			{
				ShellIcons.Draw(g, Icon, new Rectangle(xPos, Height / 2 - 8, 16, 16), fore);
				xPos += textWidth > 0 ? 20 : 16;
			}
			if (textWidth > 0)
			{
				TextRenderer.DrawText(g, Text, Font, new Rectangle(xPos, 0, Math.Max(0, Width - xPos - 4), Height), fore,
					TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
				xPos += textWidth;
			}
			if (TrailingIcon != ShellIcon.None) ShellIcons.Draw(g, TrailingIcon, new Rectangle(xPos + 2, Height / 2 - 7, 14, 14), ShellPalette.Muted);
		}
	}

	/// <summary>A panel with hairlines on chosen edges.</summary>
	internal class ShellPanel : Panel
	{
		internal AnchorStyles Lines { get; set; }

		internal ShellPanel()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
			BackColor = ShellPalette.Surface;
			ForeColor = ShellPalette.Text;
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

	/// <summary>Lays its visible children out top to bottom at full width, each at its own height plus margins.</summary>
	internal class StackPanel : Panel
	{
		internal StackPanel()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			BackColor = ShellPalette.Surface;
		}

		protected override void OnLayout(LayoutEventArgs levent)
		{
			int y = Padding.Top - (AutoScroll ? -AutoScrollPosition.Y : 0);
			int width = ClientSize.Width - Padding.Horizontal;
			foreach (Control child in Controls)
			{
				if (!child.Visible) continue;
				y += child.Margin.Top;
				int w = width - child.Margin.Horizontal;
				int h = child is Label label && label.AutoSize ? label.GetPreferredSize(new Size(w, 0)).Height : child.Height;
				child.SetBounds(Padding.Left + child.Margin.Left, y, w, h);
				y += h + child.Margin.Bottom;
			}
			if (AutoScroll) AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y + Padding.Bottom);
		}

		/// <summary>Total height the children need.</summary>
		internal int ContentHeight
		{
			get
			{
				int y = Padding.Vertical;
				foreach (Control child in Controls) if (child.Visible) y += child.Height + child.Margin.Vertical;
				return y;
			}
		}
	}

	/// <summary>Wrapping text in the modern palette.</summary>
	internal class ShellLabel : Label
	{
		internal ShellLabel(string text = "", Font font = null, Color? color = null)
		{
			Text = text;
			AutoSize = true;
			Font = font ?? ShellPalette.UiFont;
			ForeColor = color ?? ShellPalette.Text;
			BackColor = Color.Transparent;
			Margin = new Padding(0, 2, 0, 2);
			UseMnemonic = false;
		}

		public override Size GetPreferredSize(Size proposedSize)
		{
			if (proposedSize.Width > 0) return TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(proposedSize.Width, int.MaxValue), TextFormatFlags.WordBreak);
			return base.GetPreferredSize(proposedSize);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var area = new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical);
			TextRenderer.DrawText(e.Graphics, Text, Font, area, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
		}
	}

	/// <summary>A section title with an optional small action on the right.</summary>
	internal class SectionHeader : Control
	{
		private readonly ShellButton action;

		internal SectionHeader(string text, ShellIcon icon = ShellIcon.None, EventHandler onAction = null, string actionTip = null)
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			Text = text;
			Height = 26;
			Margin = new Padding(0, 10, 0, 2);
			BackColor = ShellPalette.Surface;
			if (icon != ShellIcon.None)
			{
				action = new ShellButton { Icon = icon, ButtonStyle = ShellButtonStyle.Ghost, Width = 24, Height = 24, Margin = Padding.Empty };
				action.AccessibleName = actionTip ?? text;
				if (onAction != null) action.Click += onAction;
				Controls.Add(action);
			}
		}

		internal ShellButton Action { get { return action; } }

		protected override void OnLayout(LayoutEventArgs levent)
		{
			if (action != null) action.Location = new Point(Width - action.Width, (Height - action.Height) / 2);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.Clear(BackColor);
			TextRenderer.DrawText(e.Graphics, Text, ShellPalette.Heading, new Rectangle(0, 0, Width - 28, Height), ShellPalette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
		}
	}

	/// <summary>One row in a list: icon, name, a muted detail and an optional toggle on the right.</summary>
	internal class ListRow : Control
	{
		private bool hover;
		internal ShellIcon Icon { get; set; }
		internal Color? Swatch { get; set; }
		internal string Detail { get; set; }
		internal bool Selected { get; set; }
		internal bool Dimmed { get; set; }
		internal bool? Toggle { get; set; }
		internal event EventHandler ToggleClicked;

		internal ListRow(string text)
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
			Text = text;
			Height = 30;
			Margin = new Padding(0, 1, 0, 1);
			Cursor = Cursors.Hand;
			TabStop = true;
		}

		protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

		protected override void OnMouseClick(MouseEventArgs e)
		{
			if (Toggle.HasValue && e.X > Width - 28) { ToggleClicked?.Invoke(this, EventArgs.Empty); return; }
			base.OnMouseClick(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? ShellPalette.Surface);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			if (Selected || hover)
				using (var path = Graph.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 6))
				using (var b = new SolidBrush(Selected ? ShellPalette.AccentSoft : ShellPalette.Card)) g.FillPath(b, path);
			int x = 8;
			Color text = Dimmed ? ShellPalette.Faint : Selected ? ShellPalette.Text : ShellPalette.TextSoft;
			if (Swatch.HasValue)
			{
				using (var b = new SolidBrush(Swatch.Value)) g.FillRectangle(b, x, Height / 2 - 9, 14, 18);
				x += 22;
			}
			else if (Icon != ShellIcon.None)
			{
				ShellIcons.Draw(g, Icon, new Rectangle(x, Height / 2 - 8, 16, 16), text);
				x += 24;
			}
			int right = Width - (Toggle.HasValue ? 30 : 8);
			int detailWidth = string.IsNullOrEmpty(Detail) ? 0 : TextRenderer.MeasureText(Detail, ShellPalette.Small).Width;
			TextRenderer.DrawText(g, Text, Selected ? ShellPalette.UiBold : ShellPalette.UiFont, new Rectangle(x, 0, right - x - detailWidth - 4, Height), text,
				TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
			if (detailWidth > 0)
				TextRenderer.DrawText(g, Detail, ShellPalette.Small, new Rectangle(right - detailWidth, 0, detailWidth, Height), ShellPalette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
			if (Toggle.HasValue)
				ShellIcons.Draw(g, Toggle.Value ? ShellIcon.Eye : ShellIcon.EyeOff, new Rectangle(Width - 24, Height / 2 - 8, 16, 16), Toggle.Value ? ShellPalette.Link : ShellPalette.Faint);
		}
	}

	/// <summary>A compact text box with a short prefix label inside the border, like "X 9.00".</summary>
	internal class FieldBox : Control
	{
		internal readonly TextBox Box;
		private readonly string prefix;
		internal event EventHandler Committed;

		internal FieldBox(string prefix, string accessibleName)
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			this.prefix = prefix;
			Height = 28;
			Margin = new Padding(0);
			Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = ShellPalette.Card, ForeColor = ShellPalette.Text, Font = ShellPalette.UiFont, AccessibleName = accessibleName };
			Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Committed?.Invoke(this, EventArgs.Empty); } };
			Box.Leave += (s, e) => { if (Box.Modified) Committed?.Invoke(this, EventArgs.Empty); Box.Modified = false; };
			Controls.Add(Box);
		}

		internal string Value { get { return Box.Text; } set { if (!Box.Focused) { Box.Text = value; Box.Modified = false; } } }
		internal bool ReadOnly { get { return Box.ReadOnly; } set { Box.ReadOnly = value; Invalidate(); } }

		protected override void OnLayout(LayoutEventArgs levent)
		{
			if (Box == null) return; // layout can run from the constructor before the box exists
			int left = 10 + TextRenderer.MeasureText(prefix, ShellPalette.Small).Width + 4;
			Box.SetBounds(left, (Height - Box.PreferredHeight) / 2 + 1, Math.Max(10, Width - left - 6), Box.PreferredHeight);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? ShellPalette.Surface);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (var path = Graph.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 5))
			using (var b = new SolidBrush(ShellPalette.Card))
			using (var p = new Pen(Box.Focused ? ShellPalette.Accent : ShellPalette.BorderStrong))
			{
				g.FillPath(b, path);
				g.DrawPath(p, path);
			}
			TextRenderer.DrawText(g, prefix, ShellPalette.Small, new Rectangle(8, 0, 30, Height), ShellPalette.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
		}
	}

	/// <summary>A thin slider with a value readout; raises Committed when the user lets go.</summary>
	internal class FlatSlider : Control
	{
		private int value, minimum, maximum = 100;
		private bool dragging;
		internal string Caption { get; set; }
		internal Func<int, string> Format { get; set; }
		internal event EventHandler Committed;

		internal FlatSlider(string caption, int min, int max, int value)
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
			Caption = caption;
			minimum = min;
			maximum = max;
			this.value = value;
			Height = 26;
			Margin = new Padding(0, 1, 0, 1);
			TabStop = true;
			AccessibleName = caption;
			AccessibleRole = AccessibleRole.Slider;
		}

		internal int Value { get { return value; } set { if (!dragging) { this.value = Math.Max(minimum, Math.Min(maximum, value)); Invalidate(); } } }

		private Rectangle Track { get { return new Rectangle(78, Height / 2 - 2, Math.Max(10, Width - 78 - 40), 4); } }

		private void SetFromX(int x)
		{
			var t = Track;
			double f = Math.Max(0, Math.Min(1, (x - t.X) / (double)t.Width));
			value = (int)Math.Round(minimum + f * (maximum - minimum));
			Invalidate();
		}

		protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Focus(); SetFromX(e.X); base.OnMouseDown(e); }
		protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (dragging) { dragging = false; Committed?.Invoke(this, EventArgs.Empty); }
			base.OnMouseUp(e);
		}
		protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
			{
				value = Math.Max(minimum, Math.Min(maximum, value + (e.KeyCode == Keys.Left ? -1 : 1) * Math.Max(1, (maximum - minimum) / 40)));
				Invalidate();
				Committed?.Invoke(this, EventArgs.Empty);
			}
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? ShellPalette.Surface);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			TextRenderer.DrawText(g, Caption, ShellPalette.UiFont, new Rectangle(0, 0, 76, Height), ShellPalette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
			var t = Track;
			double f = maximum == minimum ? 0 : (value - minimum) / (double)(maximum - minimum);
			int x = t.X + (int)(f * t.Width);
			using (var b = new SolidBrush(ShellPalette.BorderStrong)) g.FillRectangle(b, t);
			using (var b = new SolidBrush(Enabled ? ShellPalette.Link : ShellPalette.Faint)) g.FillRectangle(b, t.X, t.Y, x - t.X, t.Height);
			using (var b = new SolidBrush(Enabled ? ShellPalette.Text : ShellPalette.Faint)) g.FillEllipse(b, x - 6, Height / 2 - 6, 12, 12);
			if (Focused) using (var p = new Pen(ShellPalette.Link)) g.DrawEllipse(p, x - 8, Height / 2 - 8, 16, 16);
			TextRenderer.DrawText(g, Format != null ? Format(value) : value.ToString(), ShellPalette.UiFont, new Rectangle(Width - 38, 0, 38, Height), ShellPalette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
		}
	}

	/// <summary>Dark drop-down list. Items are shown with ToString().</summary>
	internal class FlatCombo : ComboBox
	{
		internal FlatCombo()
		{
			DropDownStyle = ComboBoxStyle.DropDownList;
			FlatStyle = FlatStyle.Flat;
			DrawMode = DrawMode.OwnerDrawFixed;
			ItemHeight = 20;
			BackColor = ShellPalette.Card;
			ForeColor = ShellPalette.Text;
			Font = ShellPalette.UiFont;
			Margin = new Padding(0, 2, 0, 2);
		}

		/// <summary>Draw as muted right-aligned text with a chevron instead of a box.</summary>
		internal bool Plain { get; set; }

		/// <summary>Paints the closed face over the system frame, so it matches the dark inputs.</summary>
		protected override void WndProc(ref Message m)
		{
			base.WndProc(ref m);
			if (m.Msg != 0x000F || !IsHandleCreated) return; // WM_PAINT
			using (var g = Graphics.FromHwnd(Handle))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				var parent = Parent?.BackColor ?? ShellPalette.Surface;
				using (var clear = new SolidBrush(parent)) g.FillRectangle(clear, ClientRectangle);
				string text = SelectedItem?.ToString() ?? Text;
				Color fore = Enabled ? (Plain ? ShellPalette.Muted : ShellPalette.Text) : ShellPalette.Faint;
				if (Plain)
				{
					// Reads as text with a chevron, like "45° scan ▾".
					TextRenderer.DrawText(g, text, Font, new Rectangle(0, 0, Width - 16, Height), fore,
						TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
					if (Enabled) ShellIcons.Draw(g, ShellIcon.Chevron, new Rectangle(Width - 14, Height / 2 - 6, 12, 12), ShellPalette.Muted);
					return;
				}
				var rect = new Rectangle(0, 0, Width - 1, Height - 1);
				using (var path = Graph.RoundedRect(rect, 5))
				using (var fill = new SolidBrush(ShellPalette.Input))
				using (var pen = new Pen(Focused || DroppedDown ? ShellPalette.Accent : ShellPalette.BorderStrong))
				{
					g.FillPath(fill, path);
					g.DrawPath(pen, path);
				}
				TextRenderer.DrawText(g, text, Font, new Rectangle(7, 0, Width - 25, Height), fore,
					TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
				ShellIcons.Draw(g, ShellIcon.Chevron, new Rectangle(Width - 18, Height / 2 - 6, 12, 12), ShellPalette.Muted);
			}
		}

		protected override void OnDrawItem(DrawItemEventArgs e)
		{
			bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
			using (var b = new SolidBrush(selected ? ShellPalette.Accent : ShellPalette.Card)) e.Graphics.FillRectangle(b, e.Bounds);
			if (e.Index >= 0)
				TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, e.Bounds, Enabled ? ShellPalette.Text : ShellPalette.Faint,
					TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
		}
	}

	/// <summary>A card for one pass: number, name, a menu, and power / speed / scan pickers on the second line.</summary>
	internal class PassCard : Panel
	{
		internal readonly FlatCombo Power = new FlatCombo { Width = 70 };
		internal readonly FlatCombo Speed = new FlatCombo { Width = 82 };
		internal readonly FlatCombo Scan = new FlatCombo { Width = 92, Plain = true };
		internal readonly ShellButton Menu = new ShellButton { Icon = ShellIcon.Dots, ButtonStyle = ShellButtonStyle.Ghost, Width = 24, Height = 22, Margin = Padding.Empty, TabStop = false };
		internal string Number { get; set; }
		internal string Title { get; set; }
		internal string Time { get; set; }
		private bool selected;

		/// <summary>Highlights the card; its background is passed to the pickers so their corners blend in.</summary>
		internal bool Selected
		{
			get { return selected; }
			set { selected = value; BackColor = value ? ShellPalette.SelectedCard : ShellPalette.Surface; Invalidate(true); }
		}

		internal PassCard()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			Height = 66;
			Margin = new Padding(0, 2, 0, 4);
			BackColor = ShellPalette.Surface;
			Menu.AccessibleName = "Pass options";
			Controls.AddRange(new Control[] { Menu, Power, Speed, Scan });
		}

		protected override void OnLayout(LayoutEventArgs levent)
		{
			Menu.Location = new Point(Width - Menu.Width - 6, 6);
			int y = 34;
			Power.SetBounds(24, y, 70, 24);
			Speed.SetBounds(100, y, 82, 24);
			Scan.SetBounds(188, y, Math.Max(60, Width - 188 - 8), 24);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? ShellPalette.Surface);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (var path = Graph.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 7))
			using (var b = new SolidBrush(Selected ? ShellPalette.SelectedCard : ShellPalette.Surface))
			using (var p = new Pen(Selected ? ShellPalette.Accent : ShellPalette.Border))
			{
				g.FillPath(b, path);
				g.DrawPath(p, path);
			}
			ShellIcons.Draw(g, ShellIcon.Grip, new Rectangle(4, 8, 16, 16), ShellPalette.Faint);
			TextRenderer.DrawText(g, Number, ShellPalette.UiFont, new Rectangle(24, 6, 24, 20), ShellPalette.Muted, TextFormatFlags.VerticalCenter);
			int timeWidth = string.IsNullOrEmpty(Time) ? 0 : TextRenderer.MeasureText(Time, ShellPalette.Small).Width;
			TextRenderer.DrawText(g, Title, ShellPalette.UiBold, new Rectangle(50, 6, Width - 50 - 36 - timeWidth, 20), ShellPalette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			if (timeWidth > 0)
				TextRenderer.DrawText(g, Time, ShellPalette.Small, new Rectangle(Width - 34 - timeWidth, 6, timeWidth, 20), ShellPalette.Muted, TextFormatFlags.VerticalCenter);
		}
	}

	/// <summary>A label: value row for settings lists. Clickable when it has an action.</summary>
	internal class SettingRow : Control
	{
		private bool hover;
		internal string Value { get; set; }
		internal bool Clickable { get; set; }

		internal SettingRow(string text)
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			Text = text;
			Height = 28;
			Margin = new Padding(0);
		}

		protected override void OnMouseEnter(EventArgs e) { hover = Clickable; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(hover ? ShellPalette.Card : Parent?.BackColor ?? ShellPalette.Surface);
			Cursor = Clickable ? Cursors.Hand : Cursors.Default;
			TextRenderer.DrawText(g, Text, ShellPalette.UiFont, new Rectangle(0, 0, Width / 2, Height), ShellPalette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
			string value = (Value ?? "") + (Clickable ? "  ▾" : "");
			TextRenderer.DrawText(g, value, ShellPalette.UiFont, new Rectangle(Width / 2, 0, Width / 2, Height), ShellPalette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
		}
	}

	/// <summary>One pre-flight line with a coloured mark.</summary>
	internal class CheckRow : Control
	{
		internal Color MarkColor { get; set; }
		internal string Mark { get; set; }

		internal CheckRow()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			Margin = new Padding(0, 1, 0, 1);
		}

		internal int Measure(int width)
		{
			return Math.Max(20, TextRenderer.MeasureText(Text, ShellPalette.UiFont, new Size(Math.Max(20, width - 20), int.MaxValue), TextFormatFlags.WordBreak).Height + 2);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.Clear(Parent?.BackColor ?? ShellPalette.Surface);
			TextRenderer.DrawText(e.Graphics, Mark, ShellPalette.UiBold, new Rectangle(0, 0, 18, 20), MarkColor, TextFormatFlags.Top | TextFormatFlags.Left);
			TextRenderer.DrawText(e.Graphics, Text, ShellPalette.UiFont, new Rectangle(20, 0, Width - 20, Height), ShellPalette.TextSoft, TextFormatFlags.WordBreak | TextFormatFlags.Top);
		}
	}
}
