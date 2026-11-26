using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class BadgeLabel : Label
	{
		public BadgeLabel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}

		protected override void OnPaint(PaintEventArgs e)
        {
            UiSurface.PaintWhole(this,e,PaintContent);
        }

        private void PaintContent(Graphics graphics)
		{
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				UiSurface.PaintBackdrop(this, graphics);
				Rectangle gb = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
				bool activeState = !string.IsNullOrEmpty(Text) && (Text.Contains("运行") || Text.Contains("挖矿") || Text.Contains("日常") || Text.Contains("竞技") || Text.Contains("首领") || Text.Contains("推进") || Text.Contains("执行") || Text.Contains("强化") || Text.Contains("连抽") || Text.Contains("采集"));
				UiTheme.DrawLiquidGlassPill(graphics, this, gb, gb.Height / 2, Color.White, activeState ? 40 : 16, Color.White, false, false, activeState, base.Enabled);
				TextFormatFlags bf = UiSurface.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
				GameText.Draw(graphics, Text, Font, new Rectangle(gb.X + 1, gb.Y + 1, gb.Width, gb.Height), Color.FromArgb(155, 0, 0, 0), bf);
				GameText.Draw(graphics, Text, Font, gb, base.Enabled ? Color.White : Color.FromArgb(175, 255, 255, 255), bf);
				return;
			}
            using(var background=new SolidBrush(BackColor))graphics.FillRectangle(background,ClientRectangle);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				using (var brush = new SolidBrush(BackColor))
				using (var pen = new Pen(Color.FromArgb(41, 18, 4), 2f))
				{
					graphics.FillRectangle(brush, bounds);
					graphics.DrawRectangle(pen, 1, 1, Math.Max(1, base.Width - 2), Math.Max(1, base.Height - 2));
				}
				GameText.Draw(graphics, Text, Font, bounds, base.Enabled ? ForeColor : UiTheme.Palette.MutedColor, UiSurface.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
				return;
			}
			int radius = UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? 3 : 11;
			Color bdr = UiTheme.CurrentTheme == UiTheme.ThemeClassic ? Color.FromArgb(145, 185, 149, 76) : UiTheme.Palette.BadgeBorderColor;
			using (GraphicsPath path = UiTheme.RoundedRect(bounds, radius))
			{
				using (SolidBrush brush = new SolidBrush(BackColor))
				using (Pen pen = new Pen(bdr))
				{
					graphics.FillPath(brush, path);
					graphics.DrawPath(pen, path);
				}
			}
			GameText.Draw(graphics, Text, Font, bounds, base.Enabled ? ForeColor : Color.FromArgb(125, 130, 112), UiSurface.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
	}
}
