using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AandmAuto
{
	internal sealed class WallpaperEnginePlayback : IDisposable
	{
		private const uint RenderClient = 3;
		private const uint NoZOrderOrActivate = 0x0014;
		private const int OffScreen = -30000;

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr FindWindow(string className, string title);
		[DllImport("user32.dll")]
		private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
		[DllImport("user32.dll")]
		private static extern bool PostMessage(IntPtr window, uint command, IntPtr wparam, IntPtr lparam);
		[DllImport("user32.dll")]
		private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
		[DllImport("gdi32.dll")]
		private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
		[DllImport("gdi32.dll")]
		private static extern bool DeleteDC(IntPtr hdc);
		[DllImport("gdi32.dll")]
		private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
		[DllImport("gdi32.dll")]
		private static extern bool DeleteObject(IntPtr obj);
		[DllImport("gdi32.dll")]
		private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
		[DllImport("winmm.dll")]
		private static extern uint timeBeginPeriod(uint period);
		[DllImport("winmm.dll")]
		private static extern uint timeEndPeriod(uint period);

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeRect
		{
			public int Left, Top, Right, Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct BITMAPINFO
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

		internal sealed class FrameSlot : IDisposable
		{
			internal readonly int Width;
			internal readonly int Height;
			internal readonly int CaptureWidth;
			internal readonly int CaptureHeight;
			internal readonly IntPtr Hdc;
			internal readonly IntPtr Dib;
			internal readonly IntPtr OldObj;
			internal readonly IntPtr CaptureBits;
			internal readonly IntPtr RawBits;
			internal readonly IntPtr VeiledBits;
			internal readonly Bitmap RawBitmap;
			internal readonly Bitmap VeiledBitmap;

			internal long Sequence;
			internal double CaptureMs;
			internal double WorkerMs;
			internal Color VeilColor;
			internal int VeilAlpha;
			internal Bitmap GlassBitmap;
			internal float[] LuminanceGrid;
			internal int State; // 0=Free, 1=Writing, 2=Ready, 3=Reading

			internal FrameSlot(int captureWidth, int captureHeight, int outputWidth, int outputHeight)
			{
				CaptureWidth = captureWidth;
				CaptureHeight = captureHeight;
				Width = outputWidth;
				Height = outputHeight;
				var bmi = new BITMAPINFO
				{
					biSize = 40,
					biWidth = captureWidth,
					biHeight = -captureHeight,
					biPlanes = 1,
					biBitCount = 32
				};
				Hdc = CreateCompatibleDC(IntPtr.Zero);
				Dib = CreateDIBSection(Hdc, ref bmi, 0, out CaptureBits, IntPtr.Zero, 0);
				OldObj = SelectObject(Hdc, Dib);
				RawBits = Marshal.AllocHGlobal(outputWidth * outputHeight * 4);
				VeiledBits = Marshal.AllocHGlobal(outputWidth * outputHeight * 4);
				RawBitmap = new Bitmap(outputWidth, outputHeight, outputWidth * 4, PixelFormat.Format32bppPArgb, RawBits);
				VeiledBitmap = new Bitmap(outputWidth, outputHeight, outputWidth * 4, PixelFormat.Format32bppPArgb, VeiledBits);
			}

			internal void ReleaseGlass()
			{
				if (GlassBitmap != null)
				{
					GlassBitmap.Dispose();
					GlassBitmap = null;
				}
				LuminanceGrid = null;
			}

			public void Dispose()
			{
				ReleaseGlass();
				RawBitmap.Dispose();
				VeiledBitmap.Dispose();
				Marshal.FreeHGlobal(RawBits);
				Marshal.FreeHGlobal(VeiledBits);
				SelectObject(Hdc, OldObj);
				DeleteObject(Dib);
				DeleteDC(Hdc);
			}
		}

		private readonly string executable;
		private readonly string title = "AandmWallpaper-" + Guid.NewGuid().ToString("N");
		private Size renderSize;
		private Size desiredSize;
		private IntPtr positionedWindow;
		private volatile bool warmedUp;
		private volatile bool disposed;
		private volatile bool paused;

		private readonly object stateLock = new object();
		private FrameSlot[] slots;
		private Size slotsSize;
		private FrameSlot latestReadySlot;
		private FrameSlot currentUiSlot;
		private long nextSequence;

		private Color desiredVeilColor;
		private int desiredVeilAlpha;
		private bool desiredPrepareGlass;
		private int desiredBlurLevel;

		private Thread[] pipelineThreads;
		private long lastCaptureStartMs = -100;
		private Stopwatch pipelineClock;
		private DateTime nextGlassBuildUtc = DateTime.MinValue;
		private readonly object startGate = new object();
		private readonly object windowPosLock = new object();
		private bool timerPeriodActive;

		private FrameSlot syncSlot;
		private Bitmap spareFrame;
		private readonly object frameLock = new object();

		internal event Action FrameReady;

		internal WallpaperEnginePlayback(string executable, string source, Size size)
		{
			this.executable = executable;
			desiredSize = size;
			// A unique title lets this instance close only its own Wallpaper Engine window.
			SendCommand(string.Format(CultureInfo.InvariantCulture,
				"openWallpaper -file \"{0}\" -playInWindow {1} -width {2} -height {3} -x {4} -y {4} -borderless",
				source, title, size.Width, size.Height, OffScreen));
		}

		internal void StartPipeline(Size targetSize, Color veilColor, int veilAlpha, bool prepareGlass, int blurLevel)
		{
			UpdatePipelineConfig(targetSize, veilColor, veilAlpha, prepareGlass, blurLevel);
			lock (stateLock)
			{
				if (disposed || pipelineThreads != null) return;
				pipelineClock = Stopwatch.StartNew();
				try
				{
					timeBeginPeriod(1);
					timerPeriodActive = true;
				}
				catch { }
				pipelineThreads = new Thread[2];
				for (int i = 0; i < 2; i++)
				{
					int workerId = i;
					var t = new Thread(PipelineWorkerLoop) { IsBackground = true, Name = "AandmWallpaperCap-" + workerId };
					pipelineThreads[i] = t;
					t.Start(workerId);
				}
			}
		}

		internal void PausePipeline()
		{
			paused = true;
		}

		internal void UpdatePipelineConfig(Size targetSize, Color veilColor, int veilAlpha, bool prepareGlass, int blurLevel)
		{
			lock (stateLock)
			{
				paused = false;
				desiredSize = targetSize;
				desiredVeilColor = veilColor;
				desiredVeilAlpha = veilAlpha;
				desiredPrepareGlass = prepareGlass;
				desiredBlurLevel = blurLevel;
			}
		}

		internal FrameSlot AcquireLatestSlot()
		{
			lock (stateLock)
			{
				if (latestReadySlot == null) return null;
				if (currentUiSlot != null && currentUiSlot != latestReadySlot)
				{
					currentUiSlot.ReleaseGlass();
					currentUiSlot.State = 0;
				}
				currentUiSlot = latestReadySlot;
				currentUiSlot.State = 3;
				latestReadySlot = null;
				return currentUiSlot;
			}
		}

		private IntPtr EnsureTargetWindow(Size size, out int width, out int height)
		{
			width = 0;
			height = 0;
			IntPtr window = FindWindow(null, title);
			if (window == IntPtr.Zero) return IntPtr.Zero;
			lock (windowPosLock)
			{
				if (window != positionedWindow || size != renderSize)
				{
					if (!SetWindowPos(window, IntPtr.Zero, OffScreen, OffScreen,
						size.Width, size.Height, NoZOrderOrActivate)) return IntPtr.Zero;
					positionedWindow = window;
					renderSize = size;
				}
			}
			NativeRect rect;
			if (!GetClientRect(window, out rect)) return IntPtr.Zero;
			width = rect.Right - rect.Left;
			height = rect.Bottom - rect.Top;
			if (width < 16 || height < 16 || width > 8192 || height > 8192) return IntPtr.Zero;
			return window;
		}

		private FrameSlot CheckoutFreeSlot(int captureWidth, int captureHeight, int outputWidth, int outputHeight)
		{
			lock (stateLock)
			{
				if (disposed) return null;
				if (slots == null || slotsSize.Width != outputWidth || slotsSize.Height != outputHeight
					|| slots[0].CaptureWidth != captureWidth || slots[0].CaptureHeight != captureHeight)
				{
					if (slots != null)
					{
						for (int i = 0; i < slots.Length; i++)
						{
							if (slots[i] != currentUiSlot && slots[i] != null) slots[i].Dispose();
						}
					}
					slots = new FrameSlot[4];
					for (int i = 0; i < 4; i++) slots[i] = new FrameSlot(captureWidth, captureHeight, outputWidth, outputHeight);
					slotsSize = new Size(outputWidth, outputHeight);
					latestReadySlot = null;
				}
				for (int i = 0; i < slots.Length; i++)
				{
					if (slots[i].State == 0)
					{
						slots[i].State = 1;
						slots[i].ReleaseGlass();
						return slots[i];
					}
				}
				if (latestReadySlot != null && latestReadySlot.State == 2)
				{
					FrameSlot reuse = latestReadySlot;
					latestReadySlot = null;
					reuse.State = 1;
					reuse.ReleaseGlass();
					return reuse;
				}
				return null;
			}
		}

		private static Size ChooseCaptureSize(Size target)
		{
			const int maxEdge = 1280;
			int longEdge = Math.Max(target.Width, target.Height);
			if (longEdge <= maxEdge) return target;
			double sc = maxEdge / (double)longEdge;
			return new Size(Math.Max(16, (int)Math.Round(target.Width * sc)),
				Math.Max(16, (int)Math.Round(target.Height * sc)));
		}

		private void PipelineWorkerLoop(object state)
		{
			int workerId = state is int ? (int)state : 0;
			try
			{
				RunPipelineWorker(workerId);
			}
			catch { }
		}

		private void RunPipelineWorker(int workerId)
		{
			while (!disposed)
			{
				if (paused)
				{
					Thread.Sleep(50);
					continue;
				}
				if (!warmedUp && workerId != 0)
				{
					Thread.Sleep(15);
					continue;
				}
				Size size;
				Color veilColor;
				int veilAlpha;
				bool prepareGlass;
				int blurLevel;
				lock (stateLock)
				{
					size = desiredSize;
					veilColor = desiredVeilColor;
					veilAlpha = desiredVeilAlpha;
					prepareGlass = desiredPrepareGlass;
					blurLevel = desiredBlurLevel;
				}

				int wait = 0;
				lock (startGate)
				{
					long now = pipelineClock.ElapsedMilliseconds;
					long gap = now - lastCaptureStartMs;
					int minGap = warmedUp ? 13 : 25;
					if (gap < minGap)
					{
						wait = (int)(minGap - gap);
						lastCaptureStartMs = now + wait;
					}
					else
					{
						lastCaptureStartMs = now;
					}
				}
				if (wait > 0) Thread.Sleep(wait);
				if (disposed) break;

				bool firstPosition = positionedWindow == IntPtr.Zero;
				Size captureTarget = ChooseCaptureSize(size);
				int capW, capH;
				IntPtr window = EnsureTargetWindow(captureTarget, out capW, out capH);
				if (window == IntPtr.Zero)
				{
					Thread.Sleep(30);
					continue;
				}
				if (firstPosition)
				{
					Thread.Sleep(60);
				}

				FrameSlot slot = CheckoutFreeSlot(capW, capH, size.Width, size.Height);
				if (slot == null)
				{
					Thread.Sleep(4);
					continue;
				}

				double t0 = pipelineClock.Elapsed.TotalMilliseconds;
				bool captured = PrintWindow(window, slot.Hdc, RenderClient);
				double tCap = pipelineClock.Elapsed.TotalMilliseconds;
				bool valid = false;
				if (captured)
				{
					bool hasColor = ProcessSlotPixels(slot.CaptureBits, capW, capH, slot.RawBits, slot.VeiledBits,
						size.Width, size.Height, veilColor, veilAlpha);
					if (warmedUp || hasColor)
					{
						warmedUp = true;
						valid = true;
					}
				}
				if (!valid || disposed)
				{
					lock (stateLock)
					{
						if (slot.Width == slotsSize.Width && slot.Height == slotsSize.Height) slot.State = 0;
						else slot.Dispose();
					}
					if (!captured) Thread.Sleep(25);
					continue;
				}

				bool needGlass = false;
				if (prepareGlass && DateTime.UtcNow >= nextGlassBuildUtc)
				{
					lock (stateLock)
					{
						if (DateTime.UtcNow >= nextGlassBuildUtc)
						{
							nextGlassBuildUtc = DateTime.UtcNow.AddMilliseconds(300);
							needGlass = true;
						}
					}
				}
				if (needGlass)
				{
					float[] lumGrid;
					using (var tmpRaw = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, slot.RawBits))
					{
						slot.GlassBitmap = UiTheme.CreateBlurredBitmap(tmpRaw, blurLevel, blurLevel > 0 ? 256 : 0, out lumGrid);
					}
					slot.LuminanceGrid = lumGrid;
				}

				double tEnd = pipelineClock.Elapsed.TotalMilliseconds;
				slot.CaptureMs = tCap - t0;
				slot.WorkerMs = tEnd - t0;
				slot.VeilColor = veilColor;
				slot.VeilAlpha = veilAlpha;

				bool notify = false;
				lock (stateLock)
				{
					if (disposed || slot.Width != slotsSize.Width || slot.Height != slotsSize.Height)
					{
						slot.Dispose();
					}
					else
					{
						if (latestReadySlot != null && latestReadySlot != slot)
						{
							if (latestReadySlot.GlassBitmap != null && slot.GlassBitmap == null)
							{
								slot.GlassBitmap = latestReadySlot.GlassBitmap;
								slot.LuminanceGrid = latestReadySlot.LuminanceGrid;
								latestReadySlot.GlassBitmap = null;
								latestReadySlot.LuminanceGrid = null;
							}
							latestReadySlot.ReleaseGlass();
							latestReadySlot.State = 0;
						}
						slot.Sequence = ++nextSequence;
						slot.State = 2;
						latestReadySlot = slot;
						notify = true;
					}
				}
				if (notify)
				{
					Action cb = FrameReady;
					if (cb != null) cb();
				}
			}
		}

		private static unsafe bool ProcessSlotPixels(IntPtr captureBits, int capW, int capH,
			IntPtr rawBits, IntPtr veiledBits, int outW, int outH, Color veilColor, int veilAlpha)
		{
			int inv = 256 - veilAlpha;
			uint addR = (uint)(veilColor.R * veilAlpha);
			uint addG = (uint)(veilColor.G * veilAlpha);
			uint addB = (uint)(veilColor.B * veilAlpha);
			int nonZero = 0;
			if (capW == outW && capH == outH)
			{
				int total = outW * outH;
				Parallel.For(0, 4, band =>
				{
					int start = (total * band) / 4;
					int end = (total * (band + 1)) / 4;
					uint* src = (uint*)captureBits;
					uint* raw = (uint*)rawBits;
					uint* dst = (uint*)veiledBits;
					uint localOr = 0;
					for (int i = start; i < end; i++)
					{
						uint c = src[i];
						localOr |= (c & 0x00FFFFFFu);
						raw[i] = c | 0xFF000000u;
						uint b = (((c & 0xFFu) * (uint)inv + addB) >> 8);
						uint g = ((((c >> 8) & 0xFFu) * (uint)inv + addG) >> 8);
						uint r = ((((c >> 16) & 0xFFu) * (uint)inv + addR) >> 8);
						dst[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
					}
					if (localOr != 0) Interlocked.Increment(ref nonZero);
				});
				return nonZero > 0;
			}
			int stepX = (capW << 16) / outW;
			int stepY = (capH << 16) / outH;
			Parallel.For(0, 4, band =>
			{
				int yStart = (outH * band) / 4;
				int yEnd = (outH * (band + 1)) / 4;
				uint* src = (uint*)captureBits;
				uint* raw = (uint*)rawBits;
				uint* dst = (uint*)veiledBits;
				uint localOr = 0;
				for (int y = yStart; y < yEnd; y++)
				{
					int sy = (y * stepY) >> 16;
					if (sy >= capH) sy = capH - 1;
					uint* sRow = src + sy * capW;
					int rowOff = y * outW;
					uint* rRow = raw + rowOff;
					uint* dRow = dst + rowOff;
					int sxFp = 0;
					for (int x = 0; x < outW; x++)
					{
						int sx = sxFp >> 16;
						if (sx >= capW) sx = capW - 1;
						uint c = sRow[sx];
						sxFp += stepX;
						localOr |= (c & 0x00FFFFFFu);
						rRow[x] = c | 0xFF000000u;
						uint b = (((c & 0xFFu) * (uint)inv + addB) >> 8);
						uint g = ((((c >> 8) & 0xFFu) * (uint)inv + addG) >> 8);
						uint r = ((((c >> 16) & 0xFFu) * (uint)inv + addR) >> 8);
						dRow[x] = 0xFF000000u | (r << 16) | (g << 8) | b;
					}
				}
				if (localOr != 0) Interlocked.Increment(ref nonZero);
			});
			return nonZero > 0;
		}

		internal void RecycleFrame(Bitmap frame)
		{
			lock (frameLock)
			{
				if (spareFrame != null) spareFrame.Dispose();
				spareFrame = frame;
			}
		}

		internal Bitmap Capture(Size size)
		{
			int width, height;
			IntPtr window = EnsureTargetWindow(size, out width, out height);
			if (window == IntPtr.Zero) return null;

			if (syncSlot == null || syncSlot.CaptureWidth != width || syncSlot.CaptureHeight != height)
			{
				if (syncSlot != null) syncSlot.Dispose();
				syncSlot = new FrameSlot(width, height, width, height);
			}
			if (!PrintWindow(window, syncSlot.Hdc, RenderClient)) return null;

			Bitmap frame;
			lock (frameLock)
			{
				frame = spareFrame;
				spareFrame = null;
			}
			if (frame != null && (frame.Width != width || frame.Height != height))
			{
				frame.Dispose();
				frame = null;
			}
			if (frame == null) frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
			bool hasColor = CopyRawToBitmap(syncSlot.CaptureBits, frame, width, height);
			if (!warmedUp && !hasColor)
			{
				RecycleFrame(frame);
				return null;
			}
			warmedUp = true;
			return frame;
		}

		private static unsafe bool CopyRawToBitmap(IntPtr rawBits, Bitmap dstBmp, int width, int height)
		{
			BitmapData data = dstBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
			uint nonZero = 0;
			try
			{
				int strideInts = data.Stride / 4;
				uint* src = (uint*)rawBits;
				uint* dst = (uint*)data.Scan0;
				for (int y = 0; y < height; y++)
				{
					uint* sRow = src + y * width;
					uint* dRow = dst + y * strideInts;
					for (int x = 0; x < width; x++)
					{
						uint c = sRow[x];
						nonZero |= (c & 0x00FFFFFFu);
						dRow[x] = c | 0xFF000000u;
					}
				}
			}
			finally
			{
				dstBmp.UnlockBits(data);
			}
			return nonZero != 0;
		}

		private void SendCommand(string command)
		{
			using (Process.Start(new ProcessStartInfo
			{
				FileName = executable,
				WorkingDirectory = Path.GetDirectoryName(executable),
				Arguments = "-control " + command,
				UseShellExecute = false,
				CreateNoWindow = true
			})) { }
		}

		private void CloseNamedWindow()
		{
			IntPtr window = FindWindow(null, title);
			if (window != IntPtr.Zero) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
		}

		public void Dispose()
		{
			disposed = true;
			Thread[] threads = pipelineThreads;
			if (threads != null)
			{
				for (int i = 0; i < threads.Length; i++)
				{
					try { if (threads[i] != null) threads[i].Join(1200); }
					catch { }
				}
			}
			if (timerPeriodActive)
			{
				try { timeEndPeriod(1); }
				catch { }
				timerPeriodActive = false;
			}
			lock (stateLock)
			{
				if (slots != null)
				{
					for (int i = 0; i < slots.Length; i++)
					{
						if (slots[i] != null) slots[i].Dispose();
					}
					slots = null;
				}
				currentUiSlot = null;
				latestReadySlot = null;
			}
			if (syncSlot != null)
			{
				syncSlot.Dispose();
				syncSlot = null;
			}
			RecycleFrame(null);
			try { SendCommand("closeWallpaper -location " + title); }
			catch (System.ComponentModel.Win32Exception) { CloseNamedWindow(); }
			catch (InvalidOperationException) { CloseNamedWindow(); }
		}
	}
}
