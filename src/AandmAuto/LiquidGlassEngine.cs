using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AandmAuto
{
	/// <summary>
	/// 60fps on-demand animation driver for Liquid Glass micro-interactions.
	/// Stops its single shared timer the moment all active animations settle (strict 0% idle CPU).
	/// </summary>
	internal static class GlassAnimator
	{
		private sealed class Entry
		{
			internal Func<float, bool> Step;
		}

		private static readonly List<Entry> entries = new List<Entry>();
		private static Timer timer;
		private static long lastTicks;

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
			if (entries.Count == 0 && timer != null && timer.Enabled) timer.Stop();
		}

		private static void EnsureRunning()
		{
			if (timer == null)
			{
				timer = new Timer { Interval = 16 };
				timer.Tick += OnTick;
			}
			if (!timer.Enabled)
			{
				lastTicks = Environment.TickCount;
				timer.Start();
			}
		}

		private static void OnTick(object sender, EventArgs e)
		{
			long now = Environment.TickCount;
			float dt = (now - lastTicks) / 1000f;
			lastTicks = now;
			if (dt <= 0.001f || dt > 0.05f) dt = 1f / 60f;

			if (!Enabled)
			{
				for (int i = entries.Count - 1; i >= 0; i--)
				{
					try { entries[i].Step(1f); } catch { }
				}
				entries.Clear();
				if (timer != null) timer.Stop();
				return;
			}

			for (int i = entries.Count - 1; i >= 0; i--)
			{
				bool keep = false;
				try { keep = entries[i].Step(dt); }
				catch { keep = false; }
				if (!keep && i < entries.Count) entries.RemoveAt(i);
			}

			if (entries.Count == 0 && timer != null && timer.Enabled)
			{
				timer.Stop();
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

		private sealed class PillCache
		{
			internal Bitmap Image;
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
				if (Image != null) { Image.Dispose(); Image = null; }
			}
		}

		private static readonly ConditionalWeakTable<Control, PillCache> pillCaches =
			new ConditionalWeakTable<Control, PillCache>();

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
				? Color.FromArgb(185, 255, 255, 255)
				: Color.FromArgb(24, 34, 52);
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

				int baseAlpha;
				Color fillBase;
				if (Preset == PresetCrystal)
				{
					baseAlpha = bright ? (subtle ? 26 : 38) : (subtle ? 12 : 20);
					fillBase = bright ? Color.White : Color.FromArgb(240, 248, 255);
				}
				else if (Preset == PresetDeep)
				{
					baseAlpha = subtle ? 102 : 126;
					fillBase = Color.FromArgb(12, 18, 30);
				}
				else
				{
					baseAlpha = bright ? (subtle ? 46 : 62) : (subtle ? 28 : 42);
					fillBase = bright ? Color.White : Color.FromArgb(16, 24, 38);
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

			// Crisp 315° / 135° Apple meniscus rim
			int hiA = bright ? (subtle ? 165 : 225) : (subtle ? 68 : 112);
			int loA = bright ? (subtle ? 70 : 120) : (subtle ? 22 : 44);
			using (var rim = new LinearGradientBrush(bounds, Color.FromArgb(hiA, Color.White), Color.FromArgb(loA, Color.White), 135f))
			using (var pen = new Pen(rim, 1.1f))
			{
				g.DrawPath(pen, clipPath);
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
			Point pointerLocal)
		{
			if (rect.Width < 4 || rect.Height < 4) return;
			int radius = Math.Max(3, Math.Min(cornerRadius, Math.Min(rect.Width, rect.Height) / 2));
			bool bright = IsBrightBackdrop();

			int canvasW = rect.Width, canvasH = rect.Height, ox = 0, oy = 0;
			if (ctrl != null) UiTheme.GetWindowCanvasMetrics(ctrl, out canvasW, out canvasH, out ox, out oy);
			ox += rect.X;
			oy += rect.Y;

			Bitmap source = ctrl != null ? ResolvePillBackdropSample(ctrl) : null;
			long seq = UiTheme.WallpaperFrameSequence;

			// Use cached refracted pill bitmap when control is provided so 60fps hover/sheen costs ~0.05ms!
			Bitmap rendered = null;
			bool disposeRendered = false;
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
				rendered = cache.Image;
			}
			else
			{
				rendered = BakeRefractedPillBitmap(
					rect.Width, rect.Height, radius,
					source, canvasW, canvasH, ox, oy,
					tintColor, tintAlpha, selected, enabled, bright);
				disposeRendered = true;
			}

			// Subtle outer ambient drop shadow
			g.SmoothingMode = SmoothingMode.HighQuality;
			using (GraphicsPath shadowPath = UiTheme.RoundedRect(new Rectangle(rect.X, rect.Y + 2, rect.Width, rect.Height), radius))
			using (var shadowBrush = new SolidBrush(Color.FromArgb(bright ? 22 : 44, 0, 0, 0)))
			{
				g.FillPath(shadowBrush, shadowPath);
			}

			if (rendered != null)
			{
				g.DrawImageUnscaled(rendered, rect.X, rect.Y);
				if (disposeRendered) rendered.Dispose();
			}

			// Fast dynamic interactive overlays (hover lift, press depth, cursor specular spot, click liquid sheen)
			float effectiveHover = Math.Max(hoverT, hovered && !GlassAnimator.Enabled ? 1f : 0f);
			float effectivePress = Math.Max(pressT, pressed && !GlassAnimator.Enabled ? 1f : 0f);
			if (effectiveHover > 0.01f || effectivePress > 0.01f || (sheenT > 0.005f && sheenT < 0.995f))
			{
				using (GraphicsPath path = UiTheme.RoundedRect(rect, radius))
				{
					GraphicsState st = g.Save();
					try
					{
						g.SetClip(path, CombineMode.Intersect);
						if (effectiveHover > 0.01f && enabled)
						{
							int ha = (int)(effectiveHover * (bright ? 48f : 32f));
							using (var hb = new SolidBrush(Color.FromArgb(ha, Color.White)))
								g.FillRectangle(hb, rect);

							// Cursor-tracked subtle optical reflection
							if (pointerLocal.X >= 0 && pointerLocal.Y >= 0 && pointerLocal.X <= rect.Width + 20)
							{
								int spotR = Math.Max(36, Math.Min(96, rect.Width / 2));
								Rectangle spotRect = new Rectangle(
									rect.X + pointerLocal.X - spotR,
									rect.Y + pointerLocal.Y - spotR,
									spotR * 2, spotR * 2);
								using (var gp = new GraphicsPath())
								{
									gp.AddEllipse(spotRect);
									using (var pgb = new PathGradientBrush(gp))
									{
										pgb.CenterColor = Color.FromArgb((int)(effectiveHover * 54f), Color.White);
										pgb.SurroundColors = new[] { Color.FromArgb(0, Color.White) };
										g.FillPath(pgb, gp);
									}
								}
							}
						}
						if (effectivePress > 0.01f && enabled)
						{
							int pa = (int)(effectivePress * 34f);
							using (var pb = new SolidBrush(Color.FromArgb(pa, 0, 0, 0)))
								g.FillRectangle(pb, rect);
						}
						if (sheenT > 0.005f && sheenT < 0.995f)
						{
							float bandW = Math.Max(38f, rect.Width * 0.42f);
							float cx = rect.X - bandW + (rect.Width + bandW * 2f) * sheenT;
							RectangleF sheenRect = new RectangleF(cx, rect.Y - 4, bandW, rect.Height + 8);
							float fade = (float)Math.Sin(sheenT * Math.PI);
							int sa = Math.Max(0, Math.Min(110, (int)(fade * 95f)));
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

					if (effectiveHover > 0.01f && enabled)
					{
						using (var hp = new Pen(Color.FromArgb((int)(effectiveHover * 115f), Color.White), 1.15f))
							g.DrawPath(hp, path);
					}
				}
			}
		}

		/// <summary>
		/// Exact /unsafe LockBits port of the 4-layer thick Apple Liquid Glass pill shader:
		/// 4-tap bilinear convex rim refraction + subtle chromatic fringe + translucent tint UNDER specular
		/// + outer optical lip + 315/135 liquid-mercury rim + inner caustic bounce + hero dome gloss + SDF AA.
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
			float rimThick = isBigHero ? Math.Min(rad * 0.92f, 14.5f) : Math.Min(rad * 0.88f, 10.5f);
			float baseStrength = Preset == PresetCrystal ? 9.5f : (Preset == PresetDeep ? 5.0f : 7.8f);
			float strength = isBigHero ? baseStrength * 1.22f : baseStrength;
			float chroma = Preset == PresetDeep ? 0.45f : 0.95f;
			float halfBoxW = Math.Max(0f, pw * 0.5f - rad);
			float halfBoxH = Math.Max(0f, ph * 0.5f - rad);
			float cx = pw * 0.5f;
			float cy = ph * 0.5f;

			// Determine translucent tint blended UNDERNEATH the 4-layer crystal specular rim
			float tintA;
			if (!enabled)
			{
				tintA = bright ? 0.14f : 0.10f;
			}
			else if (tintAlpha > 0 && tintColor != Color.White)
			{
				tintA = Math.Max(0.16f, Math.Min(0.50f, tintAlpha / 255f));
			}
			else if (selected)
			{
				tintA = bright ? 0.46f : 0.24f;
			}
			else
			{
				tintA = Preset == PresetCrystal ? (bright ? 0.14f : 0.055f)
					: (Preset == PresetDeep ? 0.36f : (bright ? 0.20f : 0.10f));
			}
			float tR = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 14f : tintColor.R;
			float tG = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 22f : tintColor.G;
			float tB = (Preset == PresetDeep && tintColor == Color.White && !selected) ? 36f : tintColor.B;

			// Compute source mapping rectangle (cover alignment matching UiTheme)
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

					float fallbackR = bright ? 225f : 26f;
					float fallbackG = bright ? 236f : 38f;
					float fallbackB = bright ? 246f : 56f;

					for (int y = 0; y < ph; y++)
					{
						byte* row = dstBase + y * dstStride;
						float py = y + 0.5f - cy;
						float yNorm = (y + 0.5f) / ph;

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

							float dx = 0f, dy = 0f, rimAdd = 0f, rimDark = 1f, caustic = 0f, domeGloss = 0f;
							if (distToEdge < rimThick)
							{
								float d = distToEdge > 0f ? distToEdge : 0f;
								float t = 1f - d / rimThick;
								float bend = t * t * (3f - 2f * t) * strength;
								float nx = distCore > 0.001f ? ((px < 0f ? -qx : qx) / distCore) : 0f;
								float ny = distCore > 0.001f ? ((py < 0f ? -qy : qy) / distCore) : 0f;
								dx = -nx * bend;
								dy = -ny * bend;

								// Layer 1: subtle 0.9px outer optical boundary lip
								if (d < 0.95f) rimDark = 1f - (1f - d / 0.95f) * (bright ? 0.14f : 0.24f);

								// Layer 2: razor-bright 315° / 135° liquid-mercury specular meniscus arc
								float dirTL = -(nx * 0.72f + ny * 0.69f);
								if (dirTL < 0f) dirTL = 0f;
								float dirBR = (nx * 0.72f + ny * 0.69f);
								if (dirBR < 0f) dirBR = 0f;

								float arcCenter = isBigHero ? 1.15f : 1.0f;
								float arcSpread = isBigHero ? 1.05f : 0.85f;
								float u = (d - arcCenter) / arcSpread;
								float sharpArc = (float)Math.Exp(-(u * u));
								float arcDir = (float)Math.Pow(dirTL, 1.15) + (float)Math.Pow(dirBR, 1.45) * 0.62f + (selected ? 0.28f : 0.15f);
								float arcBoost = !enabled ? 0.55f : (selected ? 1.15f : 1.0f);
								rimAdd = sharpArc * arcDir * (isBigHero ? 228f : 196f) * arcBoost;

								// Layer 3: internal glass caustic bounce
								float cCenter = isBigHero ? 5.0f : 3.4f;
								float cSpread = isBigHero ? 3.0f : 2.1f;
								float cu = (d - cCenter) / cSpread;
								float cBand = (float)Math.Exp(-(cu * cu));
								caustic = cBand * (dirTL * dirTL * 32f + dirBR * dirBR * 40f);
							}

							// Layer 4: tall hero button sculpted top dome reflection
							if (isBigHero && yNorm < 0.44f && distToEdge > 1.6f)
							{
								float df = 1f - yNorm / 0.44f;
								domeGloss = df * df * 24f;
							}

							float rS = fallbackR, gS = fallbackG, bS = fallbackB;
							if (srcBase != null)
							{
								float fy = (y + dy - srcMap.Top) * mapScaleY;
								float fxG = (x + dx - srcMap.Left) * mapScaleX;
								float fxR = (x + dx * (1f + chroma * 0.09f) - srcMap.Left) * mapScaleX;
								float fxB = (x + dx * (1f - chroma * 0.09f) - srcMap.Left) * mapScaleX;
								rS = SampleChannel(srcBase, srcStride, sw, sh, fxR, fy, 2);
								gS = SampleChannel(srcBase, srcStride, sw, sh, fxG, fy, 1);
								bS = SampleChannel(srcBase, srcStride, sw, sh, fxB, fy, 0);
							}

							float invT = 1f - tintA;
							float rOut = (rS * invT + tR * tintA) * rimDark + rimAdd + caustic + domeGloss;
							float gOut = (gS * invT + tG * tintA) * rimDark + rimAdd + caustic + domeGloss;
							float bOut = (bS * invT + tB * tintA) * rimDark + rimAdd + caustic + domeGloss;

							if (rOut > 255f) rOut = 255f; else if (rOut < 0f) rOut = 0f;
							if (gOut > 255f) gOut = 255f; else if (gOut < 0f) gOut = 0f;
							if (bOut > 255f) bOut = 255f; else if (bOut < 0f) bOut = 0f;

							// Premultiplied ARGB output for crisp GDI+ alpha compositing
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
