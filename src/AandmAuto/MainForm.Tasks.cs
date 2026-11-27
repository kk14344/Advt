using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private DateTime nextWaitLog = DateTime.MinValue;
        private bool taskInitializing;
        private Timer dailyTaskTimer;
        private bool taskPumpBusy;
        private readonly List<string> uiTextProblems=new List<string>();
        private readonly List<string> buttonRatioProblems=new List<string>();
        private int checkedButtons;

        private void RecordUiTextIssues(Control control)
        {
            if (control.Visible && (control is ButtonBase || control is Label || control is ComboBox ||
                control is DateTimePicker || control is NumericUpDown || control is TextBoxBase || control is ListView))
            {
                using (var expected = UiTheme.GameFont(9f))
                    if (control.Font.FontFamily.Name != expected.FontFamily.Name)
                        uiTextProblems.Add(control.GetType().Name + "「" + control.Text + "」字体 " + control.Font.FontFamily.Name +
                            "（应为 " + expected.FontFamily.Name + "）");
            }
            var button=control as GameButton;
            if(button!=null && control.Visible) {
                checkedButtons++;
                if(UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass && button.Width!=button.Height*3)buttonRatioProblems.Add(button.Text+" "+button.Size);
                using(var font=button.LabelFont()) {
                    if(font.SizeInPoints>button.LabelBasePoints+.01f)uiTextProblems.Add(button.Text+" font exceeds intended label size");
                    Size needed=TextRenderer.MeasureText(button.Text,font,new Size(10000,10000),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
                    if(needed.Width>button.LabelBounds.Width || needed.Height>button.LabelBounds.Height)
                        uiTextProblems.Add(button.Text+" "+needed+" in "+button.LabelBounds.Size);
                }
            }
            else if(control.Visible && !string.IsNullOrEmpty(control.Text) && control is GameTopTab) {
                var tab=(GameTopTab)control;
                Size needed=TextRenderer.MeasureText(tab.Text,tab.Font,new Size(10000,10000),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
                if(needed.Width>tab.TextBounds.Width || needed.Height>tab.TextBounds.Height)
                    uiTextProblems.Add(tab.Text+" tab ["+needed.Width+"x"+needed.Height+" in "+tab.TextBounds.Size+"]");
            }
            else if(control.Visible && !string.IsNullOrEmpty(control.Text) && control is GameCheckBox) {
                Size needed=TextRenderer.MeasureText(control.Text,control.Font,new Size(10000,10000),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
                Size bounds = ((GameCheckBox)control).LabelBounds.Size;
                if(needed.Width>bounds.Width || needed.Height>bounds.Height)
                    uiTextProblems.Add(control.FindForm().Text+" / "+control.Text+" ["+needed.Width+"x"+needed.Height+" in "+bounds+"]");
            }
            foreach(Control child in control.Controls)RecordUiTextIssues(child);
        }

        private async void FastTaskTick(object sender,EventArgs e)
        {
            if(taskPumpBusy || IsDisposed || !coreReady || taskInitializing) return;
            taskPumpBusy=true;
            try {
                switch (activeTask)
                {
                    case AssistantTaskKind.Daily: await TryDailyStepAsync(); break;
                    case AssistantTaskKind.Arena: await TryArenaStepAsync(); break;
                    case AssistantTaskKind.Void: await TryVoidStepAsync(); break;
                    case AssistantTaskKind.Mine: await TryMineStepAsync(); break;
                    case AssistantTaskKind.Workshop: await TryWorkshopStepAsync(); break;
                    case AssistantTaskKind.GuildBoss: await TryGuildStepAsync(); break;
                    case AssistantTaskKind.Growth: await TryGrowthStepAsync(); break;
                    case AssistantTaskKind.VoidExchange: await TryVoidExchangeStepAsync(); break;
                }
            } finally {taskPumpBusy=false;}
        }

        private DateTime NextTaskAt(Dictionary<string,object> result)
        {
            object delay;
            double seconds = 0;
            bool hasDelay = result.TryGetValue("delaySeconds", out delay) &&
                double.TryParse(Convert.ToString(delay, System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds) && seconds > 0;
            if (!BuildEdition.IsLowLoad || GetBool(result, "refineTap"))
            {
                return hasDelay ? DateTime.Now.AddSeconds(Math.Min(15, seconds)) : DateTime.Now.AddMilliseconds(30);
            }
            double minSeconds = BuildEdition.TaskMinStepMs / 1000.0;
            double clamped = hasDelay ? Math.Max(minSeconds, seconds) : minSeconds;
            return DateTime.Now.AddSeconds(Math.Min(15.0, clamped));
        }

        private async Task<bool> InitializeTaskAsync(string code)
        {
            int generation=taskGeneration;
            taskInitializing=true;
            try {
                for(int attempt=0;attempt<90;attempt++) {
                    if(generation!=taskGeneration)return false;
                    string result=await ExecuteLuaAsync(code);
                    if(generation!=taskGeneration)return false;
                    if(result=="true")return true;
                    ReportTaskMessage("等待游戏客户端加载…",true);
                    await Task.Delay(1000);
                }
                throw new InvalidOperationException("等待游戏客户端超时，请重新加载游戏后重试");
            } finally { if(generation==taskGeneration)taskInitializing=false; }
        }

        private async void CancelTaskInPage()
        {
            if (!coreReady || IsDisposed) return;
            try { await ExecuteScriptAsync("window.__MWA_APP && window.__MWA_APP.cancelTask()"); }
            catch { }
        }

        private void RefreshTaskControls()
        {
            UpdateGamePollInterval();
            if (AnyTaskRunning() && runtimeHeartbeat != null) KeepPageRunning();
            if (btnMain == null || btnDaily == null || btnArena == null) return;
            bool any = HasActiveTask;
            bool dailyActive = IsTaskRunning(AssistantTaskKind.Daily);
            bool arenaActive = IsTaskRunning(AssistantTaskKind.Arena);
            bool voidActive = IsTaskRunning(AssistantTaskKind.Void);
            bool mineActive = IsTaskRunning(AssistantTaskKind.Mine);
            bool workshopActive = IsTaskRunning(AssistantTaskKind.Workshop);
            bool guildActive = IsTaskRunning(AssistantTaskKind.GuildBoss);
            bool growthActive = IsTaskRunning(AssistantTaskKind.Growth);
            bool exchangeActive = IsTaskRunning(AssistantTaskKind.VoidExchange);
            if(btnDailyOptions!=null)btnDailyOptions.Enabled=!dailyActive;
            btnMain.Enabled = coreReady && (!any || (dailyActive && dailyEncounterPhase));
            btnDaily.Enabled = coreReady && !IsOtherTaskRunning(AssistantTaskKind.Daily);
            btnDaily.Text = dailyActive ? (escortFollowupRunning ? "停止矿车续跑" : (refineDaily ? "停止炼化续跑" : "停止日常")) : "一键日常";
            btnDaily.Selected = dailyActive;
            btnArena.Enabled = coreReady && !IsOtherTaskRunning(AssistantTaskKind.Arena);
            if (btnVoid != null) {
                btnVoid.Enabled = coreReady && !IsOtherTaskRunning(AssistantTaskKind.Void);
                btnVoid.Text = voidActive ? "停止虚空" : "开始虚空";
            }
            bool shortRow = activeUiLayout == UiLayoutList;
            bool compactStatus = activeUiLayout == UiLayoutStatus;
            if (numArenaCount != null) numArenaCount.Enabled = !any;
            btnArena.Text = arenaActive ? (shortRow ? "停止" : (compactStatus ? "停止竞技" : "停止竞技场")) : (shortRow ? "开始" : (compactStatus ? "竞技场" : "开始竞技场"));
            btnArena.Selected = arenaActive;
            if (btnMine != null) {
                btnMine.Enabled = coreReady && !IsOtherTaskRunning(AssistantTaskKind.Mine);
                btnMine.Text = mineActive ? (shortRow ? "停止" : "停止采集") : (shortRow ? "执行" : "矿山采集");
                btnMine.Selected = mineActive;
            }
            if (btnWorkshop != null) {
                btnWorkshop.Enabled = coreReady && !IsOtherTaskRunning(AssistantTaskKind.Workshop);
                btnWorkshop.Text = workshopActive ? (shortRow ? "停止" : "停止工坊") : (shortRow ? "执行" : "工坊");
                btnWorkshop.Selected = workshopActive;
            }
            if (btnGuildBoss != null) {
                btnGuildBoss.Enabled = coreReady && !IsOtherTaskRunning(AssistantTaskKind.GuildBoss);
                btnGuildBoss.Text = guildActive ? (shortRow ? "停止" : "停止首领") : (shortRow ? "挑战" : "工会首领");
                btnGuildBoss.Selected = guildActive;
            }
            if (btnWorkshopOptions != null) btnWorkshopOptions.Enabled = !any;
            if (btnRune != null) {
                bool active = growthActive && growthMode == "rune";
                btnRune.Enabled = coreReady && (growthActive ? growthMode == "rune" : !any);
                btnRune.Text = active ? (shortRow ? "停止" : "停止符文") : (shortRow ? "开始" : "符文连抽");
                btnRune.Selected = active;
            }
            if (btnDestiny != null) {
                bool active = growthActive && growthMode == "destiny";
                btnDestiny.Enabled = coreReady && (growthActive ? growthMode == "destiny" : !any);
                btnDestiny.Text = active ? (shortRow ? "停止" : "停止宿命") : (shortRow ? "开始" : "宿命强化");
                btnDestiny.Selected = active;
            }
            if (btnEarth != null) {
                bool active = growthActive && growthMode == "miningBoss";
                btnEarth.Enabled = coreReady && (growthActive ? growthMode == "miningBoss" : !any);
                btnEarth.Text = active ? (shortRow ? "停止" : "停止首领") : (shortRow ? "挑战" : "矿区首领");
                btnEarth.Selected = active;
            }
            if (btnGrowthOptions != null) btnGrowthOptions.Enabled = !any;
            if(btnOrePacks!=null) {
                bool active = growthActive && growthMode=="orepacks";
                btnOrePacks.Enabled=coreReady && (growthActive ? growthMode=="orepacks" : !any);
                btnOrePacks.Text = active ? (shortRow ? "停止" : "停止使用") : (shortRow ? "使用" : "使用矿石包");
                btnOrePacks.Selected = active;
            }
            if(btnPush!=null){
                bool active = growthActive && growthMode=="push";
                btnPush.Enabled=coreReady&&(growthActive?growthMode=="push":!any);
                btnPush.Text = active ? (shortRow ? "停止" : "停止推关") : (shortRow ? "开始" : "自动推关");
                btnPush.Selected = active;
            }
            if (btnVoidExchange != null) {
                btnVoidExchange.Enabled = coreReady && !IsOtherTaskRunning(AssistantTaskKind.VoidExchange);
                btnVoidExchange.Text = exchangeActive ? "停止兑换" : "兑换奖励";
            }
            UpdateTaskListRowHintsC();
        }

        private void ReportTaskMessage(string message, bool waiting)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (waiting && message != lastTaskMessage && DateTime.Now >= nextWaitLog) {
                AddLog(message,YellowColor);
                nextWaitLog = DateTime.Now.AddSeconds(30);
            }
            lastTaskMessage = message;
            SetStatusUi(null, cachedStatusColor, message);
            UpdateTaskListRowHintsC();
        }

        internal void RenderPreview(string path, int width, int height)
        {
            WindowState=FormWindowState.Normal;
            StartPosition=FormStartPosition.Manual;
            Location=new Point(-30000,-30000);
            ShowInTaskbar=false;
            ClientSize=new Size(width,height);
            Show();
            Application.DoEvents();
            CreateControl();
            LayoutAll(this);
            lblStatus.Text="营地待命";
            lblDetail.Text="选择一项任务，开始今天的冒险";
            if (lblMining != null) lblMining.Text="矿镐 --/--  ·  魔像 --  ·  深度 --";
            SetStatusUi("营地待命", GoldColor, "选择一项任务，开始今天的冒险");
            cachedDigCount = 12; cachedDigMax = 20; cachedGolemLv = 3; cachedDepth = 120;
            cachedRefineTemp = 3400; cachedRefineMax = 10000;
            cachedMineEarliest = "1号 06:42"; cachedMineEarliestSeconds = 402;
            EnableAllPreviewButtons();
            SetBadge("已就绪", GoldColor);
            if (lblVoidHint != null) lblVoidHint.Text="今日 · 周日攻略";
            cachedMinePlan = "按研究缺口选择：2号 → 4层";
            cachedMineSlots = "矿队：1号 2层 开采中 06:42 · 2号 空闲 · 3号 未解锁";
            if (lblMinePlan != null) lblMinePlan.Text=cachedMinePlan;
            if (lblMineSlots != null) lblMineSlots.Text=cachedMineSlots;
            AddLog("界面预览 · 支持四套可切换主面板排版",GoldColor);
            AddLog("战斗委托橙色 · 生产功能黄色 · 养成功能绿色",BlueColor);
            AddLog("可在助手设置→游戏偏好自由切换版本",GreenColor);
            UpdateSettingsSummary();
#if TEST_PVP_EDITION
            RenderTestPvpPreviews(path);
#else
            SwitchBottomTab("status"); LayoutAll(this); VerifyBattlePlaybackControls(); VerifyRichContentFonts(); SavePreview(path);
            SwitchBottomTab("stats"); LayoutAll(this); SavePreview(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-stats.png"));
            SwitchBottomTab("log"); LayoutAll(this); SavePreview(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-log.png"));
            SwitchBottomTab("status");
            SwitchTab("camp"); LayoutAll(this); SavePreview(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-camp.png"));
            SwitchTab("tasks"); LayoutAll(this);

            int savedMode = activeUiLayout;
            foreach (int mode in new[] { UiLayoutGrid, UiLayoutStatus, UiLayoutList })
            {
                SwitchUiLayout(mode);
                EnableAllPreviewButtons();
                SwitchTab("tasks");
                SwitchBottomTab(mode == UiLayoutStatus ? "log" : "status");
                LayoutAll(this); Application.DoEvents();
                VerifyBattlePlaybackControls();
                SaveSidebarPreview(Path.Combine(Path.GetDirectoryName(path), "preview-layout-" + mode + "-sidebar.png"));
                SwitchTab("camp");
                LayoutAll(this); Application.DoEvents();
                SaveSidebarPreview(Path.Combine(Path.GetDirectoryName(path), "preview-layout-" + mode + "-camp-sidebar.png"));
                SwitchBottomTab("stats");
                LayoutAll(this); Application.DoEvents();
                RecordUiTextIssues(this);
            }
            SwitchUiLayout(savedMode);
            EnableAllPreviewButtons();
            SwitchTab("tasks"); SwitchBottomTab("status"); LayoutAll(this);

            int savedTheme = UiTheme.CurrentTheme;
            int savedUserTheme = GetNumberOption("uiTheme", 0);
            string savedBg = customBgPath;
            int savedOpacity = UiTheme.CustomBackgroundOpacity;
            int savedBlur = UiTheme.GlassBlur;
            int savedFit = UiTheme.CustomBackgroundFit;
            string outDir = Path.GetDirectoryName(path);
            string sampleBg = Path.Combine(outDir, "custom-bg-sample.png");
            using (var bgBmp = new Bitmap(1280, 720))
            using (var gg = Graphics.FromImage(bgBmp))
            {
                gg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 1280, 720), Color.FromArgb(26, 62, 118), Color.FromArgb(214, 134, 88), 32f))
                    gg.FillRectangle(lg, 0, 0, 1280, 720);
                using (var glow = new SolidBrush(Color.FromArgb(120, 118, 88, 210)))
                    gg.FillEllipse(glow, 180, -80, 640, 520);
                using (var sunHalo = new SolidBrush(Color.FromArgb(75, 255, 210, 135)))
                    gg.FillEllipse(sunHalo, 760, 70, 340, 340);
                using (var sun = new SolidBrush(Color.FromArgb(215, 255, 224, 152)))
                    gg.FillEllipse(sun, 845, 155, 170, 170);
                using (var hillBack = new SolidBrush(Color.FromArgb(175, 28, 56, 86)))
                    gg.FillPolygon(hillBack, new Point[] { new Point(0, 560), new Point(220, 410), new Point(510, 505), new Point(820, 365), new Point(1100, 470), new Point(1280, 395), new Point(1280, 720), new Point(0, 720) });
                using (var hillFront = new SolidBrush(Color.FromArgb(215, 18, 36, 58)))
                    gg.FillPolygon(hillFront, new Point[] { new Point(0, 620), new Point(360, 480), new Point(660, 575), new Point(980, 445), new Point(1280, 540), new Point(1280, 720), new Point(0, 720) });
                bgBmp.Save(sampleBg, System.Drawing.Imaging.ImageFormat.Png);
            }
            for (int theme = 1; theme <= 4; theme++)
            {
                numberOptions["uiTheme"] = theme;
                string thBg = theme == UiTheme.ThemeLiquidGlass ? sampleBg : "";
                int thOp = theme == UiTheme.ThemeLiquidGlass ? 65 : 35;
                SwitchUiAppearance(0, theme, thBg, thOp, UiTheme.BgModeStatic, 12, UiTheme.BgFitCover);
                EnableAllPreviewButtons();
                SwitchTab("tasks"); SwitchBottomTab("status");
                LayoutAll(this); Application.DoEvents();
                VerifyBattlePlaybackControls();
                VerifyRichContentFonts();
                SaveSidebarPreview(Path.Combine(outDir, "preview-theme-" + theme + "-sidebar.png"));
                if (theme == UiTheme.ThemeLiquidGlass)
                {
                    SaveFullWindowPreview(Path.Combine(outDir, "preview-theme-4-full.png"));
                    SwitchUiAppearance(0, theme, thBg, thOp, UiTheme.BgModeStatic, 12, UiTheme.BgFitContain);
                    EnableAllPreviewButtons();
                    SwitchTab("tasks"); SwitchBottomTab("status");
                    LayoutAll(this); Application.DoEvents();
                    SaveFullWindowPreview(Path.Combine(outDir, "preview-theme-4-contain-full.png"));
                    SwitchUiAppearance(0, theme, thBg, thOp, UiTheme.BgModeStatic, 12, UiTheme.BgFitCover);
                }
                SwitchUiAppearance(2, theme, thBg, thOp, UiTheme.BgModeStatic, 12, UiTheme.BgFitCover);
                EnableAllPreviewButtons();
                SwitchBottomTab("log");
                LayoutAll(this); Application.DoEvents();
                SaveSidebarPreview(Path.Combine(outDir, "preview-theme-" + theme + "-layout2-sidebar.png"));
                using (var s = new AssistantSettingsForm(options, numberOptions, ScheduleBook.Migrate(new ScheduleSettings(), DateTime.Now), new Dictionary<int, string>(), 4095, "", "", "game", thBg, outDir, theme == UiTheme.ThemeLiquidGlass ? "液态示例背景" : ""))
                {
                    s.StartPosition = FormStartPosition.Manual; s.Location = new Point(-30000, -30000); s.ShowInTaskbar = false; s.Show();
                    s.SetCategory("game"); LayoutAll(s); Application.DoEvents(); RecordUiTextIssues(s);
                    using (var bm = new Bitmap(s.ClientSize.Width, s.ClientSize.Height))
                    {
                        s.Controls[0].DrawToBitmap(bm, new Rectangle(0, 0, bm.Width, bm.Height));
                        bm.Save(Path.Combine(outDir, "preview-theme-" + theme + "-settings.png"));
                    }
                }
            }
            foreach (int mode in new[] { UiLayoutGrid, UiLayoutStatus, UiLayoutList })
            {
                SwitchUiAppearance(mode, UiTheme.ThemeLiquidGlass, sampleBg, 65, UiTheme.BgModeStatic, 12, UiTheme.BgFitCover);
                EnableAllPreviewButtons();
                SwitchTab("tasks");
                SwitchBottomTab("log");
                LayoutAll(this);
                Application.DoEvents();
                SaveSidebarPreview(Path.Combine(outDir, "glass-layout-" + mode + "-log.png"));
                SwitchBottomTab("stats");
                LayoutAll(this);
                Application.DoEvents();
                SaveSidebarPreview(Path.Combine(outDir, "glass-layout-" + mode + "-stats.png"));
            }

            numberOptions["uiTheme"] = savedUserTheme;
            SwitchUiAppearance(0, 2, sampleBg, 40, UiTheme.BgModeStatic, savedBlur, UiTheme.BgFitCover);
            EnableAllPreviewButtons();
            SwitchTab("tasks"); SwitchBottomTab("status");
            LayoutAll(this); Application.DoEvents();
            SaveSidebarPreview(Path.Combine(outDir, "preview-theme-bg-sidebar.png"));
            SwitchUiAppearance(savedMode, savedTheme, savedBg, savedOpacity, UiTheme.CustomBackgroundMode, savedBlur, savedFit);
            EnableAllPreviewButtons();
            SwitchTab("tasks"); SwitchBottomTab("status"); LayoutAll(this);

            using (var cropDlg = new WallpaperCropForm(sampleBg, 1000, 600, 7400, 8200, ClientSize))
            {
                cropDlg.StartPosition = FormStartPosition.Manual; cropDlg.Location = new Point(-30000, -30000); cropDlg.ShowInTaskbar = false; cropDlg.Show();
                LayoutAll(cropDlg); Application.DoEvents();
                using (var bm = new Bitmap(cropDlg.Width, cropDlg.Height))
                {
                    cropDlg.DrawToBitmap(bm, new Rectangle(0, 0, cropDlg.Width, cropDlg.Height));
                    bm.Save(Path.Combine(outDir, "preview-crop-dialog.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            string mockWe = CreateMockWallpaperEngineDir(outDir);
            using (var picker = new WallpaperEnginePickerForm(mockWe))
            {
                picker.StartPosition = FormStartPosition.Manual; picker.Location = new Point(-30000, -30000); picker.ShowInTaskbar = false; picker.Show();
                LayoutAll(picker);
                Application.DoEvents();
                System.Threading.Thread.Sleep(550);
                Application.DoEvents();
                RecordUiTextIssues(picker);
                using (var bm = new Bitmap(picker.ClientSize.Width, picker.ClientSize.Height))
                {
                    picker.Controls[0].DrawToBitmap(bm, new Rectangle(0, 0, bm.Width, bm.Height));
                    bm.Save(Path.Combine(outDir, "preview-we-picker.png"));
                }
            }

            var researchTitles=new Dictionary<int,string>();
            for(int kind=1;kind<=6;kind++)for(int branch=1;branch<=2;branch++)
                researchTitles[kind*10+branch]=ResearchTitleOf(kind*10+branch)+"（25/100级）";

            using(var settings=new AssistantSettingsForm(options,numberOptions,ScheduleBook.Migrate(new ScheduleSettings(),DateTime.Now),researchTitles,4095,
                lblMinePlan!=null?lblMinePlan.Text:"",lblMineSlots!=null?lblMineSlots.Text:"","mining",
                customBgPath, DataRoot(), customBgLabel, wallpaperEngineDir, ClientSize, wallpaperEngineProject, playbackStatus, null,
                extremePerformanceMode, webViewStartedExtremePerf)) {
                settings.StartPosition=FormStartPosition.Manual;settings.Location=new Point(-30000,-30000);settings.ShowInTaskbar=false;settings.Show();
                foreach(string category in new[]{"mining","resources","daily","workshop","growth","mine","schedule","game","advanced"}) {
                    settings.SetCategory(category);LayoutAll(settings);Application.DoEvents();RecordUiTextIssues(settings);
                    using(var bitmap=new Bitmap(settings.ClientSize.Width,settings.ClientSize.Height)) {
                        settings.Controls[0].DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(path),"unified-"+category+".png"));
                    }
                    if (category == "game")
                    {
                        var wallpaper = (GameButton)settings.Controls.Find("WallpaperSettingsButton", true)[0];
                        using (var closeAfterCapture = new Timer { Interval = 100 })
                        {
                            closeAfterCapture.Tick += delegate
                            {
                                Form editor = Application.OpenForms["WallpaperSettingsDialog"];
                                if (editor == null) return;
                                closeAfterCapture.Stop();
                                try
                                {
                                    var mode = (ComboBox)editor.Controls.Find("CustomBgModeCombo", true)[0];
                                    mode.SelectedIndex = UiTheme.BgModeEngine;
                                    RecordUiTextIssues(editor);
                                    using (var bitmap = new Bitmap(editor.Width, editor.Height))
                                    {
                                        editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                        bitmap.Save(Path.Combine(Path.GetDirectoryName(path), "wallpaper-settings.png"));
                                    }
                                }
                                finally { editor.Close(); }
                            };
                            closeAfterCapture.Start();
                            wallpaper.PerformClick();
                        }
                    }
                    if(category=="game") {
                        var live = (GameCheckBox)settings.Controls.Find("LowDefinitionLiveCheck", true)[0];
                        bool wasEnabled = live.Checked;
                        live.Checked = !wasEnabled;
                        if (settings.Values["lowDefinitionLive"] != !wasEnabled)
                            throw new InvalidOperationException("Live resolution control failed to update the profile option");
                        live.Checked = wasEnabled;
                    }
                    if(category=="daily") {
                        foreach(string sub in new[]{"growth","battle","mine","tiers","shop"}) {
                            settings.SetDailySubCategory(sub);LayoutAll(settings);Application.DoEvents();RecordUiTextIssues(settings);
                            if(sub=="tiers" || sub=="shop") {
                                using(var bitmap=new Bitmap(settings.ClientSize.Width,settings.ClientSize.Height)) {
                                    settings.Controls[0].DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                                    bitmap.Save(Path.Combine(Path.GetDirectoryName(path),"unified-daily-"+sub+".png"));
                                }
                            }
                        }
                        var products = (CheckedListBox)settings.Controls.Find("MysteryProducts", true)[0];
                        if (products.Items.Count != 9 || settings.SelectedMysteryProducts().Count != 0)
                            throw new InvalidOperationException("Mystery shop settings must show nine unchecked categories offline");
                        settings.BindMysteryShop(new HashSet<string> { "2:29", "2:25", "2:39", "2:11" });
                        if (!settings.SelectedMysteryProducts().SetEquals(new[] { "2:29", "2:39" }))
                            throw new InvalidOperationException("Legacy selections must preserve supported items without authorizing extra ore packs");
                        products.SetItemChecked(2, true);
                        products.SetItemChecked(7, true);
                        if (!settings.SelectedMysteryProducts().SetEquals(new[] { "2:29", "2:25", "2:26", "2:27", "2:39", "1:10000048" }))
                            throw new InvalidOperationException("Mystery shop categories do not expand to the intended reward keys");
                        var shopCheck = (GameCheckBox)settings.Controls.Find("DailyMysteryShopCheck", true)[0];
                        var onlyMine = (GameButton)settings.Controls.Find("DailyOnlyMine", true)[0];
                        shopCheck.Checked = true;
                        onlyMine.PerformClick();
                        if (settings.Values["dailyMysteryShop"])
                            throw new InvalidOperationException("Mine-only daily selection retained paid mystery shop");
                        settings.SetDailySubCategory("rewards");
                    }
                }
            }
            using(var settings=new AccountSettingsForm("","",false)) {
                settings.StartPosition=FormStartPosition.Manual;settings.Location=new Point(-30000,-30000);settings.ShowInTaskbar=false;settings.Show();
                LayoutAll(settings);Application.DoEvents();RecordUiTextIssues(settings);
                using(var bitmap=new Bitmap(settings.ClientSize.Width,settings.ClientSize.Height)) {
                    settings.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(path),"AccountSettingsForm.png"));
                }
            }
            RenderChecks.Run(Path.GetDirectoryName(path));
            VerifyMiningStartDepth(Path.GetDirectoryName(path));
            string profilePreview=Path.Combine(Path.GetDirectoryName(path),"profile-preview-data");
            var profiles=new AccountProfiles(profilePreview);
            if(profiles.Read().Count==1){profiles.Save(null,"预览账号A","preview-only-a",false,null);profiles.Save(null,"预览账号B","preview-only-b",false,null);}
            using(var manager=new AccountManagerForm(profiles)) {
                manager.StartPosition=FormStartPosition.Manual;manager.Location=new Point(-30000,-30000);manager.ShowInTaskbar=false;manager.Show();LayoutAll(manager);Application.DoEvents();
                RecordUiTextIssues(manager);
                using(var bitmap=new Bitmap(manager.Width,manager.Height)){manager.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(path),"account-manager.png"));}
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"ui-text-check.json"),json.Serialize(new {font=UiTheme.GameFontFamily,buttonFit=uiTextProblems.Count==0,issues=uiTextProblems}));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"button-layout-check.json"),json.Serialize(new {result=buttonRatioProblems.Count==0?"passed":"failed",ratio="3:1",ink="#000000",fontPoints=10.5,largeFontStyle=UiTheme.ButtonLabelStyle,primaryFontPoints=btnMain.LabelBasePoints,checkedButtons=checkedButtons,issues=buttonRatioProblems}));
#endif
        }

        private void VerifyBattlePlaybackControls()
        {
            var speeds = BattleReplaySpeeds.Select(value =>
                (GameButton)Controls.Find("BattleReplaySpeed" + value, true).Single()).ToArray();
            var skip = (GameCheckBox)Controls.Find("MainBattleSkip", true).Single();
            if (speeds.Any(speed => !speed.Visible || !speed.Parent.ClientRectangle.Contains(speed.Bounds))
                || !skip.Visible || !skip.Parent.ClientRectangle.Contains(skip.Bounds))
                throw new InvalidOperationException("Battle playback controls are clipped or unavailable");

            var previousSpeed = speeds.Single(speed => speed.Selected);
            bool previousSkip = skip.Checked;
            speeds[3].PerformClick();
            skip.Checked = !previousSkip;
            if (GetNumberOption(BattleReplaySpeedOption, 100) != 200 || options["autoSkipBattle"] != !previousSkip
                || speeds.Single(speed => speed.Selected) != speeds[3])
                throw new InvalidOperationException("Main panel playback controls did not update options");
            var saved = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(OptionsPath()));
            if (GetInt(saved, BattleReplaySpeedOption) != 200 || GetBool(saved, "autoSkipBattle") != !previousSkip)
                throw new InvalidOperationException("Main panel playback controls did not save profile preferences");
            numberOptions[BattleReplaySpeedOption] = 100;
            LoadOptions();
            if (GetNumberOption(BattleReplaySpeedOption, 100) != 200)
                throw new InvalidOperationException("Battle replay speed did not survive loading the profile");
            previousSpeed.PerformClick();
            skip.Checked = previousSkip;
        }

        private void VerifyRichContentFonts()
        {
            string currentReport = rtbReport.Text;
            rtbReport.SetBodyText("第 2 场 · 魔像 15 Lv · 战斗掉落 250");
            try { VerifyVisibleRichContentFonts(); }
            finally { rtbReport.SetBodyText(currentReport); }
        }

        private void VerifyVisibleRichContentFonts()
        {
            using (var expected = GlassRichTextBox.BodyFont())
            {
                foreach (var box in new[] { txtLog, rtbReport })
                {
                    int start = box.SelectionStart, length = box.SelectionLength;
                    box.SelectAll();
                    string effective;
                    if (!((GlassRichTextBox)box).SelectionMatchesBodyFont(expected, out effective))
                        throw new InvalidOperationException((box == txtLog ? "Log" : "Report") + " RichEdit font "
                            + effective + ", expected " + expected.Name + " " + expected.SizeInPoints);
                    box.Select(start, length);
                }
            }
        }

        private void VerifyMiningStartDepth(string directory)
        {
            var checks=new List<string>();
            var current=new Dictionary<string,int>{{"horizontalStartDepth",175}};
            using(var settings=new AssistantSettingsForm(options,current,new ScheduleBook(),new Dictionary<int,string>(),4095,"","","mining")) {
                var found=settings.Controls.Find("MiningStartDepth",true);
                if(found.Length!=1 || ((GameNumber)found[0]).Value!=175)throw new InvalidOperationException("Mining depth not loaded into settings");
                checks.Add("selected depth appears in numeric control");
                ((GameNumber)found[0]).Value=235;
                if(settings.Numbers["horizontalStartDepth"]!=235 || current["horizontalStartDepth"]!=175)throw new InvalidOperationException("Mining depth edit did not stay isolated until save");
                checks.Add("editing affects only the dialog copy until saved");
                ((GameNumber)found[0]).Value=100000;
                if(settings.Numbers["horizontalStartDepth"]!=9999)throw new InvalidOperationException("Mining depth upper bound failed");
                ((GameNumber)found[0]).Value=0;
                if(settings.Numbers["horizontalStartDepth"]!=1)throw new InvalidOperationException("Mining depth lower bound failed");
                checks.Add("numeric range is 1 to 9999");
            }
            using(var defaults=new AssistantSettingsForm(options,new Dictionary<string,int>(),new ScheduleBook(),new Dictionary<int,string>(),4095,"","","mining")) {
                if(defaults.Numbers["horizontalStartDepth"]!=150)throw new InvalidOperationException("Legacy mining depth default changed");
                checks.Add("missing legacy setting defaults to 150");
            }
            int previous=GetNumberOption("horizontalStartDepth",150);
            try {
                numberOptions["horizontalStartDepth"]=275;SaveOptions();
                numberOptions["horizontalStartDepth"]=1;LoadOptions();
                if(GetNumberOption("horizontalStartDepth",150)!=275)throw new InvalidOperationException("Mining depth failed real options-file round trip");
                checks.Add("selected depth survives options-file save and reload");
            } finally {numberOptions["horizontalStartDepth"]=previous;SaveOptions();}
            using(var layoutDefaults=new AssistantSettingsForm(options,new Dictionary<string,int>(),new ScheduleBook(),new Dictionary<int,string>(),4095,"","","game")) {
                if(layoutDefaults.Numbers["uiLayout"]!=0)throw new InvalidOperationException("Legacy layout default changed");
                checks.Add("missing uiLayout defaults to 0 (classic)");
            }
            int prevLayout=GetNumberOption("uiLayout",0);
            try {
                numberOptions["uiLayout"]=2;SaveOptions();
                numberOptions["uiLayout"]=0;LoadOptions();
                if(GetNumberOption("uiLayout",0)!=2)throw new InvalidOperationException("uiLayout failed options-file round trip");
                checks.Add("uiLayout survives options-file save and reload");
            } finally {numberOptions["uiLayout"]=prevLayout;SaveOptions();}
            int prevTheme=GetNumberOption("uiTheme",0);
            int prevOp=GetNumberOption("customBgOpacity",35);
            int prevBgMode=GetNumberOption("customBgMode",0);
            int prevBgFit=GetNumberOption("customBgFit",0);
            int prevCropX=GetNumberOption("customBgCropX",0);
            int prevCropY=GetNumberOption("customBgCropY",0);
            int prevCropW=GetNumberOption("customBgCropW",10000);
            int prevCropH=GetNumberOption("customBgCropH",10000);
            int prevBlur=GetNumberOption("glassBlur",12);
            bool prevStage=!options.ContainsKey("stageWallpaperBg") || options["stageWallpaperBg"];
            string prevBgPath=customBgPath;
            string prevBgLbl=customBgLabel;
            string prevWeDir=wallpaperEngineDir;
            try {
                numberOptions["uiTheme"]=4;numberOptions["customBgOpacity"]=55;numberOptions["customBgMode"]=2;numberOptions["customBgFit"]=1;
                numberOptions["customBgCropX"]=1200;numberOptions["customBgCropY"]=800;numberOptions["customBgCropW"]=6800;numberOptions["customBgCropH"]=7400;
                numberOptions["glassBlur"]=18;
                options["stageWallpaperBg"]=false;
                customBgPath="sample-bg.png";customBgLabel="像素夜景";wallpaperEngineDir="C:\\Steam\\wallpaper_engine";SaveOptions();
                numberOptions["uiTheme"]=0;numberOptions["customBgOpacity"]=35;numberOptions["customBgMode"]=0;numberOptions["customBgFit"]=0;
                numberOptions["customBgCropX"]=0;numberOptions["customBgCropY"]=0;numberOptions["customBgCropW"]=10000;numberOptions["customBgCropH"]=10000;
                numberOptions["glassBlur"]=12;
                options["stageWallpaperBg"]=true;
                customBgPath="";customBgLabel="";wallpaperEngineDir="";LoadOptions();
                if(GetNumberOption("uiTheme",0)!=4 || GetNumberOption("customBgOpacity",35)!=55 || GetNumberOption("customBgMode",0)!=2 || GetNumberOption("customBgFit",0)!=1 || GetNumberOption("customBgCropX",0)!=1200 || GetNumberOption("customBgCropW",10000)!=6800 || GetNumberOption("glassBlur",12)!=18 || options["stageWallpaperBg"] || customBgPath!="sample-bg.png" || customBgLabel!="像素夜景" || wallpaperEngineDir!="C:\\Steam\\wallpaper_engine")
                    throw new InvalidOperationException("uiTheme/glassBlur/stageWallpaperBg/customBgFit/customBgCrop/customBg failed options-file round trip");
                using (var srcTest = new Bitmap(200, 120))
                using (var cropOut = UiTheme.CreateCroppedBitmap(srcTest, 1000, 1000, 5000, 5000))
                using (var blurOut = UiTheme.CreateBlurredBitmap(srcTest, 16))
                {
                    if (cropOut == null || cropOut.Width != 100 || cropOut.Height != 60) throw new InvalidOperationException("CreateCroppedBitmap dimensions mismatch");
                    if (blurOut == null || blurOut.Width < 10) throw new InvalidOperationException("CreateBlurredBitmap failed");
                }
                checks.Add("uiTheme (0..4), glassBlur, stageWallpaperBg, customBgFit, customBgCrop, customBgMode and Wallpaper Engine path survive round trip");
            } finally {numberOptions["uiTheme"]=prevTheme;numberOptions["customBgOpacity"]=prevOp;numberOptions["customBgMode"]=prevBgMode;numberOptions["customBgFit"]=prevBgFit;numberOptions["customBgCropX"]=prevCropX;numberOptions["customBgCropY"]=prevCropY;numberOptions["customBgCropW"]=prevCropW;numberOptions["customBgCropH"]=prevCropH;numberOptions["glassBlur"]=prevBlur;options["stageWallpaperBg"]=prevStage;customBgPath=prevBgPath;customBgLabel=prevBgLbl;wallpaperEngineDir=prevWeDir;SaveOptions();}
            string mockWeDir = Path.Combine(directory, "mock-we-verify");
            if (!Directory.Exists(mockWeDir)) mockWeDir = CreateMockWallpaperEngineDir(directory);
            List<WallpaperEngineItem> scannedWe = WallpaperEngineLocator.ScanWallpapers(mockWeDir);
            if (scannedWe.Count < 3 || !scannedWe.Exists(x => x.Title == "星露矿镇·暮色")) throw new InvalidOperationException("WallpaperEngineLocator.ScanWallpapers missing mock items, got " + scannedWe.Count);
            WallpaperEngineItem activeWe = WallpaperEngineLocator.ReadActiveDesktopWallpaper(mockWeDir);
            if (activeWe == null || activeWe.Title != "星露矿镇·暮色") throw new InvalidOperationException("ReadActiveDesktopWallpaper failed on mock config.json");
            WallpaperEngineItem gifItem = scannedWe.Find(x => x.Title == "像素篝火·动态" && x.HasAnimatedPreview);
            if (gifItem == null) throw new InvalidOperationException("Mock Wallpaper Engine GIF item missing");
            string importedWePng = UiTheme.ImportWallpaperEngineItem(gifItem, Path.Combine(directory, "we-import-data"));
            if (!File.Exists(importedWePng))
                throw new InvalidOperationException("ImportWallpaperEngineItem failed");
            if (UiTheme.ExtremePerformanceMode)
            {
                if (UiTheme.LoadCustomBackground(importedWePng, 40, UiTheme.BgModeAnimated))
                    throw new InvalidOperationException("ExtremePerformanceMode should skip animated GIF background load");
            }
            else if (!UiTheme.LoadCustomBackground(importedWePng, 40, UiTheme.BgModeAnimated))
                throw new InvalidOperationException("ImportWallpaperEngineItem or animated GIF load failed");
            UiTheme.ClearCustomBackground();
            UiTheme.LoadCustomBackground(customBgPath, GetNumberOption("customBgOpacity", 35), GetNumberOption("customBgMode", 0));
            checks.Add("Wallpaper Engine scanner, active config sync and import round trip succeed");
            var campButtons=new List<Control>();
            foreach(Control control in campGridHost.Controls)if(control is GameButton)campButtons.Add(control);
            if(campButtons.Count!=6 && campButtons.Count!=9)throw new InvalidOperationException("Camp settings grid must have 6 or 9 category buttons");
            checks.Add("camp settings grid has expected category buttons");
            Control p=btnOrePacks.Parent;
            while(p!=null && p!=pageTasks)p=p.Parent;
            if(p!=pageTasks)throw new InvalidOperationException("Ore packs button must live on Tasks page");
            checks.Add("ore packs action resides on tasks page");
            File.WriteAllText(Path.Combine(directory,"mining-settings-checks.json"),json.Serialize(new {result="passed",checks=checks}));
        }

        private static string CreateMockWallpaperEngineDir(string parentDir)
        {
            string root = Path.Combine(parentDir, "mock-we-verify");
            Directory.CreateDirectory(root);
            string p1 = WriteMockWeItem(root, "29001001", "星露矿镇·暮色", "scene", Color.FromArgb(44, 72, 114), Color.FromArgb(212, 142, 74), false);
            WriteMockWeItem(root, "29001002", "像素篝火·动态", "video", Color.FromArgb(88, 38, 22), Color.FromArgb(234, 178, 84), true);
            WriteMockWeItem(root, "29001003", "晨光山谷手绘", "image", Color.FromArgb(62, 112, 86), Color.FromArgb(236, 226, 178), false);
            var cfg = new Dictionary<string, object>
            {
                { "general", new Dictionary<string, object> {
                    { "user", new Dictionary<string, object> {
                        { "selectedwallpapers", new Dictionary<string, object> {
                            { "Monitor0", new Dictionary<string, object> { { "file", p1.Replace('\\', '/') } } }
                        }}
                    }}
                }}
            };
            File.WriteAllText(Path.Combine(root, "config.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cfg), System.Text.Encoding.UTF8);
            return root;
        }

        private static string WriteMockWeItem(string root, string id, string title, string type, Color c1, Color c2, bool withGif)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            using (var bmp = new Bitmap(240, 150))
            using (var g = Graphics.FromImage(bmp))
            using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 240, 150), c1, c2, 35f))
            {
                g.FillRectangle(lg, 0, 0, 240, 150);
                bmp.Save(Path.Combine(dir, "preview.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
                if (withGif) bmp.Save(Path.Combine(dir, "preview.gif"), System.Drawing.Imaging.ImageFormat.Gif);
            }
            string pj = Path.Combine(dir, "project.json");
            var meta = new Dictionary<string, object>
            {
                { "title", title },
                { "type", type },
                { "preview", withGif ? "preview.gif" : "preview.jpg" },
                { "file", type == "image" ? "preview.jpg" : (type == "video" ? "bg.mp4" : "scene.json") }
            };
            File.WriteAllText(pj, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(meta), System.Text.Encoding.UTF8);
            return pj;
        }

        private void EnableAllPreviewButtons()
        {
            if (btnMain != null) btnMain.Enabled = true;
            if (btnArena != null) btnArena.Enabled = true;
            if (btnDaily != null) btnDaily.Enabled = true;
            if (btnVoid != null) btnVoid.Enabled = true;
            if (btnMine != null) btnMine.Enabled = true;
            if (btnWorkshop != null) btnWorkshop.Enabled = true;
            if (btnGuildBoss != null) btnGuildBoss.Enabled = true;
            if (btnRune != null) btnRune.Enabled = true;
            if (btnDestiny != null) btnDestiny.Enabled = true;
            if (btnEarth != null) btnEarth.Enabled = true;
            if (btnOrePacks != null) btnOrePacks.Enabled = true;
            if (btnPush != null) btnPush.Enabled = true;
        }

        private void SaveSidebarPreview(string sidebarPath)
        {
            RecordUiTextIssues(this);
            var sidebar = Controls[0].Controls[0];
            using (var bitmap = new Bitmap(sidebar.Width, sidebar.Height))
            {
                sidebar.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(sidebarPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private void SaveFullWindowPreview(string fullPath)
        {
            RecordUiTextIssues(this);
            int w = Math.Max(1, ClientSize.Width);
            int h = Math.Max(1, ClientSize.Height);
            using (var bitmap = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(14, 20, 32));
                if (webView != null && webView.Width > 4 && webView.Height > 4)
                {
                    int webX = 0, webY = 0;
                    Control wc = webView;
                    while (wc != null && wc != this)
                    {
                        webX += wc.Left;
                        webY += wc.Top;
                        wc = wc.Parent;
                    }
                    var st = g.Save();
                    g.TranslateTransform(webX, webY);
                    UiTheme.DrawCustomBackground(g, webView, new Rectangle(0, 0, webView.Width, webView.Height), Color.FromArgb(14, 20, 32));
                    g.Restore(st);

                    int gameH = Math.Min(webView.Height - 40, Math.Max(220, webView.Height - 48));
                    int gameW = Math.Min(webView.Width - 60, Math.Max(180, (int)Math.Round(gameH * 9.0 / 16.0)));
                    int gx = webX + (webView.Width - gameW) / 2;
                    int gy = webY + (webView.Height - gameH) / 2;
                    using (var sh = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                        g.FillRectangle(sh, gx + 4, gy + 8, gameW, gameH);
                    using (var card = new SolidBrush(Color.FromArgb(24, 22, 18)))
                        g.FillRectangle(card, gx, gy, gameW, gameH);
                    if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass)
                    {
                        using (var rim = new Pen(Color.FromArgb(80, 255, 255, 255), 1.5f))
                            g.DrawRectangle(rim, gx, gy, gameW, gameH);
                    }
                    using (var fnt = UiTheme.UiFont(12f, FontStyle.Bold))
                        GameText.Draw(g, "冒险与挖矿 游戏画面区域", fnt, new Rectangle(gx, gy, gameW, gameH), Color.FromArgb(210, 240, 245, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                if (viewportBar != null && viewportBar.Width > 0 && viewportBar.Height > 0)
                {
                    int barX = 0, barY = 0;
                    Control bc = viewportBar;
                    while (bc != null && bc != this)
                    {
                        barX += bc.Left;
                        barY += bc.Top;
                        bc = bc.Parent;
                    }
                    using (var barBmp = new Bitmap(viewportBar.Width, viewportBar.Height))
                    {
                        viewportBar.DrawToBitmap(barBmp, new Rectangle(0, 0, barBmp.Width, barBmp.Height));
                        g.DrawImageUnscaled(barBmp, barX, barY);
                    }
                }
                var sidebar = Controls[0].Controls[0];
                if (sidebar != null && sidebar.Width > 0 && sidebar.Height > 0)
                {
                    using (var sideBmp = new Bitmap(sidebar.Width, sidebar.Height))
                    {
                        sidebar.DrawToBitmap(sideBmp, new Rectangle(0, 0, sideBmp.Width, sideBmp.Height));
                        g.DrawImageUnscaled(sideBmp, 0, 0);
                    }
                }
                bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private void SavePreview(string path)
        {
            RecordUiTextIssues(this);
            using(var bitmap=new Bitmap(ClientSize.Width,ClientSize.Height)) {
                Controls[0].DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            }
            var sidebar=Controls[0].Controls[0];
            using(var bitmap=new Bitmap(sidebar.Width,sidebar.Height)) {
                sidebar.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                bitmap.Save(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"-sidebar.png"),
                    System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        private static void LayoutAll(Control control)
        {
            control.CreateControl();control.PerformLayout();
            foreach(Control child in control.Controls)LayoutAll(child);
        }
    }
}
