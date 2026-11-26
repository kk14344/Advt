using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class PixelBoxPanel : Panel, IBackdropOwner
	{
		public int BorderEdge = 13;
		public int SourceEdge = 46;
		public Color FillTint = Color.Empty;

		public PixelBoxPanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			UiSurface.PaintFrameBackdrop(this, this, e.Graphics);
		}

		public void PaintBackdrop(Graphics graphics)
		{
			Rectangle bounds = new Rectangle(0, 0, Width, Height);
			if (bounds.Width < 4 || bounds.Height < 4) return;
			if (UiTheme.ExtremePerformanceMode)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				using (var brush = new SolidBrush(Color.White))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(214, 211, 202), 1f))
					graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
				return;
			}
			UiSurface.PaintBackdrop(this, graphics);

			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
				using (GraphicsPath path = UiTheme.RoundedRect(r, 9))
					UiTheme.DrawGlassCardSurface(graphics, this, path, 9, Color.FromArgb(14, 18, 24), false);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				int a = UiTheme.CustomBackground != null ? 222 : 255;
				using (var brush = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(214, 211, 202), 1f))
					graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				int a = UiTheme.CustomBackground != null ? 222 : 255;
				using (var brush = new SolidBrush(Color.FromArgb(a, 251, 232, 190)))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(99, 51, 18), 2f))
					graphics.DrawRectangle(pen, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
				using (var hi = new Pen(Color.FromArgb(255, 247, 222), 2f))
				{
					graphics.DrawLine(hi, 3, 3, Width - 4, 3);
					graphics.DrawLine(hi, 3, 3, 3, Height - 4);
				}
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				if (UiTheme.CustomBackground != null)
				{
					using (var brush = new SolidBrush(Color.FromArgb(222, 243, 228, 188)))
						graphics.FillRectangle(brush, bounds);
					using (var pen = new Pen(Color.FromArgb(138, 105, 57), 2f))
						graphics.DrawRectangle(pen, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
				}
				else
				{
					Image paper = GameArt.Get("game-paper_bg4_d.png") ?? GameArt.Get("game-paper_bg1.png");
					if (paper != null) GameArt.DrawNinePatch(graphics, paper, bounds, 16, 10, null);
					else using (var brush = new SolidBrush(Color.FromArgb(243, 228, 188))) graphics.FillRectangle(brush, bounds);
				}
				return;
			}

			Image box = GameArt.Get("game-box_bg.png");
			if (box != null && UiTheme.CustomBackground == null)
			{
				GameArt.DrawNinePatch(graphics, box, bounds, SourceEdge, BorderEdge, null);
				if (FillTint.A > 0)
				{
					Rectangle inner = Rectangle.Inflate(bounds, -BorderEdge, -BorderEdge);
					if (inner.Width > 0 && inner.Height > 0)
					{
						using (var brush = new SolidBrush(FillTint))
							graphics.FillRectangle(brush, inner);
					}
				}
			}
			else
			{
				int alpha = UiTheme.CustomBackground != null ? 215 : 255;
				Color baseBg = FillTint.A > 0 ? Color.FromArgb(alpha, FillTint.R, FillTint.G, FillTint.B) : Color.FromArgb(alpha, 87, 73, 42);
				using (var brush = new SolidBrush(baseBg)) graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(143, 135, 97), 2f)) graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
			}
		}
	}

	internal sealed class ParchmentPanel : Panel, IBackdropOwner
	{
		public ParchmentPanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			UiSurface.PaintFrameBackdrop(this, this, e.Graphics);
		}

		public void PaintBackdrop(Graphics graphics)
		{
			Rectangle bounds = new Rectangle(0, 0, Width, Height);
			if (bounds.Width < 4 || bounds.Height < 4) return;
			if (UiTheme.ExtremePerformanceMode)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				using (var brush = new SolidBrush(Color.White))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(214, 211, 202), 1f))
					graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
				return;
			}
			UiSurface.PaintBackdrop(this, graphics);

			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
				using (GraphicsPath path = UiTheme.RoundedRect(r, 9))
					UiTheme.DrawGlassCardSurface(graphics, this, path, 9, Color.FromArgb(14, 18, 24), false);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				int a = UiTheme.CustomBackground != null ? 225 : 255;
				using (var brush = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(214, 211, 202), 1f))
					graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				int a = UiTheme.CustomBackground != null ? 225 : 255;
				using (var brush = new SolidBrush(Color.FromArgb(a, 251, 232, 190)))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(99, 51, 18), 2f))
					graphics.DrawRectangle(pen, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
				return;
			}
            if (UiTheme.UsesEngineCanvas(this))
            {
                using (var veil = new SolidBrush(Color.FromArgb(145, 245, 232, 169))) graphics.FillRectangle(veil, bounds);
                using (var pen = new Pen(Color.FromArgb(90, 62, 24), 1.5f))
                    graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                return;
            }
			Image paper = GameArt.Get("game-paper_bg1.png");
			if (paper != null)
			{
				GameArt.DrawNinePatch(graphics, paper, bounds, 18, 10, null);
			}
			else
			{
				using (var brush = new SolidBrush(Color.FromArgb(245, 232, 169)))
					graphics.FillRectangle(brush, bounds);
				using (var pen = new Pen(Color.FromArgb(90, 62, 24), 1.5f))
					graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
			}
		}
	}

	internal sealed class InsetPanel : Panel, IBackdropOwner
	{
		public Color FillColor = Color.FromArgb(47, 38, 22);
		public Color OuterHighlight = Color.FromArgb(109, 89, 52);
		public Color InnerBorder = Color.FromArgb(31, 24, 13);
		public Color LeftStripeColor = Color.Empty;

		public InsetPanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			UiSurface.PaintFrameBackdrop(this, this, e.Graphics);
		}

		public void PaintBackdrop(Graphics graphics)
		{
			Rectangle bounds = new Rectangle(0, 0, Width, Height);
			if (bounds.Width < 3 || bounds.Height < 3) return;
			if (UiTheme.ExtremePerformanceMode)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				using (var fill = new SolidBrush(Color.FromArgb(250, 249, 246)))
					graphics.FillRectangle(fill, bounds);
				using (var border = new Pen(Color.FromArgb(224, 221, 213), 1f))
					graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
				if (LeftStripeColor.A > 0)
				{
					using (var stripe = new SolidBrush(LeftStripeColor))
						graphics.FillRectangle(stripe, 1, 1, 4, Math.Max(1, Height - 2));
				}
				return;
			}
			UiSurface.PaintBackdrop(this, graphics);

			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				Rectangle faceG = new Rectangle(0, 0, Width - 1, Height - 1);
				using (GraphicsPath pathG = UiTheme.RoundedRect(faceG, 7))
					UiTheme.DrawGlassCardSurface(graphics, this, pathG, 7, Color.FromArgb(14, 18, 24), true);
				return;
			}
			Color fillC = FillColor;
			Color borderC = InnerBorder;
			Color hiC = OuterHighlight;
			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				fillC = Color.FromArgb(251, 243, 219);
				borderC = Color.FromArgb(191, 161, 106);
				hiC = Color.FromArgb(255, 250, 234);
			}
			else if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				fillC = Color.FromArgb(250, 249, 246);
				borderC = Color.FromArgb(224, 221, 213);
				hiC = Color.Transparent;
			}
			else if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				fillC = Color.FromArgb(255, 246, 220);
				borderC = Color.FromArgb(138, 86, 39);
				hiC = Color.FromArgb(223, 192, 132);
			}

            if (UiTheme.UsesEngineCanvas(this)) fillC = Color.FromArgb(178, fillC);
			Rectangle face = new Rectangle(0, 0, Width, Height - 1);
			using (var fill = new SolidBrush(fillC))
				graphics.FillRectangle(fill, face);
			using (var border = new Pen(borderC, 1f))
				graphics.DrawRectangle(border, face.X, face.Y, face.Width - 1, face.Height - 1);
			if (hiC.A > 0)
			{
				using (var highlight = new Pen(hiC, 1f))
					graphics.DrawLine(highlight, 0, Height - 1, Width - 1, Height - 1);
			}
			if (LeftStripeColor.A > 0)
			{
				using (var stripe = new SolidBrush(LeftStripeColor))
					graphics.FillRectangle(stripe, 1, 1, 4, Math.Max(1, face.Height - 2));
			}
		}
	}

	internal sealed class GameTopTab : Control
	{
		private bool selected;
		private bool hovered;

		public bool Selected
		{
			get { return selected; }
			set { if (selected != value) { selected = value; Invalidate(); } }
		}

		public GameTopTab()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			Cursor = Cursors.Hand;
			BackColor = Color.Transparent;
			Font = UiTheme.GameFont(8f, FontStyle.Bold);
			ForeColor = Color.FromArgb(255, 233, 176);
		}

		internal Rectangle TextBounds
		{
			get
			{
				if (UiTheme.ExtremePerformanceMode || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
					return new Rectangle(6, 2, Math.Max(1, Width - 12), Math.Max(1, Height - 4));
				return new Rectangle(6, 3, Math.Max(1, Width - 20), Math.Max(1, Height - 5));
			}
		}

		protected override void OnPaintBackground(PaintEventArgs e) { }

		protected override void OnPaint(PaintEventArgs e)
		{
			UiSurface.PaintWhole(this, e, PaintContent);
		}

		private void PaintContent(Graphics g)
		{
			if (UiTheme.ExtremePerformanceMode)
			{
				if (Width < 8 || Height < 8) return;
				g.SmoothingMode = SmoothingMode.None;
				Color bg = !Enabled ? Color.FromArgb(240, 239, 234) : (selected ? Color.White : (hovered ? Color.FromArgb(248, 247, 244) : Color.FromArgb(234, 232, 225)));
				using (var b = new SolidBrush(bg))
					g.FillRectangle(b, 0, 0, Width, Height);
				using (var p = new Pen(Color.FromArgb(213, 209, 199), 1f))
					g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
				if (selected)
				{
					using (var accent = new SolidBrush(Color.FromArgb(44, 110, 73)))
						g.FillRectangle(accent, 0, Height - 3, Width, 3);
				}
				Color tc = !Enabled ? Color.FromArgb(153, 149, 140) : (selected ? Color.FromArgb(31, 31, 31) : Color.FromArgb(99, 95, 87));
				TextFormatFlags f = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
				GameText.Draw(g, Text, Font, TextBounds, tc, f);
				return;
			}
			UiSurface.PaintBackdrop(this, g);
			if (Width < 8 || Height < 8) return;

			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				Rectangle rg = new Rectangle(0, selected ? 0 : 2, Width - 1, Height - (selected ? 0 : 2) - 2);
				UiTheme.DrawLiquidGlassPill(g, this, rg, rg.Height / 2, Color.White, selected ? 42 : 16, Color.White, hovered, false, selected, Enabled);
				Color tcg = selected ? Color.White : Color.FromArgb(228, 255, 255, 255);
				Rectangle tbg = TextBounds;
				if (!selected) tbg.Y += 1;
				TextFormatFlags fg = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
				GameText.Draw(g, Text, Font, new Rectangle(tbg.X + 1, tbg.Y + 1, tbg.Width, tbg.Height), Color.FromArgb(155, 0, 0, 0), fg);
				GameText.Draw(g, Text, Font, tbg, tcg, fg);
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
				Color bg = selected ? Color.FromArgb(255, 255, 255) : (hovered ? Color.FromArgb(248, 247, 244) : Color.FromArgb(234, 232, 225));
				using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
				using (var p = new Pen(Color.FromArgb(213, 209, 199), 1f)) g.DrawRectangle(p, r);
				if (selected)
				{
					using (var accent = new SolidBrush(Color.FromArgb(44, 110, 73)))
						g.FillRectangle(accent, 0, Height - 3, Width, 3);
				}
				Color tc = selected ? Color.FromArgb(31, 31, 31) : Color.FromArgb(99, 95, 87);
				TextFormatFlags f = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
				GameText.Draw(g, Text, Font, TextBounds, tc, f);
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				Rectangle r = new Rectangle(0, selected ? 0 : 2, Width - 1, Height - (selected ? 0 : 2) - 1);
				Color bg = selected ? Color.FromArgb(251, 232, 190) : (hovered ? Color.FromArgb(186, 114, 58) : Color.FromArgb(168, 100, 49));
				Color tc = selected ? Color.FromArgb(59, 30, 10) : Color.FromArgb(252, 229, 184);
				using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
				using (var p = new Pen(Color.FromArgb(41, 18, 4), 2f)) g.DrawRectangle(p, r);
				using (var hi = new Pen(selected ? Color.FromArgb(255, 247, 222) : Color.FromArgb(201, 128, 71), 2f))
					g.DrawLine(hi, r.Left + 2, r.Top + 2, r.Right - 2, r.Top + 2);
				Rectangle tbw = TextBounds;
				if (!selected) tbw.Y += 1;
				TextFormatFlags fw = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
				GameText.Draw(g, Text, Font, tbw, tc, fw);
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				Rectangle rp = new Rectangle(0, selected ? 0 : 2, Width, Height - (selected ? 0 : 2));
				Image img = GameArt.Get(selected ? "game-buttonbg_1.png" : "game-buttonbg_12.png");
				if (img != null)
				{
					ImageAttributes attrs = hovered && !selected ? GameArt.Overlay(Color.White, 0.12f) : null;
					try
					{
						if (attrs == null) GameArt.DrawNinePatch(g, img, rp, 12, 6, null);
						else GameArt.DrawNinePatch(g, img, rp, 12, 6, attrs);
					}
					finally { if (attrs != null) attrs.Dispose(); }
				}
				else
				{
					using (var b = new SolidBrush(selected ? Color.FromArgb(224, 188, 76) : Color.FromArgb(148, 142, 132)))
						g.FillRectangle(b, rp);
				}
				Rectangle tbp = TextBounds;
				if (!selected) tbp.Y += 1;
				TextFormatFlags fp = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
				GameText.Draw(g, Text, Font, tbp, selected ? Color.FromArgb(43, 25, 3) : Color.FromArgb(30, 30, 30), fp);
				return;
			}

			Image tabImg = GameArt.Get("game-top_tab.png");
			Rectangle rect = new Rectangle(0, selected ? 0 : 2, Width, Height - (selected ? 0 : 2));
			if (tabImg != null)
			{
				ImageAttributes attrs = null;
				if (!selected) attrs = GameArt.Overlay(Color.Black, hovered ? 0.14f : 0.28f);
				else if (hovered) attrs = GameArt.Overlay(Color.White, 0.08f);
				try
				{
					DrawAsymNinePatch(g, tabImg, rect, 14, 12, 38, 4, 8, 6, 18, 2, attrs);
				}
				finally
				{
					if (attrs != null) attrs.Dispose();
				}
			}
			else
			{
				Color top = selected ? Color.FromArgb(138, 114, 65) : Color.FromArgb(84, 70, 42);
				Color bot = selected ? Color.FromArgb(95, 76, 42) : Color.FromArgb(56, 46, 28);
				using (var brush = new LinearGradientBrush(rect, top, bot, 90f))
					g.FillRectangle(brush, rect);
				using (var pen = new Pen(Color.Black, 1.2f))
					g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
			}
			Color textColor = selected ? Color.FromArgb(255, 233, 176) : Color.FromArgb(176, 161, 128);
			Rectangle tb = TextBounds;
			if (!selected) tb.Y += 1;
			TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
			GameText.Draw(g, Text, Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), Color.FromArgb(145, 0, 0, 0), flags);
			GameText.Draw(g, Text, Font, tb, textColor, flags);
		}

		private static void DrawAsymNinePatch(Graphics g, Image img, Rectangle target,
			int sL, int sT, int sR, int sB, int dL, int dT, int dR, int dB, ImageAttributes attrs)
		{
			if (img == null || target.Width < 1 || target.Height < 1) return;
			int dl = Math.Min(dL, target.Width / 2);
			int dr = Math.Min(dR, target.Width - dl);
			int dt = Math.Min(dT, target.Height / 2);
			int db = Math.Min(dB, target.Height - dt);
			int[] sx = { 0, sL, Math.Max(sL + 1, img.Width - sR), img.Width };
			int[] sy = { 0, sT, Math.Max(sT + 1, img.Height - sB), img.Height };
			int[] tx = { target.Left, target.Left + dl, target.Right - dr, target.Right };
			int[] ty = { target.Top, target.Top + dt, target.Bottom - db, target.Bottom };
			var oldInterp = g.InterpolationMode;
			var oldOffset = g.PixelOffsetMode;
			g.InterpolationMode = InterpolationMode.NearestNeighbor;
			g.PixelOffsetMode = PixelOffsetMode.Half;
			for (int y = 0; y < 3; y++)
			{
				for (int x = 0; x < 3; x++)
				{
					var dst = new Rectangle(tx[x], ty[y], tx[x + 1] - tx[x], ty[y + 1] - ty[y]);
					var src = new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]);
					if (dst.Width > 0 && dst.Height > 0 && src.Width > 0 && src.Height > 0)
					{
						if (attrs == null) g.DrawImage(img, dst, src, GraphicsUnit.Pixel);
						else g.DrawImage(img, dst, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
					}
				}
			}
			g.InterpolationMode = oldInterp;
			g.PixelOffsetMode = oldOffset;
		}

		protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
	}

	internal sealed class GameProgressBar : Control
	{
		private float fraction;
		public Color FillTop = Color.FromArgb(184, 236, 122);
		public Color FillBottom = Color.FromArgb(121, 184, 60);
		public Color TrackColor = Color.FromArgb(216, 196, 142);
		public Color BorderColor = Color.FromArgb(138, 106, 56);

		public float Fraction
		{
			get { return fraction; }
			set
			{
				float clamped = Math.Max(0f, Math.Min(1f, value));
				if (Math.Abs(fraction - clamped) > 0.002f) { fraction = clamped; Invalidate(); }
			}
		}

		public GameProgressBar()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
			Height = 10;
		}

		protected override void OnPaintBackground(PaintEventArgs e) { }

		protected override void OnPaint(PaintEventArgs e)
		{
			UiSurface.PaintWhole(this, e, PaintContent);
		}

		private void PaintContent(Graphics g)
		{
			UiSurface.PaintBackdrop(this, g);
			if (Width < 4 || Height < 4) return;
			Rectangle r = new Rectangle(0, (Height - 10) / 2, Width - 1, Math.Min(10, Height - 1));

			if (UiTheme.ExtremePerformanceMode)
			{
				g.SmoothingMode = SmoothingMode.None;
				using (var bg = new SolidBrush(Color.FromArgb(230, 227, 219)))
					g.FillRectangle(bg, r);
				int fillWExtreme = (int)Math.Round((r.Width - 2) * fraction);
				if (fillWExtreme > 0 && r.Height > 2)
				{
					Color accent = FillBottom.A > 0 ? FillBottom : Color.FromArgb(44, 110, 73);
					using (var brush = new SolidBrush(accent))
						g.FillRectangle(brush, r.X + 1, r.Y + 1, fillWExtreme, r.Height - 1);
				}
				using (var border = new Pen(Color.FromArgb(204, 200, 190), 1f))
					g.DrawRectangle(border, r);
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				using (var trough = new SolidBrush(Color.FromArgb(74, 39, 14))) g.FillRectangle(trough, r);
				using (var border = new Pen(Color.FromArgb(41, 18, 4), 1f)) g.DrawRectangle(border, r);
				int segments = 10;
				int innerW = Math.Max(10, r.Width - 4);
				int filled = (int)Math.Round(fraction * segments);
				for (int i = 0; i < segments; i++)
				{
					int sx = r.X + 2 + (innerW * i) / segments;
					int ex = r.X + 2 + (innerW * (i + 1)) / segments - 2;
					if (ex <= sx) ex = sx + 1;
					Rectangle seg = new Rectangle(sx, r.Y + 2, ex - sx, Math.Max(2, r.Height - 4));
					Color c = i < filled ? FillBottom : Color.FromArgb(44, 22, 7);
					using (var sb = new SolidBrush(c)) g.FillRectangle(sb, seg);
					if (i < filled && seg.Height >= 3)
					{
						using (var hi = new SolidBrush(FillTop)) g.FillRectangle(hi, seg.X, seg.Y, seg.Width, 2);
					}
				}
				return;
			}

			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				using (GraphicsPath tr = UiTheme.RoundedRect(r, 4))
				using (var bgG = new SolidBrush(Color.FromArgb(110, 14, 20, 32)))
				using (var brG = new Pen(Color.FromArgb(85, 215, 235, 255), 1f))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;
					g.FillPath(bgG, tr);
					g.DrawPath(brG, tr);
				}
				int fwG = (int)Math.Round((r.Width - 2) * fraction);
				if (fwG > 1 && r.Height > 2)
				{
					Rectangle fgRect = new Rectangle(r.X + 1, r.Y + 1, fwG, r.Height - 1);
					using (GraphicsPath fp = UiTheme.RoundedRect(fgRect, 3))
					using (var br = new LinearGradientBrush(fgRect, UiTheme.Mix(FillTop, Color.White, 0.2f), FillBottom, 0f))
						g.FillPath(br, fp);
				}
				return;
			}
			Color track = UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? Color.FromArgb(230, 227, 219) : TrackColor;
			Color bdr = UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? Color.FromArgb(204, 200, 190) : BorderColor;
			using (var bg = new SolidBrush(track)) g.FillRectangle(bg, r);
			int fillW = (int)Math.Round((r.Width - 2) * fraction);
			if (fillW > 0 && r.Height > 2)
			{
				Rectangle fill = new Rectangle(r.X + 1, r.Y + 1, fillW, r.Height - 1);
				if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
				{
					using (var brush = new SolidBrush(FillBottom)) g.FillRectangle(brush, fill);
				}
				else
				{
					using (var brush = new LinearGradientBrush(fill, FillTop, FillBottom, 90f))
						g.FillRectangle(brush, fill);
				}
			}
			using (var border = new Pen(bdr, 1f)) g.DrawRectangle(border, r);
		}
	}

	internal sealed class SettingsRootPanel : TableLayoutPanel, IBackdropOwner
	{
		public SettingsRootPanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			UiSurface.PaintFrameBackdrop(this, this, e.Graphics);
		}

		public void PaintBackdrop(Graphics graphics)
		{
			Rectangle r = ClientRectangle;
			if (r.Width <= 0 || r.Height <= 0) return;
			if (UiTheme.ExtremePerformanceMode)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				using (var b = new SolidBrush(Color.FromArgb(244, 243, 239)))
					graphics.FillRectangle(b, r);
				return;
			}
			Color baseBg = UiTheme.Palette.BgColor;
			using (var b = new SolidBrush(baseBg)) graphics.FillRectangle(b, r);
			if (UiTheme.CustomBackground != null || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
				UiTheme.DrawCustomBackground(graphics, this, r, baseBg);
		}
	}

	internal sealed class WallpaperViewportPanel : Panel
	{
		private readonly Func<bool> showWallpaper;
		internal Func<Graphics, bool> SharedWallpaperPainter { get; set; }

		internal WallpaperViewportPanel(Func<bool> showWallpaper)
		{
			this.showWallpaper = showWallpaper;
			DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
			ResizeRedraw = true;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			base.OnPaintBackground(e);
			if (UiTheme.ExtremePerformanceMode) return;
			if (SharedWallpaperPainter != null && SharedWallpaperPainter(e.Graphics)) return;
			if (showWallpaper()) UiTheme.DrawCustomBackground(e.Graphics, this, ClientRectangle, UiTheme.BackgroundVeilColor);
		}
	}

	internal sealed class ViewportBarPanel : Panel, IBackdropOwner
	{
		internal Func<Graphics, bool> SharedWallpaperPainter { get; set; }
		public ViewportBarPanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			UiSurface.PaintFrameBackdrop(this, this, e.Graphics);
		}

		public void PaintBackdrop(Graphics graphics)
		{
			Rectangle r = ClientRectangle;
			if (r.Width <= 0 || r.Height <= 0) return;
			if (UiTheme.ExtremePerformanceMode)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				using (var b = new SolidBrush(Color.White))
					graphics.FillRectangle(b, r);
				using (var pen = new Pen(Color.FromArgb(214, 211, 202), 1f))
					graphics.DrawLine(pen, 0, r.Height - 1, r.Width - 1, r.Height - 1);
				return;
			}
			if (SharedWallpaperPainter != null && SharedWallpaperPainter(graphics))
			{
				using (var tint = new SolidBrush(Color.FromArgb(112, UiTheme.BackgroundVeilColor)))
					graphics.FillRectangle(tint, ClientRectangle);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass || UiTheme.CustomBackground != null)
			{
				Color baseColor = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
					? Color.FromArgb(14, 20, 32)
					: UiTheme.Palette.SideOuterColor;
				using (var bg = new SolidBrush(baseColor)) graphics.FillRectangle(bg, r);
				UiTheme.DrawCustomBackground(graphics, this, r, baseColor);
				using (var frost = new SolidBrush(Color.FromArgb(UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? 22 : 16, 255, 255, 255)))
					graphics.FillRectangle(frost, r);
				int glossH = Math.Min(12, Math.Max(4, r.Height / 3));
				using (var gloss = new LinearGradientBrush(
					new Rectangle(0, 0, Math.Max(1, r.Width), glossH),
					Color.FromArgb(32, 255, 255, 255),
					Color.FromArgb(0, 255, 255, 255), 90f))
				{
					gloss.WrapMode = WrapMode.TileFlipXY;
					graphics.FillRectangle(gloss, 0, 0, r.Width, glossH);
				}
				if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass)
				{
					using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
						graphics.DrawLine(pen, 0, r.Height - 1, r.Width, r.Height - 1);
				}
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				graphics.Clear(Color.White);
				using (var pen = new Pen(UiTheme.Palette.BorderColor))
					graphics.DrawLine(pen, 0, r.Height - 1, r.Width, r.Height - 1);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				using (var b = new SolidBrush(Color.FromArgb(107, 55, 20)))
					graphics.FillRectangle(b, 0, 0, r.Width, r.Height);
				using (var pen = new Pen(Color.FromArgb(41, 18, 4)))
					graphics.DrawLine(pen, 0, r.Height - 1, r.Width, r.Height - 1);
				return;
			}
			using (var brush = new LinearGradientBrush(new Rectangle(0, 0, r.Width, r.Height),
				Color.FromArgb(83, 65, 37), Color.FromArgb(53, 41, 25), 0f))
			{
				graphics.FillRectangle(brush, 0, 0, r.Width, r.Height);
			}
			using (var pen = new Pen(Color.FromArgb(85, 126, 99, 52)))
				graphics.DrawLine(pen, 0, r.Height - 1, r.Width, r.Height - 1);
		}
	}

	internal sealed class ThemeSwatchPreview : Control
	{
		private bool selected;
		public int ThemeMode { get; private set; }
		public bool Selected
		{
			get { return selected; }
			set { if (selected != value) { selected = value; Invalidate(); } }
		}

		public ThemeSwatchPreview(int mode)
		{
			ThemeMode = mode;
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			Cursor = Cursors.Hand;
			BackColor = Color.Transparent;
		}

		protected override void OnPaintBackground(PaintEventArgs e) { }
		protected override void OnPaint(PaintEventArgs e) { UiSurface.PaintWhole(this, e, PaintContent); }

		private void PaintContent(Graphics g)
		{
			UiSurface.PaintBackdrop(this, g);
			int w = Width - 2, h = Height - 2;
			if (w < 12 || h < 12) return;
			Color outer, page, btn1, btn2, ink;
			if (ThemeMode == UiTheme.ThemeParchment)
			{
				outer = Color.FromArgb(61, 49, 34); page = Color.FromArgb(243, 228, 188);
				btn1 = Color.FromArgb(148, 200, 44); btn2 = Color.FromArgb(228, 191, 48); ink = Color.FromArgb(92, 50, 9);
			}
			else if (ThemeMode == UiTheme.ThemeNativeLight)
			{
				outer = Color.FromArgb(244, 243, 239); page = Color.FromArgb(255, 255, 255);
				btn1 = Color.FromArgb(44, 110, 73); btn2 = Color.FromArgb(214, 211, 202); ink = Color.FromArgb(31, 31, 31);
			}
			else if (ThemeMode == UiTheme.ThemeWarmWood)
			{
				outer = Color.FromArgb(140, 75, 31); page = Color.FromArgb(251, 232, 190);
				btn1 = Color.FromArgb(104, 176, 59); btn2 = Color.FromArgb(234, 179, 56); ink = Color.FromArgb(59, 30, 10);
			}
			else if (ThemeMode == UiTheme.ThemeLiquidGlass)
			{
				outer = Color.FromArgb(28, 62, 114); page = Color.FromArgb(72, 255, 255, 255);
				btn1 = Color.FromArgb(115, 255, 255, 255); btn2 = Color.FromArgb(85, 255, 255, 255); ink = Color.White;
			}
			else
			{
				outer = Color.FromArgb(41, 32, 20); page = Color.FromArgb(87, 73, 42);
				btn1 = Color.FromArgb(139, 184, 48); btn2 = Color.FromArgb(228, 191, 48); ink = Color.FromArgb(238, 209, 143);
			}
			if (UiTheme.ExtremePerformanceMode) g.SmoothingMode = SmoothingMode.None;
			if (ThemeMode == UiTheme.ThemeLiquidGlass && !UiTheme.ExtremePerformanceMode)
			{
				using (var bg = new LinearGradientBrush(new Rectangle(1, 1, w, h), Color.FromArgb(32, 74, 134), Color.FromArgb(92, 54, 138), 35f))
					g.FillRectangle(bg, 1, 1, w, h);
			}
			else
			{
				using (var b = new SolidBrush(outer)) g.FillRectangle(b, 1, 1, w, h);
			}
			int pw = w - 12;
			using (var b = new SolidBrush(btn1)) g.FillRectangle(b, 6, 6, (pw - 4) / 2, 10);
			using (var b = new SolidBrush(btn2)) g.FillRectangle(b, 6 + (pw - 4) / 2 + 4, 6, (pw - 4) / 2, 10);
			using (var b = new SolidBrush(page)) g.FillRectangle(b, 6, 19, pw, Math.Max(8, h - 25));
			using (var b = new SolidBrush(ink))
			{
				g.FillRectangle(b, 11, 24, Math.Max(10, pw - 24), 3);
				g.FillRectangle(b, 11, 31, Math.Max(8, pw - 42), 3);
			}
			Color frame = selected ? Color.FromArgb(230, 184, 58) : UiTheme.Palette.BorderColor;
			using (var pen = new Pen(frame, selected ? 2.4f : 1.2f))
				g.DrawRectangle(pen, 1, 1, w - 1, h - 1);
		}
	}

	internal sealed class LayoutThumbnailPreview : Control
	{
		private bool selected;
		public int LayoutMode { get; private set; }
		public bool Selected
		{
			get { return selected; }
			set { if (selected != value) { selected = value; Invalidate(); } }
		}

		public LayoutThumbnailPreview(int mode)
		{
			LayoutMode = mode;
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			Cursor = Cursors.Hand;
			BackColor = Color.Transparent;
		}

		protected override void OnPaintBackground(PaintEventArgs e) { }

		protected override void OnPaint(PaintEventArgs e)
		{
			UiSurface.PaintWhole(this, e, PaintContent);
		}

		private void PaintContent(Graphics g)
		{
			UiSurface.PaintBackdrop(this, g);
			g.SmoothingMode = UiTheme.ExtremePerformanceMode ? SmoothingMode.None : SmoothingMode.AntiAlias;
			int w = Width - 2, h = Height - 2;
			if (w < 20 || h < 20) return;

			using (var bg = new SolidBrush(Color.FromArgb(52, 42, 24)))
				g.FillRectangle(bg, 1, 1, w, h);

			Color cardBg = Color.FromArgb(87, 73, 42);
			Color darkInset = Color.FromArgb(47, 38, 22);
			Color gold = Color.FromArgb(228, 191, 48);
			Color green = Color.FromArgb(148, 206, 56);
			Color orange = Color.FromArgb(222, 108, 45);
			Color stone = Color.FromArgb(128, 128, 120);
			Color paper = Color.FromArgb(238, 222, 152);

			int pad = 7;
			int innerW = w - pad * 2;
			int y = 6;

			Fill(g, darkInset, pad, y, innerW, 13);
			Fill(g, Color.FromArgb(165, 58, 36), pad + 14, y + 2, 48, 8);
			y += 16;

			if (LayoutMode == 0)
			{
				Fill(g, green, pad, y, (innerW - 6) / 2, 14);
				Fill(g, gold, pad + (innerW - 6) / 2 + 6, y, (innerW - 6) / 2, 14);
				y += 17;
				Fill(g, gold, pad + 12, y, 42, 8);
				Fill(g, stone, pad + innerW - 54, y, 42, 8);
				y += 11;
				Fill(g, cardBg, pad, y, innerW, 25);
				Fill(g, orange, pad + innerW - 44, y + 6, 38, 13);
				y += 28;
				Fill(g, cardBg, pad, y, innerW, 21);
				int bw = (innerW - 14) / 3;
				for (int i = 0; i < 3; i++) Fill(g, orange, pad + 3 + i * (bw + 4), y + 6, bw, 10);
				y += 24;
				Fill(g, cardBg, pad, y, innerW, 30);
				int pw = (innerW - 10) / 2;
				Fill(g, gold, pad + 3, y + 5, pw, 9);
				Fill(g, gold, pad + 7 + pw, y + 5, pw, 9);
				for (int i = 0; i < 3; i++) Fill(g, green, pad + 3 + i * (bw + 4), y + 17, bw, 9);
				y += 33;
				Fill(g, cardBg, pad, y, innerW, h - y - 5);
				Fill(g, darkInset, pad + 4, y + 14, innerW - 8, h - y - 22);
			}
			else if (LayoutMode == 1)
			{
				Fill(g, cardBg, pad, y, innerW, 20);
				Fill(g, green, pad + 6, y + 4, (innerW - 18) / 2, 12);
				Fill(g, gold, pad + (innerW - 18) / 2 + 12, y + 4, (innerW - 18) / 2, 12);
				y += 23;
				Fill(g, gold, pad + 4, y, 30, 7);
				Fill(g, stone, pad + 36, y, 30, 7);
				y += 7;
				Fill(g, cardBg, pad, y, innerW, 66);
				int bw = (innerW - 14) / 3;
				for (int i = 0; i < 3; i++) Fill(g, orange, pad + 3 + i * (bw + 4), y + 8, bw, 10);
				Fill(g, orange, pad + 3, y + 21, bw, 10);
				Fill(g, darkInset, pad + 3 + (bw + 4), y + 21, bw, 10);
				Fill(g, darkInset, pad + 3 + 2 * (bw + 4), y + 21, bw, 10);
				Fill(g, gold, pad + 3, y + 38, bw, 10);
				Fill(g, gold, pad + 3 + (bw + 4), y + 38, bw, 10);
				Fill(g, green, pad + 3 + 2 * (bw + 4), y + 38, bw, 10);
				Fill(g, green, pad + 3, y + 51, bw, 10);
				Fill(g, green, pad + 3 + (bw + 4), y + 51, bw, 10);
				y += 69;
				Fill(g, gold, pad + 4, y, 34, 7);
				Fill(g, stone, pad + 40, y, 34, 7);
				Fill(g, stone, pad + 76, y, 34, 7);
				y += 7;
				Fill(g, cardBg, pad, y, innerW, h - y - 5);
				Fill(g, darkInset, pad + 5, y + 14, innerW - 10, 22);
				Fill(g, darkInset, pad + 5, y + 40, innerW - 10, Math.Max(8, h - y - 48));
			}
			else if (LayoutMode == 2)
			{
				Fill(g, paper, pad, y, innerW, 34);
				Fill(g, green, pad + 24, y + 12, innerW - 44, 3);
				Fill(g, orange, pad + 24, y + 19, (innerW - 44) * 2 / 3, 3);
				Fill(g, gold, pad + 24, y + 26, (innerW - 44) / 2, 3);
				y += 37;
				Fill(g, green, pad + 6, y, (innerW - 18) / 2, 13);
				Fill(g, gold, pad + (innerW - 18) / 2 + 12, y, (innerW - 18) / 2, 13);
				y += 16;
				Fill(g, gold, pad + 4, y, 30, 7);
				Fill(g, stone, pad + 36, y, 30, 7);
				y += 7;
				Fill(g, cardBg, pad, y, innerW, 44);
				int cw = (innerW - 26) / 4;
				for (int i = 0; i < 4; i++) Fill(g, orange, pad + 18 + i * (cw + 2), y + 4, cw, 8);
				Fill(g, darkInset, pad + 18, y + 14, innerW - 22, 6);
				Fill(g, gold, pad + 18, y + 23, cw, 8);
				Fill(g, gold, pad + 20 + cw, y + 23, cw, 8);
				Fill(g, darkInset, pad + 22 + 2 * cw, y + 23, cw * 2 + 2, 8);
				for (int i = 0; i < 3; i++) Fill(g, green, pad + 18 + i * (cw + 2), y + 33, cw, 8);
				y += 47;
				Fill(g, gold, pad + 4, y, 38, 7);
				Fill(g, stone, pad + 44, y, 38, 7);
				y += 7;
				Fill(g, cardBg, pad, y, innerW, h - y - 5);
				Fill(g, darkInset, pad + 5, y + 14, innerW - 10, Math.Max(10, h - y - 22));
			}
			else
			{
				Fill(g, cardBg, pad, y, innerW, 18);
				Fill(g, green, pad + 6, y + 3, (innerW - 18) / 2, 12);
				Fill(g, gold, pad + (innerW - 18) / 2 + 12, y + 3, (innerW - 18) / 2, 12);
				y += 21;
				Fill(g, gold, pad + 4, y, 30, 7);
				Fill(g, stone, pad + 36, y, 30, 7);
				y += 7;
				Fill(g, cardBg, pad, y, innerW, 86);
				Color[] rowColors = { orange, orange, orange, orange, gold, gold, green, green, green };
				for (int i = 0; i < rowColors.Length; i++)
				{
					int ry = y + 4 + i * 9;
					Fill(g, darkInset, pad + 4, ry, innerW - 8, 7);
					Fill(g, rowColors[i], pad + 4, ry, 3, 7);
					Fill(g, rowColors[i], pad + innerW - 22, ry + 1, 16, 5);
				}
				y += 89;
				Fill(g, gold, pad + 4, y, 34, 7);
				Fill(g, stone, pad + 40, y, 34, 7);
				y += 7;
				Fill(g, cardBg, pad, y, innerW, Math.Max(18, h - y - 5));
				Fill(g, darkInset, pad + 5, y + 10, innerW - 10, Math.Max(6, h - y - 18));
			}

			Color frameColor = selected ? Color.FromArgb(245, 211, 108) : Color.FromArgb(123, 94, 48);
			using (var pen = new Pen(frameColor, selected ? 2.4f : 1.2f))
				g.DrawRectangle(pen, 1, 1, w - 1, h - 1);
		}

		private static void Fill(Graphics g, Color c, int x, int y, int w, int h)
		{
			if (w <= 0 || h <= 0) return;
			using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
		}
	}
}
