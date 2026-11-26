using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	partial class MainForm
	{
		private string wallpaperEngineProject = "";
		private string playbackStatus = "";
		private string activePlaybackProject = "";
		private WallpaperEnginePlayback wallpaperPlayback;
		private DateTime playbackLastFrameUtc;
		private Size originalVideoSize;
        private double originalVideoFps;
        private DateTime nextDisplayRateProbeUtc;
        private double independentCaptureFps = 30;
        private DateTime captureResizeAt;
        private Size requestedPlaybackSize;
        private Size settledPlaybackSize;
        private bool forceWallpaperRetry;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initialization);
        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr context, int index);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr context);

        private double IndependentCaptureRate(bool refresh)
        {
            int fpsLimit = GetNumberOption("wallpaperFps", 30);
            if (!refresh && DateTime.UtcNow < nextDisplayRateProbeUtc)
                return Math.Min(fpsLimit, independentCaptureFps);
            nextDisplayRateProbeUtc = DateTime.UtcNow.AddSeconds(5);
            int refreshRate = 0;
            IntPtr display = CreateDC("DISPLAY", Screen.FromControl(this).DeviceName, null, IntPtr.Zero);
            if (display != IntPtr.Zero)
            {
                try { refreshRate = GetDeviceCaps(display, 116); }
                finally { DeleteDC(display); }
            }
            independentCaptureFps = Math.Min(60, refreshRate > 1 ? refreshRate : 60);
            if (originalVideoFps >= 5)
            {
                independentCaptureFps = Math.Min(independentCaptureFps, originalVideoFps);
                // Allow a small sampling margin for two independent clocks.
                // Repeated source pixels never enter the draw queue.
                if (independentCaptureFps >= 50) independentCaptureFps = Math.Min(72, independentCaptureFps * 1.20);
            }
            return Math.Min(fpsLimit, independentCaptureFps);
        }
		private bool playbackFailed;
        private bool retriedWallpaperStartup;
		private int frameDeliveryQueued;
		private long lastDeliveredSequence;

		private bool UseNativeStageBackdrop
		{
			get
			{
				if (extremePerformanceMode) return false;
				return sharedWallpaper != null || UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine ||
                    UiTheme.CustomBackgroundMode == UiTheme.BgModeAnimated && (!options.ContainsKey("stageWallpaperBg") || options["stageWallpaperBg"]);
			}
		}

		private void StopIndependentWallpaper(bool preserveFrame = false)
        {
            if (!preserveFrame)
            {
                UiTheme.SetEngineFrame(null);
                if (UiTheme.EngineCanvasOwner == this) UiTheme.EngineCanvasOwner = null;
            }
            else if (wallpaperPlayback != null && UiTheme.EngineFrame != null)
                UiTheme.SetEngineFrame(new Bitmap(UiTheme.EngineFrame));
			lastDeliveredSequence = 0;
			if (wallpaperPlayback != null)
			{
				WallpaperEnginePlayback previous = wallpaperPlayback;
				wallpaperPlayback = null;
                previous.FrameReady -= OnIndependentWallpaperFrameReady;
                if (preserveFrame) Task.Run(() => previous.Dispose());
                else previous.Dispose();
			}
		}

		private void SetPlaybackStatus(string message)
		{
			if (playbackStatus == message) return;
			playbackStatus = message;
			if (!string.IsNullOrEmpty(message)) AddLog(message, BlueColor);
		}

		private void ConfigureIndependentWallpaper()
		{
			if (extremePerformanceMode)
			{
				StopIndependentWallpaper();
				return;
			}
			if (forceWallpaperRetry || !string.Equals(activePlaybackProject, wallpaperEngineProject, StringComparison.OrdinalIgnoreCase))
            {
                forceWallpaperRetry = false;
                StopIndependentWallpaper(true);
				activePlaybackProject = wallpaperEngineProject;
                retriedWallpaperStartup = false;
				playbackFailed = false;
			}
			EnsureBgRuntimeTimer(100);
			CaptureIndependentWallpaper();
		}

		private void ResumeIndependentWallpaper(object sender, EventArgs e)
		{
			if (extremePerformanceMode)
			{
				StopIndependentWallpaper();
				if (bgRuntimeTimer != null) bgRuntimeTimer.Stop();
				return;
			}
			if (sharedWallpaper != null) return;
			if (!Visible || WindowState == FormWindowState.Minimized)
			{
				if (GetNumberOption("wallpaperPauseHidden", 1) == 0 && UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine) return;
				StopIndependentWallpaper(true);
				if ((UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine || UiTheme.HasAnimatedBackground)
					&& bgRuntimeTimer != null) bgRuntimeTimer.Stop();
			}
			else if ((UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine || UiTheme.HasAnimatedBackground)
				&& bgRuntimeTimer != null)
			{
				if (UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine && wallpaperPlayback == null)
                {
                    playbackFailed = false;
                    retriedWallpaperStartup = false;
                }
				bgRuntimeTimer.Start();
				if (UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine) CaptureIndependentWallpaper();
			}
		}

		private bool StartIndependentWallpaper(Size size)
		{
			WallpaperEngineItem item = WallpaperEngineLocator.ParseProjectJson(wallpaperEngineProject);
			string source = item == null ? "" : item.PlaybackSource;
            if (retriedWallpaperStartup && item != null && item.Type == "web" && File.Exists(item.ProjectJsonPath))
                source = item.ProjectJsonPath;
			string executable = WallpaperEngineLocator.FindPlaybackExecutable(wallpaperEngineDir);
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(executable))
			{
				playbackFailed = true;
				SetPlaybackStatus(string.IsNullOrEmpty(source)
					? "独立壁纸的作品入口已失效。仍显示备用背景，可到设置重新选择。"
					: "Wallpaper Engine 尚未运行。仍显示备用背景，可打开引擎后重新选择。");
				return false;
			}
			try
			{
                originalVideoFps = 0;
                originalVideoSize = item.Type == "video" ? WallpaperEngineLocator.ReadVideoInfo(source, out originalVideoFps) : Size.Empty;
				Size renderSize = ChooseEngineRenderSize(size);
				bool prepareGlass = !UiTheme.HasCustomCrop && (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
                    || UiTheme.CustomBackgroundFit == UiTheme.BgFitContain);
				Size captureSize = ChooseIndependentCaptureSize(renderSize);
                UiTheme.EngineCanvasOwner = this;
                var player = new WallpaperEnginePlayback(executable, source, captureSize);
				player.FrameReady += OnIndependentWallpaperFrameReady;
				player.StartPipeline(renderSize, UiTheme.BackgroundVeilColor, UiTheme.EffectiveVeilAlpha, prepareGlass, UiTheme.GlassBlur, IndependentCaptureRate(true), captureSize);
				wallpaperPlayback = player;
				playbackLastFrameUtc = DateTime.UtcNow;
				SetPlaybackStatus("正在载入独立壁纸：" + item.Title);
				return true;
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				playbackFailed = true;
				SetPlaybackStatus("独立壁纸启动失败：" + ex.Message);
				return false;
			}
		}

		private Size ChooseIndependentCaptureSize(Size target)
        {
            int quality = GetNumberOption("wallpaperQuality", 0);
            if (quality == 0 && !originalVideoSize.IsEmpty) target = originalVideoSize;
            return WallpaperEnginePlayback.ChooseCaptureSize(target, quality == 2 ? 1280 : quality == 1 ? 2560 : 4096);
        }

        private void RetryIndependentWallpaper()
        {
            if (UiTheme.CustomBackgroundMode != UiTheme.BgModeEngine || extremePerformanceMode) return;
            forceWallpaperRetry = true;
            ConfigureIndependentWallpaper();
        }

        private Size ChooseEngineRenderSize(Size target)
		{
			if (originalVideoSize.IsEmpty) return WallpaperEnginePlayback.ChooseCaptureSize(target, 4096);
			double x = target.Width / (double)originalVideoSize.Width;
			double y = target.Height / (double)originalVideoSize.Height;
			double scale = UiTheme.CustomBackgroundFit == UiTheme.BgFitContain ? Math.Min(x, y) : Math.Max(x, y);
			double crop = Math.Min(UiTheme.CustomBgCropW, UiTheme.CustomBgCropH) / 10000.0;
			scale = Math.Min(1.0, scale / Math.Max(0.05, crop));
			return new Size(Math.Max(16, (int)Math.Ceiling(originalVideoSize.Width * scale)),
				Math.Max(16, (int)Math.Ceiling(originalVideoSize.Height * scale)));
		}

		private void OnIndependentWallpaperFrameReady()
		{
			if (IsDisposed || !IsHandleCreated) return;
			if (Interlocked.CompareExchange(ref frameDeliveryQueued, 1, 0) != 0) return;
			try
			{
                BeginInvoke((MethodInvoker)delegate
                {
                    try { DeliverIndependentWallpaperFrame(); }
                    finally { Interlocked.Exchange(ref frameDeliveryQueued, 0); }
                });
			}
			catch
			{
				Interlocked.Exchange(ref frameDeliveryQueued, 0);
			}
		}

		private void DeliverIndependentWallpaperFrame()
		{
			WallpaperEnginePlayback session = wallpaperPlayback;
			if (session == null || playbackFailed || IsDisposed || !Visible || WindowState == FormWindowState.Minimized
				|| UiTheme.CustomBackgroundMode != UiTheme.BgModeEngine || sharedWallpaper != null)
				return;

			WallpaperEnginePlayback.FrameSlot slot = session.AcquireLatestSlot();
			if (slot == null || slot.Sequence == lastDeliveredSequence) return;
			lastDeliveredSequence = slot.Sequence;
			playbackLastFrameUtc = DateTime.UtcNow;

			if (wallpaperSmokeTimes != null)
			{
				wallpaperSmokeTimes.Add(wallpaperSmokeClock.Elapsed.TotalMilliseconds);
				wallpaperSmokeWorkerTimes.Add(slot.WorkerMs);
				wallpaperSmokeCaptureTimes.Add(slot.CaptureMs);
			}

			Bitmap glassFrame = slot.GlassBitmap;
			float[] lumGrid = slot.LuminanceGrid;
			slot.GlassBitmap = null;
			slot.LuminanceGrid = null;
			UiTheme.SetEnginePreparedFrame(slot.RawBitmap, slot.VeiledBitmap, slot.VeilColor, slot.VeilAlpha, glassFrame, lumGrid);
            SetPlaybackStatus("独立壁纸播放中 · " +
                (originalVideoSize.IsEmpty ? "" : "素材 " + originalVideoSize.Width + "×" + originalVideoSize.Height + " · ") +
                "采集 " + slot.CaptureWidth + "×" + slot.CaptureHeight + " · 输出 " + slot.Width + "×" + slot.Height);

            // Paint the same frame on all native surfaces inside this UI dispatch.
            // The game WebView stays above the viewport and keeps its own paint loop.
            WindowStyle.PaintWallpaperFrame(viewportPanel, false);
            WindowStyle.PaintWallpaperFrame(activeSidePanel ?? (Control)sidebarScroll, true);
            WindowStyle.PaintWallpaperFrame(viewportBar, true);
		}

		private void CaptureIndependentWallpaper()
		{
			if (extremePerformanceMode || playbackFailed || !Visible || WindowState == FormWindowState.Minimized || sharedWallpaper != null)
				return;
			Size size = ClientSize;
			if (size.Width < 16 || size.Height < 16) return;
            DateTime now = DateTime.UtcNow;
            if (size != requestedPlaybackSize)
            {
                requestedPlaybackSize = size;
                captureResizeAt = now.AddMilliseconds(180);
            }
            if (settledPlaybackSize.IsEmpty || wallpaperPlayback == null || now >= captureResizeAt)
                settledPlaybackSize = size;
            size = settledPlaybackSize;
			if (wallpaperPlayback == null)
			{
				StartIndependentWallpaper(size);
				return;
			}
			bool prepareGlass = !UiTheme.HasCustomCrop && (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
                    || UiTheme.CustomBackgroundFit == UiTheme.BgFitContain);
            Size outputSize = ChooseEngineRenderSize(size);
			wallpaperPlayback.UpdatePipelineConfig(outputSize, UiTheme.BackgroundVeilColor,
				UiTheme.EffectiveVeilAlpha, prepareGlass, UiTheme.GlassBlur, IndependentCaptureRate(false), ChooseIndependentCaptureSize(outputSize));
			DeliverIndependentWallpaperFrame();
			// Some scene renders stay black after a fast switch. Reopen once, leaving the fallback in place.
            TimeSpan sinceFrame = DateTime.UtcNow - playbackLastFrameUtc;
            if (!retriedWallpaperStartup && sinceFrame.TotalSeconds >= (wallpaperPlayback.AwaitingNonBlackFrame ? 5 : 8)
                && !wallpaperPlayback.HasRecentCapture(8))
            {
                retriedWallpaperStartup = true;
                SetPlaybackStatus("正在恢复独立壁纸 · " + wallpaperPlayback.CaptureStage);
                StopIndependentWallpaper(true);
                StartIndependentWallpaper(size);
                return;
            }
            if (sinceFrame.TotalSeconds > 20 && !wallpaperPlayback.HasRecentCapture(20))
			{
				FailIndependentWallpaper("独立壁纸未能出帧 · " + wallpaperPlayback.CaptureStage + "。可在壁纸设置重试。");
			}
		}

		private void FailIndependentWallpaper(string reason)
		{
			playbackFailed = true;
			StopIndependentWallpaper(true);
			SetPlaybackStatus(reason);
			InvalidateSidebarBackdrop();
			if (viewportPanel != null) viewportPanel.Invalidate();
		}
	}
}
