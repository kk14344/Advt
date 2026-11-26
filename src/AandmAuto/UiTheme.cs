using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AandmAuto
{
	internal static class UiTheme
	{
		internal const int ThemeClassic = 0;
		internal const int ThemeParchment = 1;
		internal const int ThemeNativeLight = 2;
		internal const int ThemeWarmWood = 3;
		internal const int ThemeLiquidGlass = 4;

		private static readonly PrivateFontCollection gameFonts = new PrivateFontCollection();
		private static IntPtr fontMemory;
		[DllImport("gdi32.dll")]
		private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint size, IntPtr reserved, ref uint count);
		private static readonly string gameFontFamily = ResolveGameFontFamily();
		private static readonly string uiCleanFontFamily = ResolveCleanUiFontFamily();
		private static readonly string glassUiFontFamily = ResolveGlassUiFontFamily();

		public static string GameFontFamily { get { return gameFontFamily; } }
		internal static string ButtonLabelStyle = "B";

		internal static int CurrentTheme = ThemeClassic;
		internal static int GlassBlur = 12;
		internal static bool ExtremePerformanceMode = false;
		internal const int BgModeStatic = 0;
		internal const int BgModeAnimated = 1;
		internal const int BgModeDesktopPassthrough = 2;
		internal const int BgModeEngine = 3;
		internal static Color BackgroundVeilColor
		{
			get { return CurrentTheme == ThemeLiquidGlass ? Color.FromArgb(14, 20, 32) : Palette.SideOuterColor; }
		}
		internal static Bitmap EngineFrame { get; private set; }
        internal static System.Windows.Forms.Form EngineCanvasOwner { get; set; }

        internal static bool UsesEngineCanvas(System.Windows.Forms.Control control)
        {
            return EngineFrame != null && EngineCanvasOwner != null && control != null &&
                !ExtremePerformanceMode && CustomBackgroundMode == BgModeEngine && control.FindForm() == EngineCanvasOwner;
        }

        private static bool IsOutsideEngineCanvas(System.Windows.Forms.Control control)
        {
            return EngineFrame != null && EngineCanvasOwner != null && control != null &&
                control.FindForm() != EngineCanvasOwner;
        }
		internal static Bitmap EngineVeiledFrame { get; private set; }
		internal static long WallpaperFrameSequence { get; private set; }
		private static bool engineFrameOwned = true;
		private static Color engineVeilColor;
		private static int engineVeilAlpha = -1;
		private static float[] blurredLuminanceGrid;

		internal static int EffectiveVeilAlpha
		{
			get
			{
				int pct = Math.Max(10, Math.Min(90, CustomBackgroundOpacity));
				if (CurrentTheme == ThemeLiquidGlass)
				{
					double scale = LiquidGlassEngine.Preset == LiquidGlassEngine.PresetCrystal ? 24.0
						: (LiquidGlassEngine.Preset == LiquidGlassEngine.PresetDeep ? 115.0 : 52.0);
					int va = (int)Math.Round(scale * (1.0 - pct / 100.0));
					return Math.Max(4, Math.Min(160, va));
				}
				int veilAlpha = (int)Math.Round(255.0 * (1.0 - pct / 100.0));
				return Math.Max(18, Math.Min(235, veilAlpha));
			}
		}

		internal static void SetEngineFrame(Bitmap frame, Bitmap glassFrame = null)
		{
			Bitmap previous = ExchangeEngineFrame(frame, glassFrame);
			if (previous != null) previous.Dispose();
		}

		internal static Bitmap ExchangeEngineFrame(Bitmap frame, Bitmap glassFrame = null)
		{
			Bitmap previous = engineFrameOwned ? EngineFrame : null;
			EngineFrame = frame;
			EngineVeiledFrame = null;
			engineFrameOwned = true;
			engineVeilAlpha = -1;
			WallpaperFrameSequence++;
			InvalidateCropCache();
			InvalidateBlurCache();
			if (glassFrame != null)
			{
				blurredCache = glassFrame;
				blurredCacheSource = frame;
				blurredCacheLevel = GlassBlur;
			}
			return previous;
		}

		internal static void SetEnginePreparedFrame(Bitmap rawFrame, Bitmap veiledFrame, Color veilColor, int veilAlpha, Bitmap glassFrame, float[] luminanceGrid)
		{
			if (engineFrameOwned && EngineFrame != null) EngineFrame.Dispose();
			EngineFrame = rawFrame;
			EngineVeiledFrame = veiledFrame;
			engineFrameOwned = false;
			engineVeilColor = veilColor;
			engineVeilAlpha = veilAlpha;
			WallpaperFrameSequence++;
			InvalidateCropCache();
			if (glassFrame != null)
			{
				if (blurredCache != null) blurredCache.Dispose();
				blurredCache = glassFrame;
				blurredCacheSource = rawFrame;
				blurredCacheLevel = GlassBlur;
				blurredLuminanceGrid = luminanceGrid;
			}
            else if (blurredCache != null)
            {
                InvalidateBlurCache();
            }
		}
		internal const int BgFitCover = 0;
		internal const int BgFitContain = 1;

		private static Bitmap staticBackground;
		private static AnimatedBackground animatedBackground;
		private static Bitmap liveDesktopFrame;
		private static Bitmap defaultGlassBackdrop;
		private static Bitmap minimalGlassBackdrop;
		private static Bitmap blurredCache;
		private static Image blurredCacheSource;
		private static int blurredCacheLevel = -1;

		internal static int CustomBackgroundMode = BgModeStatic;
		internal static int CustomBackgroundFit = BgFitCover;
		internal static string CustomBackgroundPath { get; private set; }
		internal static int CustomBackgroundOpacity = 35;
		internal static int CustomBgCropX = 0;
		internal static int CustomBgCropY = 0;
		internal static int CustomBgCropW = 10000;
		internal static int CustomBgCropH = 10000;
		private static Bitmap croppedCache;
		private static Image croppedCacheSource;
		private static int croppedKeyX;
		private static int croppedKeyY;
		private static int croppedKeyW;
		private static int croppedKeyH;

		internal static bool HasCustomCrop
		{
			get
			{
				return (CurrentTheme == ThemeLiquidGlass || CustomBackgroundMode == BgModeEngine) &&
					(CustomBgCropX > 0 || CustomBgCropY > 0 || CustomBgCropW < 10000 || CustomBgCropH < 10000);
			}
		}

		internal static void SetCustomCrop(int x, int y, int w, int h)
		{
			int cw = Math.Max(500, Math.Min(10000, w));
			int ch = Math.Max(500, Math.Min(10000, h));
			int cx = Math.Max(0, Math.Min(10000 - cw, x));
			int cy = Math.Max(0, Math.Min(10000 - ch, y));
			CustomBgCropX = cx;
			CustomBgCropY = cy;
			CustomBgCropW = cw;
			CustomBgCropH = ch;
			InvalidateCropCache();
			InvalidateBlurCache();
		}

		internal static Image CustomBackground
		{
			get
			{
				if (ExtremePerformanceMode) return null;
				if (CustomBackgroundMode == BgModeEngine) return EngineFrame;
				if (CustomBackgroundMode == BgModeDesktopPassthrough)
				{
					return liveDesktopFrame ?? staticBackground;
				}
				if (HasAnimatedBackground) return animatedBackground.Frame;
				return staticBackground;
			}
		}

		internal static bool HasAnimatedBackground
		{
			get { return !ExtremePerformanceMode && CustomBackgroundMode == BgModeAnimated && animatedBackground != null; }
		}

		internal static string BgModeName(int mode)
		{
			switch (mode)
			{
			case BgModeAnimated: return "动态预览图（GIF 动图）";
			case BgModeEngine: return "助手独立动态";
			case BgModeDesktopPassthrough: return "透出桌面壁纸引擎（实时）";
			default: return "静态高清（最省资源）";
			}
		}

		internal static string BgFitName(int fit)
		{
			return fit == BgFitContain ? "完整原图（零裁剪）" : "整窗合一铺满";
		}

		private static readonly UiPalette ClassicPalette = new UiPalette(
			Color.FromArgb(41, 32, 20),
			Color.FromArgb(63, 51, 32),
			Color.FromArgb(87, 73, 42),
			Color.FromArgb(47, 38, 22),
			Color.FromArgb(47, 38, 22),
			Color.FromArgb(123, 94, 48),
			Color.FromArgb(185, 194, 158, 88),
			Color.FromArgb(123, 94, 48),
			Color.FromArgb(238, 209, 143),
			Color.FromArgb(242, 232, 205),
			Color.FromArgb(171, 156, 125),
			Color.FromArgb(140, 214, 104),
			Color.FromArgb(121, 189, 238),
			Color.FromArgb(235, 107, 95),
			Color.FromArgb(245, 198, 92),
			Color.FromArgb(41, 33, 23),
			Color.FromArgb(145, 185, 149, 76),
			Color.Black,
			Color.FromArgb(47, 38, 22));

		private static readonly UiPalette ParchmentPalette = new UiPalette(
			Color.FromArgb(61, 49, 34),
			Color.FromArgb(61, 49, 34),
			Color.FromArgb(243, 228, 188),
			Color.FromArgb(251, 243, 219),
			Color.FromArgb(251, 243, 219),
			Color.FromArgb(158, 122, 68),
			Color.FromArgb(180, 140, 82),
			Color.FromArgb(189, 160, 103),
			Color.FromArgb(92, 50, 9),
			Color.FromArgb(54, 35, 16),
			Color.FromArgb(115, 88, 52),
			Color.FromArgb(43, 110, 18),
			Color.FromArgb(31, 90, 138),
			Color.FromArgb(168, 36, 25),
			Color.FromArgb(143, 90, 8),
			Color.FromArgb(245, 232, 192),
			Color.FromArgb(158, 122, 68),
			Color.FromArgb(34, 20, 6),
			Color.FromArgb(255, 248, 224));

		private static readonly UiPalette NativeLightPalette = new UiPalette(
			Color.FromArgb(244, 243, 239),
			Color.FromArgb(244, 243, 239),
			Color.FromArgb(255, 255, 255),
			Color.FromArgb(250, 249, 246),
			Color.FromArgb(255, 255, 255),
			Color.FromArgb(214, 211, 202),
			Color.FromArgb(44, 110, 73),
			Color.FromArgb(224, 221, 213),
			Color.FromArgb(31, 31, 31),
			Color.FromArgb(31, 31, 31),
			Color.FromArgb(102, 98, 90),
			Color.FromArgb(44, 110, 73),
			Color.FromArgb(34, 98, 158),
			Color.FromArgb(182, 45, 38),
			Color.FromArgb(146, 98, 14),
			Color.FromArgb(248, 247, 243),
			Color.FromArgb(214, 211, 202),
			Color.FromArgb(31, 31, 31),
			Color.FromArgb(255, 255, 255));

		private static readonly UiPalette WarmWoodPalette = new UiPalette(
			Color.FromArgb(140, 75, 31),
			Color.FromArgb(140, 75, 31),
			Color.FromArgb(251, 232, 190),
			Color.FromArgb(255, 246, 220),
			Color.FromArgb(255, 246, 220),
			Color.FromArgb(99, 51, 18),
			Color.FromArgb(199, 123, 64),
			Color.FromArgb(148, 93, 42),
			Color.FromArgb(71, 36, 10),
			Color.FromArgb(59, 30, 10),
			Color.FromArgb(110, 70, 33),
			Color.FromArgb(43, 122, 22),
			Color.FromArgb(32, 88, 136),
			Color.FromArgb(168, 38, 24),
			Color.FromArgb(156, 92, 14),
			Color.FromArgb(255, 244, 212),
			Color.FromArgb(41, 18, 4),
			Color.FromArgb(43, 22, 6),
			Color.FromArgb(255, 248, 228));

		private static readonly UiPalette LiquidGlassPalette = new UiPalette(
			Color.FromArgb(18, 24, 36),
			Color.FromArgb(20, 27, 40),
			Color.FromArgb(30, 39, 56),
			Color.FromArgb(24, 33, 48),
			Color.FromArgb(16, 22, 33),
			Color.FromArgb(115, 215, 232, 255),
			Color.FromArgb(94, 192, 255),
			Color.FromArgb(76, 185, 215, 248),
			Color.FromArgb(168, 224, 255),
			Color.FromArgb(245, 249, 255),
			Color.FromArgb(182, 198, 220),
			Color.FromArgb(108, 232, 168),
			Color.FromArgb(124, 206, 255),
			Color.FromArgb(255, 134, 128),
			Color.FromArgb(255, 212, 118),
			Color.FromArgb(26, 36, 54),
			Color.FromArgb(120, 215, 235, 255),
			Color.White,
			Color.FromArgb(24, 33, 48));

		internal static bool IsLightTheme
		{
			get { return CurrentTheme == ThemeParchment || CurrentTheme == ThemeNativeLight || CurrentTheme == ThemeWarmWood; }
		}

		internal static UiPalette Palette
		{
			get { return PaletteFor(CurrentTheme); }
		}

		internal static UiPalette PaletteFor(int theme)
		{
			switch (theme)
			{
			case ThemeParchment: return ParchmentPalette;
			case ThemeNativeLight: return NativeLightPalette;
			case ThemeWarmWood: return WarmWoodPalette;
			case ThemeLiquidGlass: return LiquidGlassPalette;
			default: return ClassicPalette;
			}
		}

		internal static string ThemeName(int theme)
		{
			switch (theme)
			{
			case ThemeParchment: return "羊皮纸书页";
			case ThemeNativeLight: return "原生浅灰";
			case ThemeWarmWood: return "复古暖木";
			case ThemeLiquidGlass: return "液态玻璃";
			default: return "现版暗褐";
			}
		}

		private static string ResolveGameFontFamily()
		{
			try
			{
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.fonts.general.ttf"))
				{
					if (stream != null)
					{
						byte[] bytes = new byte[stream.Length];
						int read = 0;
						while (read < bytes.Length)
						{
							int n = stream.Read(bytes, read, bytes.Length - read);
							if (n <= 0) break;
							read += n;
						}
						fontMemory = Marshal.AllocHGlobal(bytes.Length);
						Marshal.Copy(bytes, 0, fontMemory, bytes.Length);
						uint count = 0;
						AddFontMemResourceEx(fontMemory, (uint)bytes.Length, IntPtr.Zero, ref count);
						gameFonts.AddMemoryFont(fontMemory, bytes.Length);
						if (gameFonts.Families.Length > 0) return gameFonts.Families[0].Name;
					}
				}
				return ResolveCleanUiFontFamily();
			}
			catch { }
			return SystemFonts.MessageBoxFont.FontFamily.Name;
		}

		private static string ResolveCleanUiFontFamily()
		{
			try
			{
				string[] preferred = { "Microsoft YaHei UI", "Microsoft YaHei", "黑体", "SimHei" };
				string[] installed = FontFamily.Families.Select(f => f.Name).ToArray();
				foreach (string name in preferred)
					if (installed.Contains(name)) return name;
			}
			catch { }
			return SystemFonts.MessageBoxFont.FontFamily.Name;
		}

		private static string ResolveGlassUiFontFamily()
		{
			try
			{
				string[] preferred =
				{
					"PingFang SC",
					"SF Pro Text",
					"MiSans",
					"HarmonyOS Sans SC",
					"Noto Sans SC",
					"Noto Sans SC Medium",
					"DengXian",
					"Microsoft YaHei UI",
					"Microsoft YaHei"
				};
				string[] installed = FontFamily.Families.Select(f => f.Name).ToArray();
				foreach (string name in preferred)
					if (installed.Contains(name)) return name;
			}
			catch { }
			return ResolveCleanUiFontFamily();
		}

		public static Font GameFont(float size, FontStyle style = FontStyle.Regular)
		{
			if (ExtremePerformanceMode)
			{
				return new Font(uiCleanFontFamily, size, style, GraphicsUnit.Point);
			}
			if (CurrentTheme == ThemeLiquidGlass)
			{
				return new Font(glassUiFontFamily, size * 1.01f, style, GraphicsUnit.Point);
			}
			if (CurrentTheme == ThemeNativeLight)
			{
				return new Font(uiCleanFontFamily, size * 1.05f, style, GraphicsUnit.Point);
			}
			float larger = size * 1.15f;
			if (gameFonts.Families.Length > 0) return new Font(gameFonts.Families[0], larger, style, GraphicsUnit.Point);
			return new Font(GameFontFamily, larger, style, GraphicsUnit.Point);
		}

		public static Font UiFont(float size, FontStyle style = FontStyle.Regular)
		{
            return GameFont(size, style);
        }

		internal static bool LoadCustomBackground(string path, int opacity)
		{
			return LoadCustomBackground(path, opacity, CustomBackgroundMode);
		}

		internal static bool LoadCustomBackground(string path, int opacity, int mode)
		{
			if (ExtremePerformanceMode)
			{
				CustomBackgroundOpacity = Math.Max(10, Math.Min(90, opacity));
				CustomBackgroundMode = BgModeStatic;
				SetEngineFrame(null);
				ClearStaticAndAnimated();
				ClearLiveDesktopFrame();
				CustomBackgroundPath = "";
				return false;
			}
			CustomBackgroundOpacity = Math.Max(10, Math.Min(90, opacity));
			bool keepEngineFrame = CustomBackgroundMode == BgModeEngine && mode == BgModeEngine;
			CustomBackgroundMode = Math.Max(0, Math.Min(BgModeEngine, mode));
			if (!keepEngineFrame) SetEngineFrame(null);
			ClearStaticAndAnimated();
			if (CustomBackgroundMode != BgModeDesktopPassthrough) ClearLiveDesktopFrame();
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				CustomBackgroundPath = "";
				return CustomBackgroundMode == BgModeDesktopPassthrough;
			}
			try
			{
				string staticCandidate = ResolveStaticBgFile(path);
				if (!string.IsNullOrEmpty(staticCandidate) && File.Exists(staticCandidate))
				{
					byte[] bytes = File.ReadAllBytes(staticCandidate);
					using (var ms = new MemoryStream(bytes))
					using (var img = Image.FromStream(ms))
					{
						staticBackground = new Bitmap(img);
					}
				}

				if (CustomBackgroundMode == BgModeAnimated)
				{
					string gifCandidate = ResolveAnimatedBgFile(path);
					if (!string.IsNullOrEmpty(gifCandidate) && File.Exists(gifCandidate))
					{
						try { animatedBackground = AnimatedBackground.Open(gifCandidate); }
						catch (IOException) { }
						catch (ArgumentException) { }
						catch (System.Runtime.InteropServices.ExternalException) { }
					}
				}

				CustomBackgroundPath = path;
				return staticBackground != null || animatedBackground != null;
			}
			catch
			{
				CustomBackgroundPath = "";
				ClearStaticAndAnimated();
				return false;
			}
		}

		private static string ResolveStaticBgFile(string path)
		{
			if (string.IsNullOrEmpty(path)) return "";
			if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
			{
				string pngSibling = Path.Combine(Path.GetDirectoryName(path) ?? "", "custom-bg.png");
				if (File.Exists(pngSibling)) return pngSibling;
			}
			return path;
		}

		private static string ResolveAnimatedBgFile(string path)
		{
			if (string.IsNullOrEmpty(path)) return "";
			if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
			string dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir))
			{
				string gifSibling = Path.Combine(dir, "custom-bg.gif");
				if (File.Exists(gifSibling)) return gifSibling;
			}
			return "";
		}

		internal static int AnimationFrameDelay
		{
			get { return animatedBackground == null ? 100 : animatedBackground.Delay; }
		}

		internal static int AdvanceAnimationFrame()
		{
			if (!HasAnimatedBackground) return 100;
			InvalidateCropCache();
			InvalidateBlurCache();
			try { animatedBackground.Advance(); }
			catch (ArgumentException) { ClearAnimatedFrames(); }
			catch (System.Runtime.InteropServices.ExternalException) { ClearAnimatedFrames(); }
			return AnimationFrameDelay;
		}

		internal static void SetLiveDesktopFrame(Bitmap frame)
		{
			if (liveDesktopFrame != null)
			{
				liveDesktopFrame.Dispose();
				liveDesktopFrame = null;
			}
			liveDesktopFrame = frame;
			InvalidateBlurCache();
		}

		internal static void ClearLiveDesktopFrame()
		{
			if (liveDesktopFrame != null)
			{
				liveDesktopFrame.Dispose();
				liveDesktopFrame = null;
			}
			InvalidateBlurCache();
		}

		internal static void ClearCustomBackground()
		{
			ClearStaticAndAnimated();
			ClearLiveDesktopFrame();
			CustomBackgroundPath = "";
			InvalidateBlurCache();
		}

		internal static void ReleasePerformanceCaches()
		{
			SetEngineFrame(null);
			ClearStaticAndAnimated();
			ClearLiveDesktopFrame();
			Bitmap glass = defaultGlassBackdrop;
			defaultGlassBackdrop = null;
			if (glass != null) glass.Dispose();
			Bitmap minimal = minimalGlassBackdrop;
			minimalGlassBackdrop = null;
			if (minimal != null) minimal.Dispose();
			blurredLuminanceGrid = null;
			CustomBackgroundPath = "";
			GameText.ClearCache();
		}

		private static void ClearStaticAndAnimated()
		{
			if (staticBackground != null)
			{
				staticBackground.Dispose();
				staticBackground = null;
			}
			ClearAnimatedFrames();
			InvalidateCropCache();
			InvalidateBlurCache();
		}

		private static void InvalidateCropCache()
		{
			if (croppedCache != null)
			{
				croppedCache.Dispose();
				croppedCache = null;
			}
			croppedCacheSource = null;
		}

		private static void InvalidateBlurCache()
		{
			if (blurredCache != null)
			{
				blurredCache.Dispose();
				blurredCache = null;
			}
			blurredCacheSource = null;
			blurredCacheLevel = -1;
			blurredLuminanceGrid = null;
		}

		internal static Bitmap CreateCroppedBitmap(Image src, int cx, int cy, int cw, int ch)
		{
			if (src == null) return null;
			int sx = (int)Math.Round(src.Width * (cx / 10000.0));
			int sy = (int)Math.Round(src.Height * (cy / 10000.0));
			int sw = Math.Max(4, (int)Math.Round(src.Width * (cw / 10000.0)));
			int sh = Math.Max(4, (int)Math.Round(src.Height * (ch / 10000.0)));
			if (sx < 0) sx = 0;
			if (sy < 0) sy = 0;
			if (sx + sw > src.Width) sw = Math.Max(1, src.Width - sx);
			if (sy + sh > src.Height) sh = Math.Max(1, src.Height - sy);
			var bmp = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
			using (var g = Graphics.FromImage(bmp))
			{
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.DrawImage(src, new Rectangle(0, 0, sw, sh), new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
			}
			return bmp;
		}

		internal static string SyncCroppedWallpaperFile(string dataRoot)
		{
			if (ExtremePerformanceMode) return "";
			if (string.IsNullOrEmpty(dataRoot)) return "";
			string cropPath = Path.Combine(dataRoot, "custom-bg-crop.png");
			if (!HasCustomCrop || staticBackground == null)
			{
				if (File.Exists(cropPath)) { try { File.Delete(cropPath); } catch { } }
				return "";
			}
			try
			{
				Directory.CreateDirectory(dataRoot);
				using (var cropped = CreateCroppedBitmap(staticBackground, CustomBgCropX, CustomBgCropY, CustomBgCropW, CustomBgCropH))
				{
					if (cropped != null) cropped.Save(cropPath, ImageFormat.Png);
				}
				return File.Exists(cropPath) ? cropPath : "";
			}
			catch
			{
				return "";
			}
		}

		internal static Image EffectiveStageBackground
		{
			get
			{
				if (ExtremePerformanceMode) return null;
				Image bg = CustomBackground;
				if (bg != null)
				{
					if (HasCustomCrop && bg != liveDesktopFrame)
					{
						if (croppedCache == null || croppedCacheSource != bg ||
							croppedKeyX != CustomBgCropX || croppedKeyY != CustomBgCropY ||
							croppedKeyW != CustomBgCropW || croppedKeyH != CustomBgCropH)
						{
							InvalidateCropCache();
							croppedCache = CreateCroppedBitmap(bg, CustomBgCropX, CustomBgCropY, CustomBgCropW, CustomBgCropH);
							croppedCacheSource = bg;
							croppedKeyX = CustomBgCropX;
							croppedKeyY = CustomBgCropY;
							croppedKeyW = CustomBgCropW;
							croppedKeyH = CustomBgCropH;
						}
						if (croppedCache != null) return croppedCache;
					}
					return bg;
				}
				if (CurrentTheme == ThemeLiquidGlass) return EnsureDefaultGlassBackdrop();
				return null;
			}
		}

		internal static Bitmap EnsureDefaultGlassBackdrop()
		{
			if (ExtremePerformanceMode)
			{
				if (minimalGlassBackdrop == null)
				{
					minimalGlassBackdrop = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
					minimalGlassBackdrop.SetPixel(0, 0, NativeLightPalette.BgColor);
				}
				return minimalGlassBackdrop;
			}
			if (defaultGlassBackdrop != null) return defaultGlassBackdrop;
			int w = 1280, h = 720;
			var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.HighQuality;
				using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
					Color.FromArgb(20, 42, 78), Color.FromArgb(14, 20, 34), 32f))
				{
					g.FillRectangle(bg, 0, 0, w, h);
				}
				using (var blob1 = new GraphicsPath())
				{
					blob1.AddEllipse(-80, -60, 620, 520);
					using (var pgb = new PathGradientBrush(blob1))
					{
						pgb.CenterColor = Color.FromArgb(140, 72, 168, 224);
						pgb.SurroundColors = new[] { Color.FromArgb(0, 72, 168, 224) };
						g.FillPath(pgb, blob1);
					}
				}
				using (var blob2 = new GraphicsPath())
				{
					blob2.AddEllipse(460, 120, 680, 540);
					using (var pgb = new PathGradientBrush(blob2))
					{
						pgb.CenterColor = Color.FromArgb(130, 118, 86, 198);
						pgb.SurroundColors = new[] { Color.FromArgb(0, 118, 86, 198) };
						g.FillPath(pgb, blob2);
					}
				}
				using (var blob3 = new GraphicsPath())
				{
					blob3.AddEllipse(780, -40, 580, 500);
					using (var pgb = new PathGradientBrush(blob3))
					{
						pgb.CenterColor = Color.FromArgb(120, 56, 188, 164);
						pgb.SurroundColors = new[] { Color.FromArgb(0, 56, 188, 164) };
						g.FillPath(pgb, blob3);
					}
				}
			}
			defaultGlassBackdrop = bmp;
			return defaultGlassBackdrop;
		}

		internal static Bitmap GetBlurredGlassBackground()
		{
			if (ExtremePerformanceMode) return null;
			Image src = EffectiveStageBackground;
			if (src == null) return null;
			int blur = Math.Max(0, Math.Min(24, GlassBlur));
			if (blurredCache != null && blurredCacheSource == src && blurredCacheLevel == blur)
				return blurredCache;

			if (blurredCache != null)
			{
				blurredCache.Dispose();
				blurredCache = null;
			}
			blurredCache = CreateBlurredBitmap(src, blur, HasAnimatedBackground && blur > 0 ? 480 : 0);
			blurredCacheSource = src;
			blurredCacheLevel = blur;
			return blurredCache;
		}

		internal static Bitmap CreateBlurredBitmap(Image source, int blurLevel, int longEdgeLimit = 0)
		{
			float[] ignored;
			return CreateBlurredBitmap(source, blurLevel, longEdgeLimit, out ignored);
		}

		internal static Bitmap CreateBlurredBitmap(Image source, int blurLevel, int longEdgeLimit, out float[] luminanceGrid)
		{
			luminanceGrid = null;
			if (source == null) return null;
			int maxDim = blurLevel <= 0 ? 1920 : 960;
			if (longEdgeLimit > 0) maxDim = Math.Min(maxDim, longEdgeLimit);
			int w = source.Width;
			int h = source.Height;
			if (w > maxDim || h > maxDim)
			{
				double sc = Math.Min((double)maxDim / w, (double)maxDim / h);
				w = Math.Max(4, (int)Math.Round(w * sc));
				h = Math.Max(4, (int)Math.Round(h * sc));
			}
			var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.InterpolationMode = blurLevel <= 0 ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
				g.DrawImage(source, 0, 0, w, h);
			}
			double blurScale = longEdgeLimit > 0 ? Math.Min(1.0, longEdgeLimit / 960.0) : 1.0;
			int radius = blurLevel <= 0 ? 0 : Math.Max(1, (int)Math.Round(blurLevel * 0.25 * blurScale));
			if (radius <= 0)
			{
				luminanceGrid = SampleLuminanceGridFromBitmap(bmp);
				return bmp;
			}

			Rectangle rect = new Rectangle(0, 0, w, h);
			BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
			try
			{
				int stride = Math.Abs(data.Stride);
				int totalBytes = stride * h;
				byte[] bufA = new byte[totalBytes];
				byte[] bufB = new byte[totalBytes];
				Marshal.Copy(data.Scan0, bufA, 0, totalBytes);
				BoxBlurPassHorizontal(bufA, bufB, w, h, stride, radius);
				BoxBlurPassVertical(bufB, bufA, w, h, stride, radius);
				BoxBlurPassHorizontal(bufA, bufB, w, h, stride, radius);
				BoxBlurPassVertical(bufB, bufA, w, h, stride, radius);
				Marshal.Copy(bufA, 0, data.Scan0, totalBytes);
				luminanceGrid = ComputeLuminanceGrid(bufA, w, h, stride);
			}
			finally
			{
				bmp.UnlockBits(data);
			}
			return bmp;
		}

		private static float[] SampleLuminanceGridFromBitmap(Bitmap bmp)
		{
			var grid = new float[64];
			int w = Math.Max(1, bmp.Width);
			int h = Math.Max(1, bmp.Height);
			for (int gy = 0; gy < 8; gy++)
			{
				int py = Math.Max(0, Math.Min(h - 1, (gy * h + h / 2) / 8));
				for (int gx = 0; gx < 8; gx++)
				{
					int px = Math.Max(0, Math.Min(w - 1, (gx * w + w / 2) / 8));
					Color c = bmp.GetPixel(px, py);
					grid[gy * 8 + gx] = (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;
				}
			}
			return grid;
		}

		private static float[] ComputeLuminanceGrid(byte[] buf, int w, int h, int stride)
		{
			var grid = new float[64];
			for (int gy = 0; gy < 8; gy++)
			{
				int py = Math.Max(0, Math.Min(h - 1, (gy * h + h / 2) / 8));
				int row = py * stride;
				for (int gx = 0; gx < 8; gx++)
				{
					int px = Math.Max(0, Math.Min(w - 1, (gx * w + w / 2) / 8));
					int idx = row + px * 4;
					grid[gy * 8 + gx] = (0.2126f * buf[idx + 2] + 0.7152f * buf[idx + 1] + 0.0722f * buf[idx]) / 255f;
				}
			}
			return grid;
		}

		private static void BoxBlurPassHorizontal(byte[] src, byte[] dst, int w, int h, int stride, int r)
		{
			int div = r * 2 + 1;
			for (int y = 0; y < h; y++)
			{
				int row = y * stride;
				int sumB = 0, sumG = 0, sumR = 0;
				for (int k = -r; k <= r; k++)
				{
					int cx = k < 0 ? 0 : (k >= w ? w - 1 : k);
					int idx = row + cx * 4;
					sumB += src[idx];
					sumG += src[idx + 1];
					sumR += src[idx + 2];
				}
				for (int x = 0; x < w; x++)
				{
					int o = row + x * 4;
					dst[o] = (byte)(sumB / div);
					dst[o + 1] = (byte)(sumG / div);
					dst[o + 2] = (byte)(sumR / div);
					dst[o + 3] = 255;

					int xRem = x - r;
					if (xRem < 0) xRem = 0;
					int xAdd = x + r + 1;
					if (xAdd >= w) xAdd = w - 1;
					int iRem = row + xRem * 4;
					int iAdd = row + xAdd * 4;
					sumB += src[iAdd] - src[iRem];
					sumG += src[iAdd + 1] - src[iRem + 1];
					sumR += src[iAdd + 2] - src[iRem + 2];
				}
			}
		}

		private static void BoxBlurPassVertical(byte[] src, byte[] dst, int w, int h, int stride, int r)
		{
			int div = r * 2 + 1;
			for (int x = 0; x < w; x++)
			{
				int x4 = x * 4;
				int sumB = 0, sumG = 0, sumR = 0;
				for (int k = -r; k <= r; k++)
				{
					int cy = k < 0 ? 0 : (k >= h ? h - 1 : k);
					int idx = cy * stride + x4;
					sumB += src[idx];
					sumG += src[idx + 1];
					sumR += src[idx + 2];
				}
				for (int y = 0; y < h; y++)
				{
					int o = y * stride + x4;
					dst[o] = (byte)(sumB / div);
					dst[o + 1] = (byte)(sumG / div);
					dst[o + 2] = (byte)(sumR / div);
					dst[o + 3] = 255;

					int yRem = y - r;
					if (yRem < 0) yRem = 0;
					int yAdd = y + r + 1;
					if (yAdd >= h) yAdd = h - 1;
					int iRem = yRem * stride + x4;
					int iAdd = yAdd * stride + x4;
					sumB += src[iAdd] - src[iRem];
					sumG += src[iAdd + 1] - src[iRem + 1];
					sumR += src[iAdd + 2] - src[iRem + 2];
				}
			}
		}

		internal static void GetWindowCanvasMetrics(System.Windows.Forms.Control ctrl, out int canvasW, out int canvasH, out int ox, out int oy)
		{
			ox = 0;
			oy = 0;
			if (ctrl == null)
			{
				canvasW = 1;
				canvasH = 1;
				return;
			}
			System.Windows.Forms.Form form = ctrl.FindForm();
			if (form != null && !form.IsDisposed && form.ClientSize.Width > 0 && form.ClientSize.Height > 0)
			{
				canvasW = Math.Max(1, form.ClientSize.Width);
				canvasH = Math.Max(1, form.ClientSize.Height);
				System.Windows.Forms.Control cur = ctrl;
				while (cur != null && cur != form)
				{
					ox += cur.Left;
					oy += cur.Top;
					cur = cur.Parent;
				}
				return;
			}
			System.Windows.Forms.Control root = FindRootBackdropOwner(ctrl);
			if (root != null && !root.IsDisposed && root.ClientSize.Width > 0 && root.ClientSize.Height > 0)
			{
				canvasW = Math.Max(1, root.ClientSize.Width);
				canvasH = Math.Max(1, root.ClientSize.Height);
				System.Windows.Forms.Control cur = ctrl;
				while (cur != null && cur != root)
				{
					ox += cur.Left;
					oy += cur.Top;
					cur = cur.Parent;
				}
				return;
			}
			canvasW = Math.Max(1, ctrl.ClientSize.Width);
			canvasH = Math.Max(1, ctrl.ClientSize.Height);
		}

		private static RectangleF ComputeImageRectF(int imgW, int imgH, int canvasW, int canvasH, bool contain)
		{
			float sw = Math.Max(1f, imgW);
			float sh = Math.Max(1f, imgH);
			float scale = contain
				? Math.Min((float)canvasW / sw, (float)canvasH / sh)
				: Math.Max((float)canvasW / sw, (float)canvasH / sh);
			float dw = sw * scale;
			float dh = sh * scale;
			float dx = (canvasW - dw) * 0.5f;
			float dy = (canvasH - dh) * 0.5f;
			return new RectangleF(dx, dy, dw, dh);
		}

		private static Rectangle ApplyLensAndOffset(RectangleF canvasRect, int ox, int oy, float lensScale, PointF lensCenterInCanvas)
		{
			float dx = canvasRect.X;
			float dy = canvasRect.Y;
			float dw = canvasRect.Width;
			float dh = canvasRect.Height;
			if (lensScale > 1.001f)
			{
				float extra = lensScale - 1f;
				dx -= (lensCenterInCanvas.X - dx) * extra;
				dy -= (lensCenterInCanvas.Y - dy) * extra;
				dw *= lensScale;
				dh *= lensScale;
			}
			return new Rectangle(
				(int)Math.Round(dx - ox),
				(int)Math.Round(dy - oy),
				Math.Max(1, (int)Math.Ceiling(dw)),
				Math.Max(1, (int)Math.Ceiling(dh)));
		}

		private static void DrawAlignedBackdropImage(Graphics g, Image primaryImg, Image containEdgeBlurImg, Rectangle bounds, int canvasW, int canvasH, int ox, int oy, float lensScale, PointF lensCenterInCanvas, bool highQuality)
		{
			if (primaryImg == null) return;
			bool liveFrame = CustomBackgroundMode == BgModeDesktopPassthrough && liveDesktopFrame != null && primaryImg == liveDesktopFrame;
			if (liveFrame && primaryImg.Width == canvasW && primaryImg.Height == canvasH && Math.Abs(lensScale - 1f) < 0.001f)
			{
				g.DrawImageUnscaled(primaryImg, -ox, -oy);
				return;
			}
			g.InterpolationMode = highQuality ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
			if (CustomBackgroundFit == BgFitContain && !liveFrame)
			{
				Image edgeBg = containEdgeBlurImg ?? primaryImg;
				RectangleF coverRect = ComputeImageRectF(edgeBg.Width, edgeBg.Height, canvasW, canvasH, false);
				g.DrawImage(edgeBg, ApplyLensAndOffset(coverRect, ox, oy, lensScale, lensCenterInCanvas));
				using (var dim = new SolidBrush(Color.FromArgb(62, 10, 14, 22)))
					g.FillRectangle(dim, bounds);
				RectangleF containRect = ComputeImageRectF(primaryImg.Width, primaryImg.Height, canvasW, canvasH, true);
				g.DrawImage(primaryImg, ApplyLensAndOffset(containRect, ox, oy, lensScale, lensCenterInCanvas));
				return;
			}
			RectangleF fullCover = ComputeImageRectF(primaryImg.Width, primaryImg.Height, canvasW, canvasH, false);
			g.DrawImage(primaryImg, ApplyLensAndOffset(fullCover, ox, oy, lensScale, lensCenterInCanvas));
		}

		private static void DrawGlassSample(Graphics g, Image image, Rectangle area, Rectangle destination)
		{
			Rectangle visible = Rectangle.Intersect(Rectangle.Intersect(area, destination), Rectangle.Ceiling(g.VisibleClipBounds));
			if (visible.Width <= 0 || visible.Height <= 0) return;
			float scaleX = image.Width / (float)destination.Width;
			float scaleY = image.Height / (float)destination.Height;
			g.InterpolationMode = InterpolationMode.Bilinear;
			g.DrawImage(image, visible, (visible.X - destination.X) * scaleX, (visible.Y - destination.Y) * scaleY,
				visible.Width * scaleX, visible.Height * scaleY, GraphicsUnit.Pixel);
		}

		private static Rectangle GlassSourceRectangle(Image image, int width, int height, int x, int y)
		{
			return ApplyLensAndOffset(ComputeImageRectF(image.Width, image.Height, width, height, CustomBackgroundFit == BgFitContain),
				x, y, 1f, PointF.Empty);
		}

		internal static Color SampleGlassTextBackground(System.Windows.Forms.Control control)
		{
			Bitmap sample = IsOutsideEngineCanvas(control) ? null : GetBlurredGlassBackground();
			if (sample == null) return Mix(BackgroundVeilColor, Color.White, 0.07f);
			int width, height, x, y;
			GetWindowCanvasMetrics(control, out width, out height, out x, out y);
			Rectangle imageBounds = GlassSourceRectangle(sample, width, height, x, y);
			var center = new Point(control.Width / 2, control.Height / 2);
			if (!imageBounds.Contains(center)) return Mix(BackgroundVeilColor, Color.White, 0.07f);
			int sampleX = (int)((center.X - imageBounds.Left) * (long)sample.Width / imageBounds.Width);
			int sampleY = (int)((center.Y - imageBounds.Top) * (long)sample.Height / imageBounds.Height);
			return Mix(BackgroundVeilColor, sample.GetPixel(sampleX, sampleY), 0.34f);
		}

		private static int LocalTintAlpha(Bitmap image, Rectangle destination, Rectangle bounds, int baseAlpha)
		{
			if (image == null || destination.Width <= 0 || destination.Height <= 0) return baseAlpha;
			float brightness = 0f;
			if (image == blurredCache && blurredLuminanceGrid != null && blurredLuminanceGrid.Length == 64)
			{
				for (int i = 1; i <= 3; i++)
				{
					int gx = (int)Math.Floor((bounds.Left + bounds.Width * (i / 4f) - destination.Left) * 8f / destination.Width);
					int gy = (int)Math.Floor((bounds.Top + bounds.Height * (i / 4f) - destination.Top) * 8f / destination.Height);
					gx = Math.Max(0, Math.Min(7, gx));
					gy = Math.Max(0, Math.Min(7, gy));
					brightness = Math.Max(brightness, blurredLuminanceGrid[gy * 8 + gx]);
				}
				return Math.Min(170, baseAlpha + (int)(Math.Max(0f, brightness - 0.43f) * 220f));
			}
			for (int i = 1; i <= 3; i++)
			{
				int x = (int)((bounds.Left + bounds.Width * (i / 4f) - destination.Left) * image.Width / destination.Width);
				int y = (int)((bounds.Top + bounds.Height * (i / 4f) - destination.Top) * image.Height / destination.Height);
				Color sample = image.GetPixel(Math.Max(0, Math.Min(image.Width - 1, x)), Math.Max(0, Math.Min(image.Height - 1, y)));
				brightness = Math.Max(brightness, (0.2126f * sample.R + 0.7152f * sample.G + 0.0722f * sample.B) / 255f);
			}
			return Math.Min(170, baseAlpha + (int)(Math.Max(0f, brightness - 0.43f) * 220f));
		}

        private static Bitmap CurrentEngineGlass()
        {
            return !HasCustomCrop && GlassBlur > 0 && blurredCacheSource == EngineFrame ? blurredCache : null;
        }

		internal static bool UsesEngineCanvasPublic(System.Windows.Forms.Control ctrl) { return UsesEngineCanvas(ctrl); }
		internal static bool IsOutsideEngineCanvasPublic(System.Windows.Forms.Control ctrl) { return IsOutsideEngineCanvas(ctrl); }
		internal static Bitmap CurrentEngineGlassPublic() { return CurrentEngineGlass(); }
		internal static Rectangle GlassSourceRectanglePublic(Image image, int canvasW, int canvasH, int ox, int oy)
		{
			return GlassSourceRectangle(image, canvasW, canvasH, ox, oy);
		}
		internal static void DrawGlassSamplePublic(Graphics g, Image sample, Rectangle area, int canvasW, int canvasH, int ox, int oy)
		{
			Rectangle dest = GlassSourceRectangle(sample, canvasW, canvasH, ox, oy);
			DrawGlassSample(g, sample, area, dest);
		}

		internal static void DrawGlassCardSurface(Graphics g, System.Windows.Forms.Control ctrl, GraphicsPath clipPath, int cornerRadius, Color tintColor, bool subtle)
		{
			if (ExtremePerformanceMode)
			{
				if (ctrl == null || ctrl.Width < 2 || ctrl.Height < 2) return;
				g.SmoothingMode = SmoothingMode.None;
				Color fill = CurrentTheme == ThemeLiquidGlass
					? (subtle ? LiquidGlassPalette.InsetColor : LiquidGlassPalette.CardColor)
					: (subtle ? NativeLightPalette.InsetColor : NativeLightPalette.CardColor);
				Color border = CurrentTheme == ThemeLiquidGlass
					? Color.FromArgb(62, 78, 102)
					: (subtle ? NativeLightPalette.LineColor : NativeLightPalette.BorderColor);
				using (var b = new SolidBrush(fill))
					g.FillRectangle(b, 0, 0, ctrl.Width, ctrl.Height);
				using (var p = new Pen(border, 1f))
					g.DrawRectangle(p, 0, 0, ctrl.Width - 1, ctrl.Height - 1);
				return;
			}
			LiquidGlassEngine.DrawGlassCard(g, ctrl, clipPath, cornerRadius, tintColor, subtle);
		}

		internal static void DrawLiquidGlassPill(Graphics g, System.Windows.Forms.Control ctrl, Rectangle rect, int cornerRadius, Color tintColor, int tintAlpha, Color rimAccent, bool hovered, bool pressed, bool selected, bool enabled)
		{
			DrawLiquidGlassPill(g, ctrl, rect, cornerRadius, tintColor, tintAlpha, rimAccent, hovered, pressed, selected, enabled, hovered ? 1f : 0f, pressed ? 1f : 0f, 0f, new Point(-100, -100), false, false);
		}

		internal static void DrawLiquidGlassPill(Graphics g, System.Windows.Forms.Control ctrl, Rectangle rect, int cornerRadius, Color tintColor, int tintAlpha, Color rimAccent, bool hovered, bool pressed, bool selected, bool enabled, float hoverT, float pressT, float sheenT, Point pointerLocal, bool quietAtRest = false, bool fastMotion = false)
		{
			if (rect.Width < 4 || rect.Height < 4) return;
			if (ExtremePerformanceMode)
			{
				g.SmoothingMode = SmoothingMode.None;
				Color fill, border;
				if (CurrentTheme == ThemeLiquidGlass)
				{
					fill = !enabled ? Color.FromArgb(30, 38, 50)
						: pressed ? Color.FromArgb(24, 34, 48)
						: selected ? Color.FromArgb(44, 98, 148)
						: hovered ? Color.FromArgb(46, 58, 78)
						: Color.FromArgb(34, 45, 64);
					border = selected ? Color.FromArgb(94, 192, 255) : Color.FromArgb(82, 102, 130);
				}
				else
				{
					fill = !enabled ? Color.FromArgb(240, 239, 234)
						: selected ? Color.FromArgb(44, 110, 73)
						: pressed ? Color.FromArgb(232, 229, 222)
						: hovered ? Color.FromArgb(239, 236, 230)
						: Color.FromArgb(248, 247, 244);
					border = selected ? Color.FromArgb(34, 88, 58) : Color.FromArgb(201, 197, 188);
				}
				using (var b = new SolidBrush(fill))
					g.FillRectangle(b, rect);
				using (var p = new Pen(border, 1f))
					g.DrawRectangle(p, rect.X, rect.Y, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1));
				return;
			}
			LiquidGlassEngine.DrawCrystalPill(g, ctrl, rect, cornerRadius, tintColor, tintAlpha, hovered, pressed, selected, enabled, hoverT, pressT, sheenT, pointerLocal, quietAtRest, fastMotion);
		}

		private static System.Windows.Forms.Control FindRootBackdropOwner(System.Windows.Forms.Control ctrl)
		{
			System.Windows.Forms.Control cur = ctrl != null ? ctrl.Parent : null;
			System.Windows.Forms.Control last = null;
			while (cur != null)
			{
				if (cur is GameSidePanel || cur is SettingsRootPanel) return cur;
				if (cur is IBackdropOwner) last = cur;
				cur = cur.Parent;
			}
			return last;
		}

		private static void ClearAnimatedFrames()
		{
			if (animatedBackground == null) return;
			animatedBackground.Dispose();
			animatedBackground = null;
		}

		internal static string ImportCustomBackgroundImage(string sourceFile, string dataRoot)
		{
			if (string.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile)) return "";
			Directory.CreateDirectory(dataRoot);
			string destPng = Path.Combine(dataRoot, "custom-bg.png");
			string destGif = Path.Combine(dataRoot, "custom-bg.gif");

			byte[] bytes = File.ReadAllBytes(sourceFile);
			using (var ms = new MemoryStream(bytes))
			using (var src = Image.FromStream(ms))
			{
				bool isMultiFrameGif = false;
				try
				{
					if (sourceFile.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
					{
						var dim = new FrameDimension(src.FrameDimensionsList[0]);
						isMultiFrameGif = src.GetFrameCount(dim) > 1;
					}
				}
				catch { }

				if (isMultiFrameGif) File.WriteAllBytes(destGif, bytes);
				else if (File.Exists(destGif)) { try { File.Delete(destGif); } catch { } }

				int w = src.Width;
				int h = src.Height;
				int maxDim = 3840;
				if (w > maxDim || h > maxDim)
				{
					double scale = Math.Min((double)maxDim / w, (double)maxDim / h);
					w = Math.Max(1, (int)Math.Round(w * scale));
					h = Math.Max(1, (int)Math.Round(h * scale));
				}
				using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
				{
					using (var g = Graphics.FromImage(bmp))
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.SmoothingMode = SmoothingMode.HighQuality;
						g.DrawImage(src, 0, 0, w, h);
					}
					bmp.Save(destPng, ImageFormat.Png);
				}
			}
			return destPng;
		}

		internal static string ImportWallpaperEngineItem(WallpaperEngineItem item, string dataRoot)
		{
			if (item == null) return "";
			string staticSrc = item.BestStaticImagePath;
			string imported = "";
			if (!string.IsNullOrEmpty(staticSrc) && File.Exists(staticSrc))
			{
				imported = ImportCustomBackgroundImage(staticSrc, dataRoot);
			}
			if (!string.IsNullOrEmpty(item.AnimatedPreviewPath) && File.Exists(item.AnimatedPreviewPath) && !string.IsNullOrEmpty(dataRoot))
			{
				Directory.CreateDirectory(dataRoot);
				string destGif = Path.Combine(dataRoot, "custom-bg.gif");
				try { File.Copy(item.AnimatedPreviewPath, destGif, true); }
				catch { }
			}
			return imported;
		}

		internal static void DrawCustomBackground(Graphics g, Rectangle bounds, Color veilColor)
		{
			DrawCustomBackground(g, null, bounds, veilColor);
		}

		internal static void DrawCustomBackground(Graphics g, System.Windows.Forms.Control owner, Rectangle bounds, Color veilColor)
		{
			if (ExtremePerformanceMode || IsOutsideEngineCanvas(owner)) return;
			Image bg = EffectiveStageBackground;
			if (bg == null || bounds.Width < 2 || bounds.Height < 2) return;
			GraphicsState state = g.Save();
			try
			{
				g.SetClip(bounds, CombineMode.Intersect);
				int canvasW, canvasH, ox, oy;
				if (owner != null)
				{
					GetWindowCanvasMetrics(owner, out canvasW, out canvasH, out ox, out oy);
				}
				else
				{
					canvasW = bounds.Width;
					canvasH = bounds.Height;
					ox = -bounds.X;
					oy = -bounds.Y;
				}
				int clampedVeilAlpha = EffectiveVeilAlpha;
				if (bg == EngineFrame && EngineVeiledFrame != null && CustomBackgroundFit == BgFitCover
					&& bg.Width == canvasW && bg.Height == canvasH && veilColor == engineVeilColor && clampedVeilAlpha == engineVeilAlpha)
				{
					Rectangle area = Rectangle.Intersect(bounds, Rectangle.Ceiling(g.VisibleClipBounds));
					Rectangle source = area;
					source.Offset(ox, oy);
					if (area.Width > 0 && area.Height > 0)
						g.DrawImage(EngineVeiledFrame, area, source, GraphicsUnit.Pixel);
					return;
				}
				if (CustomBackgroundFit == BgFitCover && bg.Width == canvasW && bg.Height == canvasH)
				{
					Rectangle area = Rectangle.Intersect(bounds, Rectangle.Ceiling(g.VisibleClipBounds));
					Rectangle source = area;
					source.Offset(ox, oy);
					if (area.Width > 0 && area.Height > 0)
						g.DrawImage(bg, area, source, GraphicsUnit.Pixel);
				}
				else
					DrawAlignedBackdropImage(g, bg, CustomBackgroundFit == BgFitContain && GlassBlur > 0
                        ? (UsesEngineCanvas(owner) ? CurrentEngineGlass() : GetBlurredGlassBackground()) : null,
						bounds, canvasW, canvasH, ox, oy, 1f, PointF.Empty, CustomBackgroundMode != BgModeAnimated && !UsesEngineCanvas(owner));
				using (var veil = new SolidBrush(Color.FromArgb(clampedVeilAlpha, veilColor)))
				{
					g.FillRectangle(veil, bounds);
				}
			}
			finally
			{
				g.Restore(state);
			}
		}

		public static Color Mix(Color from, Color to, float amount)
		{
			amount = Math.Max(0f, Math.Min(1f, amount));
			return Color.FromArgb(
				(int)((float)from.A + (float)(to.A - from.A) * amount),
				(int)((float)from.R + (float)(to.R - from.R) * amount),
				(int)((float)from.G + (float)(to.G - from.G) * amount),
				(int)((float)from.B + (float)(to.B - from.B) * amount));
		}

		public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
		{
			int num = Math.Max(1, radius * 2);
			GraphicsPath graphicsPath = new GraphicsPath();
			graphicsPath.AddArc(bounds.X, bounds.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(bounds.Right - num, bounds.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(bounds.X, bounds.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}
}
