using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private SharedWallpaper sharedWallpaper;
		private Timer sharedWallpaperRefresh;
		private long lastSharedFrame;
		private bool sharedLocationHooked;

		private bool PaintSharedWallpaper(Graphics graphics, Control control)
		{
			if (sharedWallpaper == null || dockHost == 0 || !control.IsHandleCreated) return false;
			Rectangle canvas = WindowStyle.ClientAreaOnScreen(new IntPtr(dockHost));
			if (canvas.IsEmpty) return false;
			Point origin = control.PointToScreen(Point.Empty);
			return sharedWallpaper.Paint(graphics, canvas.Size,
				new Point(origin.X - canvas.Left, origin.Y - canvas.Top), control.ClientRectangle);
		}

		private void InvalidateSharedViewport()
		{
			if (sharedWallpaper == null || IsDisposed) return;
			if (viewportPanel != null && !viewportPanel.IsDisposed) viewportPanel.Invalidate();
			if (viewportBar != null && !viewportBar.IsDisposed) viewportBar.Invalidate(true);
		}

		private async Task SetSharedWallpaperAsync(string name)
		{
			if (sharedWallpaper != null && sharedWallpaper.Name == name) return;
			if (sharedWallpaper == null && string.IsNullOrEmpty(name)) return;
			var next = string.IsNullOrEmpty(name) ? null : new SharedWallpaper(name);
			ReleaseSharedWallpaper();
			sharedWallpaper = next;
			if (!sharedLocationHooked)
			{
				sharedLocationHooked = true;
				base.LocationChanged += delegate { InvalidateSharedViewport(); };
				base.SizeChanged += delegate { InvalidateSharedViewport(); };
			}
			if (next == null)
			{
				playbackFailed = false;
				ConfigureBackgroundRuntime();
				InvalidateSidebarBackdrop();
				if (viewportPanel != null)
				{
					bool unified = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass || UiTheme.CustomBackground != null || UseNativeStageBackdrop;
					viewportPanel.Padding = new Padding(unified ? 0 : 3, 0, 0, 0);
				}
			}
			else
			{
				if (bgRuntimeTimer != null) bgRuntimeTimer.Stop();
				StopIndependentWallpaper();
				UiTheme.SetEngineFrame(null);
				if (desktopCapture != null)
				{
					desktopCapture.Dispose();
					desktopCapture = null;
				}
				UiTheme.ClearLiveDesktopFrame();
				viewportPanel.Padding = Padding.Empty;
				lastSharedFrame = -1;
				sharedWallpaperRefresh = new Timer { Interval = 100 };
				sharedWallpaperRefresh.Tick += delegate
				{
					if (IsMinimizedOrHostMinimized()) return;
					long sequence = next.Sequence;
					if (lastSharedFrame != sequence)
					{
						lastSharedFrame = sequence;
						viewportPanel.Invalidate();
						viewportBar.Invalidate(true);
					}
				};
				sharedWallpaperRefresh.Start();
			}
			if (viewportPanel != null) viewportPanel.Invalidate();
			if (viewportBar != null) viewportBar.Invalidate(true);
			await ApplyStageWallpaperAsync();
		}

		private void ReleaseSharedWallpaper()
		{
			if (sharedWallpaperRefresh != null)
			{
				sharedWallpaperRefresh.Dispose();
				sharedWallpaperRefresh = null;
			}
			if (sharedWallpaper != null)
			{
				sharedWallpaper.Dispose();
				sharedWallpaper = null;
			}
		}
	}
}
