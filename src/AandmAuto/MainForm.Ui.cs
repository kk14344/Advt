using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		internal const int SidebarWidth = 480;

		private CheckBox chkArenaRefresh;

		private Label lblVoidHint = null;

		private ToolTip hints = new ToolTip();

		private Control pageTasks;

		private Control pageMine = null;

		private Control pageCamp;

		private GameButton tabTasks;

		private GameButton tabCamp;

		private GameButton btnMine;

		private GameButton btnMineResearch;

		private GameButton btnDailyOptions;

		private GameButton btnAdvanced;

		private GameButton btnChest;

		private GameButton btnSchedule;

		private HeaderIconButton btnHeaderAccount;

		private HeaderIconButton btnHeaderReload;

		private Panel campGridHost;

		private SmoothLabel lblSumMining;

		private SmoothLabel lblSumDaily;

		private SmoothLabel lblSumWorkshop;

		private SmoothLabel lblSumSchedule;

		private SmoothLabel lblStatusSchedule;

		private GameButton tabBottomStatus;

		private GameButton tabBottomStats;

		private GameButton tabBottomLog;

		private GameButton btnBottomReview;

		private GameButton btnBottomClearLog;

		private Control panelBottomStatus;

		private Control panelBottomStats;

		private Control panelBottomLog;

		private Label lblMinePlan;

		private Label lblMineReason = null;

		private Label lblMineSlots;

		private string currentTab = "tasks";

		private string currentBottomTab = "status";

		private void BuildInterface()
		{
			SuspendLayout();
			var root = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 1,
				Margin = Padding.Empty,
				BackColor = BgColor
			};
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SidebarWidth));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var scroll = new CompositedPanel
			{
				Dock = DockStyle.Fill,
				AutoScroll = false,
				Margin = Padding.Empty,
				BackColor = SideColor
			};
			sidebarScroll = scroll;
			scroll.SizeChanged += delegate { UpdateResponsiveHeights(); };
			BuildSidebarContents();

			var viewport = new WallpaperViewportPanel(() => UseNativeStageBackdrop)
			{
				Dock = DockStyle.Fill,
				BackColor = BgColor,
				Padding = new Padding((UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass || UiTheme.CustomBackground != null || UseNativeStageBackdrop) ? 0 : 3, 0, 0, 0),
				Margin = Padding.Empty
			};
			viewportPanel = viewport;
			viewport.SharedWallpaperPainter = graphics => PaintSharedWallpaper(graphics, viewport);
			var bar = new ViewportBarPanel
			{
				Dock = DockStyle.Top,
				Height = 32,
				BackColor = SideColor,
				Padding = new Padding(18, 0, 16, 0)
			};
			viewportBar = bar;
			bar.SharedWallpaperPainter = graphics => PaintSharedWallpaper(graphics, bar);
			lblViewportTitle = new SmoothLabel
			{
				Text = "冒险旅程",
				Dock = DockStyle.Left,
				Width = 290,
				BackColor = Color.Transparent,
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass || UiTheme.CustomBackground != null)
					? Color.White
					: (UiTheme.CurrentTheme == UiTheme.ThemeParchment ? Color.FromArgb(255, 238, 178) : (UiTheme.CurrentTheme == UiTheme.ThemeWarmWood ? Color.FromArgb(255, 228, 143) : GoldColor)),
				Font = UiTheme.GameFont(9f, FontStyle.Bold)
			};
			bar.Controls.Add(lblViewportTitle);
			bar.Controls.Add(new SmoothLabel
			{
				Text = "",
				Dock = DockStyle.Right,
				Width = 210,
				BackColor = Color.Transparent,
				TextAlign = ContentAlignment.MiddleRight,
				ForeColor = MutedColor,
				Font = UiTheme.GameFont(8f)
			});
			webView = new WebView2
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				DefaultBackgroundColor = UseNativeStageBackdrop ? Color.Transparent : BgColor,
				AllowExternalDrop = false
			};
			viewport.Controls.Add(webView);
			viewport.Controls.Add(bar);

			root.Controls.Add(scroll, 0, 0);
			root.Controls.Add(viewport, 1, 0);
			Controls.Add(root);

			pollTimer = new Timer { Interval = 1000 };
			pollTimer.Tick += PollTimer_Tick;
			dailyTaskTimer = new Timer { Interval = 45 };
			dailyTaskTimer.Tick += FastTaskTick;

			UpdateSettingsSummary();
			ApplyCachedStateToUi();
			ResumeLayout(true);
		}

		private void BuildSidebarContents()
		{
#if TEST_PVP_EDITION
			if (!testPvpShowLegacy)
			{
				sidebarScroll.Controls.Add(BuildTestPvpSidebar());
				return;
			}
#endif
			GameSidePanel side;
			switch (activeUiLayout)
			{
			case UiLayoutGrid:
				side = BuildSidebarLayoutA();
				break;
			case UiLayoutStatus:
				side = BuildSidebarLayoutB();
				break;
			case UiLayoutList:
				side = BuildSidebarLayoutC();
				break;
			default:
				side = BuildClassicSidebar();
				break;
			}
#if TEST_PVP_EDITION
			var wrap = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = SideColor };
			var backBar = BuildTestPvpLegacyBackBar();
			side.Dock = DockStyle.Fill;
			wrap.Controls.Add(side);
			wrap.Controls.Add(backBar);
			sidebarScroll.Controls.Add(wrap);
