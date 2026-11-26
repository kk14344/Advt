using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace AandmAuto
{
	internal static class WindowStyle
	{
		private const int GWL_EXSTYLE = -20;
		private const int WS_EX_TOOLWINDOW = 0x00000080;
		private const int WS_EX_APPWINDOW = 0x00040000;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_FRAMECHANGED = 0x0020;
        [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr handle,uint flags);
        public static bool IsForeground(IntPtr handle){return GetAncestor(GetForegroundWindow(),2)==handle;}

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
		private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeRect { public int Left, Top, Right, Bottom; }
		[DllImport("user32.dll")]
		private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
		[DllImport("user32.dll")]
		private static extern bool ClientToScreen(IntPtr window, ref Point point);

		internal static Rectangle ClientAreaOnScreen(IntPtr window)
		{
			NativeRect rect;
			Point origin = Point.Empty;
			if (!GetClientRect(window, out rect) || !ClientToScreen(window, ref origin)) return Rectangle.Empty;
			return new Rectangle(origin, new Size(rect.Right - rect.Left, rect.Bottom - rect.Top));
		}

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr handle,IntPtr updateRect,IntPtr updateRegion,uint flags);

        public static void RedrawAll(IntPtr handle)
        {
            if(handle!=IntPtr.Zero) RedrawWindow(handle,IntPtr.Zero,IntPtr.Zero,0x0001|0x0004|0x0080|0x0100);
        }
        internal static void PaintWallpaperFrame(System.Windows.Forms.Control control, bool children)
        {
            if (control == null || !control.IsHandleCreated || !control.Visible) return;
            const uint invalidate = 0x0001, noErase = 0x0020, noChildren = 0x0040, allChildren = 0x0080, updateNow = 0x0100;
            RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero,
                invalidate | noErase | updateNow | (children ? allChildren : noChildren));
        }

        [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
        internal static void SetRedraw(IntPtr handle,bool enabled){SendMessage(handle,0x000B,enabled?new IntPtr(1):IntPtr.Zero,IntPtr.Zero);}

		[DllImport("user32.dll", EntryPoint = "SendMessageW")]
		private static extern IntPtr SendRichEditMessage(IntPtr handle, uint message, IntPtr wParam, ref Point position);

		internal static Point GetRichEditScroll(IntPtr handle)
		{
			Point position = Point.Empty;
			SendRichEditMessage(handle, 0x04DD, IntPtr.Zero, ref position);
			return position;
		}

		internal static void SetRichEditScroll(IntPtr handle, Point position)
		{
			SendRichEditMessage(handle, 0x04DE, IntPtr.Zero, ref position);
		}


	    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList { }

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr window);
        void DeleteTab(IntPtr window);
        void ActivateTab(IntPtr window);
        void SetActiveAlt(IntPtr window);
    }

    internal static void HidePlaybackTaskbarWindow(IntPtr window)
    {
        // The renderer remains visible and keeps drawing off screen.
        SetTaskbarVisible(window, false);
        ITaskbarList taskbar = null;
        try
        {
            taskbar = (ITaskbarList)new TaskbarList();
            taskbar.HrInit();
            taskbar.DeleteTab(window);
        }
        catch (COMException) { }
        finally { if (taskbar != null) Marshal.ReleaseComObject(taskbar); }
    }

    internal static bool IsPlaybackToolWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        long style = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
        return (style & WS_EX_TOOLWINDOW) != 0 && (style & WS_EX_APPWINDOW) == 0;
    }

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

		/// <summary>
		/// Applies native Windows 10/11 DWM immersive dark mode, rounded corners, and caption/border tint
		/// so the window chrome blends seamlessly with Liquid Glass and dark themes without fragile borderless hacks.
		/// Silently degrades on older Windows versions.
		/// </summary>
		internal static void ApplyThemeChrome(System.Windows.Forms.Form form)
		{
			if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
			try
			{
				bool dark = !UiTheme.ExtremePerformanceMode
					&& (UiTheme.CurrentTheme == UiTheme.ThemeClassic
						|| (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && !LiquidGlassEngine.IsBrightBackdrop()));
				int useDark = dark ? 1 : 0;
				if (DwmSetWindowAttribute(form.Handle, 20, ref useDark, sizeof(int)) != 0)
				{
					DwmSetWindowAttribute(form.Handle, 19, ref useDark, sizeof(int));
				}

				// Win11 rounded window corners (DWMWCP_ROUND = 2)
				int roundPref = 2;
				DwmSetWindowAttribute(form.Handle, 33, ref roundPref, sizeof(int));

				// Win11 caption & border color (COLORREF = 0x00BBGGRR; 0xFFFFFFFF = system default)
				int captionColor = unchecked((int)0xFFFFFFFF);
				int borderColor = unchecked((int)0xFFFFFFFF);
				if (!UiTheme.ExtremePerformanceMode && UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
				{
					bool bright = LiquidGlassEngine.IsBrightBackdrop();
					Color cap = bright ? Color.FromArgb(228, 238, 248) : Color.FromArgb(16, 24, 38);
					Color bdr = bright ? Color.FromArgb(182, 204, 224) : Color.FromArgb(52, 74, 104);
					captionColor = (cap.B << 16) | (cap.G << 8) | cap.R;
					borderColor = (bdr.B << 16) | (bdr.G << 8) | bdr.R;
				}
				else if (!UiTheme.ExtremePerformanceMode && UiTheme.CurrentTheme == UiTheme.ThemeClassic)
				{
					Color cap = Color.FromArgb(46, 37, 22);
					captionColor = (cap.B << 16) | (cap.G << 8) | cap.R;
				}
				DwmSetWindowAttribute(form.Handle, 35, ref captionColor, sizeof(int));
				DwmSetWindowAttribute(form.Handle, 34, ref borderColor, sizeof(int));
			}
			catch
			{
			}
		}

		public static void SetTaskbarVisible(IntPtr handle, bool visible)
		{
			if (handle == IntPtr.Zero) return;
			try
			{
				long style = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
				if (visible)
				{
					style = (style | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW;
				}
				else
				{
					style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
				}
				SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style));
				SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
					SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
			}
			catch
			{
			}
		}
	}
}
