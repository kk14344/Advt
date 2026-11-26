using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AandmAuto
{
	/// <summary>
	/// Applies unified Apple Liquid Glass styling and Win10/11 DWM chrome to Forms
	/// and native WinForms controls (ComboBox, ListBox, TextBox, NumericUpDown, RadioButton).
	/// Non-Liquid-Glass themes only receive standard DWM chrome and remain visually untouched.
	/// </summary>
	internal static class GlassControlStyler
	{
		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

		private static readonly ConditionalWeakTable<Control, object> styledControls =
			new ConditionalWeakTable<Control, object>();

		internal static void ApplyToForm(Form form)
		{
			if (form == null || form.IsDisposed) return;
			WindowStyle.ApplyThemeChrome(form);
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass) return;
			ApplyRecursive(form);
		}

		private static void ApplyRecursive(Control parent)
		{
			if (parent == null || parent.IsDisposed) return;
			bool bright = LiquidGlassEngine.IsBrightBackdrop();
			Color inputBg = LiquidGlassEngine.GlassInputFill();
			Color ink = LiquidGlassEngine.GlassInk(true);

			foreach (Control ctrl in parent.Controls)
			{
				if (ctrl == null || ctrl.IsDisposed) continue;

				if (ctrl is ComboBox)
				{
					StyleComboBox((ComboBox)ctrl, inputBg, ink, bright);
				}
				else if (ctrl is ListBox && !(ctrl is CheckedListBox))
				{
					StyleListBox((ListBox)ctrl, inputBg, ink, bright);
				}
				else if (ctrl is TextBox)
				{
					var tb = (TextBox)ctrl;
					tb.BackColor = inputBg;
					tb.ForeColor = ink;
					tb.BorderStyle = BorderStyle.FixedSingle;
					ApplyScrollTheme(tb, bright);
				}
				else if (ctrl is NumericUpDown)
				{
					var nud = (NumericUpDown)ctrl;
					nud.BackColor = inputBg;
					nud.ForeColor = ink;
					nud.BorderStyle = BorderStyle.FixedSingle;
				}
				else if (ctrl is RadioButton)
				{
					var rb = (RadioButton)ctrl;
					rb.ForeColor = ink;
					rb.BackColor = Color.Transparent;
				}
				else if (ctrl is CheckBox && !(ctrl is GameCheckBox))
				{
					var cb = (CheckBox)ctrl;
					cb.ForeColor = ink;
					cb.BackColor = Color.Transparent;
				}
				else if (ctrl is GlassRichTextBox)
				{
					((GlassRichTextBox)ctrl).RefreshGlassChrome();
				}

				if (ctrl.HasChildren)
				{
					ApplyRecursive(ctrl);
				}
			}
		}

		private static void StyleComboBox(ComboBox cb, Color inputBg, Color ink, bool bright)
		{
			cb.FlatStyle = FlatStyle.Flat;
			cb.BackColor = inputBg;
			cb.ForeColor = ink;
			if (cb.DropDownStyle == ComboBoxStyle.DropDownList)
			{
				cb.DrawMode = DrawMode.OwnerDrawFixed;
				cb.ItemHeight = Math.Max(20, cb.Font.Height + 6);
				object existing;
				if (!styledControls.TryGetValue(cb, out existing))
				{
					styledControls.Add(cb, new object());
					cb.DrawItem += OnDrawGlassComboItem;
				}
			}
			ApplyScrollTheme(cb, bright, true);
		}

		private static void OnDrawGlassComboItem(object sender, DrawItemEventArgs e)
		{
			var cb = sender as ComboBox;
			if (cb == null || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;

			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			bool bright = LiquidGlassEngine.IsBrightBackdrop();
			bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
			bool isEditBox = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;

			Color bg = LiquidGlassEngine.GlassInputFill();
			using (var b = new SolidBrush(bg))
				g.FillRectangle(b, e.Bounds);

			if (isSelected && !isEditBox)
			{
				Rectangle pill = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 1, Math.Max(4, e.Bounds.Width - 6), Math.Max(4, e.Bounds.Height - 2));
				using (GraphicsPath p = UiTheme.RoundedRect(pill, 6))
				using (var sb = new SolidBrush(Color.FromArgb(220, 14, 132, 255)))
					g.FillPath(sb, p);
			}

			if (e.Index >= 0 && e.Index < cb.Items.Count)
			{
				string text = cb.GetItemText(cb.Items[e.Index]) ?? string.Empty;
				Color textInk = (isSelected && !isEditBox) ? Color.White : LiquidGlassEngine.GlassInk(cb.Enabled);
				Rectangle tb = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 10), e.Bounds.Height);
				TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
					| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
				GameText.Draw(g, text, cb.Font, tb, textInk, flags);
			}
		}

		private static void StyleListBox(ListBox lb, Color inputBg, Color ink, bool bright)
		{
			lb.BackColor = inputBg;
			lb.ForeColor = ink;
			lb.BorderStyle = BorderStyle.FixedSingle;
			ApplyScrollTheme(lb, bright);
		}

		private static void ApplyScrollTheme(Control ctrl, bool bright, bool isCombo = false)
		{
			string themeName = isCombo
				? (bright ? "CFD" : "DarkMode_CFD")
				: (bright ? "Explorer" : "DarkMode_Explorer");

			Action apply = delegate
			{
				if (ctrl.IsDisposed || !ctrl.IsHandleCreated) return;
				try { SetWindowTheme(ctrl.Handle, themeName, null); } catch { }
			};

			if (ctrl.IsHandleCreated)
			{
				apply();
			}
			else
			{
				ctrl.HandleCreated += delegate { apply(); };
			}
		}
	}

	/// <summary>
	/// RadioButton that renders crisp iOS circular glass indicators in ThemeLiquidGlass,
	/// while delegating untouched to standard WinForms RadioButton in all other 4 themes.
	/// </summary>
	internal sealed class GlassRadioButton : RadioButton
	{
		public GlassRadioButton()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
			Cursor = Cursors.Hand;
		}

		protected override void OnPaint(PaintEventArgs pevent)
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass || UiTheme.ExtremePerformanceMode)
			{
				base.OnPaint(pevent);
				return;
			}
			Graphics g = pevent.Graphics;
			UiSurface.PaintBackdrop(this, g);
			g.SmoothingMode = SmoothingMode.AntiAlias;

			bool bright = LiquidGlassEngine.IsBrightBackdrop();
			int dia = 14;
			int rx = 2;
			int ry = Math.Max(1, (Height - dia) / 2);
			Rectangle circle = new Rectangle(rx, ry, dia, dia);

			if (Checked)
			{
				using (var fill = new SolidBrush(Color.FromArgb(230, 14, 132, 255)))
					g.FillEllipse(fill, circle);
				using (var rim = new Pen(Color.FromArgb(220, 180, 224, 255), 1.1f))
					g.DrawEllipse(rim, circle);
				using (var core = new SolidBrush(Color.White))
					g.FillEllipse(core, rx + 4.2f, ry + 4.2f, 5.6f, 5.6f);
			}
			else
			{
				Color bg = bright ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(24, 255, 255, 255);
				Color border = bright ? Color.FromArgb(125, 24, 38, 60) : Color.FromArgb(155, 255, 255, 255);
				using (var fill = new SolidBrush(bg))
					g.FillEllipse(fill, circle);
				using (var rim = new Pen(border, 1.15f))
					g.DrawEllipse(rim, circle);
			}

			Rectangle tb = new Rectangle(rx + dia + 6, 0, Math.Max(1, Width - rx - dia - 8), Height);
			Color ink = LiquidGlassEngine.GlassInk(Enabled);
			Color shadow = LiquidGlassEngine.GlassTextShadow();
			TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
				| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
			if (shadow.A > 0)
				GameText.Draw(g, Text, Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadow, flags);
			GameText.Draw(g, Text, Font, tb, ink, flags);
		}
	}
}
