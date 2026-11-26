using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class GameCheckBox : CheckBox
	{
        private bool hovered;
        private float checkT;
        private float hoverT;
        private Func<float, bool> animStep;
        private const int MarkWidth = 14;
        private const int MarkX = 2;
        internal Rectangle LabelBounds
        {
            get { return new Rectangle(MarkX + MarkWidth + 7, 0, Math.Max(0, Width - MarkX - MarkWidth - 9), Height); }
        }

        internal Color? LabelColorOverride { get; set; }

		public GameCheckBox()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			base.FlatStyle = FlatStyle.Flat;
			Cursor = Cursors.Hand;
			base.UseCompatibleTextRendering = true;
			BackColor = Color.Transparent;
		}

		protected override void OnPaintBackground(PaintEventArgs e) { }

		protected override void OnPaint(PaintEventArgs e)
		{
			UiSurface.PaintWhole(this, e, PaintContent);
		}

		private void PaintContent(Graphics graphics)
		{
			UiSurface.PaintBackdrop(this, graphics);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			int num = MarkWidth;
			int num2 = MarkX;
			int num3 = Math.Max(1, (base.Height - num) / 2);
			Rectangle rectangle = new Rectangle(num2, num3, num, num);

			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				using (var bg = new SolidBrush(Color.FromArgb(255, 247, 224))) graphics.FillRectangle(bg, rectangle);
				using (var bdr = new Pen(Color.FromArgb(117, 85, 40), 1.8f)) graphics.DrawRectangle(bdr, rectangle);
				if (Checked)
				{
					Image yes = GameArt.Get("game-decide_yes.png");
					if (yes != null)
					{
						graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
						graphics.DrawImage(yes, new Rectangle(rectangle.X + 1, rectangle.Y + 1, rectangle.Width - 2, rectangle.Height - 2));
					}
					else DrawCheckLines(graphics, num2, num3, Color.FromArgb(43, 110, 18));
				}
			}
			else if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				using (GraphicsPath path = UiTheme.RoundedRect(rectangle, 2))
				{
					using (var bg = new SolidBrush(Checked ? Color.FromArgb(44, 110, 73) : Color.White))
						graphics.FillPath(bg, path);
					Color bColor = Checked ? Color.FromArgb(44, 110, 73) : (hovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(158, 153, 142));
					using (var p = new Pen(bColor, 1f))
						graphics.DrawPath(p, path);
				}
				if (Checked) DrawCheckLines(graphics, num2, num3, Color.White);
			}
			else if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				bool bright = LiquidGlassEngine.IsBrightBackdrop();
				float ct = GlassAnimator.Enabled ? checkT : (Checked ? 1f : 0f);
				float ht = GlassAnimator.Enabled ? hoverT : (hovered ? 1f : 0f);
				Rectangle box = new Rectangle(num2, Math.Max(1, (base.Height - 15) / 2), 15, 15);
				using (GraphicsPath path = UiTheme.RoundedRect(box, 4))
				{
					Color uncheckedFill = bright ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(24, 255, 255, 255);
					Color checkedFill = Color.FromArgb(215, 14, 132, 255);
					int r = (int)(uncheckedFill.R + (checkedFill.R - uncheckedFill.R) * ct);
					int gC = (int)(uncheckedFill.G + (checkedFill.G - uncheckedFill.G) * ct);
					int b = (int)(uncheckedFill.B + (checkedFill.B - uncheckedFill.B) * ct);
					int a = (int)(uncheckedFill.A + (checkedFill.A - uncheckedFill.A) * ct + ht * 24f);
					using (var bg = new SolidBrush(Color.FromArgb(Math.Min(255, a), r, gC, b)))
						graphics.FillPath(bg, path);

					Color bColor = ct > 0.5f
						? Color.FromArgb(220, 180, 224, 255)
						: (bright
							? Color.FromArgb((int)(110 + ht * 75), 24, 38, 60)
							: Color.FromArgb((int)(155 + ht * 85), 255, 255, 255));
					using (var p = new Pen(bColor, 1.15f))
						graphics.DrawPath(p, path);
				}
				if (ct > 0.12f)
				{
					using (Pen pen = new Pen(Color.FromArgb((int)(Math.Min(1f, ct) * 255f), Color.White), 1.85f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
					{
						graphics.DrawLines(pen, new PointF[3]
						{
							new PointF(box.X + 3.4f, box.Y + 7.6f),
							new PointF(box.X + 6.4f, box.Y + 10.6f),
							new PointF(box.X + 11.6f, box.Y + 4.4f)
						});
					}
				}
			}
			else if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				using (var bg = new SolidBrush(Color.FromArgb(255, 247, 226))) graphics.FillRectangle(bg, rectangle);
				using (var bdr = new Pen(Color.FromArgb(89, 46, 16), 2f)) graphics.DrawRectangle(bdr, rectangle);
				if (Checked) DrawCheckLines(graphics, num2, num3, Color.FromArgb(43, 122, 22));
			}
			else
			{
				using (GraphicsPath path = UiTheme.RoundedRect(rectangle, 4))
				{
					if (base.Checked)
					{
						using (LinearGradientBrush brush = new LinearGradientBrush(rectangle, Color.FromArgb(137, 163, 78), Color.FromArgb(69, 93, 42), 90f))
							graphics.FillPath(brush, path);
						DrawCheckLines(graphics, num2, num3, Color.FromArgb(255, 247, 205));
					}
					else
					{
						using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(10, 18, 13)))
							graphics.FillPath(brush2, path);
					}
					Color color = (hovered ? Color.FromArgb(225, 190, 109) : (base.Checked ? Color.FromArgb(180, 155, 82) : Color.FromArgb(83, 83, 55)));
					using (Pen pen2 = new Pen(color, 1f))
						graphics.DrawPath(pen2, path);
				}
			}

			Color foreColor;
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				foreColor = base.Enabled ? (LabelColorOverride ?? LiquidGlassEngine.GlassInk(true)) : LiquidGlassEngine.GlassInk(false);
				Color shadow = LiquidGlassEngine.GlassTextShadow();
				TextFormatFlags lf = UiSurface.TextFlags | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
				if (shadow.A > 0 && !UiTheme.ExtremePerformanceMode)
					GameText.Draw(graphics, Text, Font, new Rectangle(LabelBounds.X + 1, LabelBounds.Y + 1, LabelBounds.Width, LabelBounds.Height), shadow, lf);
				GameText.Draw(graphics, Text, Font, LabelBounds, foreColor, lf);
			}
			else
			{
				foreColor = base.Enabled
					? (LabelColorOverride ?? (UiTheme.IsLightTheme ? UiTheme.Palette.TextColor : ForeColor))
					: UiTheme.Palette.MutedColor;
				GameText.Draw(graphics, Text, Font, LabelBounds, foreColor, UiSurface.TextFlags | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
			if (Focused)
			{
				Rectangle rectangle2 = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
				ControlPaint.DrawFocusRectangle(graphics, rectangle2, Color.FromArgb(180, 205, 150), BackColor);
			}
		}

		private static void DrawCheckLines(Graphics graphics, int x, int y, Color color)
		{
			using (Pen pen = new Pen(color, 1.8f))
			{
				graphics.DrawLines(pen, new Point[3]
				{
					new Point(x + 3, y + 7),
					new Point(x + 6, y + 10),
					new Point(x + 11, y + 4)
				});
			}
		}

		private void StartGlassAnim()
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass) return;
			if (!GlassAnimator.Enabled)
			{
				checkT = Checked ? 1f : 0f;
				hoverT = hovered ? 1f : 0f;
				return;
			}
			if (animStep == null) animStep = StepGlassAnim;
			GlassAnimator.Add(animStep);
		}

		private bool StepGlassAnim(float dt)
		{
			if (IsDisposed || !Visible) return false;
			bool dirty = false;
			float tc = Checked ? 1f : 0f;
			float th = hovered ? 1f : 0f;
			if (Math.Abs(checkT - tc) > 0.005f)
			{
				checkT += (tc - checkT) * Math.Min(1f, dt * 11f);
				if (Math.Abs(checkT - tc) <= 0.01f) checkT = tc;
				dirty = true;
			}
			if (Math.Abs(hoverT - th) > 0.005f)
			{
				hoverT += (th - hoverT) * Math.Min(1f, dt * 7f);
				if (Math.Abs(hoverT - th) <= 0.01f) hoverT = th;
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

		protected override void OnCheckedChanged(EventArgs e)
		{
			StartGlassAnim();
			Invalidate();
			base.OnCheckedChanged(e);
		}

		protected override void OnMouseEnter(EventArgs e) { hovered = true; StartGlassAnim(); Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hovered = false; StartGlassAnim(); Invalidate(); base.OnMouseLeave(e); }
		protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
	}
}
