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
            internal readonly int[] ScaleColumns;
            internal readonly int[] ScaleRows;

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
                if (captureWidth != outputWidth || captureHeight != outputHeight)
                {
                    ScaleColumns = BuildScaleMap(captureWidth, outputWidth);
                    ScaleRows = BuildScaleMap(captureHeight, outputHeight);
                }
				var bmi = new BITMAPINFO
				{
					biSize = 40,
					biWidth = captureWidth,
					biHeight = -captureHeight,
					biPlanes = 1,
					biBitCount = 32
				};
                try
                {
                    Hdc = CreateCompatibleDC(IntPtr.Zero);
                    if (Hdc == IntPtr.Zero) throw new InvalidOperationException("无法创建采集设备");
                    Dib = CreateDIBSection(Hdc, ref bmi, 0, out CaptureBits, IntPtr.Zero, 0);
                    if (Dib == IntPtr.Zero || CaptureBits == IntPtr.Zero)
                        throw new InvalidOperationException("无法分配壁纸采集缓冲区");
                    OldObj = SelectObject(Hdc, Dib);
                    RawBits = Marshal.AllocHGlobal(outputWidth * outputHeight * 4);
                    VeiledBits = Marshal.AllocHGlobal(outputWidth * outputHeight * 4);
                    RawBitmap = new Bitmap(outputWidth, outputHeight, outputWidth * 4, PixelFormat.Format32bppPArgb, RawBits);
                    VeiledBitmap = new Bitmap(outputWidth, outputHeight, outputWidth * 4, PixelFormat.Format32bppPArgb, VeiledBits);
                }
                catch
                {
                    Dispose();
                    throw;
                }
			}

            private static int[] BuildScaleMap(int source, int target)
            {
                int[] indices = new int[target];
                long step = ((long)source << 16) / target;
                for (int i = 0; i < target; i++)
                    indices[i] = Math.Min(source - 1, (int)((i * step) >> 16));
                return indices;
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
				if (RawBitmap != null) RawBitmap.Dispose();
				if (VeiledBitmap != null) VeiledBitmap.Dispose();
				Marshal.FreeHGlobal(RawBits);
				Marshal.FreeHGlobal(VeiledBits);
				if (OldObj != IntPtr.Zero) SelectObject(Hdc, OldObj);
				if (Dib != IntPtr.Zero) DeleteObject(Dib);
				if (Hdc != IntPtr.Zero) DeleteDC(Hdc);
			}
		}

		private readonly string executable;
		internal string WindowTitle { get { return title; } }
        internal bool TaskbarWindowHidden { get { return WindowStyle.IsPlaybackToolWindow(FindWindow(null, title)); } }
        private int droppedBlackFrames;
        internal int DroppedBlackFrames { get { return Volatile.Read(ref droppedBlackFrames); } }
		private readonly string title = "AandmWallpaper-" + Guid.NewGuid().ToString("N");
		private Size renderSize;
		private Size desiredSize;
        private Size desiredCaptureSize;
        private volatile string captureStage = "等待引擎创建播放窗口";
        internal string CaptureStage { get { return captureStage; } }
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
        private int settingsRevision;
        private readonly object captureHistoryLock = new object();
        private IntPtr captureHistory;
        private int captureHistoryBytes, captureHistoryWidth, captureHistoryHeight, captureHistoryRevision = -1;
        private long lastSuccessfulCapture;
        internal bool AwaitingNonBlackFrame { get { return capturedBlackAtStartup && !warmedUp; } }
        private volatile bool capturedBlackAtStartup;
        private readonly object captureCallLock = new object();
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr memset(IntPtr destination, int value, UIntPtr bytes);

        private static bool ReadWindowPixels(IntPtr window, FrameSlot slot)
        {
            // PrintWindow can report success while a DirectX surface has written nothing.
            uint bytes = (uint)(slot.CaptureWidth * slot.CaptureHeight * 4);
            memset(slot.CaptureBits, 0, new UIntPtr(bytes));
            return PrintWindow(window, slot.Hdc, RenderClient);
        }

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int memcmp(IntPtr left, IntPtr right, UIntPtr length);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyCapture(IntPtr destination, IntPtr source, UIntPtr length);

        internal bool HasRecentCapture(int seconds)
        {
            long stamp = Interlocked.Read(ref lastSuccessfulCapture);
            return stamp != 0 && Stopwatch.GetTimestamp() - stamp <= seconds * Stopwatch.Frequency;
        }

		private Color desiredVeilColor;
		private int desiredVeilAlpha;
		private bool desiredPrepareGlass;
		private int desiredBlurLevel;

		private Thread[] pipelineThreads;
		private double nextCaptureMs;
        private double desiredCaptureIntervalMs;
        private double lastPublishedCaptureMs = -1;
		private Stopwatch pipelineClock;
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
            desiredCaptureSize = size;
			// A unique title lets this instance close only its own Wallpaper Engine window.
			SendCommand(string.Format(CultureInfo.InvariantCulture,
				"openWallpaper -file \"{0}\" -playInWindow {1} -width {2} -height {3} -x {4} -y {4} -borderless",
				source, title, size.Width, size.Height, OffScreen));
		}

		internal void StartPipeline(Size targetSize, Color veilColor, int veilAlpha, bool prepareGlass, int blurLevel, double fps, Size captureSize)
		{
			UpdatePipelineConfig(targetSize, veilColor, veilAlpha, prepareGlass, blurLevel, fps, captureSize);
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
				pipelineThreads = new Thread[captureSize.Width * (long)captureSize.Height > 3000000 ? 1 : 2];
				for (int i = 0; i < pipelineThreads.Length; i++)
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

		internal void UpdatePipelineConfig(Size targetSize, Color veilColor, int veilAlpha, bool prepareGlass, int blurLevel, double fps, Size captureSize)
		{
			lock (stateLock)
			{
				paused = false;
                if (desiredSize != targetSize || desiredCaptureSize != captureSize || desiredVeilColor != veilColor || desiredVeilAlpha != veilAlpha ||
                    desiredPrepareGlass != prepareGlass || desiredBlurLevel != blurLevel)
                    settingsRevision++;
				desiredSize = targetSize;
                desiredCaptureSize = captureSize;
				desiredVeilColor = veilColor;
				desiredVeilAlpha = veilAlpha;
				desiredPrepareGlass = prepareGlass;
				desiredBlurLevel = blurLevel;
                desiredCaptureIntervalMs = 1000.0 / Math.Max(5, Math.Min(72, fps));
			}
		}

		internal FrameSlot AcquireLatestSlot()
		{
			lock (stateLock)
			{
				if (latestReadySlot == null) return null;
				if (currentUiSlot != null && currentUiSlot != latestReadySlot)
				{
					ReleaseSlot(currentUiSlot);
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
                if (window != positionedWindow) WindowStyle.HidePlaybackTaskbarWindow(window);
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

		private FrameSlot CheckoutFreeSlot(int captureWidth, int captureHeight, int outputWidth, int outputHeight, int revision)
		{
			lock (stateLock)
			{
				if (disposed || revision != settingsRevision) return null;
				if (slots == null || slotsSize.Width != outputWidth || slotsSize.Height != outputHeight
					|| slots[0].CaptureWidth != captureWidth || slots[0].CaptureHeight != captureHeight)
				{
					if (slots != null)
					{
						for (int i = 0; i < slots.Length; i++)
						{
							if (slots[i] != currentUiSlot && slots[i] != null && slots[i].State != 1) slots[i].Dispose();
						}
					}
					slots = new FrameSlot[pipelineThreads.Length + 2];
                    try
                    {
                        for (int i = 0; i < slots.Length; i++) slots[i] = new FrameSlot(captureWidth, captureHeight, outputWidth, outputHeight);
                    }
                    catch
                    {
                        foreach (FrameSlot slot in slots) if (slot != null) slot.Dispose();
                        slots = null;
                        throw;
                    }
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

        private bool OwnsSlot(FrameSlot slot)
        {
            return slots != null && Array.IndexOf(slots, slot) >= 0;
        }

        private void ReleaseSlot(FrameSlot slot)
        {
            if (!OwnsSlot(slot)) { slot.Dispose(); return; }
            slot.ReleaseGlass();
            slot.State = 0;
        }

		internal static Size ChooseCaptureSize(Size target, int maxEdge)
        {
            int longEdge = Math.Max(target.Width, target.Height);
            double pixels = (double)target.Width * target.Height;
            double sc = Math.Min(1, Math.Min(maxEdge / (double)longEdge, Math.Sqrt(8847360 / pixels)));
            if (sc >= 1) return target;
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
			catch (Exception ex) { captureStage = "采集线程错误 · " + ex.Message; }
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
                Size captureTarget;
				Color veilColor;
				int veilAlpha;
				bool prepareGlass;
				int blurLevel;
                double intervalMs;
                int revision;
				lock (stateLock)
				{
					size = desiredSize;
                    captureTarget = desiredCaptureSize;
					veilColor = desiredVeilColor;
					veilAlpha = desiredVeilAlpha;
					prepareGlass = desiredPrepareGlass;
					blurLevel = desiredBlurLevel;
                    intervalMs = desiredCaptureIntervalMs;
                    revision = settingsRevision;
				}

                double startAt;
                lock (startGate)
                {
                    double now = pipelineClock.Elapsed.TotalMilliseconds;
                    startAt = Math.Max(now, nextCaptureMs);
                    nextCaptureMs = startAt + (warmedUp ? intervalMs : Math.Max(25, intervalMs));
                }
                int wait = (int)Math.Round(startAt - pipelineClock.Elapsed.TotalMilliseconds);
                if (wait > 0) Thread.Sleep(wait);
                if (disposed || paused || revision != settingsRevision) continue;

				bool firstPosition = positionedWindow == IntPtr.Zero;
				int capW, capH;
				IntPtr window = EnsureTargetWindow(captureTarget, out capW, out capH);
				if (window == IntPtr.Zero)
				{
                    captureStage = "播放窗口尚未就绪";
					Thread.Sleep(30);
					continue;
				}
				if (firstPosition)
				{
					Thread.Sleep(60);
				}

				FrameSlot slot = CheckoutFreeSlot(capW, capH, size.Width, size.Height, revision);
				if (slot == null)
				{
					Thread.Sleep(4);
					continue;
				}

                double t0;
                bool captured;
                lock (captureCallLock)
                {
                    t0 = pipelineClock.Elapsed.TotalMilliseconds;
                    captureStage = "引擎窗口已就绪，等待采集";
                    captured = !disposed && revision == settingsRevision && ReadWindowPixels(window, slot);
                }
                if (!captured) captureStage = "播放窗口拒绝采集";
                if (disposed)
                {
                    lock (stateLock) ReleaseSlot(slot);
                    return;
                }
				double tCap = pipelineClock.Elapsed.TotalMilliseconds;
				bool valid = false;
				if (captured)
				{
                    if (warmedUp && IsRepeatedCapture(slot, revision))
                    {
                        Interlocked.Exchange(ref lastSuccessfulCapture, Stopwatch.GetTimestamp());
                        lock (stateLock) ReleaseSlot(slot);
                        continue;
                    }
					bool hasColor = ProcessSlotPixels(slot, veilColor, veilAlpha);
                    if (!hasColor)
                    {
                        if (!warmedUp) capturedBlackAtStartup = true;
                        else Interlocked.Increment(ref droppedBlackFrames);
                        captureStage = "采集画面全黑，保留上一帧";
                    }
					if (hasColor)
					{
						warmedUp = true;
                        captureStage = "已收到有效画面";
						valid = true;
                        Interlocked.Exchange(ref lastSuccessfulCapture, Stopwatch.GetTimestamp());
					}
				}
				if (!valid || disposed)
				{
					lock (stateLock)
					{
						ReleaseSlot(slot);
					}
					if (!captured) Thread.Sleep(25);
					continue;
				}

                if (prepareGlass && blurLevel > 0)
				{
					float[] lumGrid;
					slot.GlassBitmap = UiTheme.CreateBlurredBitmap(slot.RawBitmap, blurLevel, 256, out lumGrid);
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
					if (disposed || paused || revision != settingsRevision || !OwnsSlot(slot) || t0 <= lastPublishedCaptureMs)
					{
						ReleaseSlot(slot);
					}
					else
					{
						if (latestReadySlot != null && latestReadySlot != slot)
						{
							latestReadySlot.ReleaseGlass();
							latestReadySlot.State = 0;
						}
                        RememberCapture(slot, revision);
                        lastPublishedCaptureMs = t0;
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

        private bool IsRepeatedCapture(FrameSlot slot, int revision)
        {
            lock (captureHistoryLock)
                return captureHistory != IntPtr.Zero && captureHistoryRevision == revision &&
                    captureHistoryWidth == slot.CaptureWidth && captureHistoryHeight == slot.CaptureHeight &&
                    memcmp(captureHistory, slot.CaptureBits, new UIntPtr((uint)captureHistoryBytes)) == 0;
        }

        private void RememberCapture(FrameSlot slot, int revision)
        {
            int bytes = slot.CaptureWidth * slot.CaptureHeight * 4;
            lock (captureHistoryLock)
            {
                if (captureHistoryBytes != bytes)
                {
                    if (captureHistory != IntPtr.Zero) Marshal.FreeHGlobal(captureHistory);
                    captureHistory = IntPtr.Zero;
                    captureHistoryBytes = 0;
                    captureHistory = Marshal.AllocHGlobal(bytes);
                    captureHistoryBytes = bytes;
                }
                CopyCapture(captureHistory, slot.CaptureBits, new UIntPtr((uint)bytes));
                captureHistoryRevision = revision;
                captureHistoryWidth = slot.CaptureWidth;
                captureHistoryHeight = slot.CaptureHeight;
            }
        }

        private static unsafe bool ProcessSlotPixels(FrameSlot slot, Color veilColor, int veilAlpha)
        {
            uint inv = (uint)(256 - veilAlpha);
            uint addRb = (uint)veilColor.R * (uint)veilAlpha << 16 | (uint)veilColor.B * (uint)veilAlpha;
            uint addG = (uint)veilColor.G * (uint)veilAlpha << 8;
            int nonZero = 0;
            int bands = slot.Height >= 640 ? 2 : 1;
            Parallel.For(0, bands, band =>
            {
                int first = slot.Height * band / bands;
                int last = slot.Height * (band + 1) / bands;
                uint* src = (uint*)slot.CaptureBits;
                uint* raw = (uint*)slot.RawBits;
                uint* dst = (uint*)slot.VeiledBits;
                uint seen = 0;
                bool scaling = slot.ScaleColumns != null;
                fixed (int* columns = slot.ScaleColumns)
                {
                    for (int y = first; y < last; y++)
                    {
                        uint* input = src + (scaling ? slot.ScaleRows[y] : y) * slot.CaptureWidth;
                        int offset = y * slot.Width;
                        for (int x = 0; x < slot.Width; x++)
                        {
                            uint pixel = input[scaling ? columns[x] : x];
                            seen |= pixel;
                            raw[offset + x] = pixel | 0xFF000000u;
                            uint rb = (((pixel & 0x00FF00FFu) * inv + addRb) >> 8) & 0x00FF00FFu;
                            uint g = (((pixel & 0x0000FF00u) * inv + addG) >> 8) & 0x0000FF00u;
                            dst[offset + x] = 0xFF000000u | rb | g;
                        }
                    }
                }
                if ((seen & 0x00FFFFFFu) != 0) Interlocked.Increment(ref nonZero);
            });
            return nonZero != 0;
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
			if (!ReadWindowPixels(window, syncSlot)) return null;

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
			if (!hasColor)
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
                if (currentUiSlot != null && !OwnsSlot(currentUiSlot)) currentUiSlot.Dispose();
				if (slots != null)
				{
					for (int i = 0; i < slots.Length; i++)
					{
						if (slots[i] != null && slots[i].State != 1) slots[i].Dispose();
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
            lock (captureHistoryLock)
            {
                if (captureHistory != IntPtr.Zero) Marshal.FreeHGlobal(captureHistory);
                captureHistory = IntPtr.Zero;
                captureHistoryBytes = 0;
            }
			try { SendCommand("closeWallpaper -location " + title); }
			catch (System.ComponentModel.Win32Exception) { CloseNamedWindow(); }
			catch (InvalidOperationException) { CloseNamedWindow(); }
		}
	}
}
