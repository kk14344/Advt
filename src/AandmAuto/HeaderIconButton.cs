using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AandmAuto
{
	internal enum HeaderIconKind
	{
		Accounts,
		Reload,
		Settings
	}

	/// <summary>
	/// 侧栏顶部的像素风小图标按钮，不继承 GameButton 以保持正方形尺寸。
	/// </summary>
	internal sealed class HeaderIconButton : Control
	{
		private readonly HeaderIconKind kind;
		private bool hovered;
		private bool pressed;
		private float hoverT;
		private float pressT;
		private float sheenT;
		private bool sheenActive;
		private Func<float, bool> animStep;

		public HeaderIconButton(HeaderIconKind kind)
		{
			this.kind = kind;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
			DoubleBuffered = true;
			Cursor = Cursors.Hand;
			TabStop = true;
			Size = new Size(30, 30);
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
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.PixelOffsetMode = PixelOffsetMode.Half;

			if (Width < 8 || Height < 8) return;

			Rectangle face = new Rectangle(1, 1, Width - 3, Height - 4);
			if (pressed) face.Y += 1;

			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				Image bgSpr = GameArt.Get("game-buttonbg_12.png");
				if (bgSpr != null) GameArt.DrawNinePatch(g, bgSpr, face, 8, 5, null);
				else using (var b = new SolidBrush(Color.FromArgb(148, 142, 132))) g.FillRectangle(b, face);
				Image icSpr = GameArt.Get(kind == HeaderIconKind.Accounts ? "game-mainui_rune.png" : "game-mainui_setting.png");
				if (icSpr != null)
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					int s = Math.Min(18, Math.Min(face.Width - 6, face.Height - 6));
					g.DrawImage(icSpr, new Rectangle(face.X + (face.Width - s) / 2, face.Y + (face.Height - s) / 2, s, s));
				}
				else DrawGlyph(g, face, Color.FromArgb(43, 25, 3));
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				Color bg = pressed ? Color.FromArgb(232, 229, 222) : (hovered ? Color.FromArgb(245, 243, 238) : Color.White);
				using (GraphicsPath p = UiTheme.RoundedRect(face, 3))
				using (var b = new SolidBrush(bg))
				using (var pen = new Pen(Color.FromArgb(204, 200, 190), 1f))
				{
					g.FillPath(b, p);
					g.DrawPath(pen, p);
				}
				DrawGlyph(g, face, Color.FromArgb(51, 51, 51));
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				UiTheme.DrawLiquidGlassPill(
					g, this, face, face.Height / 2,
					Color.White, 0, Color.White,
					hovered, pressed, false, Enabled,
					hoverT, pressT, sheenActive ? sheenT : 0f, new Point(face.Width / 2, face.Height / 2));
				DrawGlyph(g, face, LiquidGlassEngine.GlassInk(Enabled));
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				Color bg = pressed ? Color.FromArgb(224, 188, 125) : (hovered ? Color.FromArgb(252, 229, 178) : Color.FromArgb(242, 211, 153));
				using (var b = new SolidBrush(bg)) g.FillRectangle(b, face);
				using (var p = new Pen(Color.FromArgb(41, 18, 4), 2f)) g.DrawRectangle(p, face);
				DrawGlyph(g, face, Color.FromArgb(59, 30, 10));
				return;
			}

			int radius = 5;
			using (GraphicsPath shadow = UiTheme.RoundedRect(new Rectangle(face.X, face.Y + 2, face.Width, face.Height), radius))
			using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(115, 14, 10, 5)))
			{
				g.FillPath(shadowBrush, shadow);
			}

			Color fill = !Enabled
				? Color.FromArgb(58, 49, 34)
				: pressed
					? Color.FromArgb(56, 43, 25)
					: hovered
						? Color.FromArgb(100, 78, 45)
						: Color.FromArgb(74, 58, 34);

			using (GraphicsPath path = UiTheme.RoundedRect(face, radius))
			{
				using (LinearGradientBrush bg = new LinearGradientBrush(
					face,
					UiTheme.Mix(fill, Color.White, 0.08f),
					UiTheme.Mix(fill, Color.Black, 0.14f),
					90f))
				{
					g.FillPath(bg, path);
				}
				using (Pen outer = new Pen(Color.FromArgb(190, 33, 24, 13), 1.2f))
				{
					g.DrawPath(outer, path);
				}
			}

			Rectangle inner = Rectangle.Inflate(face, -1, -1);
			if (inner.Width > 4 && inner.Height > 4)
			{
				using (GraphicsPath innerPath = UiTheme.RoundedRect(inner, Math.Max(2, radius - 1)))
				using (Pen innerPen = new Pen(Enabled ? Color.FromArgb(150, 152, 121, 66) : Color.FromArgb(80, 120, 102, 70), 1f))
				{
					g.DrawPath(innerPen, innerPath);
				}
			}

			Color iconColor = Enabled
				? (hovered ? Color.FromArgb(255, 229, 165) : Color.FromArgb(238, 209, 143))
				: Color.FromArgb(145, 134, 110);

			DrawGlyph(g, face, iconColor);

			if (Focused && ShowFocusCues)
			{
				Rectangle focus = Rectangle.Inflate(face, -2, -2);
				ControlPaint.DrawFocusRectangle(g, focus, Color.FromArgb(238, 210, 132), Color.Transparent);
			}
		}

		private void DrawGlyph(Graphics g, Rectangle face, Color color)
		{
			float cx = face.X + face.Width / 2f;
			float cy = face.Y + face.Height / 2f;

			if (kind == HeaderIconKind.Accounts)
			{
				using (SolidBrush brush = new SolidBrush(color))
				using (Pen pen = new Pen(color, 1.55f))
				{
					// 右后侧辅助人物轮廓
					Color backColor = UiTheme.Mix(color, Color.Black, 0.25f);
					using (SolidBrush backBrush = new SolidBrush(backColor))
					{
						g.FillEllipse(backBrush, cx + 1.5f, cy - 5.5f, 5.2f, 5.2f);
					}
					using (Pen backPen = new Pen(backColor, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
					{
						g.DrawArc(backPen, cx - 0.5f, cy + 1.0f, 9.2f, 6.6f, 195f, 150f);
					}
					// 前侧主人物轮廓
					g.FillEllipse(brush, cx - 5.8f, cy - 6.8f, 6.2f, 6.2f);
					pen.StartCap = LineCap.Round;
					pen.EndCap = LineCap.Round;
					g.DrawArc(pen, cx - 8.2f, cy + 0.8f, 11.0f, 7.6f, 185f, 170f);
				}
			}
			else if (kind == HeaderIconKind.Settings)
			{
				using (Pen ringPen = new Pen(color, 1.85f))
				using (Pen toothPen = new Pen(color, 2.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
				{
					float rOuter = 7.0f;
					float rInner = 4.6f;
					for (int i = 0; i < 8; i++)
					{
						double rad = i * Math.PI / 4.0;
						float x1 = cx + (float)(rInner * Math.Cos(rad));
						float y1 = cy + (float)(rInner * Math.Sin(rad));
						float x2 = cx + (float)(rOuter * Math.Cos(rad));
						float y2 = cy + (float)(rOuter * Math.Sin(rad));
						g.DrawLine(toothPen, x1, y1, x2, y2);
					}
					g.DrawEllipse(ringPen, cx - 4.4f, cy - 4.4f, 8.8f, 8.8f);
				}
			}
			else
			{
				using (Pen arcPen = new Pen(color, 2.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
				using (SolidBrush headBrush = new SolidBrush(color))
				{
					float r = 6.2f;
					RectangleF oval = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);
					g.DrawArc(arcPen, oval, -50f, 275f);

					// 顺时针末端三角形箭头（朝向 -50° 附近切线右下）
					double angle = -42.0 * Math.PI / 180.0;
					float tipX = cx + (float)(r * Math.Cos(angle)) + 1.6f;
					float tipY = cy + (float)(r * Math.Sin(angle)) + 1.4f;
					PointF[] arrow =
					{
						new PointF(tipX, tipY),
						new PointF(tipX - 5.4f, tipY - 0.6f),
						new PointF(tipX - 1.2f, tipY - 5.0f)
					};
					g.FillPolygon(headBrush, arrow);
				}
			}
		}

		private void StartGlassAnim()
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass) return;
			if (!GlassAnimator.Enabled)
			{
				hoverT = hovered ? 1f : 0f;
				pressT = pressed ? 1f : 0f;
				sheenActive = false;
				return;
			}
			if (animStep == null) animStep = StepGlassAnim;
			GlassAnimator.Add(animStep);
		}

		private bool StepGlassAnim(float dt)
		{
			if (IsDisposed || !Visible) return false;
			bool dirty = false;
			float th = hovered ? 1f : 0f;
			float tp = pressed ? 1f : 0f;
			if (Math.Abs(hoverT - th) > 0.005f)
			{
				hoverT += (th - hoverT) * Math.Min(1f, dt * 5.8f);
				if (Math.Abs(hoverT - th) <= 0.01f) hoverT = th;
				dirty = true;
			}
			if (Math.Abs(pressT - tp) > 0.005f)
			{
				pressT += (tp - pressT) * Math.Min(1f, dt * 12f);
				if (Math.Abs(pressT - tp) <= 0.01f) pressT = tp;
				dirty = true;
			}
			if (sheenActive)
			{
				sheenT += dt * 2.4f;
				if (sheenT >= 1f) { sheenT = 0f; sheenActive = false; }
				dirty = true;
			}
			if (dirty) Invalidate();
			return dirty;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && animStep != null) GlassAnimator.Remove(animStep);
			base.Dispose(disposing);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			hovered = true;
			StartGlassAnim();
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hovered = false;
			pressed = false;
			StartGlassAnim();
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				pressed = true;
				if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && GlassAnimator.Enabled)
				{
					sheenActive = true;
					sheenT = 0.02f;
				}
				Focus();
				StartGlassAnim();
				Invalidate();
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			pressed = false;
			StartGlassAnim();
			Invalidate();
			base.OnMouseUp(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
			{
				e.Handled = true;
				OnClick(EventArgs.Empty);
			}
			base.OnKeyDown(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			Invalidate();
			base.OnEnabledChanged(e);
		}

		protected override void OnGotFocus(EventArgs e)
		{
			Invalidate();
			base.OnGotFocus(e);
		}

		protected override void OnLostFocus(EventArgs e)
		{
			Invalidate();
			base.OnLostFocus(e);
		}
	}
}
