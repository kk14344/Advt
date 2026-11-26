using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class GameHeader : Panel
	{
		public string Title = "";
		public string Hint = "";
		public Color AccentColor = Color.FromArgb(238, 209, 143);
		private readonly Font titleFont;
		private readonly Font hintFont;

		public GameHeader()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
			BackColor = Color.Transparent;
			Height = 28;
			Margin = new Padding(0, 0, 0, 3);
			titleFont = UiTheme.GameFont(9f, FontStyle.Bold);
			hintFont = UiTheme.GameFont(7.5f, FontStyle.Regular);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				titleFont.Dispose();
				hintFont.Dispose();
			}
			base.Dispose(disposing);
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			Color color = UiTheme.Palette.CardColor;
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
			using (var brush = new SolidBrush(color)) e.Graphics.FillRectangle(brush, ClientRectangle);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			UiSurface.PaintWhole(this, e, PaintContent);
		}

		private void PaintContent(Graphics graphics)
		{
			UiSurface.PaintBackdrop(this, graphics);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle rectangle = new Rectangle(0, 0, base.Width, Math.Max(1, base.Height - 2));
			if (rectangle.Width <= 2 || rectangle.Height <= 2) return;

			if (UiTheme.CurrentTheme == UiTheme.ThemeParchment)
			{
				Rectangle ribbon = new Rectangle(0, 0, base.Width, Math.Max(1, base.Height - 1));
				Image banner = GameArt.Get("game-paper_bg7_d.png");
				if (banner != null) GameArt.DrawNinePatch(graphics, banner, ribbon, 10, 4, null);
				else using (var b = new SolidBrush(Color.FromArgb(245, 198, 148))) graphics.FillRectangle(b, ribbon);
				DrawTitleAndHint(graphics, new Rectangle(0, 1, ribbon.Width, ribbon.Height - 1), Color.FromArgb(61, 33, 8), Color.FromArgb(105, 59, 18), Color.Empty);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight)
			{
				using (var b = new SolidBrush(Color.FromArgb(247, 246, 242))) graphics.FillRectangle(b, rectangle);
				using (var p = new Pen(Color.FromArgb(224, 221, 213), 1f))
					graphics.DrawLine(p, 0, rectangle.Bottom, rectangle.Width, rectangle.Bottom);
				DrawTitleAndHint(graphics, rectangle, Color.FromArgb(31, 31, 31), Color.FromArgb(102, 98, 90), Color.Empty);
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				DrawTitleAndHint(graphics, rectangle, Color.White, Color.FromArgb(200, 255, 255, 255), Color.FromArgb(145, 0, 0, 0));
				return;
			}
			if (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood)
			{
				using (var b = new SolidBrush(Color.FromArgb(233, 201, 141))) graphics.FillRectangle(b, rectangle);
				using (var p = new Pen(Color.FromArgb(148, 93, 42), 2f))
					graphics.DrawRectangle(p, 1, 1, Math.Max(1, rectangle.Width - 2), Math.Max(1, rectangle.Height - 2));
				DrawTitleAndHint(graphics, rectangle, Color.FromArgb(71, 36, 10), Color.FromArgb(110, 70, 33), Color.Empty);
				return;
			}

			Image image = GameArt.Get("game-headerbar.png");
			if (image != null)
			{
				GameArt.DrawNinePatch(graphics, image, rectangle, 24);
			}
			else
			{
				using (LinearGradientBrush brush = new LinearGradientBrush(rectangle, Color.FromArgb(91, 71, 39), Color.FromArgb(49, 39, 24), 90f))
				{
					graphics.FillRectangle(brush, rectangle);
				}
			}
			using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(48, 18, 14, 8)))
			{
				graphics.FillRectangle(brush2, new Rectangle(8, 6, rectangle.Width - 16, rectangle.Height - 8));
			}
			using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(210, 232, 195, 123)))
			{
				Point[] diamond = new Point[4]
				{
					new Point(13, rectangle.Height / 2),
					new Point(17, rectangle.Height / 2 - 4),
					new Point(21, rectangle.Height / 2),
					new Point(17, rectangle.Height / 2 + 4)
				};
				graphics.FillPolygon(brush3, diamond);
			}
			DrawTitleAndHint(graphics, rectangle, AccentColor, Color.FromArgb(210, 189, 147), Color.FromArgb(170, 0, 0, 0));
			using (Pen pen = new Pen(Color.FromArgb(120, 29, 22, 12)))
			{
				graphics.DrawLine(pen, 6, rectangle.Bottom, rectangle.Width - 6, rectangle.Bottom);
			}
		}

		private void DrawTitleAndHint(Graphics graphics, Rectangle rectangle, Color titleColor, Color hintColor, Color shadowColor)
		{
			bool showHint = !string.IsNullOrEmpty(Hint) && rectangle.Width >= 280;
			int hintWidth = showHint ? TextRenderer.MeasureText(Hint, hintFont).Width + 12 : 0;
			int textX = UiTheme.CurrentTheme == UiTheme.ThemeClassic ? 28 : 12;
			Rectangle textBounds = new Rectangle(textX, 0, Math.Max(1, rectangle.Width - textX - 10 - hintWidth), rectangle.Height);
			TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
			if (shadowColor.A > 0)
				GameText.Draw(graphics, Title, titleFont, new Rectangle(textBounds.X + 1, textBounds.Y + 1, textBounds.Width, textBounds.Height), shadowColor, flags);
			GameText.Draw(graphics, Title, titleFont, textBounds, titleColor, flags);
			if (showHint)
			{
				Rectangle hintBounds = new Rectangle(rectangle.Width - hintWidth - 8, 0, hintWidth, rectangle.Height);
				GameText.Draw(graphics, Hint, hintFont, hintBounds, hintColor, flags | TextFormatFlags.Right);
			}
		}
	}
}
