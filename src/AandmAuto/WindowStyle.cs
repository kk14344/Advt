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
