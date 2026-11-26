using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class SharedWallpaper : IDisposable
	{
		internal const int MaxEdge = 1920;
		private const int HeaderSize = 32;
		private const long Capacity = HeaderSize + 4L * MaxEdge * MaxEdge;
		private const string Namespace = "Local\\AandmMultiWallpaper-";
		private readonly MemoryMappedFile mapping;
		private readonly MemoryMappedViewAccessor view;
		private readonly Mutex sync;

		internal string Name { get; private set; }

		internal SharedWallpaper(string name = null)
		{
			bool publisher = name == null;
			Name = name ?? Namespace + Guid.NewGuid().ToString("N");
			Guid id;
			if (!Name.StartsWith(Namespace, StringComparison.Ordinal) ||
				!Guid.TryParseExact(Name.Substring(Namespace.Length), "N", out id))
				throw new ArgumentException("同屏壁纸会话名无效。");
			mapping = publisher ? MemoryMappedFile.CreateNew(Name, Capacity) :
				MemoryMappedFile.OpenExisting(Name, MemoryMappedFileRights.Read);
			try
			{
				view = mapping.CreateViewAccessor(0, Capacity, publisher ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read);
				sync = publisher ? new Mutex(false, Name + "-frames") : Mutex.OpenExisting(Name + "-frames");
			}
			catch
			{
				if (view != null) view.Dispose();
				mapping.Dispose();
				throw;
			}
		}

		internal long Sequence
		{
			get { return view.ReadInt64(0); }
		}

		[DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
		private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);

		private IntPtr Pixels
		{
			get { return IntPtr.Add(view.SafeMemoryMappedViewHandle.DangerousGetHandle(), HeaderSize); }
		}

		private bool TryEnter(int wait)
		{
			try { return sync.WaitOne(wait); }
			catch (AbandonedMutexException) { return true; }
		}

		internal bool Publish(Bitmap frame)
		{
			if (frame.Width > MaxEdge || frame.Height > MaxEdge || !TryEnter(5)) return false;
			try
			{
				int rowBytes = frame.Width * 4;
				var data = frame.LockBits(new Rectangle(Point.Empty, frame.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
				try
				{
					if (data.Stride == rowBytes)
						CopyMemory(Pixels, data.Scan0, new UIntPtr((uint)(rowBytes * frame.Height)));
					else
						for (int row = 0; row < frame.Height; row++)
							CopyMemory(IntPtr.Add(Pixels, rowBytes * row), IntPtr.Add(data.Scan0, data.Stride * row), new UIntPtr((uint)rowBytes));
				}
				finally { frame.UnlockBits(data); }
				view.Write(8, frame.Width);
				view.Write(12, frame.Height);
				view.Write(0, Sequence + 1);
				return true;
			}
			finally { sync.ReleaseMutex(); }
		}

        internal int SampleHash()
        {
            if (!TryEnter(25)) return 0;
            try
            {
                int width = view.ReadInt32(8), height = view.ReadInt32(12);
                if (width < 1 || height < 1 || Sequence == 0) return 0;
                int hash = 17;
                for (int y = 1; y <= 8; y++)
                    for (int x = 1; x <= 12; x++)
                    {
                        int offset = (y * height / 9 * width + x * width / 13) * 4;
                        hash = unchecked(hash * 31 + (Marshal.ReadInt32(Pixels, offset) & 0xffffff));
                    }
                return hash;
            }
            finally { sync.ReleaseMutex(); }
        }

		internal bool Paint(Graphics graphics, Size canvas, Point offset, Rectangle bounds)
		{
			if (bounds.Width < 1 || bounds.Height < 1 || canvas.Width < 1 || canvas.Height < 1 || !TryEnter(25)) return false;
			try
			{
				if (Sequence == 0) return false;
				int width = view.ReadInt32(8), height = view.ReadInt32(12);
				if (width < 1 || height < 1 || width > MaxEdge || height > MaxEdge) return false;
				double scale = Math.Max(canvas.Width / (double)width, canvas.Height / (double)height);
				int outputWidth = (int)Math.Ceiling(width * scale), outputHeight = (int)Math.Ceiling(height * scale);
				var destination = new Rectangle((canvas.Width - outputWidth) / 2 - offset.X,
					(canvas.Height - outputHeight) / 2 - offset.Y, outputWidth, outputHeight);
				Rectangle clip = Rectangle.Intersect(bounds, Rectangle.Ceiling(graphics.VisibleClipBounds));
				if (clip.Width < 1 || clip.Height < 1) return true;
				GraphicsState state = graphics.Save();
				try
				{
					using (var image = new Bitmap(width, height, width * 4, PixelFormat.Format32bppRgb, Pixels))
					{
						graphics.InterpolationMode = InterpolationMode.Bilinear;
						float sx = width / (float)outputWidth, sy = height / (float)outputHeight;
						graphics.DrawImage(image, clip, (clip.X - destination.X) * sx, (clip.Y - destination.Y) * sy,
							clip.Width * sx, clip.Height * sy, GraphicsUnit.Pixel);
					}
				}
				finally { graphics.Restore(state); }
				return true;
			}
			finally { sync.ReleaseMutex(); }
		}

		public void Dispose()
		{
			sync.Dispose();
			view.Dispose();
			mapping.Dispose();
		}
	}

	internal sealed class SharedWallpaperPlayer : IDisposable
	{
		private readonly Form owner;
		private readonly System.Windows.Forms.Timer timer;
		private WallpaperEnginePlayback engine;
		private WallpaperEnginePlayback captureInFlight;
		internal string WindowTitle { get { return engine == null ? "" : engine.WindowTitle; } }
		private string executable;
		private string source;
		private bool capturing;
		private bool disposed;
		private DateTime lastFrameUtc;
		internal readonly SharedWallpaper Frames = new SharedWallpaper();
		internal string Error { get; private set; }
		internal event Action FrameChanged;

		internal SharedWallpaperPlayer(Form owner)
		{
			this.owner = owner;
			timer = new System.Windows.Forms.Timer { Interval = 100 };
			timer.Tick += Capture;
		}

		internal async Task Play(string executable, string source)
		{
			StopEngine();
			this.executable = executable;
			this.source = source;
			Error = null;
			lastFrameUtc = DateTime.UtcNow;
			long previous = Frames.Sequence;
			timer.Start();
			DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!disposed && Error == null && Frames.Sequence == previous && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            if (disposed) throw new ObjectDisposedException("SharedWallpaperPlayer");
            if (Frames.Sequence == previous && Error == null)
            {
                timer.Stop();
                StopEngine();
                throw new TimeoutException("同屏壁纸未能出帧。");
            }
			if (Error != null) throw new InvalidOperationException(Error);
		}

		private async void Capture(object sender, EventArgs args)
		{
			if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
			if (!owner.Visible || owner.WindowState == FormWindowState.Minimized)
			{
				StopEngine();
				return;
			}
			if (capturing || owner.ClientSize.Width < 16 || owner.ClientSize.Height < 16) return;
			capturing = true;
			try
			{
				Size size = owner.ClientSize;
				double scale = Math.Min(1, SharedWallpaper.MaxEdge / (double)Math.Max(size.Width, size.Height));
				size = new Size(Math.Max(16, (int)Math.Round(size.Width * scale)), Math.Max(16, (int)Math.Round(size.Height * scale)));
				if (engine == null)
				{
					engine = new WallpaperEnginePlayback(executable, source, size);
					lastFrameUtc = DateTime.UtcNow;
				}
				WallpaperEnginePlayback current = engine;
				captureInFlight = current;
				using (Bitmap frame = await Task.Run(() => current.Capture(size)))
				{
					if (disposed || owner.IsDisposed || current != engine) return;
					if (frame != null && Frames.Publish(frame))
					{
						lastFrameUtc = DateTime.UtcNow;
						if (FrameChanged != null) FrameChanged();
					}
					else if (DateTime.UtcNow - lastFrameUtc > TimeSpan.FromSeconds(15))
						throw new InvalidOperationException("同屏壁纸未能出帧。");
				}
			}
			catch (Exception error)
			{
				if (!disposed)
				{
					Error = error.Message;
					timer.Stop();
					StopEngine();
				}
			}
			finally
            {
                WallpaperEnginePlayback completed = captureInFlight;
                captureInFlight = null;
                capturing = false;
                if (completed != null && completed != engine) completed.Dispose();
            }
		}

		internal void PublishStatic(string imagePath)
		{
			if (disposed) throw new ObjectDisposedException("SharedWallpaperPlayer");
			timer.Stop();
			StopEngine();
			Error = null;
			if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
				throw new System.IO.FileNotFoundException("找不到壁纸图片文件。", imagePath ?? "");
			using (var raw = Image.FromFile(imagePath))
			{
				double scale = Math.Min(1.0, SharedWallpaper.MaxEdge / (double)Math.Max(raw.Width, raw.Height));
				int w = Math.Max(1, (int)Math.Round(raw.Width * scale));
				int h = Math.Max(1, (int)Math.Round(raw.Height * scale));
				using (var scaled = new Bitmap(w, h, PixelFormat.Format32bppRgb))
				{
					using (Graphics g = Graphics.FromImage(scaled))
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage(raw, 0, 0, w, h);
					}
					if (!Frames.Publish(scaled))
						throw new InvalidOperationException("同屏壁纸帧发布失败。");
				}
			}
			lastFrameUtc = DateTime.UtcNow;
			if (FrameChanged != null) FrameChanged();
		}

		private void StopEngine()
		{
			if (engine == null) return;
			WallpaperEnginePlayback previous = engine;
            engine = null;
            if (previous != captureInFlight) previous.Dispose();
		}

		public void Dispose()
		{
			disposed = true;
			timer.Stop();
			timer.Dispose();
			StopEngine();
			Frames.Dispose();
		}
	}
}
