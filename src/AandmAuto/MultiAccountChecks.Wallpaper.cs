using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
    internal static partial class MultiAccountChecks
    {
        private static readonly string VideoProject = Environment.GetEnvironmentVariable("AANDM_ACCEPT_VIDEO_PROJECT");
        private static readonly string SceneProject = Environment.GetEnvironmentVariable("AANDM_ACCEPT_SCENE_PROJECT");
        private static readonly string EngineConfig = Environment.GetEnvironmentVariable("AANDM_ACCEPT_ENGINE_CONFIG");
        private static bool CheckDynamic { get { return !string.IsNullOrEmpty(VideoProject); } }

        private static void PrepareDynamicProfiles(AccountProfiles profiles, List<string> ids)
        {
            if (!CheckDynamic) return;
            for (int i = 0; i < ids.Count; i++)
            {
                string root = profiles.Root(ids[i]);
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, "original-wallpaper.png");
                using (var image = new Bitmap(64, 64))
                {
                    using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.FromArgb(45 + 38 * i, 59, 97));
                    image.Save(path);
                }
                File.WriteAllText(Path.Combine(root, "options.json"), new JavaScriptSerializer().Serialize(new
                {
                    uiLayout = 3, uiTheme = 4, customBgMode = 0, customBgPath = path, stageWallpaperBg = true
                }));
            }
        }

        private static async Task VerifyDynamicWallpapers(MultiAccountForm screen, AccountProfiles profiles, List<string> ids,
            string output, List<string> checks)
        {
            var report = new Dictionary<string, object>();
            var transitions = new List<object>();
            string prior = "";
            string configBefore = FileHash(EngineConfig);
            report["engineConfigBefore"] = configBefore;
            report["projects"] = transitions;
            try
            {
                var original = await ReadAll(profiles, ids);
                string[] paths = original.Select(state => Convert.ToString(state["customBgPath"])).ToArray();
                Require(paths.All(File.Exists) && paths.Distinct().Count() == ids.Count, "各账号的原壁纸夹具未载入。");
                foreach (var pair in new[] { Tuple.Create("video", VideoProject), Tuple.Create("scene", SceneProject),
                                             Tuple.Create("video-again", VideoProject) })
                {
                    var item = WallpaperEngineLocator.ParseProjectJson(pair.Item2);
                    Require(item != null && item.Type == (pair.Item1 == "scene" ? "scene" : "video"), "壁纸项目类型不匹配。");
                    Require(await screen.SmokeApplySharedWallpaper(item), screen.SmokeWallpaperStatus);
                    string current = screen.SmokeWallpaperWindow;
                    Require(!string.IsNullOrEmpty(current) && FindWindow(null, current) != IntPtr.Zero, "找不到共享播放器窗口。");
                    if (prior.Length > 0) await WaitClosed(prior);
                    prior = current;

                    var hashes = new HashSet<int>();
                    long start = screen.SmokeWallpaperSequence;
                    for (int i = 0; i < 15; i++)
                    {
                        await Task.Delay(200);
                        hashes.Add(screen.SmokeWallpaperSample());
                    }
                    long end = screen.SmokeWallpaperSequence;
                    Require(end - start >= 6 && hashes.Count >= 3, "同屏壁纸画面未持续变化。");
                    var states = await ReadAll(profiles, ids);
                    string mapping = Convert.ToString(states[0]["sharedWallpaper"]);
                    Require(mapping.Length > 0 && states.All(state => Convert.ToString(state["sharedWallpaper"]) == mapping
                        && Convert.ToInt64(state["sharedPaintSequence"]) > start && !Convert.ToBoolean(state["ownWallpaperPlaying"])),
                        "四个账号未全部绘制最新共享帧，或另有独立播放器。");
                    int[] columns = states.Select(state => Convert.ToInt32(state["sharedPaintX"])).ToArray();
                    Require(columns.Zip(columns.Skip(1), (left, right) => left < right).All(value => value),
                        "四个账号未按同屏页坐标从左到右取景。");
                    var inputs = await Task.WhenAll(ids.Select(id => AccountCommand.Send(profiles.Root("main"), id,
                        new TaskCommand { Action = "smokeWallpaperInput", Host = screen.Handle.ToInt64() })));
                    Require(inputs.All(input => Convert.ToBoolean(input["ok"]) && Convert.ToBoolean(input["canvasClick"])),
                        "铺设同屏壁纸后 WebView2 画布未收到输入。");

                    screen.Activate();
                    await Task.Delay(200);
                    Rectangle bounds = screen.RectangleToScreen(screen.ClientRectangle);
                    if (GetForegroundWindow() == screen.Handle)
                    {
                        using (var image = new Bitmap(bounds.Width, bounds.Height))
                        {
                            using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                            image.Save(Path.Combine(output, "four-dynamic-" + pair.Item1 + ".png"));
                        }
                    }
                    transitions.Add(new { kind = pair.Item1, project = Path.GetFileName(Path.GetDirectoryName(pair.Item2)),
                        session = current, sequenceDelta = end - start, distinctSamples = hashes.Count, columns, canvasClicks = inputs.Length });
                }

                long before = screen.SmokeWallpaperSequence;
                screen.WindowState = FormWindowState.Minimized;
                await WaitClosed(prior);
                await Task.Delay(350);
                long paused = screen.SmokeWallpaperSequence;
                await Task.Delay(850);
                Require(screen.SmokeWallpaperSequence == paused && paused >= before, "最小化时仍在发布壁纸帧。");
                screen.WindowState = FormWindowState.Maximized;
                for (int attempt = 0; attempt < 60 && screen.SmokeWallpaperSequence <= paused + 2; attempt++) await Task.Delay(200);
                string restored = screen.SmokeWallpaperWindow;
                Require(!string.IsNullOrEmpty(restored) && screen.SmokeWallpaperSequence > paused + 2 &&
                        FindWindow(null, restored) != IntPtr.Zero, "恢复同屏后动态壁纸未继续。");
                report["resumeSequence"] = screen.SmokeWallpaperSequence;
                await screen.SmokeDetachAsync();
                await WaitClosed(restored);
                var released = await ReadAll(profiles, ids);
                for (int i = 0; i < ids.Count; i++)
                {
                    Require(Convert.ToString(released[i]["sharedWallpaper"]) == "" &&
                        Convert.ToString(released[i]["customBgPath"]) == paths[i] &&
                        DockWindow.GetParent(new IntPtr(Convert.ToInt64(released[i]["window"]))) == IntPtr.Zero,
                        "播放中退出同屏后，账号未恢复其独立壁纸。");
                }
                string configAfter = FileHash(EngineConfig);
                report["engineConfigAfter"] = configAfter;
                Require(configBefore == configAfter, "测试期间 Wallpaper Engine 桌面配置发生变化。");
                checks.Add("four-dynamic-video-scene-switch-pause-restore-input-and-cleanup");
                report["result"] = "passed";
            }
            catch (Exception error)
            {
                report["result"] = "failed";
                report["error"] = error.ToString();
                throw;
            }
            finally
            {
                File.WriteAllText(Path.Combine(output, "dynamic-wallpaper-check.json"), new JavaScriptSerializer().Serialize(report));
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static string FileHash(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        private static async Task WaitClosed(string title)
        {
            for (int attempt = 0; attempt < 40 && FindWindow(null, title) != IntPtr.Zero; attempt++) await Task.Delay(150);
            Require(FindWindow(null, title) == IntPtr.Zero, "旧共享播放器窗口仍然存在。");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string windowClass, string title);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
