using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AandmAuto
{
	/// <summary>
	/// Applies unified Apple Liquid Glass styling, true frosted ComboBox rendering,
	/// wallpaper-adaptive native inputs, and Win10/11 DWM chrome to Forms.
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
			Color ink = LiquidGlassEngine.GlassInk(true);

			foreach (Control ctrl in parent.Controls)
			{
				if (ctrl == null || ctrl.IsDisposed) continue;

				if (ctrl is ComboBox)
				{
					StyleComboBox((ComboBox)ctrl, ink, bright);
				}
				else if (ctrl is ListBox && !(ctrl is CheckedListBox))
				{
					StyleListBox((ListBox)ctrl, ink, bright);
				}
				else if (ctrl is TextBox)
				{
					var tb = (TextBox)ctrl;
					tb.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(tb);
					tb.ForeColor = ink;
					tb.BorderStyle = BorderStyle.FixedSingle;
					ApplyScrollTheme(tb, bright);
					HookAdaptiveFill(tb);
				}
				else if (ctrl is NumericUpDown)
				{
					var nud = (NumericUpDown)ctrl;
					nud.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(nud);
					nud.ForeColor = ink;
					nud.BorderStyle = BorderStyle.FixedSingle;
					HookAdaptiveFill(nud);
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

		private static void HookAdaptiveFill(Control ctrl)
		{
			object existing;
			if (styledControls.TryGetValue(ctrl, out existing)) return;
			styledControls.Add(ctrl, new object());
			ctrl.HandleCreated += delegate
			{
				if (!ctrl.IsDisposed && UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
				{
					ctrl.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(ctrl);
				}
			};
			ctrl.VisibleChanged += delegate
			{
				if (ctrl.Visible && !ctrl.IsDisposed && UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
				{
					Color next = LiquidGlassEngine.SampleAdaptiveGlassFill(ctrl);
					if (ctrl.BackColor != next) ctrl.BackColor = next;
				}
			};
		}

		private static void StyleComboBox(ComboBox cb, Color ink, bool bright)
		{
			cb.FlatStyle = FlatStyle.Flat;
			cb.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(cb);
			cb.ForeColor = ink;
			if (cb.DropDownStyle == ComboBoxStyle.DropDownList)
			{
				cb.DrawMode = DrawMode.OwnerDrawFixed;
				cb.ItemHeight = Math.Max(20, cb.Font.Height + 6);
				object existing;
				if (!styledControls.TryGetValue(cb, out existing))
				{
					var overlay = new GlassComboOverlay(cb);
					styledControls.Add(cb, overlay);
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
			bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
			bool isEditBox = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;

			Color bg = LiquidGlassEngine.SampleAdaptiveGlassFill(cb);
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
				Rectangle tb = new Rectangle(e.Bounds.X + 7, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 12), e.Bounds.Height);
				TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
					| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
				GameText.Draw(g, text, cb.Font, tb, textInk, flags);
			}
		}

		private static void StyleListBox(ListBox lb, Color ink, bool bright)
		{
			lb.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(lb);
			lb.ForeColor = ink;
			lb.BorderStyle = BorderStyle.FixedSingle;
			ApplyScrollTheme(lb, bright);
			HookAdaptiveFill(lb);
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

		/// <summary>
		/// Subclasses a native DropDownList ComboBox in ThemeLiquidGlass to replace its opaque black box
		/// and white arrow square with a true frosted glass pill + thin vector chevron while keeping 100% native behavior.
		/// </summary>
		private sealed class GlassComboOverlay : NativeWindow
		{
			private const int WmPaint = 0x000F;
			private const int WmEraseBkgnd = 0x0014;
			private const int WmPrint = 0x0317;
			private const int WmPrintClient = 0x0318;
			private readonly ComboBox combo;
			private bool hovered;

			[StructLayout(LayoutKind.Sequential)]
			private struct PaintStruct
			{
				public IntPtr hdc;
				public int fErase;
				public int rcLeft, rcTop, rcRight, rcBottom;
				public int fRestore;
				public int fIncUpdate;
				private long r0, r1, r2, r3;
			}

			[DllImport("user32.dll")]
			private static extern IntPtr BeginPaint(IntPtr hWnd, out PaintStruct lpPaint);

			[DllImport("user32.dll")]
			private static extern bool EndPaint(IntPtr hWnd, ref PaintStruct lpPaint);

			internal GlassComboOverlay(ComboBox cb)
			{
				combo = cb;
				cb.MouseEnter += delegate { hovered = true; cb.Invalidate(); };
				cb.MouseLeave += delegate { hovered = false; cb.Invalidate(); };
				cb.HandleCreated += delegate { Attach(); };
				cb.HandleDestroyed += delegate { ReleaseHandle(); };
				cb.Disposed += delegate { ReleaseHandle(); };
				if (cb.IsHandleCreated) Attach();
			}

			private void Attach()
			{
				if (combo.IsDisposed || !combo.IsHandleCreated) return;
				if (Handle != combo.Handle)
				{
					ReleaseHandle();
					AssignHandle(combo.Handle);
				}
			}

			protected override void WndProc(ref Message m)
			{
				if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass || UiTheme.ExtremePerformanceMode || combo.IsDisposed)
				{
					base.WndProc(ref m);
					return;
				}
				if (m.Msg == WmEraseBkgnd)
				{
					m.Result = new IntPtr(1);
					return;
				}
				if (m.Msg == WmPrint || m.Msg == WmPrintClient)
				{
					if (m.WParam != IntPtr.Zero && combo.Width > 4 && combo.Height > 4)
					{
						using (Graphics gTarget = Graphics.FromHdc(m.WParam))
						using (var bmp = new Bitmap(combo.Width, combo.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
						{
							using (Graphics g = Graphics.FromImage(bmp))
								PaintFrostedFace(g);
							gTarget.DrawImageUnscaled(bmp, 0, 0);
						}
					}
					m.Result = IntPtr.Zero;
					return;
				}
				if (m.Msg == WmPaint)
				{
					PaintStruct ps;
					IntPtr hdc = BeginPaint(combo.Handle, out ps);
					try
					{
						if (hdc != IntPtr.Zero && combo.Width > 4 && combo.Height > 4)
						{
							using (Graphics gScreen = Graphics.FromHdc(hdc))
							using (var bmp = new Bitmap(combo.Width, combo.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
							{
								using (Graphics g = Graphics.FromImage(bmp))
								{
									PaintFrostedFace(g);
								}
								gScreen.DrawImageUnscaled(bmp, 0, 0);
							}
						}
					}
					finally
					{
						EndPaint(combo.Handle, ref ps);
					}
					m.Result = IntPtr.Zero;
					return;
				}
				base.WndProc(ref m);
			}

			private void PaintFrostedFace(Graphics g)
			{
				UiSurface.PaintBackdrop(combo, g);
				g.SmoothingMode = SmoothingMode.HighQuality;

				Rectangle r = new Rectangle(0, 0, Math.Max(4, combo.Width - 1), Math.Max(4, combo.Height - 1));
				int rad = Math.Min(8, r.Height / 2);
				bool bright = LiquidGlassEngine.IsBrightBackdrop();

				using (GraphicsPath path = UiTheme.RoundedRect(r, rad))
				{
					LiquidGlassEngine.DrawGlassCard(g, combo, path, rad, Color.FromArgb(14, 18, 24), true);
					if (hovered || combo.Focused || combo.DroppedDown)
					{
						int ha = combo.DroppedDown ? 36 : 22;
						using (var hb = new SolidBrush(Color.FromArgb(ha, bright ? Color.Black : Color.White)))
							g.FillPath(hb, path);
						Color focusBorder = combo.DroppedDown
							? Color.FromArgb(180, 14, 132, 255)
							: (bright ? Color.FromArgb(65, 20, 32, 52) : Color.FromArgb(78, 255, 255, 255));
						using (var hp = new Pen(focusBorder, 1f))
							g.DrawPath(hp, path);
					}
				}

				// Selected text
				string text = combo.SelectedIndex >= 0 ? (combo.GetItemText(combo.SelectedItem) ?? "") : (combo.Text ?? "");
				Rectangle tb = new Rectangle(9, 0, Math.Max(1, r.Width - 28), r.Height);
				Color ink = LiquidGlassEngine.GlassInk(combo.Enabled);
				Color shadow = LiquidGlassEngine.GlassTextShadow();
				TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
					| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
				if (shadow.A > 0)
					GameText.Draw(g, text, combo.Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadow, flags);
				GameText.Draw(g, text, combo.Font, tb, ink, flags);

				// Crisp thin vector chevron
				float cx = r.Right - 13f;
				float cy = r.Y + r.Height / 2f;
				using (var pen = new Pen(LiquidGlassEngine.GlassInk(combo.Enabled), 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
				{
					g.DrawLines(pen, new[]
					{
						new PointF(cx - 3.6f, cy - 1.6f),
						new PointF(cx, cy + 2.0f),
						new PointF(cx + 3.6f, cy - 1.6f)
					});
				}
			}
		}
	}

	/// <summary>
	/// True transparent frosted-glass multi-line read-only viewer (replaces solid black TextBox
	/// for Disk Report / Release Notes in ThemeLiquidGlass). Supports mouse-wheel scroll & copy.
	/// </summary>
	internal sealed class GlassReadOnlyTextBox : Control
	{
		private string content = string.Empty;
		private int scrollOffset;
		private int contentHeight;
		private bool hovered;

		public override string Text
		{
			get { return content; }
			set
			{
				string next = value ?? string.Empty;
				if (content != next)
				{
					content = next;
					scrollOffset = 0;
					RecalcContentHeight();
					Invalidate();
				}
			}
		}

		public GlassReadOnlyTextBox()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
			BackColor = Color.Transparent;
			TabStop = true;
			Font = UiTheme.GameFont(8.75f);

			var menu = new ContextMenuStrip();
			menu.Items.Add("复制全部内容", null, delegate { CopyAll(); });
			ContextMenuStrip = menu;
		}

		private void CopyAll()
		{
			if (!string.IsNullOrEmpty(content))
			{
				try { Clipboard.SetText(content); } catch { }
			}
		}

		private void RecalcContentHeight()
		{
			int w = Math.Max(40, Width - 26);
			Size sz = TextRenderer.MeasureText(
				string.IsNullOrEmpty(content) ? " " : content,
				Font,
				new Size(w, 100000),
				TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
			contentHeight = sz.Height + 16;
			int maxScroll = Math.Max(0, contentHeight - Height);
			if (scrollOffset > maxScroll) scrollOffset = maxScroll;
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			RecalcContentHeight();
		}

		protected override void OnFontChanged(EventArgs e)
		{
			base.OnFontChanged(e);
			RecalcContentHeight();
		}

		protected override void OnPaintBackground(PaintEventArgs e) { }

		protected override void OnPaint(PaintEventArgs e)
		{
			UiSurface.PaintWhole(this, e, PaintContent);
		}

		private void PaintContent(Graphics g)
		{
			UiSurface.PaintBackdrop(this, g);
			if (Width < 10 || Height < 10) return;

			g.SmoothingMode = SmoothingMode.HighQuality;
			Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
			using (GraphicsPath path = UiTheme.RoundedRect(r, 10))
			{
				LiquidGlassEngine.DrawGlassCard(g, this, path, 10, Color.FromArgb(10, 14, 22), false);

				GraphicsState st = g.Save();
				try
				{
					g.SetClip(new Rectangle(8, 6, Math.Max(1, Width - 22), Math.Max(1, Height - 12)), CombineMode.Intersect);
					Rectangle textRect = new Rectangle(10, 8 - scrollOffset, Math.Max(20, Width - 26), Math.Max(contentHeight, Height));
					Color ink = LiquidGlassEngine.GlassInk(Enabled);
					Color shadow = LiquidGlassEngine.GlassTextShadow();
					TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
					if (shadow.A > 0)
						GameText.Draw(g, content, Font, new Rectangle(textRect.X + 1, textRect.Y + 1, textRect.Width, textRect.Height), shadow, flags);
					GameText.Draw(g, content, Font, textRect, ink, flags);
				}
				finally { g.Restore(st); }
			}

			// Sleek thin frosted scrollbar thumb (only shown when content overflows)
			int maxScroll = Math.Max(0, contentHeight - Height);
			if (maxScroll > 0)
			{
				int trackH = Math.Max(16, Height - 16);
				int thumbH = Math.Max(24, (int)(trackH * ((float)Height / contentHeight)));
				int thumbY = 8 + (int)((trackH - thumbH) * ((float)scrollOffset / maxScroll));
				Rectangle thumb = new Rectangle(Width - 8, thumbY, 4, thumbH);
				bool bright = LiquidGlassEngine.IsBrightBackdrop();
				int a = hovered ? (bright ? 120 : 135) : (bright ? 68 : 76);
				using (GraphicsPath tp = UiTheme.RoundedRect(thumb, 2))
				using (var tb = new SolidBrush(Color.FromArgb(a, bright ? Color.Black : Color.White)))
					g.FillPath(tb, tp);
			}
		}

		protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

		protected override void OnMouseDown(MouseEventArgs e)
		{
			Focus();
			base.OnMouseDown(e);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			int maxScroll = Math.Max(0, contentHeight - Height);
			if (maxScroll > 0)
			{
				int step = Math.Max(24, Font.Height * 2);
				scrollOffset = Math.Max(0, Math.Min(maxScroll, scrollOffset - Math.Sign(e.Delta) * step));
				Invalidate();
			}
			base.OnMouseWheel(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.Control && e.KeyCode == Keys.C)
			{
				CopyAll();
				e.Handled = true;
			}
			base.OnKeyDown(e);
		}
	}

	/// <summary>
	/// Hardware-composited pure DWM fade-in (~140ms) and fade-out (~100ms) for Settings & Dialog forms.
	/// Avoids moving form.Top (zero WM_MOVE child invalidations) and pre-paints once while invisible
	/// so all fade frames update only DWM layered alpha at 60-120fps. Automatically bypassed when
	/// ExtremePerformanceMode, !MotionEnabled, or PreviewStaticMode is active.
	/// </summary>
	internal static class GlassFormTransition
	{
		internal static void Attach(Form form)
		{
			if (form == null || form.IsDisposed) return;
			if (!GlassAnimator.Enabled) return;

			bool closingAnimated = false;
			bool fadingOut = false;
			float t = 0f;
			Func<float, bool> animStep = null;

			form.Load += delegate
			{
				if (!GlassAnimator.Enabled || form.IsDisposed) return;
				form.Opacity = 0.0;
			};

			form.Shown += delegate
			{
				if (!GlassAnimator.Enabled || form.IsDisposed)
				{
					if (!form.IsDisposed) form.Opacity = 1.0;
					return;
				}
				// Paint all controls & warm glass caches ONCE while opacity is 0 so fade frames are pure GPU DWM alpha.
				try { form.Update(); } catch { }
				if (form.IsDisposed) return;
				form.Opacity = 0.02;
				t = 0f;
				animStep = delegate(float dt)
				{
					if (form.IsDisposed || fadingOut) return false;
					t += dt / 0.14f;
					if (t >= 1f)
					{
						form.Opacity = 1.0;
						return false;
					}
					float inv = 1f - t;
					float ease = 1f - inv * inv * inv;
					form.Opacity = Math.Max(0.02, Math.Min(0.995, ease));
					return true;
				};
				GlassAnimator.Add(animStep);
			};

			form.FormClosing += delegate(object sender, FormClosingEventArgs e)
			{
				if (closingAnimated || fadingOut || !GlassAnimator.Enabled || form.IsDisposed || !form.Visible) return;
				if (e.CloseReason != CloseReason.UserClosing && e.CloseReason != CloseReason.None) return;

				DialogResult savedResult = form.DialogResult;
				e.Cancel = true;
				fadingOut = true;
				if (animStep != null) GlassAnimator.Remove(animStep);

				float startOp = (float)form.Opacity;
				float outT = 0f;
				Func<float, bool> outStep = null;
				outStep = delegate(float dt)
				{
					if (form.IsDisposed) return false;
					outT += dt / 0.10f;
					if (outT >= 1f)
					{
						closingAnimated = true;
						form.Opacity = 0.0;
						form.DialogResult = savedResult;
						if (savedResult == DialogResult.None) form.Close();
						return false;
					}
					float inv = 1f - outT;
					form.Opacity = Math.Max(0.0, startOp * inv * inv);
					return true;
				};
				GlassAnimator.Add(outStep);
			};

			form.Disposed += delegate
			{
				if (animStep != null) GlassAnimator.Remove(animStep);
			};
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
