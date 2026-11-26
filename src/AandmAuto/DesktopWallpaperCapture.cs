using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AandmAuto
{
	internal sealed class DesktopWallpaperCapture : IDisposable
	{
		private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		private const uint PW_RENDERFULLCONTENT = 0x00000002;
		private const uint SMTO_NORMAL = 0x0000;
		private const uint SPI_GETDESKWALLPAPER = 0x0073;

		[DllImport("user32.dll")]
		private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		[DllImport("user32.dll")]
		private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

		[DllImport("user32.dll")]
		private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

		private Bitmap windowBuffer;
		private Bitmap regionBuffer;
		private RECT lastHostRect;
		private bool hasWindowCapture;
		private Image staticDesktopCache;
		private string staticDesktopCachePath = "";

		internal static IntPtr FindWallpaperHostWindow()
		{
			try
			{
				IntPtr progman = FindWindow("Progman", null);
				if (progman != IntPtr.Zero)
				{
					IntPtr res;
					SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), IntPtr.Zero, SMTO_NORMAL, 200, out res);
					SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), SMTO_NORMAL, 200, out res);
				}

				IntPtr workerW = IntPtr.Zero;
				EnumWindows(delegate(IntPtr topHandle, IntPtr lParam)
				{
					IntPtr shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
					if (shellView != IntPtr.Zero)
					{
						workerW = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
						if (workerW == IntPtr.Zero)
						{
							// Windows 11 raised desktop splits may put WorkerW inside Progman directly
							IntPtr p = FindWindow("Progman", null);
							if (p != IntPtr.Zero)
							{
								IntPtr childWorker = FindWindowEx(p, IntPtr.Zero, "WorkerW", null);
								if (childWorker != IntPtr.Zero && IsWindowVisible(childWorker)) workerW = childWorker;
							}
						}
						return false;
					}
					return true;
				}, IntPtr.Zero);

				if (workerW != IntPtr.Zero && IsWindowVisible(workerW)) return workerW;
				if (progman != IntPtr.Zero && IsWindowVisible(progman)) return progman;
			}
			catch { }
			return IntPtr.Zero;
		}

		internal Bitmap CaptureSidebarRegion(Rectangle sidebarScreenRect)
		{
			if (sidebarScreenRect.Width < 4 || sidebarScreenRect.Height < 4) return null;

			try
			{
				IntPtr hwnd = FindWallpaperHostWindow();
				RECT hostRect;
				if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out hostRect))
				{
					int hostW = hostRect.Right - hostRect.Left;
					int hostH = hostRect.Bottom - hostRect.Top;
					if (hostW > 64 && hostH > 64)
					{
						EnsureWindowBuffer(hostW, hostH);
						bool captured = false;
						using (Graphics wg = Graphics.FromImage(windowBuffer))
						{
							IntPtr hdc = wg.GetHdc();
							try
							{
								captured = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
								if (!captured) captured = PrintWindow(hwnd, hdc, 0);
							}
							finally
							{
								wg.ReleaseHdc(hdc);
							}
						}

						if (captured && !LooksAllBlack(windowBuffer))
						{
							lastHostRect = hostRect;
							hasWindowCapture = true;
							EnsureRegionBuffer(sidebarScreenRect.Width, sidebarScreenRect.Height);
							Rectangle srcRect = new Rectangle(
								sidebarScreenRect.X - hostRect.Left,
								sidebarScreenRect.Y - hostRect.Top,
								sidebarScreenRect.Width,
								sidebarScreenRect.Height);
							using (Graphics rg = Graphics.FromImage(regionBuffer))
							{
								rg.Clear(UiTheme.Palette.SideOuterColor);
								rg.InterpolationMode = InterpolationMode.Bilinear;
								rg.DrawImage(windowBuffer, new Rectangle(0, 0, regionBuffer.Width, regionBuffer.Height), srcRect, GraphicsUnit.Pixel);
							}
							return new Bitmap(regionBuffer);
						}
					}
				}
			}
			catch { }

			hasWindowCapture = false;
			return CaptureStaticWindowsWallpaperFallback(sidebarScreenRect);
		}

		internal string CaptureRegionJpegDataUri(Rectangle screenRect, int maxWidth)
		{
			if (screenRect.Width < 16 || screenRect.Height < 16) return null;
			try
			{
				Bitmap raw = null;
				if (hasWindowCapture && windowBuffer != null)
				{
					int dw = Math.Max(16, Math.Min(maxWidth, screenRect.Width));
					int dh = Math.Max(16, (int)Math.Round(dw * (double)screenRect.Height / screenRect.Width));
					raw = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
					Rectangle srcRect = new Rectangle(
						screenRect.X - lastHostRect.Left,
						screenRect.Y - lastHostRect.Top,
						screenRect.Width,
						screenRect.Height);
					using (Graphics g = Graphics.FromImage(raw))
					{
						g.Clear(UiTheme.Palette.BgColor);
						g.InterpolationMode = InterpolationMode.Bilinear;
						g.DrawImage(windowBuffer, new Rectangle(0, 0, dw, dh), srcRect, GraphicsUnit.Pixel);
					}
				}
				else
				{
					using (Bitmap fb = CaptureStaticWindowsWallpaperFallback(screenRect))
					{
						if (fb != null)
						{
							int dw = Math.Max(16, Math.Min(maxWidth, fb.Width));
							int dh = Math.Max(16, (int)Math.Round(dw * (double)fb.Height / fb.Width));
							raw = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
							using (Graphics g = Graphics.FromImage(raw))
							{
								g.InterpolationMode = InterpolationMode.Bilinear;
								g.DrawImage(fb, 0, 0, dw, dh);
							}
						}
					}
				}
				if (raw == null) return null;
				using (raw)
				using (var ms = new MemoryStream())
				{
					ImageCodecInfo jpg = FindJpegCodec();
					if (jpg != null)
					{
						using (var ep = new EncoderParameters(1))
						{
							long quality = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? 88L : 70L;
							ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
							raw.Save(ms, jpg, ep);
						}
					}
					else
					{
						raw.Save(ms, ImageFormat.Jpeg);
					}
					return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
				}
			}
			catch
			{
				return null;
			}
		}

		private static ImageCodecInfo FindJpegCodec()
		{
			ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
			for (int i = 0; i < codecs.Length; i++)
			{
				if (codecs[i].FormatID == ImageFormat.Jpeg.Guid) return codecs[i];
			}
			return null;
		}

		private Bitmap CaptureStaticWindowsWallpaperFallback(Rectangle sidebarScreenRect)
		{
			try
			{
				var sb = new StringBuilder(520);
				if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0)) return null;
				string wallPath = sb.ToString().Trim();
				if (string.IsNullOrEmpty(wallPath) || !File.Exists(wallPath)) return null;

				if (staticDesktopCache == null || !string.Equals(staticDesktopCachePath, wallPath, StringComparison.OrdinalIgnoreCase))
				{
					if (staticDesktopCache != null) { staticDesktopCache.Dispose(); staticDesktopCache = null; }
					byte[] bytes = File.ReadAllBytes(wallPath);
					using (var ms = new MemoryStream(bytes))
					using (var img = Image.FromStream(ms))
					{
						staticDesktopCache = new Bitmap(img);
						staticDesktopCachePath = wallPath;
					}
				}

				if (staticDesktopCache == null) return null;
				Rectangle screen = System.Windows.Forms.Screen.FromRectangle(sidebarScreenRect).Bounds;
				float scale = Math.Max((float)screen.Width / staticDesktopCache.Width, (float)screen.Height / staticDesktopCache.Height);
				int dw = Math.Max(1, (int)Math.Ceiling(staticDesktopCache.Width * scale));
				int dh = Math.Max(1, (int)Math.Ceiling(staticDesktopCache.Height * scale));
				int dx = screen.X + (screen.Width - dw) / 2 - sidebarScreenRect.X;
				int dy = screen.Y + (screen.Height - dh) / 2 - sidebarScreenRect.Y;

				var bmp = new Bitmap(sidebarScreenRect.Width, sidebarScreenRect.Height, PixelFormat.Format32bppArgb);
				using (Graphics g = Graphics.FromImage(bmp))
				{
					g.Clear(UiTheme.Palette.SideOuterColor);
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.DrawImage(staticDesktopCache, new Rectangle(dx, dy, dw, dh));
				}
				return bmp;
			}
			catch
			{
				return null;
			}
		}

		private static bool LooksAllBlack(Bitmap bmp)
		{
			if (bmp == null || bmp.Width < 16 || bmp.Height < 16) return true;
			int[,] probes =
			{
				{ bmp.Width / 6, bmp.Height / 4 },
				{ bmp.Width / 2, bmp.Height / 2 },
				{ bmp.Width * 5 / 6, bmp.Height / 4 },
				{ bmp.Width / 4, bmp.Height * 3 / 4 },
				{ bmp.Width * 3 / 4, bmp.Height * 3 / 4 }
			};
			for (int i = 0; i < probes.GetLength(0); i++)
			{
				Color c = bmp.GetPixel(probes[i, 0], probes[i, 1]);
				if (c.R > 6 || c.G > 6 || c.B > 6) return false;
			}
			return true;
		}

		private void EnsureWindowBuffer(int w, int h)
		{
			if (windowBuffer == null || windowBuffer.Width != w || windowBuffer.Height != h)
			{
				if (windowBuffer != null) windowBuffer.Dispose();
				windowBuffer = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			}
		}

		private void EnsureRegionBuffer(int w, int h)
		{
			if (regionBuffer == null || regionBuffer.Width != w || regionBuffer.Height != h)
			{
				if (regionBuffer != null) regionBuffer.Dispose();
				regionBuffer = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			}
		}

		public void Dispose()
		{
			if (windowBuffer != null) { windowBuffer.Dispose(); windowBuffer = null; }
			if (regionBuffer != null) { regionBuffer.Dispose(); regionBuffer = null; }
			if (staticDesktopCache != null) { staticDesktopCache.Dispose(); staticDesktopCache = null; }
		}
	}
}