#else
			sidebarScroll.Controls.Add(side);
#endif
		}

		private GameSidePanel BuildClassicSidebar()
		{
			var side = new GameSidePanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 5,
				Padding = new Padding(12, 8, 12, 10),
				Margin = Padding.Empty
			};
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, (glass ? 58f : 56f) + BattlePlaybackRowHeight));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 42f : 38f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 322f : 308f));
			side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			sidebarLayout = side;
			activeSidePanel = side;

			side.Controls.Add(BuildHeader(), 0, 0);
			side.Controls.Add(BuildPrimaryActionsWithPlayback(BuildPrimaryAction()), 0, 1);
			side.Controls.Add(BuildTabs(), 0, 2);
			side.Controls.Add(BuildPages(), 0, 3);
			side.Controls.Add(BuildBottomDock(), 0, 4);
			return side;
		}

		private Control BuildHeader()
		{
			var header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4), BackColor = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? Color.Transparent : SideColor };
			Image cave = LoadArt("AandmAuto.mine.png");
			Image banner = GameArt.Get("game-maintitle_bg.png");
			Image headerBar = GameArt.Get("game-headerbar.png");
			Image rewardBg = GameArt.Get("game-rewardbtn_bg.png");
			Image icon = LoadArt("AandmAuto.icon.png");
			header.Disposed += delegate
			{
				try { if (cave != null) { cave.Dispose(); cave = null; } } catch { }
				try { if (icon != null) { icon.Dispose(); icon = null; } } catch { }
			};
			Font titleFont = UiTheme.GameFont(13f, FontStyle.Bold);
			Font subFont = UiTheme.GameFont(7.5f, FontStyle.Regular);
			Font nativeTitleFont = UiTheme.UiFont(11f, FontStyle.Bold);
			Font nativeSubFont = UiTheme.UiFont(7.75f, FontStyle.Regular);
			header.Paint += delegate(object sender, PaintEventArgs e)
			{
				Graphics g = e.Graphics;
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int mode = UiTheme.CurrentTheme;
				if (mode == UiTheme.ThemeLiquidGlass)
				{
					UiSurface.PaintBackdrop(header, g);
					Rectangle hr = new Rectangle(0, 0, Math.Max(1, header.Width - 1), Math.Max(1, header.Height - 1));
					using (GraphicsPath hp = UiTheme.RoundedRect(hr, 9))
						UiTheme.DrawGlassCardSurface(g, header, hp, 9, Color.FromArgb(14, 18, 24), false);
					if (icon != null)
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage(icon, new Rectangle(12, 13, 30, 30));
					}
					TextFormatFlags gFlags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
					GameText.Draw(g, "冒险与挖矿助手", nativeTitleFont, new Rectangle(49, 11, 210, 22), Color.FromArgb(150, 0, 0, 0), gFlags);
					GameText.Draw(g, "冒险与挖矿助手", nativeTitleFont, new Rectangle(48, 10, 210, 22), Color.White, gFlags);
					GameText.Draw(g, BuildEdition.HeaderSubtitle, nativeSubFont, new Rectangle(48, 33, 210, 16), Color.FromArgb(215, 255, 255, 255), gFlags);
					return;
				}
				if (mode == UiTheme.ThemeNativeLight)
				{
					g.Clear(Color.White);
					if (icon != null)
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage(icon, new Rectangle(12, 13, 30, 30));
					}
					TextFormatFlags nFlags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
					GameText.Draw(g, "冒险与挖矿助手", nativeTitleFont, new Rectangle(48, 10, 210, 22), UiTheme.Palette.TextColor, nFlags);
					GameText.Draw(g, BuildEdition.HeaderSubtitle, nativeSubFont, new Rectangle(48, 33, 210, 16), UiTheme.Palette.MutedColor, nFlags);
					using (var pen = new Pen(UiTheme.Palette.BorderColor, 1f))
						g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
					return;
				}
				if (mode == UiTheme.ThemeWarmWood)
				{
					g.SmoothingMode = SmoothingMode.None;
					using (var bg = new SolidBrush(Color.FromArgb(107, 55, 20)))
						g.FillRectangle(bg, 0, 0, header.Width, header.Height);
					using (var pen = new Pen(Color.FromArgb(41, 18, 4), 2f))
						g.DrawRectangle(pen, 1, 1, Math.Max(1, header.Width - 2), Math.Max(1, header.Height - 2));
					using (var hi = new Pen(Color.FromArgb(158, 88, 39)))
						g.DrawLine(hi, 2, 2, Math.Max(2, header.Width - 3), 2);
					using (var boxBg = new SolidBrush(Color.FromArgb(251, 232, 190)))
					using (var boxBorder = new Pen(Color.FromArgb(41, 18, 4)))
					{
						g.FillRectangle(boxBg, 10, 10, 36, 36);
						g.DrawRectangle(boxBorder, 10, 10, 36, 36);
					}
					if (icon != null)
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage(icon, new Rectangle(14, 14, 28, 28));
					}
					TextFormatFlags wFlags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
					GameText.Draw(g, "冒险与挖矿", titleFont, new Rectangle(56, 10, 196, 24), Color.FromArgb(41, 18, 4), wFlags);
					GameText.Draw(g, "冒险与挖矿", titleFont, new Rectangle(54, 8, 196, 24), Color.FromArgb(255, 228, 143), wFlags);
					GameText.Draw(g, BuildEdition.HeaderSubtitle, subFont, new Rectangle(54, 34, 196, 16), Color.FromArgb(230, 195, 142), wFlags);
					return;
				}
				if (mode == UiTheme.ThemeParchment)
				{
					if (headerBar != null)
					{
						g.InterpolationMode = InterpolationMode.NearestNeighbor;
						g.DrawImage(headerBar, new Rectangle(0, 0, header.Width, header.Height));
					}
					else
					{
						using (var bg = new SolidBrush(Color.FromArgb(92, 56, 28)))
							g.FillRectangle(bg, 0, 0, header.Width, header.Height);
					}
					if (banner != null)
					{
						g.InterpolationMode = InterpolationMode.NearestNeighbor;
						g.DrawImage(banner, new Rectangle(50, 4, 198, 38));
					}
					if (rewardBg != null)
					{
						g.InterpolationMode = InterpolationMode.NearestNeighbor;
						g.DrawImage(rewardBg, new Rectangle(8, 11, 34, 34));
					}
					if (icon != null)
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage(icon, new Rectangle(11, 14, 28, 28));
					}
					Rectangle pTitleRect = new Rectangle(68, 10, 162, 24);
					TextFormatFlags pFlags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
					GameText.Draw(g, "冒险与挖矿", titleFont, new Rectangle(pTitleRect.X + 1, pTitleRect.Y + 2, pTitleRect.Width, pTitleRect.Height), Color.FromArgb(165, 32, 16, 4), pFlags);
					GameText.Draw(g, "冒险与挖矿", titleFont, pTitleRect, Color.FromArgb(255, 244, 209), pFlags);
					GameText.Draw(g, BuildEdition.HeaderSubtitle, subFont, new Rectangle(62, 41, 176, 16), Color.FromArgb(236, 214, 170), pFlags);
					using (var pen = new Pen(LineColor, 1f)) g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
					return;
				}
				using (var brush = new LinearGradientBrush(new Rectangle(0, 0, header.Width, header.Height),
					Color.FromArgb(85, 64, 33), Color.FromArgb(53, 41, 25), 90f))
					g.FillRectangle(brush, 0, 0, header.Width, header.Height);
				if (cave != null)
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					g.DrawImage(cave, new Rectangle(header.Width - 148, 0, 148, header.Height));
					using (var shade = new LinearGradientBrush(new Rectangle(header.Width - 178, 0, 178, header.Height), SideColor,
						Color.FromArgb(80, SideColor), 0f))
						g.FillRectangle(shade, header.Width - 178, 0, 178, header.Height);
				}
				if (banner != null)
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					g.DrawImage(banner, new Rectangle(52, 4, 196, 38));
				}
				if (icon != null)
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					using (var glow = new SolidBrush(Color.FromArgb(55, 10, 7, 3)))
						g.FillEllipse(glow, 6, 12, 36, 36);
					g.DrawImage(icon, new Rectangle(4, 10, 38, 38));
					g.PixelOffsetMode = PixelOffsetMode.Half;
				}
				Rectangle titleRect = new Rectangle(68, 10, 162, 24);
				TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
				GameText.Draw(g, "冒险与挖矿", titleFont, new Rectangle(titleRect.X + 2, titleRect.Y + 2, titleRect.Width, titleRect.Height), Color.FromArgb(150, 0, 0, 0), flags);
				GameText.Draw(g, "冒险与挖矿", titleFont, titleRect, Color.FromArgb(255, 235, 158), flags);
				GameText.Draw(g, BuildEdition.HeaderSubtitle, subFont, new Rectangle(62, 42, 176, 16),
					Color.FromArgb(205, 189, 151), flags);
				using (var pen = new Pen(LineColor, 1f)) g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
			};

			var rightHost = new Panel { Dock = DockStyle.Right, Width = 104, BackColor = Color.Transparent };
			var btnHeaderSettings = new HeaderIconButton(HeaderIconKind.Settings) { Location = new Point(0, 3), Size = new Size(30, 30) };
