using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AandmAuto
{
	internal sealed partial class MainForm : Form
	{
#if TEST_PVP_EDITION
		private const string GameUrl = "https://47.101.70.170/";

		private const string GameHost = "47.101.70.170";
#else
		private const string GameUrl = "https://118.195.220.62/";

		private const string GameHost = "118.195.220.62";
#endif

		private static Color BgColor { get { return UiTheme.Palette.BgColor; } }

		private static Color SideColor { get { return UiTheme.Palette.SideOuterColor; } }

		private static Color CardColor { get { return UiTheme.Palette.CardColor; } }

		private static Color LineColor { get { return UiTheme.Palette.LineColor; } }

		private static Color GoldColor { get { return UiTheme.Palette.GoldColor; } }

		private static Color TextColor { get { return UiTheme.Palette.TextColor; } }

		private static Color MutedColor { get { return UiTheme.Palette.MutedColor; } }

		private static Color GreenColor { get { return UiTheme.Palette.GreenColor; } }

		private static Color RedColor { get { return UiTheme.Palette.RedColor; } }

		private static Color YellowColor { get { return UiTheme.Palette.YellowColor; } }

		private static Color BlueColor { get { return UiTheme.Palette.BlueColor; } }

		private static Color AutoLogColor { get { return UiTheme.IsLightTheme ? UiTheme.Palette.MutedColor : Color.FromArgb(169, 191, 154); } }

		private string customBgPath = "";
		private string customBgLabel = "";
		private string wallpaperEngineDir = "";

		private readonly JavaScriptSerializer json = new JavaScriptSerializer();

		// 任务完成气泡通知（1.16）：notifyDone 开启时弹出气泡 + 提示音。
		// 复用 MainForm.Background.cs 的 trayIcon；未创建时惰性补一个。
		private void NotifyTaskDone(string task, string message)
		{
			if(scheduleActive||checkinDaily||refineDaily||(Program.ScheduledLaunch&&WindowState==FormWindowState.Minimized))return;
			bool enabled;
			if (!options.TryGetValue("notifyDone", out enabled) || !enabled) return;
			try
			{
				if (trayIcon == null)
				{
					trayIcon = new System.Windows.Forms.NotifyIcon
					{
						Icon = this.Icon ?? SystemIcons.Application,
						Text = BuildEdition.TrayTitle,
						Visible = true
					};
				}
				trayIcon.BalloonTipTitle = "任务完成：" + task;
				string text = message ?? "";
				if (text.Length > 200) text = text.Substring(0, 200);
				trayIcon.BalloonTipText = text;
				trayIcon.ShowBalloonTip(4000);
				System.Media.SystemSounds.Exclamation.Play();
			}
			catch
			{
			}
		}

		private readonly Dictionary<string, bool> options = new Dictionary<string, bool>();

		private readonly Dictionary<string, int> numberOptions = new Dictionary<string, int>();

		private readonly Dictionary<string, Label> statLabels = new Dictionary<string, Label>();

		private readonly Dictionary<string, CheckBox> optionChecks = new Dictionary<string, CheckBox>();

		private readonly HashSet<string> seenLogs = new HashSet<string>();

		private readonly HashSet<string> lastSnapshotLogs = new HashSet<string>();
		private int adventureLogCursor;
		private int adventureLogEpoch;

		private readonly Dictionary<string, int> guildBossDoneDays = new Dictionary<string, int>();

		private int guildBossSyncedPage = -1;

		private const int LogCleanupIntervalMs = 10 * 60 * 1000;

		private const int ExtremeLogCleanupIntervalMs = 3 * 60 * 1000;

		private Timer logCleanupTimer;

		private Timer memoryTrimTimer;

		private bool extremePerformanceMode;

		private bool webViewStartedExtremePerf;

		private bool logUiDirty;

		private int LogHistoryLimit { get { return extremePerformanceMode ? 60 : 200; } }

		private WebView2 webView;
		private BootstrapAssetCache bootstrapAssets;

		private RichTextBox txtLog;

		private Label lblStatus;

		private Label lblDetail;

		private Label lblMining;

		private Label lblBadge;

		private GameButton btnMain;

		private GameButton btnDaily;

		private GameButton btnArena;

		private GameButton btnVoid = null;

		private GameNumber numArenaCount;

		private int arenaTarget = 5;

        private bool dailyMinePhase;
        private bool dailyEncounterPhase;
        private string dailyEncounterRole;
        private bool resumeEncounterOnPage;
        private readonly Dictionary<string, long> encounterClearDays = new Dictionary<string, long>();

		private bool muteLogged;

		private Timer pollTimer;

		private bool coreReady;
		private bool pollCycleBusy;
		private int taskGeneration;
		private bool previewMode;
		private string lastTaskMessage="";

		private bool startRequested;

		private bool startLoopRunning;

		private bool polling;
		private bool gameSnapshotReady;

		private int pageGeneration;
		private int injectingPage = -1;
		private int injectedPage = -1;
		private readonly SelfHealWatchdog selfHeal = new SelfHealWatchdog();
		private Timer selfHealTimer;

		internal void SafeBeginInvoke(Action action)
		{
			if (action == null || IsDisposed || Disposing || !IsHandleCreated) return;
			try
			{
				BeginInvoke((MethodInvoker)delegate
				{
					if (!IsDisposed && !Disposing) action();
				});
			}
			catch (ObjectDisposedException) { }
			catch (InvalidOperationException) { }
		}

		private bool lastRunning;

		private string injectScript;
        private string backgroundGuardScript;

		private bool syncingOptions;

		private string accountUsername = string.Empty;

		private string accountPassword = string.Empty;

		private bool autoLoginEnabled;

		private bool autoLoginDone;

		private bool autoLoginWasInWorld;

		private bool autoLoginReconnecting;

		private bool autoLoginForceSignIn;

		private int autoLoginDisconnectCount;

		private DateTime autoLoginRetryAt = DateTime.MinValue;

		private bool autoLoginBusy;

		private int autoLoginStage;

		private int autoLoginFailures;

		private int autoLoginStableCount;

		private int autoLoginReadyCount;

		private DateTime autoLoginEarliestAt = DateTime.MinValue;

		private DateTime autoLoginAttemptAt = DateTime.MinValue;

		private DateTime autoLoginUserLoginAt = DateTime.MinValue;

		private static string DataRoot()
		{
			return AppPaths.DataRoot;
		}

		public MainForm(bool preview = false)
		{
			previewMode = preview;
			LiquidGlassEngine.PreviewStaticMode = preview;
			Text = BuildEdition.WindowTitle;
            Text+=" · "+new AccountProfiles(AppPaths.BaseRoot).Label(AppPaths.ProfileId??"main");
			AutoScaleDimensions = new SizeF(96f,96f);
			AutoScaleMode = AutoScaleMode.Dpi;
			base.Width = 1440;
			base.Height = 960;
			MinimumSize = new Size(1080, 700);
			base.StartPosition = FormStartPosition.CenterScreen;
			base.WindowState = FormWindowState.Maximized;
            if(AppPaths.ProfileId!="main" && !preview){base.WindowState=FormWindowState.Normal;base.StartPosition=FormStartPosition.WindowsDefaultLocation;}
			Font = UiTheme.GameFont(9f, FontStyle.Regular);
			DoubleBuffered = true;
			json.MaxJsonLength = int.MaxValue;
			LoadOptions();
			extremePerformanceMode = ExtremePerformanceSettings.Load(AppPaths.BaseRoot);
			webViewStartedExtremePerf = extremePerformanceMode;
			UiTheme.ExtremePerformanceMode = extremePerformanceMode;
			if (extremePerformanceMode)
			{
				UiTheme.CurrentTheme = UiTheme.ThemeNativeLight;
				UiTheme.GlassBlur = 0;
				UiTheme.CustomBackgroundFit = UiTheme.BgFitCover;
				UiTheme.LoadCustomBackground("", 35, UiTheme.BgModeStatic);
				UiTheme.ReleasePerformanceCaches();
			}
			else
			{
				UiTheme.CurrentTheme = GetNumberOption("uiTheme", 0);
				UiTheme.GlassBlur = GetNumberOption("glassBlur", 12);
				LiquidGlassEngine.Preset = GetNumberOption("glassPreset", LiquidGlassEngine.PresetBalanced);
				LiquidGlassEngine.MotionEnabled = !options.ContainsKey("glassMotion") || options["glassMotion"];
				UiTheme.CustomBackgroundFit = GetNumberOption("customBgFit", 0);
				UiTheme.SetCustomCrop(
					GetNumberOption("customBgCropX", 0),
					GetNumberOption("customBgCropY", 0),
					GetNumberOption("customBgCropW", 10000),
					GetNumberOption("customBgCropH", 10000));
				UiTheme.LoadCustomBackground(customBgPath, GetNumberOption("customBgOpacity", 35), GetNumberOption("customBgMode", 0));
				UiTheme.SyncCroppedWallpaperFile(DataRoot());
			}
			BackColor = BgColor;
			ForeColor = TextColor;
			activeUiLayout = GetNumberOption("uiLayout", 0);
			LoadGuildBossDaily();
            LoadEncounterClearDays();
			if (!previewMode && !Program.QuietResourceTest &&
                Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE") != "1" &&
                Environment.GetEnvironmentVariable("AANDM_SMOKE") != "1" &&
                Environment.GetEnvironmentVariable("AANDM_PUBLIC_SMOKE") != "1" &&
                Environment.GetEnvironmentVariable("AANDM_PUBLIC_INPUT_SMOKE") != "1") LoadAccountSettings();
			BuildInterface();
			LoadInjectScript();
			LoadIconResource();
			SetupBackgroundMode();
            SetupScheduling();
			UpdateSettingsSummary();
			if (!previewMode)
			{
				if (BuildEdition.IsLowLoad && !string.IsNullOrEmpty(BuildEdition.StartupBanner)) AddLog(BuildEdition.StartupBanner, GoldColor);
				ConfigureBackgroundRuntime();
				int retentionDays = GetNumberOption("auditRetentionDays", 14);
				string currentRoot = DataRoot();
				System.Threading.Tasks.Task.Run(delegate { DiskGovernance.RotateAuditLogs(currentRoot, retentionDays); });
				logCleanupTimer = new Timer { Interval = extremePerformanceMode ? ExtremeLogCleanupIntervalMs : LogCleanupIntervalMs };
				logCleanupTimer.Tick += delegate { ClearAdventureLogs(true); };
				logCleanupTimer.Start();
				UpdateMemoryTrimTimer();
				base.Shown += OnShown;
			}
			base.FormClosing += OnFormClosing;
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			WindowStyle.ApplyThemeChrome(this);
		}







		private Control BuildStatusBody()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			tableLayoutPanel.Dock = DockStyle.Fill;
			tableLayoutPanel.BackColor = Color.Transparent;
			tableLayoutPanel.ColumnCount = 1;
			tableLayoutPanel.RowCount = 6;
			tableLayoutPanel.Margin = Padding.Empty;
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 26f : 24f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 22f : 20f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 22f : 20f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, options["showMiningRoute"] ? 22f : 0f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			lblStatus = new SmoothLabel();
			lblStatus.Text = "等待加载";
			lblStatus.Dock = DockStyle.Fill;
			lblStatus.ForeColor = GoldColor;
			lblStatus.Font = UiTheme.GameFont(11f, FontStyle.Bold);
			lblStatus.TextAlign = ContentAlignment.MiddleLeft;
			lblStatus.BackColor = Color.Transparent;
			lblStatus.AutoEllipsis = true;
			lblStatus.Padding = new Padding(1, 0, 0, 0);

			lblDetail = new SmoothLabel();
			lblDetail.Text = "登录后选择任务";
			lblDetail.Dock = DockStyle.Fill;
			lblDetail.ForeColor = MutedColor;
			lblDetail.Font = UiTheme.GameFont(8f, FontStyle.Regular);
			lblDetail.TextAlign = ContentAlignment.MiddleLeft;
			lblDetail.BackColor = Color.Transparent;
			lblDetail.AutoEllipsis = true;

			lblMining = new SmoothLabel();
			lblMining.Text = "矿镐 --/-- · 巨魔 Lv-- · 发现 -- · 深度 --";
			lblMining.Dock = DockStyle.Fill;
			lblMining.ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : Color.FromArgb(197, 211, 169);
			lblMining.Font = UiTheme.GameFont(8f, FontStyle.Regular);
			lblMining.TextAlign = ContentAlignment.MiddleLeft;
			lblMining.BackColor = Color.Transparent;
			lblMining.AutoEllipsis = true;

			mineRouteLabel = new SmoothLabel
			{
				Text = "矿区目标 · 可在游戏偏好开启标记",
				Dock = DockStyle.Fill,
				ForeColor = GoldColor,
				Font = UiTheme.GameFont(7.75f),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true,
				BackColor = Color.Transparent,
				Visible = options["showMiningRoute"]
			};

			TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
			tableLayoutPanel2.Dock = DockStyle.Fill;
			tableLayoutPanel2.BackColor = Color.Transparent;
			tableLayoutPanel2.ColumnCount = 3;
			tableLayoutPanel2.RowCount = 2;
			tableLayoutPanel2.Margin = new Padding(0, 2, 0, 2);
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			AddStat(tableLayoutPanel2, "digs", "挖矿 0", 0, 0);
			AddStat(tableLayoutPanel2, "golems", "魔像 0", 1, 0);
			AddStat(tableLayoutPanel2, "bosses", "BOSS 0", 2, 0);
			AddStat(tableLayoutPanel2, "wins", "胜利 0", 0, 1);
			AddStat(tableLayoutPanel2, "losses", "失败 0", 1, 1);
			AddStat(tableLayoutPanel2, "chests", "宝箱 0", 2, 1);

			var extraCard = new InsetPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(8, 5, 8, 5),
				Margin = new Padding(0, 4, 0, 0)
			};
			var extraStack = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				BackColor = Color.Transparent,
				Margin = Padding.Empty
			};
			extraStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			extraStack.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 24f : 22f));
			extraStack.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 22f : 20f));
			extraStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			lblMinePlan = new SmoothLabel
			{
				Text = "推荐矿层：等待游戏数据",
				Dock = DockStyle.Fill,
				AutoEllipsis = true,
				ForeColor = GoldColor,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				BackColor = Color.Transparent
			};
			lblMineSlots = new SmoothLabel
			{
				Text = "矿队：等待矿山数据",
				Dock = DockStyle.Fill,
				AutoEllipsis = true,
				ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? BlueColor : Color.FromArgb(198, 214, 178),
				Font = UiTheme.GameFont(7.75f),
				TextAlign = ContentAlignment.MiddleLeft,
				BackColor = Color.Transparent
			};
			lblStatusSchedule = new SmoothLabel
			{
				Text = "自动续跑与签到：等待读取",
				Dock = DockStyle.Fill,
				AutoEllipsis = true,
				ForeColor = MutedColor,
				Font = UiTheme.GameFont(7.75f),
				TextAlign = ContentAlignment.TopLeft,
				BackColor = Color.Transparent
			};
			extraStack.Controls.Add(lblMinePlan, 0, 0);
			extraStack.Controls.Add(lblMineSlots, 0, 1);
			extraStack.Controls.Add(lblStatusSchedule, 0, 2);
			extraCard.Controls.Add(extraStack);

			tableLayoutPanel.Controls.Add(lblStatus, 0, 0);
			tableLayoutPanel.Controls.Add(lblDetail, 0, 1);
			tableLayoutPanel.Controls.Add(lblMining, 0, 2);
			tableLayoutPanel.Controls.Add(mineRouteLabel, 0, 3);
			tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 4);
			tableLayoutPanel.Controls.Add(extraCard, 0, 5);
			return tableLayoutPanel;
		}

		private void AddStat(TableLayoutPanel parent, string key, string text, int column, int row)
		{
			Label label = new SmoothLabel();
			label.Text = text;
			label.Dock = DockStyle.Fill;
			label.TextAlign = ContentAlignment.MiddleLeft;
			label.ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : Color.FromArgb(218, 216, 180);
			label.Font = UiTheme.GameFont(8f, FontStyle.Regular);
			label.BackColor = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? Color.Transparent : (UiTheme.IsLightTheme ? UiTheme.Palette.InsetColor : Color.FromArgb(76, 63, 36));
			label.Margin = new Padding(1);
			label.Padding = new Padding(5, 0, 0, 0);
			parent.Controls.Add(label, column, row);
			statLabels[key] = label;
		}

		private async void BtnAdvanced_Click(object sender, EventArgs e) { await OpenUnifiedSettingsAsync("mining"); }

		private async void BtnDailyOptions_Click(object sender, EventArgs e) { await OpenUnifiedSettingsAsync("daily"); }

		private async void OpenOptions(string category) { await OpenUnifiedSettingsAsync(category); }

		private async void BtnChest_Click(object sender, EventArgs e) { await OpenUnifiedSettingsAsync("resources"); }

		private async Task OpenUnifiedSettingsAsync(string initialCategory)
		{
			Dictionary<int, string> branchTitles = new Dictionary<int, string>();
			if (coreReady)
			{
				try
				{
					string resultJson = await ExecuteLuaAsync("return __MWA:research_branches()");
					Dictionary<string, object> data = json.Deserialize<Dictionary<string, object>>(resultJson);
					if (data != null && data.ContainsKey("list"))
					{
						var rows = new List<Dictionary<string, object>>();
						var map = data["list"] as Dictionary<string, object>;
						if (map != null)
						{
							foreach (var value in map.Values) if (value is Dictionary<string, object>) rows.Add((Dictionary<string, object>)value);
						}
						else
						{
							var array = data["list"] as System.Collections.IEnumerable;
							if (array != null) foreach (var value in array) if (value is Dictionary<string, object>) rows.Add((Dictionary<string, object>)value);
						}
						foreach (var row in rows)
						{
							int key = GetInt(row, "key");
							string title = GetString(row, "title");
							bool available = row.ContainsKey("level") && (!row.ContainsKey("available") || Convert.ToBoolean(row["available"]));
							int max = GetInt(row, "max");
							branchTitles[key] = title + (available ? "（" + GetInt(row, "level") + (max > 0 ? "/" + max : "") + "级）" : "（等级待同步）");
						}
					}
				}
				catch { }
			}

			var scheduleCopy = json.Deserialize<ScheduleBook>(json.Serialize(scheduleBook));
			string planText = lblMinePlan != null ? lblMinePlan.Text : "";
			string slotsText = lblMineSlots != null ? lblMineSlots.Text : "";

			int themeBeforeSettings = UiTheme.CurrentTheme;
			bool themePreviewed = false;
			using (var wallpaperSnapshot = UiTheme.EngineFrame == null ? null : new Bitmap(UiTheme.EngineFrame))
			using (var dialog = new AssistantSettingsForm(options, numberOptions, scheduleCopy, branchTitles, MineResearchMask(), planText, slotsText, initialCategory, customBgPath, DataRoot(), customBgLabel, wallpaperEngineDir, ClientSize, wallpaperEngineProject, playbackStatus, wallpaperSnapshot, extremePerformanceMode, webViewStartedExtremePerf, RetryIndependentWallpaper, () => playbackStatus, AddLog, () => txtLog != null ? txtLog.Text : "", delegate(int previewTheme)
			{
				if (!extremePerformanceMode && previewTheme != UiTheme.CurrentTheme)
				{
					themePreviewed = true;
					SwitchUiAppearance(activeUiLayout, previewTheme, customBgPath, UiTheme.CustomBackgroundOpacity, UiTheme.CustomBackgroundMode, UiTheme.GlassBlur, UiTheme.CustomBackgroundFit);
				}
			}))
			{
				dialog.BindMysteryShop(mysteryProducts);
				if (dialog.ShowDialog(this) != DialogResult.OK)
				{
					if (themePreviewed && !extremePerformanceMode && UiTheme.CurrentTheme != themeBeforeSettings)
					{
						SwitchUiAppearance(activeUiLayout, themeBeforeSettings, customBgPath, UiTheme.CustomBackgroundOpacity, UiTheme.CustomBackgroundMode, UiTheme.GlassBlur, UiTheme.CustomBackgroundFit);
					}
					return;
				}

				bool prevExtreme = extremePerformanceMode;
				bool nextExtreme = dialog.ExtremePerformanceMode;
				int prevLayout = activeUiLayout;
				int prevTheme = UiTheme.CurrentTheme;
				int prevSavedTheme = GetNumberOption("uiTheme", 0);
				string prevBg = customBgPath ?? "";
				string prevProject = wallpaperEngineProject;
				int prevOpacity = UiTheme.CustomBackgroundOpacity;
				int prevBgMode = UiTheme.CustomBackgroundMode;
				int prevBgFit = UiTheme.CustomBackgroundFit;
				int prevCropX = UiTheme.CustomBgCropX;
				int prevCropY = UiTheme.CustomBgCropY;
				int prevCropW = UiTheme.CustomBgCropW;
				int prevCropH = UiTheme.CustomBgCropH;
				int prevBlur = UiTheme.GlassBlur;
				int prevGlassPreset = LiquidGlassEngine.Preset;
				bool prevGlassMotion = LiquidGlassEngine.MotionEnabled;
				bool prevStage = !options.ContainsKey("stageWallpaperBg") || options["stageWallpaperBg"];
				foreach (var pair in dialog.Values) options[pair.Key] = pair.Value;
				foreach (var pair in dialog.Numbers) numberOptions[pair.Key] = pair.Value;
				mysteryProducts = dialog.SelectedMysteryProducts();
				customBgPath = dialog.CustomBackgroundPath ?? "";
				customBgLabel = dialog.CustomBackgroundLabel ?? "";
				wallpaperEngineDir = dialog.WallpaperEngineDir ?? "";
				wallpaperEngineProject = dialog.WallpaperEngineProject ?? "";
				bool projectChanged = !string.Equals(wallpaperEngineProject, prevProject, StringComparison.OrdinalIgnoreCase);
				if (projectChanged || dialog.WallpaperEngineSelectionChanged)
                {
                    playbackFailed = false;
                    forceWallpaperRetry = dialog.WallpaperEngineSelectionChanged;
                }
				ClampNumberOptions();
				int nextLayout = GetNumberOption("uiLayout", 0);
				int nextTheme = GetNumberOption("uiTheme", 0);
				int nextOpacity = GetNumberOption("customBgOpacity", 35);
				int nextBgMode = GetNumberOption("customBgMode", 0);
				int nextBgFit = GetNumberOption("customBgFit", 0);
				int nextCropX = GetNumberOption("customBgCropX", 0);
				int nextCropY = GetNumberOption("customBgCropY", 0);
				int nextCropW = GetNumberOption("customBgCropW", 10000);
				int nextCropH = GetNumberOption("customBgCropH", 10000);
				int nextBlur = GetNumberOption("glassBlur", 12);
				int nextGlassPreset = GetNumberOption("glassPreset", LiquidGlassEngine.PresetBalanced);
				bool nextGlassMotion = !options.ContainsKey("glassMotion") || options["glassMotion"];
				LiquidGlassEngine.Preset = nextGlassPreset;
				LiquidGlassEngine.MotionEnabled = nextGlassMotion;
				bool nextStage = !options.ContainsKey("stageWallpaperBg") || options["stageWallpaperBg"];

				bool refineEnabled;
				if (options.TryGetValue("dailyRefine", out refineEnabled) && !refineEnabled) CancelHarvest("refine");

				ClearWorkshopBlock();
				SaveOptions();
                if (dialog.WallpaperTrayIconSetting.HasValue)
                {
                    try
                    {
                        WallpaperEngineLocator.SetTrayIconHidden(dialog.WallpaperTrayIconSetting.Value);
                        AddLog("已保存托盘图标设置，重启 Wallpaper Engine 后生效。", BlueColor);
                    }
                    catch (Exception error)
                    {
                        AddLog("未能保存 Wallpaper Engine 托盘设置 · " + error.Message, YellowColor);
                    }
                }
				ExtremePerformanceSettings.Save(AppPaths.BaseRoot, nextExtreme);
				bool cropChanged = nextCropX != prevCropX || nextCropY != prevCropY || nextCropW != prevCropW || nextCropH != prevCropH;
				if (nextExtreme != prevExtreme)
				{
					activeUiLayout = nextLayout;
					await ApplyExtremePerformanceModeAsync(nextExtreme, true);
					if (nextLayout != prevLayout) AddLog("已切换主面板版本：" + UiLayoutName(nextLayout), BlueColor);
				}
				else if (nextExtreme)
				{
					if (nextLayout != prevLayout)
					{
						SwitchUiAppearance(nextLayout, UiTheme.ThemeNativeLight, "", nextOpacity, UiTheme.BgModeStatic, 0, nextBgFit);
						AddLog("已切换主面板版本：" + UiLayoutName(nextLayout), BlueColor);
					}
					if (projectChanged || dialog.WallpaperEngineSelectionChanged || nextTheme != prevSavedTheme || !string.Equals(customBgPath, prevBg, StringComparison.OrdinalIgnoreCase) || nextBgMode != prevBgMode || cropChanged)
					{
						AddLog("主题与壁纸配置已保存，关闭极致性能模式后将恢复生效。", BlueColor);
					}
				}
				else if (projectChanged || dialog.WallpaperEngineSelectionChanged || nextLayout != prevLayout || nextTheme != prevTheme || !string.Equals(customBgPath, prevBg, StringComparison.OrdinalIgnoreCase) || nextOpacity != prevOpacity || nextBgMode != prevBgMode || nextBgFit != prevBgFit || cropChanged || nextBlur != prevBlur || nextStage != prevStage || nextGlassPreset != prevGlassPreset || nextGlassMotion != prevGlassMotion)
				{
					UiTheme.SetCustomCrop(nextCropX, nextCropY, nextCropW, nextCropH);
					SwitchUiAppearance(nextLayout, nextTheme, customBgPath, nextOpacity, nextBgMode, nextBlur, nextBgFit);
					if (nextLayout != prevLayout) AddLog("已切换主面板版本：" + UiLayoutName(nextLayout), BlueColor);
					if (nextTheme != prevTheme) AddLog("已切换界面风格：" + UiTheme.ThemeName(nextTheme), BlueColor);
					if (!string.Equals(customBgPath, prevBg, StringComparison.OrdinalIgnoreCase))
					{
						AddLog(string.IsNullOrEmpty(customBgPath) ? "已清除自定义背景图。" : "已应用自定义背景：" + (string.IsNullOrEmpty(customBgLabel) ? "本地图片" : customBgLabel), BlueColor);
					}
					if (nextBgMode != prevBgMode) AddLog("已切换背景显示模式：" + UiTheme.BgModeName(nextBgMode), BlueColor);
					if (nextBgFit != prevBgFit) AddLog("已切换壁纸画面适配：" + UiTheme.BgFitName(nextBgFit), BlueColor);
					if (cropChanged) AddLog("已更新壁纸自定义裁剪区域。", BlueColor);
					await ApplyStageWallpaperAsync();
				}
				RefreshOptionChecks();
				await ApplyOptionsAsync();

				if (dialog.StopQueueRequested)
				{
					CancelScheduledQueue();
					CancelHarvest();
					await StopCurrentTasksAsync();
					AddLog("任务队列已停止。", YellowColor);
				}
				else if (dialog.ScheduleTouched || dialog.RunPlanId != null)
				{
					ApplyScheduleChanges(dialog.Schedule, dialog.RunPlanId);
				}

				UpdateSettingsSummary();
				AddLog("助手设置已保存：横挖层 " + GetNumberOption("horizontalStartDepth", 150) + " 层，工坊 " + GetNumberOption("workshopDepth", 150) + " 层/" + GetNumberOption("workshopMinutes", 120) + " 分。", BlueColor);

				if (nextExtreme != webViewStartedExtremePerf && !previewMode && !Program.QuietResourceTest)
				{
					string promptText = nextExtreme
						? "极致性能模式的极简外观与后台渲染暂停已即时生效。\r\n\r\n是否立即重启本窗口，以启用浏览器底层低内存启动参数（进一步降低 2GB 运存占用）？"
						: "已关闭极致性能模式并恢复原主题与壁纸。\r\n\r\n是否立即重启本窗口，以恢复浏览器默认缓存与内存参数？";
					DialogResult restartChoice = MessageBox.Show(
						this,
						promptText,
						"重启生效底层内存参数",
						MessageBoxButtons.YesNo,
						MessageBoxIcon.Question);
					if (restartChoice == DialogResult.Yes)
					{
						await RestartCurrentProfileAsync();
						return;
					}
				}

				if (dialog.OpenDevTools) BtnDevTools_Click(this, EventArgs.Empty);
			}
		}

		private async Task ApplyExtremePerformanceModeAsync(bool enabled, bool notifyPeers)
		{
			bool changed = extremePerformanceMode != enabled;
			extremePerformanceMode = enabled;
			UiTheme.ExtremePerformanceMode = enabled;

			if (enabled)
			{
				ReleaseSharedWallpaper();
				StopIndependentWallpaper();
				if (bgRuntimeTimer != null) bgRuntimeTimer.Stop();
				if (desktopCapture != null)
				{
					desktopCapture.Dispose();
					desktopCapture = null;
				}
				UiTheme.ClearLiveDesktopFrame();
				UiTheme.ReleasePerformanceCaches();
				GameText.ClearCache();
			}

			int targetTheme = enabled ? UiTheme.ThemeNativeLight : GetNumberOption("uiTheme", 0);
			string targetBgPath = enabled ? "" : customBgPath;
			int targetOpacity = GetNumberOption("customBgOpacity", 35);
			int targetBgMode = enabled ? UiTheme.BgModeStatic : GetNumberOption("customBgMode", 0);
			int targetBlur = enabled ? 0 : GetNumberOption("glassBlur", 12);
			int targetFit = GetNumberOption("customBgFit", 0);

			if (!enabled)
			{
				UiTheme.SetCustomCrop(
					GetNumberOption("customBgCropX", 0),
					GetNumberOption("customBgCropY", 0),
					GetNumberOption("customBgCropW", 10000),
					GetNumberOption("customBgCropH", 10000));
				playbackFailed = false;
			}

			SwitchUiAppearance(activeUiLayout, targetTheme, targetBgPath, targetOpacity, targetBgMode, targetBlur, targetFit);
			if (detachedSidebar != null && !detachedSidebar.IsDisposed) detachedSidebar.BackColor = SideColor;

			UpdateLogRuntimePolicy();
			UpdateMemoryTrimTimer();
			UpdateGamePollInterval();
			await ApplyStageWallpaperAsync();
			if (!enabled) FlushDeferredLogUi();
			KeepPageRunning();

			if (MultiAccountForm.Current != null && !MultiAccountForm.Current.IsDisposed)
			{
				MultiAccountForm.Current.OnExtremePerformanceChanged(enabled);
			}

			if (enabled)
			{
				TrimRuntimeMemory();
			}

			if (changed)
			{
				AddLog(enabled
					? "已开启极致性能模式（全局生效：极简外观、低内存目标、挂机暂停画面渲染，自动任务原速无损）。"
					: "已关闭极致性能模式，已恢复原界面主题与壁纸配置。", BlueColor);
				if (notifyPeers)
				{
					NotifyOtherProfilesExtremePerf(enabled);
				}
			}
		}

		private async Task RestartCurrentProfileAsync()
		{
			try
			{
				string profileId = AppPaths.ValidateProfileId(AppPaths.ProfileId ?? "main");
				string baseRoot = Path.GetFullPath(AppPaths.BaseRoot);
				string exe = Application.ExecutablePath;
				var argBuilder = new StringBuilder();
				argBuilder.Append("--profile ").Append(profileId);
				argBuilder.Append(" --data-root ").Append(AppPaths.QuoteArgument(baseRoot));
				if (Program.NoWindowsTaskRegistration) argBuilder.Append(" --no-schtasks");
				var info = new ProcessStartInfo(exe, argBuilder.ToString())
				{
					UseShellExecute = false,
					WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
				};
				info.EnvironmentVariables["AANDM_DATA_DIR"] = baseRoot;

				ResetAllTaskStates();
				coreReady = false;
				if (pollTimer != null) pollTimer.Stop();
				if (dailyTaskTimer != null) dailyTaskTimer.Stop();
				if (runtimeHeartbeat != null) runtimeHeartbeat.Stop();
				if (memoryTrimTimer != null) memoryTrimTimer.Stop();
				if (logCleanupTimer != null) logCleanupTimer.Stop();
				if (webView != null)
				{
					try { ((Component)webView).Dispose(); }
					catch { }
					webView = null;
				}
				await Task.Delay(500);
				Program.ReleaseSingleInstanceForRestart();
				using (var proc = Process.Start(info)) { }
				Close();
			}
			catch (Exception ex)
			{
				AddLog("重启失败，请手动重启助手：" + ex.Message, RedColor);
			}
		}

		private void UpdateLogRuntimePolicy()
		{
			int interval = extremePerformanceMode ? ExtremeLogCleanupIntervalMs : LogCleanupIntervalMs;
			if (logCleanupTimer != null && logCleanupTimer.Interval != interval)
			{
				logCleanupTimer.Interval = interval;
			}
			int limit = LogHistoryLimit;
			if (logHistoryLines.Count > limit)
			{
				int excess = logHistoryLines.Count - limit;
				logHistoryLines.RemoveRange(0, excess);
				logHistoryColors.RemoveRange(0, excess);
				if (extremePerformanceMode) logUiDirty = true;
			}
		}

		private void UpdateMemoryTrimTimer()
		{
			if (previewMode || IsDisposed) return;
			if (extremePerformanceMode)
			{
				if (memoryTrimTimer == null)
				{
					memoryTrimTimer = new Timer { Interval = 45000 };
					memoryTrimTimer.Tick += delegate { TrimRuntimeMemory(); };
				}
				if (!memoryTrimTimer.Enabled) memoryTrimTimer.Start();
			}
			else if (memoryTrimTimer != null)
			{
				memoryTrimTimer.Stop();
			}
		}

		private void TrimRuntimeMemory()
		{
			if (!extremePerformanceMode || previewMode || IsDisposed) return;
			UiTheme.ReleasePerformanceCaches();
			GameText.ClearCache();
			ExtremePerformanceSettings.TrimCurrentProcessMemory();
		}

		private bool ShouldDeferLogUi()
		{
			return extremePerformanceMode && (IsMinimizedOrHostMinimized() || currentBottomTab != "log");
		}

		private void FlushDeferredLogUi()
		{
			if (!logUiDirty || txtLog == null || txtLog.IsDisposed) return;
			logUiDirty = false;
			txtLog.Clear();
			ReplayLogHistoryToRtb();
			UpdateRecentLogView();
		}

		private async void ApplyScheduleChanges(ScheduleBook updated, string runPlanId)
		{
			try
			{
				foreach (var p in updated.Plans)
				{
					var old = scheduleBook.Plans.Find(x => x.Id == p.Id);
					if (old == null || PlanDefinition(old) != PlanDefinition(p)) p.Cursor = ScheduleBook.Stamp(DateTime.Now);
				}
				updated.Pending = scheduleBook.Pending;
				foreach (var p in updated.Plans)
				{
					var old = scheduleBook.Plans.Find(x => x.Id == p.Id);
					if (old != null && PlanDefinition(old) == PlanDefinition(p)) p.Cursor = old.Cursor;
				}
				await Task.Run(delegate { ScheduleBook.UpdateWindowsTask(updated, DataRoot(), Application.ExecutablePath); });
				updated.Pending = scheduleBook.Pending.FindAll(r => !scheduleBook.Plans.Exists(p => p.Id == r.PlanId) || updated.Plans.Exists(p => p.Id == r.PlanId && p.Enabled));
				foreach (var p in updated.Plans)
				{
					var old = scheduleBook.Plans.Find(x => x.Id == p.Id);
					if (old != null && PlanDefinition(old) == PlanDefinition(p)) p.Cursor = old.Cursor;
				}
				updated.Harvest = scheduleBook.Harvest;
				bool checkinChanged = updated.AutoCheckin != scheduleBook.AutoCheckin;
				updated.LastCheckinSlot = (checkinChanged && updated.AutoCheckin)
					? ScheduleBook.CheckinSlotUtc(DateTime.UtcNow).Ticks
					: scheduleBook.LastCheckinSlot;
				bool switches = updated.MineFollowup != scheduleBook.MineFollowup
					|| updated.EscortFollowup != scheduleBook.EscortFollowup
					|| updated.RefineFollowup != scheduleBook.RefineFollowup
					|| checkinChanged;
				bool removed = updated.RemoveDisabledHarvests();
				updated.Checkpoint = scheduleBook.Checkpoint;
				scheduleBook = updated;
				SaveSchedule();
				if (removed || switches) SyncHarvestWindowsTask();
				if (checkinChanged) SyncCheckinWindowsTask();
				if (switches)
				{
					nextScheduleProbeUtc = DateTime.MinValue;
					bool autoOn = updated.MineFollowup || updated.EscortFollowup || updated.RefineFollowup || updated.AutoCheckin;
					AddLog(autoOn ? "已开启自动续跑与签到（矿山 / 矿车 / 炼化 / 00:00·12:00·18:00）" : "已关闭自动续跑与签到", BlueColor);
				}
				if (runPlanId != null)
				{
					var p = scheduleBook.Plans.Find(x => x.Id == runPlanId);
					if (p != null) EnqueueSchedule(new ScheduleRun { PlanId = p.Id, Name = p.Name + "（手动）", Due = DateTime.Now, MaxWaitMinutes = p.MaxWaitMinutes, Steps = p.Steps });
				}
				UpdateSettingsSummary();
			}
			catch (Exception error)
			{
				AddLog("定时设置未保存：" + error.Message, YellowColor);
			}
		}

		private Control BuildLogsBody()
		{
			txtLog = GlassRichTextBox.Create(UiTheme.CurrentTheme == UiTheme.ThemeClassic
				? Color.FromArgb(68, 57, 33) : UiTheme.Palette.LogBackColor);
			txtLog.Dock = DockStyle.Fill;
			txtLog.ReadOnly = true;
			txtLog.ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : Color.FromArgb(188, 205, 166);
			txtLog.BorderStyle = BorderStyle.None;
			txtLog.WordWrap = true;
			txtLog.DetectUrls = false;
			txtLog.HideSelection = false;
			txtLog.ScrollBars = RichTextBoxScrollBars.Vertical;
			txtLog.ShortcutsEnabled = true;
			txtLog.Margin = new Padding(0, 2, 0, 0);
			txtLog.VisibleChanged += delegate
			{
				if (txtLog != null && !txtLog.IsDisposed && txtLog.Visible && logUiDirty && !IsMinimizedOrHostMinimized())
				{
					FlushDeferredLogUi();
				}
			};
			return GlassRichTextBox.WithBackdrop(txtLog);
		}


		private void LoadIconResource()
		{
			try
			{
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.icon.ico"))
				{
					if (stream != null)
					{
						base.Icon = new Icon(stream);
					}
				}
			}
			catch
			{
			}
		}

		private void LoadInjectScript()
		{
			try
			{
                using(var guard=Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.background.js"))
                    using(var reader=new StreamReader(guard,Encoding.UTF8))backgroundGuardScript=reader.ReadToEnd();
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.inject.js"))
				{
					if (stream != null)
					{
						using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8))
						{
							injectScript = streamReader.ReadToEnd();
							return;
						}
					}
				}
			}
			catch (Exception ex)
			{
				injectScript = null;
				AddLog("读取注入脚本失败：" + ex.Message, RedColor);
			}
		}

		private async void OnShown(object sender, EventArgs e)
		{
			AddLog("程序已启动，正在初始化内置 Edge 浏览器…", BlueColor);
			await InitializeWebViewAsync();
			if (Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1" && Environment.GetEnvironmentVariable("AANDM_IDLE_SMOKE") == "1")
			{
				await RunIdleSmokeAsync();
				return;
			}
			string stabilityProjects = Environment.GetEnvironmentVariable("AANDM_STABILITY_PROJECTS");
			if (Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1" && !string.IsNullOrEmpty(stabilityProjects))
			{
				await RunWallpaperStabilityAsync(stabilityProjects.Split('|'));
				return;
			}
			string wallpaperSmokeProject = Environment.GetEnvironmentVariable("AANDM_WALLPAPER_SMOKE");
			if (Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1" && !string.IsNullOrEmpty(wallpaperSmokeProject))
			{
				await RunIndependentWallpaperSmokeAsync(wallpaperSmokeProject);
				return;
			}
            if(Environment.GetEnvironmentVariable("AANDM_SMOKE")=="1"&&Environment.GetEnvironmentVariable("AANDM_REFINE_SMOKE")=="1"){
                await RunRefineSmokeAsync();return;
            }
            if(Environment.GetEnvironmentVariable("AANDM_SMOKE")=="1"&&Environment.GetEnvironmentVariable("AANDM_CHECKIN_SMOKE")=="1"){
                await RunCheckinSmokeAsync();return;
            }
            if(Environment.GetEnvironmentVariable("AANDM_SMOKE")=="1"&&Environment.GetEnvironmentVariable("AANDM_SCHEDULE_SMOKE")=="1"){
                await RunScheduleSmokeAsync();return;
            }
            if(Environment.GetEnvironmentVariable("AANDM_SMOKE")=="1"&&Environment.GetEnvironmentVariable("AANDM_FOLLOWUP_SMOKE")=="1"){
                await RunFollowupSmokeAsync();return;
            }
            if (Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1" &&
                Environment.GetEnvironmentVariable("AANDM_BOOTSTRAP_FIXTURE_PORT") != null)
            {
                await RunBootstrapFixtureAsync();
                return;
            }
            if(Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE")=="1"){await RunMultiChildFixtureAsync();return;}
            if(Environment.GetEnvironmentVariable("AANDM_BACKGROUND_SMOKE")=="1"){await RunBackgroundSmokeAsync();return;}
            if (Environment.GetEnvironmentVariable("AANDM_PUBLIC_SMOKE") == "1") { await RunPublicStartupSmokeAsync(); return; }
            if (Environment.GetEnvironmentVariable("AANDM_PUBLIC_INPUT_SMOKE") == "1") { await RunPublicInputSmokeAsync(); return; }
            if (Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1") {
                string probe="";
                bool scriptReady=false;
                double minimizeTicksBefore=-1, minimizeTicksAfter=-1, normalRate=-1, minimizedRate=-1;
                string visibilityAfterMinimize="";
                int restoreChecks=0;
                bool restoreImagesStable=false;
                int fastDailySteps=0;
                bool collapseControlsWorking=false;
                bool contractCaptureSaved=false;
                bool profileCookieIsolated=false;
                bool workshopStatusStable=false;
                try {
                    if(webView.CoreWebView2==null)throw new InvalidOperationException("CoreWebView2 is null");
                    var cookieManager=webView.CoreWebView2.CookieManager;
                    cookieManager.AddOrUpdateCookie(cookieManager.CreateCookie("profileIsolation",AppPaths.ProfileId,"isolation.invalid","/"));
                    var cookies=await cookieManager.GetCookiesAsync("https://isolation.invalid/");
                    profileCookieIsolated=cookies.Count==1 && cookies[0].Value==AppPaths.ProfileId;
                    webView.CoreWebView2.NavigateToString("<!doctype html><html><body>Assistant integration check</body></html>");
                    await Task.Delay(700);
                    probe=await ExecuteScriptAsync("(function(){try{" + injectScript + "\nreturn {ok:!!window.__MWA_APP,state:document.readyState};}catch(e){return {error:String(e.stack||e)}}})()");
                    scriptReady=probe!=null && probe.IndexOf("\"ok\":true",StringComparison.Ordinal)>=0;
                    string captureDir=Path.Combine(DataRoot(),"contract-capture-test");
                    Environment.SetEnvironmentVariable("AANDM_CAPTURE_DIR",captureDir);
                    await ExecuteScriptAsync("window.__captureTestAck=0;window.__MWA_CAPTURE_ACK=function(g,id){window.__captureTestAck++;};window.chrome.webview.postMessage({type:'contractStone',generation:'smoke',id:1,amount:1,x:123,y:135});");
                    for(int tries=0;tries<25;tries++){await Task.Delay(100);if(Directory.Exists(captureDir)&&Directory.GetFiles(captureDir,"*.png").Length==1)break;}
                    await ExecuteScriptAsync("window.chrome.webview.postMessage({type:'contractStone',generation:'smoke',id:1,amount:1,x:123,y:135});");
                    await Task.Delay(200);
                    contractCaptureSaved=Directory.Exists(captureDir)&&Directory.GetFiles(captureDir,"*.png").Length==1&&(await ExecuteScriptAsync("window.__captureTestAck")).Trim('"')=="2";
                    // 最小化仍运行：用 requestAnimationFrame 帧率与 visibilityState 实测。
                    await ExecuteScriptAsync("window.__smokeTicks=0;(function f(){window.__smokeTicks++;requestAnimationFrame(f);})()");
                    await Task.Delay(600);
                    double ticksFirst = await ReadSmokeTicksAsync();
                    await Task.Delay(600);
                    minimizeTicksBefore = await ReadSmokeTicksAsync();
                    normalRate = (minimizeTicksBefore - ticksFirst) / 0.6;
                    base.WindowState = FormWindowState.Minimized;
                    await Task.Delay(250);
                    KeepPageRunningIfMinimized();
                    double ticksAtMinimize = await ReadSmokeTicksAsync();
                    await Task.Delay(1500);
                    minimizeTicksAfter = await ReadSmokeTicksAsync();
                    minimizedRate = (minimizeTicksAfter - ticksAtMinimize) / 1.5;
                    visibilityAfterMinimize = await ReadVisibilityAsync();
                    base.WindowState = FormWindowState.Normal;
                    pollTimer.Stop();
                    await Task.Delay(300);
                    ClientSize=new Size(1440,940);
                    foreach(string category in new[]{"tasks","camp"}) {
                        SwitchTab(category);
                        PerformLayout();
                        ActiveControl=webView;
                        await Task.Delay(200);
                        Control sidebar=Controls[0].Controls[0];
                        using(var baseline=new Bitmap(sidebar.Width,sidebar.Height)) {
                            sidebar.DrawToBitmap(baseline,new Rectangle(Point.Empty,baseline.Size));
                            for(int cycle=0;cycle<3;cycle++) {
                                WindowState=FormWindowState.Minimized;
                                await Task.Delay(100);
                                KeepPageRunningIfMinimized();
                                WindowState=FormWindowState.Normal;
                                ActiveControl=webView;
                                await Task.Delay(200);
                                using(var restored=new Bitmap(sidebar.Width,sidebar.Height)) {
                                    sidebar.DrawToBitmap(restored,new Rectangle(Point.Empty,restored.Size));
                                    bool equal=baseline.Size==restored.Size;
                                    for(int y=0;equal && y<baseline.Height;y++)
                                        for(int x=0;x<baseline.Width;x++)
                                            if(baseline.GetPixel(x,y)!=restored.GetPixel(x,y)){equal=false;break;}
                                    if(!equal) {
                                        baseline.Save(Path.Combine(DataRoot(),"restore-failed-"+category+"-baseline.png"));
                                        restored.Save(Path.Combine(DataRoot(),"restore-failed-"+category+"-actual.png"));
                                        throw new InvalidOperationException("恢复后面板像素不一致："+category+" #"+cycle);
                                    }
                                    restoreChecks++;
                                    if(cycle==2) restored.Save(Path.Combine(DataRoot(),"restored-"+category+".png"));
                                }
                            }
                        }
                    }
                    restoreImagesStable=restoreChecks==6 && completedRestoreRepaints>=7;
                    SwitchBottomTab("stats");
                    bool statsShown = panelBottomStats.Visible && !panelBottomStatus.Visible;
                    SwitchBottomTab("log");
                    bool logShown = panelBottomLog.Visible && !panelBottomStats.Visible;
                    SwitchBottomTab("status");
                    collapseControlsWorking = statsShown && logShown && panelBottomStatus.Visible && sidebarLayout.Height <= sidebarScroll.ClientSize.Height;
                    await ExecuteScriptAsync("window.__smokeReady=window.__MWA_APP.ready;window.__smokeRunLua=window.__MWA_APP.runLua;window.__smokeSteps=0;window.__MWA_APP.ready=function(){return true;};window.__MWA_APP.runLua=function(code){if(code.indexOf('daily_step')>=0){window.__smokeSteps++;return {message:'',wait:false};}return null;};");
                    activeTask=AssistantTaskKind.Daily;taskNextAt=DateTime.MinValue;dailyTaskTimer.Start();
                    await Task.Delay(1600);
                    dailyTaskTimer.Stop();activeTask=AssistantTaskKind.None;
                    fastDailySteps=int.Parse((await ExecuteScriptAsync("window.__smokeSteps")).Trim('"'));
                    await ExecuteScriptAsync("window.__MWA_APP.ready=window.__smokeReady;window.__MWA_APP.runLua=window.__smokeRunLua;");
                    RecordWorkshopBlock(new Dictionary<string,object>{
                        {"workshop_block","工坊雷管不足"},{"workshop_detail","深层L2填 3 格需 18 支，保留 2 支，现有 15 支"}
                    });
                    UpdateFromSnapshot(new Dictionary<string,object>{{"ready",true},{"enabled",false},{"status","已暂停"}});
                    workshopStatusStable=lblStatus.Text=="工坊雷管不足" && lblDetail.Text.Contains("现有 15 支");
                    ClearWorkshopBlock();
                    cookies=await cookieManager.GetCookiesAsync("https://isolation.invalid/");
                    profileCookieIsolated=profileCookieIsolated && cookies.Count==1 && cookies[0].Value==AppPaths.ProfileId;
                } catch(Exception error) { probe=error.ToString(); }
                File.WriteAllText(Path.Combine(DataRoot(),"smoke-result.json"),
                    json.Serialize(new { browserReady=webView.CoreWebView2!=null, scriptReady=scriptReady,contractCaptureSaved=contractCaptureSaved,
                        profileId=AppPaths.ProfileId,dataRoot=DataRoot(),profileCookieIsolated=profileCookieIsolated,
                        controllerVisible=ControllerIsVisible(),
                        minimizeTicksBefore=minimizeTicksBefore, minimizeTicksAfter=minimizeTicksAfter,
                        visibilityAfterMinimize=visibilityAfterMinimize,
                        normalRate=normalRate, minimizedRate=minimizedRate,
                        minimizedStillRunning=minimizedRate>=normalRate*0.5 && minimizedRate>1,workshopStatusStable=workshopStatusStable,
                        restoreChecks=restoreChecks,restoreRepaints=completedRestoreRepaints,
                        restoreImagesStable=restoreImagesStable,
                        fastDailySteps=fastDailySteps,fastDailyScheduling=fastDailySteps>=4,
                        collapseControlsWorking=collapseControlsWorking,font=UiTheme.GameFontFamily,
                        logs=txtLog.Text,probe=probe }),Encoding.UTF8);
                Close();
            }
            else if (Environment.GetEnvironmentVariable("AANDM_STATS_TEST") == "1") {
                // 挖矿统计自检（1.16）：不依赖游戏账号，跑增量合并/归零/落盘并写出结果。
                RunMiningStatsSelfTest();
                Close();
            }
            else if (Environment.GetEnvironmentVariable("AANDM_MEASURE") == "1") {
                // 资源占用 A/B 测量：稳定 10 秒后记录本进程与全部 WebView2 子进程的私有内存。
                try {
                    await Task.Delay(10000);
                    string heap = await ExecuteScriptAsync("String(performance.memory?performance.memory.usedJSHeapSize:0)");
                    long ownPrivate = 0, childrenPrivate = 0;
                    int children = 0;
                    using (var process = System.Diagnostics.Process.GetCurrentProcess()) {
                        process.Refresh();
                        ownPrivate = process.PrivateMemorySize64;
                        DateTime started = process.StartTime;
                        foreach (var other in System.Diagnostics.Process.GetProcessesByName("msedgewebview2")) {
                            try {
                                if (other.StartTime < started.AddSeconds(-5)) continue;
                                childrenPrivate += other.PrivateMemorySize64;
                                children++;
                            } catch { }
                        }
                    }
                    File.WriteAllText(Path.Combine(DataRoot(), "measure.json"),
                        json.Serialize(new { baseline=Environment.GetEnvironmentVariable("AANDM_BASELINE_OPTS") == "1",
                            ownPrivateMb=Math.Round(ownPrivate/1048576.0,1),
                            webviewChildren=children,
                            webviewPrivateMb=Math.Round(childrenPrivate/1048576.0,1),
                            totalPrivateMb=Math.Round((ownPrivate+childrenPrivate)/1048576.0,1),
                            jsHeapMb=Math.Round(double.Parse(heap.Trim('"'))/1048576.0,2) }), Encoding.UTF8);
                } catch (Exception error) {
                    File.WriteAllText(Path.Combine(DataRoot(), "measure.json"), json.Serialize(new { error=error.ToString() }), Encoding.UTF8);
                }
                Close();
            }
            else
            {
                StartSilentUpdateCheck();
            }
		}

		private void StartSilentUpdateCheck()
		{
			if (previewMode || Program.ScheduledLaunch || Program.QuietResourceTest || WindowState == FormWindowState.Minimized) return;
			if (Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1"
				|| Environment.GetEnvironmentVariable("AANDM_PUBLIC_SMOKE") == "1"
				|| Environment.GetEnvironmentVariable("AANDM_PUBLIC_INPUT_SMOKE") == "1"
				|| Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE") == "1"
				|| Environment.GetEnvironmentVariable("AANDM_BACKGROUND_SMOKE") == "1")
			{
				return;
			}
			System.Threading.ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					System.Threading.Thread.Sleep(2500);
					if (IsDisposed) return;
					UpdateCheckResult res = UpdateManager.CheckForUpdates(System.Threading.CancellationToken.None);
					if (res != null && res.State == UpdateCheckState.UpdateAvailable && !IsDisposed && IsHandleCreated)
					{
						SafeBeginInvoke(delegate
						{
							AddLog("检测到新版本 v" + res.LatestVersion + "（当前 v" + res.CurrentVersion + "），可在「设置 → 检查更新」按需下载更新。", GoldColor);
						});
					}
				}
				catch { }
			});
		}

		private async Task<double> ReadSmokeTicksAsync()
		{
			string raw = await ExecuteScriptAsync("String(window.__smokeTicks||0)");
			double value;
			return double.TryParse(raw.Trim('"'), out value) ? value : -1;
		}

		private async Task<string> ReadVisibilityAsync()
		{
			string raw = await ExecuteScriptAsync("String(document.visibilityState||'')");
			return raw.Trim('"');
		}

		private async Task InitializeWebViewAsync()
		{
			try
			{
				string userDataFolder = Path.Combine(DataRoot(), "WebView2");
				Directory.CreateDirectory(userDataFolder);
				// Keep the game loop and WebView timers alive while the assistant is
				// minimized or covered by another window. Chromium otherwise applies
				// occlusion and renderer background throttling, which makes the
				// automation appear to stop when it is sent to the background.
				webViewStartedExtremePerf = extremePerformanceMode;
				string browserArgs = "--ignore-certificate-errors --autoplay-policy=no-user-gesture-required" +
					" --disable-backgrounding-occluded-windows --disable-renderer-backgrounding" +
					" --disable-background-timer-throttling";
				// AANDM_BASELINE_OPTS=1 只用于内部 A/B 测量：临时关掉本次的资源优化项。
				bool baselineOptions = Environment.GetEnvironmentVariable("AANDM_BASELINE_OPTS") == "1";
				string disabledFeatures = "CalculateNativeWinOcclusion,IntensiveWakeUpThrottling,TimerThrottling";
				if (!baselineOptions)
				{
					if (webViewStartedExtremePerf)
					{
						browserArgs += " --disk-cache-size=33554432 --media-cache-size=8388608" +
							" --js-flags=--max-old-space-size=192 --optimize-for-size" +
							" --renderer-process-limit=1 --disable-gpu-shader-disk-cache --disable-software-rasterizer";
						disabledFeatures += ",BackForwardCache,BackgroundFetch,OptimizationHints,Translate,MediaRouter,HardwareMediaKeyHandling,PaintHolding,SpellCheck";
					}
					else
					{
						// 资源占用：限制磁盘/媒体缓存，关闭用不到的后台特性与 JS 大对象预留。
						browserArgs += " --disk-cache-size=268435456 --media-cache-size=33554432";
						disabledFeatures += ",BackForwardCache,BackgroundFetch,OptimizationHints,Translate";
					}
				}
				browserArgs += " --disable-features=" + disabledFeatures;
#if TEST_PVP_EDITION
				string extraEnv = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? "";
				if (extraEnv.IndexOf("--remote-debugging-port", StringComparison.OrdinalIgnoreCase) < 0)
				{
					browserArgs += " --remote-debugging-port=19341";
				}
				if (extraEnv.IndexOf("--mute-audio", StringComparison.OrdinalIgnoreCase) < 0)
				{
					browserArgs += " --mute-audio";
				}
#else
				if (Environment.GetEnvironmentVariable("AANDM_DEBUG") == "1")
				{
					browserArgs += " --remote-debugging-port=9444";
				}
#endif
				CoreWebView2EnvironmentOptions environmentOptions = new CoreWebView2EnvironmentOptions(browserArgs);
				CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, environmentOptions);
				await webView.EnsureCoreWebView2Async(environment);
				coreReady = true;
				CoreWebView2 core = webView.CoreWebView2;
#if TEST_PVP_EDITION
				core.ServerCertificateErrorDetected += delegate(object s, CoreWebView2ServerCertificateErrorDetectedEventArgs ev)
				{
					try
					{
						Uri u = new Uri(ev.RequestUri);
						if (string.Equals(u.Host, GameHost, StringComparison.OrdinalIgnoreCase))
						{
							ev.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
						}
					}
					catch { }
				};
#endif
				if (extremePerformanceMode)
				{
					UpdateBrowserMemoryPolicy(core, DateTime.UtcNow, false);
				}
				try
				{
					Directory.CreateDirectory(DataRoot());
					core.SetVirtualHostNameToFolderMapping("aandm-bg.local", DataRoot(), CoreWebView2HostResourceAccessKind.Allow);
				}
				catch { }
                await ResetProfileLoginAsync();
				if (Environment.GetEnvironmentVariable("AANDM_DISABLE_BOOTSTRAP_CACHE") != "1")
				{
					try { bootstrapAssets = new BootstrapAssetCache(core, new Uri(GameUrl), Path.Combine(AppPaths.BaseRoot, "public-bootstrap-cache")); }
					catch (Exception ex) { AddLog("资源分片缓存暂不可用：" + ex.Message, YellowColor); }
				}
                await core.AddScriptToExecuteOnDocumentCreatedAsync(Environment.GetEnvironmentVariable("AANDM_BACKGROUND_BASELINE")=="1"?"window.__MWA_DISABLE_BG_GUARD=true;":backgroundGuardScript);
				ApplyMute();
				// 调试工具只在 AANDM_DEBUG=1 时开启，平时关闭可少一份渲染与内存开销。
				core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("AANDM_DEBUG") == "1";
				core.Settings.AreDefaultContextMenusEnabled = false;
				core.Settings.IsStatusBarEnabled = false;
				core.Settings.IsGeneralAutofillEnabled = false;
				core.Settings.IsPasswordAutosaveEnabled = false;
				core.Settings.AreBrowserAcceleratorKeysEnabled = false;
				core.Settings.IsSwipeNavigationEnabled = false;
				core.Settings.IsPinchZoomEnabled = false;
				core.Settings.IsZoomControlEnabled = false;
				core.Settings.IsBuiltInErrorPageEnabled = false;
				core.NavigationStarting += Core_NavigationStarting;
				core.NavigationCompleted += Core_NavigationCompleted;
				core.DOMContentLoaded += Core_DOMContentLoaded;
				core.NewWindowRequested += Core_NewWindowRequested;
				core.ProcessFailed += Core_ProcessFailed;
                core.WebMessageReceived += ContractCaptureReceived;
                core.WebMessageReceived += MysteryBudgetReceived;
				AddLog("内置浏览器初始化成功，正在打开游戏页面…", BlueColor);
				if (autoLoginEnabled && !string.IsNullOrEmpty(accountUsername) && !string.IsNullOrEmpty(accountPassword))
				{
					AddLog("自动登录已开启（账号 " + MaskedAccount() + "）。", BlueColor);
				}
				btnMain.Enabled = true;
				btnDaily.Enabled = true;
				btnArena.Enabled = true;
				if (btnVoid != null) btnVoid.Enabled = true;
				if (btnMine != null) btnMine.Enabled = true;
				autoLoginEarliestAt = DateTime.Now.AddSeconds(1);
				autoLoginReadyCount = 0;
				core.Navigate((Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1" || Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE")=="1") ? "about:blank" : GameUrl);
				pollTimer.Start();
                dailyTaskTimer.Start();
			}
			catch (Exception ex)
			{
				coreReady=false;
                AddLog("WebView2 初始化失败：" + ex.Message, RedColor);
				SetStatusUi("浏览器初始化失败", RedColor, "请确认系统已安装 Microsoft Edge WebView2 运行时");
				SetBadge("需处理", RedColor);
				if (!Program.ScheduledLaunch && Environment.GetEnvironmentVariable("AANDM_SMOKE") != "1") MessageBox.Show(this, "内置浏览器初始化失败：\r\n" + ex.Message + "\r\n\r\n请安装 Microsoft Edge WebView2 Runtime 后重试。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}

		private void Core_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            ClearWorkshopBlock();
            if (IsTaskRunning(AssistantTaskKind.Daily) && dailyEncounterPhase && !string.IsNullOrEmpty(dailyEncounterRole))
            {
                resumeEncounterOnPage = true;
                taskGeneration++;
                taskBusy = false;
                taskInitializing = false;
                taskNextAt = DateTime.Now.AddSeconds(1);
            }
            else
            {
                StopDaily(); StopArena(); StopVoid(); StopMine(); StopWorkshop(); StopGuild(); StopGrowth(); StopVoidExchange();
            }
			seenLogs.Clear();
			lastSnapshotLogs.Clear();
			adventureLogCursor = 0;
			adventureLogEpoch = 0;
			gameSnapshotReady = false;
			UpdateGamePollInterval();
			pageGeneration++;
			lastAuditSeq = 0;
			lastRunning = false;
			if (!startRequested)
			{
				SetStatusUi("正在加载游戏页面…", YellowColor, "请稍候，内置浏览器正在连接服务器");
				SetBadge("加载中", YellowColor);
				UpdateMainButton(false);
			}
			AddLog(e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? "正在加载内置页面…" : "正在加载游戏页面：" + e.Uri, BlueColor);
		}

		private async void Core_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
		{
			if (!e.IsSuccess)
			{
				AddLog("游戏页面加载失败：" + e.WebErrorStatus, RedColor);
				SetStatusUi("页面加载失败", RedColor, "可点击“重新加载游戏”重试");
				SetBadge("加载失败", RedColor);
				if (IsSelfHealEnabled() && (startRequested || selfHeal.HasPendingResumeMining(DateTime.UtcNow)))
				{
					TryTriggerSelfHeal("页面加载失败：" + e.WebErrorStatus, startRequested || selfHeal.HasPendingResumeMining(DateTime.UtcNow));
				}
			}
			else
			{
				AddLog("游戏页面加载完成，正在注入自动助手…", BlueColor);
				autoLoginEarliestAt = DateTime.Now.AddSeconds(1);
				autoLoginReadyCount = 0;
				await InjectAsync();
				await ApplyStageWallpaperAsync();
				await PollOnce();
			}
		}

		private async void Core_DOMContentLoaded(object sender, CoreWebView2DOMContentLoadedEventArgs e)
		{
			await InjectAsync();
			await ApplyStageWallpaperAsync();
		}

		private void Core_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
		{
			e.Handled = true;
			Uri result;
			if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out result))
			{
				return;
			}
			if (string.Equals(result.Host, GameHost, StringComparison.OrdinalIgnoreCase))
			{
				webView.CoreWebView2.Navigate(e.Uri);
			}
			else if (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps)
			{
				try
				{
					Process.Start(e.Uri);
				}
				catch
				{
				}
			}
		}

		private bool IsSelfHealEnabled()
		{
			if (previewMode || Program.QuietResourceTest || Environment.GetEnvironmentVariable("AANDM_SMOKE") == "1" || Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE") == "1") return false;
			bool enabled;
			return !options.TryGetValue("autoSelfHeal", out enabled) || enabled;
		}

		private void TryTriggerSelfHeal(string reason, bool resumeMining)
		{
			if (!IsSelfHealEnabled() || !coreReady || selfHeal.ReloadScheduled || IsDisposed) return;
			SelfHealDecision decision = selfHeal.TryConsumeRecoveryQuota(DateTime.UtcNow);
			if (!decision.Allowed)
			{
				AddLog("挂机自愈未触发（" + reason + "）：" + decision.Reason, YellowColor);
				return;
			}
			if (resumeMining) selfHeal.MarkPendingResumeMining(DateTime.UtcNow);
			selfHeal.ReloadScheduled = true;
			AddLog("触发挂机自愈（" + reason + " · " + decision.AttemptInWindow + "/" + decision.MaxInWindow + "）：3 秒后自动重载游戏页面" + (resumeMining ? "并续跑挖矿" : "") + "…", GoldColor);
			SetStatusUi("挂机自愈重载中…", YellowColor, "3 秒后自动重载恢复");
			SetBadge("自愈中", YellowColor);
			if (selfHealTimer != null)
			{
				try { selfHealTimer.Stop(); selfHealTimer.Dispose(); } catch { }
				selfHealTimer = null;
			}
			selfHealTimer = new Timer { Interval = 3000 };
			selfHealTimer.Tick += delegate
			{
				if (selfHealTimer != null)
				{
					try { selfHealTimer.Stop(); selfHealTimer.Dispose(); } catch { }
					selfHealTimer = null;
				}
				selfHeal.ReloadScheduled = false;
				if (IsDisposed || !coreReady || webView == null || webView.CoreWebView2 == null) return;
				try { webView.CoreWebView2.Reload(); }
				catch (Exception ex) { AddLog("挂机自愈重载失败：" + ex.Message, RedColor); }
			};
			selfHealTimer.Start();
		}

		private void Core_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            bool wasMining = startRequested || lastRunning;
            ResetAllTaskStates();
            startRequested=lastRunning=false;RefreshTaskControls();
			AddLog("游戏渲染进程异常：" + e.ProcessFailedKind, RedColor);
			if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
			{
				coreReady = false;
				selfHeal.ClearPendingResume();
				SetStatusUi("浏览器主进程退出", RedColor, "请关闭并重新打开助手窗口");
				SetBadge("需重启", RedColor);
				AddLog("WebView2 浏览器主进程已异常退出，无法原地重载，请重启助手。", RedColor);
				return;
			}
			SetStatusUi("游戏进程异常", RedColor, "请点击“重新加载游戏”恢复");
			SetBadge("需处理", RedColor);
			TryTriggerSelfHeal("渲染进程异常 " + e.ProcessFailedKind, wasMining);
		}

		private async Task InjectAsync()
		{
			if (Environment.GetEnvironmentVariable("AANDM_BACKGROUND_SMOKE") == "1" || Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE") == "1") return;
			int generation = pageGeneration;
			if (!coreReady || injectScript == null || injectingPage == generation || injectedPage == generation)
			{
				return;
			}
			injectingPage = generation;
			try
			{
				await webView.CoreWebView2.ExecuteScriptAsync(injectScript);
				if (generation != pageGeneration || IsDisposed) return;
				await ApplyOptionsAsync();
				if (generation == pageGeneration && !IsDisposed)
				{
					injectedPage = generation;
					AddLog("自动助手脚本注入成功。", BlueColor);
					if (extremePerformanceMode) KeepPageRunning();
				}
			}
			catch (Exception ex)
			{
				AddLog("自动助手注入失败：" + ex.Message, RedColor);
			}
			finally
			{
				if (injectingPage == generation) injectingPage = -1;
			}
		}

		private async void PollTimer_Tick(object sender, EventArgs e)
        {
            if (pollCycleBusy || IsDisposed) return;
            pollCycleBusy = true;
            try {
                // 最小化时每轮复核页面可见，防止浏览器把渲染/计时挂起。
                KeepPageRunningIfMinimized();
                await PollOnce();
                await TryAutoLoginAsync();
            } finally { pollCycleBusy = false; UpdateGamePollInterval(); }
        }

		private void UpdateGamePollInterval()
		{
			if (pollTimer == null || IsDisposed) return;
			if (extremePerformanceMode)
			{
				bool active = AnyTaskRunning() || autoLoginReconnecting || !gameSnapshotReady || (autoLoginEnabled && !autoLoginDone);
				bool backgroundOrMin = IsMinimizedOrHostMinimized() || quietGamePresentation || backgroundSinceUtc != DateTime.MinValue;
				int extremeInterval = active ? 2000 : (backgroundOrMin ? 8000 : 4000);
				if (pollTimer.Interval != extremeInterval) pollTimer.Interval = extremeInterval;
				return;
			}
			bool idle = gameSnapshotReady && !AnyTaskRunning() && !autoLoginEnabled;
			int interval = idle ? (quietGamePresentation ? 5000 : IsMinimizedOrHostMinimized() ? 3000 : 2000) : 1000;
			if (pollTimer.Interval != interval) pollTimer.Interval = interval;
		}

		private async Task PollOnce()
		{
			if (!coreReady || polling || webView.CoreWebView2.IsSuspended)
			{
				return;
			}
			polling = true;
			int generation = pageGeneration;
			try
			{
				string result = await webView.CoreWebView2.ExecuteScriptAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture,
					"window.__MWA_APP ? window.__MWA_APP.snapshot({0},{1}) : null", adventureLogCursor, adventureLogEpoch));
				if (generation != pageGeneration || IsDisposed) return;
				if (string.IsNullOrEmpty(result) || result == "null")
				{
					gameSnapshotReady = false;
					UpdateNotReady();
					return;
				}
				Dictionary<string, object> snapshot = json.Deserialize<Dictionary<string, object>>(result);
				selfHeal.RecordPollSuccess();
				gameSnapshotReady = GetBool(snapshot, "ready");
				UpdateFromSnapshot(snapshot);
				if (gameSnapshotReady)
				{
					await SyncSafetyStateAsync(snapshot, generation);
					if (selfHeal.ConsumePendingResumeMining(DateTime.UtcNow) && !lastRunning && !startRequested && !AnyTaskRunning())
					{
						AddLog("挂机自愈完成：游戏页面已恢复就绪，自动续跑挖矿。", GreenColor);
						BtnMain_Click(this, EventArgs.Empty);
					}
				}
			}
			catch
			{
				if (coreReady && IsSelfHealEnabled() && (lastRunning || startRequested || AnyTaskRunning()))
				{
					int errs = selfHeal.RecordPollError();
					if (errs >= SelfHealWatchdog.PollErrorReloadThreshold)
					{
						bool wasMining = lastRunning || startRequested;
						TryTriggerSelfHeal("连续 " + errs + " 次脚本轮询无响应", wasMining);
					}
				}
			}
			finally
			{
				polling = false;
			}
		}

		private void UpdateNotReady()
		{
			if (startRequested)
			{
				SetStatusUi("正在启动自动挖矿…", YellowColor, "等待游戏客户端加载");
				SetBadge("启动中", YellowColor);
				UpdateMainButton(false);
			}
			else
			{
				SetStatusUi("等待加载", GoldColor, "登录后选择任务");
				SetBadge("加载中", YellowColor);
				UpdateMainButton(false);
			}
			cachedMiningText = "矿镐 --/-- · 巨魔 Lv-- · 发现 -- · 深度 --";
			if (lblMining != null) lblMining.Text = cachedMiningText;
			if (lblMinePlan != null) lblMinePlan.Text = "推荐矿层：等待游戏数据";
			if (lblMineReason != null) lblMineReason.Text = "进入游戏后读取六座修炼建筑的下一次升级材料。";
			if (lblMineSlots != null) lblMineSlots.Text = "矿队：等待矿山数据";
			UpdateStats(null);
			UpdateLayoutDynamicViews(null);
		}

		private void UpdateFromSnapshot(Dictionary<string, object> snapshot)
		{
			if (!GetBool(snapshot, "ready"))
			{
				UpdateNotReady();
				return;
			}
			bool flag = GetBool(snapshot, "enabled");
			string text = GetString(snapshot, "status");
			string text2 = GetString(snapshot, "detail");
			if (string.IsNullOrEmpty(text))
			{
				text = (flag ? "运行中" : "已暂停");
			}
			if (startRequested && !flag)
			{
				text = "正在启动自动挖矿…";
				text2 = "等待游戏客户端进入可操作状态";
			}
			lastRunning = flag;
			Color logicalStatusColor = (text.IndexOf("错误", StringComparison.Ordinal) >= 0 || text.IndexOf("失败", StringComparison.Ordinal) >= 0 || text.IndexOf("超时", StringComparison.Ordinal) >= 0)
				? RedColor
				: (flag ? GreenColor : GoldColor);
			SetStatusUi(text, logicalStatusColor, text2);
            UpdateMiningReview(snapshot);
			Dictionary<string, object> dictionary = GetDictionary(snapshot, "mining");
			if (dictionary == null)
			{
				cachedMiningText = "矿镐 --/-- · 巨魔 Lv-- · 发现 -- · 深度 --";
			}
			else
			{
				cachedMiningText = "矿镐 " + GetInt(dictionary, "digCount") + "/" + GetInt(dictionary, "digMax") + " · 巨魔 Lv" + GetInt(dictionary, "golemLv") + " · 发现 " + GetInt(dictionary, "golemNum") + " · 深度 " + GetInt(dictionary, "depth");
			}
			if (lblMining != null) lblMining.Text = cachedMiningText;
			Dictionary<string, object> mine = GetDictionary(snapshot, "mine");
			if (mine != null)
			{
				string minePlan = GetString(mine, "plan");
				string mineReason = GetString(mine, "reason");
				string mineSlots = GetString(mine, "slots");
				if (lblMinePlan != null && !string.IsNullOrEmpty(minePlan)) {lblMinePlan.Text = minePlan;hints.SetToolTip(lblMinePlan,minePlan);}
				if (lblMineReason != null && !string.IsNullOrEmpty(mineReason)) {lblMineReason.Text = mineReason;hints.SetToolTip(lblMineReason,mineReason);}
				if (lblMineSlots != null && !string.IsNullOrEmpty(mineSlots)) lblMineSlots.Text = mineSlots;
			}
			UpdateStats(GetDictionary(snapshot, "stats"));
			ApplyMiningStats(GetDictionary(snapshot, "miningStats"));
			UpdateLogs(snapshot);
			UpdateLayoutDynamicViews(snapshot);
			if (startRequested)
			{
				SetBadge("启动中", YellowColor);
			}
			else if (flag && text.IndexOf("登录", StringComparison.Ordinal) >= 0)
			{
				SetBadge("等待登录", YellowColor);
			}
			else if (flag)
			{
				SetBadge("运行中", GreenColor);
			}
			else
			{
				SetBadge("已暂停", MutedColor);
			}
            if (HasActiveTask) {
                string taskStatus;
                Color taskColor;
                string badge;
                GetActiveTaskDisplay(out taskStatus, out taskColor, out badge);
                SetStatusUi(taskStatus, taskColor, lastTaskMessage);
                SetBadge(badge,GreenColor);
            }
            ShowWorkshopBlock();
            int week = GetInt(snapshot,"guideWeek");
            if (lblVoidHint != null && week>=1 && week<=7) lblVoidHint.Text = "今日 · " + new[]{"周一","周二","周三","周四","周五","周六","周日"}[week-1] + "攻略";
            UpdateMainButton(flag);
        }

		private void UpdateLogs(Dictionary<string, object> snapshot)
		{
			if (snapshot.ContainsKey("logSeq") && snapshot.ContainsKey("logEpoch"))
			{
				object[] recent = GetArray(snapshot, "logs");
				if (recent != null && recent.Length != 0)
				{
					var entries = new List<string>(recent.Length);
					for (int i = recent.Length - 1; i >= 0; i--)
					{
						string text = Convert.ToString(recent[i]);
						if (!string.IsNullOrEmpty(text)) entries.Add("[自动] " + text);
					}
					AppendAdventureLogs(entries, AutoLogColor);
				}
				adventureLogCursor = GetInt(snapshot, "logSeq");
				adventureLogEpoch = GetInt(snapshot, "logEpoch");
				return;
			}
			object[] array = GetArray(snapshot, "allLogs");
			if (array == null)
			{
				array = GetArray(snapshot, "logs");
			}
			if (array == null)
			{
				return;
			}
			lastSnapshotLogs.Clear();
			for (int num = array.Length - 1; num >= 0; num--)
			{
				string text = ((array[num] == null) ? "" : Convert.ToString(array[num]));
				if (!string.IsNullOrEmpty(text))
				{
					lastSnapshotLogs.Add(text);
					if (!seenLogs.Contains(text))
					{
						seenLogs.Add(text);
						AddLog("[自动] " + text, AutoLogColor);
					}
				}
			}
			if (seenLogs.Count > 500)
			{
				seenLogs.Clear();
				foreach (string item in lastSnapshotLogs) seenLogs.Add(item);
			}
		}

		private void UpdateStats(Dictionary<string, object> stats, bool useCached = false)
		{
			string[] array = new string[6] { "digs", "golems", "bosses", "wins", "losses", "chests" };
			string[] array2 = new string[6] { "挖矿", "魔像", "BOSS", "胜利", "失败", "宝箱" };
			for (int i = 0; i < array.Length; i++)
			{
				int num;
				if (stats != null)
				{
					num = GetInt(stats, array[i]);
					cachedStats[array[i]] = num;
				}
				else if (useCached)
				{
					cachedStats.TryGetValue(array[i], out num);
				}
				else
				{
					num = 0;
					cachedStats[array[i]] = 0;
				}
				Label value;
				if (statLabels.TryGetValue(array[i], out value))
				{
					value.Text = array2[i] + " " + num;
				}
				Label tileVal;
				if (statValueLabels.TryGetValue(array[i], out tileVal))
				{
					tileVal.Text = num.ToString();
				}
			}
		}

		private void UpdateMainButton(bool running)
		{
			if (btnMain != null)
			{
				if (startRequested)
				{
					btnMain.Text = "取消启动";
					btnMain.Skin = GameButtonSkin.Gold;
				}
				else if (running || dailyEncounterPhase)
				{
					btnMain.Text = "Ⅱ  暂停挖矿";
					btnMain.Skin = GameButtonSkin.Orange;
				}
				else
				{
					btnMain.Text = "▶  开始挖矿";
					btnMain.Skin = GameButtonSkin.Green;
				}
				btnMain.Invalidate();
			}
		}

		private void SetBadge(string text, Color color)
		{
			cachedBadgeText = text ?? "";
			cachedBadgeColor = color;
			if (lblBadge != null)
			{
				lblBadge.Text = cachedBadgeText;
				lblBadge.ForeColor = color;
			}
		}

        private bool escortFollowupRunning,mineFollowupRunning,dailyRetryRunning;
        protected override bool ShowWithoutActivation {get{return Program.QuietResourceTest||(Program.ScheduledLaunch&&WindowState==FormWindowState.Minimized);}}
		private async void BtnDaily_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Daily))
            {
                if(escortFollowupRunning)CancelHarvest("escort");else CancelScheduledQueue();
                int generation=++taskGeneration;
                taskBusy=true;
                try{
                    if(!escortFollowupRunning&&coreReady){
                        string raw=await ExecuteLuaAsync("local report=__MWA:daily_report(true); __MWA:cancel_task(); return report");
                        if(generation==taskGeneration&&raw!="null")RecordDailyReview(json.DeserializeObject(raw),dailyRetryRunning);
                    }
                }catch(Exception ex){AddLog("日常结果暂未保存 · "+ex.Message,YellowColor);}
                if(generation==taskGeneration){StopDaily();AddLog("已停止当前日常任务。",YellowColor);}
                return;
            }
            await StartDailyAsync(false);
        }

        private async Task StartDailyAsync(bool escortFollowup,string replay=null,bool scheduledCheckin=false,bool refineFollowup=false)
        {
            if (!CanStartTask()) return;
            ClearWorkshopBlock();
            checkinDaily=scheduledCheckin;
            refineDaily=refineFollowup;
            escortFollowupRunning=escortFollowup;
            dailyRetryRunning=replay!=null;
            dailyTaskTimer.Start();
            int generation=BeginTask(AssistantTaskKind.Daily);
			if(!escortFollowup&&!refineFollowup)AddLog("开始一键日常：自动执行可完成任务并领取奖励；副本买满打完，工会首领许可证买满并挑战1次；领取邮件、金币召唤4次；合战与宿命远征当日开启时买满次数并推进（血钻按实际余额执行，预算不限）", BlueColor);
            try
            {
                if(lastRunning || startRequested) await StopAutomationAsync();
                if(generation!=taskGeneration) return;
                if(replay!=null){
                    string accepted=await ExecuteLuaAsync("return "+replay);
                    if(generation!=taskGeneration)return;
                    taskInitializing=false;
                    if(accepted!="true"){AddLog("请进入当日结果对应的账号后补办。",YellowColor);StopDaily();return;}
                }else if (!await InitializeTaskAsync(escortFollowup ? "__MWA:start_escort_followup(); return true" :
                    refineFollowup ? "__MWA:start_daily(true,false,true); return true" :
                    scheduledCheckin ? "__MWA:start_daily(true,true); return true" : "__MWA:start_daily(true); return true")) return;
                if (!escortFollowup && !refineFollowup)
                {
                    taskInitializing = true;
                    await SyncGuildBossDailyAsync();
                    if (generation != taskGeneration) return;
                    await SyncEncounterClearDaysAsync();
                    if (generation != taskGeneration) return;
                    taskInitializing = false;
                }
                if (generation != taskGeneration) return;
				await TryDailyStepAsync();
			}
			catch (Exception ex)
			{
                if(generation!=taskGeneration) return;
				AddLog((escortFollowup?"矿车续跑失败：":"一键日常失败：") + ex.Message, RedColor);
				if(!escortFollowup)CancelScheduledQueue(); StopDaily();
			}
		}

		private async Task TryDailyStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.Daily) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
            int generation = taskGeneration;
			try
			{
                if (resumeEncounterOnPage)
                {
                    if (injectedPage != pageGeneration)
                    {
                        taskNextAt = DateTime.Now.AddSeconds(1);
                        return;
                    }
                    await SyncEncounterClearDaysAsync();
                    if (generation != taskGeneration) return;
                    string resumed = await ExecuteLuaAsync("return __MWA:start_daily_encounter_resume(" + LuaStringLiteral(dailyEncounterRole) + ")");
                    if (generation != taskGeneration) return;
                    if (resumed != "true")
                    {
                        taskNextAt = DateTime.Now.AddSeconds(1);
                        return;
                    }
                    resumeEncounterOnPage = false;
                }
				string resultJson = await ExecuteLuaAsync(escortFollowupRunning
                    ? "return __MWA:harvest_result(__MWA:escort_followup_step())"
                    : "return __MWA:harvest_result(__MWA:daily_step())");
				if (generation != taskGeneration) return;
                if (string.IsNullOrEmpty(resultJson) || resultJson == "null")
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				Dictionary<string, object> result = json.Deserialize<Dictionary<string, object>>(resultJson);
				if (result == null)
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				string message = GetString(result, "message");
                string phase = GetString(result, "phase");
                if (phase == "mine") dailyMinePhase = true;
                if (phase == "encounter")
                {
                    string role = GetString(result, "encounterRole");
                    if (!string.IsNullOrEmpty(role)) dailyEncounterRole = role;
                    if (!dailyEncounterPhase)
                    {
                        dailyEncounterPhase = true;
                        RefreshTaskControls();
                        UpdateMainButton(false);
                    }
                }
                    ReportTaskMessage(TaskWaitText(message,GetBool(result,"wait"),result),GetBool(result,"wait"));
				RecordWorkshopBlock(result);
                RecordHarvestUpdates(result);
                RecordGuildBossDone(result);
                RecordEncounterClearDone(result);
                if(GetBool(result,"refineTap"))await SendRefineTapAsync(result,generation);
                await HandleRuneChoiceAsync(result,generation);
                if(generation!=taskGeneration)return;
				if (GetBool(result, "done"))
				{
                    object report;
                    if(result.TryGetValue("dailyReport",out report))RecordDailyReview(report,dailyRetryRunning);
                    if(escortFollowupRunning)RecordTaskReview("矿车续跑",message);
                    else if(result.TryGetValue("cartReview",out report))RecordTaskReview("矿车运送",Convert.ToString(report));
					AddLog(message, GetBool(result,"error") ? RedColor : (GetBool(result,"partial") ? YellowColor : GreenColor));
					if(!escortFollowupRunning&&!GetBool(result,"refineProbeOnly"))NotifyTaskDone("一键日常", message);
					StopDaily();
					return;
				}
				if (!string.IsNullOrEmpty(message) && !GetBool(result, "wait"))
				{
					AddLog(message, BlueColor);
				}
				else if (GetBool(result, "wait") && !taskWaitLogged)
				{
					taskWaitLogged = true;
					AddLog(string.IsNullOrEmpty(message) ? "等待日常数据…" : message, YellowColor);
				}
                taskNextAt=NextTaskAt(result);
			}
			catch (Exception ex)
			{
                if (generation != taskGeneration) return;
				AddLog((escortFollowupRunning?"矿车续跑失败：":"一键日常失败：") + ex.Message, RedColor);
				StopDaily();
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopDaily()
		{
            if(checkinDaily){checkinFinished=true;checkinDaily=false;nextScheduleProbeUtc=DateTime.MinValue;}
            if(refineDaily){refineFinished=true;refineDaily=false;nextScheduleProbeUtc=DateTime.MinValue;}
            escortFollowupRunning=false;
            dailyRetryRunning=false;
            if(runePrompt!=null)runePrompt.Close();
            dailyMinePhase=false;
            dailyEncounterPhase=false;
            dailyEncounterRole=null;
            resumeEncounterOnPage=false;
            UpdateMainButton(lastRunning);
            EndTask(AssistantTaskKind.Daily);
			ShowWorkshopBlock();
		}

		private async void BtnMine_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Mine))
			{
				CancelScheduledQueue(); CancelHarvest("mine"); StopMine();
				AddLog("已停止一键矿山采集。", YellowColor);
				return;
			}
            await StartMineAsync(false);
        }

        private async Task StartMineAsync(bool followup)
        {
			if (!CanStartTask("已有其他自动任务在运行，请稍候。")) return;
            mineFollowupRunning=followup;
            int generation=BeginTask(AssistantTaskKind.Mine);
			AddLog("开始一键矿山采集：成熟领奖后核对库存、升级研究，再按新材料缺口选择矿层", BlueColor);
            try
            {
                if(lastRunning || startRequested) await StopAutomationAsync();
                if(generation!=taskGeneration) return;
				if (!await InitializeTaskAsync(followup ? "__MWA:start_mine_collect(nil,true); return true" : "__MWA:start_mine_collect(); return true")) return;
				await TryMineStepAsync();
			}
			catch (Exception ex)
			{
                if(generation!=taskGeneration) return;
				AddLog("一键矿山采集失败：" + ex.Message, RedColor);
				if(!followup)CancelScheduledQueue(); StopMine();
			}
		}

		private async Task TryMineStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.Mine) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
			int generation = taskGeneration;
			try
			{
				string resultJson = await ExecuteLuaAsync("return __MWA:harvest_result(__MWA:mine_step())");
				if (generation != taskGeneration) return;
				if (string.IsNullOrEmpty(resultJson) || resultJson == "null")
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				Dictionary<string, object> result = json.Deserialize<Dictionary<string, object>>(resultJson);
				if (result == null)
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				string message = GetString(result, "message");
				ReportTaskMessage(message, GetBool(result, "wait"));
                RecordHarvestUpdates(result);
				if (GetBool(result, "done"))
				{
					AddLog(message, GetBool(result, "error") ? RedColor : (GetBool(result, "partial") ? YellowColor : GreenColor));
					if(!mineFollowupRunning)NotifyTaskDone("矿山采集", message);
					StopMine();
					return;
				}
				if (!string.IsNullOrEmpty(message) && !GetBool(result, "wait"))
				{
					AddLog(message, BlueColor);
				}
				else if (GetBool(result, "wait") && !taskWaitLogged)
				{
					taskWaitLogged = true;
					AddLog(string.IsNullOrEmpty(message) ? "等待矿山数据…" : message, YellowColor);
				}
				taskNextAt = NextTaskAt(result);
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("一键矿山采集失败：" + ex.Message, RedColor);
				StopMine();
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopMine()
		{
			EndTask(AssistantTaskKind.Mine);
		}

		private async void BtnArena_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Arena))
			{
				CancelScheduledQueue(); StopArena();
				AddLog("已停止自动竞技场。", YellowColor);
				return;
			}
			if (!CanStartTask("已有其他自动任务在运行，请稍候。")) return;
			int generation = BeginTask(AssistantTaskKind.Arena);
			arenaTarget = numArenaCount != null
				? Math.Max(1, Math.Min(99, (int)numArenaCount.Value))
				: arenaTarget;
			AddLog("开始自动竞技场，共 " + arenaTarget + " 轮，每轮固定依次挑战九名对手；刷新策略：" + (options["arenaAllowBloodRefresh"] ? "轮次间允许血钻刷新，费用使用游戏当前值" : "只用免费刷新，到时自动继续") + "", BlueColor);
            try
            {
                if(lastRunning || startRequested) await StopAutomationAsync();
                if(generation!=taskGeneration) return;
                if (!await InitializeTaskAsync("__MWA:start_arena(" + arenaTarget + "); return true")) return;
				await TryArenaStepAsync();
			}
			catch (Exception ex)
			{
                if(generation!=taskGeneration) return;
				AddLog("竞技场启动失败：" + ex.Message, RedColor);
				CancelScheduledQueue(); StopArena();
			}
		}

		private async Task TryArenaStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.Arena) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
            int generation = taskGeneration;
			try
			{
				
				try
				{
					string resultJson = await ExecuteLuaAsync("return __MWA:arena_step()");
					if (generation != taskGeneration) return;
                if (string.IsNullOrEmpty(resultJson) || resultJson == "null")
					{
						taskNextAt = DateTime.Now.AddSeconds(2.0);
						return;
					}
					Dictionary<string, object> result = json.Deserialize<Dictionary<string, object>>(resultJson);
					if (result == null)
					{
						taskNextAt = DateTime.Now.AddSeconds(2.0);
						return;
					}
					string message = GetString(result, "message");
                    ReportTaskMessage(message,GetBool(result,"wait"));
					if (GetBool(result, "done"))
					{
						NotifyTaskDone("竞技场", message);
AddLog(message, GetBool(result,"error") ? RedColor : (GetBool(result,"partial") ? YellowColor : GreenColor));
						StopArena();
						return;
					}
					if (!string.IsNullOrEmpty(message) && !GetBool(result, "wait"))
					{
						AddLog(message, BlueColor);
					}
					taskNextAt = NextTaskAt(result);
				}
				catch (Exception ex)
				{
                    if (generation != taskGeneration) return;
					AddLog("竞技场运行失败：" + ex.Message, RedColor);
					StopArena();
				}
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopArena()
		{
			EndTask(AssistantTaskKind.Arena);
		}

		private async void BtnVoid_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Void))
			{
				CancelScheduledQueue(); StopVoid();
				AddLog("已停止自动虚空裂隙。", YellowColor);
				return;
			}
			if (!CanStartTask("已有其他自动任务在运行，请稍候。")) return;
			int generation = BeginTask(AssistantTaskKind.Void);
			AddLog("开始虚空：整轮评估（含符文与出战次数），不足则逐次血钻招募，延后倒数第二关领奖并核验末关重打。", BlueColor);
            try
            {
                if(lastRunning || startRequested) await StopAutomationAsync();
                if(generation!=taskGeneration) return;
                if (!await InitializeTaskAsync("__MWA:start_void(); return true")) return;
				await TryVoidStepAsync();
			}
			catch (Exception ex)
			{
                if(generation!=taskGeneration) return;
				AddLog("虚空裂隙启动失败：" + ex.Message, RedColor);
				CancelScheduledQueue(); StopVoid();
			}
		}

		private async Task TryVoidStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.Void) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
            int generation = taskGeneration;
			try
			{
				
				try
				{
					string resultJson = await ExecuteLuaAsync("return __MWA:void_step()");
					if (generation != taskGeneration) return;
                if (string.IsNullOrEmpty(resultJson) || resultJson == "null")
					{
						taskNextAt = DateTime.Now.AddSeconds(2.0);
						return;
					}
					Dictionary<string, object> result = json.Deserialize<Dictionary<string, object>>(resultJson);
					if (result == null)
					{
						taskNextAt = DateTime.Now.AddSeconds(2.0);
						return;
					}
					string message = GetString(result, "message");
                    ReportTaskMessage(message,GetBool(result,"wait"));
					if (GetBool(result, "done"))
					{
						NotifyTaskDone("虚空", message);
AddLog(message, GetBool(result,"error") ? RedColor : (GetBool(result,"partial") ? YellowColor : GreenColor));
						StopVoid();
						return;
					}
					if (!string.IsNullOrEmpty(message) && !GetBool(result, "wait"))
					{
						AddLog(message, BlueColor);
					}
					taskNextAt = NextTaskAt(result);
				}
				catch (Exception ex)
				{
                    if (generation != taskGeneration) return;
					AddLog("虚空裂隙运行失败：" + ex.Message, RedColor);
					StopVoid();
				}
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopVoid()
		{
			EndTask(AssistantTaskKind.Void);
		}

		private async void BtnMain_Click(object sender, EventArgs e)
		{
            if (IsTaskRunning(AssistantTaskKind.Daily) && dailyEncounterPhase)
            {
                BtnDaily_Click(sender, e);
                return;
            }
            if (HasActiveTask) { AddLog("请先停止当前任务，再启动挖矿。",YellowColor); return; }
			if (startRequested)
			{
				startRequested = false;
				AddLog("已取消启动等待。", YellowColor);
				await StopAutomationAsync();
			}
			else if (lastRunning)
			{
				await StopAutomationAsync();
			}
			else if (!coreReady)
			{
				AddLog("内置浏览器尚未加载完成，请稍候。", YellowColor);
			}
			else
			{
				startRequested = true;
				AddLog("正在启动自动挖矿…", BlueColor);
				UpdateMainButton(false);
				SetBadge("启动中", YellowColor);
				await StartLoopAsync();
			}
		}

		private async Task StartLoopAsync()
		{
			if (startLoopRunning)
			{
				return;
			}
			startLoopRunning = true;
            int generation=taskGeneration;
			try
			{
				for (int attempt = 0; attempt < 90 && startRequested; attempt++)
				{
					try
					{
						if (!coreReady)
						{
							await Task.Delay(1000);
							continue;
						}
						await InjectAsync();
						if(!startRequested || generation!=taskGeneration || IsDisposed)return;
						string result = await webView.CoreWebView2.ExecuteScriptAsync("__MWA_APP.start()");
						if (result != null && result.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0)
						{
							startRequested = false;
							AddLog("自动挖矿已开启（登录后将自动进入矿区）", GreenColor);
							await PollOnce();
							UpdateMainButton(lastRunning);
							return;
						}
					}
					catch
					{
					}
					if (attempt == 0)
					{
						AddLog("等待游戏客户端加载完成…", YellowColor);
					}
					else if (attempt > 0 && attempt % 10 == 0)
					{
						AddLog("仍在等待游戏客户端（" + attempt + " 秒）…", YellowColor);
					}
					await Task.Delay(1000);
				}
				if (startRequested)
				{
					startRequested = false;
					AddLog("启动等待超时：请确认游戏页面已加载，必要时重新加载游戏。", RedColor);
					SetBadge("需处理", RedColor);
				}
			}
			finally
			{
				startLoopRunning = false;
				UpdateMainButton(lastRunning);
			}
		}

		private async Task ApplyOptionsAsync()
		{
			if (!coreReady || IsDisposed) return;
			var values = new Dictionary<string, object>(options.Count + numberOptions.Count);
			foreach (var pair in options) values[pair.Key] = pair.Value;
			foreach (var pair in numberOptions) values[pair.Key] = pair.Value;
			try {
				await ExecuteScriptAsync("window.__MWA_APP && (__MWA_APP.setOptions(" + json.Serialize(values)
					+ "),__MWA_APP.setMysteryProducts&&__MWA_APP.setMysteryProducts(" + json.Serialize(mysteryProducts) + "))");
			}
			catch (InvalidOperationException) { }
			KeepPageRunning();
			await SyncGuildBossDailyAsync();
			ApplyMute();
			await ApplyStageWallpaperAsync();
		}

		internal async Task ApplyStageWallpaperAsync(string liveDataUri = null)
		{
			if (!coreReady || IsDisposed || previewMode || webView == null || webView.CoreWebView2 == null) return;
			if (extremePerformanceMode)
			{
				webView.DefaultBackgroundColor = BgColor;
				string clearJs = "(function(){try{window.__aandmStageCfg={active:false};var oldBg=document.getElementById('__aandm_stage_bg');var oldSt=document.getElementById('__aandm_stage_bg_style');if(oldBg)oldBg.remove();if(oldSt)oldSt.remove();}catch(e){}})();";
				try { await ExecuteScriptAsync(clearJs); }
				catch { }
				return;
			}
			bool nativeBackdrop = UseNativeStageBackdrop;
			webView.DefaultBackgroundColor = nativeBackdrop ? Color.Transparent : BgColor;
			bool stageEnabled = !options.ContainsKey("stageWallpaperBg") || options["stageWallpaperBg"];
			bool hasCustomFile = !string.IsNullOrEmpty(customBgPath) && File.Exists(customBgPath);
			bool hasLive = !string.IsNullOrEmpty(liveDataUri);
			bool glassFallback = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;

			string url = "";
			if (stageEnabled && !nativeBackdrop)
			{
				if (hasLive)
				{
					url = liveDataUri;
				}
				else if (UiTheme.CustomBackgroundMode == UiTheme.BgModeAnimated && File.Exists(Path.Combine(DataRoot(), "custom-bg.gif")))
				{
					url = "https://aandm-bg.local/custom-bg.gif?v=" + DateTime.UtcNow.Ticks;
				}
				else if (hasCustomFile)
				{
					string cropFile = Path.Combine(DataRoot(), "custom-bg-crop.png");
					string pngInRoot = Path.Combine(DataRoot(), "custom-bg.png");
					if (UiTheme.HasCustomCrop && File.Exists(cropFile)) url = "https://aandm-bg.local/custom-bg-crop.png?v=" + DateTime.UtcNow.Ticks;
					else if (string.Equals(customBgPath, pngInRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(pngInRoot)) url = "https://aandm-bg.local/custom-bg.png?v=" + DateTime.UtcNow.Ticks;
					else url = "https://aandm-bg.local/" + Uri.EscapeDataString(Path.GetFileName(customBgPath)) + "?v=" + DateTime.UtcNow.Ticks;
				}
			}

			int pct = Math.Max(10, Math.Min(90, UiTheme.CustomBackgroundOpacity));
			int rawVeilAlpha = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
				? (int)Math.Round(86.0 * (1.0 - pct / 100.0))
				: (int)Math.Round(255.0 * (1.0 - pct / 100.0));
			int veilA = Math.Max(18, Math.Min(235, rawVeilAlpha));
			Color baseVeil = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
				? Color.FromArgb(14, 20, 32)
				: UiTheme.Palette.SideOuterColor;
			string veilRgba = string.Format(System.Globalization.CultureInfo.InvariantCulture,
				"rgba({0},{1},{2},{3:0.00})", baseVeil.R, baseVeil.G, baseVeil.B, veilA / 255.0);
			bool active = nativeBackdrop || stageEnabled && (!string.IsNullOrEmpty(url) || glassFallback);

			int formW = Math.Max(1, ClientSize.Width);
			int formH = Math.Max(1, ClientSize.Height);
			int webX = 0, webY = 0;
			Control wc = webView;
			while (wc != null && wc != this)
			{
				webX += wc.Left;
				webY += wc.Top;
				wc = wc.Parent;
			}
			int webW = Math.Max(1, webView.ClientSize.Width > 0 ? webView.ClientSize.Width : Math.Max(1, formW - webX));
			int webH = Math.Max(1, webView.ClientSize.Height > 0 ? webView.ClientSize.Height : Math.Max(1, formH - webY));
			int fitMode = UiTheme.CustomBackgroundFit == UiTheme.BgFitContain ? 1 : 0;

			string js = "(function(cfg){"
				+ "try{"
				+ "window.__aandmStageCfg=cfg;"
				+ "var oldBg=document.getElementById('__aandm_stage_bg');"
				+ "var oldSt=document.getElementById('__aandm_stage_bg_style');"
				+ "if(!cfg.active){if(oldBg)oldBg.remove();if(oldSt)oldSt.remove();return;}"
				+ "if(cfg.nativeBackdrop&&oldBg){oldBg.remove();oldBg=null;}"
				+ "if(!oldSt){oldSt=document.createElement('style');oldSt.id='__aandm_stage_bg_style';document.head.appendChild(oldSt);}"
				+ "oldSt.textContent='html,body{background:transparent !important;background-color:transparent !important;background-image:none !important;}';"
				+ "if(!cfg.nativeBackdrop&&!oldBg){"
				+ "oldBg=document.createElement('div');oldBg.id='__aandm_stage_bg';"
				+ "oldBg.innerHTML='<div id=\"__aandm_stage_canvas\"><img id=\"__aandm_stage_img_blur\"><img id=\"__aandm_stage_img\"></div><div id=\"__aandm_stage_veil\"></div>';"
				+ "document.body.insertBefore(oldBg,document.body.firstChild);"
				+ "}"
				+ "if(oldBg)oldBg.style.cssText='position:fixed;inset:0;z-index:0;pointer-events:none;overflow:hidden;background:#0e1420;';"
				+ "function syncStageLayout(){"
				+ "var c=window.__aandmStageCfg;if(!c||!c.active)return;"
				+ "var bg=document.getElementById('__aandm_stage_bg');if(!bg)return;"
				+ "var canvas=document.getElementById('__aandm_stage_canvas');"
				+ "var blurImg=document.getElementById('__aandm_stage_img_blur');"
				+ "var img=document.getElementById('__aandm_stage_img');"
				+ "var vw=window.innerWidth||1,vh=window.innerHeight||1;"
				+ "if(c.isLive){"
				+ "canvas.style.cssText='position:absolute;left:0;top:0;width:100%;height:100%;overflow:hidden;background:#0e1420;';"
				+ "blurImg.style.display='none';"
				+ "img.style.cssText='position:absolute;inset:0;width:100%;height:100%;object-fit:fill;display:block;';"
				+ "}else{"
				+ "var fw=vw*(c.formW/Math.max(1,c.webW)),fh=vh*(c.formH/Math.max(1,c.webH));"
				+ "var ox=vw*(c.webX/Math.max(1,c.webW)),oy=vh*(c.webY/Math.max(1,c.webH));"
				+ "canvas.style.cssText='position:absolute;left:'+(-ox).toFixed(2)+'px;top:'+(-oy).toFixed(2)+'px;width:'+fw.toFixed(2)+'px;height:'+fh.toFixed(2)+'px;overflow:hidden;background:radial-gradient(circle at 18% 22%,rgba(72,168,224,0.45),transparent 42%),radial-gradient(circle at 62% 52%,rgba(118,86,198,0.42),transparent 48%),radial-gradient(circle at 82% 18%,rgba(56,188,164,0.38),transparent 42%),linear-gradient(122deg,#142a4e,#0e1422);';"
				+ "if(c.url){"
				+ "if(c.fit===1){"
				+ "blurImg.style.cssText='position:absolute;inset:0;width:100%;height:100%;object-fit:cover;object-position:center center;filter:blur(18px) brightness(0.75);transform:scale(1.06);display:block;';"
				+ "img.style.cssText='position:absolute;inset:0;width:100%;height:100%;object-fit:contain;object-position:center center;display:block;';"
				+ "}else{"
				+ "blurImg.style.display='none';"
				+ "img.style.cssText='position:absolute;inset:0;width:100%;height:100%;object-fit:cover;object-position:center center;display:block;';"
				+ "}"
				+ "}else{blurImg.style.display='none';img.style.display='none';}"
				+ "}"
				+ "}"
				+ "var blurImgEl=document.getElementById('__aandm_stage_img_blur');"
				+ "var imgEl=document.getElementById('__aandm_stage_img');"
				+ "if(imgEl&&cfg.url){"
				+ "if(imgEl.getAttribute('data-u')!==cfg.url){imgEl.src=cfg.url;imgEl.setAttribute('data-u',cfg.url);blurImgEl.src=cfg.url;}"
				+ "}"
				+ "syncStageLayout();"
				+ "var veil=document.getElementById('__aandm_stage_veil');"
				+ "if(veil)veil.style.cssText='position:absolute;inset:0;background:'+cfg.veil+';';"
				+ "function liftCanvas(){"
				+ "var vw=window.innerWidth||1,vh=window.innerHeight||1;"
				+ "var ch=document.body.children;for(var i=0;i<ch.length;i++){if(ch[i].id!=='__aandm_stage_bg'){if(getComputedStyle(ch[i]).position==='static')ch[i].style.position='relative';ch[i].style.zIndex='1';}}"
				+ "var cvs=document.querySelectorAll('canvas');"
				+ "for(var j=0;j<cvs.length;j++){"
				+ "var c=cvs[j];c.style.boxShadow=cfg.noRim?'0 16px 48px rgba(0,0,0,0.58)':'0 14px 48px rgba(0,0,0,0.62),0 0 0 1px rgba(255,255,255,0.22)';c.style.borderRadius='8px';"
				+ "var p=c.parentElement;while(p&&p!==document.body&&p!==document.documentElement){"
				+ "var r=p.getBoundingClientRect();if(r.width>=vw*0.85&&r.height>=vh*0.85){p.style.setProperty('background','transparent','important');p.style.setProperty('background-color','transparent','important');p.style.setProperty('background-image','none','important');}"
				+ "p=p.parentElement;}}"
				+ "}"
				+ "liftCanvas();if(!window.__aandmStageHooked){window.__aandmStageHooked=true;window.addEventListener('resize',syncStageLayout);setTimeout(liftCanvas,600);setTimeout(liftCanvas,1800);}"
				+ "}catch(e){}"
				+ "})(" + json.Serialize(new { active = active || nativeBackdrop, nativeBackdrop = nativeBackdrop, url = url, isLive = hasLive, fit = fitMode, noRim = glassFallback, formW = formW, formH = formH, webX = webX, webY = webY, webW = webW, webH = webH, veil = veilRgba }) + ");";
			try { await ExecuteScriptAsync(js); }
			catch { }
		}

		private string GuildBossDailyPath()
		{
			string dir = DataRoot();
			Directory.CreateDirectory(dir);
			return Path.Combine(dir, "guild-boss-daily.json");
		}

		private void LoadGuildBossDaily()
		{
			guildBossDoneDays.Clear();
			try
			{
				string path = GuildBossDailyPath();
				if (!File.Exists(path)) return;
				var map = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
				if (map == null) return;
				int minKeepDay = SpendDayNow() - 7;
				foreach (KeyValuePair<string, object> pair in map)
				{
					if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) continue;
					try
					{
						int day = Convert.ToInt32(pair.Value);
						if (day >= minKeepDay) guildBossDoneDays[pair.Key] = day;
					}
					catch { }
				}
			}
			catch { }
		}

		private void SaveGuildBossDaily()
		{
			if (previewMode) return;
			try
			{
				int minKeepDay = SpendDayNow() - 7;
				var payload = new Dictionary<string, int>();
				foreach (KeyValuePair<string, int> pair in guildBossDoneDays)
				{
					if (!string.IsNullOrEmpty(pair.Key) && pair.Value >= minKeepDay) payload[pair.Key] = pair.Value;
				}
				string path = GuildBossDailyPath();
				string temp = path + ".tmp";
				File.WriteAllText(temp, json.Serialize(payload), Encoding.UTF8);
				if (File.Exists(path)) File.Replace(temp, path, null);
				else File.Move(temp, path);
			}
			catch { }
		}

		private void RecordGuildBossDone(Dictionary<string, object> result)
		{
			if (previewMode || result == null) return;
			object raw;
			if (!result.TryGetValue("guildBossDone", out raw)) return;
			var info = raw as Dictionary<string, object>;
			if (info == null) return;
			string account = GetString(info, "account");
			int day = GetInt(info, "day");
			if (string.IsNullOrEmpty(account) || day <= 0) return;
			int existing;
			if (guildBossDoneDays.TryGetValue(account, out existing) && existing == day) return;
			guildBossDoneDays[account] = day;
			SaveGuildBossDaily();
		}

		private static string LuaStringLiteral(string value)
		{
			if (value == null) return "\"\"";
			var sb = new StringBuilder(value.Length + 2);
			sb.Append('"');
			foreach (char ch in value)
			{
				if (ch == '\\') sb.Append("\\\\");
				else if (ch == '"') sb.Append("\\\"");
				else if (ch == '\r') sb.Append("\\r");
				else if (ch == '\n') sb.Append("\\n");
				else sb.Append(ch);
			}
			sb.Append('"');
			return sb.ToString();
		}

		private async Task SyncGuildBossDailyAsync()
		{
			if (!coreReady || IsDisposed || guildBossDoneDays.Count == 0) return;
			var sb = new StringBuilder();
			sb.Append("return __MWA:set_guild_boss_done_days({");
			bool first = true;
			foreach (KeyValuePair<string, int> pair in guildBossDoneDays)
			{
				if (string.IsNullOrEmpty(pair.Key)) continue;
				if (!first) sb.Append(',');
				first = false;
				sb.Append('[');
				sb.Append(LuaStringLiteral(pair.Key));
				sb.Append("]=");
				sb.Append(pair.Value);
			}
			sb.Append("})");
			try { await ExecuteLuaAsync(sb.ToString()); }
			catch { }
		}

		private void ApplyMute()
		{
			if (webView == null || webView.CoreWebView2 == null)
			{
				return;
			}
			try
			{
				bool flag = !options.ContainsKey("autoMute") || options["autoMute"];
				webView.CoreWebView2.IsMuted = flag;
				if (muteLogged != flag)
				{
					muteLogged = flag;
					AddLog(flag ? "已自动静音浏览器/游戏声音" : "已恢复浏览器/游戏声音", BlueColor);
				}
			}
			catch
			{
			}
		}

		private async Task<string> ExecuteScriptAsync(string script)
		{
			if (!coreReady)
			{
				throw new InvalidOperationException("内置浏览器尚未初始化");
			}
			return await webView.CoreWebView2.ExecuteScriptAsync(script);
		}

		private async Task<string> ExecuteLuaAsync(string lua)
		{
			string expression = "window.__MWA_APP && window.__MWA_APP.ready() ? window.__MWA_APP.runLua(" + json.Serialize(lua) + ") : null";
			return await ExecuteScriptAsync(expression);
		}

		// ------------------------------------------------------------
		// 安全与可见性（PRD v2.0 里程碑 A）：消费基数持久化、审计落盘、
		// 兼容性自检。全部失败静默，不影响任务轮询。
		// ------------------------------------------------------------
		private readonly object auditFileLock = new object();
		private int lastAuditSeq;
		private int lastCompatTick = int.MinValue;
		private string lastCompatStatus = "";

		private static int SpendDayNow()
		{
			// 与 Lua spend_day_now 一致：UTC+8 的天数序号（时间种子，非业务逻辑）。
			DateTime now = DateTime.UtcNow.AddHours(8.0);
			return (int)(now.Date - new DateTime(1970, 1, 1)).TotalDays;
		}

		private async Task SyncSafetyStateAsync(Dictionary<string, object> snapshot, int generation)
		{
			try
			{
				if (guildBossSyncedPage != generation)
				{
					guildBossSyncedPage = generation;
					await SyncGuildBossDailyAsync();
					if (generation != pageGeneration) return;
				}
				int spendTotal = GetInt(snapshot, "spendTotal");
				int auditSeq = GetInt(snapshot, "auditSeq");
				int today = SpendDayNow();
				int storedDay = GetNumberOption("spendDay", 0);
				int storedBase = GetNumberOption("spendBase", 0);
				bool changed = false;
				int newBase = storedBase;
				if (storedDay != today)
				{
					// 跨游戏日：基数与会话增量一并清零，昨天的消耗不计入今天。
					numberOptions["spendDay"] = today;
					numberOptions["spendBase"] = 0;
					newBase = 0;
					changed = true;
				}
				else if (spendTotal > storedBase)
				{
					numberOptions["spendBase"] = spendTotal;
					newBase = spendTotal;
					changed = true;
				}
				if (changed)
				{
					SaveOptions();
					if (storedDay != today)
					{
						// 跨游戏日：整体清零（昨天的会话消耗不再计入今天）。
						await ExecuteLuaAsync("__MWA:spend_reset_day()");
					}
					else
					{
						// 回推新基数：Lua 侧按差值扣减会话增量，避免丢失间隙消费。
						await ExecuteLuaAsync("__MWA:spend_reset_session(" + newBase + ")");
					}
				}
				if (generation != pageGeneration) return;
				if (auditSeq < lastAuditSeq) lastAuditSeq = 0;
				if (auditSeq > lastAuditSeq)
				{
					int saved = await WriteAuditTailAsync(lastAuditSeq, generation);
					if (generation != pageGeneration) return;
					lastAuditSeq = saved;
				}
				int nowTicks = Environment.TickCount;
				if (lastCompatTick == int.MinValue || nowTicks - lastCompatTick < 0 || nowTicks - lastCompatTick > 300000)
				{
					lastCompatTick = nowTicks;
					await RunCompatCheckAsync();
				}
			}
			catch
			{
			}
		}

		private async Task<int> WriteAuditTailAsync(int sinceSeq, int generation)
		{
			string raw = await ExecuteLuaAsync("return __MWA:audit_tail(" + sinceSeq + ")");
			if (generation != pageGeneration || string.IsNullOrEmpty(raw) || raw == "null" || raw == "[]")
			{
				return sinceSeq;
			}
			object parsed = json.DeserializeObject(raw);
			System.Collections.IEnumerable entries = parsed as System.Collections.IEnumerable;
			if (entries == null)
			{
				return sinceSeq;
			}
			string directory = Path.Combine(DataRoot(), "audit");
			string file = Path.Combine(directory, "audit-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
			var builder = new StringBuilder();
			int newestSeq = sinceSeq;
			foreach (object entry in entries)
			{
				var record = entry as Dictionary<string, object>;
				if (record == null) continue;
				int seq = GetInt(record, "seq");
				if (seq <= newestSeq) continue;
				newestSeq = seq;
				builder.Append(json.Serialize(record));
				builder.Append("\n");
			}
			if (builder.Length == 0) return sinceSeq;
			string lines = builder.ToString();
			await Task.Run(delegate {
				lock (auditFileLock)
				{
					Directory.CreateDirectory(directory);
					File.AppendAllText(file, lines, Encoding.UTF8);
				}
			});
			return newestSeq;
		}

		private async Task RunCompatCheckAsync()
		{
			string raw = await ExecuteLuaAsync("return __MWA:compat_report()");
			if (string.IsNullOrEmpty(raw) || raw == "null")
			{
				return;
			}
			var report = json.Deserialize<Dictionary<string, object>>(raw);
			if (report == null) return;
			string status = GetString(report, "status");
			if (status == lastCompatStatus) return;
			bool previouslyOk = lastCompatStatus == "ok";
			lastCompatStatus = status;
			if (status == "ok")
			{
				if (previouslyOk) return;
				AddLog("兼容性自检通过：助手依赖的游戏接口与配置表完整。", GreenColor);
				return;
			}
			int total = GetInt(report, "total");
			AddLog("兼容性自检：检查 " + total + " 项，发现以下依赖异常，相关功能可能无法工作——", RedColor);
			object failuresObj = null;
			if (report.TryGetValue("failures", out failuresObj) && failuresObj is System.Collections.IEnumerable)
			{
				foreach (object item in (System.Collections.IEnumerable)failuresObj)
				{
					var record = item as Dictionary<string, object>;
					if (record != null)
					{
						AddLog("  · " + GetString(record, "name") + "：" + GetString(record, "reason"), RedColor);
					}
				}
			}
			AddLog("若游戏刚更新，请等待助手适配；审计目录（AandmAutoData\\audit）可导出反馈。", YellowColor);
		}

		private void BeginRelogin(string reason)
		{
			autoLoginDone = false;
			autoLoginWasInWorld = false;
			autoLoginReconnecting = true;
			autoLoginForceSignIn = true;
			autoLoginStage = 0;
			autoLoginFailures = 0;
			autoLoginStableCount = 0;
			autoLoginReadyCount = 0;
			autoLoginDisconnectCount = 0;
			autoLoginRetryAt = DateTime.MinValue;
			autoLoginAttemptAt = DateTime.MinValue;
			autoLoginUserLoginAt = DateTime.MinValue;
			autoLoginEarliestAt = DateTime.Now.AddSeconds(2.0);
			AddLog(reason + "，正在自动重连…", YellowColor);
		}

		private void ScheduleAutoLoginRetry(string message)
		{
			autoLoginDone = true;
			autoLoginStage = 0;
			autoLoginFailures = 0;
			autoLoginReadyCount = 0;
			autoLoginRetryAt = DateTime.Now.AddSeconds(30.0);
			AddLog(message, RedColor);
		}

		private async Task TryAutoLoginAsync()
		{
			if (!autoLoginEnabled || autoLoginBusy || !coreReady || string.IsNullOrEmpty(accountUsername) || string.IsNullOrEmpty(accountPassword))
			{
				return;
			}
			if (autoLoginRetryAt != DateTime.MinValue)
			{
				if (DateTime.Now < autoLoginRetryAt)
				{
					return;
				}
				autoLoginRetryAt = DateTime.MinValue;
				autoLoginDone = false;
				autoLoginStage = 0;
				autoLoginFailures = 0;
				autoLoginReadyCount = 0;
				autoLoginEarliestAt = DateTime.Now;
				AddLog("重新尝试自动登录…", YellowColor);
			}
			if ((DateTime.Now - autoLoginAttemptAt).TotalMilliseconds < 900.0 || (autoLoginStage == 2 && (DateTime.Now - autoLoginUserLoginAt).TotalSeconds < 20.0) || (autoLoginStage == 0 && DateTime.Now < autoLoginEarliestAt))
			{
				return;
			}
			autoLoginBusy = true;
			try
			{
				string stateJson = await ExecuteLuaAsync("local l=require(\"logic.login\") local sm=require(\"scene.scene_manager\") local d=l.account_delegate local sc=cc.Director:getInstance():getRunningScene() local ui=sc and sc.ui_root local btn=ui and ui.login_btn local button_ready=false if ui and ui.root_node and ui.root_node:isVisible() and ui.bottom_node and ui.bottom_node:isVisible() and btn and btn:isVisible() then button_ready=true end if button_ready and type(btn.getOpacity)==\"function\" and btn:getOpacity() < 250 then button_ready=false end return {ready=true,scene=sm:GetCurrentSceneName(),user_login=l.is_user_login and true or false,server_count=#(l.server_list or {}),server_id=l.server_id or 0,has_openid=type(l.openid)==\"string\" and #l.openid>0,login_ready=(type(d)==\"table\" and type(l.SignIn)==\"function\") and true or false,login_button_ready=button_ready,authorizing=(d and d.is_authorizing) and true or false}");
				if (string.IsNullOrEmpty(stateJson) || stateJson == "null")
				{
					return;
				}
				Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(stateJson);
				if (state == null || !GetBool(state, "ready"))
				{
					return;
				}
				string scene = GetString(state, "scene");
				bool userLogin = GetBool(state, "user_login");
				bool authorizing = GetBool(state, "authorizing");
				bool hasOpenid = GetBool(state, "has_openid");
				bool loginReady = GetBool(state, "login_ready");
				bool loginButtonReady = GetBool(state, "login_button_ready");
				int serverId = GetInt(state, "server_id");
				if (scene == "world" && userLogin)
				{
					autoLoginStableCount++;
					autoLoginDisconnectCount = 0;
					if (autoLoginStableCount >= 3)
					{
						bool flag = autoLoginReconnecting;
						if (!autoLoginDone)
						{
							AddLog(flag ? "掉线重连成功。" : "自动登录完成。", GreenColor);
						}
						autoLoginDone = true;
						autoLoginWasInWorld = true;
						autoLoginReconnecting = false;
						autoLoginForceSignIn = false;
						autoLoginRetryAt = DateTime.MinValue;
					}
					return;
				}
				autoLoginStableCount = 0;
				if (scene == "world" && !userLogin)
				{
					if (!autoLoginWasInWorld)
					{
						return;
					}
					autoLoginDisconnectCount++;
					if (autoLoginDisconnectCount < 4)
					{
						return;
					}
					BeginRelogin("检测到角色掉线");
					try
					{
						webView.CoreWebView2.Reload();
						return;
					}
					catch
					{
						return;
					}
				}
				if (scene != "login")
				{
					autoLoginReadyCount = 0;
					autoLoginDisconnectCount = 0;
					return;
				}
				if (autoLoginWasInWorld)
				{
					autoLoginDisconnectCount++;
					if (autoLoginDisconnectCount >= 2)
					{
						BeginRelogin("检测到掉线");
					}
					return;
				}
				autoLoginDisconnectCount = 0;
				if (autoLoginStage == 0 && !hasOpenid && !autoLoginReconnecting)
				{
					if (loginReady && loginButtonReady)
					{
						autoLoginReadyCount++;
					}
					else
					{
						autoLoginReadyCount = 0;
					}
					if (autoLoginReadyCount < 3)
					{
						return;
					}
				}
				if (authorizing)
				{
					autoLoginAttemptAt = DateTime.Now;
				}
				else if (hasOpenid && serverId > 0 && !autoLoginForceSignIn)
				{
					if (autoLoginStage != 2)
					{
						string enterLua = "local l=require(\"logic.login\"); return l:UserLogin(" + serverId + ")";
						autoLoginStage = 2;
						autoLoginUserLoginAt = DateTime.Now;
						autoLoginAttemptAt = DateTime.Now;
						await ExecuteLuaAsync(enterLua);
						AddLog("账号验证成功，正在进入游戏…", BlueColor);
					}
					else if ((DateTime.Now - autoLoginUserLoginAt).TotalSeconds > 120.0)
					{
						ScheduleAutoLoginRetry("自动进入游戏超时，30 秒后重试。");
					}
				}
				else if (autoLoginStage != 1 || !((DateTime.Now - autoLoginAttemptAt).TotalSeconds < 9.0))
				{
					autoLoginFailures++;
					if (autoLoginFailures > 3)
					{
						ScheduleAutoLoginRetry("自动登录未成功，30 秒后重试。");
						return;
					}
					string signInLua = "local l=require(\"logic.login\"); l:SignIn(" + json.Serialize(accountUsername) + "," + json.Serialize(accountPassword) + "); return true";
					await ExecuteLuaAsync(signInLua);
					autoLoginStage = 1;
					autoLoginForceSignIn = false;
					autoLoginAttemptAt = DateTime.Now;
					AddLog("正在自动登录账号 " + MaskedAccount() + "…", BlueColor);
				}
			}
			catch (Exception ex)
			{
				AddLog("自动登录异常：" + ex.Message, RedColor);
				autoLoginFailures++;
				if (autoLoginFailures > 3)
				{
					ScheduleAutoLoginRetry("自动登录异常，30 秒后重试。");
				}
			}
			finally
			{
				autoLoginBusy = false;
			}
		}

		private async Task StopAutomationAsync()
		{
			startRequested = false;
			if (coreReady)
			{
				try
				{
					await webView.CoreWebView2.ExecuteScriptAsync("window.__MWA_APP ? __MWA_APP.stop() : null");
				}
				catch
				{
				}
			}
			AddLog("已暂停自动挖矿。", BlueColor);
			await PollOnce();
			UpdateMainButton(lastRunning);
		}

		private async void Option_CheckedChanged(object sender, EventArgs e)
		{
			if (syncingOptions)
			{
				return;
			}
			CheckBox box = sender as CheckBox;
			if (box == null)
			{
				return;
			}
			string key = Convert.ToString(box.Tag);
			options[key] = box.Checked;
			SaveOptions();
			AddLog("设置 " + box.Text + " = " + (box.Checked ? "开" : "关"), BlueColor);
			if (!coreReady)
			{
				return;
			}
			try
			{
				string script = "__MWA_APP.setOption(" + json.Serialize(key) + "," + (box.Checked ? "true" : "false") + ")";
				await webView.CoreWebView2.ExecuteScriptAsync(script);
			}
			catch
			{
			}
		}

		private void BtnReload_Click(object sender, EventArgs e)
		{
            StopDaily(); StopArena(); StopVoid(); StopMine(); StopWorkshop(); StopGuild(); StopGrowth(); StopVoidExchange();
			startRequested = false;
			seenLogs.Clear();
			lastSnapshotLogs.Clear();
			lastRunning = false;
			AddLog("正在重新加载游戏页面…", BlueColor);
			if (coreReady)
			{
				webView.CoreWebView2.Reload();
			}
			else
			{
				webView.Source = new Uri(GameUrl);
			}
		}

        private void BtnAccount_Click(object sender,EventArgs e)
        {
            using(var manager=new AccountManagerForm(new AccountProfiles(AppPaths.BaseRoot))){
                var result=manager.ShowDialog(this);
                if(result==DialogResult.Retry)EditCurrentAccount(sender,e);
                if(result==DialogResult.Yes)try{new MultiAccountForm(new AccountProfiles(AppPaths.BaseRoot),manager.ScreenProfiles).Show();}catch(Exception error){AddLog(error.Message,YellowColor);}
            }
        }
		private async void EditCurrentAccount(object sender, EventArgs e)
		{
            if(AnyTaskRunning()){AddLog("请先停止本窗口的自动任务，再修改登录账号。",YellowColor);return;}
			using (AccountSettingsForm dialog = new AccountSettingsForm(accountUsername, accountPassword, autoLoginEnabled))
			{
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
                    if(!dialog.Cleared) {
                        try {
                            var profiles=new AccountProfiles(AppPaths.BaseRoot);
                            foreach(var p in profiles.Read())if(p.Id!=AppPaths.ProfileId && string.Equals(profiles.ReadCredential(p.Id).Username,dialog.AccountUsername,StringComparison.OrdinalIgnoreCase)) {
                                AddLog("该账号已在多开列表中，请打开其已有窗口。",YellowColor);return;
                            }
                        } catch(Exception error){AddLog("账号信息未保存："+error.Message,RedColor);return;}
                    }

                    bool changed=!string.Equals(accountUsername,dialog.AccountUsername,StringComparison.OrdinalIgnoreCase);
                    if(changed)File.WriteAllText(Path.Combine(DataRoot(),"browser-reset-required"),"account changed");
					accountUsername = dialog.AccountUsername;
					accountPassword = dialog.AccountPassword;
					autoLoginEnabled = dialog.AutoLogin;
					SaveAccountSettings();
                    Text=BuildEdition.WindowTitle+" · "+new AccountProfiles(AppPaths.BaseRoot).Label(AppPaths.ProfileId);
                    if(trayIcon!=null)trayIcon.Text=ProfileTrayText();
					autoLoginDone = false;
					autoLoginStage = 0;
					autoLoginFailures = 0;
					autoLoginStableCount = 0;
					autoLoginReadyCount = 0;
					autoLoginDisconnectCount = 0;
					autoLoginWasInWorld = false;
					autoLoginReconnecting = false;
					autoLoginForceSignIn = false;
					autoLoginRetryAt = DateTime.MinValue;
					autoLoginEarliestAt = DateTime.Now.AddSeconds(3.0);
					autoLoginAttemptAt = DateTime.MinValue;
					autoLoginUserLoginAt = DateTime.MinValue;
					if (dialog.Cleared)
					{
						AddLog("账号设置已清除。", BlueColor);
					}
					else
					{
						AddLog("账号设置已保存（账号 " + MaskedAccount() + "）。", BlueColor);
					}
                    if((changed||File.Exists(Path.Combine(DataRoot(),"browser-reset-required")))&&webView.CoreWebView2!=null) {
                        coreReady=false;autoLoginBusy=true;
                        try{await ResetProfileLoginAsync();coreReady=true;webView.CoreWebView2.Reload();}
                        catch(Exception error){AddLog("旧登录状态尚未清理，请重新打开此账号窗口："+error.Message,RedColor);}
                        finally{autoLoginBusy=false;}
                        return;
                    }
					if (autoLoginEnabled && !string.IsNullOrEmpty(accountUsername) && !string.IsNullOrEmpty(accountPassword))
					{
						AddLog("已开启自动登录，正在尝试登录…", BlueColor);
						await TryAutoLoginAsync();
					}
				}
			}
		}

		private void BtnDevTools_Click(object sender, EventArgs e)
		{
			if (!coreReady)
			{
				AddLog("内置浏览器尚未初始化，无法打开开发者工具。", YellowColor);
				return;
			}
			try
			{
				if (!webView.CoreWebView2.Settings.AreDevToolsEnabled)
				{
					AddLog("开发者工具默认关闭（减少资源占用）。如需调试，请设置环境变量 AANDM_DEBUG=1 后重启助手。", YellowColor);
					return;
				}
				webView.CoreWebView2.OpenDevToolsWindow();
			}
			catch (Exception ex)
			{
				AddLog("打开开发者工具失败：" + ex.Message, RedColor);
			}
		}

		private void BtnClearLog_Click(object sender, EventArgs e)
		{
			ClearAdventureLogs(false);
		}

		private void ClearAdventureLogs(bool automatic)
		{
			if (txtLog == null || txtLog.IsDisposed)
			{
				return;
			}
			if (base.InvokeRequired)
			{
				SafeBeginInvoke(delegate { ClearAdventureLogs(automatic); });
				return;
			}
			if (automatic && txtLog.TextLength == 0 && logHistoryLines.Count == 0)
			{
				return;
			}
			logUiDirty = false;
			txtLog.Clear();
			seenLogs.Clear();
			logHistoryLines.Clear();
			logHistoryColors.Clear();
			recentLogTimes.Clear();
			recentLogTexts.Clear();
			recentLogColors.Clear();
			UpdateRecentLogView();
			foreach (string item in lastSnapshotLogs)
			{
				seenLogs.Add(item);
			}
			if (automatic)
			{
				AddLog(extremePerformanceMode ? "冒险日志已定时清理（极致性能模式每 3 分钟自动防溢出）。" : "冒险日志已定时清理（每 10 分钟自动防溢出）。", MutedColor);
			}
		}

		private void AddLog(string text, Color color)
		{
			if (txtLog == null)
			{
				return;
			}
			if (base.InvokeRequired)
			{
				SafeBeginInvoke(delegate { AddLog(text, color); });
				return;
			}
			AppendAdventureLogs(new[] { text }, color);
		}

		private void AppendAdventureLogs(IList<string> entries, Color color)
		{
			if (txtLog == null || txtLog.IsDisposed || entries.Count == 0) return;
			var block = new StringBuilder();
			string stamp = DateTime.Now.ToString("HH:mm:ss");
			foreach (string entry in entries)
			{
				RecordRecentLogEntry(entry, color);
				string line = "[" + stamp + "] " + entry + "\r\n";
				RecordLogHistoryLine(line, color);
				block.Append(line);
			}
			if (!extremePerformanceMode || !IsMinimizedOrHostMinimized())
			{
				UpdateRecentLogView();
			}
			if (ShouldDeferLogUi())
			{
				logUiDirty = true;
				return;
			}
			if (logUiDirty)
			{
				FlushDeferredLogUi();
				return;
			}
			int selectionStart = txtLog.SelectionStart;
			int selectionLength = txtLog.SelectionLength;
			bool followTail = selectionLength == 0 && (txtLog.TextLength == 0 ||
				txtLog.GetCharIndexFromPosition(new Point(Math.Max(0, txtLog.ClientSize.Width - 3),
					Math.Max(0, txtLog.ClientSize.Height - 3))) >= txtLog.TextLength - 2);
			Point scroll = followTail ? Point.Empty : WindowStyle.GetRichEditScroll(txtLog.Handle);
			txtLog.SelectionStart = txtLog.TextLength;
			txtLog.SelectionLength = 0;
			if (!extremePerformanceMode)
			{
				txtLog.SelectionColor = color;
			}
			txtLog.AppendText(block.ToString());
			int removed = 0;
			int maxLen = extremePerformanceMode ? 12000 : 60000;
			if (txtLog.TextLength > maxLen)
			{
				int cut = extremePerformanceMode
					? Math.Min(Math.Max(6000, txtLog.TextLength - 6000), txtLog.TextLength)
					: Math.Min(25000, txtLog.TextLength);
				int nl = txtLog.Text.IndexOf('\n', cut);
				if (nl > cut && nl < txtLog.TextLength) cut = nl + 1;
				removed = cut;
				txtLog.Select(0, cut);
				// RichEdit rejects selection replacement while read-only and plays a system sound.
				bool wasReadOnly = txtLog.ReadOnly;
				txtLog.ReadOnly = false;
				try
				{
					txtLog.SelectedText = string.Empty;
				}
				finally
				{
					txtLog.ReadOnly = wasReadOnly;
				}
			}
			if (followTail)
			{
				txtLog.SelectionStart = txtLog.TextLength;
				txtLog.ScrollToCaret();
			}
			else
			{
				int start = Math.Max(0, selectionStart - removed);
				int end = Math.Max(start, selectionStart + selectionLength - removed);
				txtLog.Select(start, end - start);
				if (removed == 0) WindowStyle.SetRichEditScroll(txtLog.Handle, scroll);
			}
		}

        private async Task ResetProfileLoginAsync()
        {
            string marker=Path.Combine(DataRoot(),"browser-reset-required");if(!File.Exists(marker))return;
            await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
            File.Delete(marker);AddLog("已清理此窗口的旧登录缓存，将使用新保存的账号。",BlueColor);
        }
		private string AccountPath()
		{
			return Path.Combine(DataRoot(), "account.dat");
		}

		private string FindAccountPath()
		{
			string text = AccountPath();
			if (File.Exists(text))
			{
				return text;
			}
            if(AppPaths.ProfileId!="main")return text;
			try
			{
				string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AandmAuto", "account.dat");
				if (File.Exists(text2))
				{
					return text2;
				}
			}
			catch
			{
			}
			try
			{
				string directoryName = Path.GetDirectoryName(Application.ExecutablePath);
				if (!string.IsNullOrEmpty(directoryName))
				{
					string text3 = Path.Combine(directoryName, "AandmAutoData", "account.dat");
					if (File.Exists(text3))
					{
						return text3;
					}
				}
			}
			catch
			{
			}
			return text;
		}

		private void LoadAccountSettings()
		{
			try
			{
				string path = FindAccountPath();
				if (!File.Exists(path))
				{
					return;
				}
				string input = File.ReadAllText(path, Encoding.UTF8);
				Dictionary<string, object> dictionary = json.Deserialize<Dictionary<string, object>>(input);
				if (dictionary != null)
				{
					object value;
					if (dictionary.TryGetValue("username", out value) && value != null)
					{
						accountUsername = Convert.ToString(value);
					}
					if (dictionary.TryGetValue("autoLogin", out value) && value is bool)
					{
						autoLoginEnabled = (bool)value;
					}
					if (dictionary.TryGetValue("password", out value) && value != null)
					{
						byte[] encryptedData = Convert.FromBase64String(Convert.ToString(value));
						byte[] bytes = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
						accountPassword = Encoding.UTF8.GetString(bytes);
					}
				}
			}
			catch
			{
				accountUsername = string.Empty;
				accountPassword = string.Empty;
				autoLoginEnabled = false;
			}
		}

		private void SaveAccountSettings()
		{
			try
			{
				if (string.IsNullOrEmpty(accountUsername) && string.IsNullOrEmpty(accountPassword))
				{
					string path = AccountPath();
					if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
				else
				{
					byte[] bytes = Encoding.UTF8.GetBytes(accountPassword ?? string.Empty);
					byte[] inArray = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
					Dictionary<string, object> dictionary = new Dictionary<string, object>();
					dictionary["username"] = accountUsername ?? string.Empty;
					dictionary["password"] = Convert.ToBase64String(inArray);
					dictionary["autoLogin"] = autoLoginEnabled;
					File.WriteAllText(AccountPath(), json.Serialize(dictionary), Encoding.UTF8);
				}
			}
			catch (Exception ex)
			{
				AddLog("保存账号设置失败：" + ex.Message, RedColor);
			}
		}

		private string MaskedAccount()
		{
			if (string.IsNullOrEmpty(accountUsername))
			{
				return "未设置";
			}
			if (accountUsername.Length <= 4)
			{
				return accountUsername.Substring(0, 1) + "***";
			}
			return accountUsername.Substring(0, 4) + "****";
		}

		private string OptionsPath()
		{
			string text = DataRoot();
			Directory.CreateDirectory(text);
			return Path.Combine(text, "options.json");
		}

		private void LoadOptions()
		{
			string[] array = new string[27]
			{
				"autoMine", "autoGolem", "autoBoss", "autoEnter", "refuseEvents", "autoChest", "autoCaveBoss", "useTntOnLong", "allowLongOre", "autoSkipBattle",
				"autoUseMiningBag", "autoTreasureEvent", "autoMute", "arenaAllowBloodRefresh", "autoChest40", "autoChest41", "autoChest42", "autoChest43", "autoChest44", "autoChest45",
				"autoChest48", "waitNightmareCooldown", "autoUseEarthHeart", "runeUseFlashGold",
				"spendAllowCaveBuy", "autoMiningTnt", "workshopShallowMining"
			};
            var optionKeys=new List<string>(array);
            for(int id=1;id<=15;id++)optionKeys.Add("dailyTask"+id);
            optionKeys.AddRange(new[]{"dailyRewards","dailyFreePrayer","dailyFreeAlchemy","dailyIncludeMine",
                "dailyMysteryShop",
                "autoMysticStoneReset","lowDefinitionLive",
                "cartUpgradeMax","cartBuyEscort","cartPaidTargetRefresh",
                "dailyVoidChallenge","dailyMarketExchange","voidExchangeGoldSoul","voidExchangePurpleSoul","dailyGiftAll","dailyCheckinAll","dailyVipReward","dailyVoidExchange","dailyGuildBoss","dailyEvents","dailyMail","dailyGoldSummon","dailyActivityRewards","dailyRefine","earthUseAll","followMiningCamera","showMiningRoute","dailyMineGuards","collapseStatus","collapseStats","collapseLog","notifyDone","stageWallpaperBg","autoSelfHeal","glassMotion"});
            array=optionKeys.ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				options[array[i]] = true;
			}
			options["collapseStatus"]=options["collapseStats"]=options["collapseLog"]=false;
            options["showMiningRoute"]=false;
            options["dailyMysteryShop"]=false;
            options["autoMysticStoneReset"]=options["lowDefinitionLive"]=false;
			options["earthUseAll"]=false;
            options["voidExchangeGoldSoul"]=options["voidExchangePurpleSoul"]=false;
            options["autoChest"] = false;
			options["autoChest44"] = false;
			options["waitNightmareCooldown"] = false;
			options["autoCaveBoss"] = false;
			options["useTntOnLong"] = false;
			options["allowLongOre"] = false;
			options["autoSkipBattle"] = true;
			options["arenaAllowBloodRefresh"] = true;
            options["arenaPaidRefreshChoiceV11"] = true;
            numberOptions[BattleReplaySpeedOption] = 100;
            numberOptions["horizontalStartDepth"] = 150;
			numberOptions["miningTntMinSaving"] = 2;
			numberOptions["mysticStoneThreshold"] = 150;
			numberOptions["mysticStoneResetTier"] = 1;
			numberOptions["mysteryBuyBudget"] = 0;
			numberOptions["mysteryRefreshBudget"] = 0;
			numberOptions["workshopDepth"] = 150;
			numberOptions["workshopMinutes"] = 120;
			numberOptions["guildBossCount"] = 3;
			numberOptions["spendBudget"] = -1;
			numberOptions["reserveTnt"] = 0;
            numberOptions["refineReserve"] = 1000;
			numberOptions["spendBase"] = 0;
			numberOptions["spendDay"] = 0;
			numberOptions["mineResearch"] = 0;
            numberOptions["mineResearchMask"] = -1;
            numberOptions["dailyAlchemyTier"] = 1;
            numberOptions["dailyTrollLevel"] = 0;
            numberOptions["dailyUndergroundLevel"] = 0;
            numberOptions["uiLayout"] = 0;
            numberOptions["uiTheme"] = 0;
            numberOptions["pvpUiStyle"] = 0;
            numberOptions["customBgOpacity"] = 35;
            numberOptions["customBgMode"] = 0;
            numberOptions["customBgFit"] = 0;
            numberOptions["customBgCropX"] = 0;
            numberOptions["customBgCropY"] = 0;
            numberOptions["customBgCropW"] = 10000;
            numberOptions["customBgCropH"] = 10000;
            numberOptions["glassBlur"] = 12;
            numberOptions["glassPreset"] = LiquidGlassEngine.PresetBalanced;
            numberOptions["auditRetentionDays"] = 14;
            customBgPath = "";
            customBgLabel = "";
            wallpaperEngineProject = "";
            wallpaperEngineDir = "";
			try
			{
				string path = OptionsPath();
				if (!File.Exists(path))
				{
					return;
				}
				string input = File.ReadAllText(path, Encoding.UTF8);
				Dictionary<string, object> dictionary = json.Deserialize<Dictionary<string, object>>(input);
				if (dictionary == null)
				{
					return;
				}
				string[] array2 = array;
				string[] array3 = array2;
				foreach (string key in array3)
				{
                    if (key == "arenaAllowBloodRefresh" && !dictionary.ContainsKey("arenaPaidRefreshChoiceV11")) continue;
					object value;
					if (dictionary.TryGetValue(key, out value) && value is bool)
					{
						options[key] = (bool)value;
					}
				}
				foreach (string key in new[] { BattleReplaySpeedOption, "horizontalStartDepth", "miningTntMinSaving", "mysteryBuyBudget", "mysteryRefreshBudget", "mysticStoneThreshold", "mysticStoneResetTier", "workshopDepth", "workshopMinutes", "guildBossCount", "refineReserve", "spendBase", "spendDay", "mineResearch", "mineResearchMask", "dailyAlchemyTier", "dailyTrollLevel", "dailyUndergroundLevel", "uiLayout", "uiTheme", "pvpUiStyle", "customBgOpacity", "customBgMode", "customBgFit", "customBgCropX", "customBgCropY", "customBgCropW", "customBgCropH", "glassBlur", "glassPreset", "wallpaperQuality", "wallpaperFps", "wallpaperPauseHidden", "auditRetentionDays" })
				{
					object value;
					if (dictionary.TryGetValue(key, out value) && value != null)
					{
						try { numberOptions[key] = Convert.ToInt32(value); }
						catch { }
					}
				}
				object bgVal;
				if (dictionary.TryGetValue("customBgPath", out bgVal) && bgVal != null)
				{
					customBgPath = Convert.ToString(bgVal) ?? "";
				}
				object engineProject;
				if (dictionary.TryGetValue("wallpaperEngineProject", out engineProject))
					wallpaperEngineProject = Convert.ToString(engineProject) ?? "";
				object bgLblVal;
				if (dictionary.TryGetValue("customBgLabel", out bgLblVal) && bgLblVal != null)
				{
					customBgLabel = Convert.ToString(bgLblVal) ?? "";
				}
				object weDirVal;
				if (dictionary.TryGetValue("wallpaperEngineDir", out weDirVal) && weDirVal != null)
				{
					wallpaperEngineDir = Convert.ToString(weDirVal) ?? "";
				}
				ClampNumberOptions();
				LoadMysteryShopOptions(dictionary);
			}
			catch
			{
			}
		}

		private void ClampNumberOptions()
		{
            if (Array.IndexOf(BattleReplaySpeeds, GetNumberOption(BattleReplaySpeedOption, 100)) < 0)
                numberOptions[BattleReplaySpeedOption] = 100;
            numberOptions["horizontalStartDepth"] = Math.Max(1,Math.Min(9999,GetNumberOption("horizontalStartDepth",150)));
			numberOptions["miningTntMinSaving"] = Math.Max(1, Math.Min(90, GetNumberOption("miningTntMinSaving", 2)));
			numberOptions["mysteryBuyBudget"] = Math.Max(0, Math.Min(1000000000, GetNumberOption("mysteryBuyBudget", 0)));
			numberOptions["mysteryRefreshBudget"] = Math.Max(0, Math.Min(1000000000, GetNumberOption("mysteryRefreshBudget", 0)));
			ClampMysticStoneOptions();
			numberOptions["workshopDepth"] = Math.Max(0, Math.Min(999, GetNumberOption("workshopDepth", 150)));
			int minutes = GetNumberOption("workshopMinutes", 120);
			if (minutes != 60 && minutes != 120 && minutes != 240) minutes = 120;
			numberOptions["workshopMinutes"] = minutes;
			numberOptions["guildBossCount"] = Math.Max(1, Math.Min(99, GetNumberOption("guildBossCount", 3)));
			numberOptions["pvpUiStyle"] = Math.Max(0, Math.Min(2, GetNumberOption("pvpUiStyle", 0)));
			numberOptions["spendBudget"] = -1;
			numberOptions["reserveTnt"] = 0;
            numberOptions["refineReserve"] = Math.Max(0,Math.Min(1000000,GetNumberOption("refineReserve",1000)));
			numberOptions["spendBase"] = Math.Max(0, GetNumberOption("spendBase", 0));
			numberOptions["spendDay"] = Math.Max(0, GetNumberOption("spendDay", 0));
			numberOptions["mineResearch"] = Math.Max(0, Math.Min(99, GetNumberOption("mineResearch", 0)));
            numberOptions["mineResearchMask"] = Math.Max(-1,Math.Min(4095,GetNumberOption("mineResearchMask",-1)));
            numberOptions["dailyAlchemyTier"] = Math.Max(1, Math.Min(3, GetNumberOption("dailyAlchemyTier", 1)));
            numberOptions["dailyTrollLevel"] = Math.Max(0, Math.Min(12, GetNumberOption("dailyTrollLevel", 0)));
            numberOptions["dailyUndergroundLevel"] = Math.Max(0, Math.Min(12, GetNumberOption("dailyUndergroundLevel", 0)));
            numberOptions["uiLayout"] = Math.Max(0, Math.Min(3, GetNumberOption("uiLayout", 0)));
            numberOptions["uiTheme"] = Math.Max(0, Math.Min(4, GetNumberOption("uiTheme", 0)));
            numberOptions["customBgOpacity"] = Math.Max(10, Math.Min(90, GetNumberOption("customBgOpacity", 35)));
            numberOptions["customBgMode"] = Math.Max(0, Math.Min(UiTheme.BgModeEngine, GetNumberOption("customBgMode", 0)));
            numberOptions["customBgFit"] = Math.Max(0, Math.Min(1, GetNumberOption("customBgFit", 0)));
            int cw = Math.Max(500, Math.Min(10000, GetNumberOption("customBgCropW", 10000)));
            int ch = Math.Max(500, Math.Min(10000, GetNumberOption("customBgCropH", 10000)));
            int cx = Math.Max(0, Math.Min(10000 - cw, GetNumberOption("customBgCropX", 0)));
            int cy = Math.Max(0, Math.Min(10000 - ch, GetNumberOption("customBgCropY", 0)));
            numberOptions["customBgCropX"] = cx;
            numberOptions["customBgCropY"] = cy;
            numberOptions["customBgCropW"] = cw;
            numberOptions["customBgCropH"] = ch;
            numberOptions["glassBlur"] = Math.Max(0, Math.Min(24, GetNumberOption("glassBlur", 12)));
            numberOptions["glassPreset"] = Math.Max(0, Math.Min(2, GetNumberOption("glassPreset", LiquidGlassEngine.PresetBalanced)));
            numberOptions["wallpaperQuality"] = Math.Max(0, Math.Min(2, GetNumberOption("wallpaperQuality", 0)));
            numberOptions["wallpaperFps"] = Math.Max(5, Math.Min(60, GetNumberOption("wallpaperFps", 30)));
            numberOptions["wallpaperPauseHidden"] = GetNumberOption("wallpaperPauseHidden", 1) == 0 ? 0 : 1;
            numberOptions["auditRetentionDays"] = Math.Max(3, Math.Min(90, GetNumberOption("auditRetentionDays", 14)));
		}

		private void ClampMysticStoneOptions()
		{
			numberOptions["mysticStoneThreshold"] = Math.Max(2, Math.Min(9999, GetNumberOption("mysticStoneThreshold", 150)));
			int tier = GetNumberOption("mysticStoneResetTier", 1);
			numberOptions["mysticStoneResetTier"] = (tier==100 || tier==200 || tier==300 || tier==350) ? tier : 1;
		}

		private int GetNumberOption(string key, int fallback)
		{
			int value;
			if (numberOptions.TryGetValue(key, out value)) return value;
			return fallback;
		}

		private void SaveOptions()
		{
			try
			{
				var payload = new Dictionary<string, object>();
				foreach (KeyValuePair<string, bool> pair in options) payload[pair.Key] = pair.Value;
				foreach (KeyValuePair<string, int> pair in numberOptions) payload[pair.Key] = pair.Value;
				payload["mysteryProducts"] = mysteryProducts;
				payload["customBgPath"] = customBgPath ?? "";
				payload["customBgLabel"] = customBgLabel ?? "";
				payload["wallpaperEngineProject"] = wallpaperEngineProject ?? "";
				payload["wallpaperEngineDir"] = wallpaperEngineDir ?? "";
				File.WriteAllText(OptionsPath(), json.Serialize(payload), Encoding.UTF8);
			}
			catch
			{
			}
		}

		private static bool GetBool(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null)
			{
				return false;
			}
			object value;
			if (dictionary.TryGetValue(key, out value) && value is bool)
			{
				return (bool)value;
			}
			return false;
		}

		private static string GetString(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null)
			{
				return string.Empty;
			}
			object value;
			if (dictionary.TryGetValue(key, out value) && value != null)
			{
				return Convert.ToString(value);
			}
			return string.Empty;
		}

		private static int GetInt(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null)
			{
				return 0;
			}
			object value;
			if (dictionary.TryGetValue(key, out value) && value != null)
			{
				try
				{
					return Convert.ToInt32(value);
				}
				catch
				{
					return 0;
				}
			}
			return 0;
		}

		private static Dictionary<string, object> GetDictionary(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null)
			{
				return null;
			}
			object value;
			if (dictionary.TryGetValue(key, out value))
			{
				return value as Dictionary<string, object>;
			}
			return null;
		}

		private static object[] GetArray(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null)
			{
				return null;
			}
			object value;
			if (!dictionary.TryGetValue(key, out value) || value == null)
			{
				return null;
			}
			// JavaScriptSerializer 把 JSON 数组反序列化成 ArrayList，不一定是 object[]；
			// 两种形状都要接受，否则日志镜像与挖矿统计会读不到列表。
			object[] array = value as object[];
			if (array != null)
			{
				return array;
			}
			System.Collections.IList list = value as System.Collections.IList;
			if (list != null)
			{
				array = new object[list.Count];
				list.CopyTo(array, 0);
				return array;
			}
			return null;
		}

		private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try { DisposeTaskCommands(); } catch { }
            try { if(MultiAccountForm.Current!=null)MultiAccountForm.Current.DetachAllImmediate(); } catch { }
            try { ResetAllTaskStates(); coreReady=false; } catch { }
            try { DisposeBackgroundMode(); } catch { }
            try { DisposeBackgroundRuntime(); } catch { }
            try { if(pageMine!=null && pageMine.Parent==null)pageMine.Dispose(); } catch { }
            try { if(scheduleTimer!=null){scheduleTimer.Stop();scheduleTimer.Dispose();} } catch { }
            try { if(dailyTaskTimer!=null){dailyTaskTimer.Stop();dailyTaskTimer.Dispose();} } catch { }
            try { if(logCleanupTimer!=null){logCleanupTimer.Stop();logCleanupTimer.Dispose();logCleanupTimer=null;} } catch { }
            try { if(memoryTrimTimer!=null){memoryTrimTimer.Stop();memoryTrimTimer.Dispose();memoryTrimTimer=null;} } catch { }
            try { if(selfHealTimer!=null){selfHealTimer.Stop();selfHealTimer.Dispose();selfHealTimer=null;} } catch { }
            try { if(hints!=null){hints.RemoveAll();hints.Dispose();hints=null;} } catch { }
            try
            {
                if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
            }
            catch
            {
            }
			if (pollTimer != null)
			{
				pollTimer.Stop();
				pollTimer.Dispose();
			}
			try
			{
				if (webView != null)
				{
					((Component)webView).Dispose();
				}
			}
			catch
			{
			}
		}
	}
}
