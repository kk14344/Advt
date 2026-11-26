using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class CardPanel : Panel, IBackdropOwner
	{
		public Color BorderColor = Color.FromArgb(123, 94, 48);
		public Color AccentColor = Color.FromArgb(185, 194, 158, 88);
		private int cornerRadius = 7;

		public int CornerRadius
		{
			get { return cornerRadius; }
			set { cornerRadius = Math.Max(4, value); Invalidate(); }
		}

		public CardPanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
		}

		private Color ParentColor()
		{
			Color color = UiTheme.Palette.SideOuterColor;
			Control parent = Parent;
			while (parent != null)
			{
				if (parent.BackColor.A != 0)
				{
					color = parent.BackColor;
					break;
				}
				parent = parent.Parent;
			}
			return color;
		}

		protected override void OnPaintBackground(PaintEventArgs e) { UiSurface.PaintFrameBackdrop(this, this, e.Graphics); }

		public void PaintBackdrop(Graphics graphics)
		{
			Rectangle bounds = new Rectangle(0, 0, base.Width, base.Height);
			if (bounds.Width < 4 || bounds.Height < 4) return;

			if (UiTheme.ExtremePerformanceMode)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				using (var brush = new SolidBrush(Color.White))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(214, 211, 202), 1f))
					graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				UiSurface.PaintBackdrop(this, graphics);
				int r = Math.Min(11, Math.Min(bounds.Width / 2, bounds.Height / 2));
				Rectangle rect = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
				using (GraphicsPath path = UiTheme.RoundedRect(rect, Math.Max(4, r)))
				{
					UiTheme.DrawGlassCardSurface(graphics, this, path, Math.Max(4, r), Color.FromArgb(14, 18, 24), false);
				}
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				UiSurface.PaintBackdrop(this, graphics);
				int fillAlpha = UiTheme.CustomBackground != null ? 218 : 255;
				Color faceColor = (BackColor.A != 0 && BackColor != Color.Transparent && UiTheme.CurrentTheme == UiTheme.ThemeClassic)
					? BackColor : Color.FromArgb(fillAlpha, 255, 255, 255);
				Rectangle rect = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
				using (GraphicsPath path = UiTheme.RoundedRect(rect, 3))
				using (var brush = new SolidBrush(faceColor))
				using (var pen = new Pen(Color.FromArgb(214, 211, 202), 1f))
				{
					graphics.SmoothingMode = SmoothingMode.AntiAlias;
					graphics.FillPath(brush, path);
					graphics.DrawPath(pen, path);
				}
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				UiSurface.PaintBackdrop(this, graphics);
				Rectangle rect = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
				int alpha = UiTheme.CustomBackground != null ? 218 : 255;
				using (var brush = new SolidBrush(Color.FromArgb(alpha, 251, 232, 190)))
					graphics.FillRectangle(brush, rect);
				using (var border = new Pen(Color.FromArgb(99, 51, 18), 2f))
					graphics.DrawRectangle(border, 1, 1, Math.Max(1, bounds.Width - 3), Math.Max(1, bounds.Height - 3));
				using (var hi = new Pen(Color.FromArgb(255, 247, 222), 2f))
				{
					graphics.DrawLine(hi, 3, 3, bounds.Width - 4, 3);
					graphics.DrawLine(hi, 3, 3, 3, bounds.Height - 4);
				}
				using (var lo = new Pen(Color.FromArgb(223, 192, 132), 2f))
				{
					graphics.DrawLine(lo, 3, bounds.Height - 4, bounds.Width - 4, bounds.Height - 4);
					graphics.DrawLine(lo, bounds.Width - 4, 3, bounds.Width - 4, bounds.Height - 4);
				}
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				UiSurface.PaintBackdrop(this, graphics);
				Rectangle faceP = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
				if (UiTheme.CustomBackground != null)
				{
					using (var brush = new SolidBrush(Color.FromArgb(222, 243, 228, 188)))
						graphics.FillRectangle(brush, faceP);
					using (var pen = new Pen(Color.FromArgb(138, 105, 57), 2f))
						graphics.DrawRectangle(pen, 1, 1, Math.Max(1, faceP.Width - 2), Math.Max(1, faceP.Height - 2));
				}
				else
				{
					Image paper = GameArt.Get("game-paper_bg4_d.png") ?? GameArt.Get("game-paper_bg1.png");
					if (paper != null)
					{
						GameArt.DrawNinePatch(graphics, paper, faceP, 16, 10, null);
					}
					else
					{
						using (var brush = new SolidBrush(Color.FromArgb(243, 228, 188)))
							graphics.FillRectangle(brush, faceP);
						using (var pen = new Pen(Color.FromArgb(138, 105, 57), 2f))
							graphics.DrawRectangle(pen, 1, 1, Math.Max(1, faceP.Width - 2), Math.Max(1, faceP.Height - 2));
					}
				}
				return;
			}

			// Classic 1.44
			if (UiTheme.CustomBackground != null)
				UiSurface.PaintBackdrop(this, graphics);
			else
				using (var background = new SolidBrush(ParentColor())) graphics.FillRectangle(background, bounds);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.PixelOffsetMode = PixelOffsetMode.Half;
			int radius = Math.Min(cornerRadius, Math.Min(bounds.Width / 2, bounds.Height / 2));
			radius = Math.Max(4, radius);
			Rectangle face = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 2);
			using (GraphicsPath shadow = UiTheme.RoundedRect(new Rectangle(1, 2, face.Width, face.Height), radius))
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(78, 17, 12, 7)))
			{
				graphics.FillPath(brush, shadow);
			}
			using (GraphicsPath path = UiTheme.RoundedRect(face, radius))
			{
				GraphicsState state = graphics.Save();
				graphics.SetClip(path, CombineMode.Intersect);
				Color baseColor = (base.BackColor.A != 0) ? base.BackColor : Color.FromArgb(87, 73, 42);
				int a = UiTheme.CustomBackground != null ? 215 : 255;
				using (LinearGradientBrush brush = new LinearGradientBrush(face,
					Color.FromArgb(a, UiTheme.Mix(baseColor, Color.White, 0.07f)),
					Color.FromArgb(a, UiTheme.Mix(baseColor, Color.Black, 0.16f)), 90f))
				{
					graphics.FillRectangle(brush, face);
				}
				if (UiTheme.CustomBackground == null)
				{
					Image paper = GameArt.Get("game-paper_bg1.png");
					if (paper != null)
					{
						using (ImageAttributes shade = GameArt.Overlay(Color.Black, 0.62f))
						{
							GameArt.DrawNinePatch(graphics, paper, face, 12, shade);
						}
					}
				}
				using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(22, 255, 242, 205)))
				{
					graphics.FillRectangle(brush2, face.Left + radius, face.Top + 1, Math.Max(1, face.Width - radius * 2), 2);
				}
				graphics.Restore(state);
				using (Pen pen = new Pen(Color.FromArgb(185, 33, 24, 13), 1.5f))
				{
					graphics.DrawPath(pen, path);
				}
				Rectangle inner = Rectangle.Inflate(face, -3, -3);
				if (inner.Width > 4 && inner.Height > 4)
				{
					using (GraphicsPath innerPath = UiTheme.RoundedRect(inner, Math.Max(3, radius - 2)))
					using (Pen pen2 = new Pen(Color.FromArgb(150, BorderColor), 1f))
					{
						graphics.DrawPath(pen2, innerPath);
					}
				}
				if (face.Width > radius * 2 + 8)
				{
					using (Pen pen3 = new Pen(Color.FromArgb(110, AccentColor), 1f))
					{
						graphics.DrawLine(pen3, face.Left + radius + 2, face.Top + 2,
							face.Right - radius - 3, face.Top + 2);
					}
				}
			}
		}
	}
}
