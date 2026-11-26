using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class AcceptanceRunner
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static readonly List<object> Results = new List<object>();
    private static string executable;
    private static string output;
    private static string suite = "quick";
    private static bool publicCheck;
    private static string video;
    private static string scene;
    private static string engineConfig;
    private static int soakSeconds;
    private static int failures;
    private static int skipped;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--help"))
            {
                Console.WriteLine("助手验收.exe --exe PATH [--output DIR] [--suite quick|multi|long|all] [--public] [--video PROJECT.JSON --scene PROJECT.JSON --engine-config CONFIG.JSON] [--soak-seconds 7200]");
                return 0;
            }
            Parse(args);
            if (executable == null)
            {
                using (var picker = new OpenFileDialog { Title = "选择冒险与挖矿助手 EXE", Filter = "Windows 应用程序 (*.exe)|*.exe" })
                {
                    if (picker.ShowDialog() != DialogResult.OK) return 2;
                    executable = picker.FileName;
                }
            }
            executable = Path.GetFullPath(executable);
            string hash = HashFile(executable);
            if (output == null)
                output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AandmAcceptance", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            if (Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("验收输出目录必须为空。");
            // A marker opened with CreateNew prevents competing runs sharing one directory.
            using (File.Open(Path.Combine(output, ".acceptance-run"), FileMode.CreateNew)) { }
            string version = FileVersionInfo.GetVersionInfo(executable).ProductVersion;
            Console.WriteLine(output);
            Action save = delegate {
                string result = failures > 0 ? "failed" : skipped > 0 ? "incomplete" : "passed";
                File.WriteAllText(Path.Combine(output, "result.json"),
                    Json.Serialize(new { schema = 1, result, executable, sha256 = hash, version, suite, scenarios = Results }));
            };
            try
            {
                if (suite == "quick" || suite == "all")
                {
                    Run("layout", "--preview {out} 1440 940 A", null, 180, "render-checks.json", "button-layout-check.json", "ui-text-check.json");
                    Run("profile", "--profile-checks {dir}", null, 45, "profile-test-results.json");
                    Run("offline-input", "", Env("AANDM_SMOKE", "1"), 65, "smoke-result.json");
                    if (publicCheck)
                    {
                        Run("public-login", "", Env("AANDM_PUBLIC_SMOKE", "1"), 115, "public-game-check.json");
                        Run("public-input", "", Env("AANDM_PUBLIC_INPUT_SMOKE", "1"), 115, "public-input-check.json");
                    }
                    else Skip("public-login", "未选择 --public");
                }
                if (suite == "multi" || suite == "all")
                {
                    Run("four-offline", "--multi-checks {dir}", null, 180, "multi-account-test-results.json");
                    if (video == null || scene == null || engineConfig == null ||
                        !File.Exists(video) || !File.Exists(scene) || !File.Exists(engineConfig))
                        Skip("four-dynamic", "需要已安装的两个 Wallpaper Engine 作品及引擎 config.json");
                    else
                    {
                        var env = Env("AANDM_ACCEPT_VIDEO_PROJECT", video);
                        env.Add("AANDM_ACCEPT_SCENE_PROJECT", scene);
                        env.Add("AANDM_ACCEPT_ENGINE_CONFIG", engineConfig);
                        Run("four-dynamic", "--multi-checks {dir}", env, 310, "dynamic-wallpaper-check.json", "multi-account-test-results.json");
                    }
                }
                if (suite == "long" || suite == "all")
                {
                    Run("idle-wake-restore", "", Env("AANDM_SMOKE", "1", "AANDM_IDLE_SMOKE", "1",
                        "AANDM_ACCEPT_REPEAT", "20"), 240, "idle-smoke.json");
                    if (soakSeconds == 0) Skip("minimized-soak", "未选择 --soak-seconds");
                    else RunSoak();
                }
            }
            finally { save(); }
            return failures > 0 ? 1 : skipped > 0 ? 2 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 3;
        }
    }

    private static void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--public") { publicCheck = true; continue; }
            if (++i == args.Length) throw new ArgumentException("缺少参数值 " + key);
            string value = args[i];
            switch (key)
            {
                case "--exe": executable = value; break;
                case "--output": output = value; break;
                case "--suite": suite = value; break;
                case "--video": video = Path.GetFullPath(value); break;
                case "--scene": scene = Path.GetFullPath(value); break;
                case "--engine-config": engineConfig = Path.GetFullPath(value); break;
                case "--soak-seconds": soakSeconds = int.Parse(value); break;
                default: throw new ArgumentException("未知参数 " + key);
            }
        }
        if (!new[] { "quick", "multi", "long", "all" }.Contains(suite) ||
            (soakSeconds != 0 && (soakSeconds < 10 || soakSeconds > 86400)))
            throw new ArgumentException("场景或采样时长无效。");
    }

    private static Dictionary<string, string> Env(params string[] pairs)
    {
        var env = new Dictionary<string, string>();
        for (int i = 0; i < pairs.Length; i += 2) env.Add(pairs[i], pairs[i + 1]);
        return env;
    }

    private static void Skip(string name, string reason)
    {
        skipped++;
        Results.Add(new { name, status = "skipped", reason });
        Console.WriteLine(name + " skipped");
    }

    private static string HashFile(string path)
    {
        using (var file = File.OpenRead(path))
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
    }

    private static string Quote(string path)
    {
        return "\"" + path.TrimEnd('\\') + "\"";
    }

    private static Process Launch(string dir, string args, Dictionary<string, string> env)
    {
        if (env != null && env.ContainsKey("AANDM_PUBLIC_INPUT_SMOKE"))
            File.WriteAllText(Path.Combine(dir, "account.dat"), "{\"autoLogin\":false}");
        var info = new ProcessStartInfo(executable,
            args.Replace("{dir}", Quote(dir)).Replace("{out}", Quote(Path.Combine(dir, "preview.png"))) +
                " --data-root " + Quote(dir) + " --no-schtasks");
        info.UseShellExecute = false;
        foreach (string key in info.EnvironmentVariables.Keys.Cast<string>().Where(key => key.StartsWith("AANDM_", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.EnvironmentVariables.Remove(key);
        if (env != null)
            foreach (var pair in env) info.EnvironmentVariables[pair.Key] = pair.Value;
        return Process.Start(info);
    }

    private static void Run(string name, string args, Dictionary<string, string> env, int seconds, params string[] reports)
    {
        string dir = Path.Combine(output, name);
        Directory.CreateDirectory(dir);
        var clock = Stopwatch.StartNew();
        string status = "passed";
        string error = "";
        string lastWindowImage = Path.Combine(dir, "failure-window.png");
        try
        {
            using (var job = new RunJob())
            using (var process = Launch(dir, args, env))
            {
                job.Add(process);
                while (!process.WaitForExit(300))
                {
                    if (clock.Elapsed.TotalSeconds >= seconds)
                    {
                        TrySnapshot(process, lastWindowImage);
                        throw new TimeoutException("超出场景时限");
                    }
                    if (reports.Any(report => HasReportedFailure(Path.Combine(dir, report))))
                        TrySnapshot(process, lastWindowImage);
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("助手进程退出码 " + process.ExitCode);
            }
            foreach (string report in reports)
            {
                string path = Path.Combine(dir, report);
                if (!File.Exists(path))
                {
                    if (report == "dynamic-wallpaper-check.json" && File.Exists(Path.Combine(dir, "multi-account-test-results.json")))
                    {
                        var multi = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "multi-account-test-results.json")));
                        if (Equals(multi["result"], "passed"))
                        {
                            Skip(name, "此构建没有四账号动态壁纸探针");
                            return;
                        }
                        throw new InvalidDataException(Convert.ToString(multi["error"]));
                    }
                    throw new FileNotFoundException("助手未生成 " + report);
                }
                var evidence = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                object verdict;
                bool passed = evidence.TryGetValue("result", out verdict)
                    ? Equals(verdict, "passed")
                    : evidence.TryGetValue("buttonFit", out verdict) && Equals(verdict, true);
                if (report == "smoke-result.json")
                    passed = new[] { "browserReady", "scriptReady", "profileCookieIsolated", "minimizedStillRunning",
                        "restoreImagesStable", "fastDailyScheduling", "collapseControlsWorking" }
                        .All(key => evidence.ContainsKey(key) && Equals(evidence[key], true));
                if (report == "public-input-check.json")
                    passed = passed && evidence.ContainsKey("pointerFallbackReachedEngine") &&
                        Equals(evidence["pointerFallbackReachedEngine"], true);
                if (!passed || (name == "idle-wake-restore" && evidence.ContainsKey("trialCount") &&
                                Convert.ToInt32(evidence["trialCount"]) != 20))
                    {
                    object detail;
                    string reason = evidence.TryGetValue("error", out detail) ? Convert.ToString(detail) : "";
                    throw new InvalidDataException("检查失败 " + report + " " + reason);
                }
                if (name == "idle-wake-restore" && !evidence.ContainsKey("trialCount"))
                {
                    Skip(name, "此构建只支持单次唤醒验收，已有报告记录单次结果");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            failures++;
            status = "failed";
            error = ex.Message;
        }
        var images = Directory.GetFiles(dir, "*.png").Select(Path.GetFileName).ToArray();
        Results.Add(new { name, status, seconds = clock.Elapsed.TotalSeconds, reports, images, error });
        Console.WriteLine(name + " " + status + " " + error);
    }

    private static bool HasReportedFailure(string path)
    {
        try { return File.Exists(path) && File.ReadAllText(path).Contains("\"result\":\"failed\""); }
        catch (IOException) { return false; }
    }

    private static void TrySnapshot(Process process, string path)
    {
        try
        {
            IntPtr window = process.MainWindowHandle;
            Rect bounds;
            if (window == IntPtr.Zero || window != GetForegroundWindow() || !GetWindowRect(window, out bounds)) return;
            using (var image = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
            {
                using (var graphics = Graphics.FromImage(image))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, image.Size);
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch (ArgumentException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void RunSoak()
    {
        string dir = Path.Combine(output, "minimized-soak");
        Directory.CreateDirectory(dir);
        try
        {
            using (var job = new RunJob())
            using (var process = Launch(dir, "", Env("AANDM_MULTI_SMOKE", "1")))
            {
                job.Add(process);
                var wait = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(dir, "multi-cookie-debug.json")) ||
                       process.MainWindowHandle == IntPtr.Zero)
                {
                    if (process.HasExited || wait.Elapsed.TotalSeconds > 40)
                        throw new InvalidOperationException("离线 WebView2 未就绪");
                    process.Refresh();
                    Thread.Sleep(200);
                }
                IntPtr handle = process.MainWindowHandle;
                ShowWindowAsync(handle, 6);
                Thread.Sleep(22000);
                var elapsed = Stopwatch.StartNew();
                int count = 0;
                using (var trace = new StreamWriter(Path.Combine(dir, "samples.jsonl")))
                {
                    do
                    {
                        if (process.HasExited || !IsIconic(handle)) throw new InvalidOperationException("助手未保持最小化");
                        trace.WriteLine(Json.Serialize(new { seconds = elapsed.Elapsed.TotalSeconds, processes = job.Sample(process.Id) }));
                        trace.Flush();
                        count++;
                        if (elapsed.Elapsed.TotalSeconds < soakSeconds) Thread.Sleep(1000);
                    } while (elapsed.Elapsed.TotalSeconds < soakSeconds);
                    trace.WriteLine(Json.Serialize(new { seconds = elapsed.Elapsed.TotalSeconds, processes = job.Sample(process.Id) }));
                }
                Results.Add(new { name = "minimized-soak", status = "passed", seconds = elapsed.Elapsed.TotalSeconds,
                    samples = count + 1, physicalMemoryMiB = PhysicalMemoryMiB(), report = "minimized-soak/samples.jsonl" });
            }
        }
        catch (Exception error)
        {
            failures++;
            Results.Add(new { name = "minimized-soak", status = "failed", error = error.Message });
        }
    }

    private static ulong PhysicalMemoryMiB()
    {
        ulong kib;
        return GetPhysicallyInstalledSystemMemory(out kib) ? kib / 1024 : 0;
    }

    private sealed class RunJob : IDisposable
    {
        private readonly IntPtr handle;
        internal RunJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            var limits = new ExtendedLimits();
            limits.Basic.Flags = 0x2000;
            int size = Marshal.SizeOf(typeof(ExtendedLimits));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(handle, 9, buffer, (uint)size))
                {
                    var error = new System.ComponentModel.Win32Exception();
                    CloseHandle(handle);
                    throw error;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        internal void Add(Process process)
        {
            if (AssignProcessToJobObject(handle, process.Handle)) return;
            if (process.HasExited) return;
            var error = new System.ComponentModel.Win32Exception();
            process.Kill();
            throw error;
        }
        internal object[] Sample(int root)
        {
            int size = 8 + IntPtr.Size * 512;
            IntPtr list = Marshal.AllocHGlobal(size);
            try
            {
                if (!QueryInformationJobObject(handle, 3, list, (uint)size, IntPtr.Zero))
                    throw new System.ComponentModel.Win32Exception();
                int count = Marshal.ReadInt32(list, 4);
                var results = new List<object>();
                for (int i = 0; i < count; ++i)
                {
                    int pid = checked((int)Marshal.ReadIntPtr(list, 8 + i * IntPtr.Size).ToInt64());
                    try
                    {
                        using (var process = Process.GetProcessById(pid))
                            results.Add(new { pid, role = pid == root ? "assistant" :
                                process.ProcessName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase) ? "webview" : process.ProcessName,
                                cpuSeconds = process.TotalProcessorTime.TotalSeconds, privateBytes = process.PrivateMemorySize64,
                                workingSetBytes = process.WorkingSet64, gdi = pid == root ? GetGuiResources(process.Handle, 0) : 0 });
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
                return results.ToArray();
            }
            finally { Marshal.FreeHGlobal(list); }
        }
        public void Dispose() { CloseHandle(handle); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal UIntPtr Minimum, Maximum;
        internal uint Active;
        internal UIntPtr Affinity;
        internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        internal UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr value, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr job, int type, IntPtr value, uint size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kib);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, int flags);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
