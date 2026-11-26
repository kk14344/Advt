using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AandmAuto
{
	internal static class Program
	{
		private static Mutex singleInstanceMutex;
        internal static string BuildId
        {
            get
            {
                var assembly = typeof(Program).Assembly;
                var value = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    assembly, typeof(AssemblyInformationalVersionAttribute));
                return value == null ? assembly.GetName().Version.ToString() : value.InformationalVersion;
            }
        }
        internal static string DisplayVersion
        {
            get
            {
#if VERSIONED_RELEASE
                return BuildId.Split('+')[0];
#else
                return typeof(Program).Assembly.GetName().Version.ToString(2);
#endif
            }
        }
        internal static readonly bool QuietResourceTest =
            Environment.GetEnvironmentVariable("AANDM_LOW_RESOURCE_TEST") == "1" &&
            Array.IndexOf(Environment.GetCommandLineArgs(),"--no-schtasks") >= 0 &&
            Array.IndexOf(Environment.GetCommandLineArgs(),"--data-root") >= 0;
        internal static bool ScheduledLaunch;
        internal static bool CheckinLaunch;
        internal static bool HarvestLaunch;
        internal static bool NoWindowsTaskRegistration;
        internal static EventWaitHandle ScheduleSignal;
        internal static EventWaitHandle ActivationSignal;
        internal static string[] StartupArgs { get; private set; }

        internal static void ReleaseSingleInstanceForRestart()
        {
            Mutex mutex = singleInstanceMutex;
            singleInstanceMutex = null;
            if (mutex == null) return;
            try { mutex.ReleaseMutex(); }
            catch { }
            try { mutex.Dispose(); }
            catch { }
        }

		[STAThread]
		private static void Main(string[] commandArgs)
		{
            StartupArgs = commandArgs ?? new string[0];
            CheckinLaunch=Array.IndexOf(commandArgs,"--checkin-run")>=0;
            HarvestLaunch=Array.IndexOf(commandArgs,"--harvest-run")>=0;
			ScheduledLaunch=CheckinLaunch||HarvestLaunch||Array.IndexOf(commandArgs,"--scheduled-run")>=0;
            NoWindowsTaskRegistration=Array.IndexOf(commandArgs,"--no-schtasks")>=0||Array.IndexOf(commandArgs,"--preview")>=0||Array.IndexOf(commandArgs,"--ui-check")>=0;
			try
			{
                string profile="main";int index=Array.IndexOf(commandArgs,"--profile");
                if(index>=0){if(index+1>=commandArgs.Length)throw new ArgumentException("缺少账号标识");profile=commandArgs[index+1];}
                int dataIndex=Array.IndexOf(commandArgs,"--data-root");
                if(dataIndex>=0){if(dataIndex+1>=commandArgs.Length)throw new ArgumentException("缺少数据目录");Environment.SetEnvironmentVariable("AANDM_DATA_DIR",Path.GetFullPath(commandArgs[dataIndex+1]));}
                AppPaths.ConfigureProfile(profile);
                int multiCheck=Array.IndexOf(commandArgs,"--multi-checks");
                if(multiCheck>=0){if(multiCheck+1>=commandArgs.Length)throw new ArgumentException("缺少测试目录");AppDomain.CurrentDomain.AssemblyResolve+=ResolveWebView2Assembly;NativeBootstrap.Prepare();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);MultiAccountChecks.Run(commandArgs[multiCheck+1]);return;}
                int check=Array.IndexOf(commandArgs,"--profile-checks");
                if(check>=0){if(check+1>=commandArgs.Length)throw new ArgumentException("缺少测试输出目录");ProfileChecks.Run(commandArgs[check+1]);return;}
                int updaterSmoke=Array.IndexOf(commandArgs,"--updater-smoke");
                if(updaterSmoke>=0){if(updaterSmoke+1>=commandArgs.Length)throw new ArgumentException("缺少测试输出目录");UpdateManager.RunSmokeSelfTest(commandArgs[updaterSmoke+1]);return;}
                int diagSmoke=Array.IndexOf(commandArgs,"--diag-smoke");
                if(diagSmoke>=0){if(diagSmoke+1>=commandArgs.Length)throw new ArgumentException("缺少测试输出目录");DiagnosticExport.RunSmokeSelfTest(commandArgs[diagSmoke+1]);return;}
                int p2Smoke=Array.IndexOf(commandArgs,"--p2-smoke");
                if(p2Smoke>=0){if(p2Smoke+1>=commandArgs.Length)throw new ArgumentException("缺少测试输出目录");SelfHealWatchdog.RunSmokeSelfTest(commandArgs[p2Smoke+1]);TestPvpExtras.RunSmokeSelfTest(commandArgs[p2Smoke+1]);return;}
                if(Array.IndexOf(commandArgs,"--ui-check")>=0) {
                    AppDomain.CurrentDomain.AssemblyResolve+=ResolveWebView2Assembly;
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var form=new MainForm(true)) {
                        form.Text="助手 · 渲染检查 "+BuildEdition.AppVersion;
                        Application.Run(form);
                    }
                    return;
                }
                if (commandArgs.Length >= 2 && commandArgs[0] == "--preview") {
                    AppDomain.CurrentDomain.AssemblyResolve += ResolveWebView2Assembly;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    RunPreview(commandArgs);
                    return;
                }
				Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs args)
				{
					WriteFatal(args.Exception);
				};
				AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
				{
					WriteFatal(args.ExceptionObject as Exception);
				};
				bool createdNew = false;
                ScheduleSignal=new EventWaitHandle(false,EventResetMode.AutoReset,AppPaths.SignalName("ScheduledRun",AppPaths.BaseRoot,AppPaths.ProfileId));
                ActivationSignal=new EventWaitHandle(false,EventResetMode.AutoReset,AppPaths.SignalName("Activate",AppPaths.BaseRoot,AppPaths.ProfileId));
				singleInstanceMutex = new Mutex(true, AppPaths.InstanceName(AppPaths.BaseRoot,AppPaths.ProfileId) + ((Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1" || Environment.GetEnvironmentVariable("AANDM_PUBLIC_SMOKE") == "1") ? "Smoke" : ""), out createdNew);
				if (!createdNew)
				{
                    if(ScheduledLaunch){ScheduleSignal.Set();return;}
                    ActivationSignal.Set();
					return;
				}
				AppDomain.CurrentDomain.AssemblyResolve += ResolveWebView2Assembly;
                if(CheckinLaunch&&!ScheduleBook.HasDueCheckinFile(AppPaths.DataRoot,DateTime.UtcNow))return;
                if(ScheduledLaunch&&!CheckinLaunch&&!ScheduleBook.HasDueFile(AppPaths.DataRoot,DateTime.Now))return;
				NativeBootstrap.Prepare();
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				RunApplication();
			}
			catch (Exception ex)
			{
				WriteFatal(ex);
				if(!ScheduledLaunch && Array.IndexOf(commandArgs,"--preview")<0)MessageBox.Show("程序启动失败：\r\n" + ex.Message + "\r\n\r\n详细信息已写入日志。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}

		private static void WriteFatal(Exception ex)
		{
			try
			{
				string dataRoot = AppPaths.DataRoot;
				Directory.CreateDirectory(dataRoot);
				File.AppendAllText(Path.Combine(dataRoot, "startup-error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + BuildId + "\r\n" + ((ex == null) ? "unknown" : ex.ToString()) + "\r\n\r\n", Encoding.UTF8);
			}
			catch
			{
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void RunApplication()
		{
			try
			{
				var form=new MainForm();
				if (QuietResourceTest)
				{
					form.StartPosition = FormStartPosition.Manual;
					form.Location = new System.Drawing.Point(-30000,-30000);
					form.ShowInTaskbar = false;
				}
				if(ScheduledLaunch)form.WindowState=FormWindowState.Minimized;
				Application.Run(form);
			}
			finally
			{
				ReleaseSingleInstanceForRestart();
#if TEST_PVP_EDITION
				Environment.Exit(0);
#endif
			}
		}

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunPreview(string[] args)
        {
            if(args.Length>4 && (args[4]=="A" || args[4]=="B" || args[4]=="C"))UiTheme.ButtonLabelStyle=args[4];
            using (var form = new MainForm(true)) {
                form.RenderPreview(args[1], args.Length>2 ? int.Parse(args[2]) : 1440,
                    args.Length>3 ? int.Parse(args[3]) : 940);
            }
        }

		private static Assembly ResolveWebView2Assembly(object sender, ResolveEventArgs args)
		{
			string name;
			try
			{
				name = new AssemblyName(args.Name).Name;
			}
			catch
			{
				return null;
			}
			string text = null;
			if (name == "Microsoft.Web.WebView2.Core")
			{
				text = "AandmAuto.WebView2.Core.dll";
			}
			else if (name == "Microsoft.Web.WebView2.WinForms")
			{
				text = "AandmAuto.WebView2.WinForms.dll";
			}
			if (text == null)
			{
				return null;
			}
			try
			{
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(text))
				{
					if (stream == null)
					{
						return null;
					}
					byte[] array = new byte[stream.Length];
					int num;
					for (int i = 0; i < array.Length; i += num)
					{
						num = stream.Read(array, i, array.Length - i);
						if (num <= 0)
						{
							break;
						}
					}
					return Assembly.Load(array);
				}
			}
			catch
			{
				return null;
			}
		}
	}
}
