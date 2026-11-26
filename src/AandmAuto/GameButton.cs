using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AandmAuto
{
	internal enum GameButtonSkin
	{
		Green,
		Gold,
		Orange,
		Stone,
		Violet
	}

	internal class GameButton : Button
	{
		private GameButtonSkin skin = GameButtonSkin.Green;
		private bool hovered;
		private bool pressed;
		private bool selected;
		private Font cachedLabelFont;
		private string cachedLabelText;
		private Size cachedLabelBox;
		private int cachedLabelTheme = -1;
		private string cachedLabelStyle;
		private bool cachedLabelExtreme;

		public GameButtonSkin Skin
		{
			get { return skin; }
			set { if (skin != value) { skin = value; Invalidate(); } }
		}

		public bool Selected
		{
			get { return selected; }
			set { selected = value; Invalidate(); }
		}

		public bool StretchWide { get; set; }

		public GameButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
			base.FlatStyle = FlatStyle.Flat;
			base.FlatAppearance.BorderSize = 0;
			base.UseCompatibleTextRendering = false;
			DoubleBuffered = true;
			Cursor = Cursors.Hand;
			TabStop = true;
			AutoSize = false;
			AutoSizeMode = AutoSizeMode.GrowOnly;
			MinimumSize = Size.Empty;
			BackColor = Color.Transparent;
			ForeColor = Color.Black;
			Font = UiTheme.GameFont(10.5f, FontStyle.Bold);
		}

		protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
		{
			if (!StretchWide && UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass && width > 0 && height > 0)
			{
				int unit = Math.Max(1, Math.Min(height, width / 3));
				int fitWidth = unit * 3, fitHeight = unit;
				x += (width - fitWidth) / 2; y += (height - fitHeight) / 2;
				width = fitWidth; height = fitHeight;
			}
			base.SetBoundsCore(x, y, width, height, specified);
		}

		internal Rectangle LabelBounds
		{
			get
			{
				if (UiTheme.ExtremePerformanceMode)
					return new Rectangle(4, 2, Math.Max(1, Width - 8), Math.Max(1, Height - 4));
				if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
					return new Rectangle(6, 2, Math.Max(1, Width - 12), Math.Max(1, Height - 4));
				return new Rectangle(7, 4, Math.Max(1, Width - 14), Math.Max(1, Height - 9));
			}
		}

		internal Font LabelFont()
		{
			Rectangle box = LabelBounds;
			float size = LabelBasePoints;
			Font result = null;
			while (true)
			{
				if (result != null) result.Dispose();
				result = new Font(Font.FontFamily, size, FontStyle.Regular, GraphicsUnit.Point);
				Size measure = TextRenderer.MeasureText(Text, result, new Size(10000, 10000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
				if ((measure.Width <= box.Width && measure.Height <= box.Height) || size <= 6f) return result;
				size -= 0.25f;
			}
		}

		internal float LabelBasePoints
		{
			get
			{
				if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
				{
					if (Height >= 46) return 11.5f;
					if (Height >= 34) return 9.75f;
					return 9f;
				}
				if (Height < 64) return 10.5f;
				if (UiTheme.ButtonLabelStyle == "B") return 13.5f;
				if (UiTheme.ButtonLabelStyle == "C") return 10.5f * Height / 52f;
				return 12f;
			}
		}

		private string TextureName(Rectangle target)
		{
			bool compact = target.Width <= 150;
			switch (skin)
			{
			case GameButtonSkin.Gold:
				return compact ? "game-buttonbg_5.png" : "game-buttonbg_1.png";
			case GameButtonSkin.Orange:
				return compact ? "game-buttonbg_6.png" : "game-buttonbg_2.png";
			case GameButtonSkin.Green:
				return compact ? "game-buttonbg_4.png" : "game-buttonbg_3.png";
			case GameButtonSkin.Stone:
			case GameButtonSkin.Violet:
				return "game-buttonbg_12.png";
			default:
				return compact ? "game-buttonbg_4.png" : "game-buttonbg_3.png";
			}
		}

		protected override void OnPaintBackground(PaintEventArgs e) { }

		protected override void OnPaint(PaintEventArgs e)
		{
			UiSurface.PaintWhole(this, e, PaintContent);
		}

		private void PaintNativeLight(Graphics graphics, Rectangle target)
		{
			Color fill, border, ink;
			if (!Enabled)
			{
				fill = Color.FromArgb(240, 239, 234);
				border = Color.FromArgb(214, 211, 202);
				ink = Color.FromArgb(153, 149, 140);
			}
			else if (skin == GameButtonSkin.Green || (selected && skin == GameButtonSkin.Gold))
			{
				fill = pressed ? Color.FromArgb(34, 88, 58) : (hovered ? Color.FromArgb(53, 129, 87) : Color.FromArgb(44, 110, 73));
				border = Color.FromArgb(34, 88, 58);
				ink = Color.White;
			}
			else if (skin == GameButtonSkin.Gold)
			{
				fill = pressed ? Color.FromArgb(245, 232, 198) : (hovered ? Color.FromArgb(255, 249, 230) : Color.FromArgb(253, 246, 227));
				border = Color.FromArgb(199, 163, 82);
				ink = Color.FromArgb(31, 31, 31);
			}
			else if (skin == GameButtonSkin.Orange)
			{
				fill = pressed ? Color.FromArgb(247, 236, 224) : (hovered ? Color.FromArgb(255, 248, 240) : Color.FromArgb(255, 255, 255));
				border = Color.FromArgb(212, 180, 149);
				ink = Color.FromArgb(34, 34, 34);
			}
			else
			{
				fill = pressed ? Color.FromArgb(232, 229, 222) : (hovered ? Color.FromArgb(239, 236, 230) : Color.FromArgb(248, 247, 244));
				border = Color.FromArgb(201, 197, 188);
				ink = Color.FromArgb(34, 34, 34);
			}
			Rectangle rect = new Rectangle(target.X, target.Y, Math.Max(2, target.Width - 1), Math.Max(2, target.Height));
			using (GraphicsPath path = UiTheme.RoundedRect(rect, 3))
			using (var b = new SolidBrush(fill))
			using (var p = new Pen(border, 1f))
			{
				graphics.FillPath(b, path);
				graphics.DrawPath(p, path);
			}
			DrawLabelColored(graphics, ink, Color.Empty);
		}

		private void PaintWarmWood(Graphics graphics, Rectangle target)
		{
			Color face, hi, lo, ink, shadowInk;
			if (!Enabled)
			{
				face = Color.FromArgb(205, 182, 142);
				hi = Color.FromArgb(228, 208, 172);
				lo = Color.FromArgb(165, 142, 104);
				ink = Color.FromArgb(118, 92, 58);
				shadowInk = Color.Empty;
			}
			else
			{
				switch (skin)
				{
				case GameButtonSkin.Green:
					face = Color.FromArgb(104, 176, 59); hi = Color.FromArgb(155, 224, 106); lo = Color.FromArgb(63, 120, 28);
					ink = Color.White; shadowInk = Color.FromArgb(38, 77, 15); break;
				case GameButtonSkin.Gold:
					face = Color.FromArgb(234, 179, 56); hi = Color.FromArgb(255, 224, 125); lo = Color.FromArgb(176, 121, 19);
					ink = Color.FromArgb(51, 28, 2); shadowInk = Color.Empty; break;
				case GameButtonSkin.Orange:
					face = Color.FromArgb(217, 119, 50); hi = Color.FromArgb(247, 166, 99); lo = Color.FromArgb(150, 69, 18);
					ink = Color.FromArgb(255, 248, 237); shadowInk = Color.FromArgb(102, 41, 5); break;
				default:
					face = Color.FromArgb(242, 211, 153); hi = Color.FromArgb(255, 245, 217); lo = Color.FromArgb(190, 148, 83);
					ink = Color.FromArgb(59, 30, 10); shadowInk = Color.Empty; break;
				}
				if (hovered && !pressed) face = UiTheme.Mix(face, Color.White, 0.12f);
				if (pressed) { Color tmp = hi; hi = lo; lo = tmp; }
			}
			Rectangle rect = new Rectangle(target.X, target.Y, Math.Max(4, target.Width - 1), Math.Max(4, target.Height));
			using (var b = new SolidBrush(face)) graphics.FillRectangle(b, rect);
			using (var hiPen = new Pen(hi, 2f))
			{
				graphics.DrawLine(hiPen, rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.Top + 2);
				graphics.DrawLine(hiPen, rect.Left + 2, rect.Top + 2, rect.Left + 2, rect.Bottom - 2);
			}
			using (var loPen = new Pen(lo, 2f))
			{
				graphics.DrawLine(loPen, rect.Left + 2, rect.Bottom - 2, rect.Right - 2, rect.Bottom - 2);
				graphics.DrawLine(loPen, rect.Right - 2, rect.Top + 2, rect.Right - 2, rect.Bottom - 2);
			}
			using (var border = new Pen(Color.FromArgb(41, 18, 4), 2f))
				graphics.DrawRectangle(border, rect);
			if (selected)
			{
				using (var sel = new Pen(Color.FromArgb(255, 246, 200), 1.5f))
					graphics.DrawLine(sel, rect.Left + 6, rect.Bottom - 4, rect.Right - 6, rect.Bottom - 4);
			}
			DrawLabelColored(graphics, ink, shadowInk);
		}

		private void PaintLiquidGlass(Graphics graphics, Rectangle target)
		{
			bool activeState = selected || (!string.IsNullOrEmpty(Text) && (Text.StartsWith("■") || Text.StartsWith("停止")));
			Color ink = Enabled ? Color.White : Color.FromArgb(170, 255, 255, 255);
			int rad = Math.Min(target.Height / 2, 16);
			Rectangle rect = new Rectangle(target.X, target.Y, Math.Max(4, target.Width - 1), Math.Max(4, target.Height));
			UiTheme.DrawLiquidGlassPill(graphics, this, rect, rad, Color.White, activeState ? 42 : 16, Color.White, hovered, pressed, activeState, Enabled);
			DrawLabelColored(graphics, ink, Color.FromArgb(165, 0, 0, 0));
		}

		private void PaintExtremePerf(Graphics graphics, Rectangle target)
		{
			Color fill, border, ink;
			if (!Enabled)
			{
				fill = Color.FromArgb(240, 239, 234);
				border = Color.FromArgb(214, 211, 202);
				ink = Color.FromArgb(153, 149, 140);
			}
			else if (skin == GameButtonSkin.Green || (selected && skin == GameButtonSkin.Gold))
			{
				fill = pressed ? Color.FromArgb(34, 88, 58) : (hovered ? Color.FromArgb(53, 129, 87) : Color.FromArgb(44, 110, 73));
				border = Color.FromArgb(34, 88, 58);
				ink = Color.White;
			}
			else if (skin == GameButtonSkin.Gold)
			{
				fill = pressed ? Color.FromArgb(245, 232, 198) : (hovered ? Color.FromArgb(255, 249, 230) : Color.FromArgb(253, 246, 227));
				border = Color.FromArgb(199, 163, 82);
				ink = Color.FromArgb(31, 31, 31);
			}
			else if (skin == GameButtonSkin.Orange)
			{
				fill = pressed ? Color.FromArgb(247, 236, 224) : (hovered ? Color.FromArgb(255, 248, 240) : Color.FromArgb(255, 255, 255));
				border = Color.FromArgb(212, 180, 149);
				ink = Color.FromArgb(34, 34, 34);
			}
			else
			{
				fill = pressed ? Color.FromArgb(232, 229, 222) : (hovered ? Color.FromArgb(239, 236, 230) : Color.FromArgb(248, 247, 244));
				border = selected ? Color.FromArgb(44, 110, 73) : Color.FromArgb(201, 197, 188);
				ink = Color.FromArgb(34, 34, 34);
			}
			int rw = Math.Max(2, target.Width - 1);
			int rh = Math.Max(2, target.Height);
			using (var b = new SolidBrush(fill))
				graphics.FillRectangle(b, target.X, target.Y, rw, rh);
			using (var p = new Pen(border, 1f))
				graphics.DrawRectangle(p, target.X, target.Y, rw - 1, rh - 1);
			if (selected && Enabled && skin != GameButtonSkin.Green && skin != GameButtonSkin.Gold)
			{
				using (var acc = new SolidBrush(Color.FromArgb(44, 110, 73)))
					graphics.FillRectangle(acc, target.X + 1, target.Y + rh - 3, Math.Max(1, rw - 2), 2);
			}
			DrawLabelColored(graphics, ink, Color.Empty);
		}

		private void PaintContent(Graphics graphics)
		{
			UiSurface.PaintBackdrop(this, graphics);
			Rectangle rectangle = new Rectangle(0, 0, base.Width, base.Height);
			if (rectangle.Width <= 2 || rectangle.Height <= 2) return;
			Rectangle target = new Rectangle(1, 1, Math.Max(1, rectangle.Width - 2), Math.Max(1, rectangle.Height - 3));
			if (pressed)
			{
				target.Y += 1;
				target.Height = Math.Max(1, target.Height - 1);
			}

			if (UiTheme.ExtremePerformanceMode)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				PaintExtremePerf(graphics, target);
				return;
			}

			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				PaintNativeLight(graphics, target);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				PaintWarmWood(graphics, target);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				PaintLiquidGlass(graphics, target);
				return;
			}

			Image image = GameArt.Get(TextureName(target));
			int radius = 6;
			int sourceRadius = 12;
			if (image != null)
			{
				sourceRadius = GameArt.CornerRadius(image);
				radius = (int)Math.Round((double)sourceRadius * (double)target.Height / (double)image.Height);
			}
			radius = Math.Max(3, Math.Min(radius, Math.Min(target.Width / 2, target.Height / 2)));
			using (GraphicsPath shadow = UiTheme.RoundedRect(new Rectangle(target.X, target.Y + 2, target.Width, target.Height), radius + 1))
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 19, 13, 6)))
			{
				graphics.FillPath(brush, shadow);
			}
			if (image != null)
			{
				ImageAttributes attributes = null;
				GraphicsState state = graphics.Save();
				try
				{
					using (GraphicsPath clip = UiTheme.RoundedRect(target, radius))
					{
						graphics.SetClip(clip, CombineMode.Intersect);
					}
					if (!base.Enabled) attributes = GameArt.Grayscale(0.55f);
					else if (pressed) attributes = GameArt.Overlay(Color.Black, 0.18f);
					else if (hovered) attributes = GameArt.Overlay(Color.White, 0.10f);
					else if (skin == GameButtonSkin.Violet) attributes = TintViolet();
					GameArt.DrawNinePatch(graphics, image, target, sourceRadius, radius, attributes);
				}
				finally
				{
					graphics.Restore(state);
					if (attributes != null) attributes.Dispose();
				}
			}
			else
			{
				using (GraphicsPath shape = UiTheme.RoundedRect(target, radius))
				using (LinearGradientBrush brush = new LinearGradientBrush(target, SkinColor(1.12f), SkinColor(0.82f), 90f))
				{
					graphics.FillPath(brush, shape);
				}
			}
			using (GraphicsPath shape = UiTheme.RoundedRect(target, radius))
			using (Pen pen = new Pen(Enabled ? Color.FromArgb(190, 47, 35, 19) : Color.FromArgb(120, 77, 68, 51), 1.2f))
			{
				graphics.DrawPath(pen, shape);
			}
			if (selected)
			{
				using (Pen pen = new Pen(Color.FromArgb(210, 255, 226, 142), 1.4f))
				{
					graphics.DrawLine(pen, target.Left + 8, target.Bottom - 3, target.Right - 8, target.Bottom - 3);
				}
			}
			DrawLabelColored(graphics, Color.Black, Color.Empty);
			if (Focused && ShowFocusCues)
			{
				Rectangle focus = new Rectangle(target.Left + 4, target.Top + 4, Math.Max(1, target.Width - 8), Math.Max(1, target.Height - 8));
				ControlPaint.DrawFocusRectangle(graphics, focus, Color.FromArgb(238, 210, 132), Color.Transparent);
			}
		}

		private Color SkinColor(float amount)
		{
			Color color;
			switch (skin)
			{
			case GameButtonSkin.Gold: color = Color.FromArgb(205, 174, 45); break;
			case GameButtonSkin.Orange: color = Color.FromArgb(190, 96, 35); break;
			case GameButtonSkin.Stone: color = Color.FromArgb(117, 117, 111); break;
			case GameButtonSkin.Violet: color = Color.FromArgb(126, 91, 185); break;
			default: color = Color.FromArgb(139, 184, 48); break;
			}
			return UiTheme.Mix(color, amount > 1f ? Color.White : Color.Black, Math.Abs(amount - 1f));
		}

		private static ImageAttributes TintViolet()
		{
			ColorMatrix matrix = new ColorMatrix(new float[][]
			{
				new float[] { 0.72f, 0.08f, 0.22f, 0f, 0f },
				new float[] { 0.16f, 0.55f, 0.34f, 0f, 0f },
				new float[] { 0.22f, 0.10f, 0.82f, 0f, 0f },
				new float[] { 0f, 0f, 0f, 1f, 0f },
				new float[] { 0.03f, 0.02f, 0.06f, 0f, 1f }
			});
			ImageAttributes attributes = new ImageAttributes();
			attributes.SetColorMatrix(matrix);
			attributes.SetColorKey(Color.Transparent, Color.Transparent, ColorAdjustType.Default);
			return attributes;
		}

		private Font GetCachedLabelFont()
		{
			Size box = LabelBounds.Size;
			string text = Text ?? "";
			string style = UiTheme.ButtonLabelStyle ?? "";
			int theme = UiTheme.CurrentTheme;
			bool extreme = UiTheme.ExtremePerformanceMode;
			if (cachedLabelFont != null && cachedLabelText == text && cachedLabelBox == box
				&& cachedLabelTheme == theme && cachedLabelStyle == style && cachedLabelExtreme == extreme)
				return cachedLabelFont;
			if (cachedLabelFont != null) cachedLabelFont.Dispose();
			cachedLabelFont = LabelFont();
			cachedLabelText = text;
			cachedLabelBox = box;
			cachedLabelTheme = theme;
			cachedLabelStyle = style;
			cachedLabelExtreme = extreme;
			return cachedLabelFont;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && cachedLabelFont != null)
			{
				cachedLabelFont.Dispose();
				cachedLabelFont = null;
			}
			base.Dispose(disposing);
		}

		private void DrawLabelColored(Graphics graphics, Color ink, Color shadowInk)
		{
			Rectangle bounds = LabelBounds;
			TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
				TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter;
			if (pressed) bounds.Y += 1;
			Font font = GetCachedLabelFont();
			if (shadowInk.A > 0 && !UiTheme.ExtremePerformanceMode)
				GameText.Draw(graphics, Text, font, new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width, bounds.Height), shadowInk, flags);
			GameText.Draw(graphics, Text, font, bounds, ink, flags);
		}

		protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
		protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
		protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
		protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
		protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
	}
}
