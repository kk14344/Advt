using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		internal const int UiLayoutClassic = 0;
		internal const int UiLayoutGrid = 1;
		internal const int UiLayoutStatus = 2;
		internal const int UiLayoutList = 3;

		private int activeUiLayout;
		private int savedArenaCount = 5;
		private string cachedStatusText = "等待加载";
		private Color cachedStatusColor = GoldColor;
		private string cachedDetailText = "登录后选择任务";
		private string cachedBadgeText = "准备中";
		private Color cachedBadgeColor = GoldColor;
		private string cachedMiningText = "矿镐 --/-- · 巨魔 Lv-- · 发现 -- · 深度 --";
		private int cachedDigCount = -1;
		private int cachedDigMax = -1;
		private int cachedGolemLv = -1;
		private int cachedDepth = -1;
		private string cachedMinePlan = "推荐矿层：等待游戏数据";
		private string cachedMineSlots = "矿队：等待矿山数据";
		private string cachedMineEarliest = "空闲";
		private double cachedMineEarliestSeconds = -1;
		private string cachedWorkshopLabel = "";
		private int cachedGuildTickets = -1;
		private string cachedAccountId = "";
		private double cachedRefineTemp = -1;
		private int cachedRefineMax = 10000;
		private readonly Dictionary<string, int> cachedStats = new Dictionary<string, int>();
		private readonly List<string> recentLogTimes = new List<string>();
		private readonly List<string> recentLogTexts = new List<string>();
		private readonly List<Color> recentLogColors = new List<Color>();
		private readonly List<string> logHistoryLines = new List<string>();
		private readonly List<Color> logHistoryColors = new List<Color>();

		// 跨布局共用的可选视图引用（在 RebuildSidebar 时清零并重建）
		private readonly Dictionary<string, Label> statValueLabels = new Dictionary<string, Label>();
		private GameTopTab topTabTasks;
		private GameTopTab topTabCamp;
		private GameTopTab topTabBottomStatus;
		private GameTopTab topTabBottomStats;
		private GameTopTab topTabBottomLog;

		// 方案 A 专用扩展控件
		private Label chipPicks;
		private Label chipGolem;
		private Label chipDepth;
		private Control panelRecentTitle;
		private Control panelRecentBox;
		private readonly Label[] recentTimeLabels = new Label[3];
		private readonly Label[] recentMsgLabels = new Label[3];

		// 方案 B 专用控件（顶部羊皮纸看板 + 缩略状态卡）
		private Label lblHeroDailySummary;
		private GameProgressBar heroBarPicks;
		private Label lblHeroPicksVal;
		private GameProgressBar heroBarRefine;
		private Label lblHeroRefineVal;
		private GameProgressBar heroBarMine;
		private Label lblHeroMineVal;
		private Label lblHeroSchedName;
		private Label lblHeroSchedTime;
		private Label lblMiniWorkshop;
		private Label lblMiniMine;

		// 方案 C 专用控件（任务清单逐行详情 + 紧凑状态栏）
		private Label lblRowGuild;
		private Label lblRowEarth;
		private Label lblRowPush;
		private Label lblRowWorkshop;
		private Label lblRowMine;
		private Label lblRowRune;
		private Label lblRowDestiny;
		private Label lblRowOre;
		private Label lblStatusSchedShort;
		private TableLayoutPanel listTasksTable;
		private GameSidePanel activeSidePanel;
		private bool adjustingResponsiveLayout;
		private Panel viewportPanel;
		private Panel viewportBar;
		private Label lblViewportTitle;
		private Timer bgRuntimeTimer;
		private bool bgRuntimeUpdating;
		private DesktopWallpaperCapture desktopCapture;
		private bool passthroughEventsHooked;
		private bool unifiedResizeHooked;
		private int passthroughStageCounter;
		private bool stagePushBusy;

		internal void SwitchUiAppearance(int layoutMode, int themeMode, string bgPath, int bgOpacity)
		{
			SwitchUiAppearance(layoutMode, themeMode, bgPath, bgOpacity, UiTheme.CustomBackgroundMode, UiTheme.GlassBlur, UiTheme.CustomBackgroundFit);
		}

		internal void SwitchUiAppearance(int layoutMode, int themeMode, string bgPath, int bgOpacity, int bgMode)
		{
			SwitchUiAppearance(layoutMode, themeMode, bgPath, bgOpacity, bgMode, UiTheme.GlassBlur, UiTheme.CustomBackgroundFit);
		}

		internal void SwitchUiAppearance(int layoutMode, int themeMode, string bgPath, int bgOpacity, int bgMode, int glassBlur)
		{
			SwitchUiAppearance(layoutMode, themeMode, bgPath, bgOpacity, bgMode, glassBlur, UiTheme.CustomBackgroundFit);
		}

		internal void SwitchUiAppearance(int layoutMode, int themeMode, string bgPath, int bgOpacity, int bgMode, int glassBlur, int bgFit)
		{
			if (themeMode < 0 || themeMode > 4) themeMode = 0;
			int effectiveTheme = extremePerformanceMode ? UiTheme.ThemeNativeLight : themeMode;
			string effectiveBgPath = extremePerformanceMode ? "" : bgPath;
			int effectiveBgMode = extremePerformanceMode ? UiTheme.BgModeStatic : bgMode;
			int effectiveBlur = extremePerformanceMode ? 0 : Math.Max(0, Math.Min(24, glassBlur));
			bool wasDark = !UiTheme.IsLightTheme;
			UiTheme.CurrentTheme = effectiveTheme;
			UiTheme.GlassBlur = effectiveBlur;
			UiTheme.CustomBackgroundFit = Math.Max(0, Math.Min(1, bgFit));
			UiTheme.LoadCustomBackground(effectiveBgPath, bgOpacity, effectiveBgMode);
			if (!extremePerformanceMode) UiTheme.SyncCroppedWallpaperFile(DataRoot());
			if (!UiTheme.IsLightTheme != wasDark) RemapHistoryColorsForTheme();

			bool unifiedBackdrop = !extremePerformanceMode && (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass || UiTheme.CustomBackground != null
				|| effectiveBgMode == UiTheme.BgModeEngine || sharedWallpaper != null);
			BackColor = BgColor;
			ForeColor = TextColor;
			Font = UiTheme.GameFont(9f, FontStyle.Regular);
			if (sidebarScroll != null && !sidebarScroll.IsDisposed) sidebarScroll.BackColor = SideColor;
			if (viewportPanel != null && !viewportPanel.IsDisposed)
			{
				viewportPanel.BackColor = BgColor;
				viewportPanel.Padding = new Padding(unifiedBackdrop ? 0 : 3, 0, 0, 0);
			}
			if (viewportBar != null && !viewportBar.IsDisposed)
			{
				viewportBar.BackColor = SideColor;
                foreach (Control item in viewportBar.Controls)
                    if (item is SmoothLabel && item.Font.FontFamily.Name != Font.FontFamily.Name)
                        item.Font = Font;
				viewportBar.Invalidate(true);
			}
			if (lblViewportTitle != null && !lblViewportTitle.IsDisposed)
			{
				lblViewportTitle.ForeColor = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass
					? LiquidGlassEngine.GlassInk(true)
					: (unifiedBackdrop
						? Color.White
						: (UiTheme.CurrentTheme == UiTheme.ThemeParchment
							? Color.FromArgb(255, 238, 178)
							: (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood ? Color.FromArgb(255, 228, 143) : GoldColor)));
			}
			WindowStyle.ApplyThemeChrome(this);
			SwitchUiLayout(layoutMode);
			ConfigureBackgroundRuntime();
		}

		private async void OnUnifiedResize(object sender, EventArgs e)
		{
			if (IsDisposed || previewMode || extremePerformanceMode) return;
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass || UiTheme.CustomBackground != null || UseNativeStageBackdrop)
			{
				InvalidateSidebarBackdrop();
				if (viewportPanel != null) viewportPanel.Invalidate();
				if (coreReady) await ApplyStageWallpaperAsync();
			}
		}

		private void ConfigureBackgroundRuntime()
		{
			if (!unifiedResizeHooked)
			{
				unifiedResizeHooked = true;
				base.SizeChanged += OnUnifiedResize;
				base.VisibleChanged += ResumeIndependentWallpaper;
				base.Resize += ResumeIndependentWallpaper;
			}
			if (previewMode) return;
			if (extremePerformanceMode)
			{
				StopIndependentWallpaper();
				activePlaybackProject = "";
				playbackFailed = false;
				playbackStatus = "";
				if (bgRuntimeTimer != null) bgRuntimeTimer.Stop();
				if (desktopCapture != null)
				{
					desktopCapture.Dispose();
					desktopCapture = null;
				}
				UiTheme.ClearLiveDesktopFrame();
				UiTheme.ReleasePerformanceCaches();
				return;
			}
			if (sharedWallpaper != null)
			{
				if (bgRuntimeTimer != null) bgRuntimeTimer.Stop();
				return;
			}
			int mode = UiTheme.CustomBackgroundMode;
			if (mode == UiTheme.BgModeEngine)
			{
				if (desktopCapture != null) { desktopCapture.Dispose(); desktopCapture = null; }
				ConfigureIndependentWallpaper();
				return;
			}
			StopIndependentWallpaper();
			activePlaybackProject = "";
			playbackFailed = false;
			playbackStatus = "";
			if (mode == UiTheme.BgModeDesktopPassthrough)
			{
				if (desktopCapture == null) desktopCapture = new DesktopWallpaperCapture();
				if (!passthroughEventsHooked)
				{
					passthroughEventsHooked = true;
					base.Move += delegate { if (UiTheme.CustomBackgroundMode == UiTheme.BgModeDesktopPassthrough) CapturePassthroughTick(); };
					base.Resize += delegate { if (UiTheme.CustomBackgroundMode == UiTheme.BgModeDesktopPassthrough) CapturePassthroughTick(); };
				}
				EnsureBgRuntimeTimer(100);
				CapturePassthroughTick();
			}
			else if (UiTheme.HasAnimatedBackground)
			{
				if (desktopCapture != null) { desktopCapture.Dispose(); desktopCapture = null; }
				EnsureBgRuntimeTimer(UiTheme.AnimationFrameDelay);
			}
			else
			{
				if (bgRuntimeTimer != null) bgRuntimeTimer.Stop();
				if (desktopCapture != null) { desktopCapture.Dispose(); desktopCapture = null; }
			}
		}

		private void EnsureBgRuntimeTimer(int intervalMs)
		{
			if (bgRuntimeTimer == null)
			{
				bgRuntimeTimer = new Timer();
				bgRuntimeTimer.Tick += BgRuntimeTimer_Tick;
			}
			bgRuntimeTimer.Interval = Math.Max(16, intervalMs);
			if (!bgRuntimeTimer.Enabled) bgRuntimeTimer.Start();
		}

		private void BgRuntimeTimer_Tick(object sender, EventArgs e)
		{
			if (bgRuntimeUpdating || IsDisposed || previewMode || sharedWallpaper != null || extremePerformanceMode) return;
			if (!Visible || WindowState == FormWindowState.Minimized) return;
			if (UiTheme.CustomBackgroundMode == UiTheme.BgModeEngine)
			{
				CaptureIndependentWallpaper();
				return;
			}
			bgRuntimeUpdating = true;
			try
			{
				if (UiTheme.CustomBackgroundMode == UiTheme.BgModeDesktopPassthrough)
				{
					CapturePassthroughTick();
				}
				else if (UiTheme.HasAnimatedBackground)
				{
					int nextDelay = UiTheme.AdvanceAnimationFrame();
					if (bgRuntimeTimer != null && bgRuntimeTimer.Interval != Math.Max(16, nextDelay))
						bgRuntimeTimer.Interval = Math.Max(16, nextDelay);
					InvalidateSidebarBackdrop();
					if (UseNativeStageBackdrop && viewportPanel != null) viewportPanel.Invalidate();
				}
			}
			finally
			{
				bgRuntimeUpdating = false;
			}
		}

		private void CapturePassthroughTick()
		{
			if (IsDisposed || !IsHandleCreated || !Visible || sharedWallpaper != null) return;
			if (desktopCapture == null) return;
			Rectangle formScreenRect = RectangleToScreen(ClientRectangle);
			if (formScreenRect.Width < 16 || formScreenRect.Height < 16) return;
			Bitmap frame = desktopCapture.CaptureSidebarRegion(formScreenRect);
			if (frame != null)
			{
				UiTheme.SetLiveDesktopFrame(frame);
				InvalidateSidebarBackdrop();
			}
			if (coreReady && !previewMode && (!options.ContainsKey("stageWallpaperBg") || options["stageWallpaperBg"]))
			{
				if ((passthroughStageCounter++ % 2) == 0 && !stagePushBusy && webView != null && !webView.IsDisposed && webView.IsHandleCreated)
				{
					Rectangle webRect = webView.RectangleToScreen(webView.ClientRectangle);
					string dataUri = desktopCapture.CaptureRegionJpegDataUri(webRect, UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? 1440 : 640);
					if (!string.IsNullOrEmpty(dataUri)) PushStageLiveFrameAsync(dataUri);
				}
			}
		}

		private async void PushStageLiveFrameAsync(string dataUri)
		{
			if (stagePushBusy || IsDisposed) return;
			stagePushBusy = true;
			try { await ApplyStageWallpaperAsync(dataUri); }
			finally { stagePushBusy = false; }
		}

		private void InvalidateSidebarBackdrop()
		{
			if (activeSidePanel != null && !activeSidePanel.IsDisposed)
			{
				activeSidePanel.Invalidate(true);
			}
			else if (sidebarScroll != null && !sidebarScroll.IsDisposed)
			{
				sidebarScroll.Invalidate(true);
			}
			if (viewportBar != null && !viewportBar.IsDisposed) viewportBar.Invalidate(true);
		}

		private void DisposeBackgroundRuntime()
		{
			StopIndependentWallpaper();
			UiTheme.SetEngineFrame(null);
			if (bgRuntimeTimer != null)
			{
				bgRuntimeTimer.Stop();
				bgRuntimeTimer.Dispose();
				bgRuntimeTimer = null;
			}
			if (desktopCapture != null)
			{
				desktopCapture.Dispose();
				desktopCapture = null;
			}
			UiTheme.ClearLiveDesktopFrame();
		}

		private void RemapHistoryColorsForTheme()
		{
			cachedStatusColor = RemapSemanticColor(cachedStatusColor);
			cachedBadgeColor = RemapSemanticColor(cachedBadgeColor);
			for (int i = 0; i < logHistoryColors.Count; i++) logHistoryColors[i] = RemapSemanticColor(logHistoryColors[i]);
			for (int i = 0; i < recentLogColors.Count; i++) recentLogColors[i] = RemapSemanticColor(recentLogColors[i]);
		}

		private static Color RemapSemanticColor(Color c)
		{
			double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
			if (UiTheme.IsLightTheme)
			{
				if (lum <= 145) return c;
				if (c.R >= c.G + 35 && c.R >= c.B + 25) return RedColor;
				if (c.G >= c.R + 18 && c.G >= c.B + 15) return GreenColor;
				if (c.B >= c.R + 20) return BlueColor;
				if (c.R >= 190 && c.G >= 165) return YellowColor;
				return TextColor;
			}
			if (lum >= 120) return c;
			if (c.R >= c.G + 35 && c.R >= c.B + 35) return RedColor;
			if (c.G >= c.R + 15 && c.G >= c.B + 15) return GreenColor;
			if (c.B >= c.R + 20) return BlueColor;
			if (c.R >= 110 && c.G >= 70 && c.B <= 40) return YellowColor;
			return GoldColor;
		}

		internal static string UiLayoutName(int mode)
		{
			switch (mode)
			{
			case UiLayoutGrid: return "方案 A · 规整网格";
			case UiLayoutStatus: return "方案 B · 状态优先";
			case UiLayoutList: return "方案 C · 任务清单";
			default: return "经典分层";
			}
		}

		private void ClearSidebarDynamicRefs()
		{
			if (hints != null) hints.RemoveAll();
			optionChecks.Clear();
			statLabels.Clear();
			statValueLabels.Clear();
			dropGridLabels.Clear();
			lastDropKey = null;
			collapseSetters.Clear();

			chkArenaRefresh = null;
			numArenaCount = null;
			btnMain = null;
			btnDaily = null;
			btnArena = null;
			btnGuildBoss = null;
			btnEarth = null;
			btnPush = null;
			btnWorkshop = null;
			btnMine = null;
			btnRune = null;
			btnDestiny = null;
			btnOrePacks = null;
			btnDailyOptions = null;
			btnWorkshopOptions = null;
			btnGrowthOptions = null;
			btnMineResearch = null;
			btnSchedule = null;
			btnAdvanced = null;
			btnChest = null;
			campGridHost = null;
			pageTasks = null;
			pageCamp = null;
			tabTasks = null;
			tabCamp = null;
			segMainTabs = null;
			segBottomTabs = null;
			topTabTasks = null;
			topTabCamp = null;
			tabBottomStatus = null;
			tabBottomStats = null;
			tabBottomLog = null;
			topTabBottomStatus = null;
			topTabBottomStats = null;
			topTabBottomLog = null;
			btnBottomReview = null;
			btnBottomClearLog = null;
			panelBottomStatus = null;
			panelBottomStats = null;
			panelBottomLog = null;
			lblStatus = null;
			lblDetail = null;
			lblMining = null;
			mineRouteLabel = null;
			lblMinePlan = null;
			lblMineSlots = null;
			lblStatusSchedule = null;
			lblSumMining = null;
			lblSumDaily = null;
			lblSumWorkshop = null;
			lblSumSchedule = null;
			lblMiningStatsTitle = null;
			lblMiningStatsSpend = null;
			dropGridHost = null;
			dropGrid = null;
			rtbReport = null;
			lblReportPage = null;
			btnReportPrev = null;
			btnReportNext = null;
			txtLog = null;

			chipPicks = chipGolem = chipDepth = null;
			panelRecentTitle = panelRecentBox = null;
			for (int i = 0; i < recentTimeLabels.Length; i++) { recentTimeLabels[i] = null; recentMsgLabels[i] = null; }

			lblHeroDailySummary = lblHeroPicksVal = lblHeroRefineVal = lblHeroMineVal = null;
			lblHeroSchedName = lblHeroSchedTime = lblMiniWorkshop = lblMiniMine = null;
			heroBarPicks = heroBarRefine = heroBarMine = null;

			lblRowGuild = lblRowEarth = lblRowPush = lblRowWorkshop = lblRowMine = null;
			lblRowRune = lblRowDestiny = lblRowOre = lblStatusSchedShort = null;
			listTasksTable = null;
			activeSidePanel = null;
		}

		internal void SwitchUiLayout(int mode)
		{
			if (mode < 0 || mode > 3) mode = 0;
			activeUiLayout = mode;
			numberOptions["uiLayout"] = mode;
			if (sidebarScroll == null || sidebarScroll.IsDisposed) return;

			if (numArenaCount != null && !numArenaCount.IsDisposed) savedArenaCount = numArenaCount.Value;

			UiSurface.Batch(sidebarScroll, delegate
			{
				ClearSidebarDynamicRefs();
				while (sidebarScroll.Controls.Count > 0)
				{
					Control old = sidebarScroll.Controls[0];
					sidebarScroll.Controls.RemoveAt(0);
					old.Dispose();
				}
				BuildSidebarContents();
				ReplayLogHistoryToRtb();
				if (numArenaCount != null) numArenaCount.Value = savedArenaCount;
				RefreshOptionChecks();
				RefreshTaskControls();
				UpdateSettingsSummary();
				ApplyCachedStateToUi();
				RenderMiningStats();
				UpdateMainButton(lastRunning);
				UpdateResponsiveHeights();
			});
		}

		private void SetStatusUi(string statusText, Color logicalColor, string detailText)
		{
			if (statusText != null)
			{
				cachedStatusText = statusText;
				cachedStatusColor = logicalColor;
			}
			if (detailText != null) cachedDetailText = detailText;

			if (lblStatus != null)
			{
				lblStatus.Text = cachedStatusText;
				lblStatus.ForeColor = MapStatusForeColor(cachedStatusColor);
			}
			if (lblDetail != null)
			{
				lblDetail.Text = cachedDetailText;
				hints.SetToolTip(lblDetail, cachedDetailText);
			}
		}

		private Color MapStatusForeColor(Color logicalColor)
		{
			if (activeUiLayout != UiLayoutStatus) return RemapSemanticColor(logicalColor);
			if (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) return RemapSemanticColor(logicalColor);
			if (logicalColor == RedColor) return Color.FromArgb(164, 34, 18);
			if (logicalColor == GreenColor) return Color.FromArgb(38, 112, 24);
			if (logicalColor == YellowColor) return Color.FromArgb(156, 86, 12);
			return Color.FromArgb(90, 45, 12);
		}

		private void ApplyCachedStateToUi()
		{
			SetStatusUi(cachedStatusText, cachedStatusColor, cachedDetailText);
			SetBadge(cachedBadgeText, cachedBadgeColor);
			if (lblMining != null) lblMining.Text = cachedMiningText;
			if (lblMinePlan != null) lblMinePlan.Text = cachedMinePlan;
			if (lblMineSlots != null) lblMineSlots.Text = cachedMineSlots;
			UpdateStats(null, true);
			UpdateLayoutDynamicViews(null);
			UpdateRecentLogView();
		}

		private void RecordLogHistoryLine(string formattedLine, Color color)
		{
			logHistoryLines.Add(formattedLine);
			logHistoryColors.Add(color);
			int limit = LogHistoryLimit;
			while (logHistoryLines.Count > limit)
			{
				logHistoryLines.RemoveAt(0);
				logHistoryColors.RemoveAt(0);
			}
		}

		private void ReplayLogHistoryToRtb()
		{
			if (txtLog == null || txtLog.IsDisposed || logHistoryLines.Count == 0) return;
			if (extremePerformanceMode)
			{
				if (ShouldDeferLogUi())
				{
					logUiDirty = true;
					return;
				}
				var sb = new StringBuilder();
				for (int i = 0; i < logHistoryLines.Count; i++) sb.Append(logHistoryLines[i]);
				string text = sb.ToString();
				if (text.Length > 12000)
				{
					int cut = text.Length - 6000;
					int nl = text.IndexOf('\n', cut);
					if (nl > cut && nl < text.Length) cut = nl + 1;
					text = text.Substring(cut);
				}
				txtLog.SelectionStart = txtLog.TextLength;
				txtLog.SelectionLength = 0;
				txtLog.AppendText(text);
				txtLog.SelectionStart = txtLog.TextLength;
				txtLog.ScrollToCaret();
				logUiDirty = false;
				return;
			}
			for (int i = 0; i < logHistoryLines.Count; i++)
			{
				txtLog.SelectionStart = txtLog.TextLength;
				txtLog.SelectionLength = 0;
				txtLog.SelectionColor = logHistoryColors[i];
				txtLog.AppendText(logHistoryLines[i]);
			}
			txtLog.SelectionStart = txtLog.TextLength;
			txtLog.ScrollToCaret();
		}

		private void RecordRecentLogEntry(string text, Color color)
		{
			string tm = DateTime.Now.ToString("HH:mm");
			recentLogTimes.Insert(0, tm);
			recentLogTexts.Insert(0, text ?? "");
			recentLogColors.Insert(0, color);
			while (recentLogTimes.Count > 3)
			{
				int last = recentLogTimes.Count - 1;
				recentLogTimes.RemoveAt(last);
				recentLogTexts.RemoveAt(last);
				recentLogColors.RemoveAt(last);
			}
		}

		private void UpdateRecentLogView()
		{
			for (int i = 0; i < recentMsgLabels.Length; i++)
			{
				if (recentTimeLabels[i] == null || recentMsgLabels[i] == null) continue;
				if (i < recentLogTexts.Count)
				{
					recentTimeLabels[i].Text = recentLogTimes[i];
					recentMsgLabels[i].Text = recentLogTexts[i];
					recentMsgLabels[i].ForeColor = recentLogColors[i];
				}
				else
				{
					recentTimeLabels[i].Text = "--:--";
					recentMsgLabels[i].Text = "等待操作记录";
					recentMsgLabels[i].ForeColor = MutedColor;
				}
			}
		}

		private void UpdateResponsiveHeights()
		{
			if (adjustingResponsiveLayout || activeSidePanel == null || sidebarScroll == null || sidebarScroll.IsDisposed) return;
			int h = sidebarScroll.ClientSize.Height;
			if (h < 200) return;
			adjustingResponsiveLayout = true;
			try
			{
#if TEST_PVP_EDITION
				if (!testPvpShowLegacy)
				{
					int pvpMinH = 680;
					bool pvpScroll = h < pvpMinH;
					if (pvpScroll != sidebarScroll.AutoScroll)
					{
						if (!pvpScroll) sidebarScroll.AutoScrollPosition = Point.Empty;
						sidebarScroll.AutoScroll = pvpScroll;
					}
					activeSidePanel.Dock = pvpScroll ? DockStyle.Top : DockStyle.Fill;
					if (pvpScroll && activeSidePanel.Height != pvpMinH) activeSidePanel.Height = pvpMinH;
					return;
				}
#endif
				bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
				int minimumHeight = 0;
				if (glass)
				{
					switch (activeUiLayout)
					{
					case UiLayoutGrid: minimumHeight = 680; break;
					case UiLayoutStatus: minimumHeight = 740; break;
					case UiLayoutList: minimumHeight = 725; break;
					}
				}
				bool scroll = h < minimumHeight;
				if (scroll != sidebarScroll.AutoScroll)
				{
					if (!scroll) sidebarScroll.AutoScrollPosition = Point.Empty;
					sidebarScroll.AutoScroll = scroll;
				}
				activeSidePanel.Dock = scroll ? DockStyle.Top : DockStyle.Fill;
				if (scroll && activeSidePanel.Height != minimumHeight) activeSidePanel.Height = minimumHeight;

				if (activeUiLayout == UiLayoutGrid)
				{
					bool tall = h >= 780;
					if (panelRecentTitle != null) panelRecentTitle.Visible = tall;
					if (panelRecentBox != null) panelRecentBox.Visible = tall;
				}
				else if (activeUiLayout == UiLayoutList && activeSidePanel.RowStyles.Count >= 4 && listTasksTable != null)
				{
					bool compact = h < 775;
					float desiredMid = ListTasksSectionHeight(compact, glass);
					if (Math.Abs(activeSidePanel.RowStyles[2].Height - desiredMid) > 1f)
					{
						activeSidePanel.SuspendLayout();
						activeSidePanel.RowStyles[2].Height = desiredMid;
						float hdrH = compact ? 18f : 22f;
						float rowH = ListTaskRowHeight(compact, glass);
						int btnW = compact ? 72 : 78;
						int btnH = compact ? 24 : 26;
						int[] headerRows = { 0, 5, 8 };
						for (int r = 0; r < listTasksTable.RowStyles.Count; r++)
						{
							bool isHdr = (r == headerRows[0] || r == headerRows[1] || r == headerRows[2]);
							listTasksTable.RowStyles[r].Height = isHdr ? hdrH : rowH;
						}
						foreach (Control rowPanel in listTasksTable.Controls)
						{
							foreach (Control child in rowPanel.Controls)
							{
								var gb = child as GameButton;
								if (gb != null && !glass) gb.Size = new Size(btnW, btnH);
							}
						}
						activeSidePanel.ResumeLayout(true);
					}
				}
			}
			finally
			{
				adjustingResponsiveLayout = false;
			}
		}

		// =====================================================================
		// 方案 A · 规整网格
		// =====================================================================
		private GameSidePanel BuildSidebarLayoutA()
		{
			var side = new GameSidePanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(10, 6, 10, 8),
				Margin = Padding.Empty
			};
			side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f + BattlePlaybackRowHeight));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? 296f : 280f));
			side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			sidebarLayout = side;
			activeSidePanel = side;

			side.Controls.Add(BuildHeader(), 0, 0);
			side.Controls.Add(BuildPrimaryActionsWithPlayback(BuildBoxedPrimaryCard(54, 162)), 0, 1);

			// 中部：GameTopTab + PixelBoxPanel
			pageTasks = BuildGridTasksBodyA();
			pageCamp = BuildCampPageBoxed(108);
			GameTopTab unusedTab;
			var midDock = BuildTopTabbedSection(
				new[] { "任务", "设置" },
				new[] { "tasks", "camp" },
				new[] { pageTasks, pageCamp },
				true,
				null,
				out topTabTasks, out topTabCamp, out unusedTab);
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) midDock.Margin = new Padding(0, 0, 0, 6);
			side.Controls.Add(midDock, 0, 2);

			// 底部：营地状态 / 挖矿统计 / 冒险日志 + 任务记录/清空日志
			panelBottomStatus = BuildStatusBodyA();
			panelBottomStats = BuildMiningStatsBody();
			panelBottomLog = BuildLogsBody();
			var rightHost = BuildBottomRightButtons(26, 78);
			var botDock = BuildTopTabbedSection(
				new[] { "营地状态", "挖矿统计", "冒险日志" },
				new[] { "status", "stats", "log" },
				new[] { panelBottomStatus, panelBottomStats, panelBottomLog },
				false,
				rightHost,
				out topTabBottomStatus, out topTabBottomStats, out topTabBottomLog);
			side.Controls.Add(botDock, 0, 3);

			SwitchTab(currentTab);
			SwitchBottomTab(currentBottomTab);
			return side;
		}

		private Control BuildBoxedPrimaryCard(int btnH, int btnW)
		{
			var box = new PixelBoxPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 2, 0, 4) };
			var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			btnMain = new GameButton { Size = new Size(btnW, btnH), Anchor = AnchorStyles.None, Text = "▶ 开始挖矿", Skin = GameButtonSkin.Green, Enabled = false };
			btnMain.Click += BtnMain_Click;
			hints.SetToolTip(btnMain, "自动挖掘；先处理魔像与宝箱，再向下探索。再次点击暂停。");
			btnDaily = new GameButton { Size = new Size(btnW, btnH), Anchor = AnchorStyles.None, Text = "一键日常", Skin = GameButtonSkin.Gold, Enabled = false };
			FitGlassAction(btnMain);
			FitGlassAction(btnDaily);
			btnDaily.Click += BtnDaily_Click;
			hints.SetToolTip(btnDaily, "按日常清单执行，购买次数按实际余额处理。");
			row.Controls.Add(btnMain, 0, 0);
			row.Controls.Add(btnDaily, 1, 0);
			box.Controls.Add(row);
			return box;
		}

		private Control BuildGridTasksBodyA()
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Margin = Padding.Empty };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

			layout.Controls.Add(new GameHeader { Title = "战斗委托", Hint = "", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2) }, 0, 0);

			var g1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 4) };
			for (int i = 0; i < 3; i++) g1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			g1.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			g1.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

			btnArena = GridButton("竞技场", BtnArena_Click, GameButtonSkin.Orange, 132, 44);
			btnGuildBoss = GridButton("工会首领", BtnGuildBoss_Click, GameButtonSkin.Orange, 132, 44);
			btnEarth = GridButton("矿区首领", BtnEarth_Click, GameButtonSkin.Orange, 132, 44);
			btnPush = GridButton("自动推关", BtnPush_Click, GameButtonSkin.Orange, 132, 44);
			SetTaskTooltips();

			var countCell = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(3, 2, 3, 2), Padding = new Padding(8, 0, 8, 0) };
			var countRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			countRow.Controls.Add(new SmoothLabel
			{
				Text = "轮次",
				Dock = DockStyle.Left,
				Width = 42,
				ForeColor = MutedColor,
				BackColor = Color.Transparent,
				Font = UiTheme.GameFont(8.25f),
				TextAlign = ContentAlignment.MiddleLeft
			});
			numArenaCount = new GameNumber
			{
				Minimum = 1,
				Maximum = 99,
				Value = savedArenaCount,
				Size = new Size(64, 24),
				Anchor = AnchorStyles.Right,
				Font = UiTheme.GameFont(9f, FontStyle.Bold)
			};
			numArenaCount.Location = new Point(Math.Max(46, countCell.Width - 74), 8);
			countRow.Resize += delegate { numArenaCount.Location = new Point(Math.Max(44, countRow.Width - numArenaCount.Width), Math.Max(1, (countRow.Height - numArenaCount.Height) / 2)); };
			countRow.Controls.Add(numArenaCount);
			countCell.Controls.Add(countRow);

			var chkCell = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(3, 2, 3, 2), Padding = new Padding(8, 0, 6, 0) };
			chkArenaRefresh = new GameCheckBox
			{
				Text = "血钻刷新",
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				BackColor = Color.Transparent,
				ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : GoldColor,
				Font = UiTheme.GameFont(8.25f),
				Checked = options.ContainsKey("arenaAllowBloodRefresh") && options["arenaAllowBloodRefresh"],
				Tag = "arenaAllowBloodRefresh"
			};
			chkArenaRefresh.CheckedChanged += Option_CheckedChanged;
			optionChecks["arenaAllowBloodRefresh"] = chkArenaRefresh;
			hints.SetToolTip(chkArenaRefresh, "每轮依次挑战九名对手；结束后先免费刷新，再使用血钻刷新。");
			chkCell.Controls.Add(chkArenaRefresh);

			g1.Controls.Add(btnArena, 0, 0);
			g1.Controls.Add(btnGuildBoss, 1, 0);
			g1.Controls.Add(btnEarth, 2, 0);
			g1.Controls.Add(btnPush, 0, 1);
			g1.Controls.Add(countCell, 1, 1);
			g1.Controls.Add(chkCell, 2, 1);
			layout.Controls.Add(g1, 0, 1);

			layout.Controls.Add(new GameHeader { Title = "生产与养成", Hint = "", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2) }, 0, 2);

			var g2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
			for (int i = 0; i < 3; i++) g2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			g2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			g2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

			btnWorkshop = GridButton("工坊", BtnWorkshop_Click, GameButtonSkin.Gold, 132, 44);
			btnMine = GridButton("矿山采集", BtnMine_Click, GameButtonSkin.Gold, 132, 44);
			btnOrePacks = GridButton("使用矿石包", BtnOrePacks_Click, GameButtonSkin.Green, 132, 44);
			btnRune = GridButton("符文连抽", BtnRune_Click, GameButtonSkin.Green, 132, 44);
			btnDestiny = GridButton("宿命强化", BtnDestiny_Click, GameButtonSkin.Green, 132, 44);
			SetTaskTooltips();

			g2.Controls.Add(btnWorkshop, 0, 0);
			g2.Controls.Add(btnMine, 1, 0);
			g2.Controls.Add(btnOrePacks, 2, 0);
			g2.Controls.Add(btnRune, 0, 1);
			g2.Controls.Add(btnDestiny, 1, 1);
			layout.Controls.Add(g2, 0, 3);

			return layout;
		}

		private Control BuildStatusBodyA()
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = Color.Transparent, Margin = Padding.Empty };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			// Row 0: title + detail side by side
			var titleRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			lblStatus = new SmoothLabel
			{
				Text = "等待加载",
				Dock = DockStyle.Left,
				Width = 148,
				ForeColor = GoldColor,
				BackColor = Color.Transparent,
				Font = UiTheme.GameFont(11.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			};
			lblDetail = new SmoothLabel
			{
				Text = "登录后选择任务",
				Dock = DockStyle.Fill,
				ForeColor = MutedColor,
				BackColor = Color.Transparent,
				Font = UiTheme.GameFont(8f),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			};
			titleRow.Controls.Add(lblDetail);
			titleRow.Controls.Add(lblStatus);
			layout.Controls.Add(titleRow, 0, 0);

			// Row 1: 3 chips (矿镐 / 魔像 / 深度)
			var chips = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 1, 0, 2) };
			for (int i = 0; i < 3; i++) chips.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			chips.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			chipPicks = MakeChipCell(chips, "矿镐 --/--", 0);
			chipGolem = MakeChipCell(chips, "魔像 --", 1);
			chipDepth = MakeChipCell(chips, "深度 --", 2);
			layout.Controls.Add(chips, 0, 1);

			// Row 2: 3x2 KPI tiles
			layout.Controls.Add(BuildKpiTiles(true), 0, 2);

			// Row 3-4: 矿山信息
			layout.Controls.Add(SectionCaption("◆ 矿山"), 0, 3);
			var mineBox = new InsetPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 1, 0, 2) };
			var mineStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
			mineStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			for (int i = 0; i < 3; i++) mineStack.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
			lblMinePlan = LineInfoLabel("推荐矿层：等待游戏数据", GoldColor);
			lblMineSlots = LineInfoLabel("矿队：等待矿山数据", (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? BlueColor : Color.FromArgb(198, 214, 178));
			lblStatusSchedule = LineInfoLabel("自动续跑与签到：等待读取", MutedColor);
			mineStack.Controls.Add(lblMinePlan, 0, 0);
			mineStack.Controls.Add(lblMineSlots, 0, 1);
			mineStack.Controls.Add(lblStatusSchedule, 0, 2);
			mineBox.Controls.Add(mineStack);
			layout.Controls.Add(mineBox, 0, 4);

			// Row 5-6: 最近动态 (automatically hidden below 780 height)
			panelRecentTitle = SectionCaption("◆ 最近动态");
			layout.Controls.Add(panelRecentTitle, 0, 5);

			var recBox = new InsetPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 1, 0, 0) };
			var recTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
			recTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));
			recTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			for (int i = 0; i < 3; i++)
			{
				recTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
				recentTimeLabels[i] = new SmoothLabel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = MutedColor, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft };
				recentMsgLabels[i] = new SmoothLabel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = TextColor, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
				recTable.Controls.Add(recentTimeLabels[i], 0, i);
				recTable.Controls.Add(recentMsgLabels[i], 1, i);
			}
			recBox.Controls.Add(recTable);
			panelRecentBox = recBox;
			layout.Controls.Add(recBox, 0, 6);

			return layout;
		}

		// =====================================================================
		// 方案 B · 状态优先
		// =====================================================================
		private GameSidePanel BuildSidebarLayoutB()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var side = new GameSidePanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 5,
				Padding = new Padding(10, 6, 10, 8),
				Margin = Padding.Empty
			};
			side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 148f : 146f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, (glass ? 64f : 58f) + BattlePlaybackRowHeight));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 234f : 210f));
			side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			sidebarLayout = side;
			activeSidePanel = side;

			side.Controls.Add(BuildHeader(), 0, 0);
			side.Controls.Add(BuildHeroParchmentCard(), 0, 1);

			// Primary Row without box wrapper (symmetric 3:1)
			var priRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = glass ? new Padding(0, 4, 0, 4) : new Padding(0, 2, 0, 2), BackColor = Color.Transparent };
			priRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			priRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			priRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			btnMain = new GameButton { Size = new Size(glass ? 186 : 156, glass ? 48 : 52), Anchor = AnchorStyles.None, Text = "▶ 开始挖矿", Skin = GameButtonSkin.Green, Enabled = false };
			btnMain.Click += BtnMain_Click;
			btnDaily = new GameButton { Size = new Size(glass ? 186 : 156, glass ? 48 : 52), Anchor = AnchorStyles.None, Text = "一键日常", Skin = GameButtonSkin.Gold, Enabled = false };
			FitGlassAction(btnMain);
			FitGlassAction(btnDaily);
			btnDaily.Click += BtnDaily_Click;
			hints.SetToolTip(btnMain, "自动挖掘；先处理魔像与宝箱，再向下探索。再次点击暂停。");
			hints.SetToolTip(btnDaily, "按日常清单执行，购买次数按实际余额处理。");
			priRow.Controls.Add(btnMain, 0, 0);
			priRow.Controls.Add(btnDaily, 1, 0);
			side.Controls.Add(BuildPrimaryActionsWithPlayback(priRow), 0, 2);

			// Mid: GameTopTab (任务 / 设置)
			pageTasks = BuildCompactStripTasksB();
			pageCamp = BuildCampPageBoxed(74);
			GameTopTab unusedMidTab, unusedBotTab;
			var midDock = BuildTopTabbedSection(
				new[] { "任务", "设置" },
				new[] { "tasks", "camp" },
				new[] { pageTasks, pageCamp },
				true,
				null,
				out topTabTasks, out topTabCamp, out unusedMidTab);
			if (glass) midDock.Margin = new Padding(0, 0, 0, 6);
			side.Controls.Add(midDock, 0, 3);

			// Bottom: 冒险日志 + 挖矿统计 (stats strip embedded in log view)
			panelBottomStats = BuildMiningStatsBody();
			panelBottomLog = BuildLogWithTopStatsB();
			var rightHost = BuildBottomRightButtonsB();
			var botDock = BuildTopTabbedSection(
				new[] { "冒险日志", "挖矿统计" },
				new[] { "log", "stats" },
				new[] { panelBottomLog, panelBottomStats },
				false,
				rightHost,
				out topTabBottomLog, out topTabBottomStats, out unusedBotTab);
			side.Controls.Add(botDock, 0, 4);

			SwitchTab(currentTab);
			if (currentBottomTab == "status") currentBottomTab = "log";
			SwitchBottomTab(currentBottomTab);
			return side;
		}

		private Control BuildHeroParchmentCard()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var paper = new ParchmentPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 7), Margin = glass ? new Padding(0, 2, 0, 4) : new Padding(0, 2, 0, 2) };
			var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Color.Transparent, Margin = Padding.Empty };
			tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
			tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 17f));
			for (int i = 0; i < 4; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

			Color darkInk = (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? UiTheme.Palette.TextColor : Color.FromArgb(90, 45, 12);
			Color subInk = (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? UiTheme.Palette.MutedColor : Color.FromArgb(122, 90, 48);
			Color valInk = (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? UiTheme.Palette.TextColor : Color.FromArgb(61, 38, 16);

			var r0 = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			lblStatus = new SmoothLabel { Text = "营地待命", Dock = DockStyle.Left, Width = 230, BackColor = Color.Transparent, ForeColor = darkInk, Font = UiTheme.GameFont(12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
			lblHeroDailySummary = new SmoothLabel { Text = "今日日常 待同步", Dock = DockStyle.Right, Width = 180, BackColor = Color.Transparent, ForeColor = subInk, Font = UiTheme.GameFont(8f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
			r0.Controls.Add(lblStatus);
			r0.Controls.Add(lblHeroDailySummary);
			tbl.Controls.Add(r0, 0, 0);

			lblDetail = new SmoothLabel { Text = "选择一项任务，开始今天的冒险", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = subInk, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
			tbl.Controls.Add(lblDetail, 0, 1);

			heroBarPicks = MakeMeterRow(tbl, 2, "矿镐", Color.FromArgb(184, 236, 122), Color.FromArgb(121, 184, 60), subInk, valInk, out lblHeroPicksVal);
			heroBarRefine = MakeMeterRow(tbl, 3, "炼化过热", Color.FromArgb(255, 176, 106), Color.FromArgb(217, 98, 46), subInk, valInk, out lblHeroRefineVal);
			heroBarMine = MakeMeterRow(tbl, 4, "矿队", Color.FromArgb(255, 226, 122), Color.FromArgb(214, 165, 42), subInk, valInk, out lblHeroMineVal);

			var rSched = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			rSched.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
			rSched.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			rSched.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
			rSched.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			rSched.Controls.Add(new SmoothLabel { Text = "下次定时", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = subInk, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
			lblHeroSchedName = new SmoothLabel { Text = "无启用计划", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = valInk, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
			lblHeroSchedTime = new SmoothLabel { Text = "--:--", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = valInk, Font = UiTheme.GameFont(7.75f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
			rSched.Controls.Add(lblHeroSchedName, 1, 0);
			rSched.Controls.Add(lblHeroSchedTime, 2, 0);
			tbl.Controls.Add(rSched, 0, 5);

			paper.Controls.Add(tbl);
			return paper;
		}

		private static GameProgressBar MakeMeterRow(TableLayoutPanel parent, int row, string caption, Color top, Color bot, Color keyColor, Color valColor, out Label valLabel)
		{
			var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
			t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			t.Controls.Add(new SmoothLabel { Text = caption, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = keyColor, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
			var bar = new GameProgressBar { Dock = DockStyle.Fill, FillTop = top, FillBottom = bot, Margin = new Padding(2, 2, 2, 2) };
			t.Controls.Add(bar, 1, 0);
			valLabel = new SmoothLabel { Text = "--", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = valColor, Font = UiTheme.GameFont(7.75f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
			t.Controls.Add(valLabel, 2, 0);
			parent.Controls.Add(t, 0, row);
			return bar;
		}

		private Control BuildCompactStripTasksB()
		{
			float actionRowHeight = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? 40f : 36f;
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Margin = Padding.Empty };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, actionRowHeight));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, actionRowHeight));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, actionRowHeight));

			// Row 0: 战斗 tag + 4 orange buttons (93x31 -> 3:1)
			var r0 = CategoryRowB("战斗", UiTheme.IsLightTheme ? RedColor : Color.FromArgb(255, 176, 106));
			btnArena = GridButton("竞技场", BtnArena_Click, GameButtonSkin.Orange, 93, 31);
			btnGuildBoss = GridButton("工会首领", BtnGuildBoss_Click, GameButtonSkin.Orange, 93, 31);
			btnEarth = GridButton("矿区首领", BtnEarth_Click, GameButtonSkin.Orange, 93, 31);
			btnPush = GridButton("自动推关", BtnPush_Click, GameButtonSkin.Orange, 93, 31);
			r0.Controls.Add(btnArena, 1, 0);
			r0.Controls.Add(btnGuildBoss, 2, 0);
			r0.Controls.Add(btnEarth, 3, 0);
			r0.Controls.Add(btnPush, 4, 0);
			layout.Controls.Add(r0, 0, 0);

			// Row 1: Arena options inset aligned under buttons
			var r1Wrap = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(38, 0, 2, 0) };
			var optBar = new InsetPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 1, 8, 1) };
			var optFlow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			optFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));
			optFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
			optFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			optFlow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			optFlow.Controls.Add(new SmoothLabel { Text = "竞技场轮次", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = MutedColor, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
			numArenaCount = new GameNumber { Minimum = 1, Maximum = 99, Value = savedArenaCount, Size = new Size(62, 22), Anchor = AnchorStyles.Left, Margin = Padding.Empty, Font = UiTheme.GameFont(8.5f, FontStyle.Bold) };
			optFlow.Controls.Add(numArenaCount, 1, 0);
			chkArenaRefresh = new GameCheckBox
			{
				Text = "允许血钻刷新",
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				BackColor = Color.Transparent,
				ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : GoldColor,
				Font = UiTheme.GameFont(8f),
				Checked = options.ContainsKey("arenaAllowBloodRefresh") && options["arenaAllowBloodRefresh"],
				Tag = "arenaAllowBloodRefresh"
			};
			chkArenaRefresh.CheckedChanged += Option_CheckedChanged;
			optionChecks["arenaAllowBloodRefresh"] = chkArenaRefresh;
			optFlow.Controls.Add(chkArenaRefresh, 2, 0);
			optBar.Controls.Add(optFlow);
			r1Wrap.Controls.Add(optBar);
			layout.Controls.Add(r1Wrap, 0, 1);

			// Row 2: 生产 tag + 2 gold buttons + 2 info mini boxes
			var r2 = CategoryRowB("生产", UiTheme.IsLightTheme ? YellowColor : Color.FromArgb(255, 226, 122));
			btnWorkshop = GridButton("工坊", BtnWorkshop_Click, GameButtonSkin.Gold, 93, 31);
			btnMine = GridButton("矿山采集", BtnMine_Click, GameButtonSkin.Gold, 93, 31);
			var miniWs = MakeMiniBoxB("工坊档位", out lblMiniWorkshop);
			var miniMn = MakeMiniBoxB("矿队状态", out lblMiniMine);
			r2.Controls.Add(btnWorkshop, 1, 0);
			r2.Controls.Add(btnMine, 2, 0);
			r2.Controls.Add(miniWs, 3, 0);
			r2.Controls.Add(miniMn, 4, 0);
			layout.Controls.Add(r2, 0, 2);

			// Row 3: 养成 tag + 3 green buttons
			var r3 = CategoryRowB("养成", UiTheme.IsLightTheme ? GreenColor : Color.FromArgb(184, 236, 122));
			btnRune = GridButton("符文连抽", BtnRune_Click, GameButtonSkin.Green, 93, 31);
			btnDestiny = GridButton("宿命强化", BtnDestiny_Click, GameButtonSkin.Green, 93, 31);
			btnOrePacks = GridButton("使用矿石包", BtnOrePacks_Click, GameButtonSkin.Green, 93, 31);
			SetTaskTooltips();
			r3.Controls.Add(btnRune, 1, 0);
			r3.Controls.Add(btnDestiny, 2, 0);
			r3.Controls.Add(btnOrePacks, 3, 0);
			layout.Controls.Add(r3, 0, 3);

			return layout;
		}

		private static TableLayoutPanel CategoryRowB(string caption, Color color)
		{
			var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
			for (int i = 0; i < 4; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
			row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			var tag = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 3, 2) };
			tag.Controls.Add(new SmoothLabel { Text = caption, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = color, Font = UiTheme.GameFont(7.75f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter });
			row.Controls.Add(tag, 0, 0);
			return row;
		}

		private static Control MakeMiniBoxB(string title, out Label valueLbl)
		{
			var box = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 2), Padding = new Padding(2, 1, 2, 1) };
			var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
			tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 46f));
			tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 54f));
			tbl.Controls.Add(new SmoothLabel { Text = title, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = MutedColor, Font = UiTheme.GameFont(6.75f), TextAlign = ContentAlignment.BottomCenter }, 0, 0);
			valueLbl = new SmoothLabel { Text = "--", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = TextColor, Font = UiTheme.GameFont(7.25f, FontStyle.Bold), TextAlign = ContentAlignment.TopCenter, AutoEllipsis = true };
			tbl.Controls.Add(valueLbl, 0, 1);
			box.Controls.Add(tbl);
			return box;
		}

		private Control BuildLogWithTopStatsB()
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			layout.Controls.Add(BuildKpiTiles(false), 0, 0);
			Control log = BuildLogsBody();
			if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
			{
				log.Margin = new Padding(0, 4, 0, 0);
				layout.Controls.Add(log, 0, 1);
			}
			else
			{
				var logWrap = new InsetPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 4, 4), Margin = new Padding(0, 4, 0, 0) };
				log.BackColor = UiTheme.CurrentTheme == UiTheme.ThemeClassic ? Color.FromArgb(47, 38, 22) : UiTheme.Palette.LogBackColor;
				logWrap.Controls.Add(log);
				layout.Controls.Add(logWrap, 0, 1);
			}
			return layout;
		}

		private Control BuildBottomRightButtonsB()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = glass ? 188 : 170, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
			btnBottomReview = new GameButton { Text = "任务记录", Size = glass ? new Size(88, 30) : new Size(78, 26), Margin = new Padding(4, 1, 0, 1), Skin = GameButtonSkin.Stone, Font = UiTheme.GameFont(glass ? 8f : 7.5f, FontStyle.Bold) };
			btnBottomReview.Click += OpenReview;
			btnBottomClearLog = new GameButton { Text = "清空日志", Size = glass ? new Size(88, 30) : new Size(78, 26), Margin = new Padding(0, 1, 4, 1), Skin = GameButtonSkin.Stone, Font = UiTheme.GameFont(glass ? 8f : 7.5f, FontStyle.Bold) };
			btnBottomClearLog.Click += BtnClearLog_Click;
			hints.SetToolTip(btnBottomClearLog, "清空日志（每 10 分钟亦会自动清理）");
			right.Controls.Add(btnBottomReview);
			right.Controls.Add(btnBottomClearLog);
			return right;
		}

		// =====================================================================
		// 方案 C · 任务清单
		// =====================================================================
		private GameSidePanel BuildSidebarLayoutC()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			bool compact = sidebarScroll != null && sidebarScroll.ClientSize.Height > 100 && sidebarScroll.ClientSize.Height < 775;
			var side = new GameSidePanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(10, 6, 10, 8),
				Margin = Padding.Empty
			};
			side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f + BattlePlaybackRowHeight));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, ListTasksSectionHeight(compact, glass)));
			side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			sidebarLayout = side;
			activeSidePanel = side;

			side.Controls.Add(BuildHeader(), 0, 0);
			side.Controls.Add(BuildPrimaryActionsWithPlayback(BuildBoxedPrimaryCard(50, 150)), 0, 1);

			pageTasks = BuildListTasksBodyC(compact);
			pageCamp = BuildCampPageBoxed(120);
			GameTopTab unusedTabC;
			var midDock = BuildTopTabbedSection(
				new[] { "任务", "设置" },
				new[] { "tasks", "camp" },
				new[] { pageTasks, pageCamp },
				true,
				null,
				out topTabTasks, out topTabCamp, out unusedTabC);
			side.Controls.Add(midDock, 0, 2);

			panelBottomStatus = BuildStatusBodyC();
			panelBottomStats = BuildMiningStatsBody();
			panelBottomLog = BuildLogsBody();
			var rightHost = BuildBottomRightButtons(26, 78);
			var botDock = BuildTopTabbedSection(
				new[] { "营地状态", "挖矿统计", "冒险日志" },
				new[] { "status", "stats", "log" },
				new[] { panelBottomStatus, panelBottomStats, panelBottomLog },
				false,
				rightHost,
				out topTabBottomStatus, out topTabBottomStats, out topTabBottomLog);
			side.Controls.Add(botDock, 0, 3);

			SwitchTab(currentTab);
			SwitchBottomTab(currentBottomTab);
			return side;
		}

		private static float ListTaskRowHeight(bool compact, bool glass)
		{
			if (glass) return compact ? 30f : 34f;
			return compact ? 28f : 33f;
		}

		private static float ListTasksSectionHeight(bool compact, bool glass)
		{
			if (glass) return compact ? 384f : 434f;
			return compact ? 358f : 422f;
		}

		private Control BuildListTasksBodyC(bool compact)
		{
			float hdrH = compact ? 18f : 22f;
			float rowH = ListTaskRowHeight(compact, UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass);
			int btnW = compact ? 72 : 78;
			int btnH = compact ? 24 : 26;

			var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12, BackColor = Color.Transparent, Margin = Padding.Empty };
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			int[] headerIndices = { 0, 5, 8 };
			for (int r = 0; r < 12; r++)
			{
				bool isH = (r == headerIndices[0] || r == headerIndices[1] || r == headerIndices[2]);
				table.RowStyles.Add(new RowStyle(SizeType.Absolute, isH ? hdrH : rowH));
			}
			listTasksTable = table;

			Color cOrange = Color.FromArgb(224, 118, 58);
			Color cGold = Color.FromArgb(232, 194, 58);
			Color cGreen = Color.FromArgb(143, 209, 71);

			table.Controls.Add(new GameHeader { Title = "战斗委托", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);

			// Arena custom row (num + chk + btn)
			var arenaOptHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var lblRound = new SmoothLabel { Text = "轮次", Location = new Point(0, 2), Size = new Size(34, 22), BackColor = Color.Transparent, ForeColor = MutedColor, Font = UiTheme.GameFont(7.75f), TextAlign = ContentAlignment.MiddleLeft };
			numArenaCount = new GameNumber { Minimum = 1, Maximum = 99, Value = savedArenaCount, Location = new Point(36, 2), Size = new Size(54, 21), Font = UiTheme.GameFont(8.25f, FontStyle.Bold) };
			chkArenaRefresh = new GameCheckBox
			{
				Text = "血钻刷新",
				Location = new Point(98, 2),
				Size = new Size(124, 22),
				BackColor = Color.Transparent,
				ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : GoldColor,
				Font = UiTheme.GameFont(7.75f),
				Checked = options.ContainsKey("arenaAllowBloodRefresh") && options["arenaAllowBloodRefresh"],
				Tag = "arenaAllowBloodRefresh"
			};
			chkArenaRefresh.CheckedChanged += Option_CheckedChanged;
			optionChecks["arenaAllowBloodRefresh"] = chkArenaRefresh;
			arenaOptHost.Controls.Add(lblRound);
			arenaOptHost.Controls.Add(numArenaCount);
			arenaOptHost.Controls.Add(chkArenaRefresh);
			arenaOptHost.Resize += delegate
			{
				int cy = Math.Max(0, (arenaOptHost.Height - 22) / 2);
				lblRound.Top = cy;
				numArenaCount.Top = cy;
				chkArenaRefresh.Top = cy;
			};
			btnArena = GridButton("开始", BtnArena_Click, GameButtonSkin.Orange, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("竞技场", cOrange, arenaOptHost, btnArena), 0, 1);

			btnGuildBoss = GridButton("挑战", BtnGuildBoss_Click, GameButtonSkin.Orange, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("工会首领", cOrange, MakeRowLabelC(out lblRowGuild, "待命"), btnGuildBoss), 0, 2);

			btnEarth = GridButton("挑战", BtnEarth_Click, GameButtonSkin.Orange, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("矿区首领", cOrange, MakeRowLabelC(out lblRowEarth, "待命"), btnEarth), 0, 3);

			btnPush = GridButton("开始", BtnPush_Click, GameButtonSkin.Orange, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("自动推关", cOrange, MakeRowLabelC(out lblRowPush, "待命"), btnPush), 0, 4);

			table.Controls.Add(new GameHeader { Title = "生产", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 5);

			btnWorkshop = GridButton("执行", BtnWorkshop_Click, GameButtonSkin.Gold, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("工坊", cGold, MakeRowLabelC(out lblRowWorkshop, "先收后产"), btnWorkshop), 0, 6);

			btnMine = GridButton("执行", BtnMine_Click, GameButtonSkin.Gold, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("矿山采集", cGold, MakeRowLabelC(out lblRowMine, "空闲"), btnMine), 0, 7);

			table.Controls.Add(new GameHeader { Title = "养成", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 8);

			btnRune = GridButton("开始", BtnRune_Click, GameButtonSkin.Green, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("符文连抽", cGreen, MakeRowLabelC(out lblRowRune, "优先十连 · 自动强化"), btnRune), 0, 9);

			btnDestiny = GridButton("开始", BtnDestiny_Click, GameButtonSkin.Green, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("宿命强化", cGreen, MakeRowLabelC(out lblRowDestiny, "待命"), btnDestiny), 0, 10);

			btnOrePacks = GridButton("使用", BtnOrePacks_Click, GameButtonSkin.Green, btnW, btnH);
			table.Controls.Add(BuildTaskRowC("使用矿石包", cGreen, MakeRowLabelC(out lblRowOre, "待命"), btnOrePacks), 0, 11);

			SetTaskTooltips();
			return table;
		}

		private static Control BuildTaskRowC(string name, Color stripe, Control middle, GameButton action)
		{
			var row = new InsetPanel
			{
				Dock = DockStyle.Fill,
				FillColor = UiTheme.CurrentTheme == UiTheme.ThemeClassic ? Color.FromArgb(58, 47, 27) : UiTheme.Palette.InsetColor,
				LeftStripeColor = stripe,
				Padding = new Padding(10, 1, 6, 1),
				Margin = Padding.Empty
			};
			var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
			tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? 92f : 80f));
			tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			tbl.Controls.Add(new SmoothLabel
			{
				Text = name,
				Dock = DockStyle.Fill,
				BackColor = Color.Transparent,
				ForeColor = TextColor,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			}, 0, 0);
			tbl.Controls.Add(middle, 1, 0);
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass)
			{
				action.Anchor = AnchorStyles.None;
				action.Margin = Padding.Empty;
			}
			tbl.Controls.Add(action, 2, 0);
			row.Controls.Add(tbl);
			return row;
		}

		private static Label MakeRowLabelC(out Label target, string text)
		{
			target = new SmoothLabel
			{
				Text = text,
				Dock = DockStyle.Fill,
				BackColor = Color.Transparent,
				ForeColor = MutedColor,
				Font = UiTheme.GameFont(7.75f),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			};
			return target;
		}

		private Control BuildStatusBodyC()
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			// Top status title + right chips
			var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));
			top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
			top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
			top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			lblStatus = new SmoothLabel { Text = "营地待命", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = GoldColor, Font = UiTheme.GameFont(10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
			lblDetail = new SmoothLabel { Visible = false };
			top.Controls.Add(lblStatus, 0, 0);
			chipPicks = MakeChipCell(top, "矿镐 --/--", 1);
			chipGolem = MakeChipCell(top, "魔像 --", 2);
			chipDepth = MakeChipCell(top, "深度 --", 3);
			layout.Controls.Add(top, 0, 0);

			layout.Controls.Add(BuildKpiTiles(false), 0, 1);

			var infoBox = new InsetPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 3, 0, 0) };
			var infoStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
			infoStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			for (int i = 0; i < 3; i++) infoStack.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
			lblMinePlan = LineInfoLabel("推荐矿层：等待游戏数据", GoldColor);
			lblStatusSchedule = LineInfoLabel("自动续跑与签到：等待读取", MutedColor);
			lblStatusSchedShort = LineInfoLabel("定时：--", TextColor);
			infoStack.Controls.Add(lblMinePlan, 0, 0);
			infoStack.Controls.Add(lblStatusSchedule, 0, 1);
			infoStack.Controls.Add(lblStatusSchedShort, 0, 2);
			infoBox.Controls.Add(infoStack);
			infoBox.Controls.Add(lblDetail);
			layout.Controls.Add(infoBox, 0, 2);
			return layout;
		}

		// =====================================================================
		// 公用积木：顶部斜角页签宿主 + 面板边框 + 统计方块 + 芯片
		// =====================================================================
		private Control BuildTopTabbedSection(
			string[] labels, string[] keys, Control[] bodies, bool isTopSection, Control rightAccessory,
			out GameTopTab tab0, out GameTopTab tab1, out GameTopTab tab2)
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			int barH = glass ? 35 : 28;
			var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
			var tabBar = new Panel { Dock = DockStyle.Top, Height = barH, BackColor = Color.Transparent, Padding = new Padding(glass ? 4 : 10, 0, 4, glass ? 3 : 0) };
			Panel tabHost;
			if (glass)
			{
				var glassTabs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = labels.Length, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
				for (int i = 0; i < labels.Length; i++) glassTabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / labels.Length));
				glassTabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
				var barColumns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = rightAccessory == null ? 1 : 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
				barColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
				barColumns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
				barColumns.Controls.Add(glassTabs, 0, 0);
				if (rightAccessory != null)
				{
					barColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, rightAccessory.Width));
					rightAccessory.Dock = DockStyle.Fill;
					rightAccessory.Margin = Padding.Empty;
					barColumns.Controls.Add(rightAccessory, 1, 0);
				}
				tabBar.Controls.Add(barColumns);
				tabHost = glassTabs;
			}
			else
			{
				tabHost = new FlowLayoutPanel { Dock = DockStyle.Left, Width = labels.Length >= 3 ? 318 : 220, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
				tabBar.Controls.Add(tabHost);
				if (rightAccessory != null) tabBar.Controls.Add(rightAccessory);
			}

			tab0 = tab1 = tab2 = null;
			for (int i = 0; i < labels.Length; i++)
			{
				string key = keys[i];
				var tab = new GameTopTab
				{
					Text = labels[i],
					Size = new Size(labels.Length >= 3 ? 102 : 100, 28),
					Dock = glass ? DockStyle.Fill : DockStyle.None,
					Margin = new Padding(0, 0, glass ? 4 : 2, 0),
					Font = UiTheme.GameFont(glass ? 8.5f : 8f, FontStyle.Bold)
				};
				if (isTopSection) tab.Click += delegate { SwitchTab(key); };
				else tab.Click += delegate { SwitchBottomTab(key); if (key == "log") FlushDeferredLogUi(); };
				tabHost.Controls.Add(tab);
				if (i == 0) tab0 = tab;
				else if (i == 1) tab1 = tab;
				else if (i == 2) tab2 = tab;
			}
			var box = new PixelBoxPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 9, 10, 10), Margin = glass ? new Padding(0, 3, 0, 0) : Padding.Empty };
			var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
			for (int i = bodies.Length - 1; i >= 0; i--)
			{
				if (bodies[i] == null) continue;
				bodies[i].Dock = DockStyle.Fill;
				bodyHost.Controls.Add(bodies[i]);
			}
			box.Controls.Add(bodyHost);

			// Add box first so Dock=Fill sits behind Dock=Top tabBar, or place them seamlessly via TableLayoutPanel!
			var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
			split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			split.RowStyles.Add(new RowStyle(SizeType.Absolute, barH));
			split.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			split.Controls.Add(tabBar, 0, 0);
			split.Controls.Add(box, 0, 1);
			host.Controls.Add(split);
			return host;
		}

		private Control BuildBottomRightButtons(int btnH, int btnW)
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			if (glass) { btnW = 88; btnH = 30; }
			var right = new Panel { Dock = DockStyle.Right, Width = glass ? 94 : 84, BackColor = Color.Transparent };
			btnBottomReview = new GameButton
			{
				Text = "任务记录",
				Location = new Point(2, 1),
				Size = new Size(btnW, btnH),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(7.5f, FontStyle.Bold)
			};
			btnBottomReview.Click += OpenReview;
			btnBottomClearLog = new GameButton
			{
				Text = "清空日志",
				Location = new Point(2, 1),
				Size = new Size(btnW, btnH),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(7.5f, FontStyle.Bold),
				Visible = false
			};
			btnBottomClearLog.Click += BtnClearLog_Click;
			hints.SetToolTip(btnBottomClearLog, "清空日志（每 10 分钟亦会自动清理）");
			right.Controls.Add(btnBottomReview);
			right.Controls.Add(btnBottomClearLog);
			return right;
		}

		private Control BuildCampPageBoxed(int overviewHeight)
		{
			var page = NewPage();
			page.BackColor = Color.Transparent;
			page.RowCount = 2;
			page.RowStyles.Add(new RowStyle(SizeType.Absolute, overviewHeight));
			page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			// 上卡：配置总览
			var ovWrap = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 4) };
			var ovBox = new InsetPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
			var ovSplit = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			ovSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67f));
			ovSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
			ovSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var sumStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Margin = Padding.Empty };
			sumStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			for (int i = 0; i < 4; i++) sumStack.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
			lblSumMining = SummaryLine();
			lblSumDaily = SummaryLine();
			lblSumWorkshop = SummaryLine();
			lblSumSchedule = SummaryLine();
			sumStack.Controls.Add(lblSumMining, 0, 0);
			sumStack.Controls.Add(lblSumDaily, 0, 1);
			sumStack.Controls.Add(lblSumWorkshop, 0, 2);
			sumStack.Controls.Add(lblSumSchedule, 0, 3);

			btnAdvanced = new GameButton
			{
				Text = "全部设置",
				Size = new Size(overviewHeight <= 80 ? 90 : 114, overviewHeight <= 80 ? 30 : 38),
				Anchor = AnchorStyles.None,
				Skin = GameButtonSkin.Gold,
				Font = UiTheme.GameFont(overviewHeight <= 80 ? 8f : 9f, FontStyle.Bold)
			};
			btnAdvanced.Click += async delegate { await OpenUnifiedSettingsAsync("mining"); };
			btnChest = btnAdvanced;
			ovSplit.Controls.Add(sumStack, 0, 0);
			ovSplit.Controls.Add(btnAdvanced, 1, 0);
			ovBox.Controls.Add(ovSplit);
			ovWrap.Controls.Add(ovBox);

			// 下卡：9 大分类直达（适配不同高度）
			var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
			for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			for (int i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
			campGridHost = grid;

			int catBtnH = overviewHeight <= 80 ? 27 : 34;
			int catBtnW = catBtnH * 3;
			string[,] cats =
			{
				{ "mining", "矿洞策略" }, { "resources", "资源与宝箱" }, { "daily", "日常清单" },
				{ "workshop", "工坊与工会" }, { "growth", "符文与宿命" }, { "mine", "矿山研究" },
				{ "schedule", "定时任务" }, { "game", "游戏偏好" }, { "advanced", "高级策略" }
			};
			for (int i = 0; i < cats.GetLength(0); i++)
			{
				string key = cats[i, 0];
				var btn = new GameButton
				{
					Text = cats[i, 1],
					Size = new Size(catBtnW, catBtnH),
					Anchor = AnchorStyles.None,
					Skin = GameButtonSkin.Stone,
					Font = UiTheme.GameFont(catBtnH < 30 ? 7.5f : 8.25f, FontStyle.Bold)
				};
				FitGlassAction(btn);
				btn.Click += async delegate { await OpenUnifiedSettingsAsync(key); };
				if (key == "daily") btnDailyOptions = btn;
				else if (key == "workshop") btnWorkshopOptions = btn;
				else if (key == "growth") btnGrowthOptions = btn;
				else if (key == "mine") btnMineResearch = btn;
				else if (key == "schedule") btnSchedule = btn;
				grid.Controls.Add(btn, i % 3, i / 3);
			}

			page.Controls.Add(ovWrap, 0, 0);
			page.Controls.Add(grid, 0, 1);
			return page;
		}

		private static Label MakeChipCell(TableLayoutPanel parent, string text, int col)
		{
			var chip = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(2, 1, 2, 1), Padding = new Padding(4, 0, 4, 0) };
			var lbl = new SmoothLabel
			{
				Text = text,
				Dock = DockStyle.Fill,
				BackColor = Color.Transparent,
				ForeColor = TextColor,
				Font = UiTheme.GameFont(7.75f),
				TextAlign = ContentAlignment.MiddleCenter,
				AutoEllipsis = true
			};
			chip.Controls.Add(lbl);
			parent.Controls.Add(chip, col, 0);
			return lbl;
		}

		private Control BuildKpiTiles(bool twoRows)
		{
			int cols = twoRows ? 3 : 6;
			int rows = twoRows ? 2 : 1;
			var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols, RowCount = rows, BackColor = Color.Transparent, Margin = Padding.Empty };
			for (int c = 0; c < cols; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
			for (int r = 0; r < rows; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

			string[] keys = { "digs", "golems", "bosses", "wins", "losses", "chests" };
			string[] names = { "挖矿", "魔像", "BOSS", "胜利", "失败", "宝箱" };
			for (int i = 0; i < keys.Length; i++)
			{
				var tile = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 2), Padding = new Padding(1, 1, 1, 1) };
				var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
				stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
				stack.RowStyles.Add(new RowStyle(SizeType.Percent, 56f));
				stack.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));

				Color valColor = keys[i] == "wins" ? GreenColor : (keys[i] == "losses" ? RedColor : ((UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : Color.FromArgb(255, 233, 176)));
				var valLbl = new SmoothLabel
				{
					Text = "0",
					Dock = DockStyle.Fill,
					BackColor = Color.Transparent,
					ForeColor = valColor,
					Font = UiTheme.GameFont(twoRows ? 10.5f : 9.5f, FontStyle.Bold),
					TextAlign = ContentAlignment.BottomCenter
				};
				var capLbl = new SmoothLabel
				{
					Text = names[i],
					Dock = DockStyle.Fill,
					BackColor = Color.Transparent,
					ForeColor = MutedColor,
					Font = UiTheme.GameFont(7f),
					TextAlign = ContentAlignment.TopCenter
				};
				stack.Controls.Add(valLbl, 0, 0);
				stack.Controls.Add(capLbl, 0, 1);
				tile.Controls.Add(stack);
				statValueLabels[keys[i]] = valLbl;
				grid.Controls.Add(tile, twoRows ? (i % 3) : i, twoRows ? (i / 3) : 0);
			}
			return grid;
		}

		private static Label SectionCaption(string text)
		{
			return new SmoothLabel
			{
				Text = text,
				Dock = DockStyle.Fill,
				BackColor = Color.Transparent,
				ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? MutedColor : Color.FromArgb(201, 163, 93),
				Font = UiTheme.GameFont(7.75f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			};
		}

		private static SmoothLabel LineInfoLabel(string text, Color color)
		{
			return new SmoothLabel
			{
				Text = text,
				Dock = DockStyle.Fill,
				BackColor = Color.Transparent,
				ForeColor = color,
				Font = UiTheme.GameFont(7.75f),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			};
		}

		private static void FitGlassAction(GameButton button)
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass) return;
			button.Dock = DockStyle.Fill;
			button.Margin = new Padding(3, 2, 3, 2);
		}

		private GameButton GridButton(string text, EventHandler click, GameButtonSkin skin, int w, int h)
		{
			var btn = new GameButton
			{
				Text = text,
				Size = new Size(w, h),
				Anchor = AnchorStyles.None,
				Skin = skin,
				Font = UiTheme.GameFont(h <= 26 ? 8f : (h <= 33 ? 8.5f : 9f), FontStyle.Bold)
			};
			FitGlassAction(btn);
			btn.Click += click;
			return btn;
		}

		private void SetTaskTooltips()
		{
			if (btnGuildBoss != null) hints.SetToolTip(btnGuildBoss, "按设定次数挑战当前工会首领（使用已有许可证，不自动购买）。");
			if (btnEarth != null) hints.SetToolTip(btnEarth, "使用恶魔勋章连续挑战矿区首领。");
			if (btnPush != null) hints.SetToolTip(btnPush, "简单 · 普通 · 困难，未通关主线持续重试。");
			if (btnWorkshop != null) hints.SetToolTip(btnWorkshop, "先自动收取全部已完成工程，再按工坊设置生产补满空槽位。");
			if (btnMine != null) hints.SetToolTip(btnMine, "先收成熟矿队奖励、再升级研究并按材料缺口派遣矿队。");
			if (btnDestiny != null) hints.SetToolTip(btnDestiny, "使用游戏一键强化，优先强化升星期望结晶较低的宿命武器。");
			if (btnOrePacks != null) hints.SetToolTip(btnOrePacks, "连续批量用完低级、中级、高级矿石包，并自动关闭获得窗口。");
		}

		// =====================================================================
		// 实时数据联动（方案 A / B / C 共享同步，不重复读取）
		// =====================================================================
		private void GetNextScheduleInfo(out string planName, out string timeText)
		{
			planName = "未启用";
			timeText = "--";
			if (scheduleBook == null) return;

			DateTime bestUtc = DateTime.MaxValue;
			string bestName = null;

			if (scheduleBook.Harvest != null)
			{
				foreach (var h in scheduleBook.Harvest)
				{
					if (h != null && scheduleBook.EnabledHarvest(h.Task) && h.DueUtc < bestUtc)
					{
						bestUtc = h.DueUtc;
						bestName = h.Label;
					}
				}
			}

			if (scheduleBook.AutoCheckin)
			{
				DateTime nowUtc = DateTime.UtcNow;
				DateTime bjt = nowUtc.AddHours(8);
				int[] hours = { 0, 12, 18 };
				foreach (int hr in hours)
				{
					DateTime slot = bjt.Date.AddHours(hr).AddHours(-8);
					if (slot <= nowUtc) slot = slot.AddDays(1);
					if (slot < bestUtc)
					{
						bestUtc = slot;
						bestName = "自动签到";
					}
				}
			}

			DateTime nowLocal = DateTime.Now;
			if (scheduleBook.Plans != null)
			{
				foreach (var p in scheduleBook.Plans)
				{
					if (p == null || !p.Enabled) continue;
					if (p.Kind != "interval")
					{
						try
						{
							foreach (var ts in ScheduleBook.ParseTimes(p.Times))
							{
								DateTime cand = nowLocal.Date.Add(ts);
								if (cand <= nowLocal) cand = cand.AddDays(1);
								DateTime candUtc = cand.ToUniversalTime();
								if (candUtc < bestUtc)
								{
									bestUtc = candUtc;
									bestName = p.Name;
								}
							}
						}
						catch { }
					}
					else if (bestName == null)
					{
						bestName = p.Name;
					}
				}
			}

			if (bestName != null)
			{
				planName = bestName;
				timeText = bestUtc < DateTime.MaxValue ? bestUtc.ToLocalTime().ToString("HH:mm") : "间隔执行";
			}
		}

		private string GetTodayDailySummaryText()
		{
			if (IsTaskRunning(AssistantTaskKind.Daily)) return "日常运行中…";
			try
			{
				var data = ReadReview();
				if (data != null && data.Daily != null && data.Daily.day == SpendDayNow() && data.Daily.rows != null && data.Daily.rows.Count > 0)
				{
					int total = data.Daily.rows.Count, done = 0;
					foreach (var r in data.Daily.rows)
					{
						if (r != null && (r.status == "已处理" || r.status == "已核对" || r.status == "已跳过")) done++;
					}
					return "今日日常 " + done + "/" + total + (done == total ? " ✓" : "");
				}
			}
			catch { }
			return "今日日常 待同步";
		}

		private void UpdateLayoutDynamicViews(Dictionary<string, object> snapshot)
		{
			if (snapshot != null)
			{
				string acc = GetString(snapshot, "account");
				if (!string.IsNullOrEmpty(acc)) cachedAccountId = acc;

				var mining = GetDictionary(snapshot, "mining");
				if (mining != null)
				{
					cachedDigCount = GetInt(mining, "digCount");
					cachedDigMax = GetInt(mining, "digMax");
					cachedGolemLv = GetInt(mining, "golemLv");
					cachedDepth = GetInt(mining, "depth");
				}
				var mine = GetDictionary(snapshot, "mine");
				if (mine != null)
				{
					string p = GetString(mine, "plan");
					string s = GetString(mine, "slots");
					string el = GetString(mine, "earliestLabel");
					if (!string.IsNullOrEmpty(p)) cachedMinePlan = p;
					if (!string.IsNullOrEmpty(s)) cachedMineSlots = s;
					if (!string.IsNullOrEmpty(el)) cachedMineEarliest = el;
					if (mine.ContainsKey("earliestSeconds")) cachedMineEarliestSeconds = GetInt(mine, "earliestSeconds");
				}
				var ws = GetDictionary(snapshot, "workshop");
				if (ws != null)
				{
					string lbl = GetString(ws, "label");
					if (!string.IsNullOrEmpty(lbl)) cachedWorkshopLabel = lbl;
				}
				var guild = GetDictionary(snapshot, "guild");
				if (guild != null && guild.ContainsKey("tickets")) cachedGuildTickets = GetInt(guild, "tickets");

				var refMap = GetDictionary(snapshot, "refine");
				if (refMap != null && refMap.ContainsKey("temperature"))
				{
					cachedRefineTemp = Convert.ToDouble(refMap["temperature"], System.Globalization.CultureInfo.InvariantCulture);
					int rmax = GetInt(refMap, "max");
					if (rmax > 0) cachedRefineMax = rmax;
				}
			}

			// Chips (Layouts A & C)
			if (chipPicks != null) chipPicks.Text = cachedDigMax > 0 ? ("矿镐 " + cachedDigCount + "/" + cachedDigMax) : "矿镐 --/--";
			if (chipGolem != null) chipGolem.Text = cachedGolemLv > 0 ? ("魔像 Lv" + cachedGolemLv) : "魔像 --";
			if (chipDepth != null) chipDepth.Text = cachedDepth > 0 ? ("深度 " + cachedDepth) : "深度 --";

			// 方案 B 顶部看板
			if (lblHeroDailySummary != null) lblHeroDailySummary.Text = GetTodayDailySummaryText();
			if (heroBarPicks != null && lblHeroPicksVal != null)
			{
				heroBarPicks.Fraction = cachedDigMax > 0 ? (float)cachedDigCount / cachedDigMax : 0f;
				lblHeroPicksVal.Text = cachedDigMax > 0 ? (cachedDigCount + " / " + cachedDigMax) : "-- / --";
			}
			if (heroBarRefine != null && lblHeroRefineVal != null)
			{
				double heat = -1;
				if (cachedRefineTemp >= 0) heat = cachedRefineTemp;
				else if (scheduleBook != null && scheduleBook.Harvest != null)
				{
					var hr = scheduleBook.Harvest.Find(h => h != null && h.Task == "refine");
					if (hr != null && hr.RefineHeat.HasValue)
					{
						DateTime nowUtc = DateTime.UtcNow;
						if (hr.DueUtc <= nowUtc) heat = 900;
						else
						{
							double span = Math.Max(1.0, (hr.DueUtc - hr.RefineMeasuredUtc).TotalSeconds);
							double left = Math.Max(0.0, (hr.DueUtc - nowUtc).TotalSeconds);
							heat = 999.0 + (hr.RefineHeat.Value - 999.0) * Math.Min(1.0, left / span);
						}
					}
				}
				if (heat >= 0)
				{
					int pct = Math.Max(0, Math.Min(100, (int)Math.Round(heat * 100.0 / Math.Max(1, cachedRefineMax))));
					heroBarRefine.Fraction = pct / 100f;
					lblHeroRefineVal.Text = pct + "%";
				}
				else
				{
					heroBarRefine.Fraction = 0f;
					lblHeroRefineVal.Text = "--%";
				}
			}
			if (heroBarMine != null && lblHeroMineVal != null)
			{
				lblHeroMineVal.Text = string.IsNullOrEmpty(cachedMineEarliest) ? "空闲" : cachedMineEarliest;
				if (cachedMineEarliestSeconds == 0 || (!string.IsNullOrEmpty(cachedMineEarliest) && cachedMineEarliest.IndexOf("待领取", StringComparison.Ordinal) >= 0))
					heroBarMine.Fraction = 1f;
				else if (cachedMineEarliestSeconds > 0)
					heroBarMine.Fraction = (float)Math.Max(0.12, 1.0 - Math.Min(1.0, cachedMineEarliestSeconds / 14400.0));
				else
					heroBarMine.Fraction = 0f;
			}
			string schedName, schedTime;
			GetNextScheduleInfo(out schedName, out schedTime);
			if (lblHeroSchedName != null) lblHeroSchedName.Text = schedName;
			if (lblHeroSchedTime != null) lblHeroSchedTime.Text = schedTime;
			if (lblMiniWorkshop != null) lblMiniWorkshop.Text = GetNumberOption("workshopDepth", 150) + "层·" + GetNumberOption("workshopMinutes", 120) + "分";
			if (lblMiniMine != null)
			{
				lblMiniMine.Text = string.IsNullOrEmpty(cachedMineEarliest) ? "空闲" : cachedMineEarliest;
				lblMiniMine.ForeColor = (cachedMineEarliest != null && cachedMineEarliest.IndexOf("待领取", StringComparison.Ordinal) >= 0) ? GreenColor : TextColor;
			}

			// 方案 C 逐行状态与底部定时
			if (lblStatusSchedShort != null) lblStatusSchedShort.Text = "定时：" + schedName + (schedTime != "--" ? " · " + schedTime : "");
			UpdateTaskListRowHintsC();
		}

		private void UpdateTaskListRowHintsC()
		{
			if (lblRowGuild != null)
			{
				bool doneToday = !string.IsNullOrEmpty(cachedAccountId) && IsGuildBossDoneToday(cachedAccountId);
				if (IsTaskRunning(AssistantTaskKind.GuildBoss))
				{
					lblRowGuild.Text = string.IsNullOrEmpty(lastTaskMessage) ? "挑战中…" : lastTaskMessage;
					lblRowGuild.ForeColor = GoldColor;
				}
				else if (doneToday)
				{
					lblRowGuild.Text = "今日日常已挑战 ✓";
					lblRowGuild.ForeColor = GreenColor;
				}
				else
				{
					string t = "计划 " + GetNumberOption("guildBossCount", 3) + " 次";
					if (cachedGuildTickets >= 0) t += " · 许可证 " + cachedGuildTickets;
					lblRowGuild.Text = t;
					lblRowGuild.ForeColor = MutedColor;
				}
			}
			if (lblRowEarth != null)
			{
				bool active = IsTaskRunning(AssistantTaskKind.Growth) && growthMode == "miningBoss";
				lblRowEarth.Text = active && !string.IsNullOrEmpty(lastTaskMessage) ? lastTaskMessage : "待命";
				lblRowEarth.ForeColor = active ? GoldColor : MutedColor;
			}
			if (lblRowPush != null)
			{
				bool active = IsTaskRunning(AssistantTaskKind.Growth) && growthMode == "push";
				lblRowPush.Text = active && !string.IsNullOrEmpty(lastTaskMessage) ? lastTaskMessage : "待命";
				lblRowPush.ForeColor = active ? GoldColor : MutedColor;
			}
			if (lblRowWorkshop != null)
			{
				if (IsTaskRunning(AssistantTaskKind.Workshop) && !string.IsNullOrEmpty(lastTaskMessage))
				{
					lblRowWorkshop.Text = lastTaskMessage;
					lblRowWorkshop.ForeColor = GoldColor;
				}
				else
				{
					string cfg = GetNumberOption("workshopDepth", 150) + " 层 / " + GetNumberOption("workshopMinutes", 120) + " 分";
					lblRowWorkshop.Text = !string.IsNullOrEmpty(cachedWorkshopLabel) ? (cachedWorkshopLabel + " · " + cfg) : ("先收后产 · " + cfg);
					lblRowWorkshop.ForeColor = MutedColor;
				}
			}
			if (lblRowMine != null)
			{
				string slotsShort = string.IsNullOrEmpty(cachedMineSlots) ? "待命" : cachedMineSlots;
				if (slotsShort.StartsWith("矿队：", StringComparison.Ordinal)) slotsShort = slotsShort.Substring(3);
				lblRowMine.Text = slotsShort;
				lblRowMine.ForeColor = IsTaskRunning(AssistantTaskKind.Mine) ? GoldColor : MutedColor;
			}
			if (lblRowRune != null)
			{
				bool active = IsTaskRunning(AssistantTaskKind.Growth) && growthMode == "rune";
				lblRowRune.Text = active && !string.IsNullOrEmpty(lastTaskMessage) ? lastTaskMessage : "优先十连 · 自动强化";
				lblRowRune.ForeColor = active ? GoldColor : MutedColor;
			}
			if (lblRowDestiny != null)
			{
				bool active = IsTaskRunning(AssistantTaskKind.Growth) && growthMode == "destiny";
				lblRowDestiny.Text = active && !string.IsNullOrEmpty(lastTaskMessage) ? lastTaskMessage : "待命";
				lblRowDestiny.ForeColor = active ? GoldColor : MutedColor;
			}
			if (lblRowOre != null)
			{
				bool active = IsTaskRunning(AssistantTaskKind.Growth) && growthMode == "orepacks";
				lblRowOre.Text = active && !string.IsNullOrEmpty(lastTaskMessage) ? lastTaskMessage : "待命";
				lblRowOre.ForeColor = active ? GoldColor : MutedColor;
			}
		}

		private bool IsGuildBossDoneToday(string account)
		{
			if (string.IsNullOrEmpty(account)) return false;
			int day;
			return guildBossDoneDays.TryGetValue(account, out day) && day == SpendDayNow();
		}
	}
}
