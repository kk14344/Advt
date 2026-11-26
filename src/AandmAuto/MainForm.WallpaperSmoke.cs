using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private List<double> wallpaperSmokeTimes;
		private Stopwatch wallpaperSmokeClock;

		private readonly List<double> wallpaperSmokeWorkerTimes = new List<double>();
		private readonly List<double> wallpaperSmokeCaptureTimes = new List<double>();

		private async Task RunIndependentWallpaperSmokeAsync(string projectPath)
		{
            if (Environment.GetEnvironmentVariable("AANDM_STAGE3_DIRECT_PROBE") == "1")
            {
                await RunBrowserVideoProbeAsync(projectPath);
                return;
            }
			var result = new Dictionary<string, object>();
			try
			{
				pollTimer.Stop();
				pollCycleBusy = true;
				autoLoginEnabled = false;
				WindowState = FormWindowState.Normal;
				int testWidth, testHeight;
				if (!int.TryParse(Environment.GetEnvironmentVariable("AANDM_WALLPAPER_WIDTH"), out testWidth)) testWidth = 1440;
				if (!int.TryParse(Environment.GetEnvironmentVariable("AANDM_WALLPAPER_HEIGHT"), out testHeight)) testHeight = 810;
				ClientSize = new Size(testWidth, testHeight);
				result["clientSize"] = new { ClientSize.Width, ClientSize.Height };
                bool quiet = Environment.GetEnvironmentVariable("AANDM_STAGE3_MEASURE") == "1";
				TopMost = !quiet;
				CenterToScreen();
				webView.CoreWebView2.NavigateToString(@"<!doctype html><meta charset='utf-8'>
					<style>html,body{width:100%;height:100%;margin:0;background:#111}
					canvas{position:absolute;left:35%;top:15%;width:30%;height:70%;image-rendering:pixelated}</style>
					<canvas width='100' height='160'></canvas><script>
					const c=document.querySelector('canvas'),g=c.getContext('2d');
					g.fillStyle='#102c22';g.fillRect(0,0,c.width,c.height);
					g.fillStyle='#d8e6aa';g.font='10px sans-serif';g.fillText('GAME CANVAS',10,23);
					g.fillStyle='#a67934';g.fillRect(29,45,42,58);
					c.onclick=()=>window.clicked=true;</script>");
				await Task.Delay(600);
				wallpaperEngineProject = projectPath;
				wallpaperSmokeTimes = new List<double>();
				wallpaperSmokeClock = Stopwatch.StartNew();

				int layout;
				if (!int.TryParse(Environment.GetEnvironmentVariable("AANDM_WALLPAPER_LAYOUT"), out layout)) layout = UiLayoutGrid;
				var repaintTimes = new List<double>();
                var changedPaintTimes = new List<double>();
                uint previousHash = 0;
                bool hasPreviousHash = false;
				long paintedFrame = -1;
				viewportPanel.Paint += delegate
				{
					long frame = UiTheme.WallpaperFrameSequence;
					if (UiTheme.EngineFrame == null || paintedFrame == frame) return;
					paintedFrame = frame;
					double stamp = wallpaperSmokeClock.Elapsed.TotalMilliseconds;
                    repaintTimes.Add(stamp);
                    if (quiet)
                    {
                        uint hash = SampleVisibleWallpaper();
                        if (!hasPreviousHash || hash != previousHash) changedPaintTimes.Add(stamp);
                        previousHash = hash;
                        hasPreviousHash = true;
                    }
				};
				SwitchUiAppearance(layout, UiTheme.ThemeLiquidGlass, "", 65, UiTheme.BgModeEngine, 12, UiTheme.BgFitCover);
                if (layout == UiLayoutGrid) SwitchBottomTab("log");
                AddLog("动态壁纸验收 · 日志文字保持可见", GreenColor);
                var sideFrames = new HashSet<long>();
                var headerFrames = new HashSet<long>();
                var logFrames = new HashSet<long>();
                activeSidePanel.Paint += delegate { if (UiTheme.EngineFrame != null) sideFrames.Add(UiTheme.WallpaperFrameSequence); };
                viewportBar.Paint += delegate
                {
                    if (UiTheme.EngineFrame == null) return;
                    headerFrames.Add(UiTheme.WallpaperFrameSequence);
                    var log = txtLog as GlassRichTextBox;
                    if (log != null && log.Visible && log.LastWallpaperPaint == UiTheme.WallpaperFrameSequence)
                        logFrames.Add(log.LastWallpaperPaint);
                };
				result["layout"] = layout;
				using (var g = CreateGraphics()) result["dpi"] = g.DpiX;
				await ApplyStageWallpaperAsync();
				var timeout = Stopwatch.StartNew();
				while (UiTheme.EngineFrame == null && !playbackFailed && timeout.ElapsedMilliseconds < 24000)
					await Task.Delay(100);
				if (UiTheme.EngineFrame == null) throw new InvalidOperationException(playbackStatus);
				result["launchMs"] = timeout.ElapsedMilliseconds;

				int seconds;
				if (!int.TryParse(Environment.GetEnvironmentVariable("AANDM_WALLPAPER_SECONDS"), out seconds)) seconds = 6;
                if (quiet) await Task.Delay(10000);
				int first = wallpaperSmokeTimes.Count;
				int firstPaint = repaintTimes.Count;
                int firstChanged = changedPaintTimes.Count;
				int totalSeconds = Math.Max(2, Math.Min(60, seconds));
				var metricsClock = Stopwatch.StartNew();
                if (quiet) File.WriteAllText(Path.Combine(DataRoot(), "wallpaper-metrics-start"), "");
				using (Process current = Process.GetCurrentProcess())
				{
					TimeSpan cpuStart = current.TotalProcessorTime;
					long privatePeak = current.PrivateMemorySize64;
					for (int elapsed = 0; elapsed < totalSeconds; elapsed++)
					{
						await Task.Delay(1000);
						current.Refresh();
						privatePeak = Math.Max(privatePeak, current.PrivateMemorySize64);
					}
					result["observedSeconds"] = metricsClock.Elapsed.TotalSeconds;
					result["assistantCpuCorePercent"] =
						(current.TotalProcessorTime - cpuStart).TotalSeconds / metricsClock.Elapsed.TotalSeconds * 100.0;
					result["assistantPrivatePeakMiB"] = privatePeak / (1024.0 * 1024.0);
				}
                if (quiet) File.WriteAllText(Path.Combine(DataRoot(), "wallpaper-metrics-end"), "");
                double[] changed = changedPaintTimes.Skip(firstChanged).ToArray();
                double[] changedIntervals = changed.Skip(1).Select((t, i) => t - changed[i]).OrderBy(t => t).ToArray();
                if (quiet)
                {
                    result["distinctViewportPaintFps"] = changed.Length > 1 ? (changed.Length - 1) * 1000 / (changed.Last() - changed[0]) : 0;
                    result["distinctViewportP95Ms"] = changedIntervals.Length == 0 ? 0 :
                        changedIntervals[(int)Math.Ceiling(changedIntervals.Length * 0.95) - 1];
                }
				double[] times = wallpaperSmokeTimes.Skip(first).ToArray();
				double[] intervals = times.Skip(1).Select((t, i) => t - times[i]).OrderBy(t => t).ToArray();
				result["frames"] = times.Length;
				result["workerMeanMs"] = wallpaperSmokeWorkerTimes.Average();
				result["captureMeanMs"] = wallpaperSmokeCaptureTimes.Average();
				result["glassMeanMs"] = wallpaperSmokeWorkerTimes.Average() - wallpaperSmokeCaptureTimes.Average();
				double[] painted = repaintTimes.Skip(firstPaint).ToArray();
				double[] paintIntervals = painted.Skip(1).Select((t, i) => t - painted[i]).OrderBy(t => t).ToArray();
				result["viewportPaintedFrames"] = painted.Length;
				result["viewportPaintFps"] = painted.Length > 1 ? (painted.Length - 1) * 1000.0 / (painted.Last() - painted[0]) : 0;
				result["viewportPaintP95Ms"] = paintIntervals.Length == 0 ? 0 : paintIntervals[(int)Math.Ceiling(paintIntervals.Length * 0.95) - 1];
				result["fps"] = times.Length > 1 ? (times.Length - 1) * 1000.0 / (times.Last() - times.First()) : 0;
				result["p95FrameIntervalMs"] = intervals.Length > 0 ? intervals[(int)Math.Ceiling(intervals.Length * 0.95) - 1] : 0;
				result["frameSize"] = new { UiTheme.EngineFrame.Width, UiTheme.EngineFrame.Height };
				result["renderStatus"] = playbackStatus;
                result["assistantPanelFrames"] = sideFrames.Count;
                result["headerFrames"] = headerFrames.Count;
                result["commonPanelHeaderFrames"] = sideFrames.Intersect(headerFrames).Count();
                result["logFrames"] = logFrames.Count;
                var logControl = txtLog as GlassRichTextBox;
                result["logVisible"] = logControl != null && logControl.Visible;
                result["logForegroundPrinted"] = logControl != null && logControl.HasPrintedText();
                result["taskbarPlaybackWindowHidden"] = wallpaperPlayback != null && wallpaperPlayback.TaskbarWindowHidden;
                result["trayHideSettingEnabled"] = WallpaperEngineLocator.TrayIconHidden();
                result["droppedFullBlackFrames"] = wallpaperPlayback == null ? 0 : wallpaperPlayback.DroppedBlackFrames;

                if (layout == UiLayoutGrid)
                {
                    Rectangle originalBounds = Bounds;
                    long beforeMove = UiTheme.WallpaperFrameSequence;
                    SetBounds(Left + 24, Top + 16, Width - 48, Height - 24);
                    await Task.Delay(350);
                    SwitchTab("settings");
                    await Task.Delay(200);
                    SwitchTab("tasks");
                    SwitchBottomTab("log");
                    Bounds = originalBounds;
                    await Task.Delay(350);
                    result["moveResizeAndPanelSwitch"] = UiTheme.WallpaperFrameSequence > beforeMove &&
                        !playbackFailed && wallpaperPlayback != null && wallpaperPlayback.TaskbarWindowHidden;
                    if (!(bool)result["moveResizeAndPanelSwitch"])
                        throw new InvalidOperationException("移动、缩放或切换面板后播放未恢复。");
                }

				bgRuntimeTimer.Stop();
				if (wallpaperPlayback != null) wallpaperPlayback.PausePipeline();
				await Task.Delay(230);
                if (!quiet)
				using (var screen = new Bitmap(ClientSize.Width, ClientSize.Height))
				using (var expected = new Bitmap(viewportPanel.Width, viewportPanel.Height))
				{
					using (Graphics g = Graphics.FromImage(expected))
						UiTheme.DrawCustomBackground(g, viewportPanel, viewportPanel.ClientRectangle, UiTheme.BackgroundVeilColor);
					using (Graphics g = Graphics.FromImage(screen))
						g.CopyFromScreen(PointToScreen(Point.Empty), Point.Empty, ClientSize, CopyPixelOperation.SourceCopy);
					Point origin = PointToClient(viewportPanel.PointToScreen(Point.Empty));
					var difference = new List<int>();
					for (int x = 30; x < 140; x += 17)
						for (int y = 60; y < 570; y += 37)
						{
							Color a = expected.GetPixel(x, y);
							Color b = screen.GetPixel(origin.X + x, origin.Y + y);
							difference.Add(Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B));
						}
					result["webViewBackdropMeanRgbDifference"] = difference.Average();
					screen.Save(Path.Combine(DataRoot(), "wallpaper-smoke.png"), ImageFormat.Png);
				}
				string input = await webView.CoreWebView2.ExecuteScriptAsync(@"(function(){
					var c=document.querySelector('canvas'),r=c.getBoundingClientRect();
					var el=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);
					el.click();
					return [el===c,window.clicked===true,Array.from(c.getContext('2d').getImageData(50,65,1,1).data)];
				})()");
				result["gameCanvasInputAndSample"] = json.DeserializeObject(input);
				if (input != "[true,true,[166,121,52,255]]") throw new InvalidOperationException("Game canvas click or pixel changed.");

				WindowState = FormWindowState.Minimized;
				await Task.Delay(1100);
				result["pausedWhenMinimized"] = wallpaperPlayback == null;
				int beforeRestore = wallpaperSmokeTimes.Count;
				WindowState = FormWindowState.Normal;
				timeout.Restart();
				while (wallpaperSmokeTimes.Count == beforeRestore && !playbackFailed && timeout.ElapsedMilliseconds < 24000)
					await Task.Delay(100);
				result["resumed"] = wallpaperSmokeTimes.Count > beforeRestore;
				result["resumeMs"] = timeout.ElapsedMilliseconds;
				result["resumeStatus"] = playbackStatus;
				result["result"] = (quiet || (double)result["webViewBackdropMeanRgbDifference"] < 9.0)
					&& (bool)result["resumed"] && (bool)result["pausedWhenMinimized"]
                    && (bool)result["taskbarPlaybackWindowHidden"]
                    && (int)result["commonPanelHeaderFrames"] >= 2
                    && (!(bool)result["logVisible"] || (int)result["logFrames"] >= 2 && (bool)result["logForegroundPrinted"])
                    ? "passed" : "failed";
			}
			catch (Exception ex)
			{
				result["result"] = "failed";
				result["error"] = ex.ToString();
			}
			finally
			{
				File.WriteAllText(Path.Combine(DataRoot(), "wallpaper-smoke.json"), json.Serialize(result));
				Close();
			}
		}

        private static uint SampleVisibleWallpaper()
        {
            var frame = UiTheme.EngineFrame;
            uint hash = 2166136261U;
            for (int y = 1; y < 9; y++)
                for (int x = 1; x < 13; x++)
                    hash = unchecked((hash ^ (uint)frame.GetPixel(frame.Width*x/13,frame.Height*y/9).ToArgb()) * 16777619U);
            return hash;
        }

	}
}
