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
		private int glassHoverZone; // -1 = minus, 0 = center, 1 = plus, -2 = none
		private int glassPressZone = -2;

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

		private int GlassStepButtonWidth()
		{
			return Math.Max(22, Math.Min(34, base.Width / 3));
		}

        private void PaintContent(Graphics graphics)
		{
            UiSurface.PaintBackdrop(this,graphics);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				int rad = Math.Max(6, Math.Min(bounds.Height / 2, 16));
				UiTheme.DrawLiquidGlassPill(graphics, this, bounds, rad, Color.White, 0, Color.White, hovered, false, Focused, base.Enabled);

				int btnW = GlassStepButtonWidth();
				bool canMinus = base.Enabled && value > minimum;
				bool canPlus = base.Enabled && value < maximum;
				bool bright = LiquidGlassEngine.IsBrightBackdrop();

				// Subtle hover / press highlight on minus or plus zone
				if (base.Enabled && (glassHoverZone == -1 || glassHoverZone == 1))
				{
					Rectangle zoneRect = glassHoverZone == -1
						? new Rectangle(bounds.X + 2, bounds.Y + 2, btnW - 3, Math.Max(4, bounds.Height - 4))
						: new Rectangle(bounds.Right - btnW + 1, bounds.Y + 2, btnW - 3, Math.Max(4, bounds.Height - 4));
					int zRad = Math.Max(4, Math.Min(rad - 2, zoneRect.Height / 2));
					int alpha = (glassPressZone == glassHoverZone) ? (bright ? 52 : 64) : (bright ? 36 : 34);
					using (GraphicsPath zp = UiTheme.RoundedRect(zoneRect, zRad))
					using (var zb = new SolidBrush(Color.FromArgb(alpha, bright ? Color.Black : Color.White)))
						graphics.FillPath(zb, zp);
				}

				// Hairline dividers between - | value | +
				using (var sep = new Pen(LiquidGlassEngine.GlassHairline(), 1f))
				{
					int padY = Math.Max(5, bounds.Height / 4);
					graphics.DrawLine(sep, btnW, padY, btnW, bounds.Height - padY);
					graphics.DrawLine(sep, bounds.Right - btnW, padY, bounds.Right - btnW, bounds.Height - padY);
				}

				// Crisp vector minus & plus glyphs
				Color minusInk = LiquidGlassEngine.GlassInk(canMinus);
				Color plusInk = LiquidGlassEngine.GlassInk(canPlus);
				float midY = bounds.Y + bounds.Height / 2f;
				float leftCx = bounds.X + btnW / 2f;
				float rightCx = bounds.Right - btnW / 2f;
				float halfBar = Math.Min(5.2f, Math.Max(3.6f, btnW * 0.18f));
				using (var mp = new Pen(minusInk, 1.75f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
				{
					graphics.DrawLine(mp, leftCx - halfBar, midY, leftCx + halfBar, midY);
				}
				using (var pp = new Pen(plusInk, 1.75f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
				{
					graphics.DrawLine(pp, rightCx - halfBar, midY, rightCx + halfBar, midY);
					graphics.DrawLine(pp, rightCx, midY - halfBar, rightCx, midY + halfBar);
				}

				string t = editing ? editBuffer : value.ToString();
				if (string.IsNullOrEmpty(t)) t = "0";
				Rectangle tb = new Rectangle(btnW + 2, 1, Math.Max(1, bounds.Width - btnW * 2 - 4), Math.Max(1, bounds.Height - 2));
				Font numFont = base.Height >= 32 ? UiTheme.GameFont(10.5f, FontStyle.Bold) : Font;
				Color textInk = LiquidGlassEngine.GlassInk(base.Enabled);
				Color shadowInk = LiquidGlassEngine.GlassTextShadow();
				TextFormatFlags tf = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine;
				if (shadowInk.A > 0)
					GameText.Draw(graphics, t, numFont, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadowInk, tf);
				GameText.Draw(graphics, t, numFont, tb, textInk, tf);
				if (numFont != Font) numFont.Dispose();
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
			glassHoverZone = -2;
			glassPressZone = -2;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				int btnW = GlassStepButtonWidth();
				int zone = e.X < btnW ? -1 : (e.X >= base.Width - btnW ? 1 : 0);
				if (zone != glassHoverZone)
				{
					glassHoverZone = zone;
					Invalidate();
				}
				Cursor = zone != 0 ? Cursors.Hand : Cursors.IBeam;
				base.OnMouseMove(e);
				return;
			}
			bool overArrow = e.X >= base.Width - 20;
			Cursor = overArrow ? Cursors.Hand : Cursors.IBeam;
			base.OnMouseMove(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			Focus();
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				int btnW = GlassStepButtonWidth();
				if (e.X < btnW)
				{
					glassPressZone = -1;
					Step(-1);
					Invalidate();
				}
				else if (e.X >= base.Width - btnW)
				{
					glassPressZone = 1;
					Step(1);
					Invalidate();
				}
				else
				{
					glassPressZone = 0;
					editing = true;
					editFresh = true;
					editBuffer = value.ToString();
					Invalidate();
				}
				base.OnMouseDown(e);
				return;
			}
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

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (glassPressZone != -2)
			{
				glassPressZone = -2;
				Invalidate();
			}
			base.OnMouseUp(e);
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
