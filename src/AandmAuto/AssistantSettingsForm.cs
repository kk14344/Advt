using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
	/// <summary>
	/// 统一助手设置窗口：左侧竖排九大分类，右侧直接展示对应配置项，避免多层嵌套弹窗。
	/// </summary>
	internal sealed class AssistantSettingsForm : CompositedForm
	{
		private static Color SideNavBg
		{
			get
			{
				switch (UiTheme.CurrentTheme)
				{
				case UiTheme.ThemeNativeLight: return Color.FromArgb(248, 247, 244);
				case UiTheme.ThemeWarmWood: return Color.FromArgb(110, 56, 22);
				case UiTheme.ThemeParchment: return Color.FromArgb(72, 57, 38);
				case UiTheme.ThemeLiquidGlass: return Color.FromArgb(22, 30, 46);
				default: return Color.FromArgb(52, 42, 26);
				}
			}
		}
		private static Color ContentBg { get { return UiTheme.Palette.CardColor; } }
		private static Color CardBorder { get { return UiTheme.Palette.BorderColor; } }
		private static Color GoldText { get { return UiTheme.Palette.GoldColor; } }
		private static Color BodyText { get { return UiTheme.Palette.TextColor; } }
		private static Color MutedText { get { return UiTheme.Palette.MutedColor; } }

		private readonly Dictionary<string, bool> values;
		private readonly Dictionary<string, int> numbers;
		private readonly ScheduleBook schedule;
		private readonly Dictionary<int, string> researchTitles;
		private readonly string minePlanText;
		private readonly string mineSlotsText;

		private readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
		private readonly Dictionary<string, GameButton> navButtons = new Dictionary<string, GameButton>();

		// 日常清单子页
		private readonly Dictionary<string, Panel> dailySubPages = new Dictionary<string, Panel>();
		private readonly Dictionary<string, GameButton> dailySubTabs = new Dictionary<string, GameButton>();
		private readonly Dictionary<string, CheckBox> dailyChecks = new Dictionary<string, CheckBox>();
		private string currentDailySub = "rewards";
		private GameButton btnDailyAll;
		private GameButton btnDailyNone;
		private readonly Dictionary<int, GameButton> alchemyButtons = new Dictionary<int, GameButton>();
		private SmoothLabel alchemyHint;
		private ComboBox cmbTroll;
		private ComboBox cmbUnderground;

		// 工坊时长按钮
		private readonly Dictionary<int, GameButton> minuteButtons = new Dictionary<int, GameButton>();

		// 矿山 12 项研究勾选
		private readonly Dictionary<int, GameCheckBox> researchChecks = new Dictionary<int, GameCheckBox>();

		// 主面板排版版本选择（0=现版1.44, 1=方案A, 2=方案B, 3=方案C）
		private readonly LayoutThumbnailPreview[] layoutThumbs = new LayoutThumbnailPreview[4];
		private readonly GameButton[] layoutSelectButtons = new GameButton[4];
		private readonly CardPanel[] layoutCards = new CardPanel[4];

		// 界面风格与自定义背景（0=现版暗褐, 1=羊皮纸书页, 2=原生浅灰, 3=复古暖木, 4=液态玻璃）
		private readonly ThemeSwatchPreview[] themeSwatches = new ThemeSwatchPreview[5];
		private readonly GameButton[] themeSelectButtons = new GameButton[5];
		private readonly CardPanel[] themeCards = new CardPanel[5];
		private string customBgPath = "";
		private string customBgLabel = "";
		private string wallpaperEngineDir = "";
		private string wallpaperEngineProject = "";
		private readonly string playbackInitialProject;
		private readonly string playbackStatus;
		private readonly Image playbackSnapshot;
		public string WallpaperEngineProject { get { return wallpaperEngineProject; } }
		public bool WallpaperEngineSelectionChanged { get; private set; }
		private readonly string dataRootPath = "";
		private SmoothLabel lblBgStatus;
		private SmoothLabel lblCropStatus;
		private ComboBox cmbBgMode;
		private SmoothLabel lblBgModeHint;
		private Size ownerWindowSize = new Size(1440, 940);
		private SmoothLabel lblDiskSummary;
		private TextBox txtDiskDetails;
		private ComboBox cmbAuditRetention;
		private readonly Action<string, Color> onLog;
		private readonly Func<string> getLogText;
		private SmoothLabel lblUpdateStatus;
		private SmoothLabel lblFooterVersion;

		// 定时任务子页控件
		private ListBox plansList;
		private ListBox stepsList;
		private TextBox schedName;
		private TextBox schedTimes;
		private TextBox schedDates;
		private TextBox schedStart;
		private TextBox schedEnd;
		private TextBox schedWindowStart;
		private TextBox schedWindowEnd;
		private ComboBox schedKind;
		private ComboBox schedTask;
		private CheckBox schedEnabled;
		private CheckBox schedLaunch;
		private CheckBox autoFollowupCheckin;
		private NumericUpDown schedInterval;
		private NumericUpDown schedWait;
		private NumericUpDown schedDuration;
		private readonly CheckBox[] schedDays = new CheckBox[7];
		private DateTimePicker schedAnchor;
		private int selectedPlan = -1;
		private bool loadingPlan;
		private bool initializing = true;
		private readonly string[] scheduleKinds = { "daily", "weekly", "dates", "interval" };

		public Dictionary<string, bool> Values { get { return values; } }
		public Dictionary<string, int> Numbers { get { return numbers; } }
		public ScheduleBook Schedule { get { return schedule; } }
		public bool ScheduleTouched { get; private set; }
		public string RunPlanId { get; private set; }
		public bool StopQueueRequested { get; private set; }
		public bool OpenDevTools { get; private set; }
		public bool OpenAccounts { get; private set; }
		public string CustomBackgroundPath { get { return customBgPath; } }
		public string CustomBackgroundLabel { get { return customBgLabel; } }
		public string WallpaperEngineDir { get { return wallpaperEngineDir; } }

		public AssistantSettingsForm(
			Dictionary<string, bool> currentOptions,
			Dictionary<string, int> currentNumbers,
			ScheduleBook currentSchedule,
			Dictionary<int, string> researchTitles,
			int currentResearchMask,
			string minePlanText,
			string mineSlotsText,
			string initialCategory = "mining",
			string currentCustomBgPath = "",
			string dataRootPath = "",
			string currentCustomBgLabel = "",
			string currentWeDir = "",
			Size ownerWindowSize = default(Size), string currentEngineProject = "", string currentPlaybackStatus = "", Image currentPlaybackSnapshot = null, Action<string, Color> onLogCallback = null, Func<string> getLogTextCallback = null)
		{
			onLog = onLogCallback;
			getLogText = getLogTextCallback;
			customBgPath = currentCustomBgPath ?? "";
			this.dataRootPath = dataRootPath ?? "";
			customBgLabel = currentCustomBgLabel ?? "";
			wallpaperEngineDir = currentWeDir ?? "";
			wallpaperEngineProject = currentEngineProject ?? "";
			playbackInitialProject = wallpaperEngineProject;
			playbackStatus = currentPlaybackStatus ?? "";
			playbackSnapshot = currentPlaybackSnapshot;
			if (ownerWindowSize.Width > 320 && ownerWindowSize.Height > 320) this.ownerWindowSize = ownerWindowSize;
			values = currentOptions != null ? new Dictionary<string, bool>(currentOptions) : new Dictionary<string, bool>();
			numbers = currentNumbers != null ? new Dictionary<string, int>(currentNumbers) : new Dictionary<string, int>();
			var serializer = new JavaScriptSerializer();
			schedule = currentSchedule != null
				? (serializer.Deserialize<ScheduleBook>(serializer.Serialize(currentSchedule)) ?? new ScheduleBook())
				: new ScheduleBook();
			if (schedule.Plans == null) schedule.Plans = new List<SchedulePlan>();
			if (schedule.Pending == null) schedule.Pending = new List<ScheduleRun>();
			if (schedule.Harvest == null) schedule.Harvest = new List<HarvestRun>();
			this.researchTitles = researchTitles ?? new Dictionary<int, string>();
			this.minePlanText = string.IsNullOrEmpty(minePlanText) ? "推荐矿层：等待研究信息" : minePlanText;
			this.mineSlotsText = string.IsNullOrEmpty(mineSlotsText) ? "矿队：等待矿山数据" : mineSlotsText;

			ClampDefaults(currentResearchMask);

			Text = "冒险与挖矿 · 助手设置";
			ClientSize = new Size(1020, 740);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			BackColor = UiTheme.Palette.BgColor;
			ForeColor = BodyText;
			Font = UiTheme.GameFont(9f);

			var root = new SettingsRootPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				Padding = new Padding(14, 10, 14, 10),
				Margin = Padding.Empty
			};
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

			root.Controls.Add(new GameHeader { Title = "助手设置", Hint = "", Dock = DockStyle.Fill }, 0, 0);

			var split = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 1,
				Margin = Padding.Empty,
				BackColor = Color.Transparent
			};
			split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152f));
			split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			split.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var navCard = new CardPanel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 4, 8, 4),
				Padding = new Padding(8, 12, 8, 10),
				BackColor = SideNavBg,
				BorderColor = CardBorder,
				CornerRadius = 7
			};
			var navFlow = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				BackColor = Color.Transparent,
				Margin = Padding.Empty
			};

			var contentCard = new CardPanel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 4, 0, 4),
				Padding = new Padding(14, 10, 14, 10),
				BackColor = ContentBg,
				BorderColor = CardBorder,
				CornerRadius = 7
			};
			var contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
			contentCard.Controls.Add(contentHost);

			string[,] categories =
			{
				{ "mining", "矿洞策略" },
				{ "resources", "资源与宝箱" },
				{ "daily", "日常清单" },
				{ "workshop", "工坊与工会" },
				{ "growth", "符文与宿命" },
				{ "mine", "矿山研究" },
				{ "schedule", "定时任务" },
				{ "game", "游戏偏好" },
				{ "advanced", "高级策略" }
			};

			bool glassUi = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			for (int i = 0; i < categories.GetLength(0); i++)
			{
				string key = categories[i, 0];
				var navBtn = new GameButton
				{
					Text = categories[i, 1],
					Size = glassUi ? new Size(134, 44) : new Size(126, 42),
					Margin = glassUi ? new Padding(0, 0, 0, 8) : new Padding(1, 0, 1, 10),
					Skin = GameButtonSkin.Stone,
					Font = UiTheme.GameFont(glassUi ? 9.5f : 9f, FontStyle.Bold)
				};
				navBtn.Click += delegate { SetCategory(key); };
				navButtons[key] = navBtn;
				navFlow.Controls.Add(navBtn);

				var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
				pages[key] = page;
				contentHost.Controls.Add(page);
			}
			navCard.Controls.Add(navFlow);

			BuildMiningPage(pages["mining"]);
			BuildResourcesPage(pages["resources"]);
			BuildDailyPage(pages["daily"]);
			BuildWorkshopPage(pages["workshop"]);
			BuildGrowthPage(pages["growth"]);
			BuildMineResearchPage(pages["mine"]);
			BuildSchedulePage(pages["schedule"]);
			BuildGamePage(pages["game"]);
			BuildAdvancedPage(pages["advanced"]);

			split.Controls.Add(navCard, 0, 0);
			split.Controls.Add(contentCard, 1, 0);
			root.Controls.Add(split, 0, 1);

			var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent };
			var btnFooterUpdate = new GameButton
			{
				Text = "检查更新",
				Location = new Point(0, 3),
				Size = new Size(98, 42),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			btnFooterUpdate.Click += delegate { OpenCheckUpdateDialog(); };
			var btnFooterDiag = new GameButton
			{
				Text = "导出诊断包",
				Location = new Point(106, 3),
				Size = new Size(106, 42),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			btnFooterDiag.Click += delegate { ExportDiagnosticBundle(); };
			lblFooterVersion = new SmoothLabel
			{
				Text = BuildEdition.EditionDisplayName + " v" + BuildEdition.AppVersion + " · " + UpdateManager.LastCheckSummary(),
				Location = new Point(222, 12),
				Size = new Size(340, 24),
				BackColor = Color.Transparent,
				ForeColor = MutedText,
				Font = UiTheme.GameFont(8.25f),
				TextAlign = ContentAlignment.MiddleLeft
			};
			footer.Controls.Add(btnFooterUpdate);
			footer.Controls.Add(btnFooterDiag);
			footer.Controls.Add(lblFooterVersion);
			var footerButtons = new FlowLayoutPanel
			{
				Dock = DockStyle.Right,
				Width = 280,
				FlowDirection = FlowDirection.RightToLeft,
				WrapContents = false,
				Margin = Padding.Empty,
				BackColor = Color.Transparent
			};
			var cancel = new GameButton { Text = "取消", Size = glassUi ? new Size(132, 42) : new Size(126, 42), Margin = new Padding(8, 3, 0, 0), Skin = GameButtonSkin.Stone, DialogResult = DialogResult.Cancel };
			var save = new GameButton { Text = "保存设置", Size = glassUi ? new Size(132, 42) : new Size(126, 42), Margin = new Padding(0, 3, 0, 0), Skin = GameButtonSkin.Green };
			save.Click += delegate
			{
				if (ScheduleTouched && !CommitSchedulePlan()) return;
				SyncScheduleSwitches();
				DialogResult = DialogResult.OK;
				Close();
			};
			footerButtons.Controls.Add(cancel);
			footerButtons.Controls.Add(save);
			footer.Controls.Add(footerButtons);

			AcceptButton = save;
			CancelButton = cancel;
			root.Controls.Add(footer, 0, 2);
			Controls.Add(root);

			initializing = false;
			SetCategory(initialCategory);
		}

		private void ClampDefaults(int currentResearchMask)
		{
			numbers["horizontalStartDepth"] = Clamp(GetNum("horizontalStartDepth", 150), 1, 9999);
			numbers["refineReserve"] = Clamp(GetNum("refineReserve", 1000), 0, 1000000);
			numbers["dailyAlchemyTier"] = Clamp(GetNum("dailyAlchemyTier", 1), 1, 3);
			numbers["dailyTrollLevel"] = Clamp(GetNum("dailyTrollLevel", 0), 0, 12);
			numbers["dailyUndergroundLevel"] = Clamp(GetNum("dailyUndergroundLevel", 0), 0, 12);
			numbers["workshopDepth"] = Clamp(GetNum("workshopDepth", 150), 0, 999);
			int mins = GetNum("workshopMinutes", 120);
			if (mins != 60 && mins != 120 && mins != 240) mins = 120;
			numbers["workshopMinutes"] = mins;
			numbers["guildBossCount"] = Clamp(GetNum("guildBossCount", 3), 1, 99);
			numbers["mineResearchMask"] = Clamp(currentResearchMask, 0, 4095);
			numbers["mineResearch"] = 0;
			numbers["uiLayout"] = Clamp(GetNum("uiLayout", 0), 0, 3);
			numbers["uiTheme"] = Clamp(GetNum("uiTheme", 0), 0, 4);
			numbers["customBgOpacity"] = Clamp(GetNum("customBgOpacity", 35), 10, 90);
			numbers["customBgMode"] = Clamp(GetNum("customBgMode", 0), 0, UiTheme.BgModeEngine);
			numbers["customBgFit"] = Clamp(GetNum("customBgFit", 0), 0, 1);
			int cw = Clamp(GetNum("customBgCropW", 10000), 500, 10000);
			int ch = Clamp(GetNum("customBgCropH", 10000), 500, 10000);
			numbers["customBgCropX"] = Clamp(GetNum("customBgCropX", 0), 0, 10000 - cw);
			numbers["customBgCropY"] = Clamp(GetNum("customBgCropY", 0), 0, 10000 - ch);
			numbers["customBgCropW"] = cw;
			numbers["customBgCropH"] = ch;
			numbers["glassBlur"] = Clamp(GetNum("glassBlur", 12), 0, 24);
			numbers["auditRetentionDays"] = Clamp(GetNum("auditRetentionDays", 14), 3, 90);
		}

		private int GetNum(string key, int fallback)
		{
			int v;
			return numbers.TryGetValue(key, out v) ? v : fallback;
		}

		private bool GetBool(string key, bool fallback = true)
		{
			bool v;
			return values.TryGetValue(key, out v) ? v : fallback;
		}

		private static int Clamp(int value, int min, int max)
		{
			return value < min ? min : (value > max ? max : value);
		}

		private static SmoothLabel SectionTitle(string text, int x, int y, int width = 600)
		{
			return new SmoothLabel
			{
				Text = text,
				Location = new Point(x, y),
				Size = new Size(width, 26),
				BackColor = Color.Transparent,
				ForeColor = GoldText,
				Font = UiTheme.GameFont(9.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			};
		}

		private GameCheckBox OptionCheck(Control parent, string key, string text, int x, int y, int width = 370, bool defaultVal = true)
		{
			var check = new GameCheckBox
			{
				Text = text,
				Location = new Point(x, y),
				Size = new Size(width, 32),
				BackColor = Color.Transparent,
				ForeColor = BodyText,
				Font = UiTheme.GameFont(9f),
				Checked = GetBool(key, defaultVal)
			};
			check.CheckedChanged += delegate { values[key] = check.Checked; };
			parent.Controls.Add(check);
			return check;
		}

		// ---------------- 1. 矿洞策略 ----------------
		private void BuildMiningPage(Panel page)
		{
			page.Controls.Add(SectionTitle("矿洞探索与战斗", 8, 8));
			string[,] opts =
			{
				{ "autoEnter", "自动进入矿区" },
				{ "autoMine", "自动挖矿" },
				{ "autoGolem", "优先处理魔像" },
				{ "autoBoss", "挑战矿区 BOSS" },
				{ "refuseEvents", "拒绝耗时事件" },
				{ "autoTreasureEvent", "处理藏宝箱事件" }
			};
			for (int i = 0; i < opts.GetLength(0); i++)
			{
				OptionCheck(page, opts[i, 0], opts[i, 1], 12 + (i % 2) * 390, 44 + (i / 2) * 40, 370);
			}

			page.Controls.Add(new SmoothLabel
			{
				Text = "开始挖掘层数",
				Location = new Point(12, 180),
				Size = new Size(140, 32),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = BodyText,
				BackColor = Color.Transparent
			});
			var depth = new GameNumber
			{
				Name = "MiningStartDepth",
				Minimum = 1,
				Maximum = 9999,
				Value = numbers["horizontalStartDepth"],
				Location = new Point(158, 178),
				Size = new Size(110, 32)
			};
			depth.ValueChanged += delegate { numbers["horizontalStartDepth"] = depth.Value; };
			page.Controls.Add(depth);
			page.Controls.Add(new SmoothLabel
			{
				Text = "层",
				Location = new Point(278, 180),
				Size = new Size(80, 32),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = MutedText,
				BackColor = Color.Transparent
			});
		}

		// ---------------- 2. 资源与宝箱 ----------------
		private void BuildResourcesPage(Panel page)
		{
			page.Controls.Add(SectionTitle("挖矿补给与自动开箱", 8, 8));
			OptionCheck(page, "autoUseMiningBag", "镐子不足时使用矿工包", 12, 44, 370);
			OptionCheck(page, "autoChest", "自动开启选定宝箱", 402, 44, 370);

			page.Controls.Add(SectionTitle("宝箱类别", 8, 96));

			string[,] chests =
			{
				{ "autoChest40", "开口宝箱 · 免费" },
				{ "autoChest41", "尘封宝箱 · 1 黏土钥匙" },
				{ "autoChest42", "秘密钥匙宝箱 · 3 黏土钥匙" },
				{ "autoChest43", "秘银宝箱 · 1 白银钥匙" },
				{ "autoChest44", "烈焰金宝箱 · 1 恶魔钥匙" },
				{ "autoChest45", "特殊机关宝箱 · 3 恶魔钥匙" },
				{ "autoChest48", "节日限定宝箱 · 免费/活动" }
			};
			for (int i = 0; i < chests.GetLength(0); i++)
			{
				OptionCheck(page, chests[i, 0], chests[i, 1], 12 + (i % 2) * 390, 132 + (i / 2) * 38, 370);
			}

			page.Controls.Add(SectionTitle("炼化库存保留", 8, 304));
			page.Controls.Add(new SmoothLabel
			{
				Text = "炼化每种材料保留量",
				Location = new Point(12, 340),
				Size = new Size(210, 32),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = BodyText,
				BackColor = Color.Transparent
			});
			var refineReserve = new GameNumber
			{
				Minimum = 0,
				Maximum = 1000000,
				Value = numbers["refineReserve"],
				Location = new Point(228, 338),
				Size = new Size(122, 32)
			};
			refineReserve.ValueChanged += delegate { numbers["refineReserve"] = refineReserve.Value; };
			page.Controls.Add(refineReserve);
			page.Controls.Add(new SmoothLabel
			{
				Text = "个",
				Location = new Point(360, 340),
				Size = new Size(80, 32),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = MutedText,
				BackColor = Color.Transparent
			});
		}

		// ---------------- 3. 日常清单（含炼金、副本、矿车内嵌） ----------------
		private void BuildDailyPage(Panel page)
		{
			var subNav = new FlowLayoutPanel
			{
				Location = new Point(8, 4),
				Size = new Size(792, 44),
				Margin = Padding.Empty,
				WrapContents = false,
				BackColor = Color.Transparent
			};
			var subHost = new Panel
			{
				Location = new Point(0, 52),
				Size = new Size(808, 496),
				BackColor = ContentBg
			};

			string[,] groups =
			{
				{ "rewards", "领取与社交" },
				{ "growth", "养成与生产" },
				{ "battle", "挑战与探索" },
				{ "mine", "矿山与研究" },
				{ "tiers", "炼金与副本" }
			};
			for (int i = 0; i < groups.GetLength(0); i++)
			{
				string key = groups[i, 0];
				var btn = new GameButton
				{
					Text = groups[i, 1],
					Size = new Size(120, 40),
					Margin = new Padding(0, 2, 12, 2),
					Skin = GameButtonSkin.Stone,
					Font = UiTheme.GameFont(9f, FontStyle.Bold)
				};
				btn.Click += delegate { SetDailySubCategory(key); };
				dailySubTabs[key] = btn;
				subNav.Controls.Add(btn);

				var sub = new Panel { Dock = DockStyle.Fill, BackColor = ContentBg, Visible = false };
				dailySubPages[key] = sub;
				subHost.Controls.Add(sub);
			}

			AddDailyGroup("rewards", new string[,]
			{
				{ "dailyTask1", "签到（活跃度）" },
				{ "dailyTask3", "好友赠送（1次）" },
				{ "dailyGiftAll", "好友点数送全部好友" },
				{ "dailyCheckinAll", "活动页签到（全时段）" },
				{ "dailyVipReward", "月卡每日奖励" },
				{ "dailyVoidExchange", "虚空商店兑换" },
				{ "voidExchangeGoldSoul", "虚空兑换金色灵魂碎片" },
				{ "voidExchangePurpleSoul", "虚空兑换紫色灵魂碎片" },
				{ "dailyMarketExchange", "集市矿产兑换与Bonus" },
				{ "dailyFreePrayer", "免费祈祷" },
				{ "dailyFreeAlchemy", "每日炼金" },
				{ "dailyRewards", "任务奖励与活跃宝箱" },
				{ "dailyMail", "一键领取全部邮件" },
				{ "dailyActivityRewards", "活动奖励一键领取" }
			});

			AddDailyGroup("growth", new string[,]
			{
				{ "dailyTask8", "活跃度召唤（友情优先）" },
				{ "dailyGoldSummon", "金币召唤（≤20M / 4次）" },
				{ "dailyRefine", "自动炼化及续跑" },
				{ "dailyTask15", "佣兵升级" },
				{ "dailyTask4", "佣兵强化" },
				{ "dailyTask13", "符文强化" },
				{ "dailyTask2", "工坊收取与生产" }
			});

			AddDailyGroup("battle", new string[,]
			{
				{ "dailyVoidChallenge", "虚空裂隙挑战（含招募）" },
				{ "dailyTask5", "免费竞技场" },
				{ "dailyTask6", "免费排位赛" },
				{ "dailyTask9", "巨魔巢穴（今日全项目）" },
				{ "dailyTask10", "地下探险（今日全项目）" },
				{ "dailyGuildBoss", "工会首领（买满+挑战）" },
				{ "dailyEvents", "合战与远征（买满打完）" },
				{ "dailyTask12", "首领试炼" },
				{ "dailyTask11", "矿区奇遇" },
				{ "dailyTask7", "矿车运送" },
				{ "dailyTask14", "矿车拦截" }
			});

			AddDailyGroup("mine", new string[,]
			{
				{ "dailyIncludeMine", "矿山采集与研究" },
				{ "dailyMineGuards", "矿山护卫（按研究缺料选对手）" }
			});

			BuildDailyTiersSubPage(dailySubPages["tiers"]);

			var actions = new FlowLayoutPanel
			{
				Location = new Point(8, 554),
				Size = new Size(792, 42),
				Margin = Padding.Empty,
				WrapContents = false,
				BackColor = Color.Transparent
			};
			btnDailyAll = new GameButton { Text = "本页全选", Size = new Size(114, 38), Margin = new Padding(0, 2, 10, 2), Skin = GameButtonSkin.Stone };
			btnDailyAll.Click += delegate { SelectDailyPage(true); };
			btnDailyNone = new GameButton { Text = "本页清空", Size = new Size(114, 38), Margin = new Padding(0, 2, 10, 2), Skin = GameButtonSkin.Stone };
			btnDailyNone.Click += delegate { SelectDailyPage(false); };
			var onlyMine = new GameButton { Text = "只跑矿山", Size = new Size(114, 38), Margin = new Padding(0, 2, 10, 2), Skin = GameButtonSkin.Stone };
			onlyMine.Click += delegate
			{
				foreach (var pair in dailyChecks) pair.Value.Checked = pair.Key == "dailyIncludeMine";
				SetDailySubCategory("mine");
			};
			actions.Controls.Add(btnDailyAll);
			actions.Controls.Add(btnDailyNone);
			actions.Controls.Add(onlyMine);

			page.Controls.Add(subNav);
			page.Controls.Add(subHost);
			page.Controls.Add(actions);
			SetDailySubCategory("rewards");
		}

		private void AddDailyGroup(string group, string[,] rows)
		{
			var panel = dailySubPages[group];
			for (int i = 0; i < rows.GetLength(0); i++)
			{
				string key = rows[i, 0];
				var check = OptionCheck(panel, key, rows[i, 1], 12 + (i % 2) * 392, 12 + (i / 2) * 42, 380);
				check.Tag = group;
				dailyChecks[key] = check;
			}
		}

		private void SelectDailyPage(bool value)
		{
			foreach (var pair in dailyChecks)
			{
				if ((string)pair.Value.Tag == currentDailySub) pair.Value.Checked = value;
			}
		}

		private void BuildDailyTiersSubPage(Panel panel)
		{
			panel.Controls.Add(SectionTitle("每日炼金方式（单选）", 12, 8));
			string[] tierNames = { "免费炼金", "中级血钻", "神圣血钻" };
			for (int i = 0; i < 3; i++)
			{
				int tier = i + 1;
				var btn = new GameButton
				{
					Text = tierNames[i],
					Location = new Point(12 + i * 150, 38),
					Size = new Size(132, 44),
					Skin = GameButtonSkin.Stone,
					Font = UiTheme.GameFont(9f, FontStyle.Bold)
				};
				btn.Click += delegate { SetAlchemyTier(tier); };
				alchemyButtons[tier] = btn;
				panel.Controls.Add(btn);
			}
			alchemyHint = new SmoothLabel
			{
				Location = new Point(14, 88),
				Size = new Size(760, 24),
				BackColor = Color.Transparent,
				ForeColor = GoldText,
				Font = UiTheme.GameFont(8.5f),
				TextAlign = ContentAlignment.MiddleLeft
			};
			panel.Controls.Add(alchemyHint);

			panel.Controls.Add(SectionTitle("副本难度档位", 12, 124));
			panel.Controls.Add(new SmoothLabel
			{
				Text = "巨魔巢穴",
				Location = new Point(12, 156),
				Size = new Size(96, 28),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = BodyText,
				BackColor = Color.Transparent
			});
			cmbTroll = LevelCombo(114, 154, numbers["dailyTrollLevel"]);
			cmbTroll.SelectedIndexChanged += delegate { numbers["dailyTrollLevel"] = Math.Max(0, Math.Min(12, cmbTroll.SelectedIndex)); };
			panel.Controls.Add(cmbTroll);

			panel.Controls.Add(new SmoothLabel
			{
				Text = "地下探险",
				Location = new Point(12, 194),
				Size = new Size(96, 28),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = BodyText,
				BackColor = Color.Transparent
			});
			cmbUnderground = LevelCombo(114, 192, numbers["dailyUndergroundLevel"]);
			cmbUnderground.SelectedIndexChanged += delegate { numbers["dailyUndergroundLevel"] = Math.Max(0, Math.Min(12, cmbUnderground.SelectedIndex)); };
			panel.Controls.Add(cmbUnderground);

			panel.Controls.Add(SectionTitle("矿车运送与拦截设置", 12, 240));
			OptionCheck(panel, "cartUpgradeMax", "免费刷新用完后一键升至最高级", 12, 274, 520);
			OptionCheck(panel, "cartBuyEscort", "运送次数不足时买满可购次数", 12, 312, 520);
			OptionCheck(panel, "cartPaidTargetRefresh", "拦截无 3/4 级目标时允许血钻刷新", 12, 350, 520);

			SetAlchemyTier(numbers["dailyAlchemyTier"]);
		}

		private static ComboBox LevelCombo(int x, int y, int selected)
		{
			var combo = new ComboBox
			{
				Location = new Point(x, y),
				Size = new Size(168, 30),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText,
				Font = UiTheme.GameFont(9f)
			};
			combo.Items.Add("自动最高（默认）");
			for (int lv = 1; lv <= 12; lv++) combo.Items.Add("Lv" + lv);
			combo.SelectedIndex = Clamp(selected, 0, 12);
			return combo;
		}

		private void SetAlchemyTier(int tier)
		{
			tier = Clamp(tier, 1, 3);
			numbers["dailyAlchemyTier"] = tier;
			foreach (var pair in alchemyButtons)
			{
				pair.Value.Selected = pair.Key == tier;
				pair.Value.Skin = pair.Key == tier ? GameButtonSkin.Gold : GameButtonSkin.Stone;
			}
			alchemyHint.Text = tier == 1
				? "0 血钻 · 炼金等级 +1"
				: tier == 2
					? "30 血钻 · 炼金等级 +1"
					: "60 血钻 · 炼金等级 +2";
		}

		// ---------------- 4. 工坊与工会 ----------------
		private void BuildWorkshopPage(Panel page)
		{
			page.Controls.Add(SectionTitle("工坊生产", 8, 8));
			page.Controls.Add(new SmoothLabel
			{
				Text = "目标深度",
				Location = new Point(12, 46),
				Size = new Size(92, 30),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = BodyText,
				BackColor = Color.Transparent
			});
			var depth = new GameNumber
			{
				Minimum = 0,
				Maximum = 999,
				Value = numbers["workshopDepth"],
				Location = new Point(112, 44),
				Size = new Size(94, 32)
			};
			depth.ValueChanged += delegate { numbers["workshopDepth"] = depth.Value; };
			page.Controls.Add(depth);
			page.Controls.Add(new SmoothLabel
			{
				Text = "层",
				Location = new Point(216, 46),
				Size = new Size(60, 30),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = MutedText,
				BackColor = Color.Transparent
			});

			page.Controls.Add(new SmoothLabel
			{
				Text = "时长档位",
				Location = new Point(12, 92),
				Size = new Size(92, 32),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = BodyText,
				BackColor = Color.Transparent
			});
			int x = 112;
			foreach (int option in new[] { 60, 120, 240 })
			{
				int value = option;
				var btn = new GameButton
				{
					Text = option + " 分钟",
					Location = new Point(x, 88),
					Size = new Size(108, 36),
					Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
					Skin = GameButtonSkin.Stone
				};
				btn.Click += delegate { SetWorkshopMinutes(value); };
				minuteButtons[value] = btn;
				page.Controls.Add(btn);
				x += 120;
			}
			SetWorkshopMinutes(numbers["workshopMinutes"]);

			OptionCheck(page, "autoUseEarthHeart", "日常与工坊矿镐不足时优先使用大地之心", 12, 140, 560);
			OptionCheck(page, "earthUseAll", "补充时一次用完大地之心", 12, 178, 560);

			page.Controls.Add(SectionTitle("工会首领", 8, 240));
			page.Controls.Add(new SmoothLabel
			{
				Text = "挑战次数",
				Location = new Point(12, 278),
				Size = new Size(92, 30),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = BodyText,
				BackColor = Color.Transparent
			});
			var bossCount = new GameNumber
			{
				Minimum = 1,
				Maximum = 99,
				Value = numbers["guildBossCount"],
				Location = new Point(112, 276),
				Size = new Size(94, 32)
			};
			bossCount.ValueChanged += delegate { numbers["guildBossCount"] = bossCount.Value; };
			page.Controls.Add(bossCount);
			page.Controls.Add(new SmoothLabel
			{
				Text = "次",
				Location = new Point(216, 278),
				Size = new Size(60, 30),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = MutedText,
				BackColor = Color.Transparent
			});
		}

		private void SetWorkshopMinutes(int value)
		{
			numbers["workshopMinutes"] = value;
			foreach (var pair in minuteButtons)
			{
				pair.Value.Selected = pair.Key == value;
				pair.Value.Skin = pair.Key == value ? GameButtonSkin.Gold : GameButtonSkin.Stone;
			}
		}

		// ---------------- 5. 符文与宿命 ----------------
		private void BuildGrowthPage(Panel page)
		{
			page.Controls.Add(SectionTitle("符文强化材料", 8, 8));
			OptionCheck(page, "runeUseFlashGold", "强化材料加入未锁定的闲置闪金符文", 12, 46, 620);
		}

		// ---------------- 6. 矿山研究 ----------------
		private void BuildMineResearchPage(Panel page)
		{
			page.Controls.Add(SectionTitle("矿山研究目标", 8, 6, 480));
			page.Controls.Add(new SmoothLabel
			{
				Text = minePlanText + "   |   " + mineSlotsText,
				Location = new Point(10, 34),
				Size = new Size(520, 24),
				BackColor = Color.Transparent,
				ForeColor = MutedText,
				Font = UiTheme.GameFont(8f),
				AutoEllipsis = true,
				TextAlign = ContentAlignment.MiddleLeft
			});

			var selectAll = new GameButton { Text = "全选十二项", Location = new Point(552, 14), Size = new Size(114, 38), Skin = GameButtonSkin.Stone };
			selectAll.Click += delegate
			{
				foreach (var pair in researchChecks) pair.Value.Checked = true;
			};
			var selectNone = new GameButton { Text = "全部清空", Location = new Point(676, 14), Size = new Size(114, 38), Skin = GameButtonSkin.Stone };
			selectNone.Click += delegate
			{
				foreach (var pair in researchChecks) pair.Value.Checked = false;
			};
			page.Controls.Add(selectAll);
			page.Controls.Add(selectNone);

			var grid = new TableLayoutPanel
			{
				Location = new Point(8, 64),
				Size = new Size(790, 520),
				ColumnCount = 2,
				RowCount = 6,
				Margin = Padding.Empty,
				BackColor = Color.Transparent
			};
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			for (int row = 0; row < 6; row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6f));

			int mask = numbers["mineResearchMask"];
			for (int kind = 1; kind <= 6; kind++)
			{
				for (int branch = 1; branch <= 2; branch++)
				{
					int index = (kind - 1) * 2 + branch - 1;
					int key = kind * 10 + branch;
					string rawTitle;
					if (!researchTitles.TryGetValue(key, out rawTitle)) rawTitle = MainForm.ResearchTitleOf(key) + "（等级待同步）";
					int split = rawTitle.IndexOf('（');
					string title = split >= 0 ? rawTitle.Substring(0, split) : rawTitle;
					string level = split >= 0 ? rawTitle.Substring(split).Trim('（', '）') : "等级待同步";

					var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(4, 3, 4, 3), Padding = new Padding(10, 3, 8, 3) };
					var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
					body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
					body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
					body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

					var check = new GameCheckBox
					{
						Text = title,
						Dock = DockStyle.Fill,
						Margin = Padding.Empty,
						ForeColor = BodyText,
						Font = UiTheme.GameFont(9f),
						Checked = (mask & (1 << index)) != 0
					};
					check.CheckedChanged += delegate { UpdateResearchMask(); };
					researchChecks[index] = check;
					body.Controls.Add(check, 0, 0);

					var info = new SmoothLabel
					{
						Text = level,
						Dock = DockStyle.Fill,
						BackColor = Color.Transparent,
						ForeColor = MutedText,
						Font = UiTheme.GameFont(8f),
						Padding = new Padding(23, 0, 0, 0),
						Cursor = Cursors.Hand
					};
					info.Click += delegate { check.Checked = !check.Checked; };
					body.Controls.Add(info, 0, 1);
					card.Controls.Add(body);
					grid.Controls.Add(card, branch - 1, kind - 1);
				}
			}
			page.Controls.Add(grid);
		}

		private void UpdateResearchMask()
		{
			int mask = 0;
			foreach (var pair in researchChecks)
			{
				if (pair.Value.Checked) mask |= 1 << pair.Key;
			}
			numbers["mineResearchMask"] = mask;
			numbers["mineResearch"] = 0;
		}

		// ---------------- 7. 定时任务 ----------------
		private void BuildSchedulePage(Panel page)
		{
			plansList = new ListBox
			{
				Location = new Point(8, 8),
				Size = new Size(176, 464),
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText,
				IntegralHeight = false
			};
			page.Controls.Add(plansList);

			var addPlan = SchedBtn("新增", 8, 480, 84, 28, delegate
			{
				ScheduleTouched = true;
				if (!CommitSchedulePlan()) return;
				schedule.Plans.Add(new SchedulePlan { Cursor = ScheduleBook.Stamp(DateTime.Now) });
				ReloadSchedulePlans(schedule.Plans.Count - 1);
			});
			var copyPlan = SchedBtn("复制", 100, 480, 84, 28, delegate
			{
				ScheduleTouched = true;
				if (!CommitSchedulePlan() || selectedPlan < 0) return;
				var j = new JavaScriptSerializer();
				var clone = j.Deserialize<SchedulePlan>(j.Serialize(schedule.Plans[selectedPlan]));
				clone.Id = Guid.NewGuid().ToString("N");
				clone.Name += " 副本";
				clone.Enabled = false;
				clone.Cursor = ScheduleBook.Stamp(DateTime.Now);
				schedule.Plans.Add(clone);
				ReloadSchedulePlans(schedule.Plans.Count - 1);
			});
			var delPlan = SchedBtn("删除计划", 8, 516, 108, 36, delegate
			{
				ScheduleTouched = true;
				if (selectedPlan < 0) return;
				schedule.Plans.RemoveAt(selectedPlan);
				selectedPlan = -1;
				ReloadSchedulePlans(0);
			});
			page.Controls.Add(addPlan);
			page.Controls.Add(copyPlan);
			page.Controls.Add(delPlan);

			schedLaunch = new GameCheckBox
			{
				Text = "自定义计划到点启动助手（Windows 已登录）",
				Location = new Point(198, 4),
				Size = new Size(590, 30),
				ForeColor = BodyText,
				BackColor = ContentBg,
				Checked = schedule.LaunchWhenClosed
			};
			schedLaunch.CheckedChanged += delegate { if (!initializing) ScheduleTouched = true; };
			page.Controls.Add(schedLaunch);

			schedEnabled = new GameCheckBox
			{
				Text = "启用此计划",
				Location = new Point(198, 38),
				Size = new Size(136, 28),
				ForeColor = BodyText,
				BackColor = ContentBg
			};
			schedEnabled.CheckedChanged += delegate { if (!loadingPlan && !initializing) ScheduleTouched = true; };
			page.Controls.Add(schedEnabled);

			SchedLabel(page, "计划名称", 344, 40, 82);
			schedName = SchedInput(page, 430, 38, 366);

			SchedLabel(page, "触发方式", 198, 74, 82);
			schedKind = new ComboBox
			{
				Location = new Point(284, 72),
				Size = new Size(130, 28),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText
			};
			schedKind.Items.AddRange(new object[] { "每天", "每周", "指定日期", "固定间隔" });
			schedKind.SelectedIndexChanged += delegate
			{
				UpdateTriggerState();
				if (!loadingPlan && !initializing) ScheduleTouched = true;
			};
			page.Controls.Add(schedKind);

			SchedLabel(page, "最长等待（分钟）", 432, 74, 150);
			schedWait = SchedNumber(page, 1, 10080, 586, 72, 96);

			SchedLabel(page, "每日时间", 198, 108, 82);
			schedTimes = SchedInput(page, 284, 106, 512);

			SchedLabel(page, "星期", 198, 142, 82);
			string[] dayLabels = { "日", "一", "二", "三", "四", "五", "六" };
			for (int i = 0; i < 7; i++)
			{
				var dayBox = new GameCheckBox
				{
					Text = dayLabels[i],
					Location = new Point(284 + i * 74, 140),
					Size = new Size(68, 28),
					ForeColor = BodyText,
					BackColor = Color.Transparent
				};
				dayBox.CheckedChanged += delegate { if (!loadingPlan && !initializing) ScheduleTouched = true; };
				schedDays[i] = dayBox;
				page.Controls.Add(dayBox);
			}

			SchedLabel(page, "指定日期", 198, 176, 82);
			schedDates = SchedInput(page, 284, 174, 512);

			SchedLabel(page, "间隔分钟", 198, 208, 82);
			schedInterval = SchedNumber(page, 1, 10080, 284, 208, 78);
			SchedLabel(page, "生效日期", 374, 208, 82);
			schedStart = SchedInput(page, 460, 208, 148);
			SchedLabel(page, "-", 614, 208, 14);
			schedEnd = SchedInput(page, 632, 208, 164);

			SchedLabel(page, "间隔时段", 198, 244, 82);
			schedWindowStart = SchedInput(page, 284, 242, 74);
			SchedLabel(page, "至", 362, 244, 22);
			schedWindowEnd = SchedInput(page, 388, 242, 74);
			SchedLabel(page, "起点", 472, 244, 48);
			schedAnchor = new DateTimePicker
			{
				Location = new Point(524, 242),
				Size = new Size(184, 28),
				Format = DateTimePickerFormat.Custom,
				CustomFormat = "yyyy-MM-dd HH:mm"
			};
			schedAnchor.ValueChanged += delegate { if (!loadingPlan && !initializing) ScheduleTouched = true; };
			page.Controls.Add(schedAnchor);

			stepsList = new ListBox
			{
				Location = new Point(198, 280),
				Size = new Size(482, 152),
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText,
				IntegralHeight = false
			};
			stepsList.SelectedIndexChanged += delegate
			{
				if (stepsList.SelectedItem != null) schedDuration.Value = ((ScheduleStep)stepsList.SelectedItem).Minutes;
			};
			page.Controls.Add(stepsList);

			page.Controls.Add(SchedBtn("上移", 688, 280, 108, 36, delegate { ScheduleTouched = true; MoveStep(-1); }));
			page.Controls.Add(SchedBtn("下移", 688, 326, 108, 36, delegate { ScheduleTouched = true; MoveStep(1); }));
			page.Controls.Add(SchedBtn("移除", 688, 372, 108, 36, delegate
			{
				ScheduleTouched = true;
				int n = stepsList.SelectedIndex;
				if (n >= 0)
				{
					stepsList.Items.RemoveAt(n);
					if (stepsList.Items.Count > 0) stepsList.SelectedIndex = Math.Min(n, stepsList.Items.Count - 1);
				}
			}));

			schedTask = new ComboBox
			{
				Location = new Point(198, 442),
				Size = new Size(194, 28),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText
			};
			for (int i = 0; i < ScheduleBook.Catalog.GetLength(0); i++) schedTask.Items.Add(ScheduleBook.Catalog[i, 1]);
			schedTask.SelectedIndex = 0;
			page.Controls.Add(schedTask);

			SchedLabel(page, "最多分", 398, 444, 62);
			schedDuration = SchedNumber(page, 0, 10080, 464, 442, 72);

			page.Controls.Add(SchedBtn("添加任务", 544, 440, 108, 36, delegate
			{
				ScheduleTouched = true;
				string id = ScheduleBook.Catalog[schedTask.SelectedIndex, 0];
				int m = (int)schedDuration.Value;
				if (m == 0 && (id == "autoMine" || id == "push" || id == "runeLoop")) m = 30;
				stepsList.Items.Add(new ScheduleStep { Task = id, Minutes = m });
			}));
			page.Controls.Add(SchedBtn("修改时长", 662, 440, 108, 36, delegate
			{
				ScheduleTouched = true;
				int n = stepsList.SelectedIndex;
				if (n < 0) return;
				var s = (ScheduleStep)stepsList.Items[n];
				s.Minutes = (int)schedDuration.Value;
				stepsList.Items[n] = s;
			}));

			autoFollowupCheckin = new GameCheckBox
			{
				Text = "自动续跑与签到（矿山 / 矿车 / 炼化 / 00:00·12:00·18:00）",
				Location = new Point(198, 484),
				Size = new Size(600, 30),
				ForeColor = BodyText,
				BackColor = ContentBg,
				Checked = schedule.MineFollowup || schedule.EscortFollowup || schedule.RefineFollowup || schedule.AutoCheckin
			};
			autoFollowupCheckin.CheckedChanged += delegate { if (!initializing) ScheduleTouched = true; };
			page.Controls.Add(autoFollowupCheckin);

			page.Controls.Add(new SmoothLabel
			{
				Text = FollowupSummary(),
				Location = new Point(198, 518),
				Size = new Size(600, 24),
				ForeColor = MutedText,
				BackColor = Color.Transparent,
				Font = UiTheme.GameFont(8f)
			});

			page.Controls.Add(SchedBtn("停止队列", 198, 550, 108, 36, delegate
			{
				StopQueueRequested = true;
				DialogResult = DialogResult.OK;
				Close();
			}));
			var runNow = SchedBtn("立即执行", 688, 550, 108, 36, delegate
			{
				ScheduleTouched = true;
				if (!CommitSchedulePlan() || selectedPlan < 0) return;
				SyncScheduleSwitches();
				RunPlanId = schedule.Plans[selectedPlan].Id;
				DialogResult = DialogResult.OK;
				Close();
			});
			runNow.Skin = GameButtonSkin.Gold;
			page.Controls.Add(runNow);

			plansList.SelectedIndexChanged += delegate
			{
				if (loadingPlan) return;
				int next = plansList.SelectedIndex;
				if (!CommitSchedulePlan())
				{
					loadingPlan = true;
					plansList.SelectedIndex = selectedPlan;
					loadingPlan = false;
					return;
				}
				LoadSchedulePlan(next);
			};

			ReloadSchedulePlans(0);
		}

		private string FollowupSummary()
		{
			return FollowupText("mine", "矿山") + "  ·  " + FollowupText("escort", "矿车") + "  ·  " + FollowupText("refine", "炼化");
		}

		private string FollowupText(string kind, string label)
		{
			var run = schedule.Harvest.Find(r => r.Task == kind);
			return run == null ? label + "待安排" : label + " " + run.DueUtc.ToLocalTime().ToString("MM/dd HH:mm");
		}

		private void SchedLabel(Control parent, string text, int x, int y, int w)
		{
			parent.Controls.Add(new SmoothLabel { Text = text, Location = new Point(x, y), Size = new Size(w, 28), TextAlign = ContentAlignment.MiddleLeft, ForeColor = BodyText, BackColor = Color.Transparent });
		}

		private TextBox SchedInput(Control parent, int x, int y, int w)
		{
			var tb = new TextBox
			{
				Location = new Point(x, y),
				Size = new Size(w, 28),
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText
			};
			tb.TextChanged += delegate { if (!loadingPlan && !initializing) ScheduleTouched = true; };
			parent.Controls.Add(tb);
			return tb;
		}

		private NumericUpDown SchedNumber(Control parent, int min, int max, int x, int y, int w)
		{
			var num = new NumericUpDown
			{
				Minimum = min,
				Maximum = max,
				Location = new Point(x, y),
				Size = new Size(w, 28),
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText
			};
			num.ValueChanged += delegate { if (!loadingPlan && !initializing) ScheduleTouched = true; };
			parent.Controls.Add(num);
			return num;
		}

		private static GameButton SchedBtn(string text, int x, int y, int w, int h, Action action)
		{
			var btn = new GameButton { Text = text, Location = new Point(x, y), Size = new Size(w, h), Skin = GameButtonSkin.Stone };
			btn.Click += delegate { action(); };
			return btn;
		}

		private void ReloadSchedulePlans(int index)
		{
			loadingPlan = true;
			plansList.Items.Clear();
			foreach (var p in schedule.Plans) plansList.Items.Add(p);
			plansList.SelectedIndex = schedule.Plans.Count == 0 ? -1 : Math.Min(index, schedule.Plans.Count - 1);
			loadingPlan = false;
			LoadSchedulePlan(plansList.SelectedIndex);
		}

		private void LoadSchedulePlan(int index)
		{
			selectedPlan = index;
			loadingPlan = true;
			try
			{
				if (index < 0)
				{
					stepsList.Items.Clear();
					return;
				}
				var p = schedule.Plans[index];
				schedName.Text = p.Name;
				schedEnabled.Checked = p.Enabled;
				schedKind.SelectedIndex = Math.Max(0, Array.IndexOf(scheduleKinds, p.Kind));
				schedTimes.Text = p.Times;
				schedDates.Text = p.Dates;
				schedInterval.Value = Math.Max(1, Math.Min(10080, p.IntervalMinutes));
				schedWait.Value = Math.Max(1, Math.Min(10080, p.MaxWaitMinutes));
				schedStart.Text = p.StartDate;
				schedEnd.Text = p.EndDate;
				schedWindowStart.Text = p.WindowStart;
				schedWindowEnd.Text = p.WindowEnd;
				DateTime v;
				schedAnchor.Value = DateTime.TryParse(p.Anchor, out v) ? v : DateTime.Now;
				for (int i = 0; i < 7; i++) schedDays[i].Checked = (p.Weekdays & (1 << i)) != 0;
				stepsList.Items.Clear();
				foreach (var s in p.Steps) stepsList.Items.Add(s);
				UpdateTriggerState();
			}
			finally
			{
				loadingPlan = false;
			}
		}

		private void UpdateTriggerState()
		{
			int k = schedKind.SelectedIndex;
			schedTimes.Enabled = k != 3;
			schedDates.Enabled = k == 2;
			schedInterval.Enabled = schedWindowStart.Enabled = schedWindowEnd.Enabled = k == 3;
			schedAnchor.Enabled = k == 3;
			foreach (var d in schedDays) if (d != null) d.Enabled = k == 1 || k == 3;
		}

		private bool CommitSchedulePlan()
		{
			if (selectedPlan < 0) return true;
			try
			{
				var p = schedule.Plans[selectedPlan];
				p.Name = schedName.Text.Trim();
				p.Enabled = schedEnabled.Checked;
				p.Kind = scheduleKinds[schedKind.SelectedIndex];
				p.Times = schedTimes.Text.Trim();
				p.Dates = schedDates.Text.Trim();
				p.IntervalMinutes = (int)schedInterval.Value;
				p.MaxWaitMinutes = (int)schedWait.Value;
				p.StartDate = schedStart.Text.Trim();
				p.EndDate = schedEnd.Text.Trim();
				p.WindowStart = schedWindowStart.Text.Trim();
				p.WindowEnd = schedWindowEnd.Text.Trim();
				p.Anchor = ScheduleBook.Stamp(schedAnchor.Value);
				p.Weekdays = 0;
				for (int i = 0; i < 7; i++) if (schedDays[i].Checked) p.Weekdays |= 1 << i;
				p.Steps = new List<ScheduleStep>();
				foreach (ScheduleStep s in stepsList.Items) p.Steps.Add(s);
				p.Validate();
				return true;
			}
			catch (Exception e)
			{
				SetCategory("schedule");
				MessageBox.Show(this, e.Message, "请检查定时计划");
				return false;
			}
		}

		private void MoveStep(int delta)
		{
			int n = stepsList.SelectedIndex;
			int to = n + delta;
			if (n < 0 || to < 0 || to >= stepsList.Items.Count) return;
			object v = stepsList.Items[n];
			stepsList.Items.RemoveAt(n);
			stepsList.Items.Insert(to, v);
			stepsList.SelectedIndex = to;
		}

		private void SyncScheduleSwitches()
		{
			schedule.LaunchWhenClosed = schedLaunch.Checked;
			bool autoAll = autoFollowupCheckin.Checked;
			schedule.MineFollowup = autoAll;
			schedule.EscortFollowup = autoAll;
			schedule.RefineFollowup = autoAll;
			schedule.AutoCheckin = autoAll;
		}

		// ---------------- 8. 游戏偏好 ----------------
		private void BuildGamePage(Panel page)
		{
			page.AutoScroll = true;
			page.Controls.Add(SectionTitle("游戏画面、声音与自定义背景", 8, 2));
			string[,] opts =
			{
				{ "autoSkipBattle", "自动跳过战斗播放" },
				{ "autoMute", "进入游戏后静音" },
				{ "followMiningCamera", "自动挖矿镜头跟随" },
				{ "showMiningRoute", "标记矿区目标和下一格" }
			};
			for (int i = 0; i < opts.GetLength(0); i++)
			{
				OptionCheck(page, opts[i, 0], opts[i, 1], 12 + (i % 2) * 290, 28 + (i / 2) * 30, 278);
			}

			var accBtn = new GameButton
			{
				Text = "账号与多开管理",
				Location = new Point(638, 30),
				Size = new Size(138, 46),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			accBtn.Click += delegate
			{
				OpenAccounts = true;
				if (ScheduleTouched && !CommitSchedulePlan()) return;
				SyncScheduleSwitches();
				DialogResult = DialogResult.OK;
				Close();
			};
			page.Controls.Add(accBtn);

			var bgBox = new InsetPanel
			{
				Location = new Point(10, 90),
				Size = new Size(766, 104),
				Padding = new Padding(8, 4, 8, 4)
			};
			bgBox.Controls.Add(new SmoothLabel
			{
				Text = "自定义背景图",
				Location = new Point(10, 7),
				Size = new Size(102, 26),
				BackColor = Color.Transparent,
				ForeColor = GoldText,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			});
			lblBgStatus = new SmoothLabel
			{
				Text = BackgroundStatus(),
				Location = new Point(114, 7),
				Size = new Size(250, 26),
				BackColor = Color.Transparent,
				ForeColor = MutedText,
				Font = UiTheme.GameFont(8f),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			};
			bgBox.Controls.Add(lblBgStatus);

			var btnPickBg = new GameButton
			{
				Text = "选择图片",
				Location = new Point(372, 5),
				Size = new Size(90, 30),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnPickBg.Click += delegate
			{
				using (var dlg = new OpenFileDialog())
				{
					dlg.Title = "选择自定义背景图片";
					dlg.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
					if (dlg.ShowDialog(this) != DialogResult.OK) return;
					try
					{
						customBgPath = UiTheme.ImportCustomBackgroundImage(dlg.FileName, dataRootPath);
						customBgLabel = Path.GetFileName(dlg.FileName);
						wallpaperEngineProject = "";
						if (cmbBgMode.SelectedIndex == UiTheme.BgModeEngine) cmbBgMode.SelectedIndex = UiTheme.BgModeStatic;
						ResetCropSelection();
						if (lblBgStatus != null) lblBgStatus.Text = BackgroundStatus();
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, "导入背景图失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				}
			};
			bgBox.Controls.Add(btnPickBg);

			var btnWeLib = new GameButton
			{
				Text = "壁纸引擎",
				Location = new Point(470, 5),
				Size = new Size(90, 30),
				Skin = GameButtonSkin.Gold,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnWeLib.Click += delegate
			{
				using (var picker = new WallpaperEnginePickerForm(wallpaperEngineDir))
				{
					if (picker.ShowDialog(this) != DialogResult.OK) return;
					if (!string.IsNullOrEmpty(picker.ResolvedCustomDir)) wallpaperEngineDir = picker.ResolvedCustomDir;
					ApplyWallpaperEngineSelection(picker.SelectedWallpaper, picker.ApplyToDesktop);
				}
			};
			bgBox.Controls.Add(btnWeLib);

			var btnSyncDesktop = new GameButton
			{
				Text = "同步桌面",
				Location = new Point(568, 5),
				Size = new Size(90, 30),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnSyncDesktop.Click += delegate
			{
				WallpaperEngineItem active = WallpaperEngineLocator.ReadActiveDesktopWallpaper(wallpaperEngineDir);
				if (active == null)
				{
					DialogResult ask = MessageBox.Show(
						this,
						"暂未检测到壁纸引擎当前壁纸。\r\n是否打开壁纸引擎壁纸库进行选择？",
						"壁纸引擎同步",
						MessageBoxButtons.YesNo,
						MessageBoxIcon.Question);
					if (ask == DialogResult.Yes) btnWeLib.PerformClick();
					return;
				}
				ApplyWallpaperEngineSelection(active, false);
			};
			bgBox.Controls.Add(btnSyncDesktop);

			var btnClearBg = new GameButton
			{
				Text = "清除背景",
				Location = new Point(666, 5),
				Size = new Size(90, 30),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnClearBg.Click += delegate
			{
				customBgPath = "";
				customBgLabel = "";
				wallpaperEngineProject = "";
				if (cmbBgMode.SelectedIndex == UiTheme.BgModeEngine) cmbBgMode.SelectedIndex = UiTheme.BgModeStatic;
				ResetCropSelection();
				if (lblBgStatus != null) lblBgStatus.Text = FormatCustomBgStatus("", "");
			};
			bgBox.Controls.Add(btnClearBg);

			bgBox.Controls.Add(new SmoothLabel
			{
				Text = "背景模式",
				Location = new Point(10, 40),
				Size = new Size(74, 26),
				BackColor = Color.Transparent,
				ForeColor = BodyText,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			});
			cmbBgMode = new ComboBox
			{
				Name = "CustomBgModeCombo",
				Location = new Point(86, 40),
				Size = new Size(194, 26),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText,
				Font = UiTheme.UiFont(8.25f)
			};
			cmbBgMode.Items.AddRange(new object[]
			{
				"静态高清（最省资源）",
				"动态预览图（GIF 动图）",
				"跟随桌面壁纸（实时）",
				"助手独立动态（原作品）"
			});
			cmbBgMode.SelectedIndex = Clamp(GetNum("customBgMode", 0), 0, UiTheme.BgModeEngine);
			bgBox.Controls.Add(cmbBgMode);

			bgBox.Controls.Add(new SmoothLabel
			{
				Text = "画面适配",
				Location = new Point(288, 40),
				Size = new Size(74, 26),
				BackColor = Color.Transparent,
				ForeColor = BodyText,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			});
			var cmbBgFit = new ComboBox
			{
				Name = "CustomBgFitCombo",
				Location = new Point(364, 40),
				Size = new Size(186, 26),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText,
				Font = UiTheme.UiFont(8.25f)
			};
			cmbBgFit.Items.AddRange(new object[]
			{
				"整窗合一铺满（推荐）",
				"完整原图（零裁剪）"
			});
			cmbBgFit.SelectedIndex = Clamp(GetNum("customBgFit", 0), 0, 1);
			cmbBgFit.SelectedIndexChanged += delegate
			{
				numbers["customBgFit"] = cmbBgFit.SelectedIndex;
			};
			bgBox.Controls.Add(cmbBgFit);

			lblCropStatus = new SmoothLabel
			{
				Text = FormatCropStatusText(),
				Location = new Point(556, 40),
				Size = new Size(104, 26),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? Color.White : MutedText,
				Font = UiTheme.GameFont(7.75f),
				TextAlign = ContentAlignment.MiddleRight,
				AutoEllipsis = true
			};
			bgBox.Controls.Add(lblCropStatus);

			var btnCropBg = new GameButton
			{
				Name = "CropCustomBgButton",
				Text = "裁剪区域",
				Location = new Point(666, 38),
				Size = new Size(90, 30),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnCropBg.Click += delegate
			{
				Image snapshot = GetPlaybackSnapshot();
				if (snapshot == null && (string.IsNullOrEmpty(customBgPath) || !File.Exists(customBgPath)))
				{
					MessageBox.Show(this, "请选择图片，或先应用独立壁纸并等待画面加载后再裁剪。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}
				using (var cropDlg = new WallpaperCropForm(
					customBgPath,
					GetNum("customBgCropX", 0),
					GetNum("customBgCropY", 0),
					GetNum("customBgCropW", 10000),
					GetNum("customBgCropH", 10000),
					ownerWindowSize, snapshot, GetNum("customBgFit", 0) == UiTheme.BgFitContain))
				{
					if (cropDlg.ShowDialog(this) != DialogResult.OK) return;
					numbers["customBgCropX"] = cropDlg.CropX;
					numbers["customBgCropY"] = cropDlg.CropY;
					numbers["customBgCropW"] = cropDlg.CropW;
					numbers["customBgCropH"] = cropDlg.CropH;
					if (lblCropStatus != null) lblCropStatus.Text = FormatCropStatusText();
				}
			};
			bgBox.Controls.Add(btnCropBg);

			bgBox.Controls.Add(new SmoothLabel
			{
				Text = "背景透度%",
				Location = new Point(10, 73),
				Size = new Size(86, 26),
				BackColor = Color.Transparent,
				ForeColor = BodyText,
				Font = UiTheme.GameFont(8f),
				TextAlign = ContentAlignment.MiddleLeft
			});
			var numBgOpacity = new GameNumber
			{
				Name = "CustomBgOpacity",
				Minimum = 10,
				Maximum = 90,
				Value = GetNum("customBgOpacity", 35),
				Location = new Point(98, 73),
				Size = new Size(64, 26),
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			numBgOpacity.ValueChanged += delegate { numbers["customBgOpacity"] = numBgOpacity.Value; };
			bgBox.Controls.Add(numBgOpacity);

			bgBox.Controls.Add(new SmoothLabel
			{
				Text = "玻璃模糊度",
				Location = new Point(176, 73),
				Size = new Size(90, 26),
				BackColor = Color.Transparent,
				ForeColor = BodyText,
				Font = UiTheme.GameFont(8f),
				TextAlign = ContentAlignment.MiddleLeft
			});
			var numGlassBlur = new GameNumber
			{
				Name = "GlassBlurLevel",
				Minimum = 0,
				Maximum = 24,
				Value = GetNum("glassBlur", 12),
				Location = new Point(268, 73),
				Size = new Size(64, 26),
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			numGlassBlur.ValueChanged += delegate { numbers["glassBlur"] = numGlassBlur.Value; };
			bgBox.Controls.Add(numGlassBlur);

			var chkStageBg = OptionCheck(bgBox, "stageWallpaperBg", "整窗透出壁纸（含右侧游戏底与顶栏）", 352, 72, 396, true);
			chkStageBg.Font = UiTheme.GameFont(8.25f);
			lblBgModeHint = null;
			cmbBgMode.SelectedIndexChanged += delegate
			{
				int previous = GetNum("customBgMode", UiTheme.BgModeStatic);
				if (previous == UiTheme.BgModeEngine && cmbBgMode.SelectedIndex == UiTheme.BgModeStatic)
				{
					Image snapshot = GetPlaybackSnapshot();
					if (snapshot != null)
					{
						try
						{
							string filename = "wallpaper-frame-" + Guid.NewGuid().ToString("N") + ".png";
							Directory.CreateDirectory(dataRootPath);
							string path = Path.Combine(dataRootPath, filename);
							snapshot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
							customBgPath = path;
						}
						catch (Exception ex)
						{
							MessageBox.Show(this, "保存静帧失败：" + ex.Message, "壁纸设置");
						}
					}
				}
				numbers["customBgMode"] = cmbBgMode.SelectedIndex;
				if (lblBgStatus != null) lblBgStatus.Text = BackgroundStatus();
			};

			page.Controls.Add(bgBox);

			page.Controls.Add(SectionTitle("界面风格", 8, 198));
			string[] themeTitles = { "现版暗褐", "羊皮纸书页", "原生浅灰", "复古暖木", "液态玻璃" };
			Color cardIdleBg = UiTheme.CurrentTheme == UiTheme.ThemeClassic ? Color.FromArgb(72, 59, 34) : UiTheme.Palette.InsetColor;
			for (int i = 0; i < 5; i++)
			{
				int themeMode = i;
				var tCard = new CardPanel
				{
					Location = new Point(10 + i * 155, 222),
					Size = new Size(147, 136),
					Padding = new Padding(6),
					BackColor = cardIdleBg,
					BorderColor = CardBorder,
					CornerRadius = 6,
					Cursor = Cursors.Hand
				};
				themeCards[i] = tCard;

				var swatch = new ThemeSwatchPreview(themeMode)
				{
					Location = new Point(8, 6),
					Size = new Size(131, 58)
				};
				swatch.Click += delegate { SelectUiTheme(themeMode); };
				themeSwatches[i] = swatch;
				tCard.Controls.Add(swatch);

				var titleLbl = new SmoothLabel
				{
					Text = themeTitles[i],
					Location = new Point(4, 68),
					Size = new Size(139, 22),
					BackColor = Color.Transparent,
					ForeColor = GoldText,
					Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
					TextAlign = ContentAlignment.MiddleCenter,
					Cursor = Cursors.Hand
				};
				titleLbl.Click += delegate { SelectUiTheme(themeMode); };
				tCard.Controls.Add(titleLbl);

				bool glassBtns = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
				var tBtn = new GameButton
				{
					Text = "选择",
					Location = glassBtns ? new Point(17, 94) : new Point(25, 96),
					Size = glassBtns ? new Size(112, 34) : new Size(96, 32),
					Skin = GameButtonSkin.Stone,
					Font = UiTheme.GameFont(glassBtns ? 8.5f : 8f, FontStyle.Bold)
				};
				tBtn.Click += delegate { SelectUiTheme(themeMode); };
				themeSelectButtons[i] = tBtn;
				tCard.Controls.Add(tBtn);

				tCard.Click += delegate { SelectUiTheme(themeMode); };
				page.Controls.Add(tCard);
			}

			page.Controls.Add(SectionTitle("助手主面板版本", 8, 362));

			string[] names = { "经典分层", "方案 A · 规整网格", "方案 B · 状态优先", "方案 C · 任务清单" };
			string[] descs = { "经典分层面板", "对称网格与动态", "顶部看板与长日志", "逐行状态与直达" };
			for (int i = 0; i < 4; i++)
			{
				int mode = i;
				var card = new CardPanel
				{
					Location = new Point(10 + i * 194, 386),
					Size = new Size(184, 222),
					Padding = new Padding(8),
					BackColor = cardIdleBg,
					BorderColor = CardBorder,
					CornerRadius = 6,
					Cursor = Cursors.Hand
				};
				layoutCards[i] = card;

				var thumb = new LayoutThumbnailPreview(mode)
				{
					Location = new Point(14, 6),
					Size = new Size(156, 128)
				};
				thumb.Click += delegate { SelectUiLayout(mode); };
				layoutThumbs[i] = thumb;
				card.Controls.Add(thumb);

				var titleLbl = new SmoothLabel
				{
					Text = names[i],
					Location = new Point(6, 138),
					Size = new Size(172, 20),
					BackColor = Color.Transparent,
					ForeColor = GoldText,
					Font = UiTheme.GameFont(8.75f, FontStyle.Bold),
					TextAlign = ContentAlignment.MiddleCenter,
					Cursor = Cursors.Hand
				};
				titleLbl.Click += delegate { SelectUiLayout(mode); };
				card.Controls.Add(titleLbl);

				var descLbl = new SmoothLabel
				{
					Text = descs[i],
					Location = new Point(6, 158),
					Size = new Size(172, 18),
					BackColor = Color.Transparent,
					ForeColor = MutedText,
					Font = UiTheme.GameFont(7.5f),
					TextAlign = ContentAlignment.MiddleCenter,
					Cursor = Cursors.Hand
				};
				descLbl.Click += delegate { SelectUiLayout(mode); };
				card.Controls.Add(descLbl);

				bool glassLBtn = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
				var btn = new GameButton
				{
					Text = "选择",
					Location = glassLBtn ? new Point(30, 180) : new Point(41, 180),
					Size = glassLBtn ? new Size(124, 34) : new Size(102, 34),
					Skin = GameButtonSkin.Stone,
					Font = UiTheme.GameFont(glassLBtn ? 8.5f : 8.25f, FontStyle.Bold)
				};
				btn.Click += delegate { SelectUiLayout(mode); };
				layoutSelectButtons[i] = btn;
				card.Controls.Add(btn);

				card.Click += delegate { SelectUiLayout(mode); };
				page.Controls.Add(card);
			}
			SelectUiTheme(GetNum("uiTheme", 0));
			SelectUiLayout(GetNum("uiLayout", 0));

			page.Controls.Add(SectionTitle("版本与在线更新", 8, 618));
			var updateBox = new InsetPanel
			{
				Location = new Point(10, 642),
				Size = new Size(766, 56),
				Padding = new Padding(10, 6, 10, 6)
			};
			updateBox.Controls.Add(new SmoothLabel
			{
				Text = BuildEdition.EditionDisplayName + " · v" + BuildEdition.AppVersion + "（通道：" + BuildEdition.UpdateChannel + "）",
				Location = new Point(12, 6),
				Size = new Size(560, 20),
				BackColor = Color.Transparent,
				ForeColor = GoldText,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			});
			lblUpdateStatus = new SmoothLabel
			{
				Text = "更新状态：" + UpdateManager.LastCheckSummary() + "（启动仅日志提示、不弹窗）",
				Location = new Point(12, 28),
				Size = new Size(498, 20),
				BackColor = Color.Transparent,
				ForeColor = MutedText,
				Font = UiTheme.GameFont(8f),
				TextAlign = ContentAlignment.MiddleLeft
			};
			var btnPageDiag = new GameButton
			{
				Text = "导出诊断包",
				Location = new Point(520, 10),
				Size = new Size(110, 36),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnPageDiag.Click += delegate { ExportDiagnosticBundle(); };
			var btnPageUpdate = new GameButton
			{
				Text = "检查更新",
				Location = new Point(638, 10),
				Size = new Size(114, 36),
				Skin = GameButtonSkin.Gold,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			btnPageUpdate.Click += delegate { OpenCheckUpdateDialog(); };
			updateBox.Controls.Add(lblUpdateStatus);
			updateBox.Controls.Add(btnPageDiag);
			updateBox.Controls.Add(btnPageUpdate);
			page.Controls.Add(updateBox);
		}

		private void ExportDiagnosticBundle()
		{
			string liveLog = getLogText != null ? getLogText() : "";
			DiagnosticExport.ExportInteractive(this, dataRootPath, liveLog, onLog);
		}

		private void OpenCheckUpdateDialog()
		{
			using (var dlg = new UpdateDialog(onLog))
			{
				dlg.ShowDialog(this);
			}
			string summary = UpdateManager.LastCheckSummary();
			if (lblUpdateStatus != null)
			{
				lblUpdateStatus.Text = "更新状态：" + summary + "（启动仅日志提示、不弹窗，按需手动更新）";
			}
			if (lblFooterVersion != null)
			{
				lblFooterVersion.Text = BuildEdition.EditionDisplayName + " v" + BuildEdition.AppVersion + " · " + summary;
			}
		}

		private void ApplyWallpaperEngineSelection(WallpaperEngineItem item, bool applyDesktop)
		{
			if (item == null) return;
			try
			{
				if (!string.IsNullOrEmpty(item.PlaybackSource))
				{
					wallpaperEngineProject = item.ProjectJsonPath;
					WallpaperEngineSelectionChanged = true;
					cmbBgMode.SelectedIndex = UiTheme.BgModeEngine;
				}
				else
				{
					if (item.Type != "image" && !string.IsNullOrEmpty(item.Type))
						throw new InvalidOperationException("找不到可播放的原始作品。请在壁纸库中重新定位作品，或用「选择图片」手动选择静帧。");
					string source = item.BestStaticImagePath;
					if (string.IsNullOrEmpty(source)) throw new FileNotFoundException("找不到壁纸原图。");
					using (Image.FromFile(source)) { }
					Directory.CreateDirectory(dataRootPath);
					string destination = Path.Combine(dataRootPath, "wallpaper-" + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
					File.Copy(source, destination);
					customBgPath = destination;
					wallpaperEngineProject = "";
					cmbBgMode.SelectedIndex = UiTheme.BgModeStatic;
				}
				customBgLabel = item.Title ?? Path.GetFileName(item.FolderPath);
				ResetCropSelection();
				if (lblBgStatus != null) lblBgStatus.Text = BackgroundStatus();
				if (lblBgModeHint != null && cmbBgMode != null)
				{
					if (item.HasAnimatedPreview && cmbBgMode.SelectedIndex == UiTheme.BgModeStatic)
						lblBgModeHint.Text = "所选壁纸含 GIF 动图，可将背景模式切换为「动态预览图」";
					else
						lblBgModeHint.Text = BgModeDescription(cmbBgMode.SelectedIndex);
				}
				if (applyDesktop && !string.IsNullOrEmpty(item.ProjectJsonPath))
				{
					WallpaperEngineLocator.ApplyToDesktop(string.IsNullOrEmpty(item.PlaybackSource) ? item.ProjectJsonPath : item.PlaybackSource, wallpaperEngineDir);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "应用壁纸失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private Image GetPlaybackSnapshot()
		{
			return GetNum("customBgMode", 0) == UiTheme.BgModeEngine
				&& string.Equals(playbackInitialProject, wallpaperEngineProject, StringComparison.OrdinalIgnoreCase)
				? playbackSnapshot : null;
		}

		private string BackgroundStatus()
		{
			if (GetNum("customBgMode", 0) == UiTheme.BgModeEngine && !string.IsNullOrEmpty(wallpaperEngineProject))
				return customBgLabel + (string.IsNullOrEmpty(playbackStatus) || WallpaperEngineSelectionChanged ? " · 原作品动态" : " · " + playbackStatus);
			return FormatCustomBgStatus(customBgPath, customBgLabel);
		}

		private static string BgModeDescription(int mode)
		{
			switch (mode)
			{
			case UiTheme.BgModeEngine: return "原作品在助手内独立播放。切换静态可保存当前帧，加载失败时保留备用背景。";
			case UiTheme.BgModeAnimated: return "播放本地 GIF 动图，最小化暂停";
			case UiTheme.BgModeDesktopPassthrough: return "仅侧栏透出桌面壁纸引擎实时画面，右侧游戏不变暗";
			default: return "使用选中的静态图片";
			}
		}

		private void ResetCropSelection()
		{
			numbers["customBgCropX"] = 0;
			numbers["customBgCropY"] = 0;
			numbers["customBgCropW"] = 10000;
			numbers["customBgCropH"] = 10000;
			if (lblCropStatus != null) lblCropStatus.Text = FormatCropStatusText();
		}

		private string FormatCropStatusText()
		{
			bool cropped = GetNum("customBgCropX", 0) > 0 || GetNum("customBgCropY", 0) > 0
				|| GetNum("customBgCropW", 10000) < 10000 || GetNum("customBgCropH", 10000) < 10000;
			return cropped ? "已自定义裁剪" : "默认整图显示";
		}

		private static string FormatCustomBgStatus(string path, string label)
		{
			if (string.IsNullOrEmpty(path)) return "未设置（可本地选图或连壁纸引擎）";
			string display = !string.IsNullOrEmpty(label) ? label : Path.GetFileName(path);
			try
			{
				if (File.Exists(path))
				{
					using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
					using (var img = Image.FromStream(fs, false, false))
					{
						if (img.Width < 900) return "已选：" + display + "（预览图分辨率较低）";
					}
				}
			}
			catch { }
			return "已选：" + display;
		}

		private void SelectUiTheme(int mode)
		{
			mode = Clamp(mode, 0, 4);
			numbers["uiTheme"] = mode;
			Color activeBorder = UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? UiTheme.Palette.AccentColor : UiTheme.Palette.GoldColor;
			for (int i = 0; i < 5; i++)
			{
				bool active = i == mode;
				if (themeSwatches[i] != null) themeSwatches[i].Selected = active;
				if (themeCards[i] != null)
				{
					themeCards[i].BorderColor = active ? activeBorder : CardBorder;
					themeCards[i].Invalidate();
				}
				if (themeSelectButtons[i] != null)
				{
					themeSelectButtons[i].Selected = active;
					themeSelectButtons[i].Skin = active ? GameButtonSkin.Gold : GameButtonSkin.Stone;
					themeSelectButtons[i].Text = active ? "当前使用" : "选择";
				}
			}
		}

		private void SelectUiLayout(int mode)
		{
			mode = Clamp(mode, 0, 3);
			numbers["uiLayout"] = mode;
			Color activeBorder = UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? UiTheme.Palette.AccentColor : UiTheme.Palette.GoldColor;
			for (int i = 0; i < 4; i++)
			{
				bool active = i == mode;
				if (layoutThumbs[i] != null) layoutThumbs[i].Selected = active;
				if (layoutCards[i] != null)
				{
					layoutCards[i].BorderColor = active ? activeBorder : CardBorder;
					layoutCards[i].Invalidate();
				}
				if (layoutSelectButtons[i] != null)
				{
					layoutSelectButtons[i].Selected = active;
					layoutSelectButtons[i].Skin = active ? GameButtonSkin.Gold : GameButtonSkin.Stone;
					layoutSelectButtons[i].Text = active ? "当前使用" : "选择";
				}
			}
		}

		// ---------------- 9. 高级策略 ----------------
		private void BuildAdvancedPage(Panel page)
		{
			page.Controls.Add(SectionTitle("特殊挖矿策略与任务通知", 8, 8));
			string[,] opts =
			{
				{ "autoCaveBoss", "挑战副本 BOSS" },
				{ "useTntOnLong", "长矿使用雷管" },
				{ "allowLongOre", "允许开采长矿" },
				{ "notifyDone", "任务完成弹出通知" }
			};
			for (int i = 0; i < opts.GetLength(0); i++)
			{
				OptionCheck(page, opts[i, 0], opts[i, 1], 12 + (i % 2) * 390, 46 + (i / 2) * 42, 370);
			}

			var devBtn = new GameButton
			{
				Text = "诊断工具",
				Location = new Point(12, 146),
				Size = new Size(120, 40),
				Skin = GameButtonSkin.Stone
			};
			devBtn.Click += delegate
			{
				OpenDevTools = true;
				if (ScheduleTouched && !CommitSchedulePlan()) return;
				SyncScheduleSwitches();
				DialogResult = DialogResult.OK;
				Close();
			};
			page.Controls.Add(devBtn);

			page.Controls.Add(SectionTitle("磁盘占用与缓存治理（保留登录态、计划、统计与当前壁纸）", 8, 204));
			var diskCard = new CardPanel
			{
				Location = new Point(10, 236),
				Size = new Size(790, 338),
				Padding = new Padding(12, 10, 12, 10),
				BackColor = UiTheme.Palette.InsetColor,
				BorderColor = CardBorder,
				CornerRadius = 6
			};

			diskCard.Controls.Add(new SmoothLabel
			{
				Text = "审计保留期",
				Location = new Point(12, 12),
				Size = new Size(88, 28),
				BackColor = Color.Transparent,
				ForeColor = BodyText,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			});
			cmbAuditRetention = new ComboBox
			{
				Location = new Point(102, 12),
				Size = new Size(92, 26),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText,
				Font = UiTheme.UiFont(8.5f)
			};
			int[] retentionChoices = { 7, 14, 30 };
			foreach (int d in retentionChoices) cmbAuditRetention.Items.Add(d + " 天");
			int curRet = GetNum("auditRetentionDays", 14);
			int retIdx = Array.IndexOf(retentionChoices, curRet);
			cmbAuditRetention.SelectedIndex = retIdx >= 0 ? retIdx : 1;
			cmbAuditRetention.SelectedIndexChanged += delegate
			{
				int idx = cmbAuditRetention.SelectedIndex;
				if (idx >= 0 && idx < retentionChoices.Length)
				{
					numbers["auditRetentionDays"] = retentionChoices[idx];
					RefreshDiskReport();
				}
			};
			diskCard.Controls.Add(cmbAuditRetention);

			var btnRefreshDisk = new GameButton
			{
				Text = "刷新统计",
				Location = new Point(378, 9),
				Size = new Size(104, 34),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnRefreshDisk.Click += delegate { RefreshDiskReport(); };
			diskCard.Controls.Add(btnRefreshDisk);

			var btnExportAudit = new GameButton
			{
				Text = "导出审计日志",
				Location = new Point(492, 9),
				Size = new Size(124, 34),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnExportAudit.Click += delegate
			{
				using (var dlg = new FolderBrowserDialog { Description = "选择审计日志导出目标文件夹" })
				{
					if (dlg.ShowDialog(this) != DialogResult.OK) return;
					string target = Path.Combine(dlg.SelectedPath, "AandmAuto-Audit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
					int copied = DiskGovernance.ExportAuditLogs(AppPaths.BaseRoot, target);
					MessageBox.Show(this, copied > 0 ? "已导出 " + copied + " 份审计文件到：\r\n" + target : "当前没有可导出的审计文件。", "导出审计日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			};
			diskCard.Controls.Add(btnExportAudit);

			var btnCleanDisk = new GameButton
			{
				Text = "清理临时缓存",
				Location = new Point(626, 9),
				Size = new Size(146, 34),
				Skin = GameButtonSkin.Gold,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnCleanDisk.Click += delegate
			{
				int retention = GetNum("auditRetentionDays", 14);
				DiskUsageReport preview = DiskGovernance.Scan(AppPaths.BaseRoot, customBgPath, retention);
				if (preview.TotalCleanableBytes <= 0)
				{
					MessageBox.Show(this, "当前没有可清理的临时缓存或过期审计日志。", "磁盘清理", MessageBoxButtons.OK, MessageBoxIcon.Information);
					RefreshDiskReport();
					return;
				}
				DialogResult confirm = MessageBox.Show(
					this,
					"预计可释放：" + DiskGovernance.FormatSize(preview.TotalCleanableBytes)
					+ "\r\n清理范围：浏览器临时缓存、过期审计日志、未使用的壁纸临时副本与引导缓存。"
					+ "\r\n保留项目：账号登录态（Cookie）、定时计划、挖矿统计与当前使用中的壁纸。",
					"确认清理临时缓存",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Question);
				if (confirm != DialogResult.Yes) return;
				DiskCleanupResult res = DiskGovernance.Clean(AppPaths.BaseRoot, customBgPath, retention);
				RefreshDiskReport("清理完成 · 预计释放 " + DiskGovernance.FormatSize(res.EstimatedBytes)
					+ " · 实际释放 " + DiskGovernance.FormatSize(res.FreedBytes)
					+ "（删除 " + res.DeletedFiles + " 项）");
			};
			diskCard.Controls.Add(btnCleanDisk);

			lblDiskSummary = new SmoothLabel
			{
				Text = "正在扫描磁盘占用…",
				Location = new Point(12, 52),
				Size = new Size(762, 26),
				BackColor = Color.Transparent,
				ForeColor = GoldText,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			};
			diskCard.Controls.Add(lblDiskSummary);

			txtDiskDetails = new TextBox
			{
				Location = new Point(12, 84),
				Size = new Size(762, 238),
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = BodyText,
				BorderStyle = BorderStyle.FixedSingle,
				Font = UiTheme.UiFont(8.75f)
			};
			diskCard.Controls.Add(txtDiskDetails);
			page.Controls.Add(diskCard);
			RefreshDiskReport();
		}

		private void RefreshDiskReport(string prefixNotice = "")
		{
			if (lblDiskSummary == null || txtDiskDetails == null) return;
			int retention = GetNum("auditRetentionDays", 14);
			DiskUsageReport report = DiskGovernance.Scan(AppPaths.BaseRoot, customBgPath, retention);
			string summary = "浏览器总占用 " + DiskGovernance.FormatSize(report.TotalBrowserDataBytes)
				+ " · 审计 " + DiskGovernance.FormatSize(report.TotalAuditBytes)
				+ " · 临时缓存/可清理 " + DiskGovernance.FormatSize(report.TotalCleanableBytes);
			lblDiskSummary.Text = string.IsNullOrEmpty(prefixNotice) ? summary : prefixNotice;
			var sb = new System.Text.StringBuilder();
			foreach (AccountDiskUsage acc in report.Accounts)
			{
				sb.AppendLine("账号：" + acc.Label + "（" + acc.ProfileId + "）");
				sb.AppendLine("  浏览器数据：" + DiskGovernance.FormatSize(acc.BrowserDataBytes)
					+ "（可清缓存 " + DiskGovernance.FormatSize(acc.BrowserCacheBytes) + "）"
					+ "  |  审计日志：" + DiskGovernance.FormatSize(acc.AuditBytes)
					+ "（过期/超限 " + DiskGovernance.FormatSize(acc.AuditCleanableBytes) + "）"
					+ "  |  未使用临时文件：" + DiskGovernance.FormatSize(acc.AppCacheBytes));
			}
			sb.AppendLine("公共引导资源缓存：" + DiskGovernance.FormatSize(report.SharedCacheBytes));
			if (!string.IsNullOrEmpty(prefixNotice))
				sb.AppendLine().AppendLine("当前剩余：" + summary);
			txtDiskDetails.Text = sb.ToString().TrimEnd();
		}

		internal void SetCategory(string category)
		{
			if (!pages.ContainsKey(category)) category = "mining";
			foreach (var pair in pages) pair.Value.Visible = pair.Key == category;
			foreach (var pair in navButtons)
			{
				bool active = pair.Key == category;
				pair.Value.Selected = active;
				pair.Value.Skin = active ? GameButtonSkin.Gold : GameButtonSkin.Stone;
			}
		}

		internal void SetDailySubCategory(string group)
		{
			if (!dailySubPages.ContainsKey(group)) group = "rewards";
			currentDailySub = group;
			foreach (var pair in dailySubPages) pair.Value.Visible = pair.Key == group;
			foreach (var pair in dailySubTabs)
			{
				bool active = pair.Key == group;
				pair.Value.Selected = active;
				pair.Value.Skin = active ? GameButtonSkin.Gold : GameButtonSkin.Stone;
			}
			bool editableChecks = group != "tiers";
			if (btnDailyAll != null) btnDailyAll.Visible = editableChecks;
			if (btnDailyNone != null) btnDailyNone.Visible = editableChecks;
		}
	}
}
