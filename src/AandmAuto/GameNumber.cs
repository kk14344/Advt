using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class GameNumber : Control
	{
		private int minimum;

		private int maximum = 99;

		private int value;

		private bool hovered;

		private bool editing;

		private bool editFresh;

		private string editBuffer = string.Empty;

		public event EventHandler ValueChanged;

		public GameNumber()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
			ForeColor = Color.FromArgb(242, 232, 205);
			Font = UiTheme.GameFont(9f, FontStyle.Bold);
			Cursor = Cursors.Hand;
			TabStop = true;
			Size = new Size(62, 24);
		}

		public int Minimum
		{
			get { return minimum; }
			set
			{
				minimum = Math.Max(0, value);
				if (maximum < minimum)
				{
					maximum = minimum;
				}
				Value = this.value;
			}
		}

		public int Maximum
		{
			get { return maximum; }
			set
			{
				maximum = Math.Max(minimum, value);
				Value = this.value;
			}
		}

		public int Value
		{
			get { return value; }
			set
			{
				int num = Math.Max(minimum, Math.Min(maximum, value));
				if (num != this.value)
				{
					this.value = num;
					Invalidate();
					OnValueChanged();
				}
			}
		}

		private void OnValueChanged()
		{
			EventHandler handler = ValueChanged;
			if (handler != null)
			{
				handler(this, EventArgs.Empty);
			}
		}

		private void Step(int delta)
		{
			editing = false;
			editFresh = false;
			editBuffer = string.Empty;
			Value = value + delta;
		}

        protected override void OnPaintBackground(PaintEventArgs e) { }

		protected override void OnPaint(PaintEventArgs e)
        {
            UiSurface.PaintWhole(this,e,PaintContent);
        }

        private void PaintContent(Graphics graphics)
		{
            UiSurface.PaintBackdrop(this,graphics);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				UiTheme.DrawLiquidGlassPill(graphics, this, bounds, bounds.Height / 2, Color.White, 18, Color.White, hovered, false, Focused, base.Enabled);
				int aLeft = base.Width - 20;
				using (var ab = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
				{
					int cx = aLeft + 10;
					graphics.FillPolygon(ab, new Point[] { new Point(cx - 4, base.Height / 2 - 5), new Point(cx + 4, base.Height / 2 - 5), new Point(cx, base.Height / 2 - 2) });
					graphics.FillPolygon(ab, new Point[] { new Point(cx - 4, base.Height / 2 + 5), new Point(cx + 4, base.Height / 2 + 5), new Point(cx, base.Height / 2 + 2) });
				}
				string t = editing ? editBuffer : value.ToString();
				if (string.IsNullOrEmpty(t)) t = "0";
				Rectangle tb = new Rectangle(8, 1, Math.Max(1, aLeft - 8), Math.Max(1, base.Height - 2));
				GameText.Draw(graphics, t, Font, tb, base.Enabled ? Color.White : Color.FromArgb(170, 255, 255, 255),
					UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
				return;
			}
			if (UiTheme.IsLightTheme)
			{
				Color bgTop = UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? Color.White : Color.FromArgb(255, 248, 226);
				Color bdr = (Focused || hovered) ? UiTheme.Palette.AccentColor : UiTheme.Palette.BorderColor;
				Color arrowInk = UiTheme.Palette.TextColor;
				int rad = UiTheme.CurrentTheme == UiTheme.ThemeWarmWood ? 0 : 3;
				if (rad == 0)
				{
					using (var b = new SolidBrush(bgTop)) graphics.FillRectangle(b, bounds);
					using (var p = new Pen(bdr, 1.5f)) graphics.DrawRectangle(p, bounds);
				}
				else
				{
					using (GraphicsPath path = UiTheme.RoundedRect(bounds, rad))
					using (var b = new SolidBrush(bgTop))
					using (var p = new Pen(bdr, 1f))
					{
						graphics.FillPath(b, path);
						graphics.DrawPath(p, path);
					}
				}
				int aLeft = base.Width - 20;
				using (var pen = new Pen(UiTheme.Palette.BorderColor, 1f))
					graphics.DrawLine(pen, aLeft, 3, aLeft, base.Height - 3);
				using (var ab = new SolidBrush(arrowInk))
				{
					int cx = aLeft + 10;
					graphics.FillPolygon(ab, new Point[] { new Point(cx - 4, base.Height / 2 - 5), new Point(cx + 4, base.Height / 2 - 5), new Point(cx, base.Height / 2 - 2) });
					graphics.FillPolygon(ab, new Point[] { new Point(cx - 4, base.Height / 2 + 5), new Point(cx + 4, base.Height / 2 + 5), new Point(cx, base.Height / 2 + 2) });
				}
				string t = editing ? editBuffer : value.ToString();
				if (string.IsNullOrEmpty(t)) t = "0";
				Rectangle tb = new Rectangle(7, 0, Math.Max(1, aLeft - 10), base.Height);
				GameText.Draw(graphics, t, Font, tb, base.Enabled ? UiTheme.Palette.TextColor : UiTheme.Palette.MutedColor,
					UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
				return;
			}

			int radius = Math.Max(3, Math.Min(6, bounds.Height / 2));
			using (GraphicsPath path = UiTheme.RoundedRect(bounds, radius))
			{
				using (LinearGradientBrush brush = new LinearGradientBrush(bounds,
					Color.FromArgb(58, 47, 29), Color.FromArgb(39, 31, 20), 90f))
				{
					graphics.FillPath(brush, path);
				}
				Color border = (Focused || hovered) ? Color.FromArgb(214, 181, 104) : Color.FromArgb(124, 101, 62);
				using (Pen pen = new Pen(border, 1.2f))
				{
					graphics.DrawPath(pen, path);
				}
			}
			int arrowLeft = base.Width - 20;
			using (Pen pen = new Pen(Color.FromArgb(75, 238, 209, 143), 1f))
			{
				graphics.DrawLine(pen, arrowLeft, 4, arrowLeft, base.Height - 4);
			}
			using (SolidBrush brush = new SolidBrush((Focused || hovered) ? Color.FromArgb(248, 221, 158) : Color.FromArgb(194, 172, 125)))
			{
				int cx = arrowLeft + 10;
				graphics.FillPolygon(brush, new Point[]
				{
					new Point(cx - 4, base.Height / 2 - 6),
					new Point(cx + 4, base.Height / 2 - 6),
					new Point(cx, base.Height / 2 - 2)
				});
				graphics.FillPolygon(brush, new Point[]
				{
					new Point(cx - 4, base.Height / 2 + 6),
					new Point(cx + 4, base.Height / 2 + 6),
					new Point(cx, base.Height / 2 + 2)
				});
			}
			string text = editing ? editBuffer : value.ToString();
			if (string.IsNullOrEmpty(text))
			{
				text = "0";
			}
			Rectangle textBounds = new Rectangle(7, 0, Math.Max(1, arrowLeft - 10), base.Height);
			GameText.Draw(graphics, text, Font, textBounds,
				base.Enabled ? ForeColor : Color.FromArgb(145, 139, 120),
				UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			hovered = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hovered = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			bool overArrow = e.X >= base.Width - 20;
			Cursor = overArrow ? Cursors.Hand : Cursors.IBeam;
			base.OnMouseMove(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			Focus();
			if (e.X >= base.Width - 20)
			{
				Step(e.Y < base.Height / 2 ? 1 : -1);
			}
			else
			{
				editing = true;
				editFresh = true;
				editBuffer = value.ToString();
				Invalidate();
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (ClientRectangle.Contains(e.Location))
			{
				Step(e.Delta > 0 ? 1 : -1);
			}
			base.OnMouseWheel(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Up)
			{
				Step(1);
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.Down)
			{
				Step(-1);
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.PageUp)
			{
				Step(10);
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.PageDown)
			{
				Step(-10);
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.Back)
			{
				if (!editing || editFresh)
				{
					editing = true;
					editFresh = false;
					editBuffer = value.ToString();
				}
				if (editBuffer.Length > 1)
				{
					editBuffer = editBuffer.Substring(0, editBuffer.Length - 1);
				}
				else
				{
					editBuffer = "0";
				}
				CommitBuffer();
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.Enter)
			{
				editing = false;
				editFresh = false;
				Invalidate();
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.Escape)
			{
				editing = false;
				editFresh = false;
				Invalidate();
				e.Handled = true;
			}
			base.OnKeyDown(e);
		}

		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			if (char.IsDigit(e.KeyChar))
			{
				if (!editing || editFresh)
				{
					editing = true;
					editFresh = false;
					editBuffer = string.Empty;
				}
				if (editBuffer.Length >= 4)
				{
					editBuffer = editBuffer.Substring(1);
				}
				editBuffer += e.KeyChar;
				CommitBuffer();
				e.Handled = true;
			}
			base.OnKeyPress(e);
		}

		private void CommitBuffer()
		{
			int parsed;
			if (int.TryParse(editBuffer, out parsed))
			{
				Value = parsed;
			}
			Invalidate();
		}

		protected override void OnLostFocus(EventArgs e)
		{
			editing = false;
			editFresh = false;
			editBuffer = string.Empty;
			Invalidate();
			base.OnLostFocus(e);
		}

		protected override void OnGotFocus(EventArgs e)
		{
			Invalidate();
			base.OnGotFocus(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			Invalidate();
			base.OnEnabledChanged(e);
		}
	}
}
