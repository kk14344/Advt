using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AandmAuto
{
	/// <summary>
	/// True 90-144fps+ DWM VSync animation driver for Liquid Glass micro-interactions and dialog transitions.
	/// Blocks a single background pacing thread on dwmapi.DwmFlush() ONLY while animations are active,
	/// delivering monitor-native VBlank ticks (90/120/144/165Hz) via WM_APP without WM_TIMER 60fps capping
	/// or message-queue pileups. Suspends immediately on idle for strict 0% idle CPU.
	/// </summary>
	internal static class GlassAnimator
	{
		private const int WmAppTick = 0x8000 + 0x42;

		[DllImport("dwmapi.dll")]
		private static extern int DwmFlush();

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

		[DllImport("winmm.dll")]
		private static extern uint timeBeginPeriod(uint uPeriod);

		[DllImport("winmm.dll")]
		private static extern uint timeEndPeriod(uint uPeriod);

		private sealed class Entry
		{
			internal Func<float, bool> Step;
		}

		private sealed class TickSink : NativeWindow
		{
			internal TickSink()
			{
				var cp = new CreateParams
				{
					Caption = "AandmGlassTickSink",
					Parent = new IntPtr(-3) // HWND_MESSAGE
				};
				CreateHandle(cp);
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == WmAppTick)
				{
					Interlocked.Exchange(ref tickPending, 0);
					ProcessTickOnUiThread();
					return;
				}
				base.WndProc(ref m);
			}
		}

		private static readonly List<Entry> entries = new List<Entry>();
		private static readonly ManualResetEventSlim wakeEvent = new ManualResetEventSlim(false);
		private static TickSink sink;
		private static IntPtr sinkHandle = IntPtr.Zero;
		private static Thread vsyncThread;
		private static int activeFlag;
		private static int tickPending;
		private static int shutdownFlag;
		private static long lastTimestamp;
		private static bool highResTimerActive;
		private static System.Windows.Forms.Timer fallbackTimer;

		static GlassAnimator()
		{
			AppDomain.CurrentDomain.ProcessExit += delegate { Shutdown(); };
			Application.ApplicationExit += delegate { Shutdown(); };
		}

		internal static bool Enabled
		{
			get
			{
				return UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
					&& !UiTheme.ExtremePerformanceMode
					&& LiquidGlassEngine.MotionEnabled
					&& !LiquidGlassEngine.PreviewStaticMode;
			}
		}

		internal static bool IsAnimating
		{
			get { return entries.Count > 0; }
		}

		private static void Shutdown()
		{
			Interlocked.Exchange(ref shutdownFlag, 1);
			Interlocked.Exchange(ref activeFlag, 0);
			try { wakeEvent.Set(); } catch { }
			EndHighResTimer();
		}

		private static void BeginHighResTimer()
		{
			if (!highResTimerActive)
			{
				try { timeBeginPeriod(1); highResTimerActive = true; } catch { }
			}
		}

		private static void EndHighResTimer()
		{
			if (highResTimerActive)
			{
				highResTimerActive = false;
				try { timeEndPeriod(1); } catch { }
			}
		}

		private static void StopDriver()
		{
			Interlocked.Exchange(ref activeFlag, 0);
			wakeEvent.Reset();
			if (fallbackTimer != null && fallbackTimer.Enabled) fallbackTimer.Stop();
			EndHighResTimer();
		}

		internal static void Add(Func<float, bool> step)
		{
			if (step == null) return;
			if (!Enabled)
			{
				try { step(1f); } catch { }
				return;
			}
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Step == step) return;
			}
			entries.Add(new Entry { Step = step });
			EnsureRunning();
		}

		internal static void Remove(Func<float, bool> step)
		{
			if (step == null) return;
			for (int i = entries.Count - 1; i >= 0; i--)
			{
				if (entries[i].Step == step) entries.RemoveAt(i);
			}
			if (entries.Count == 0) StopDriver();
		}

		private static void EnsureRunning()
		{
			if (Volatile.Read(ref activeFlag) != 0) return;

			if (sink == null || sinkHandle == IntPtr.Zero)
			{
				try
				{
					sink = new TickSink();
					sinkHandle = sink.Handle;
				}
				catch
				{
					sinkHandle = IntPtr.Zero;
				}
			}

			BeginHighResTimer();
			lastTimestamp = Stopwatch.GetTimestamp();

			if (sinkHandle != IntPtr.Zero)
			{
				if (vsyncThread == null)
				{
					vsyncThread = new Thread(VSyncLoop)
					{
						Name = "GlassVSyncPacer",
						IsBackground = true,
						Priority = ThreadPriority.AboveNormal
					};
					vsyncThread.Start();
				}
				Interlocked.Exchange(ref activeFlag, 1);
				wakeEvent.Set();
				// Immediately dispatch first tick so animations begin on the very next frame with zero delay
				if (Interlocked.CompareExchange(ref tickPending, 1, 0) == 0)
				{
					PostMessage(sinkHandle, WmAppTick, IntPtr.Zero, IntPtr.Zero);
				}
			}
			else
			{
				if (fallbackTimer == null)
				{
					fallbackTimer = new System.Windows.Forms.Timer { Interval = 8 };
					fallbackTimer.Tick += delegate { ProcessTickOnUiThread(); };
				}
				Interlocked.Exchange(ref activeFlag, 1);
				if (!fallbackTimer.Enabled) fallbackTimer.Start();
			}
		}

		private static void VSyncLoop()
		{
			while (Volatile.Read(ref shutdownFlag) == 0)
			{
				wakeEvent.Wait();
				if (Volatile.Read(ref shutdownFlag) != 0) break;

				while (Volatile.Read(ref activeFlag) != 0 && Volatile.Read(ref shutdownFlag) == 0)
				{
					long t0 = Stopwatch.GetTimestamp();
					int hr = -1;
					try { hr = DwmFlush(); } catch { hr = -1; }
					long elapsedMs = (Stopwatch.GetTimestamp() - t0) * 1000L / Stopwatch.Frequency;
					// If DwmFlush failed or returned instantaneously (e.g. headless/RDP), pace smoothly at ~125Hz
					if (hr != 0 || elapsedMs < 2)
					{
						Thread.Sleep(7);
					}
					if (Volatile.Read(ref activeFlag) == 0) break;
					IntPtr h = sinkHandle;
					if (h != IntPtr.Zero && Interlocked.CompareExchange(ref tickPending, 1, 0) == 0)
					{
						if (!PostMessage(h, WmAppTick, IntPtr.Zero, IntPtr.Zero))
						{
							Interlocked.Exchange(ref tickPending, 0);
						}
					}
				}
			}
		}

		private static void ProcessTickOnUiThread()
		{
			long now = Stopwatch.GetTimestamp();
			float dt = (float)((now - lastTimestamp) / (double)Stopwatch.Frequency);
			lastTimestamp = now;
			if (dt <= 0.0005f || dt > 0.045f) dt = 1f / 90f;

			if (!Enabled)
			{
				for (int i = entries.Count - 1; i >= 0; i--)
				{
					try { entries[i].Step(1f); } catch { }
				}
				entries.Clear();
				StopDriver();
				return;
			}

			for (int i = entries.Count - 1; i >= 0; i--)
			{
				bool keep = false;
				try { keep = entries[i].Step(dt); }
				catch { keep = false; }
				if (!keep && i < entries.Count) entries.RemoveAt(i);
			}

			if (entries.Count == 0)
			{
				StopDriver();
			}
		}
	}

	/// <summary>
	/// Apple iOS 26 / macOS Tahoe 26 Liquid Glass optical renderer:
	/// - /unsafe LockBits 4-tap bilinear convex-lens rim refraction + SDF anti-aliased edges
	/// - 4-layer thick crystal specular bevel composited ABOVE translucent tints
	/// - Per-control cached bitmap so hover/sheen/pointer animations stay locked at 60-120fps
	/// - Adaptive dark/light ink based on local/global wallpaper luminance
	/// </summary>
	internal static class LiquidGlassEngine
	{
		internal const int PresetCrystal = 0;  // 极清透（Apple 水晶）
		internal const int PresetBalanced = 1; // 标准通透（默认）
		internal const int PresetDeep = 2;     // 深邃护眼

		internal static int Preset = PresetBalanced;
		internal static bool MotionEnabled = true;
		internal static bool PreviewStaticMode = false;

		private static long cachedLumaSeq = -1;
		private static Image cachedLumaSource;
		private static float cachedGlobalLuma = 0.25f;

		[StructLayout(LayoutKind.Sequential)]
		private struct BlendFunction
		{
			public byte BlendOp;
			public byte BlendFlags;
			public byte SourceConstantAlpha;
			public byte AlphaFormat;
		}

		[DllImport("msimg32.dll", SetLastError = true)]
		private static extern bool AlphaBlend(
			IntPtr hdcDest, int xoriginDest, int yoriginDest, int wDest, int hDest,
			IntPtr hdcSrc, int xoriginSrc, int yoriginSrc, int wSrc, int hSrc,
			BlendFunction ftn);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern bool DeleteDC(IntPtr hdc);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

		[DllImport("gdi32.dll", SetLastError = true)]
		private static extern bool DeleteObject(IntPtr hObject);

		private sealed class PillCache
		{
			internal Bitmap Image;
			internal IntPtr HBitmap;
			internal int W, H, Radius;
			internal int Ox, Oy, CanvasW, CanvasH;
			internal long Seq;
			internal int Preset;
			internal int Blur;
			internal int TintArgb;
			internal int TintAlpha;
			internal bool Selected;
			internal bool Enabled;
			internal bool IsLight;
			internal bool HasSource;

			internal void Release()
			{
				if (HBitmap != IntPtr.Zero) { try { DeleteObject(HBitmap); } catch { } HBitmap = IntPtr.Zero; }
				if (Image != null) { Image.Dispose(); Image = null; }
			}
		}

		private sealed class ShellCache
		{
			internal Bitmap Image;
			internal IntPtr HBitmap;
			internal int W, H, Radius;
			internal int Preset;
			internal int TintArgb;
			internal int TintAlpha;
			internal bool Selected;
			internal bool Enabled;
			internal bool IsLight;

			internal void Release()
			{
				if (HBitmap != IntPtr.Zero) { try { DeleteObject(HBitmap); } catch { } HBitmap = IntPtr.Zero; }
				if (Image != null) { Image.Dispose(); Image = null; }
			}
		}

		private static readonly ConditionalWeakTable<Control, PillCache> pillCaches =
			new ConditionalWeakTable<Control, PillCache>();

		private static readonly ConditionalWeakTable<Control, ShellCache> shellCaches =
			new ConditionalWeakTable<Control, ShellCache>();

		private static Bitmap[] glowSprites;

		private static unsafe Bitmap GetHoverGlowSprite(float effectiveHover)
		{
			if (glowSprites == null)
			{
				var bank = new Bitmap[8];
				const int sz = 64;
				const float half = (sz - 1) * 0.5f;
				for (int level = 0; level < 8; level++)
				{
					float maxAlpha = (level + 1) * (44f / 8f);
					var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppPArgb);
					BitmapData bd = bmp.LockBits(new Rectangle(0, 0, sz, sz), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
					try
					{
						byte* scan = (byte*)bd.Scan0;
						int stride = bd.Stride;
						for (int y = 0; y < sz; y++)
						{
							byte* row = scan + y * stride;
							float dy = (y - half) / half;
							for (int x = 0; x < sz; x++)
							{
								float dx = (x - half) / half;
								float r2 = dx * dx + dy * dy;
								if (r2 >= 1f)
								{
									row[x * 4 + 0] = 0;
									row[x * 4 + 1] = 0;
									row[x * 4 + 2] = 0;
									row[x * 4 + 3] = 0;
								}
								else
								{
									float f = 1f - r2;
									float a = f * f * maxAlpha;
									byte ab = (byte)(a + 0.5f);
									row[x * 4 + 0] = ab;
									row[x * 4 + 1] = ab;
									row[x * 4 + 2] = ab;
									row[x * 4 + 3] = ab;
								}
							}
						}
					}
					finally { bmp.UnlockBits(bd); }
					bank[level] = bmp;
				}
				glowSprites = bank;
			}
			int idx = (int)Math.Round(Math.Max(0f, Math.Min(1f, effectiveHover)) * 7f);
			return glowSprites[idx];
		}

		private static void DrawPremultipliedAlpha(Graphics g, Bitmap bmp, ref IntPtr hBitmap, Rectangle rect, float alpha, bool allowCacheHBitmap)
		{
			if (bmp == null) return;
			if (alpha >= 0.992f)
			{
				g.DrawImageUnscaled(bmp, rect.X, rect.Y);
				return;
			}
			byte alphaByte = (byte)Math.Max(1, Math.Min(254, (int)Math.Round(alpha * 255f)));
			bool blitted = false;
			try
			{
				if (g.IsClipEmpty && rect.Width == bmp.Width && rect.Height == bmp.Height)
				{
					float tx = 0f, ty = 0f;
					bool pureTranslate = false;
					using (Matrix m = g.Transform)
					{
						float[] el = m.Elements;
						if (Math.Abs(el[0] - 1f) < 0.001f && Math.Abs(el[1]) < 0.001f
							&& Math.Abs(el[2]) < 0.001f && Math.Abs(el[3] - 1f) < 0.001f)
						{
							tx = el[4];
							ty = el[5];
							pureTranslate = true;
						}
					}
					if (pureTranslate)
					{
						IntPtr hb = hBitmap;
						bool tempHb = false;
						if (hb == IntPtr.Zero)
						{
							hb = bmp.GetHbitmap(Color.FromArgb(0));
							if (allowCacheHBitmap) hBitmap = hb;
							else tempHb = true;
						}
						if (hb != IntPtr.Zero)
						{
							IntPtr dstHdc = g.GetHdc();
							IntPtr memDc = IntPtr.Zero;
							IntPtr prevObj = IntPtr.Zero;
							try
							{
								memDc = CreateCompatibleDC(dstHdc);
								if (memDc != IntPtr.Zero)
								{
									prevObj = SelectObject(memDc, hb);
									var bf = new BlendFunction
									{
										BlendOp = 0, // AC_SRC_OVER
										BlendFlags = 0,
										SourceConstantAlpha = alphaByte,
										AlphaFormat = 1 // AC_SRC_ALPHA
									};
									blitted = AlphaBlend(
										dstHdc,
										rect.X + (int)Math.Round(tx),
										rect.Y + (int)Math.Round(ty),
										bmp.Width,
										bmp.Height,
										memDc, 0, 0, bmp.Width, bmp.Height, bf);
								}
							}
							finally
							{
								if (memDc != IntPtr.Zero)
								{
									if (prevObj != IntPtr.Zero) SelectObject(memDc, prevObj);
									DeleteDC(memDc);
								}
								g.ReleaseHdc(dstHdc);
								if (tempHb) DeleteObject(hb);
							}
						}
					}
				}
			}
			catch
			{
				blitted = false;
			}
			if (!blitted)
			{
				using (var ia = new ImageAttributes())
				{
					var cm = new ColorMatrix { Matrix33 = alpha };
					ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
					g.DrawImage(bmp, rect, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
				}
			}
		}

		internal static bool IsBrightBackdrop()
		{
			if (Preset == PresetDeep || UiTheme.ExtremePerformanceMode) return false;
			return GlobalWallpaperLuminance() >= 0.68f;
		}

		internal static float GlobalWallpaperLuminance()
		{
			Image src = UiTheme.EffectiveStageBackground;
			if (src == null) return 0.22f;
			long seq = UiTheme.WallpaperFrameSequence;
			if (cachedLumaSource == src && cachedLumaSeq == seq) return cachedGlobalLuma;
			try
			{
				Bitmap bmp = src as Bitmap;
				if (bmp != null && bmp.Width >= 8 && bmp.Height >= 8)
				{
					float sum = 0f;
					int count = 0;
					for (int gy = 1; gy <= 4; gy++)
					{
						int py = Math.Max(0, Math.Min(bmp.Height - 1, bmp.Height * gy / 5));
						for (int gx = 1; gx <= 4; gx++)
						{
							int px = Math.Max(0, Math.Min(bmp.Width - 1, bmp.Width * gx / 5));
							Color c = bmp.GetPixel(px, py);
							sum += (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;
							count++;
						}
					}
					cachedGlobalLuma = count > 0 ? sum / count : 0.25f;
				}
				else
				{
					cachedGlobalLuma = 0.25f;
				}
			}
			catch
			{
				cachedGlobalLuma = 0.25f;
			}
			cachedLumaSource = src;
			cachedLumaSeq = seq;
			return cachedGlobalLuma;
		}

		internal static Color GlassInk(bool enabled)
		{
			if (IsBrightBackdrop())
			{
				return enabled ? Color.FromArgb(242, 16, 22, 34) : Color.FromArgb(135, 24, 32, 46);
			}
			return enabled ? Color.White : Color.FromArgb(165, 255, 255, 255);
		}

		internal static Color GlassMutedInk()
		{
			return IsBrightBackdrop()
				? Color.FromArgb(176, 20, 28, 42)
				: Color.FromArgb(196, 255, 255, 255);
		}

		internal static Color GlassTextShadow()
		{
			return IsBrightBackdrop()
				? Color.FromArgb(120, 255, 255, 255)
				: Color.FromArgb(150, 0, 0, 0);
		}

		internal static Color GlassHairline()
		{
			return IsBrightBackdrop()
				? Color.FromArgb(44, 15, 28, 48)
				: Color.FromArgb(46, 255, 255, 255);
		}

		internal static Color GlassTrackFill()
		{
			return IsBrightBackdrop()
				? Color.FromArgb(26, 15, 28, 48)
				: Color.FromArgb(56, 4, 8, 16);
		}

		internal static Color GlassInputFill()
		{
			return IsBrightBackdrop()
				? Color.FromArgb(238, 244, 250)
				: Color.FromArgb(38, 46, 62);
		}

		/// <summary>
		/// Samples the actual blurred wallpaper behind a native WinForms control (TextBox/ListBox/ComboBox dropdown)
		/// to produce a harmonious frosted fill instead of an abrupt solid black box.
		/// </summary>
		internal static Color SampleAdaptiveGlassFill(Control ctrl)
		{
			bool bright = IsBrightBackdrop();
			if (ctrl == null || ctrl.IsDisposed) return GlassInputFill();
			try
			{
				Bitmap sample = UiTheme.IsOutsideEngineCanvasPublic(ctrl) ? null : UiTheme.GetBlurredGlassBackground();
				if (sample != null)
				{
					int cw, ch, ox, oy;
					UiTheme.GetWindowCanvasMetrics(ctrl, out cw, out ch, out ox, out oy);
					Rectangle dest = UiTheme.GlassSourceRectanglePublic(sample, cw, ch, ox, oy);
					int cx = ctrl.Width / 2;
					int cy = ctrl.Height / 2;
					if (dest.Width > 0 && dest.Height > 0 && dest.Contains(cx, cy))
					{
						int sx = Math.Max(0, Math.Min(sample.Width - 1, (int)((cx - dest.Left) * (long)sample.Width / dest.Width)));
						int sy = Math.Max(0, Math.Min(sample.Height - 1, (int)((cy - dest.Top) * (long)sample.Height / dest.Height)));
						Color wall = sample.GetPixel(sx, sy);
						// Preserve real blurred wallpaper hue (rose/pink/lavender/blue) with restrained frosted grounding
						if (bright)
							return UiTheme.Mix(wall, Color.FromArgb(248, 246, 250), 0.36f);
						return UiTheme.Mix(wall, Color.FromArgb(14, 18, 28), 0.34f);
					}
				}
				Color sampled = UiTheme.SampleGlassTextBackground(ctrl);
				return bright
					? UiTheme.Mix(sampled, Color.FromArgb(248, 246, 250), 0.40f)
					: UiTheme.Mix(sampled, Color.FromArgb(18, 22, 34), 0.22f);
			}
			catch
			{
				return GlassInputFill();
			}
		}

		internal static void DrawGlassCard(Graphics g, Control ctrl, GraphicsPath clipPath, int cornerRadius, Color tintColor, bool subtle)
		{
			if (ctrl == null || ctrl.Width < 2 || ctrl.Height < 2) return;
			Rectangle bounds = new Rectangle(0, 0, Math.Max(1, ctrl.Width - 1), Math.Max(1, ctrl.Height - 1));
			bool bright = IsBrightBackdrop();

			GraphicsState state = g.Save();
			try
			{
				if (clipPath != null) g.SetClip(clipPath, CombineMode.Intersect);
				int width, height, x, y;
				UiTheme.GetWindowCanvasMetrics(ctrl, out width, out height, out x, out y);
				Bitmap sample = ResolveCardBackdropSample(ctrl);
				if (sample != null)
				{
					UiTheme.DrawGlassSamplePublic(g, sample, bounds, width, height, x, y);
				}

				// Direction C quiet seamless sheet: lets wallpaper breathe with restrained depth, zero chalky fog
				int baseAlpha;
				Color fillBase;
				if (Preset == PresetCrystal)
				{
					baseAlpha = bright ? (subtle ? 38 : 54) : (subtle ? 34 : 52);
					fillBase = bright ? Color.White : Color.FromArgb(8, 12, 20);
				}
				else if (Preset == PresetDeep)
				{
					baseAlpha = subtle ? 96 : 122;
					fillBase = Color.FromArgb(8, 12, 22);
				}
				else
				{
					baseAlpha = bright ? (subtle ? 56 : 74) : (subtle ? 52 : 68);
					fillBase = bright ? Color.White : Color.FromArgb(8, 12, 20);
				}
				using (var tint = new SolidBrush(Color.FromArgb(baseAlpha, fillBase)))
				{
					g.FillRectangle(tint, bounds);
				}
			}
			finally { g.Restore(state); }

			if (clipPath == null) return;
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;

			// Ultra-refined hairline border (no thick bright plastic outline) + delicate top micro-catchlight
			Color borderColor = bright ? Color.FromArgb(30, 20, 32, 52) : Color.FromArgb(28, 255, 255, 255);
			using (var pen = new Pen(borderColor, 0.9f))
			{
				g.DrawPath(pen, clipPath);
			}
			int r = Math.Max(4, Math.Min(cornerRadius, Math.Min(bounds.Width / 2, bounds.Height / 2)));
			if (bounds.Width > r * 2 + 6)
			{
				Color topHi = bright ? Color.FromArgb(155, 255, 255, 255) : Color.FromArgb(52, 255, 255, 255);
				using (var hiPen = new Pen(topHi, 0.85f))
					g.DrawLine(hiPen, bounds.Left + r, bounds.Top + 1, bounds.Right - r, bounds.Top + 1);
			}
		}

		private static Bitmap ResolveCardBackdropSample(Control ctrl)
		{
			bool live = UiTheme.UsesEngineCanvasPublic(ctrl);
			if (live) return UiTheme.CurrentEngineGlassPublic();
			if (UiTheme.IsOutsideEngineCanvasPublic(ctrl)) return null;
			if (Preset == PresetCrystal && UiTheme.GlassBlur <= 4)
			{
				Bitmap sharp = UiTheme.EffectiveStageBackground as Bitmap;
				if (sharp != null) return sharp;
			}
			return UiTheme.GetBlurredGlassBackground();
		}

		private static Bitmap ResolvePillBackdropSample(Control ctrl)
		{
			bool live = UiTheme.UsesEngineCanvasPublic(ctrl);
			if (live)
			{
				Bitmap ef = UiTheme.EngineFrame as Bitmap;
				if (Preset == PresetCrystal && ef != null) return ef;
				return UiTheme.CurrentEngineGlassPublic() ?? ef;
			}
			if (UiTheme.IsOutsideEngineCanvasPublic(ctrl)) return null;
			// Crystal / Balanced pills sample crisp or lightly softened wallpaper for authentic water-droplet refraction!
			if (Preset != PresetDeep)
			{
				Bitmap sharp = UiTheme.EffectiveStageBackground as Bitmap;
				if (sharp != null) return sharp;
			}
			return UiTheme.GetBlurredGlassBackground();
		}

		internal static void DrawCrystalPill(
			Graphics g,
			Control ctrl,
			Rectangle rect,
			int cornerRadius,
			Color tintColor,
			int tintAlpha,
			bool hovered,
			bool pressed,
			bool selected,
			bool enabled,
			float hoverT,
			float pressT,
			float sheenT,
			Point pointerLocal,
			bool quietAtRest = false,
			bool fastMotion = false)
		{
			if (rect.Width < 4 || rect.Height < 4) return;
			int radius = Math.Max(3, Math.Min(cornerRadius, Math.Min(rect.Width, rect.Height) / 2));
			bool bright = IsBrightBackdrop();

			float effectiveHover = Math.Max(hoverT, hovered && !GlassAnimator.Enabled ? 1f : 0f);
			float effectivePress = Math.Max(pressT, pressed && !GlassAnimator.Enabled ? 1f : 0f);
			float sheenGlow = (sheenT > 0.005f && sheenT < 0.995f) ? (float)Math.Sin(sheenT * Math.PI) : 0f;

			// Direction C: Quiet seamless borderless surface at rest; physical liquid lens awakens on hover/active/hero!
			bool alwaysLens = !quietAtRest || selected || (tintAlpha > 0 && tintColor != Color.White);
			float awaken = alwaysLens ? 1f : Math.Max(effectiveHover, Math.Max(effectivePress, sheenGlow * 0.85f));
			if (!enabled && quietAtRest) awaken = 0f;

			g.SmoothingMode = SmoothingMode.HighQuality;
			if (awaken < 0.99f)
			{
				using (GraphicsPath qPath = UiTheme.RoundedRect(rect, radius))
				{
					int qAlpha = bright
						? (int)((enabled ? 13f : 8f) * (1f - awaken * 0.45f))
						: (int)((enabled ? 15f : 9f) * (1f - awaken * 0.45f));
					Color qColor = bright ? Color.FromArgb(qAlpha, 18, 30, 52) : Color.FromArgb(qAlpha, 255, 255, 255);
					using (var qb = new SolidBrush(qColor))
						g.FillPath(qb, qPath);
				}
			}

			if (awaken > 0.01f)
			{
				int canvasW = rect.Width, canvasH = rect.Height, ox = 0, oy = 0;
				if (ctrl != null) UiTheme.GetWindowCanvasMetrics(ctrl, out canvasW, out canvasH, out ox, out oy);
				ox += rect.X;
				oy += rect.Y;

				Bitmap source = ctrl != null ? ResolvePillBackdropSample(ctrl) : null;
				long seq = UiTheme.WallpaperFrameSequence;

				// Grounded tight contact shadow scaled by awaken (zero muddy float)
				int shA = (int)((bright ? 20f : 44f) * awaken);
				if (shA > 2)
				{
					using (GraphicsPath shadowPath = UiTheme.RoundedRect(new Rectangle(rect.X, rect.Y + 2, rect.Width, rect.Height), radius))
					using (var shadowBrush = new SolidBrush(Color.FromArgb(shA, 0, 0, 0)))
					{
						g.FillPath(shadowBrush, shadowPath);
					}
				}

				// Dual-stage optical pipeline:
				// - During active sliding/dragging OR live Wallpaper Engine frames: <0.25ms shifted backdrop slice + cached optical glass shell!
				// - When settled on static backdrop: full 4-tap chromatic convex-lens rim refraction cached per control!
				bool useFastMotion = fastMotion || UiTheme.EngineFrame != null;
				if (useFastMotion)
				{
					if (source != null && awaken >= 0.85f)
					{
						using (GraphicsPath clipPath = UiTheme.RoundedRect(rect, radius))
						{
							GraphicsState stClip = g.Save();
							try
							{
								g.SetClip(clipPath, CombineMode.Intersect);
								UiTheme.DrawGlassSamplePublic(g, source, rect, canvasW, canvasH, ox, oy);
							}
							finally { g.Restore(stClip); }
						}
					}

					Bitmap shell = null;
					bool disposeShell = false;
					if (ctrl != null)
					{
						ShellCache sc = shellCaches.GetValue(ctrl, c =>
						{
							var v = new ShellCache();
							c.Disposed += delegate { v.Release(); };
							return v;
						});
						bool match = sc.Image != null
							&& sc.W == rect.Width && sc.H == rect.Height && sc.Radius == radius
							&& sc.Preset == Preset && sc.TintArgb == tintColor.ToArgb() && sc.TintAlpha == tintAlpha
							&& sc.Selected == selected && sc.Enabled == enabled && sc.IsLight == bright;
						if (!match)
						{
							sc.Release();
							sc.Image = BakeGlassShellBitmap(
								rect.Width, rect.Height, radius,
								tintColor, tintAlpha, selected, enabled, bright);
							sc.W = rect.Width; sc.H = rect.Height; sc.Radius = radius;
							sc.Preset = Preset; sc.TintArgb = tintColor.ToArgb(); sc.TintAlpha = tintAlpha;
							sc.Selected = selected; sc.Enabled = enabled; sc.IsLight = bright;
						}
						DrawPremultipliedAlpha(g, sc.Image, ref sc.HBitmap, rect, awaken, true);
					}
					else
					{
						shell = BakeGlassShellBitmap(
							rect.Width, rect.Height, radius,
							tintColor, tintAlpha, selected, enabled, bright);
						disposeShell = true;
						IntPtr tmpHb = IntPtr.Zero;
						DrawPremultipliedAlpha(g, shell, ref tmpHb, rect, awaken, false);
						if (disposeShell && shell != null) shell.Dispose();
					}
				}
				else
				{
					if (ctrl != null)
					{
						PillCache cache = pillCaches.GetValue(ctrl, c =>
						{
							var pc = new PillCache();
							c.Disposed += delegate { pc.Release(); };
							return pc;
						});
						bool match = cache.Image != null
							&& cache.W == rect.Width && cache.H == rect.Height && cache.Radius == radius
							&& cache.Ox == ox && cache.Oy == oy && cache.CanvasW == canvasW && cache.CanvasH == canvasH
							&& cache.Seq == seq && cache.Preset == Preset && cache.Blur == UiTheme.GlassBlur
							&& cache.TintArgb == tintColor.ToArgb() && cache.TintAlpha == tintAlpha
							&& cache.Selected == selected && cache.Enabled == enabled && cache.IsLight == bright
							&& cache.HasSource == (source != null);
						if (!match)
						{
							cache.Release();
							cache.Image = BakeRefractedPillBitmap(
								rect.Width, rect.Height, radius,
								source, canvasW, canvasH, ox, oy,
								tintColor, tintAlpha, selected, enabled, bright);
							cache.W = rect.Width; cache.H = rect.Height; cache.Radius = radius;
							cache.Ox = ox; cache.Oy = oy; cache.CanvasW = canvasW; cache.CanvasH = canvasH;
							cache.Seq = seq; cache.Preset = Preset; cache.Blur = UiTheme.GlassBlur;
							cache.TintArgb = tintColor.ToArgb(); cache.TintAlpha = tintAlpha;
							cache.Selected = selected; cache.Enabled = enabled; cache.IsLight = bright;
							cache.HasSource = (source != null);
						}
						DrawPremultipliedAlpha(g, cache.Image, ref cache.HBitmap, rect, awaken, true);
					}
					else
					{
						using (Bitmap rendered = BakeRefractedPillBitmap(
							rect.Width, rect.Height, radius,
							source, canvasW, canvasH, ox, oy,
							tintColor, tintAlpha, selected, enabled, bright))
						{
							IntPtr tmpHb = IntPtr.Zero;
							DrawPremultipliedAlpha(g, rendered, ref tmpHb, rect, awaken, false);
						}
					}
				}
			}

			// Fast dynamic interactive overlays (pre-baked radial glow sprite, press depth, soft liquid sheen)
			if (effectiveHover > 0.01f || effectivePress > 0.01f || sheenGlow > 0.01f)
			{
				using (GraphicsPath path = UiTheme.RoundedRect(rect, radius))
				{
					GraphicsState st = g.Save();
					try
					{
						g.SetClip(path, CombineMode.Intersect);
						if (effectiveHover > 0.01f && enabled)
						{
							int ha = (int)(effectiveHover * (bright ? 34f : 18f));
							using (var hb = new SolidBrush(Color.FromArgb(ha, Color.White)))
								g.FillRectangle(hb, rect);

							// Cursor-tracked delicate optical reflection via pre-baked 64x64 PArgb sprite (zero PathGradientBrush!)
							if (pointerLocal.X >= 0 && pointerLocal.Y >= 0 && pointerLocal.X <= rect.Width + 20)
							{
								int spotR = Math.Max(32, Math.Min(86, rect.Width / 2));
								Rectangle spotRect = new Rectangle(
									rect.X + pointerLocal.X - spotR,
									rect.Y + pointerLocal.Y - spotR,
									spotR * 2, spotR * 2);
								Bitmap sprite = GetHoverGlowSprite(effectiveHover);
								g.InterpolationMode = InterpolationMode.Low;
								g.DrawImage(sprite, spotRect);
							}
						}
						if (effectivePress > 0.01f && enabled)
						{
							int pa = (int)(effectivePress * 36f);
							using (var pb = new SolidBrush(Color.FromArgb(pa, 0, 0, 0)))
								g.FillRectangle(pb, rect);
						}
						if (sheenGlow > 0.01f)
						{
							float bandW = Math.Max(36f, rect.Width * 0.40f);
							float cx = rect.X - bandW + (rect.Width + bandW * 2f) * sheenT;
							RectangleF sheenRect = new RectangleF(cx, rect.Y - 4, bandW, rect.Height + 8);
							int sa = Math.Max(0, Math.Min(78, (int)(sheenGlow * 68f)));
							using (var sb = new LinearGradientBrush(sheenRect, Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White), 20f))
							{
								var cb = new ColorBlend(3);
								cb.Positions = new[] { 0f, 0.5f, 1f };
								cb.Colors = new[] { Color.FromArgb(0, Color.White), Color.FromArgb(sa, Color.White), Color.FromArgb(0, Color.White) };
								sb.InterpolationColors = cb;
								g.FillRectangle(sb, sheenRect);
							}
						}
					}
					finally { g.Restore(st); }
				}
			}
		}

		/// <summary>
		/// Position-independent Direction C optical lens shell (dark cut-edge absorption + directional
		/// Fresnel highlight + corner caustics + restrained tint) cached once per pill size/style so
		/// sliding segmented controls and live wallpapers render at 60-120fps (<0.25ms/frame).
		/// </summary>
		private static unsafe Bitmap BakeGlassShellBitmap(
			int pw, int ph, int radius,
			Color tintColor, int tintAlpha,
			bool selected, bool enabled, bool bright)
		{
			var result = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
			bool isBigHero = ph >= 42;
			float rad = Math.Max(3f, Math.Min(radius, Math.Min(pw, ph) * 0.5f));
			float rimThick = isBigHero ? Math.Min(rad * 0.85f, 10.5f) : Math.Min(rad * 0.85f, 8.8f);
			float halfBoxW = Math.Max(0f, pw * 0.5f - rad);
			float halfBoxH = Math.Max(0f, ph * 0.5f - rad);
			float cx = pw * 0.5f;
			float cy = ph * 0.5f;

			float tintA = 0f;
			if (!enabled)
			{
				tintA = bright ? 0.12f : 0.08f;
			}
			else if (tintAlpha > 0 && tintColor != Color.White)
			{
				tintA = Math.Max(0.16f, Math.Min(0.48f, tintAlpha / 255f));
			}
			else if (selected)
			{
				tintA = bright ? 0.36f : 0.13f;
			}
			else if (Preset == PresetDeep)
			{
				tintA = 0.24f;
			}
			float tR = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 10f : tintColor.R;
			float tG = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 16f : tintColor.G;
			float tB = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 28f : tintColor.B;
			float specGain = !enabled ? 0.50f : (isBigHero ? 1.12f : 1.0f);

			BitmapData dstData = result.LockBits(new Rectangle(0, 0, pw, ph), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
			try
			{
				byte* dstBase = (byte*)dstData.Scan0;
				int dstStride = dstData.Stride;

				for (int y = 0; y < ph; y++)
				{
					byte* row = dstBase + y * dstStride;
					float py = y + 0.5f - cy;
					float yNorm = (y + 0.5f) / ph;
					float groundDark = yNorm * (bright ? 0.03f : 0.08f);

					for (int x = 0; x < pw; x++)
					{
						float px = x + 0.5f - cx;
						float absPx = px < 0f ? -px : px;
						float absPy = py < 0f ? -py : py;
						float qx = absPx > halfBoxW ? absPx - halfBoxW : 0f;
						float qy = absPy > halfBoxH ? absPy - halfBoxH : 0f;
						float distCore = (float)Math.Sqrt(qx * qx + qy * qy);
						float distToEdge = rad - distCore;

						if (distToEdge < -0.85f)
						{
							row[x * 4 + 0] = 0;
							row[x * 4 + 1] = 0;
							row[x * 4 + 2] = 0;
							row[x * 4 + 3] = 0;
							continue;
						}

						float edgeAlpha = distToEdge >= 0.85f ? 1f : (distToEdge + 0.85f) / 1.70f;
						if (edgeAlpha < 0f) edgeAlpha = 0f;
						else if (edgeAlpha > 1f) edgeAlpha = 1f;

						float absA = 0f, rimSpec = 0f, caustic = 0f;
						if (distToEdge < rimThick)
						{
							float d = distToEdge > 0f ? distToEdge : 0f;
							float nx = distCore > 0.001f ? ((px < 0f ? -qx : qx) / distCore) : 0f;
							float ny = distCore > 0.001f ? ((py < 0f ? -qy : qy) / distCore) : 0f;

							if (d < 0.95f) absA = (1f - d / 0.95f) * (bright ? 0.16f : 0.30f);

							float dirTL = -(nx * 0.65f + ny * 0.76f);
							if (dirTL < 0f) dirTL = 0f;
							float dirBR = (nx * 0.65f + ny * 0.76f);
							if (dirBR < 0f) dirBR = 0f;

							float u = (d - 1.05f) / 0.52f;
							float glint = (float)Math.Exp(-(u * u));
							float dirGain = (float)Math.Pow(dirTL, 2.15) + (float)Math.Pow(dirBR, 3.0) * 0.42f;
							rimSpec = glint * dirGain * (bright ? 155f : 138f) * specGain;

							float cu = (d - 3.6f) / 1.8f;
							caustic = (float)Math.Exp(-(cu * cu)) * (dirTL * dirTL * 14f + dirBR * dirBR * 18f) * specGain;
						}

						// Premultiplied Porter-Duff composite: base tint/grounding -> dark cut-edge -> Fresnel glint + caustic
						float pr, pg, pb, pa;
						if (tintA > 0f)
						{
							pa = tintA;
							pr = tR * tintA;
							pg = tG * tintA;
							pb = tB * tintA;
						}
						else
						{
							pa = groundDark;
							pr = 0f; pg = 0f; pb = 0f;
						}

						if (absA > 0f)
						{
							float invAbs = 1f - absA;
							pr *= invAbs;
							pg *= invAbs;
							pb *= invAbs;
							pa = absA + pa * invAbs;
						}

						float specA = (rimSpec + caustic) / 255f;
						if (specA > 1f) specA = 1f;
						if (specA > 0f)
						{
							float invS = 1f - specA;
							float s255 = 255f * specA;
							pr = s255 + pr * invS;
							pg = s255 + pg * invS;
							pb = s255 + pb * invS;
							pa = specA + pa * invS;
						}

						pr *= edgeAlpha;
						pg *= edgeAlpha;
						pb *= edgeAlpha;
						pa *= edgeAlpha;

						if (pr > 255f) pr = 255f; else if (pr < 0f) pr = 0f;
						if (pg > 255f) pg = 255f; else if (pg < 0f) pg = 0f;
						if (pb > 255f) pb = 255f; else if (pb < 0f) pb = 0f;
						if (pa > 1f) pa = 1f; else if (pa < 0f) pa = 0f;

						row[x * 4 + 0] = (byte)(pb + 0.5f);
						row[x * 4 + 1] = (byte)(pg + 0.5f);
						row[x * 4 + 2] = (byte)(pr + 0.5f);
						row[x * 4 + 3] = (byte)(pa * 255f + 0.5f);
					}
				}
			}
			finally
			{
				result.UnlockBits(dstData);
			}
			return result;
		}

		/// <summary>
		/// Zero-plastic optical liquid lens shader (Direction C):
		/// - Cubic convex rim refraction + subtle chromatic dispersion
		/// - Preserved & enriched background vibrancy (NO milky white wash!)
		/// - 0.95px dark cut-edge glass absorption + razor-thin directional Fresnel glint (NO chalky halo, NO jelly dome!)
		/// </summary>
		private static unsafe Bitmap BakeRefractedPillBitmap(
			int pw, int ph, int radius,
			Bitmap source, int canvasW, int canvasH, int ox, int oy,
			Color tintColor, int tintAlpha,
			bool selected, bool enabled, bool bright)
		{
			var result = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
			bool isBigHero = ph >= 42;
			float rad = Math.Max(3f, Math.Min(radius, Math.Min(pw, ph) * 0.5f));
			float rimThick = isBigHero ? Math.Min(rad * 0.85f, 10.5f) : Math.Min(rad * 0.85f, 8.8f);
			float baseStrength = Preset == PresetCrystal ? 7.4f : (Preset == PresetDeep ? 4.8f : 6.4f);
			float strength = isBigHero ? baseStrength * 1.12f : baseStrength;
			float chroma = Preset == PresetDeep ? 0.35f : 0.62f;
			float satBoost = Preset == PresetDeep ? 1.08f : 1.18f;
			float halfBoxW = Math.Max(0f, pw * 0.5f - rad);
			float halfBoxH = Math.Max(0f, ph * 0.5f - rad);
			float cx = pw * 0.5f;
			float cy = ph * 0.5f;

			// Restrained tint ONLY on primary CTA / active states; ZERO milky white veil on clear optical lenses!
			float tintA = 0f;
			if (!enabled)
			{
				tintA = bright ? 0.12f : 0.08f;
			}
			else if (tintAlpha > 0 && tintColor != Color.White)
			{
				tintA = Math.Max(0.16f, Math.Min(0.48f, tintAlpha / 255f));
			}
			else if (selected)
			{
				tintA = bright ? 0.36f : 0.13f;
			}
			else if (Preset == PresetDeep)
			{
				tintA = 0.24f;
			}
			float tR = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 10f : tintColor.R;
			float tG = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 16f : tintColor.G;
			float tB = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 28f : tintColor.B;

			int sw = 0, sh = 0;
			Rectangle srcMap = Rectangle.Empty;
			BitmapData srcData = null;
			Bitmap lockedSrc = null;
			try
			{
				if (source != null && source.Width > 2 && source.Height > 2 && canvasW > 0 && canvasH > 0)
				{
					srcMap = UiTheme.GlassSourceRectanglePublic(source, canvasW, canvasH, ox, oy);
					sw = source.Width;
					sh = source.Height;
					if (source.PixelFormat == PixelFormat.Format32bppArgb || source.PixelFormat == PixelFormat.Format32bppPArgb || source.PixelFormat == PixelFormat.Format32bppRgb)
					{
						lockedSrc = source;
						srcData = source.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, source.PixelFormat);
					}
				}

				BitmapData dstData = result.LockBits(new Rectangle(0, 0, pw, ph), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
				try
				{
					byte* dstBase = (byte*)dstData.Scan0;
					int dstStride = dstData.Stride;
					byte* srcBase = srcData != null ? (byte*)srcData.Scan0 : null;
					int srcStride = srcData != null ? srcData.Stride : 0;
					float mapScaleX = srcMap.Width > 0 ? (float)sw / srcMap.Width : 0f;
					float mapScaleY = srcMap.Height > 0 ? (float)sh / srcMap.Height : 0f;

					float fallbackR = bright ? 225f : 22f;
					float fallbackG = bright ? 236f : 32f;
					float fallbackB = bright ? 246f : 48f;
					float specGain = !enabled ? 0.50f : (isBigHero ? 1.12f : 1.0f);

					for (int y = 0; y < ph; y++)
					{
						byte* row = dstBase + y * dstStride;
						float py = y + 0.5f - cy;
						float yNorm = (y + 0.5f) / ph;
						// Subtle vertical depth grounding (zero jelly white dome on top!)
						float ground = 1f - yNorm * (bright ? 0.03f : 0.08f);

						for (int x = 0; x < pw; x++)
						{
							float px = x + 0.5f - cx;
							float absPx = px < 0f ? -px : px;
							float absPy = py < 0f ? -py : py;
							float qx = absPx > halfBoxW ? absPx - halfBoxW : 0f;
							float qy = absPy > halfBoxH ? absPy - halfBoxH : 0f;
							float distCore = (float)Math.Sqrt(qx * qx + qy * qy);
							float distToEdge = rad - distCore;

							if (distToEdge < -0.85f)
							{
								row[x * 4 + 0] = 0;
								row[x * 4 + 1] = 0;
								row[x * 4 + 2] = 0;
								row[x * 4 + 3] = 0;
								continue;
							}

							float edgeAlpha = distToEdge >= 0.85f ? 1f : (distToEdge + 0.85f) / 1.70f;
							if (edgeAlpha < 0f) edgeAlpha = 0f;
							else if (edgeAlpha > 1f) edgeAlpha = 1f;

							float dx = 0f, dy = 0f, rimSpec = 0f, rimAbsorb = 1f, caustic = 0f;
							if (distToEdge < rimThick)
							{
								float d = distToEdge > 0f ? distToEdge : 0f;
								float t = 1f - d / rimThick;
								// Smooth cubic optical bend right near the cut bevel
								float bend = t * t * t * strength;
								float nx = distCore > 0.001f ? ((px < 0f ? -qx : qx) / distCore) : 0f;
								float ny = distCore > 0.001f ? ((py < 0f ? -qy : qy) / distCore) : 0f;
								dx = -nx * bend;
								dy = -ny * bend;

								// 1) Physical dark cut-edge glass absorption (eliminates plastic sticker edge!)
								if (d < 0.95f) rimAbsorb = 1f - (1f - d / 0.95f) * (bright ? 0.16f : 0.30f);

								// 2) Razor-thin directional Fresnel highlight (strictly top-left & tiny bottom-right, ZERO chalky base!)
								float dirTL = -(nx * 0.65f + ny * 0.76f);
								if (dirTL < 0f) dirTL = 0f;
								float dirBR = (nx * 0.65f + ny * 0.76f);
								if (dirBR < 0f) dirBR = 0f;

								float u = (d - 1.05f) / 0.52f;
								float glint = (float)Math.Exp(-(u * u));
								float dirGain = (float)Math.Pow(dirTL, 2.15) + (float)Math.Pow(dirBR, 3.0) * 0.42f;
								rimSpec = glint * dirGain * (bright ? 155f : 138f) * specGain;

								// 3) Restrained internal corner caustic bounce
								float cu = (d - 3.6f) / 1.8f;
								caustic = (float)Math.Exp(-(cu * cu)) * (dirTL * dirTL * 14f + dirBR * dirBR * 18f) * specGain;
							}

							float rS = fallbackR, gS = fallbackG, bS = fallbackB;
							if (srcBase != null)
							{
								float fy = (y + dy - srcMap.Top) * mapScaleY;
								float fxG = (x + dx - srcMap.Left) * mapScaleX;
								float fxR = (x + dx * (1f + chroma * 0.07f) - srcMap.Left) * mapScaleX;
								float fxB = (x + dx * (1f - chroma * 0.07f) - srcMap.Left) * mapScaleX;
								rS = SampleChannel(srcBase, srcStride, sw, sh, fxR, fy, 2);
								gS = SampleChannel(srcBase, srcStride, sw, sh, fxG, fy, 1);
								bS = SampleChannel(srcBase, srcStride, sw, sh, fxB, fy, 0);
							}

							// Enrich background vibrancy so glass never looks washed-out or milky
							float lum = 0.2126f * rS + 0.7152f * gS + 0.0722f * bS;
							rS = (lum + (rS - lum) * satBoost) * ground;
							gS = (lum + (gS - lum) * satBoost) * ground;
							bS = (lum + (bS - lum) * satBoost) * ground;

							if (tintA > 0f)
							{
								float invT = 1f - tintA;
								rS = rS * invT + tR * tintA;
								gS = gS * invT + tG * tintA;
								bS = bS * invT + tB * tintA;
							}

							float rOut = rS * rimAbsorb + rimSpec + caustic;
							float gOut = gS * rimAbsorb + rimSpec + caustic;
							float bOut = bS * rimAbsorb + rimSpec + caustic;

							if (rOut > 255f) rOut = 255f; else if (rOut < 0f) rOut = 0f;
							if (gOut > 255f) gOut = 255f; else if (gOut < 0f) gOut = 0f;
							if (bOut > 255f) bOut = 255f; else if (bOut < 0f) bOut = 0f;

							byte aByte = (byte)(edgeAlpha * 255f + 0.5f);
							row[x * 4 + 0] = (byte)(bOut * edgeAlpha + 0.5f);
							row[x * 4 + 1] = (byte)(gOut * edgeAlpha + 0.5f);
							row[x * 4 + 2] = (byte)(rOut * edgeAlpha + 0.5f);
							row[x * 4 + 3] = aByte;
						}
					}
				}
				finally
				{
					result.UnlockBits(dstData);
				}
			}
			finally
			{
				if (lockedSrc != null && srcData != null) lockedSrc.UnlockBits(srcData);
			}
			return result;
		}

		private static unsafe float SampleChannel(byte* srcBase, int stride, int w, int h, float fx, float fy, int ch)
		{
			if (fx < 0f) fx = 0f; else if (fx > w - 1.001f) fx = w - 1.001f;
			if (fy < 0f) fy = 0f; else if (fy > h - 1.001f) fy = h - 1.001f;
			int x0 = (int)fx;
			int y0 = (int)fy;
			int x1 = x0 + 1 < w ? x0 + 1 : x0;
			int y1 = y0 + 1 < h ? y0 + 1 : y0;
			float tx = fx - x0;
			float ty = fy - y0;
			byte* r0 = srcBase + y0 * stride;
			byte* r1 = srcBase + y1 * stride;
			float top = r0[x0 * 4 + ch] + (r0[x1 * 4 + ch] - r0[x0 * 4 + ch]) * tx;
			float bot = r1[x0 * 4 + ch] + (r1[x1 * 4 + ch] - r1[x0 * 4 + ch]) * tx;
			return top + (bot - top) * ty;
		}
	}
}
