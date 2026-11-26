using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class GameSidePanel : TableLayoutPanel, IBackdropOwner
	{
		public GameSidePanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}

		protected override void OnPaintBackground(PaintEventArgs e) { UiSurface.PaintFrameBackdrop(this, this, e.Graphics); }

		public void PaintBackdrop(Graphics graphics)
		{
			Rectangle r = base.ClientRectangle;
			if (r.Width <= 0 || r.Height <= 0) return;

			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				using (var bg = new SolidBrush(Color.FromArgb(14, 20, 32))) graphics.FillRectangle(bg, r);
				UiTheme.DrawCustomBackground(graphics, this, r, Color.FromArgb(14, 20, 32));
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				using (var bg = new SolidBrush(Color.FromArgb(61, 49, 34))) graphics.FillRectangle(bg, r);
				if (UiTheme.CustomBackground != null)
					UiTheme.DrawCustomBackground(graphics, this, r, Color.FromArgb(61, 49, 34));
				using (var outer = new Pen(Color.FromArgb(25, 18, 11), 2f))
					graphics.DrawRectangle(outer, 1, 1, r.Width - 2, r.Height - 2);
				using (var inner = new Pen(Color.FromArgb(107, 83, 51), 2f))
					graphics.DrawRectangle(inner, 3, 3, Math.Max(1, r.Width - 6), Math.Max(1, r.Height - 6));
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				using (var bg = new SolidBrush(Color.FromArgb(244, 243, 239))) graphics.FillRectangle(bg, r);
				if (UiTheme.CustomBackground != null)
					UiTheme.DrawCustomBackground(graphics, this, r, Color.FromArgb(244, 243, 239));
				using (var pen = new Pen(Color.FromArgb(204, 200, 190), 1f))
					graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				using (var bg = new SolidBrush(Color.FromArgb(140, 75, 31))) graphics.FillRectangle(bg, r);
				if (UiTheme.CustomBackground != null)
					UiTheme.DrawCustomBackground(graphics, this, r, Color.FromArgb(140, 75, 31));
				using (var dark = new Pen(Color.FromArgb(41, 18, 4), 2f))
					graphics.DrawRectangle(dark, 1, 1, r.Width - 2, r.Height - 2);
				using (var hi = new Pen(Color.FromArgb(199, 123, 64), 2f))
				{
					graphics.DrawLine(hi, 3, 3, r.Width - 4, 3);
					graphics.DrawLine(hi, 3, 3, 3, r.Height - 4);
				}
				using (var lo = new Pen(Color.FromArgb(92, 45, 14), 2f))
				{
					graphics.DrawLine(lo, 3, r.Height - 4, r.Width - 4, r.Height - 4);
					graphics.DrawLine(lo, r.Width - 4, 3, r.Width - 4, r.Height - 4);
				}
				return;
			}

			using (var baseBrush = new SolidBrush(Color.FromArgb(104, 84, 49))) graphics.FillRectangle(baseBrush, r);
			Image texture = GameArt.Get("game-ground.png");
			if (texture != null) using (var brush = new TextureBrush(texture)) graphics.FillRectangle(brush, r);
			using (var shade = new SolidBrush(Color.FromArgb(58, 25, 18, 8))) graphics.FillRectangle(shade, r);
			if (UiTheme.CustomBackground != null)
				UiTheme.DrawCustomBackground(graphics, this, r, Color.FromArgb(45, 35, 22));
		}
	}
}
