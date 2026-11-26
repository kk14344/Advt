using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class GameCheckBox : CheckBox
	{
		private bool hovered;

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
			int num = 14;
			int num2 = 2;
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
				using (GraphicsPath path = UiTheme.RoundedRect(rectangle, 4))
				{
					Color fill = Checked ? Color.FromArgb(84, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255);
					using (var bg = new SolidBrush(fill)) graphics.FillPath(bg, path);
					Color bColor = Checked ? Color.FromArgb(245, 255, 255, 255) : (hovered ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(165, 255, 255, 255));
					using (var p = new Pen(bColor, 1.15f)) graphics.DrawPath(p, path);
				}
				if (Checked) DrawCheckLines(graphics, num2, num3, Color.White);
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

			Rectangle bounds = new Rectangle(rectangle.Right + 7, 0, base.Width - rectangle.Right - 9, base.Height);
			Color foreColor = base.Enabled
				? ((UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? UiTheme.Palette.TextColor : ForeColor)
				: UiTheme.Palette.MutedColor;
			GameText.Draw(graphics, Text, Font, bounds, foreColor, UiSurface.TextFlags | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
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

		protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
	}
}