#if TEST_PVP_EDITION
			btnHeaderSettings.Click += delegate { OpenOptions("game"); };
#else
			btnHeaderSettings.Click += delegate { OpenOptions("mining"); };
#endif
			hints.SetToolTip(btnHeaderSettings, "助手设置与检查更新");
			rightHost.Controls.Add(btnHeaderSettings);
			btnHeaderAccount = new HeaderIconButton(HeaderIconKind.Accounts) { Location = new Point(34, 3), Size = new Size(30, 30) };
			btnHeaderAccount.Click += BtnAccount_Click;
			hints.SetToolTip(btnHeaderAccount, "账号管理与多开同屏");

			btnHeaderReload = new HeaderIconButton(HeaderIconKind.Reload) { Location = new Point(70, 3), Size = new Size(30, 30) };
			btnHeaderReload.Click += BtnReload_Click;
			hints.SetToolTip(btnHeaderReload, "重新加载游戏页面");

			lblBadge = new BadgeLabel
			{
				Text = "准备中",
				Location = new Point(22, 36),
				Size = new Size(78, 20),
				TextAlign = ContentAlignment.MiddleCenter,
				ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : GoldColor,
				BackColor = UiTheme.Palette.BadgeBackColor,
				Font = UiTheme.GameFont(7.5f, FontStyle.Bold)
			};
			rightHost.Controls.Add(btnHeaderAccount);
			rightHost.Controls.Add(btnHeaderReload);
			rightHost.Controls.Add(lblBadge);
			header.Controls.Add(rightHost);
			return header;
		}

		private Control BuildPrimaryAction()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = glass ? new Padding(0, 3, 0, 5) : new Padding(0, 2, 0, 4), BackColor = Color.Transparent };
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			btnMain = new GameButton { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0), Text = "▶ 开始挖矿", Skin = GameButtonSkin.Green, Enabled = false };
			btnMain.Click += BtnMain_Click;
			hints.SetToolTip(btnMain, "自动挖掘；先处理魔像与宝箱，再向下探索。再次点击暂停。");
			btnDaily = new GameButton { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0), Text = "一键日常", Skin = GameButtonSkin.Gold, Enabled = false };
			btnDaily.Click += BtnDaily_Click;
			hints.SetToolTip(btnDaily, "按日常清单执行，购买次数按实际余额处理。");
			row.Controls.Add(btnMain, 0, 0);
			row.Controls.Add(btnDaily, 1, 0);
			return row;
		}

		private Control BuildTabs()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = glass ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 4), BackColor = Color.Transparent };
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tabTasks = TabButton("任务", delegate { SwitchTab("tasks"); });
			tabCamp = TabButton("设置", delegate { SwitchTab("camp"); });
			grid.Controls.Add(tabTasks, 0, 0);
			grid.Controls.Add(tabCamp, 1, 0);
			return grid;
		}

		private GameButton TabButton(string text, EventHandler click)
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var button = new GameButton
			{
				Text = text,
				Dock = DockStyle.Fill,
				Margin = glass ? new Padding(2, 1, 2, 1) : new Padding(4, 0, 4, 0),
				Font = UiTheme.GameFont(glass ? 9.5f : 9f, FontStyle.Bold),
				Skin = GameButtonSkin.Stone
			};
			button.Click += click;
			return button;
		}

		private Control BuildPages()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var host = new Panel { Dock = DockStyle.Fill, Margin = glass ? new Padding(0, 0, 0, 8) : new Padding(0, 0, 0, 4), BackColor = Color.Transparent };
			pageTasks = BuildTasksPage();
			pageCamp = BuildCampPage();
			host.Controls.Add(pageCamp);
			host.Controls.Add(pageTasks);
			SwitchTab("tasks");
			return host;
		}

		private TableLayoutPanel NewPage()
		{
			var page = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				Margin = Padding.Empty,
				BackColor = Color.Transparent,
				Padding = Padding.Empty
			};
			page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			return page;
		}

		private Control BuildTasksPage()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var page = NewPage();
			page.RowCount = 3;
			page.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 98f : 96f));
			page.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 86f : 84f));
			page.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 128f : 124f));
			page.Controls.Add(BuildArenaCard(), 0, 0);
			page.Controls.Add(BuildCombatCard(), 0, 1);
			page.Controls.Add(BuildProductionGrowthCard(), 0, 2);
			return page;
		}

		private Control BuildArenaCard()
		{
			var card = new CardPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10, 5, 10, 5),
				Margin = new Padding(0, 0, 0, 4),
				BackColor = CardColor,
				BorderColor = LineColor,
				CornerRadius = 6
			};
			var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
			body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			body.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));
			body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			body.Controls.Add(new GameHeader { Title = "竞技场", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);

			var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
			split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
			split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
			split.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 0, 6, 0) };
			left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			left.RowStyles.Add(new RowStyle(SizeType.Absolute, 29f));
			left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var countRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(2, 1, 4, 1) };
			countRow.Controls.Add(new SmoothLabel
			{
				Text = "挑战轮次",
				Dock = DockStyle.Left,
				Width = 86,
				ForeColor = MutedColor,
				TextAlign = ContentAlignment.MiddleLeft,
				Font = UiTheme.GameFont(8.5f),
				BackColor = Color.Transparent
			});
			numArenaCount = new GameNumber
			{
				Minimum = 1,
				Maximum = 99,
				Value = savedArenaCount,
				Width = 68,
				Dock = DockStyle.Right,
				Font = UiTheme.GameFont(9f, FontStyle.Bold)
			};
			countRow.Controls.Add(numArenaCount);
			left.Controls.Add(countRow, 0, 0);

			chkArenaRefresh = new GameCheckBox
			{
				Text = "允许血钻刷新",
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				BackColor = CardColor,
				ForeColor = GoldColor,
				Font = UiTheme.GameFont(8.5f),
				Checked = options.ContainsKey("arenaAllowBloodRefresh") && options["arenaAllowBloodRefresh"],
				Tag = "arenaAllowBloodRefresh"
			};
			chkArenaRefresh.CheckedChanged += Option_CheckedChanged;
			optionChecks["arenaAllowBloodRefresh"] = chkArenaRefresh;
			hints.SetToolTip(chkArenaRefresh, "每轮依次挑战九名对手；结束后先免费刷新，再按游戏当前费用使用血钻。");
			left.Controls.Add(chkArenaRefresh, 0, 1);

			btnArena = new GameButton
			{
				Text = "开始竞技场",
				Dock = DockStyle.Fill,
				Font = UiTheme.GameFont(9f, FontStyle.Bold),
				Skin = GameButtonSkin.Orange,
				Enabled = false,
				Margin = new Padding(6, 4, 2, 4)
			};
			btnArena.Click += BtnArena_Click;

			split.Controls.Add(left, 0, 0);
			split.Controls.Add(btnArena, 1, 0);
			body.Controls.Add(split, 0, 1);
			card.Controls.Add(body);
			return card;
		}

		private Control BuildCombatCard()
		{
			var card = new CardPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10, 5, 10, 6),
				Margin = new Padding(0, 1, 0, 4),
				BackColor = CardColor,
				BorderColor = LineColor,
				CornerRadius = 6
			};
			var wrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
			wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));
			wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			wrap.Controls.Add(new GameHeader { Title = "战斗委托", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);

			var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 0) };
			for (int i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			btnGuildBoss = TaskButton("工会首领", BtnGuildBoss_Click, GameButtonSkin.Orange);
			btnEarth = TaskButton("矿区首领", BtnEarth_Click, GameButtonSkin.Orange);
			btnPush = TaskButton("自动推关", BtnPush_Click, GameButtonSkin.Orange);
			hints.SetToolTip(btnGuildBoss, "按设定次数挑战当前工会首领（使用已有许可证，不自动购买）。");
			hints.SetToolTip(btnEarth, "使用恶魔勋章连续挑战矿区首领。");
			hints.SetToolTip(btnPush, "简单 · 普通 · 困难，未通关主线持续重试。");

			row.Controls.Add(btnGuildBoss, 0, 0);
			row.Controls.Add(btnEarth, 1, 0);
			row.Controls.Add(btnPush, 2, 0);
			wrap.Controls.Add(row, 0, 1);
			card.Controls.Add(wrap);
			return card;
		}

		private Control BuildProductionGrowthCard()
		{
			var card = new CardPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10, 5, 10, 6),
				Margin = new Padding(0, 1, 0, 0),
				BackColor = CardColor,
				BorderColor = LineColor,
				CornerRadius = 6
			};
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var wrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
			wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));
			wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			wrap.Controls.Add(new GameHeader { Title = "生产与养成", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);

			// 生产类（黄色）
			var prodRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = glass ? new Padding(0, 3, 0, 3) : new Padding(0, 3, 0, 1) };
			prodRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			prodRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			prodRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			btnWorkshop = TaskButton("工坊", BtnWorkshop_Click, GameButtonSkin.Gold);
			btnMine = TaskButton("矿山采集", BtnMine_Click, GameButtonSkin.Gold);
			btnMine.Enabled = false;
			hints.SetToolTip(btnWorkshop, "先自动收取全部已完成工程，再按工坊设置生产补满空槽位。");
			hints.SetToolTip(btnMine, "先收成熟矿队奖励、再升级研究并按材料缺口派遣矿队。");
			prodRow.Controls.Add(btnWorkshop, 0, 0);
			prodRow.Controls.Add(btnMine, 1, 0);
			wrap.Controls.Add(prodRow, 0, 1);

			// 养成类（绿色）
			var growRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = glass ? new Padding(0, 3, 0, 0) : new Padding(0, 2, 0, 0) };
			for (int i = 0; i < 3; i++) growRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			growRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			btnRune = TaskButton("符文连抽", BtnRune_Click, GameButtonSkin.Green);
			btnDestiny = TaskButton("宿命强化", BtnDestiny_Click, GameButtonSkin.Green);
			btnOrePacks = TaskButton("使用矿石包", BtnOrePacks_Click, GameButtonSkin.Green);
			hints.SetToolTip(btnDestiny, "使用游戏一键强化，优先强化升星期望结晶较低的宿命武器。");
			hints.SetToolTip(btnOrePacks, "连续批量用完低级、中级、高级矿石包，并自动关闭获得窗口。");
			growRow.Controls.Add(btnRune, 0, 0);
			growRow.Controls.Add(btnDestiny, 1, 0);
			growRow.Controls.Add(btnOrePacks, 2, 0);
			wrap.Controls.Add(growRow, 0, 2);

			card.Controls.Add(wrap);
			return card;
		}

		private GameButton TaskButton(string text, EventHandler click, GameButtonSkin skin)
		{
			var button = new GameButton
			{
				Text = text,
				Dock = DockStyle.Fill,
				Skin = skin,
				Margin = new Padding(3, 1, 3, 1),
				Font = UiTheme.GameFont(8.75f, FontStyle.Bold)
			};
			button.Click += click;
			return button;
		}

		private Control BuildCampPage()
		{
			var page = NewPage();
			page.RowCount = 2;
			page.RowStyles.Add(new RowStyle(SizeType.Absolute, 122f));
			page.RowStyles.Add(new RowStyle(SizeType.Absolute, 178f));

			// 上卡：配置总览 + 打开大设置窗口总入口
			var overviewCard = new CardPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10, 5, 10, 6),
				Margin = new Padding(0, 0, 0, 5),
				BackColor = CardColor,
				BorderColor = LineColor,
				CornerRadius = 6
			};
			var ovBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
			ovBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			ovBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));
			ovBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			ovBody.Controls.Add(new GameHeader { Title = "配置总览", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);

			var ovSplit = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 3, 0, 0) };
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
				Dock = DockStyle.Fill,
				Skin = GameButtonSkin.Gold,
				Margin = new Padding(6, 12, 2, 12),
				Font = UiTheme.GameFont(9.5f, FontStyle.Bold)
			};
			btnAdvanced.Click += async delegate { await OpenUnifiedSettingsAsync("mining"); };
			btnChest = btnAdvanced;

			ovSplit.Controls.Add(sumStack, 0, 0);
			ovSplit.Controls.Add(btnAdvanced, 1, 0);
			ovBody.Controls.Add(ovSplit, 0, 1);
			overviewCard.Controls.Add(ovBody);

			// 下卡：九大分类直达网格（统一 Stone 沉稳色调）
			var dirCard = new CardPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10, 5, 10, 6),
				Margin = Padding.Empty,
				BackColor = CardColor,
				BorderColor = LineColor,
				CornerRadius = 6
			};
			var dirBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
			dirBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			dirBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));
			dirBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			dirBody.Controls.Add(new GameHeader { Title = "分类直达", Hint = "", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);

			var grid = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 2,
				BackColor = Color.Transparent,
				Margin = new Padding(0, 4, 0, 0)
			};
			for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
			for (int i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			campGridHost = grid;

			string[,] cats =
			{
				{ "mining", "挖矿与宝箱" },
				{ "daily", "日常清单" },
				{ "workshop", "工坊与矿山" },
				{ "schedule", "定时任务" },
				{ "game", "界面与壁纸" },
				{ "advanced", "高级与治理" }
			};
			for (int i = 0; i < cats.GetLength(0); i++)
			{
				string key = cats[i, 0];
				var btn = new GameButton
				{
					Text = cats[i, 1],
					Dock = DockStyle.Fill,
					Skin = GameButtonSkin.Stone,
					Margin = new Padding(3, 2, 3, 2),
					Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
				};
				btn.Click += async delegate { await OpenUnifiedSettingsAsync(key); };
				if (key == "daily") btnDailyOptions = btn;
				else if (key == "workshop") btnWorkshopOptions = btn;
				else if (key == "schedule") btnSchedule = btn;
				grid.Controls.Add(btn, i % 3, i / 3);
			}

			dirBody.Controls.Add(grid, 0, 1);
			dirCard.Controls.Add(dirBody);

			page.Controls.Add(overviewCard, 0, 0);
			page.Controls.Add(dirCard, 0, 1);
			return page;
		}

		private SmoothLabel SummaryLine()
		{
			return new SmoothLabel
			{
				Dock = DockStyle.Fill,
				BackColor = Color.Transparent,
				ForeColor = TextColor,
				Font = UiTheme.GameFont(8f),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true,
				Margin = Padding.Empty
			};
		}

		internal void UpdateSettingsSummary()
		{
			if (lblSumMining == null) return;
			lblSumMining.Text = "矿洞：第 " + GetNumberOption("horizontalStartDepth", 150) + " 层横挖 · 炼化保留 " + GetNumberOption("refineReserve", 1000);

			int tier = GetNumberOption("dailyAlchemyTier", 1);
			string alch = tier == 2 ? "30血钻炼金" : (tier == 3 ? "60血钻炼金" : "免费炼金");
			int trollLv = GetNumberOption("dailyTrollLevel", 0);
			int ugLv = GetNumberOption("dailyUndergroundLevel", 0);
			string dung = (trollLv == 0 && ugLv == 0) ? "副本自动最高" : ("巨魔" + (trollLv == 0 ? "最高" : "Lv" + trollLv) + "/探险" + (ugLv == 0 ? "最高" : "Lv" + ugLv));
			lblSumDaily.Text = "日常：" + alch + " · " + dung;

			lblSumWorkshop.Text = "工坊：" + GetNumberOption("workshopDepth", 150) + "层 / " + GetNumberOption("workshopMinutes", 120) + "分 · 首领" + GetNumberOption("guildBossCount", 3) + "次";

			int mask = MineResearchMask(), count = 0;
			for (int i = 0; i < 12; i++) if ((mask & (1 << i)) != 0) count++;
			bool autoAll = scheduleBook != null && (scheduleBook.MineFollowup || scheduleBook.EscortFollowup || scheduleBook.RefineFollowup || scheduleBook.AutoCheckin);
			lblSumSchedule.Text = "研究：" + (count == 12 ? "全部12项" : count + "/12项") + " · " + UiTheme.ThemeName(UiTheme.CurrentTheme) + " · " + UiLayoutName(activeUiLayout);
			if (lblStatusSchedule != null)
			{
				string nextHarvest = "无待跑续跑";
				if (scheduleBook != null && scheduleBook.Harvest != null && scheduleBook.Harvest.Count > 0)
				{
					HarvestRun earliest = null;
					foreach (var h in scheduleBook.Harvest)
					{
						if (h != null && (earliest == null || h.DueUtc < earliest.DueUtc)) earliest = h;
					}
					if (earliest != null) nextHarvest = earliest.Label + " " + earliest.DueUtc.ToLocalTime().ToString("HH:mm");
				}
				lblStatusSchedule.Text = "自动续跑与签到：" + (autoAll ? "已开启" : "已关闭") + " · " + nextHarvest;
			}
			UpdateLayoutDynamicViews(null);
		}

		private Control BuildBottomDock()
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var card = new CardPanel
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Padding = glass ? new Padding(10, 8, 10, 8) : new Padding(10, 6, 10, 8),
				BackColor = CardColor,
				BorderColor = LineColor,
				CornerRadius = 6
			};
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 36f : 34f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 9f : 4f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var bar = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
			var tabFlow = new FlowLayoutPanel { Dock = DockStyle.Left, Width = glass ? 316 : 290, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
			tabBottomStatus = BottomTabBtn("营地状态", delegate { SwitchBottomTab("status"); });
			tabBottomStats = BottomTabBtn("挖矿统计", delegate { SwitchBottomTab("stats"); });
			tabBottomLog = BottomTabBtn("冒险日志", delegate { SwitchBottomTab("log"); });
			tabFlow.Controls.Add(tabBottomStatus);
			tabFlow.Controls.Add(tabBottomStats);
			tabFlow.Controls.Add(tabBottomLog);

			var rightActions = new Panel { Dock = DockStyle.Right, Width = glass ? 94 : 92, BackColor = Color.Transparent };
			btnBottomReview = new GameButton
			{
				Text = "任务记录",
				Location = glass ? new Point(2, 2) : new Point(6, 3),
				Size = glass ? new Size(90, 32) : new Size(84, 28),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(glass ? 8f : 7.5f, FontStyle.Bold)
			};
			btnBottomReview.Click += OpenReview;
			btnBottomClearLog = new GameButton
			{
				Text = "清空日志",
				Location = glass ? new Point(2, 2) : new Point(6, 3),
				Size = glass ? new Size(90, 32) : new Size(84, 28),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(glass ? 8f : 7.5f, FontStyle.Bold),
				Visible = false
			};
			btnBottomClearLog.Click += BtnClearLog_Click;
			hints.SetToolTip(btnBottomClearLog, "清空日志（每 10 分钟亦会自动清理）");
			rightActions.Controls.Add(btnBottomReview);
			rightActions.Controls.Add(btnBottomClearLog);

			bar.Controls.Add(tabFlow);
			bar.Controls.Add(rightActions);

			var divider = new Panel { Dock = DockStyle.Fill, BackColor = glass ? Color.Transparent : Color.FromArgb(110, LineColor), Margin = new Padding(0, 1, 0, 2) };

			var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
			panelBottomStatus = BuildStatusBody();
			panelBottomStats = BuildMiningStatsBody();
			panelBottomLog = BuildLogsBody();
			bodyHost.Controls.Add(panelBottomLog);
			bodyHost.Controls.Add(panelBottomStats);
			bodyHost.Controls.Add(panelBottomStatus);

			layout.Controls.Add(bar, 0, 0);
			layout.Controls.Add(divider, 0, 1);
			layout.Controls.Add(bodyHost, 0, 2);
			card.Controls.Add(layout);

			SwitchBottomTab("status");
			return card;
		}

		private static GameButton BottomTabBtn(string text, EventHandler click)
		{
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			var btn = new GameButton
			{
				Text = text,
				Size = glass ? new Size(98, 32) : new Size(90, 30),
				Margin = new Padding(0, 2, 6, 2),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(glass ? 8.25f : 8f, FontStyle.Bold)
			};
			btn.Click += click;
			return btn;
		}

		internal void SwitchBottomTab(string tab)
		{
			if (tab != "stats" && tab != "log") tab = "status";
			if (activeUiLayout == UiLayoutStatus && tab == "status") tab = "log";
			currentBottomTab = tab;
			if (panelBottomLog == null || panelBottomStats == null) return;
			UiSurface.Batch(sidebarScroll, delegate
			{
				if (panelBottomStatus != null) panelBottomStatus.Visible = tab == "status";
				panelBottomStats.Visible = tab == "stats";
				panelBottomLog.Visible = tab == "log";
				SetTabState(tabBottomStatus, tab == "status");
				SetTabState(tabBottomStats, tab == "stats");
				SetTabState(tabBottomLog, tab == "log");
				if (topTabBottomStatus != null) topTabBottomStatus.Selected = tab == "status";
				if (topTabBottomStats != null) topTabBottomStats.Selected = tab == "stats";
				if (topTabBottomLog != null) topTabBottomLog.Selected = tab == "log";
				if (activeUiLayout == UiLayoutStatus)
				{
					if (btnBottomReview != null) btnBottomReview.Visible = true;
					if (btnBottomClearLog != null) btnBottomClearLog.Visible = tab == "log";
				}
				else
				{
					if (btnBottomReview != null) btnBottomReview.Visible = tab == "status";
					if (btnBottomClearLog != null) btnBottomClearLog.Visible = tab == "log";
				}
			});
		}

		private GameCheckBox OptionCheck(string key, string text, float fontSize)
		{
			string actual = key;
			var check = new GameCheckBox
			{
				Text = text,
				Tag = actual,
				Dock = DockStyle.Fill,
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft,
				BackColor = Color.Transparent,
				ForeColor = (UiTheme.IsLightTheme || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass) ? TextColor : Color.FromArgb(228, 216, 185),
				Font = UiTheme.GameFont(fontSize),
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(0, 1, 5, 1),
				Checked = !options.ContainsKey(actual) || options[actual]
			};
			check.CheckedChanged += Option_CheckedChanged;
			optionChecks[actual] = check;
			if (actual == "arenaAllowBloodRefresh") { chkArenaRefresh = check; }
			return check;
		}

		private void RefreshOptionChecks()
		{
			syncingOptions = true;
			try
			{
				foreach (KeyValuePair<string, CheckBox> pair in optionChecks)
				{
					bool value = !options.ContainsKey(pair.Key) || options[pair.Key];
					if (pair.Value.Checked != value) pair.Value.Checked = value;
				}
			}
			finally { syncingOptions = false; }
		}

		private void SwitchTab(string tab)
		{
			if (tab == "mine") tab = "camp";
			currentTab = tab;
			if (pageTasks == null) return;
			UiSurface.Batch(sidebarScroll, delegate
			{
				pageTasks.Visible = tab == "tasks";
				pageCamp.Visible = tab == "camp";
				SetTabState(tabTasks, tab == "tasks");
				SetTabState(tabCamp, tab == "camp");
				if (topTabTasks != null) topTabTasks.Selected = tab == "tasks";
				if (topTabCamp != null) topTabCamp.Selected = tab == "camp";
			});
		}

		private static void SetTabState(GameButton button, bool selected)
		{
			if (button == null) return;
			button.Selected = selected;
			button.Skin = selected ? GameButtonSkin.Gold : GameButtonSkin.Stone;
		}

		private Image LoadArt(string name)
		{
			using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
			{
				if (stream == null) return null;
				using (var image = Image.FromStream(stream)) return new Bitmap(image);
			}
		}

		private void ShowGuide()
		{
			using (var dialog = new Form
			{
				Text = "虚空 · 每周攻略",
				Width = 700,
				Height = 720,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = SideColor,
				ForeColor = TextColor,
				Font = Font,
				MinimizeBox = false,
				MaximizeBox = false
			})
			{
				var text = new RichTextBox
				{
					Dock = DockStyle.Fill,
					ReadOnly = true,
					BackColor = CardColor,
					ForeColor = TextColor,
					Font = UiTheme.GameFont(10f),
					BorderStyle = BorderStyle.None,
					DetectUrls = false
				};
				using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.guide.txt"))
					if (stream != null) using (var reader = new StreamReader(stream, Encoding.UTF8)) text.Text = reader.ReadToEnd();
				var note = new SmoothLabel
				{
					Dock = DockStyle.Top,
					Height = 64,
					Padding = new Padding(14, 9, 14, 0),
					Text = "按游戏服务器星期匹配；原文第一关对应游戏地图2。\r\n攻略门槛不满足时停止并说明缺项。周五过滤加回合佣兵。",
					ForeColor = GoldColor
				};
				dialog.Controls.Add(text);
				dialog.Controls.Add(note);
				dialog.ShowDialog(this);
			}
		}
	}
}
