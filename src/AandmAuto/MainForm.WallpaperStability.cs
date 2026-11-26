using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		[DllImport("user32.dll")]
		private static extern uint GetGuiResources(IntPtr process, uint flags);

		private async Task RunWallpaperStabilityAsync(string[] projects)
		{
			var samples = new List<object>();
			string statusPath = Path.Combine(DataRoot(), "wallpaper-stability.json");
			string error = "";
			int completedSwitches = 0, seconds;
			if (!int.TryParse(Environment.GetEnvironmentVariable("AANDM_STABILITY_SECONDS"), out seconds)) seconds = 7200;
			int switches;
			if (!int.TryParse(Environment.GetEnvironmentVariable("AANDM_STABILITY_SWITCHES"), out switches)) switches = 20;
			var watch = new Stopwatch();
			using (Process current = Process.GetCurrentProcess())
			{
				Action<string> report = state =>
				{
					current.Refresh();
					samples.Add(new {
						seconds = Math.Round(watch.Elapsed.TotalSeconds, 2),
						switches = completedSwitches,
						gdi = GetGuiResources(current.Handle, 0),
						user = GetGuiResources(current.Handle, 1),
						privateMiB = Math.Round(current.PrivateMemorySize64 / 1048576.0, 2),
						cpuSeconds = Math.Round(current.TotalProcessorTime.TotalSeconds, 2),
						frame = UiTheme.WallpaperFrameSequence
					});
					File.WriteAllText(statusPath, json.Serialize(new { result = state, targetSeconds = seconds,
						targetSwitches = switches, completedSwitches = completedSwitches, samples = samples, error = error }));
				};
				try
				{
					autoLoginEnabled = false;
					pollTimer.Stop();
					dailyTaskTimer.Stop();
					if (!coreReady || projects.Length < 2) throw new InvalidOperationException("需要两个本地壁纸作品和 WebView2。");
					ClientSize = new Size(1440, 810);
					webView.CoreWebView2.NavigateToString("<!doctype html><html><body></body></html>");
					await Task.Delay(350);
					for (int i = 0; i < switches; i++)
					{
						long priorFrame = UiTheme.WallpaperFrameSequence;
						wallpaperEngineProject = projects[i % projects.Length];
                        forceWallpaperRetry = true;
						SwitchUiAppearance(UiLayoutGrid + i % 3, UiTheme.ThemeLiquidGlass, "", 65, UiTheme.BgModeEngine, 12, UiTheme.BgFitCover);
						await ApplyStageWallpaperAsync();
						var start = Stopwatch.StartNew();
                        while ((lastDeliveredSequence == 0 || UiTheme.WallpaperFrameSequence <= priorFrame)
                            && !playbackFailed && start.ElapsedMilliseconds < 32000)
                            await Task.Delay(100);
                        samples.Add(new
                        {
                            project = Path.GetFileName(Path.GetDirectoryName(wallpaperEngineProject)),
                            firstFrameMs = start.ElapsedMilliseconds,
                            status = playbackStatus
                        });
                        if (lastDeliveredSequence == 0 || UiTheme.WallpaperFrameSequence <= priorFrame)
                            throw new InvalidOperationException(playbackStatus);
						completedSwitches++;
						report("switching");
					}
					watch.Start();
					report("running");
					while (watch.Elapsed.TotalSeconds < seconds)
					{
						int delay = Math.Max(1, Math.Min(30000, (int)((seconds - watch.Elapsed.TotalSeconds) * 1000)));
						long previous = UiTheme.WallpaperFrameSequence;
						await Task.Delay(delay);
						if (playbackFailed || UiTheme.WallpaperFrameSequence <= previous && delay >= 1000
							&& WindowState != FormWindowState.Minimized &&
                            (wallpaperPlayback == null || !wallpaperPlayback.HasRecentCapture(10)))
							throw new InvalidOperationException("壁纸未继续出帧。" + playbackStatus);
						report("running");
					}
					report("completed");
				}
				catch (Exception ex)
				{
					error = ex.ToString();
					report("failed");
				}
			}
			Close();
		}
	}
}
