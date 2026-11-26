using System;
using System.Drawing;
using System.IO;
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
		private bool playbackFailed;
		private int frameDeliveryQueued;
		private long lastDeliveredSequence;
		private bool sidebarGlassInitialized;

		private bool UseNativeStageBackdrop
		{
			get { return sharedWallpaper != null || (UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine || UiTheme.CustomBackgroundMode == UiTheme.BgModeAnimated)
				&& (!options.ContainsKey("stageWallpaperBg") || options["stageWallpaperBg"]); }
		}

		private void StopIndependentWallpaper()
		{
			UiTheme.SetEngineFrame(null);
			Interlocked.Exchange(ref frameDeliveryQueued, 0);
			lastDeliveredSequence = 0;
			sidebarGlassInitialized = false;
			if (wallpaperPlayback != null)
			{
				wallpaperPlayback.Dispose();
				wallpaperPlayback = null;
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
			if (!string.Equals(activePlaybackProject, wallpaperEngineProject, StringComparison.OrdinalIgnoreCase))
			{
				StopIndependentWallpaper();
				activePlaybackProject = wallpaperEngineProject;
				playbackFailed = false;
			}
			EnsureBgRuntimeTimer(100);
			CaptureIndependentWallpaper();
		}

		private void ResumeIndependentWallpaper(object sender, EventArgs e)
		{
			if (sharedWallpaper != null) return;
			if (!Visible || WindowState == FormWindowState.Minimized)
			{
				StopIndependentWallpaper();
				if ((UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine || UiTheme.HasAnimatedBackground)
					&& bgRuntimeTimer != null) bgRuntimeTimer.Stop();
			}
			else if ((UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine || UiTheme.HasAnimatedBackground)
				&& bgRuntimeTimer != null)
			{
				if (UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine) playbackFailed = false;
				bgRuntimeTimer.Start();
				if (UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine) CaptureIndependentWallpaper();
			}
		}

		private bool StartIndependentWallpaper(Size size)
		{
			WallpaperEngineItem item = WallpaperEngineLocator.ParseProjectJson(wallpaperEngineProject);
			string source = item == null ? "" : item.PlaybackSource;
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
				originalVideoSize = item.Type == "video" ? WallpaperEngineLocator.ReadVideoSize(source) : Size.Empty;
				Size renderSize = ChooseEngineRenderSize(size);
				bool prepareGlass = !UiTheme.HasCustomCrop && (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
					|| UiTheme.CustomBackgroundFit == UiTheme.BgFitContain);
				var player = new WallpaperEnginePlayback(executable, source, renderSize);
				player.FrameReady += OnIndependentWallpaperFrameReady;
				player.StartPipeline(renderSize, UiTheme.BackgroundVeilColor, UiTheme.EffectiveVeilAlpha, prepareGlass, UiTheme.GlassBlur);
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

		private Size ChooseEngineRenderSize(Size target)
		{
			if (originalVideoSize.IsEmpty) return target;
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
				BeginInvoke((MethodInvoker)DeliverIndependentWallpaperFrame);
			}
			catch
			{
				Interlocked.Exchange(ref frameDeliveryQueued, 0);
			}
		}

		private void DeliverIndependentWallpaperFrame()
		{
			Interlocked.Exchange(ref frameDeliveryQueued, 0);
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
			SetPlaybackStatus("独立壁纸播放中 · " + slot.Width + "×" + slot.Height
				+ (originalVideoSize.IsEmpty ? "" : "（源 " + originalVideoSize.Width + "×" + originalVideoSize.Height + "）"));

			Bitmap glassFrame = slot.GlassBitmap;
			float[] lumGrid = slot.LuminanceGrid;
			slot.GlassBitmap = null;
			slot.LuminanceGrid = null;
			UiTheme.SetEnginePreparedFrame(slot.RawBitmap, slot.VeiledBitmap, slot.VeilColor, slot.VeilAlpha, glassFrame, lumGrid);

			if (UseNativeStageBackdrop && viewportPanel != null)
			{
				viewportPanel.Invalidate();
				viewportPanel.Update();
			}
			if (glassFrame != null)
			{
				if (!sidebarGlassInitialized)
				{
					sidebarGlassInitialized = true;
					InvalidateSidebarBackdrop();
				}
				else if (viewportBar != null && !viewportBar.IsDisposed)
				{
					viewportBar.Invalidate();
				}
			}
		}

		private void CaptureIndependentWallpaper()
		{
			if (playbackFailed || !Visible || WindowState == FormWindowState.Minimized || sharedWallpaper != null)
				return;
			Size size = ClientSize;
			if (size.Width < 16 || size.Height < 16) return;
			if (wallpaperPlayback == null)
			{
				StartIndependentWallpaper(size);
				return;
			}
			bool prepareGlass = !UiTheme.HasCustomCrop && (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
				|| UiTheme.CustomBackgroundFit == UiTheme.BgFitContain);
			wallpaperPlayback.UpdatePipelineConfig(ChooseEngineRenderSize(size), UiTheme.BackgroundVeilColor,
				UiTheme.EffectiveVeilAlpha, prepareGlass, UiTheme.GlassBlur);
			DeliverIndependentWallpaperFrame();
			if (DateTime.UtcNow - playbackLastFrameUtc > TimeSpan.FromSeconds(20))
			{
				FailIndependentWallpaper("独立壁纸未能出帧。已显示备用背景，可打开 Wallpaper Engine 后重新选择。");
			}
		}

		private void FailIndependentWallpaper(string reason)
		{
			playbackFailed = true;
			StopIndependentWallpaper();
			UiTheme.SetEngineFrame(null);
			SetPlaybackStatus(reason);
			InvalidateSidebarBackdrop();
			if (viewportPanel != null) viewportPanel.Invalidate();
		}
	}
}
