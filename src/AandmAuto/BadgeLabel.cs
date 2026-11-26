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
				Color tintC = activeState ? Color.FromArgb(24, 168, 96) : Color.White;
				int tintA = activeState ? 96 : 0;
				UiTheme.DrawLiquidGlassPill(graphics, this, gb, gb.Height / 2, tintC, tintA, Color.White, false, false, activeState, base.Enabled,
					0f, 0f, 0f, new Point(-999, -999), !activeState);

				Color ink = activeState ? Color.White : LiquidGlassEngine.GlassInk(base.Enabled);
				Color shadow = activeState ? Color.FromArgb(145, 0, 0, 0) : LiquidGlassEngine.GlassTextShadow();
				Size measured = TextRenderer.MeasureText(Text ?? string.Empty, Font, new Size(10000, 10000),
					TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
				int dotSize = 6;
				int gap = 5;
				if (measured.Width + dotSize + gap + 8 <= gb.Width)
				{
					int totalW = dotSize + gap + measured.Width;
					int startX = gb.X + (gb.Width - totalW) / 2;
					float dotY = gb.Y + (gb.Height - dotSize) / 2f;
					graphics.SmoothingMode = SmoothingMode.AntiAlias;
					Color dotC = activeState
						? Color.FromArgb(255, 62, 232, 134)
						: (LiquidGlassEngine.IsBrightBackdrop() ? Color.FromArgb(140, 30, 44, 66) : Color.FromArgb(165, 255, 255, 255));
					using (var db = new SolidBrush(dotC))
						graphics.FillEllipse(db, startX, dotY, dotSize, dotSize);
					Rectangle tb = new Rectangle(startX + dotSize + gap, gb.Y, Math.Max(1, gb.Right - (startX + dotSize + gap)), gb.Height);
					TextFormatFlags bf = UiSurface.TextFlags | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
					if (shadow.A > 0)
						GameText.Draw(graphics, Text, Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadow, bf);
					GameText.Draw(graphics, Text, Font, tb, ink, bf);
				}
				else
				{
					TextFormatFlags bf = UiSurface.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
					if (shadow.A > 0)
						GameText.Draw(graphics, Text, Font, new Rectangle(gb.X + 1, gb.Y + 1, gb.Width, gb.Height), shadow, bf);
					GameText.Draw(graphics, Text, Font, gb, ink, bf);
				}
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
