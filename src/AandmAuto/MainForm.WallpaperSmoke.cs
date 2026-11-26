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
				TopMost = true;
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
				long paintedFrame = -1;
				viewportPanel.Paint += delegate
				{
					long frame = UiTheme.WallpaperFrameSequence;
					if (UiTheme.EngineFrame == null || paintedFrame == frame) return;
					paintedFrame = frame;
					repaintTimes.Add(wallpaperSmokeClock.Elapsed.TotalMilliseconds);
				};
				SwitchUiAppearance(layout, UiTheme.ThemeLiquidGlass, "", 65, UiTheme.BgModeEngine, 12, UiTheme.BgFitCover);
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
				int first = wallpaperSmokeTimes.Count;
				int firstPaint = repaintTimes.Count;
				int totalSeconds = Math.Max(2, Math.Min(60, seconds));
				var metricsClock = Stopwatch.StartNew();
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

				bgRuntimeTimer.Stop();
				if (wallpaperPlayback != null) wallpaperPlayback.PausePipeline();
				await Task.Delay(230);
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
				result["result"] = (double)result["webViewBackdropMeanRgbDifference"] < 9.0
					&& (bool)result["resumed"] && (bool)result["pausedWhenMinimized"] ? "passed" : "failed";
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

	}
}
