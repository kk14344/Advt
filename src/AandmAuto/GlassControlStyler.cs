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
				else if (ctrl is CheckedListBox)
				{
					StyleCheckedListBox((CheckedListBox)ctrl, ink, bright);
				}
				else if (ctrl is ListBox)
				{
					StyleListBox((ListBox)ctrl, ink, bright);
				}
				else if (ctrl is ListView)
				{
					StyleListView((ListView)ctrl, ink, bright);
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
				else if (ctrl is ScrollableControl && ((ScrollableControl)ctrl).AutoScroll)
				{
					ApplyScrollTheme(ctrl, bright);
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

		internal static void ApplyScrollThemePublic(Control ctrl)
		{
			if (ctrl == null || ctrl.IsDisposed || UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass) return;
			ApplyScrollTheme(ctrl, LiquidGlassEngine.IsBrightBackdrop());
		}

		private static void StyleListBox(ListBox lb, Color ink, bool bright)
		{
			lb.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(lb);
			lb.ForeColor = ink;
			lb.BorderStyle = BorderStyle.FixedSingle;
			lb.DrawMode = DrawMode.OwnerDrawFixed;
			lb.ItemHeight = Math.Max(24, lb.Font.Height + 8);
			object existing;
			if (!styledControls.TryGetValue(lb, out existing))
			{
				var overlay = new GlassListBoxOverlay(lb);
				styledControls.Add(lb, overlay);
				lb.DrawItem += OnDrawGlassListBoxItem;
			}
			ApplyScrollTheme(lb, bright);
		}

		private static void OnDrawGlassListBoxItem(object sender, DrawItemEventArgs e)
		{
			var lb = sender as ListBox;
			if (lb == null || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;

			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && !UiTheme.ExtremePerformanceMode)
			{
				GraphicsState st = g.Save();
				try
				{
					g.SetClip(e.Bounds, CombineMode.Intersect);
					UiSurface.PaintBackdrop(lb, g);
					bool bright = LiquidGlassEngine.IsBrightBackdrop();
					int va = LiquidGlassEngine.Preset == LiquidGlassEngine.PresetDeep ? 78 : (bright ? 46 : 44);
					using (var veil = new SolidBrush(Color.FromArgb(va, bright ? Color.White : Color.FromArgb(10, 14, 24))))
						g.FillRectangle(veil, e.Bounds);
				}
				finally { g.Restore(st); }
			}
			else
			{
				using (var b = new SolidBrush(lb.BackColor))
					g.FillRectangle(b, e.Bounds);
			}

			bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
			if (selected && e.Index >= 0 && e.Index < lb.Items.Count)
			{
				Rectangle pill = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, Math.Max(4, e.Bounds.Width - 8), Math.Max(4, e.Bounds.Height - 4));
				using (GraphicsPath p = UiTheme.RoundedRect(pill, 7))
				using (var sb = new SolidBrush(Color.FromArgb(205, 14, 132, 255)))
					g.FillPath(sb, p);
			}

			if (e.Index >= 0 && e.Index < lb.Items.Count)
			{
				string text = lb.GetItemText(lb.Items[e.Index]) ?? string.Empty;
				Color textInk = selected ? Color.White : LiquidGlassEngine.GlassInk(lb.Enabled);
				Color shadow = selected ? Color.FromArgb(110, 0, 0, 0) : LiquidGlassEngine.GlassTextShadow();
				Rectangle tb = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 16), e.Bounds.Height);
				TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
					| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
				if (shadow.A > 0)
					GameText.Draw(g, text, lb.Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadow, flags);
				GameText.Draw(g, text, lb.Font, tb, textInk, flags);
			}
		}

		private static void StyleCheckedListBox(CheckedListBox clb, Color ink, bool bright)
		{
			clb.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(clb);
			clb.ForeColor = ink;
			clb.BorderStyle = BorderStyle.FixedSingle;
			ApplyScrollTheme(clb, bright);
			HookAdaptiveFill(clb);
		}

		private static void StyleListView(ListView lv, Color ink, bool bright)
		{
			lv.BackColor = LiquidGlassEngine.SampleAdaptiveGlassFill(lv);
			lv.ForeColor = ink;
			lv.BorderStyle = BorderStyle.FixedSingle;
			lv.OwnerDraw = true;
			object existing;
			if (!styledControls.TryGetValue(lv, out existing))
			{
				var overlay = new GlassListViewOverlay(lv);
				styledControls.Add(lv, overlay);
				lv.DrawColumnHeader += OnDrawGlassListViewHeader;
				lv.DrawItem += OnDrawGlassListViewItem;
				lv.DrawSubItem += OnDrawGlassListViewSubItem;
			}
			ApplyScrollTheme(lv, bright);
		}

		private static void OnDrawGlassListViewHeader(object sender, DrawListViewColumnHeaderEventArgs e)
		{
			var lv = sender as ListView;
			if (lv == null || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && !UiTheme.ExtremePerformanceMode)
			{
				GraphicsState st = g.Save();
				try
				{
					g.SetClip(e.Bounds, CombineMode.Intersect);
					UiSurface.PaintBackdrop(lv, g);
					bool bright = LiquidGlassEngine.IsBrightBackdrop();
					int va = bright ? 72 : 84;
					using (var b = new SolidBrush(Color.FromArgb(va, bright ? Color.White : Color.FromArgb(10, 14, 24))))
						g.FillRectangle(b, e.Bounds);
				}
				finally { g.Restore(st); }
				using (var sep = new Pen(LiquidGlassEngine.GlassHairline(), 1f))
				{
					g.DrawLine(sep, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
					g.DrawLine(sep, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
				}
				Rectangle tb = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 12), e.Bounds.Height);
				TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
					| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
				Color ink = LiquidGlassEngine.GlassInk(lv.Enabled);
				Color shadow = LiquidGlassEngine.GlassTextShadow();
				if (shadow.A > 0)
					GameText.Draw(g, e.Header.Text, lv.Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadow, flags);
				GameText.Draw(g, e.Header.Text, lv.Font, tb, ink, flags);
			}
			else
			{
				e.DrawDefault = true;
			}
		}

		private static void OnDrawGlassListViewItem(object sender, DrawListViewItemEventArgs e)
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass || UiTheme.ExtremePerformanceMode)
			{
				e.DrawDefault = true;
				return;
			}
			// Handled in DrawSubItem for Details view
		}

		private static void OnDrawGlassListViewSubItem(object sender, DrawListViewSubItemEventArgs e)
		{
			var lv = sender as ListView;
			if (lv == null || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass || UiTheme.ExtremePerformanceMode)
			{
				e.DrawDefault = true;
				return;
			}
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = e.Bounds;
			GraphicsState st = g.Save();
			try
			{
				g.SetClip(bounds, CombineMode.Intersect);
				UiSurface.PaintBackdrop(lv, g);
				bool bright = LiquidGlassEngine.IsBrightBackdrop();
				int va = LiquidGlassEngine.Preset == LiquidGlassEngine.PresetDeep ? 76 : (bright ? 42 : 42);
				using (var veil = new SolidBrush(Color.FromArgb(va, bright ? Color.White : Color.FromArgb(10, 14, 24))))
					g.FillRectangle(veil, bounds);
			}
			finally { g.Restore(st); }

			bool selected = e.Item != null && e.Item.Selected;
			if (selected)
			{
				using (var sb = new SolidBrush(Color.FromArgb(175, 14, 132, 255)))
					g.FillRectangle(sb, bounds.X, bounds.Y + 1, bounds.Width, Math.Max(2, bounds.Height - 2));
			}

			int textLeft = bounds.X + 8;
			if (e.ColumnIndex == 0 && lv.CheckBoxes && e.Item != null)
			{
				int box = 14;
				int bx = bounds.X + 8;
				int by = bounds.Y + Math.Max(1, (bounds.Height - box) / 2);
				DrawGlassCheckGlyph(g, new Rectangle(bx, by, box, box), e.Item.Checked);
				textLeft = bx + box + 8;
			}

			Rectangle tb = new Rectangle(textLeft, bounds.Y, Math.Max(1, bounds.Right - textLeft - 6), bounds.Height);
			string text = e.SubItem != null ? (e.SubItem.Text ?? string.Empty) : string.Empty;
			Color ink = selected ? Color.White : LiquidGlassEngine.GlassInk(lv.Enabled);
			Color shadow = selected ? Color.FromArgb(110, 0, 0, 0) : LiquidGlassEngine.GlassTextShadow();
			TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
				| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
			if (shadow.A > 0)
				GameText.Draw(g, text, lv.Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadow, flags);
			GameText.Draw(g, text, lv.Font, tb, ink, flags);
		}

		internal static void DrawGlassCheckGlyph(Graphics g, Rectangle boxRect, bool isChecked)
		{
			bool bright = LiquidGlassEngine.IsBrightBackdrop();
			using (GraphicsPath path = UiTheme.RoundedRect(boxRect, 4))
			{
				if (isChecked)
				{
					using (var fill = new SolidBrush(Color.FromArgb(230, 14, 132, 255)))
						g.FillPath(fill, path);
					using (var pen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
					{
						g.DrawLines(pen, new[]
						{
							new PointF(boxRect.X + 3.2f, boxRect.Y + boxRect.Height * 0.52f),
							new PointF(boxRect.X + boxRect.Width * 0.42f, boxRect.Bottom - 3.6f),
							new PointF(boxRect.Right - 3.0f, boxRect.Y + 3.6f)
						});
					}
				}
				else
				{
					Color fillC = bright ? Color.FromArgb(55, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255);
					Color borderC = bright ? Color.FromArgb(135, 24, 38, 60) : Color.FromArgb(160, 255, 255, 255);
					using (var fill = new SolidBrush(fillC)) g.FillPath(fill, path);
					using (var pen = new Pen(borderC, 1.15f)) g.DrawPath(pen, path);
				}
			}
		}

		/// <summary>
		/// Subclasses ListView WM_ERASEBKGND in ThemeLiquidGlass so empty rows & margins show frosted wallpaper.
		/// </summary>
		private sealed class GlassListViewOverlay : NativeWindow
		{
			private const int WmEraseBkgnd = 0x0014;
			private readonly ListView view;

			internal GlassListViewOverlay(ListView lv)
			{
				view = lv;
				lv.HandleCreated += delegate { Attach(); };
				lv.HandleDestroyed += delegate { ReleaseHandle(); };
				lv.Disposed += delegate { ReleaseHandle(); };
				if (lv.IsHandleCreated) Attach();
			}

			private void Attach()
			{
				if (view.IsDisposed || !view.IsHandleCreated) return;
				if (Handle != view.Handle)
				{
					ReleaseHandle();
					AssignHandle(view.Handle);
				}
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == WmEraseBkgnd && UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && !UiTheme.ExtremePerformanceMode && !view.IsDisposed)
				{
					if (m.WParam != IntPtr.Zero && view.ClientSize.Width > 2 && view.ClientSize.Height > 2)
					{
						try
						{
							using (Graphics g = Graphics.FromHdc(m.WParam))
							{
								UiSurface.PaintBackdrop(view, g);
								bool bright = LiquidGlassEngine.IsBrightBackdrop();
								int va = LiquidGlassEngine.Preset == LiquidGlassEngine.PresetDeep ? 76 : (bright ? 42 : 42);
								using (var veil = new SolidBrush(Color.FromArgb(va, bright ? Color.White : Color.FromArgb(10, 14, 24))))
									g.FillRectangle(veil, view.ClientRectangle);
							}
							m.Result = new IntPtr(1);
							return;
						}
						catch { }
					}
				}
				base.WndProc(ref m);
			}
		}

		/// <summary>
		/// Subclasses ListBox WM_ERASEBKGND in ThemeLiquidGlass so empty list area shows real frosted wallpaper
		/// instead of a solid gray slab.
		/// </summary>
		private sealed class GlassListBoxOverlay : NativeWindow
		{
			private const int WmEraseBkgnd = 0x0014;
			private readonly ListBox list;

			internal GlassListBoxOverlay(ListBox lb)
			{
				list = lb;
				lb.HandleCreated += delegate { Attach(); };
				lb.HandleDestroyed += delegate { ReleaseHandle(); };
				lb.Disposed += delegate { ReleaseHandle(); };
				if (lb.IsHandleCreated) Attach();
			}

			private void Attach()
			{
				if (list.IsDisposed || !list.IsHandleCreated) return;
				if (Handle != list.Handle)
				{
					ReleaseHandle();
					AssignHandle(list.Handle);
				}
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == WmEraseBkgnd && UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && !UiTheme.ExtremePerformanceMode && !list.IsDisposed)
				{
					if (m.WParam != IntPtr.Zero && list.ClientSize.Width > 2 && list.ClientSize.Height > 2)
					{
						try
						{
							using (Graphics g = Graphics.FromHdc(m.WParam))
							{
								UiSurface.PaintBackdrop(list, g);
								bool bright = LiquidGlassEngine.IsBrightBackdrop();
								int va = LiquidGlassEngine.Preset == LiquidGlassEngine.PresetDeep ? 78 : (bright ? 46 : 44);
								using (var veil = new SolidBrush(Color.FromArgb(va, bright ? Color.White : Color.FromArgb(10, 14, 24))))
									g.FillRectangle(veil, list.ClientRectangle);
							}
							m.Result = new IntPtr(1);
							return;
						}
						catch { }
					}
				}
				base.WndProc(ref m);
			}
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
	/// True 90-144fps+ macOS/iOS sheet spring transition (scale 0.94->1.00 + 16px float + fade on open;
	/// scale 1.00->0.96 + 10px drop + fade on close) driven by a zero-child-repaint Win32
	/// UpdateLayeredWindow + persistent DIBSection snapshot ghost on GlassAnimator's DWM VSync loop.
	/// Automatically bypassed when ExtremePerformanceMode, !MotionEnabled, or PreviewStaticMode is active.
	/// </summary>
	internal static class GlassFormTransition
	{
		private const uint PwRenderFullContent = 0x00000002;
		private const uint UlwAlpha = 0x00000002;
		private const int SwShowNa = 8;
		private const uint SwpNoActivate = 0x0010;
		private const uint SwpShowWindow = 0x0040;
		private static readonly IntPtr HwndTopMost = new IntPtr(-1);

		[StructLayout(LayoutKind.Sequential)]
		private struct NativePoint { public int X; public int Y; }

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeSize { public int Width; public int Height; }

		[StructLayout(LayoutKind.Sequential)]
		private struct BlendFunc
		{
			public byte BlendOp;
			public byte BlendFlags;
			public byte SourceConstantAlpha;
			public byte AlphaFormat;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct BitmapInfoHeader
		{
			public int biSize;
			public int biWidth;
			public int biHeight;
			public short biPlanes;
			public short biBitCount;
			public int biCompression;
			public int biSizeImage;
			public int biXPelsPerMeter;
			public int biYPelsPerMeter;
			public int biClrUsed;
			public int biClrImportant;
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool UpdateLayeredWindow(
			IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
			IntPtr hdcSrc, ref NativePoint pptSrc, uint crKey, ref BlendFunc pblend, uint dwFlags);

		[DllImport("user32.dll")]
		private static extern IntPtr GetDC(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern bool DeleteDC(IntPtr hdc);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern bool DeleteObject(IntPtr hObject);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

		private sealed class LayeredGhostWindow : NativeWindow, IDisposable
		{
			private readonly int width;
			private readonly int height;
			private IntPtr screenDc;
			private IntPtr memDc;
			private IntPtr hDib;
			private IntPtr prevObj;
			private Bitmap frameBitmap;
			private Graphics frameGraphics;
			private bool disposed;

			internal LayeredGhostWindow(Rectangle bounds)
			{
				width = Math.Max(4, bounds.Width);
				height = Math.Max(4, bounds.Height);

				var cp = new CreateParams
				{
					Caption = string.Empty,
					X = bounds.X,
					Y = bounds.Y,
					Width = width,
					Height = height,
					Style = unchecked((int)0x80000000), // WS_POPUP
					ExStyle = 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000 // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE
				};
				CreateHandle(cp);

				screenDc = GetDC(IntPtr.Zero);
				memDc = CreateCompatibleDC(screenDc);
				var bmi = new BitmapInfoHeader
				{
					biSize = Marshal.SizeOf(typeof(BitmapInfoHeader)),
					biWidth = width,
					biHeight = -height, // top-down DIB matching Bitmap scanlines
					biPlanes = 1,
					biBitCount = 32,
					biCompression = 0
				};
				IntPtr bits;
				hDib = CreateDIBSection(memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
				if (hDib != IntPtr.Zero && bits != IntPtr.Zero)
				{
					prevObj = SelectObject(memDc, hDib);
					frameBitmap = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
					frameGraphics = Graphics.FromImage(frameBitmap);
					frameGraphics.InterpolationMode = InterpolationMode.Bilinear;
					frameGraphics.PixelOffsetMode = PixelOffsetMode.Half;
					frameGraphics.CompositingQuality = CompositingQuality.HighSpeed;
				}
				ShowWindow(Handle, SwShowNa);
				SetWindowPos(Handle, HwndTopMost, bounds.X, bounds.Y, width, height, SwpNoActivate | SwpShowWindow);
			}

			internal bool IsReady { get { return !disposed && Handle != IntPtr.Zero && frameGraphics != null; } }

			internal void Render(Bitmap snapshot, Rectangle targetBounds, float scale, int yOffset, float opacity)
			{
				if (!IsReady || snapshot == null) return;
				frameGraphics.CompositingMode = CompositingMode.SourceCopy;
				frameGraphics.Clear(Color.Transparent);
				frameGraphics.CompositingMode = CompositingMode.SourceOver;

				float dw = width * scale;
				float dh = height * scale;
				float dx = (width - dw) * 0.5f;
				float dy = (height - dh) * 0.5f;
				frameGraphics.DrawImage(snapshot, dx, dy, dw, dh);

				var ptDst = new NativePoint { X = targetBounds.X, Y = targetBounds.Y + yOffset };
				var ptSrc = new NativePoint { X = 0, Y = 0 };
				var sz = new NativeSize { Width = width, Height = height };
				byte alpha = (byte)Math.Max(1, Math.Min(255, (int)Math.Round(opacity * 255f)));
				var blend = new BlendFunc { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
				UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref sz, memDc, ref ptSrc, 0, ref blend, UlwAlpha);
			}

			public void Dispose()
			{
				if (disposed) return;
				disposed = true;
				if (frameGraphics != null) { frameGraphics.Dispose(); frameGraphics = null; }
				if (frameBitmap != null) { frameBitmap.Dispose(); frameBitmap = null; }
				if (memDc != IntPtr.Zero)
				{
					if (prevObj != IntPtr.Zero) SelectObject(memDc, prevObj);
					DeleteDC(memDc);
					memDc = IntPtr.Zero;
				}
				if (hDib != IntPtr.Zero) { DeleteObject(hDib); hDib = IntPtr.Zero; }
				if (screenDc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, screenDc); screenDc = IntPtr.Zero; }
				if (Handle != IntPtr.Zero) { DestroyHandle(); }
			}
		}

		private static unsafe Bitmap CaptureFormSnapshot(Form form)
		{
			if (form == null || form.IsDisposed || !form.IsHandleCreated || form.Width < 16 || form.Height < 16) return null;
			int w = form.Width;
			int h = form.Height;
			var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
			bool captured = false;
			try
			{
				using (Graphics g = Graphics.FromImage(bmp))
				{
					IntPtr hdc = g.GetHdc();
					try { captured = PrintWindow(form.Handle, hdc, PwRenderFullContent); }
					finally { g.ReleaseHdc(hdc); }
				}
			}
			catch { captured = false; }

			if (!captured)
			{
				try
				{
					form.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
					captured = true;
				}
				catch { captured = false; }
			}
			if (!captured) { bmp.Dispose(); return null; }

			// PrintWindow / GDI controls leave alpha=0 on native regions: force opaque premultiplied alpha
			// with clean 8px rounded corners matching Win11 DWM chrome, and verify non-blank capture.
			System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
				System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
			bool hasNonZeroColor = false;
			try
			{
				byte* scan = (byte*)bd.Scan0;
				int stride = bd.Stride;
				const float cornerR = 8.0f;
				for (int y = 0; y < h; y++)
				{
					byte* row = scan + y * stride;
					bool nearTop = y < 9;
					bool nearBot = y >= h - 9;
					for (int x = 0; x < w; x++)
					{
						int o = x * 4;
						byte b = row[o + 0];
						byte gr = row[o + 1];
						byte r = row[o + 2];
						if (!hasNonZeroColor && (r | gr | b) != 0) hasNonZeroColor = true;

						float a = 1f;
						if ((nearTop || nearBot) && (x < 9 || x >= w - 9))
						{
							float cx = x < 9 ? cornerR : (w - 1 - cornerR);
							float cy = nearTop ? cornerR : (h - 1 - cornerR);
							float dx = x - cx;
							float dy = y - cy;
							float dist = (float)Math.Sqrt(dx * dx + dy * dy);
							if (dist > cornerR + 0.7f) a = 0f;
							else if (dist > cornerR - 0.7f) a = Math.Max(0f, Math.Min(1f, (cornerR + 0.7f - dist) / 1.4f));
						}

						if (a >= 0.995f)
						{
							row[o + 3] = 255;
						}
						else if (a <= 0.005f)
						{
							row[o + 0] = 0; row[o + 1] = 0; row[o + 2] = 0; row[o + 3] = 0;
						}
						else
						{
							row[o + 0] = (byte)(b * a + 0.5f);
							row[o + 1] = (byte)(gr * a + 0.5f);
							row[o + 2] = (byte)(r * a + 0.5f);
							row[o + 3] = (byte)(a * 255f + 0.5f);
						}
					}
				}
			}
			finally { bmp.UnlockBits(bd); }

			if (!hasNonZeroColor) { bmp.Dispose(); return null; }
			return bmp;
		}

		internal static void Attach(Form form)
		{
			if (form == null || form.IsDisposed) return;
			if (!GlassAnimator.Enabled) return;

			bool closingAnimated = false;
			bool fadingOut = false;
			Func<float, bool> animStep = null;
			LayeredGhostWindow activeGhost = null;
			Bitmap activeSnapshot = null;

			Action cleanupGhost = delegate
			{
				if (activeGhost != null) { activeGhost.Dispose(); activeGhost = null; }
				if (activeSnapshot != null) { activeSnapshot.Dispose(); activeSnapshot = null; }
			};

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
				// Warm paint once while invisible so snapshot captures fully-styled controls & glass cards
				try { form.Update(); } catch { }
				if (form.IsDisposed) return;

				Rectangle targetBounds = form.Bounds;
				Bitmap snap = CaptureFormSnapshot(form);
				if (snap != null)
				{
					LayeredGhostWindow ghost = null;
					try { ghost = new LayeredGhostWindow(targetBounds); } catch { ghost = null; }
					if (ghost != null && ghost.IsReady)
					{
						activeSnapshot = snap;
						activeGhost = ghost;
						ghost.Render(snap, targetBounds, 0.94f, 16, 0.04f);
						float t = 0f;
						animStep = delegate(float dt)
						{
							if (form.IsDisposed || fadingOut) { cleanupGhost(); return false; }
							t += dt / 0.23f;
							if (t >= 1f)
							{
								form.Opacity = 1.0;
								try { form.Activate(); } catch { }
								cleanupGhost();
								return false;
							}
							float inv = 1f - t;
							float ease = 1f - inv * inv * inv; // crisp macOS cubic-out spring feel
							float scale = 0.94f + 0.06f * ease;
							int yOff = (int)Math.Round(16f * (1f - ease));
							float op = Math.Min(1f, ease * 1.18f);
							ghost.Render(snap, targetBounds, scale, yOff, op);
							return true;
						};
						GlassAnimator.Add(animStep);
						return;
					}
					if (ghost != null) ghost.Dispose();
					snap.Dispose();
				}

				// Fallback: smooth pure DWM opacity fade if snapshot unavailable
				form.Opacity = 0.02;
				float ft = 0f;
				animStep = delegate(float dt)
				{
					if (form.IsDisposed || fadingOut) return false;
					ft += dt / 0.18f;
					if (ft >= 1f) { form.Opacity = 1.0; return false; }
					float inv = 1f - ft;
					form.Opacity = Math.Max(0.02, Math.Min(0.995, 1f - inv * inv * inv));
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
				cleanupGhost();

				Rectangle targetBounds = form.Bounds;
				Bitmap snap = CaptureFormSnapshot(form);
				if (snap != null)
				{
					LayeredGhostWindow ghost = null;
					try { ghost = new LayeredGhostWindow(targetBounds); } catch { ghost = null; }
					if (ghost != null && ghost.IsReady)
					{
						activeSnapshot = snap;
						activeGhost = ghost;
						ghost.Render(snap, targetBounds, 1.0f, 0, 1.0f);
						form.Opacity = 0.0;
						float outT = 0f;
						Func<float, bool> outStep = null;
						outStep = delegate(float dt)
						{
							if (form.IsDisposed) { cleanupGhost(); return false; }
							outT += dt / 0.145f;
							if (outT >= 1f)
							{
								cleanupGhost();
								closingAnimated = true;
								form.DialogResult = savedResult;
								if (savedResult == DialogResult.None) form.Close();
								return false;
							}
							float easeIn = outT * outT;
							float scale = 1.0f - 0.045f * easeIn;
							int yOff = (int)Math.Round(10f * easeIn);
							float op = Math.Max(0.01f, 1f - outT * (0.35f + 0.65f * outT));
							ghost.Render(snap, targetBounds, scale, yOff, op);
							return true;
						};
						GlassAnimator.Add(outStep);
						return;
					}
					if (ghost != null) ghost.Dispose();
					snap.Dispose();
				}

				float startOp = (float)form.Opacity;
				float fOutT = 0f;
				Func<float, bool> fOutStep = null;
				fOutStep = delegate(float dt)
				{
					if (form.IsDisposed) return false;
					fOutT += dt / 0.12f;
					if (fOutT >= 1f)
					{
						closingAnimated = true;
						form.Opacity = 0.0;
						form.DialogResult = savedResult;
						if (savedResult == DialogResult.None) form.Close();
						return false;
					}
					float inv = 1f - fOutT;
					form.Opacity = Math.Max(0.0, startOp * inv * inv);
					return true;
				};
				GlassAnimator.Add(fOutStep);
			};

			form.Disposed += delegate
			{
				if (animStep != null) GlassAnimator.Remove(animStep);
				cleanupGhost();
			};
		}
	}

	/// <summary>
	/// CheckedListBox that paints true transparent frosted glass rows + iOS rounded checkboxes
	/// in ThemeLiquidGlass while delegating 100% untouched to WinForms CheckedListBox in all other 4 themes.
	/// </summary>
	internal sealed class GlassCheckedListBox : CheckedListBox
	{
		private const int WmEraseBkgnd = 0x0014;

		public GlassCheckedListBox()
		{
			ItemHeight = 26;
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WmEraseBkgnd && UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && !UiTheme.ExtremePerformanceMode && !IsDisposed)
			{
				if (m.WParam != IntPtr.Zero && ClientSize.Width > 2 && ClientSize.Height > 2)
				{
					try
					{
						using (Graphics g = Graphics.FromHdc(m.WParam))
						{
							UiSurface.PaintBackdrop(this, g);
							bool bright = LiquidGlassEngine.IsBrightBackdrop();
							int va = LiquidGlassEngine.Preset == LiquidGlassEngine.PresetDeep ? 76 : (bright ? 44 : 42);
							using (var veil = new SolidBrush(Color.FromArgb(va, bright ? Color.White : Color.FromArgb(10, 14, 24))))
								g.FillRectangle(veil, ClientRectangle);
						}
						m.Result = new IntPtr(1);
						return;
					}
					catch { }
				}
			}
			base.WndProc(ref m);
		}

		protected override void OnDrawItem(DrawItemEventArgs e)
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass || UiTheme.ExtremePerformanceMode)
			{
				base.OnDrawItem(e);
				return;
			}
			if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;

			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			GraphicsState st = g.Save();
			try
			{
				g.SetClip(e.Bounds, CombineMode.Intersect);
				UiSurface.PaintBackdrop(this, g);
				bool bright = LiquidGlassEngine.IsBrightBackdrop();
				int va = LiquidGlassEngine.Preset == LiquidGlassEngine.PresetDeep ? 76 : (bright ? 44 : 42);
				using (var veil = new SolidBrush(Color.FromArgb(va, bright ? Color.White : Color.FromArgb(10, 14, 24))))
					g.FillRectangle(veil, e.Bounds);
			}
			finally { g.Restore(st); }

			if (e.Index < 0 || e.Index >= Items.Count) return;

			bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
			if (selected)
			{
				Rectangle pill = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, Math.Max(4, e.Bounds.Width - 8), Math.Max(4, e.Bounds.Height - 4));
				using (GraphicsPath p = UiTheme.RoundedRect(pill, 7))
				using (var sb = new SolidBrush(Color.FromArgb(110, 14, 132, 255)))
					g.FillPath(sb, p);
			}

			int box = 15;
			int bx = e.Bounds.X + 10;
			int by = e.Bounds.Y + Math.Max(1, (e.Bounds.Height - box) / 2);
			GlassControlStyler.DrawGlassCheckGlyph(g, new Rectangle(bx, by, box, box), GetItemChecked(e.Index));

			string text = GetItemText(Items[e.Index]) ?? string.Empty;
			Rectangle tb = new Rectangle(bx + box + 9, e.Bounds.Y, Math.Max(1, e.Bounds.Width - (box + 26)), e.Bounds.Height);
			Color ink = LiquidGlassEngine.GlassInk(Enabled);
			Color shadow = LiquidGlassEngine.GlassTextShadow();
			TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left
				| TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
			if (shadow.A > 0)
				GameText.Draw(g, text, Font, new Rectangle(tb.X + 1, tb.Y + 1, tb.Width, tb.Height), shadow, flags);
			GameText.Draw(g, text, Font, tb, ink, flags);
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
