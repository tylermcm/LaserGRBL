using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Tools;
using static LaserGRBL.UserControls.ColorSlider;

namespace LaserGRBL.UserControls
{
    public class GrblButton: Button
    {

        private bool mHover = false;
        private bool mPressed = false;

        public GrblButton() {
            SetStyle(ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ColorScheme.FormBackColor);
            Color backColor = Enabled ? ColorScheme.LogBackColor : ColorScheme.FormBackColor;
            Color textColor = Enabled ? ColorScheme.FormForeColor : Blend(ColorScheme.FormForeColor, backColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var highlight = ColorScheme.DarkScheme ? -0.12f : 0.12f;
            if (Enabled && mHover) backColor = ColorScheme.ChangeColorBrightness(backColor, highlight);
            if (Enabled && mPressed) backColor = ColorScheme.ChangeColorBrightness(backColor, highlight);
            Rectangle borderRect = ClientRectangle;
            if (borderRect.Width < 2 || borderRect.Height < 2) return;
            borderRect.Width -= 1; borderRect.Height -= 1;
            using (Brush backColorBrush = new SolidBrush(backColor))
            using (Brush foreColorBrush = new SolidBrush(textColor))
            using (Pen borderPen = new Pen(ColorScheme.ControlsBorder))
            using (GraphicsPath backRect = Graph.RoundedRect(borderRect, 5))
            {
                e.Graphics.FillPath(backColorBrush, backRect);
                e.Graphics.DrawPath(borderPen, backRect);
                int startX = 0;
                if (Image != null)
                {
                    startX = Height - 8;
                    Rectangle rect = new Rectangle(4, 4, Height - 8, Height - 8);
                    e.Graphics.DrawImage(Image, rect);
                }
                SizeF fontSize = e.Graphics.MeasureString(Text, Font);
                e.Graphics.DrawString(Text, Font, foreColorBrush, startX + (Width - fontSize.Width - startX) / 2, (Height - fontSize.Height) / 2);
            }
            if (Enabled && Focused && ShowFocusCues && borderRect.Width > 8 && borderRect.Height > 8)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(borderRect, -4, -4), textColor, backColor);
        }

        private static Color Blend(Color foreground, Color background)
        {
            return Color.FromArgb(
                (foreground.R * 4 + background.R) / 5,
                (foreground.G * 4 + background.G) / 5,
                (foreground.B * 4 + background.B) / 5);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            mPressed = mHover = false;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            mHover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            mHover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            mPressed = true;
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            mPressed = false;
            Invalidate();
        }


    }
}
