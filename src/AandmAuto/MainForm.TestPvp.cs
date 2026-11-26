#if TEST_PVP_EDITION
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private const string PvpSimDir = @"f:\antigra\pvp-simulator";

		private bool testPvpShowLegacy;
		private bool testPvpBusy;
		private CancellationTokenSource testPvpCancel;
		private Dictionary<string, object> loadedFormation;
		private string loadedFormationSource = "";
		private int selectedPvpFid = 5;
		private bool pvpSyncRunes = true;
		private string pvpV3ActiveTab = "core";

		private PvpDropZonePanel pnlPvpDropZone;
		private readonly GameButton[] btnPvpFidSlots = new GameButton[5];
		private readonly GameButton[] btnPvpStylePills = new GameButton[3];
		private GameCheckBox chkPvpSyncRunes;
		private GameButton btnPvpDeploy;
		private GameButton btnPvpStop;
		private Panel pvpV3CorePage;
		private Panel pvpV3ToolsPage;
		private GameTopTab pvpV3TabCore;
		private GameTopTab pvpV3TabTools;

		private static readonly Dictionary<string, string> KnownFriendLabels = new Dictionary<string, string>
		{
			{ "73ID6", "测试02" },
			{ "GE0Z2", "测试03" },
			{ "DAFKJ", "测试04" },
			{ "70SXF", "测试08" },
			{ "3YPN0", "测试15" },
			{ "NZVHY", "测试16" },
			{ "P0PLM", "测试18" }
		};

		private int GetSelectedPvpFid()
		{
			return Math.Max(1, Math.Min(5, selectedPvpFid));
		}

		private void SetSelectedPvpFid(int fid)
		{
			selectedPvpFid = Math.Max(1, Math.Min(5, fid));
			for (int i = 0; i < 5; i++)
			{
				var b = btnPvpFidSlots[i];
				if (b == null || b.IsDisposed) continue;
				bool sel = (i + 1) == selectedPvpFid;
				b.Selected = sel;
				b.Skin = sel ? GameButtonSkin.Gold : GameButtonSkin.Stone;
			}
			if (btnPvpDeploy != null && !btnPvpDeploy.IsDisposed)
			{
				btnPvpDeploy.Text = string.Format("一键部署到阵型 {0}", selectedPvpFid);
			}
		}

		private int GetPvpUiStyle()
		{
			return Math.Max(0, Math.Min(2, GetNumberOption("pvpUiStyle", 0)));
		}

		private void SwitchTestPvpLayout(int style, bool persist = true)
		{
			style = Math.Max(0, Math.Min(2, style));
			numberOptions["pvpUiStyle"] = style;
			testPvpShowLegacy = false;
			if (persist && !previewMode)
			{
				try { SaveOptions(); } catch { }
			}
			RebuildTestPvpSidebar();
		}

		private Control BuildTestPvpLegacyBackBar()
		{
			var bar = new Panel
			{
				Dock = DockStyle.Top,
				Height = 36,
				Padding = new Padding(12, 4, 12, 4),
				BackColor = SideColor
			};
			var btnBack = new GameButton
			{
				Text = "收起旧功能 · 返回 PvP 专项版 ▲",
				Dock = DockStyle.Fill,
				Skin = GameButtonSkin.Gold,
				StretchWide = true,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			btnBack.Click += delegate
			{
				testPvpShowLegacy = false;
				RebuildTestPvpSidebar();
			};
			bar.Controls.Add(btnBack);
			return bar;
		}

		private void RebuildTestPvpSidebar()
		{
			if (sidebarScroll == null || sidebarScroll.IsDisposed) return;
			sidebarScroll.SuspendLayout();
			try
			{
				while (sidebarScroll.Controls.Count > 0)
				{
					Control c = sidebarScroll.Controls[0];
					sidebarScroll.Controls.RemoveAt(0);
					c.Dispose();
				}
				BuildSidebarContents();
				UpdateResponsiveHeights();
				ReplayLogHistoryToRtb();
			}
			finally
			{
				sidebarScroll.ResumeLayout(true);
			}
		}

		private GameSidePanel BuildTestPvpSidebar()
		{
			if (btnMain == null || btnMain.IsDisposed) btnMain = new GameButton();
			if (btnDaily == null || btnDaily.IsDisposed) btnDaily = new GameButton();
			if (btnArena == null || btnArena.IsDisposed) btnArena = new GameButton();

			int style = GetPvpUiStyle();
			float wsHeight = style == 2 ? 320f : (style == 1 ? 430f : 444f);

			var side = new GameSidePanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(10, 6, 10, 8),
				Margin = Padding.Empty
			};
			side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
			side.RowStyles.Add(new RowStyle(SizeType.Absolute, wsHeight));
			side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			sidebarLayout = side;
			activeSidePanel = side;

			side.AllowDrop = true;
			side.DragEnter += PvpDrop_DragEnter;
			side.DragDrop += PvpDrop_DragDrop;

			side.Controls.Add(BuildHeader(), 0, 0);
			side.Controls.Add(BuildPvpStyleBar(style), 0, 1);
			if (style == 1)
				side.Controls.Add(BuildPvpWorkspaceParchment(), 0, 2);
			else if (style == 2)
				side.Controls.Add(BuildPvpWorkspaceDualTab(), 0, 2);
			else
				side.Controls.Add(BuildPvpWorkspaceHero(), 0, 2);
			side.Controls.Add(BuildBottomDock(), 0, 3);
			SwitchBottomTab("log");
			return side;
		}

		private Control BuildPvpStyleBar(int activeStyle)
		{
			var bar = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 5,
				RowCount = 1,
				Margin = new Padding(0, 1, 0, 3),
				BackColor = Color.Transparent
			};
			bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
			bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
			bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
			bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
			bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

			bar.Controls.Add(new SmoothLabel
			{
				Text = "风格:",
				Dock = DockStyle.Fill,
				ForeColor = MutedColor,
				Font = UiTheme.UiFont(8f, FontStyle.Regular),
				TextAlign = ContentAlignment.MiddleLeft
			}, 0, 0);

			string[] names = new[] { "鎏金主战", "羊皮简报", "双核极简" };
			for (int i = 0; i < 3; i++)
			{
				int idx = i;
				var btn = new GameButton
				{
					Text = names[i],
					Dock = DockStyle.Fill,
					Skin = activeStyle == idx ? GameButtonSkin.Gold : GameButtonSkin.Stone,
					Selected = activeStyle == idx,
					StretchWide = true,
					Font = UiTheme.GameFont(7.6f, FontStyle.Bold),
					Margin = new Padding(0, 1, 4, 1)
				};
				btn.Click += delegate { SwitchTestPvpLayout(idx); };
				btnPvpStylePills[i] = btn;
				bar.Controls.Add(btn, i + 1, 0);
			}

			var btnLegacy = new GameButton
			{
				Text = "旧功能 ▾",
				Dock = DockStyle.Fill,
				Skin = GameButtonSkin.Stone,
				StretchWide = true,
				Font = UiTheme.UiFont(7.8f, FontStyle.Regular),
				Margin = new Padding(4, 1, 0, 1)
			};
			btnLegacy.Click += delegate
			{
				testPvpShowLegacy = true;
				RebuildTestPvpSidebar();
			};
			bar.Controls.Add(btnLegacy, 4, 0);
			return bar;
		}

		// =====================================================================
		// Shared Custom Controls (DropZone, F1-F5 Pixel Pills, Runes, Cultivation)
		// =====================================================================
		internal sealed class PvpDropZonePanel : Panel
		{
			public bool ParchmentMode;
			public Func<Dictionary<string, object>> GetFormation;
			public Func<string> GetSourceName;
			public Func<int> GetTargetFid;

			public PvpDropZonePanel()
			{
				DoubleBuffered = true;
				SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
				BackColor = Color.Transparent;
				AllowDrop = true;
				Cursor = Cursors.Hand;
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				Graphics g = e.Graphics;
				g.SmoothingMode = SmoothingMode.AntiAlias;
				Rectangle r = new Rectangle(0, 0, Math.Max(4, Width - 1), Math.Max(4, Height - 2));
				Dictionary<string, object> form = GetFormation != null ? GetFormation() : null;
				bool hasForm = form != null;

				Color fill = ParchmentMode ? Color.FromArgb(40, 30, 18) : Color.FromArgb(29, 23, 13);
				Color border = hasForm ? Color.FromArgb(118, 198, 92) : Color.FromArgb(196, 158, 72);
				using (GraphicsPath path = UiTheme.RoundedRect(r, 6))
				{
					using (var bg = new SolidBrush(fill)) g.FillPath(bg, path);
					using (var pen = new Pen(border, hasForm ? 1.6f : 1.3f))
					{
						if (!hasForm) pen.DashStyle = DashStyle.Dash;
						g.DrawPath(pen, path);
					}
				}

				if (!hasForm)
				{
					TextFormatFlags fMain = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
					GameText.Draw(g, "拖入「一图流 PNG」到这里（或点击选文件 / 选预设）",
						UiTheme.GameFont(9f, FontStyle.Bold),
						new Rectangle(10, Math.Max(4, Height / 2 - 20), Math.Max(10, Width - 20), 20),
						Color.FromArgb(245, 211, 118), fMain);
					GameText.Draw(g, "<1ms 零误差解析 30格阵容 · 武器 · 5符文 · 洗练要求",
						UiTheme.UiFont(8f, FontStyle.Regular),
						new Rectangle(10, Math.Max(24, Height / 2 + 2), Math.Max(10, Width - 20), 18),
						Color.FromArgb(182, 168, 136), fMain);
					return;
				}

				string nm = form.ContainsKey("name") ? Convert.ToString(form["name"]) : (GetSourceName != null ? GetSourceName() : "已载入阵容");
				string src = GetSourceName != null ? GetSourceName() : "";
				int wid = GetIntField(form, "weapon", GetIntField(form, "weapon_id", 12));
				var slots = form.ContainsKey("slots") ? form["slots"] as object[] : null;
				var runes = form.ContainsKey("runes") ? form["runes"] as object[] : null;
				int fid = GetTargetFid != null ? GetTargetFid() : 5;

				// Status pill on top-right
				int pillW = 128;
				Rectangle pill = new Rectangle(Math.Max(12, Width - pillW - 10), 8, pillW, 20);
				using (GraphicsPath pp = UiTheme.RoundedRect(pill, 4))
				using (var pb = new SolidBrush(Color.FromArgb(35, 86, 46)))
				using (var ppen = new Pen(Color.FromArgb(105, 194, 114), 1f))
				{
					g.FillPath(pb, pp);
					g.DrawPath(ppen, pp);
				}
				GameText.Draw(g, string.Format("{0}格 · W{1} · 符文{2}", slots != null ? slots.Length : 0, wid, runes != null ? runes.Length : 0),
					UiTheme.UiFont(7.6f, FontStyle.Bold), pill, Color.FromArgb(156, 240, 166),
					TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

				// Line 1: Formation Name
				Rectangle nameRect = new Rectangle(12, 7, Math.Max(40, pill.Left - 18), 22);
				GameText.Draw(g, nm, UiTheme.UiFont(9.4f, FontStyle.Bold), nameRect, Color.FromArgb(255, 231, 145),
					TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

				// Line 2: Source file
				Rectangle srcRect = new Rectangle(12, 31, Math.Max(40, Width - 24), 18);
				GameText.Draw(g, "来源：" + src, UiTheme.UiFont(8f, FontStyle.Regular), srcRect, Color.FromArgb(209, 197, 168),
					TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

				// Line 3: Protection hint (if height >= 68)
				if (Height >= 66)
				{
					Rectangle hintRect = new Rectangle(12, 50, Math.Max(40, Width - 24), 16);
					GameText.Draw(g, string.Format("就绪 -> 目标阵型 {0}（仅改动阵型{0} · 其余阵型绝对保护）", fid),
						UiTheme.UiFont(7.6f, FontStyle.Regular), hintRect, Color.FromArgb(132, 214, 146),
						TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				}
			}
		}

		private PvpDropZonePanel CreateDropZone(bool parchmentMode)
		{
			var dz = new PvpDropZonePanel
			{
				Dock = DockStyle.Fill,
				ParchmentMode = parchmentMode,
				GetFormation = delegate { return loadedFormation; },
				GetSourceName = delegate { return loadedFormationSource; },
				GetTargetFid = delegate { return GetSelectedPvpFid(); },
				Margin = new Padding(0, 3, 0, 3)
			};
			dz.DragEnter += PvpDrop_DragEnter;
			dz.DragDrop += PvpDrop_DragDrop;
			dz.Click += delegate { PickAndLoadFormationPng(); };
			pnlPvpDropZone = dz;
			return dz;
		}

		private Control BuildFidSelectorAndSyncRow(bool lightParentInk)
		{
			var row = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 7,
				RowCount = 1,
				Margin = new Padding(0, 2, 0, 2),
				BackColor = Color.Transparent
			};
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
			for (int i = 0; i < 5; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50f));
			row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

			Color ink = lightParentInk ? Color.FromArgb(72, 38, 12) : GoldColor;
			row.Controls.Add(new SmoothLabel
			{
				Text = "目标:",
				Dock = DockStyle.Fill,
				ForeColor = ink,
				Font = UiTheme.GameFont(8.2f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			}, 0, 0);

			int curFid = GetSelectedPvpFid();
			for (int i = 0; i < 5; i++)
			{
				int f = i + 1;
				bool sel = (f == curFid);
				var b = new GameButton
				{
					Text = "阵" + f,
					Dock = DockStyle.Fill,
					Skin = sel ? GameButtonSkin.Gold : GameButtonSkin.Stone,
					Selected = sel,
					StretchWide = true,
					Font = UiTheme.GameFont(7.8f, FontStyle.Bold),
					Margin = new Padding(0, 1, 3, 1)
				};
				b.Click += delegate { SetSelectedPvpFid(f); if (pnlPvpDropZone != null) pnlPvpDropZone.Invalidate(); };
				btnPvpFidSlots[i] = b;
				row.Controls.Add(b, i + 1, 0);
			}

			chkPvpSyncRunes = new GameCheckBox
			{
				Text = "同步换符文",
				Dock = DockStyle.Fill,
				Checked = pvpSyncRunes,
				ForeColor = lightParentInk ? Color.FromArgb(62, 34, 10) : TextColor,
				Font = UiTheme.UiFont(8.2f, FontStyle.Bold),
				Margin = new Padding(6, 0, 0, 0)
			};
			chkPvpSyncRunes.CheckedChanged += delegate { pvpSyncRunes = chkPvpSyncRunes.Checked; };
			row.Controls.Add(chkPvpSyncRunes, 6, 0);
			return row;
		}

		private GameButton CreatePrimaryDeployButton()
		{
			btnPvpDeploy = new GameButton
			{
				Text = string.Format("一键部署到阵型 {0}", GetSelectedPvpFid()),
				Dock = DockStyle.Fill,
				Skin = GameButtonSkin.Gold,
				StretchWide = true,
				Font = UiTheme.GameFont(10.2f, FontStyle.Bold),
				Margin = new Padding(0, 2, 0, 0)
			};
			btnPvpDeploy.Click += async delegate { await DeployLoadedFormationAsync(); };
			return btnPvpDeploy;
		}

		private Control BuildRuneStripRow()
		{
			var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			for (int i = 0; i < 5; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
			AddRuneQuickButton(row, 0, "5×防衰", new int[] { 59, 59, 59, 59, 59 }, GameButtonSkin.Orange);
			AddRuneQuickButton(row, 1, "3王+2闪王", new int[] { 63, 63, 63, 69, 69 }, GameButtonSkin.Violet);
			AddRuneQuickButton(row, 2, "5×王衰", new int[] { 63, 63, 63, 63, 63 }, GameButtonSkin.Stone);
			AddRuneQuickButton(row, 3, "5×防王", new int[] { 66, 66, 66, 66, 66 }, GameButtonSkin.Stone);
			var btnCustom = new GameButton { Text = "自定义", Dock = DockStyle.Fill, Skin = GameButtonSkin.Green, StretchWide = true, Font = UiTheme.GameFont(7.8f, FontStyle.Bold), Margin = new Padding(2, 1, 0, 1) };
			btnCustom.Click += delegate { ShowCustomRuneDialog(); };
			row.Controls.Add(btnCustom, 4, 0);
			return row;
		}

		private void AddRuneQuickButton(TableLayoutPanel row, int col, string label, int[] tids, GameButtonSkin skin)
		{
			var btn = new GameButton
			{
				Text = label,
				Dock = DockStyle.Fill,
				Skin = skin,
				StretchWide = true,
				Font = UiTheme.GameFont(7.6f, FontStyle.Bold),
				Margin = new Padding(col == 0 ? 0 : 2, 1, 2, 1)
			};
			btn.Click += async delegate { await ApplyRunesInClientAsync(tids, label); };
			row.Controls.Add(btn, col, 0);
		}

		private Control BuildGrowthRow()
		{
			var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
			for (int i = 0; i < 4; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
			AddGrowthQuickBtn(row, 0, "强化佣兵", "gear", GameButtonSkin.Orange);
			AddGrowthQuickBtn(row, 1, "佣兵养成", "grow", GameButtonSkin.Gold);
			AddGrowthQuickBtn(row, 2, "自动契约", "contract", GameButtonSkin.Green);
			var btnWash = new GameButton { Text = "洗界限突破", Dock = DockStyle.Fill, Skin = GameButtonSkin.Violet, StretchWide = true, Font = UiTheme.GameFont(7.6f, FontStyle.Bold), Margin = new Padding(2, 1, 0, 1) };
			btnWash.Click += delegate { ShowMercWashDialog(); };
			row.Controls.Add(btnWash, 3, 0);
			return row;
		}

		private void AddGrowthQuickBtn(TableLayoutPanel row, int col, string label, string mode, GameButtonSkin skin)
		{
			var btn = new GameButton
			{
				Text = label,
				Dock = DockStyle.Fill,
				Skin = skin,
				StretchWide = true,
				Font = UiTheme.GameFont(7.6f, FontStyle.Bold),
				Margin = new Padding(col == 0 ? 0 : 2, 1, 2, 1)
			};
			btn.Click += async delegate { await RunTestMercLoopAsync(mode, label, null, 0); };
			row.Controls.Add(btn, col, 0);
		}

		private GameButton CreatePickPngBtn(string label = "选一图流")
		{
			var b = new GameButton { Text = label, Dock = DockStyle.Fill, Skin = GameButtonSkin.Stone, StretchWide = true, Font = UiTheme.GameFont(7.6f, FontStyle.Bold), Margin = new Padding(2, 1, 2, 1) };
			b.Click += delegate { PickAndLoadFormationPng(); };
			return b;
		}

		private GameButton CreatePickPresetBtn(string label = "选预设")
		{
			var b = new GameButton { Text = label, Dock = DockStyle.Fill, Skin = GameButtonSkin.Stone, StretchWide = true, Font = UiTheme.GameFont(7.6f, FontStyle.Bold), Margin = new Padding(2, 1, 0, 1) };
			b.Click += delegate { PickAndLoadPresetDialog(); };
			return b;
		}

		private GameButton CreateBattleBtn(string label = "好友切磋实测 (在途=1)")
		{
			var b = new GameButton { Text = label, Dock = DockStyle.Fill, Skin = GameButtonSkin.Green, StretchWide = true, Font = UiTheme.GameFont(8.8f, FontStyle.Bold), Margin = new Padding(0, 1, 3, 1) };
			b.Click += delegate { ShowPvpFriendBattleDialog(); };
			return b;
		}

		private GameButton CreateOptimizeBtn(string label = "10核一键寻优")
		{
			var b = new GameButton { Text = label, Dock = DockStyle.Fill, Skin = GameButtonSkin.Violet, StretchWide = true, Font = UiTheme.GameFont(8.8f, FontStyle.Bold), Margin = new Padding(3, 1, 0, 1) };
			b.Click += delegate { ShowPvpOptimizeDialog(); };
			return b;
		}

		private GameButton CreateSnapshotBtn(string label = "导出当前阵型为一图流")
		{
			var b = new GameButton { Text = label, Dock = DockStyle.Fill, Skin = GameButtonSkin.Stone, StretchWide = true, Font = UiTheme.GameFont(8f, FontStyle.Bold), Margin = new Padding(0, 1, 3, 1) };
			b.Click += async delegate { await ExportCurrentFormationSnapshotAsync(); };
			return b;
		}

		private GameButton CreateStopBtn(string label = "停止任务")
		{
			btnPvpStop = new GameButton
			{
				Text = label,
				Dock = DockStyle.Fill,
				Skin = GameButtonSkin.Orange,
				StretchWide = true,
				Font = UiTheme.GameFont(8f, FontStyle.Bold),
				Margin = new Padding(3, 1, 0, 1),
				Enabled = testPvpBusy
			};
			btnPvpStop.Click += delegate { CancelTestPvpWork(); };
			return btnPvpStop;
		}

		// =====================================================================
		// Version 1 (pvpUiStyle = 0): 鎏金主战台 · Hero Spotlight Pyramid
		// =====================================================================
		private Control BuildPvpWorkspaceHero()
		{
			var host = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Margin = Padding.Empty,
				BackColor = Color.Transparent
			};
			host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, 204f)); // S-Tier: Hero PixelBox Deploy Console
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));  // A-Tier 1: Friend Battle + 10-Core Opt
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));  // A-Tier 2: Lossless 5-Rune Quick Switcher
			host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // B-Tier: Compact Utility Strip

			// S-Tier Hero Card (PixelBoxPanel with gold border & GameHeader)
			var hero = new PixelBoxPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 5), Padding = new Padding(10, 8, 10, 9), AllowDrop = true };
			hero.DragEnter += PvpDrop_DragEnter;
			hero.DragDrop += PvpDrop_DragDrop;
			var hg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
			hg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			hg.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
			hg.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
			hg.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
			hg.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var hTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
			hTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			hTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f));
			hTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
			hTop.Controls.Add(new GameHeader { Title = "核心换阵引擎", Hint = "", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) }, 0, 0);
			hTop.Controls.Add(CreatePickPngBtn("选一图流"), 1, 0);
			hTop.Controls.Add(CreatePickPresetBtn("选预设"), 2, 0);

			hg.Controls.Add(hTop, 0, 0);
			hg.Controls.Add(CreateDropZone(false), 0, 1);
			hg.Controls.Add(BuildFidSelectorAndSyncRow(false), 0, 2);
			hg.Controls.Add(CreatePrimaryDeployButton(), 0, 3);
			hero.Controls.Add(hg);

			// A-Tier 1: High-Frequency Battle & Optimizer
			var midCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 5), Padding = new Padding(8, 6, 8, 6) };
			var midRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
			midRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
			midRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
			midRow.Controls.Add(CreateBattleBtn(), 0, 0);
			midRow.Controls.Add(CreateOptimizeBtn(), 1, 0);
			midCard.Controls.Add(midRow);

			// A-Tier 2: Lossless 5-Rune Quick Bar
			var runeCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 5), Padding = new Padding(8, 5, 8, 6) };
			var rg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
			rg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			rg.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
			rg.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			rg.Controls.Add(new SmoothLabel { Text = "无损换符文（锁死 auto_compose=false · 1秒秒切）", Dock = DockStyle.Fill, ForeColor = GoldColor, Font = UiTheme.GameFont(8f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
			rg.Controls.Add(BuildRuneStripRow(), 0, 1);
			runeCard.Controls.Add(rg);

			// B-Tier: Subdued Secondary Utility Tray (InsetPanel)
			var utilBox = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 2), Padding = new Padding(7, 5, 7, 5), LeftStripeColor = LineColor };
			var ug = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
			ug.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			ug.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
			ug.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
			ug.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			ug.Controls.Add(new SmoothLabel { Text = "次要辅助：测试服养成 / 快照导出 / 任务控制", Dock = DockStyle.Fill, ForeColor = MutedColor, Font = UiTheme.UiFont(7.6f, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
			ug.Controls.Add(BuildGrowthRow(), 0, 1);
			var snapRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 3, 0, 0) };
			snapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66f));
			snapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
			snapRow.Controls.Add(CreateSnapshotBtn("导出当前阵型为一图流 PNG"), 0, 0);
			snapRow.Controls.Add(CreateStopBtn("停止任务"), 1, 0);
			ug.Controls.Add(snapRow, 0, 2);
			utilBox.Controls.Add(ug);

			host.Controls.Add(hero, 0, 0);
			host.Controls.Add(midCard, 0, 1);
			host.Controls.Add(runeCard, 0, 2);
			host.Controls.Add(utilBox, 0, 3);
			return host;
		}

		// =====================================================================
		// Version 2 (pvpUiStyle = 1): 羊皮卷轴简报 · Parchment Briefing Contrast
		// =====================================================================
		private Control BuildPvpWorkspaceParchment()
		{
			var host = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Margin = Padding.Empty,
				BackColor = Color.Transparent
			};
			host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, 202f)); // S-Tier: High-contrast Warm Parchment Hero
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));  // A-Tier 1: Core PvP Action Ribbon (Battle / Opt / Snap)
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));  // A-Tier 2: Lossless Runes
			host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // B-Tier: Subdued Cultivation + Stop Strip

			// S-Tier High-Contrast Parchment Hero Card
			var paper = new ParchmentPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 5), Padding = new Padding(10, 7, 10, 8), AllowDrop = true };
			paper.DragEnter += PvpDrop_DragEnter;
			paper.DragDrop += PvpDrop_DragDrop;
			var pg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
			pg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			pg.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
			pg.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
			pg.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
			pg.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			var pTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
			pTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			pTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
			pTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
			pTop.Controls.Add(new SmoothLabel
			{
				Text = "一图流智能换阵 · 主控台",
				Dock = DockStyle.Fill,
				ForeColor = Color.FromArgb(72, 34, 8),
				Font = UiTheme.GameFont(10.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			}, 0, 0);
			pTop.Controls.Add(CreatePickPngBtn("选一图流"), 1, 0);
			pTop.Controls.Add(CreatePickPresetBtn("选预设"), 2, 0);

			pg.Controls.Add(pTop, 0, 0);
			pg.Controls.Add(CreateDropZone(true), 0, 1);
			pg.Controls.Add(BuildFidSelectorAndSyncRow(true), 0, 2);
			pg.Controls.Add(CreatePrimaryDeployButton(), 0, 3);
			paper.Controls.Add(pg);

			// A-Tier 1: 3-Button Core PvP Action Ribbon (Friend Battle + 10-Core Opt + Snapshot)
			var actionRibbon = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 5), Padding = new Padding(8, 6, 8, 6) };
			var aRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
			aRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
			aRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
			aRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f));
			aRow.Controls.Add(CreateBattleBtn("切磋实测(在途1)"), 0, 0);
			var btnOpt = CreateOptimizeBtn("10核寻优");
			btnOpt.Margin = new Padding(3, 1, 3, 1);
			aRow.Controls.Add(btnOpt, 1, 0);
			var btnSnap = CreateSnapshotBtn("导出一图流");
			btnSnap.Margin = new Padding(3, 1, 0, 1);
			aRow.Controls.Add(btnSnap, 2, 0);
			actionRibbon.Controls.Add(aRow);

			// A-Tier 2: Lossless Runes
			var runeBox = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 5), Padding = new Padding(8, 5, 8, 5) };
			var rg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
			rg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			rg.RowStyles.Add(new RowStyle(SizeType.Absolute, 19f));
			rg.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			rg.Controls.Add(new SmoothLabel { Text = "无损切换符文（auto_compose=false · 绝不毁符文）", Dock = DockStyle.Fill, ForeColor = GoldColor, Font = UiTheme.GameFont(8f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
			rg.Controls.Add(BuildRuneStripRow(), 0, 1);
			runeBox.Controls.Add(rg);

			// B-Tier: Subdued Cultivation + Emergency Stop Bar
			var secBox = new InsetPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 2), Padding = new Padding(7, 4, 7, 5) };
			var sg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
			sg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			sg.RowStyles.Add(new RowStyle(SizeType.Absolute, 17f));
			sg.RowStyles.Add(new RowStyle(SizeType.Absolute, 33f));
			sg.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			sg.Controls.Add(new SmoothLabel { Text = "测试服辅助养成（自动 200x 血钻补货）", Dock = DockStyle.Fill, ForeColor = MutedColor, Font = UiTheme.UiFont(7.5f, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
			sg.Controls.Add(BuildGrowthRow(), 0, 1);
			var stopWrap = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 0), BackColor = Color.Transparent };
			var bStop = CreateStopBtn("停止当前正在执行的 PvP / 养成任务");
			bStop.Margin = Padding.Empty;
			stopWrap.Controls.Add(bStop);
			sg.Controls.Add(stopWrap, 0, 2);
			secBox.Controls.Add(sg);

			host.Controls.Add(paper, 0, 0);
			host.Controls.Add(actionRibbon, 0, 1);
			host.Controls.Add(runeBox, 0, 2);
			host.Controls.Add(secBox, 0, 3);
			return host;
		}

		// =====================================================================
		// Version 3 (pvpUiStyle = 2): 战术双核极简舱 · Dual-Focus + Max Log Space
		// =====================================================================
		private void SwitchPvpV3Tab(string tab)
		{
			pvpV3ActiveTab = tab == "tools" ? "tools" : "core";
			if (pvpV3CorePage != null) pvpV3CorePage.Visible = pvpV3ActiveTab == "core";
			if (pvpV3ToolsPage != null) pvpV3ToolsPage.Visible = pvpV3ActiveTab == "tools";
			if (pvpV3TabCore != null) pvpV3TabCore.Selected = pvpV3ActiveTab == "core";
			if (pvpV3TabTools != null) pvpV3TabTools.Selected = pvpV3ActiveTab == "tools";
		}

		private Control BuildPvpWorkspaceDualTab()
		{
			var outer = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				Margin = Padding.Empty,
				BackColor = Color.Transparent
			};
			outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 29f));
			outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			// Top GameTopTab bar + persistent Snapshot / Stop micro-buttons on right
			var bar = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(6, 0, 0, 0) };
			var tabFlow = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 240, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
			pvpV3TabCore = new GameTopTab { Text = "PvP 核心作战", Size = new Size(116, 28), Margin = new Padding(0, 0, 3, 0), Font = UiTheme.GameFont(8.2f, FontStyle.Bold) };
			pvpV3TabTools = new GameTopTab { Text = "养成 · 工具箱", Size = new Size(116, 28), Margin = new Padding(0, 0, 0, 0), Font = UiTheme.GameFont(8.2f, FontStyle.Bold) };
			pvpV3TabCore.Click += delegate { SwitchPvpV3Tab("core"); };
			pvpV3TabTools.Click += delegate { SwitchPvpV3Tab("tools"); };
			tabFlow.Controls.Add(pvpV3TabCore);
			tabFlow.Controls.Add(pvpV3TabTools);

			var rightBtns = new TableLayoutPanel { Dock = DockStyle.Right, Width = 166, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(0, 2, 0, 3) };
			rightBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
			rightBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
			var btnMiniSnap = CreateSnapshotBtn("导出一图流");
			btnMiniSnap.Font = UiTheme.GameFont(7.4f, FontStyle.Bold);
			btnMiniSnap.Margin = new Padding(0, 0, 3, 0);
			var btnMiniStop = CreateStopBtn("停止");
			btnMiniStop.Font = UiTheme.GameFont(7.4f, FontStyle.Bold);
			btnMiniStop.Margin = Padding.Empty;
			rightBtns.Controls.Add(btnMiniSnap, 0, 0);
			rightBtns.Controls.Add(btnMiniStop, 1, 0);
			bar.Controls.Add(tabFlow);
			bar.Controls.Add(rightBtns);

			// Container box
			var box = new PixelBoxPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2), Padding = new Padding(10, 8, 10, 9), AllowDrop = true };
			box.DragEnter += PvpDrop_DragEnter;
			box.DragDrop += PvpDrop_DragDrop;
			var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

			// Tab 1: Pure Core Combat Flow (100% Zero Distraction!)
			pvpV3CorePage = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var cg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Color.Transparent };
			cg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			cg.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Pick PNG / Preset
			cg.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f)); // DropZone (shows all 3 lines cleanly)
			cg.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // F1..F5 + Sync
			cg.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // BIG GOLD DEPLOY CTA
			cg.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f)); // Battle + Opt
			cg.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Runes strip

			var cTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
			cTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			cTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f));
			cTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
			cTop.Controls.Add(new SmoothLabel { Text = "一键一图流极速换阵", Dock = DockStyle.Fill, ForeColor = GoldColor, Font = UiTheme.GameFont(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
			cTop.Controls.Add(CreatePickPngBtn("选一图流"), 1, 0);
			cTop.Controls.Add(CreatePickPresetBtn("选预设"), 2, 0);
			cg.Controls.Add(cTop, 0, 0);
			cg.Controls.Add(CreateDropZone(false), 0, 1);
			cg.Controls.Add(BuildFidSelectorAndSyncRow(false), 0, 2);
			cg.Controls.Add(CreatePrimaryDeployButton(), 0, 3);

			var cPvp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 4) };
			cPvp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
			cPvp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
			cPvp.Controls.Add(CreateBattleBtn("好友切磋实测 (在途=1)"), 0, 0);
			cPvp.Controls.Add(CreateOptimizeBtn("10核一键寻优"), 1, 0);
			cg.Controls.Add(cPvp, 0, 4);

			var cRunesWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), BackColor = Color.Transparent };
			cRunesWrap.Controls.Add(BuildRuneStripRow());
			cg.Controls.Add(cRunesWrap, 0, 5);
			pvpV3CorePage.Controls.Add(cg);

			// Tab 2: Secondary Tools (Cultivation, Snapshot, Legacy Expand)
			pvpV3ToolsPage = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var tg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.Transparent };
			tg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tg.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
			tg.RowStyles.Add(new RowStyle(SizeType.Absolute, 114f));
			tg.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
			tg.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			tg.Controls.Add(new GameHeader { Title = "测试服佣兵养成 & 扩展工具", Hint = "支持 200x 补货", Dock = DockStyle.Fill }, 0, 0);

			// Big 2x2 Grid for Cultivation buttons (much larger touch targets!)
			var g2x2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 6) };
			g2x2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			g2x2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			g2x2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			g2x2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			var bg1 = new GameButton { Text = "强化佣兵（武器+宝具）", Dock = DockStyle.Fill, Skin = GameButtonSkin.Orange, StretchWide = true, Font = UiTheme.GameFont(8.2f, FontStyle.Bold), Margin = new Padding(0, 2, 4, 3) };
			bg1.Click += async delegate { await RunTestMercLoopAsync("gear", "强化佣兵", null, 0); };
			var bg2 = new GameButton { Text = "佣兵养成（升级+5觉+契约）", Dock = DockStyle.Fill, Skin = GameButtonSkin.Gold, StretchWide = true, Font = UiTheme.GameFont(8.2f, FontStyle.Bold), Margin = new Padding(4, 2, 0, 3) };
			bg2.Click += async delegate { await RunTestMercLoopAsync("grow", "佣兵养成", null, 0); };
			var bg3 = new GameButton { Text = "自动契约（仅当前上阵）", Dock = DockStyle.Fill, Skin = GameButtonSkin.Green, StretchWide = true, Font = UiTheme.GameFont(8.2f, FontStyle.Bold), Margin = new Padding(0, 3, 4, 2) };
			bg3.Click += async delegate { await RunTestMercLoopAsync("contract", "自动契约", null, 0); };
			var bg4 = new GameButton { Text = "洗界限突破（自动突破+洗练）", Dock = DockStyle.Fill, Skin = GameButtonSkin.Violet, StretchWide = true, Font = UiTheme.GameFont(8.2f, FontStyle.Bold), Margin = new Padding(4, 3, 0, 2) };
			bg4.Click += delegate { ShowMercWashDialog(); };
			g2x2.Controls.Add(bg1, 0, 0);
			g2x2.Controls.Add(bg2, 1, 0);
			g2x2.Controls.Add(bg3, 0, 1);
			g2x2.Controls.Add(bg4, 1, 1);
			tg.Controls.Add(g2x2, 0, 1);

			var btnBigSnap = CreateSnapshotBtn("导出当前阵型为一图流 PNG（含可复用元数据）");
			btnBigSnap.Margin = new Padding(0, 2, 0, 6);
			tg.Controls.Add(btnBigSnap, 0, 2);

			var btnBigLegacy = new GameButton { Text = "展开全部旧功能面板（日常 / 挖矿 / 虚空 / 竞技场） ▾", Dock = DockStyle.Fill, Skin = GameButtonSkin.Stone, StretchWide = true, Font = UiTheme.UiFont(8.2f, FontStyle.Regular), Margin = new Padding(0, 2, 0, 2) };
			btnBigLegacy.Click += delegate { testPvpShowLegacy = true; RebuildTestPvpSidebar(); };
			tg.Controls.Add(btnBigLegacy, 0, 3);
			pvpV3ToolsPage.Controls.Add(tg);

			bodyHost.Controls.Add(pvpV3ToolsPage);
			bodyHost.Controls.Add(pvpV3CorePage);
			box.Controls.Add(bodyHost);
			outer.Controls.Add(bar, 0, 0);
			outer.Controls.Add(box, 0, 1);

			SwitchPvpV3Tab(pvpV3ActiveTab);
			return outer;
		}

		// =====================================================================
		// Preview Generator for All 3 Test PvP UI Versions
		// =====================================================================
		internal void RenderTestPvpPreviews(string path)
		{
			string outDir = Path.GetDirectoryName(path);
			string baseName = Path.GetFileNameWithoutExtension(path);
			int savedStyle = GetPvpUiStyle();
			try
			{
				using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.presets.json"))
				{
					if (s != null)
					{
						using (var r = new StreamReader(s, Encoding.UTF8))
						{
							var all = json.DeserializeObject(r.ReadToEnd()) as Dictionary<string, object>;
							if (all != null && all.ContainsKey("v14_meiling_all4_single"))
							{
								loadedFormation = all["v14_meiling_all4_single"] as Dictionary<string, object>;
								loadedFormationSource = "流派1_极速先暴流_v14_server30.png";
							}
						}
					}
				}
			}
			catch { }

			ClearAdventureLogs(true);
			AddLog("已零误差解析一图流：v14 红美铃单套四强通解(3x63+2x69)", GreenColor);
			AddLog("  [0.74s] 1包 RecommendMercenaryFormation 换阵完成", AutoLogColor);
			AddLog("  [1.14s] ExchangeRuneProperty ×2 + EquipRune ×5 全部就位", AutoLogColor);
			AddLog("部署完成（耗时 1.28s）（换阵0.7s · 洗练0.0s · 符文0.4s · 看门狗0）！", GreenColor);
			AddLog("实测 [F5 vs 测试15(3YPN0)] 80胜/0负 (100.00%) · 2.91s", GreenColor);

			for (int st = 0; st < 3; st++)
			{
				pvpV3ActiveTab = "core";
				SwitchTestPvpLayout(st, false);
				SwitchBottomTab("log");
				LayoutAll(this);
				Application.DoEvents();
				SaveSidebarPreview(Path.Combine(outDir, baseName + "_v" + (st + 1) + "_sidebar.png"));
				if (st == 2)
				{
					SwitchPvpV3Tab("tools");
					LayoutAll(this);
					Application.DoEvents();
					SaveSidebarPreview(Path.Combine(outDir, baseName + "_v3_tools_sidebar.png"));
				}
			}
			pvpV3ActiveTab = "core";
			SwitchTestPvpLayout(savedStyle, false);
		}

		private static string DecompressZlibUtf8(byte[] data, int offset, int length)
		{
			if (length < 2) return "";
			// Skip 2-byte zlib header (CMF, FLG) and decompress raw deflate stream
			using (var ms = new MemoryStream(data, offset + 2, length - 2))
			using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
			using (var outMs = new MemoryStream())
			{
				ds.CopyTo(outMs);
				return Encoding.UTF8.GetString(outMs.ToArray());
			}
		}

		// =====================================================================
		// Pure C# Zero-Dependency PNG tEXt / iTXt / zTXt ("maowa_formation_v1") Parser
		// =====================================================================
		internal static Dictionary<string, object> ExtractMaowaFormationFromPng(string pngPath, JavaScriptSerializer ser)
		{
			byte[] data = File.ReadAllBytes(pngPath);
			byte[] sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
			if (data.Length < 24) throw new InvalidDataException("文件过小，不是有效的 PNG 图片。");
			for (int i = 0; i < 8; i++)
			{
				if (data[i] != sig[i]) throw new InvalidDataException("当前仅支持带内嵌元数据的原生一图流 PNG（非 JPG/微信压缩图）。");
			}
			int pos = 8;
			while (pos + 12 <= data.Length)
			{
				int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
				if (len < 0 || pos + 12 + len > data.Length) break;
				string chunkType = Encoding.ASCII.GetString(data, pos + 4, 4);
				int dataStart = pos + 8;
				int dataEnd = dataStart + len;
				if (chunkType == "tEXt" || chunkType == "iTXt" || chunkType == "zTXt")
				{
					int nul = Array.IndexOf(data, (byte)0, dataStart, len);
					if (nul > dataStart)
					{
						string key = Encoding.GetEncoding("ISO-8859-1").GetString(data, dataStart, nul - dataStart);
						if (key == "maowa_formation_v1")
						{
							string payload = null;
							if (chunkType == "tEXt")
							{
								payload = Encoding.UTF8.GetString(data, nul + 1, dataEnd - (nul + 1));
							}
							else if (chunkType == "zTXt" && nul + 2 < dataEnd)
							{
								payload = DecompressZlibUtf8(data, nul + 2, dataEnd - (nul + 2));
							}
							else if (chunkType == "iTXt" && nul + 3 < dataEnd)
							{
								byte compFlag = data[nul + 1];
								int p = nul + 3; // skip compFlag & compMethod
								int langNul = Array.IndexOf(data, (byte)0, p, dataEnd - p);
								if (langNul >= p)
								{
									p = langNul + 1;
									int transNul = Array.IndexOf(data, (byte)0, p, dataEnd - p);
									if (transNul >= p)
									{
										p = transNul + 1;
										payload = compFlag == 1
											? DecompressZlibUtf8(data, p, dataEnd - p)
											: Encoding.UTF8.GetString(data, p, dataEnd - p);
									}
								}
							}
							if (!string.IsNullOrEmpty(payload))
							{
								object parsed = ser.DeserializeObject(payload);
								var dict = parsed as Dictionary<string, object>;
								if (dict == null) throw new InvalidDataException("maowa_formation_v1 元数据格式异常。");
								return dict;
							}
						}
					}
				}
				pos += 12 + len;
			}
			throw new InvalidDataException("该 PNG 不含 maowa_formation_v1 元数据，请使用本模拟器最新导出的一图流 PNG。");
		}

		private void PvpDrop_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void PvpDrop_DragDrop(object sender, DragEventArgs e)
		{
			try
			{
				string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
				if (files == null || files.Length == 0) return;
				LoadFormationFromPngPath(files[0]);
			}
			catch (Exception ex)
			{
				AddLog("拖图解析失败：" + ex.Message, RedColor);
				MessageBox.Show(this, ex.Message, "图片解析失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private void PickAndLoadFormationPng()
		{
			using (var ofd = new OpenFileDialog
			{
				Title = "选择本模拟器导出的一图流 PNG",
				Filter = "一图流 PNG (*.png)|*.png|所有文件 (*.*)|*.*",
				InitialDirectory = Directory.Exists(Path.Combine(PvpSimDir, "exports")) ? Path.Combine(PvpSimDir, "exports") : PvpSimDir
			})
			{
				if (ofd.ShowDialog(this) != DialogResult.OK) return;
				try { LoadFormationFromPngPath(ofd.FileName); }
				catch (Exception ex)
				{
					AddLog("读取一图流失败：" + ex.Message, RedColor);
					MessageBox.Show(this, ex.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
		}

		private void LoadFormationFromPngPath(string path)
		{
			Dictionary<string, object> parsed = ExtractMaowaFormationFromPng(path, json);
			loadedFormation = parsed;
			loadedFormationSource = Path.GetFileName(path);
			UpdateLoadedFormationUi();
			AddLog("已零误差解析一图流：" + FormatLoadedFormationSummary(), GreenColor);
		}

		private void PickAndLoadPresetDialog()
		{
			PresetLoadResult loaded = TestPvpExtras.LoadPresetsWithFallback(PvpSimDir);
			if (string.IsNullOrEmpty(loaded.RawJson))
			{
				MessageBox.Show(this, "未找到外部、云端或内置预设库 presets.json。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			Dictionary<string, object> all = json.DeserializeObject(loaded.RawJson) as Dictionary<string, object>;
			if (all == null) return;
			var keys = new List<string>();

			using (var dlg = new Form
			{
				Text = "选择预设阵容 · " + loaded.SourceLabel,
				Width = 500,
				Height = 470,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = SideColor,
				ForeColor = TextColor,
				MinimizeBox = false,
				MaximizeBox = false
			})
			{
				var lblSrc = new SmoothLabel
				{
					Text = "当前来源：" + loaded.SourceLabel,
					Dock = DockStyle.Top,
					Height = 24,
					BackColor = Color.Transparent,
					ForeColor = GoldColor,
					Font = UiTheme.GameFont(8.25f, FontStyle.Bold),
					Padding = new Padding(8, 0, 8, 0),
					TextAlign = ContentAlignment.MiddleLeft
				};
				var lb = new ListBox
				{
					Dock = DockStyle.Fill,
					BackColor = CardColor,
					ForeColor = TextColor,
					Font = UiTheme.UiFont(9.5f, FontStyle.Regular)
				};
				Action<Dictionary<string, object>> populate = delegate(Dictionary<string, object> map)
				{
					all = map;
					keys.Clear();
					lb.Items.Clear();
					if (map == null) return;
					foreach (string k in map.Keys)
					{
						if (!k.StartsWith("friend_", StringComparison.OrdinalIgnoreCase)) keys.Add(k);
					}
					keys.Sort(StringComparer.OrdinalIgnoreCase);
					foreach (string k in keys)
					{
						var d = map[k] as Dictionary<string, object>;
						string nm = (d != null && d.ContainsKey("name")) ? Convert.ToString(d["name"]) : k;
						lb.Items.Add(k + "  |  " + nm);
					}
					if (lb.Items.Count > 0) lb.SelectedIndex = 0;
				};
				populate(all);

				var bottomBar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 42, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(4, 3, 4, 3) };
				bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
				bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));

				var btnSync = new GameButton { Text = "在线同步最新预设库", Dock = DockStyle.Fill, Skin = GameButtonSkin.Stone, StretchWide = true, Font = UiTheme.GameFont(8.5f, FontStyle.Bold), Margin = new Padding(0, 0, 4, 0) };
				var btnOk = new GameButton { Text = "载入所选预设", Dock = DockStyle.Fill, Skin = GameButtonSkin.Gold, StretchWide = true, Font = UiTheme.GameFont(9f, FontStyle.Bold), Margin = new Padding(4, 0, 0, 0) };

				btnSync.Click += delegate
				{
					btnSync.Enabled = false;
					btnSync.Text = "正在同步云端预设…";
					ThreadPool.QueueUserWorkItem(delegate
					{
						try
						{
							PresetLoadResult synced = TestPvpExtras.SyncPresetsOnline(CancellationToken.None);
							var nextMap = json.DeserializeObject(synced.RawJson) as Dictionary<string, object>;
							if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
							dlg.BeginInvoke((MethodInvoker)delegate
							{
								populate(nextMap);
								lblSrc.Text = "当前来源：" + synced.SourceLabel;
								dlg.Text = "选择预设阵容 · " + synced.SourceLabel;
								btnSync.Text = "同步成功 ✓";
								btnSync.Enabled = true;
								AddLog("已在线同步最新 PvP 预设库（共 " + synced.PresetCount + " 套阵型）。", GreenColor);
							});
						}
						catch (Exception ex)
						{
							if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
							dlg.BeginInvoke((MethodInvoker)delegate
							{
								btnSync.Text = "在线同步最新预设库";
								btnSync.Enabled = true;
								MessageBox.Show(dlg, ex.Message, "同步预设库失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
							});
						}
					});
				};
				btnOk.Click += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
				lb.DoubleClick += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };

				bottomBar.Controls.Add(btnSync, 0, 0);
				bottomBar.Controls.Add(btnOk, 1, 0);
				dlg.Controls.Add(lb);
				dlg.Controls.Add(lblSrc);
				dlg.Controls.Add(bottomBar);
				if (dlg.ShowDialog(this) == DialogResult.OK && lb.SelectedIndex >= 0 && lb.SelectedIndex < keys.Count)
				{
					string key = keys[lb.SelectedIndex];
					loadedFormation = all[key] as Dictionary<string, object>;
					loadedFormationSource = "预设:" + key;
					UpdateLoadedFormationUi();
					AddLog("已载入预设：" + FormatLoadedFormationSummary(), GreenColor);
				}
			}
		}

		private string FormatLoadedFormationSummary()
		{
			if (loadedFormation == null) return "";
			string nm = loadedFormation.ContainsKey("name") ? Convert.ToString(loadedFormation["name"]) : loadedFormationSource;
			int wid = GetIntField(loadedFormation, "weapon", GetIntField(loadedFormation, "weapon_id", 12));
			var slots = loadedFormation.ContainsKey("slots") ? loadedFormation["slots"] as object[] : null;
			var runes = loadedFormation.ContainsKey("runes") ? loadedFormation["runes"] as object[] : null;
			return string.Format("{0}\r\n来源:{1} · 武器W{2} · {3}格 · 符文{4}颗", nm, loadedFormationSource, wid, slots != null ? slots.Length : 0, runes != null ? runes.Length : 0);
		}

		private void UpdateLoadedFormationUi()
		{
			if (pnlPvpDropZone != null && !pnlPvpDropZone.IsDisposed)
			{
				pnlPvpDropZone.Invalidate();
			}
		}

		private static int GetIntField(Dictionary<string, object> dict, string key, int fallback)
		{
			if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return fallback;
			int v;
			return int.TryParse(Convert.ToString(dict[key]), out v) ? v : fallback;
		}

		private bool BeginTestPvpWork(string title)
		{
			if (testPvpBusy)
			{
				AddLog("当前已有 PvP 任务运行中，请先等待完成或点击「停止当前任务」。", YellowColor);
				return false;
			}
			if (!coreReady)
			{
				AddLog("游戏页面尚未就绪，请等待登录完成后再操作。", YellowColor);
				return false;
			}
			testPvpBusy = true;
			testPvpCancel = new CancellationTokenSource();
			if (btnPvpStop != null && !btnPvpStop.IsDisposed) btnPvpStop.Enabled = true;
			SetStatusUi(title, BlueColor, "正在执行 PvP 专项任务…");
			return true;
		}

		private void EndTestPvpWork(string statusText, Color color)
		{
			testPvpBusy = false;
			if (testPvpCancel != null) { testPvpCancel.Dispose(); testPvpCancel = null; }
			if (btnPvpStop != null && !btnPvpStop.IsDisposed) btnPvpStop.Enabled = false;
			SetStatusUi(statusText, color, "");
		}

		private void CancelTestPvpWork()
		{
			if (testPvpCancel != null)
			{
				testPvpCancel.Cancel();
				AddLog("已发送停止信号，正在安全中止并还原网络钩子…", YellowColor);
			}
		}

		private async Task<string> ExecutePvpLuaRawAsync(string lua)
		{
			string script =
				"(function(){" +
				"try{" +
				"  if(window.__MWA_APP && window.__MWA_APP.ready()){ return { ok: true, val: window.__MWA_APP.runLua(" + json.Serialize(lua) + ") }; }" +
				"  var vm = window.__AANDM && window.__AANDM.vm;" +
				"  if(vm && typeof vm.execute === 'function'){ return { ok: true, val: vm.execute(" + json.Serialize(lua) + ") }; }" +
				"  return { ok: false, err: 'Lua VM 尚未就绪，请等待进入游戏主界面' };" +
				"}catch(e){ return { ok: false, err: String(e && e.message ? e.message : e) }; }" +
				"})()";
			return await ExecuteScriptAsync(script);
		}

		private async Task<object> EvalLuaDecodedAsync(string lua)
		{
			string raw = await ExecutePvpLuaRawAsync(lua);
			if (string.IsNullOrEmpty(raw) || raw == "null") return null;
			try
			{
				var wrap = json.DeserializeObject(raw) as Dictionary<string, object>;
				if (wrap != null && wrap.ContainsKey("ok"))
				{
					if (!Convert.ToBoolean(wrap["ok"]))
					{
						string err = wrap.ContainsKey("err") ? Convert.ToString(wrap["err"]) : "Lua 执行失败";
						throw new Exception(err);
					}
					return wrap.ContainsKey("val") ? wrap["val"] : null;
				}
				return wrap ?? json.DeserializeObject(raw);
			}
			catch (Exception ex)
			{
				if (ex.Message.Contains("Lua") || ex.Message.Contains("VM")) throw;
				return raw;
			}
		}

		private static string ToLuaLiteral(object v)
		{
			if (v == null) return "nil";
			if (v is bool) return ((bool)v) ? "true" : "false";
			if (v is int || v is long || v is double || v is float || v is decimal) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
			if (v is string)
			{
				string s = ((string)v).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
				return "\"" + s + "\"";
			}
			var dict = v as IDictionary<string, object>;
			if (dict != null)
			{
				var sb = new StringBuilder("{ ");
				bool first = true;
				foreach (var kv in dict)
				{
					if (!first) sb.Append(", ");
					first = false;
					sb.Append(kv.Key).Append(" = ").Append(ToLuaLiteral(kv.Value));
				}
				sb.Append(" }");
				return sb.ToString();
			}
			var list = v as IEnumerable;
			if (list != null)
			{
				var sb = new StringBuilder("{ ");
				bool first = true;
				foreach (object item in list)
				{
					if (!first) sb.Append(", ");
					first = false;
					sb.Append(ToLuaLiteral(item));
				}
				sb.Append(" }");
				return sb.ToString();
			}
			return "nil";
		}

		private string ReadEmbeddedLuaDeployer()
		{
			using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.lua_auto_deploy.lua"))
			{
				if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
			}
			string fallback = Path.Combine(PvpSimDir, "lua_auto_deploy.lua");
			if (File.Exists(fallback)) return File.ReadAllText(fallback, Encoding.UTF8);
			throw new FileNotFoundException("未找到内置部署引擎 lua_auto_deploy.lua");
		}

		// =====================================================================
		// Module 1 Execution: 1-Click Deploy Loaded Formation to F1~F5
		// =====================================================================
		private async Task DeployLoadedFormationAsync()
		{
			if (loadedFormation == null)
			{
				MessageBox.Show(this, "请先拖入一张本模拟器导出的一图流 PNG，或点击「选预设」载入阵容。", "尚未载入阵容", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			int fid = GetSelectedPvpFid();
			bool syncRunes = pvpSyncRunes;
			if (!BeginTestPvpWork("正在部署至阵型 " + fid)) return;

			var sw = Stopwatch.StartNew();
			bool needCleanup = false;
			try
			{
				// Scheme D: Cache compiled Lua deployer in VM by VERSION (skip 1,400-line recompile on hot swaps!)
				var verCheck = await EvalLuaDecodedAsync("return _G.__ANTIGRA_AUTO_DEPLOY and _G.__ANTIGRA_AUTO_DEPLOY.VERSION or ''");
				if (Convert.ToString(verCheck) != "3.0-serial-zero-stall")
				{
					string deployerLua = ReadEmbeddedLuaDeployer();
					await EvalLuaDecodedAsync(deployerLua);
				}

				int wid = GetIntField(loadedFormation, "weapon", GetIntField(loadedFormation, "weapon_id", 12));
				var rawSlots = loadedFormation["slots"] as object[];
				var slotList = new List<Dictionary<string, object>>();
				if (rawSlots != null)
				{
					foreach (object o in rawSlots)
					{
						var sd = o as Dictionary<string, object>;
						if (sd == null) continue;
						slotList.Add(new Dictionary<string, object>
						{
							{ "id", GetIntField(sd, "id", 0) },
							{ "name", sd.ContainsKey("name") ? Convert.ToString(sd["name"]) : "" },
							{ "wash", sd.ContainsKey("wash") ? Convert.ToString(sd["wash"]) : "" }
						});
					}
				}
				var rawRunes = loadedFormation.ContainsKey("runes") ? loadedFormation["runes"] as object[] : null;
				var runeList = new List<object>();
				if (rawRunes != null)
				{
					foreach (object r in rawRunes)
					{
						var arr = r as object[];
						if (arr != null && arr.Length >= 2)
							runeList.Add(new int[] { Convert.ToInt32(arr[0]), Convert.ToInt32(arr[1]) });
						else
							runeList.Add(new int[] { Convert.ToInt32(r), 20 });
					}
				}

				var cfg = new Dictionary<string, object>
				{
					{ "fid", fid },
					{ "weapon_id", wid },
					{ "reuse", true },
					{ "reuse_idle", true },
					{ "full_auto", true },
					{ "max_forge_chance", true },
					{ "auto_restock", true },
					{ "setup_runes", syncRunes && runeList.Count == 5 },
					{ "runes", runeList },
					{ "slots", slotList }
				};

				AddLog(string.Format("开始一键部署 -> 阵型{0}（同步符文:{1}，其余阵型严格保护）", fid, syncRunes ? "是" : "否"), BlueColor);
				object startObj = await EvalLuaDecodedAsync("return _G.__ANTIGRA_AUTO_DEPLOY.start(" + ToLuaLiteral(cfg) + ")");
				var stDict = startObj as Dictionary<string, object>;
				if (stDict == null || !stDict.ContainsKey("ok") || !Convert.ToBoolean(stDict["ok"]))
				{
					string err = (stDict != null && stDict.ContainsKey("err")) ? Convert.ToString(stDict["err"]) : "启动失败（请确认已进入游戏主界面）";
					throw new Exception(err);
				}

				int lastLog = 0;
				Dictionary<string, object> finalStatus = null;
				while (!testPvpCancel.IsCancellationRequested)
				{
					await Task.Delay(40);
					var st = await EvalLuaDecodedAsync("return _G.__ANTIGRA_AUTO_DEPLOY.status()") as Dictionary<string, object>;
					if (st == null) continue;
					var logs = st.ContainsKey("logs") ? st["logs"] as object[] : null;
					if (logs != null)
					{
						while (lastLog < logs.Length)
						{
							AddLog("  " + Convert.ToString(logs[lastLog++]), AutoLogColor);
						}
					}
					if (st.ContainsKey("done") && Convert.ToBoolean(st["done"]))
					{
						finalStatus = st;
						break;
					}
				}

				await EvalLuaDecodedAsync("if _G.__ANTIGRA_AUTO_DEPLOY then _G.__ANTIGRA_AUTO_DEPLOY.cleanup() end; return true");
				if (testPvpCancel.IsCancellationRequested)
				{
					EndTestPvpWork("部署已手动停止", YellowColor);
					return;
				}
				if (finalStatus == null || !Convert.ToBoolean(finalStatus["ok"]))
				{
					string err = (finalStatus != null && finalStatus.ContainsKey("err")) ? Convert.ToString(finalStatus["err"]) : "部署超时或失败";
					throw new Exception(err);
				}
				var res = finalStatus["result"] as Dictionary<string, object>;
				var bs = res != null ? res["base_stats"] as Dictionary<string, object> : null;
				var stMap = res != null ? res["stats"] as Dictionary<string, object> : null;
				string statLine = bs != null
					? string.Format("先{0} / 防{1} / 闪{2} / 王{3}", bs["speed"], bs["defense"], bs["dodge"], bs["authority"])
					: "";
				string timingLine = "";
				var ps = res != null ? res["phase_sec"] as Dictionary<string, object> : null;
				if (ps != null && stMap != null)
				{
					timingLine = string.Format("（换阵{0:0.1}s · 培养洗练{1:0.1}s · 符文{2:0.1}s · 总包{3} · 看门狗{4}）",
						Convert.ToDouble(ps["formation"]), Convert.ToDouble(ps["train"]), Convert.ToDouble(ps["runes"]),
						stMap.ContainsKey("packets") ? stMap["packets"] : 0,
						stMap.ContainsKey("watchdogs") ? stMap["watchdogs"] : 0);
				}
				AddLog(string.Format("部署完成（耗时 {0:0.2}s）{1}！阵型{2} · 面板：{3}", sw.Elapsed.TotalSeconds, timingLine, fid, statLine), GreenColor);
				EndTestPvpWork(string.Format("阵型{0} 部署完成 ({1:0.1}s)", fid, sw.Elapsed.TotalSeconds), GreenColor);
			}
			catch (Exception ex)
			{
				needCleanup = true;
				AddLog("一键部署失败：" + ex.Message, RedColor);
				EndTestPvpWork("部署失败", RedColor);
			}
			if (needCleanup)
			{
				try { await EvalLuaDecodedAsync("if _G.__ANTIGRA_AUTO_DEPLOY then _G.__ANTIGRA_AUTO_DEPLOY.cleanup() end; return true"); } catch { }
			}
		}

		// =====================================================================
		// Module 2 Execution: Lossless 5-Rune Switcher (ZERO UpgradeRune!)
		// =====================================================================
		private async Task ApplyRunesInClientAsync(int[] tids, string label)
		{
			if (tids == null || tids.Length != 5) return;
			if (!BeginTestPvpWork("无损切换符文：" + label)) return;
			var sw = Stopwatch.StartNew();
			bool needRestore = false;
			try
			{
				string targetLua = "{ " + string.Join(", ", Array.ConvertAll(tids, delegate(int x) { return x.ToString(); })) + " }";
				string lua =
					"local rl = require 'logic.rune'\n" +
					"local net = require 'util.network'\n" +
					"local target = " + targetLua + "\n" +
					"if rl.SetSelectedAutoCompose then rl:SetSelectedAutoCompose(false) end\n" +
					"if not _G.__ANTIGRA_ORIG_DISPATCH then _G.__ANTIGRA_ORIG_DISPATCH = net.event_listener.Dispatch end\n" +
					"if rl.QueryRuneLoadouts and not _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS = rl.QueryRuneLoadouts end\n" +
					"local orig = _G.__ANTIGRA_ORIG_DISPATCH\n" +
					"local orig_ql = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS\n" +
					"if orig_ql then rl.QueryRuneLoadouts = function(self) self.rune_query_pending = false; self.rune_query_needed = false; return false end end\n" +
					"local ST = { done = false, ok = false, err = nil, exchanges = 0, equips = 0, retries = 0, inflight = nil, ts = 0 }\n" +
					"_G.__PVP_RUNES = ST\n" +
					"local function restore() net.event_listener.Dispatch = orig; if orig_ql then rl.QueryRuneLoadouts = orig_ql end end\n" +
					"local function done_ok() ST.done = true; ST.ok = true; restore(); if orig_ql then pcall(function() rl:QueryRuneLoadouts() end) end end\n" +
					"local function done_err(m) ST.done = true; ST.ok = false; ST.err = m; restore() end\n" +
					"local function step()\n" +
					"  if ST.done or ST.inflight then return end\n" +
					"  if rl.SetSelectedAutoCompose then rl:SetSelectedAutoCompose(false) end\n" +
					"  rl.rune_query_pending = false; rl.rune_query_needed = false; rl.rune_native_pending = 0\n" +
					"  local all_r = rl:GetRuneList() or {}\n" +
					"  if #all_r == 0 and not ST.queried then ST.queried = true; ST.inflight = 'query'; ST.ts = os.clock(); net:Send({ query_rune_info = {} }); return end\n" +
					"  local eq = rl:GetRuneEquipmentList() or {}\n" +
					"  local need = {}; for _, t in ipairs(target) do need[t] = (need[t] or 0) + 1 end\n" +
					"  local chosen, used = {}, {}\n" +
					"  for pos = 1, 5 do local r = eq[pos]; if r and (r.level or 0) >= 20 and (need[r.template_id] or 0) > 0 and not used[r.rune_id] then chosen[pos] = r; used[r.rune_id] = true; need[r.template_id] = need[r.template_id] - 1 end end\n" +
					"  local rem = {}; for _, t in ipairs(target) do if (need[t] or 0) > 0 then rem[#rem + 1] = t; need[t] = need[t] - 1 end end\n" +
					"  local ri, sw = 1, {}; for pos = 1, 5 do if not chosen[pos] then sw[pos] = rem[ri]; ri = ri + 1 end end\n" +
					"  for pos = 1, 5 do if not chosen[pos] and sw[pos] then for _, r in ipairs(all_r) do if r.template_id == sw[pos] and (r.level or 0) >= 20 and not used[r.rune_id] then chosen[pos] = r; used[r.rune_id] = true; break end end end end\n" +
					"  for pos = 1, 5 do if not chosen[pos] and sw[pos] then\n" +
					"    local wt = sw[pos]; local low = nil\n" +
					"    for _, r in ipairs(all_r) do if r.template_id == wt and not used[r.rune_id] then if not low or (r.level or 0) > (low.level or 0) then low = r end end end\n" +
					"    if not low then return done_err('背包缺少符文 tid=' .. tostring(wt)) end\n" +
					"    local donor = nil\n" +
					"    for _, r in ipairs(all_r) do if (r.level or 0) >= 20 and not used[r.rune_id] and r.rune_id ~= low.rune_id then if not donor or ((r.equip_pos or 0) == 0 and (donor.equip_pos or 0) ~= 0) then donor = r end end end\n" +
					"    if not donor then return done_err('没有可用的 Lv20 转换源符文') end\n" +
					"    ST.inflight = 'exch'; ST.ts = os.clock(); ST.exchanges = ST.exchanges + 1; rl:ExchangeRuneProperty(low.rune_id, donor.rune_id); return\n" +
					"  end end\n" +
					"  for pos = 1, 5 do local wr = chosen[pos]; local cr = eq[pos]; if not cr or cr.rune_id ~= wr.rune_id then ST.inflight = 'equip'; ST.ts = os.clock(); ST.equips = ST.equips + 1; rl:EquipRune(pos, wr); return end end\n" +
					"  done_ok()\n" +
					"end\n" +
					"_G.__PVP_RUNES_STEP = step\n" +
					"net.event_listener.Dispatch = function(self, ev, msg, ...)\n" +
					"  local r1, r2, r3 = orig(self, ev, msg, ...)\n" +
					"  if not ST.done and (ev == 'rune_exchange_ret' or ev == 'equip_rune_ret' or ev == 'query_rune_info_ret' or ev == 'query_rune_loadouts_ret') then\n" +
					"    if ev ~= 'query_rune_loadouts_ret' then ST.inflight = nil end\n" +
					"    local res = type(msg) == 'table' and msg.result or 'success'\n" +
					"    if ev ~= 'query_rune_info_ret' and ev ~= 'query_rune_loadouts_ret' and res ~= 'success' and res ~= '' then done_err(ev .. ':' .. tostring(res)) else pcall(step) end\n" +
					"  end\n" +
					"  return r1, r2, r3\n" +
					"end\n" +
					"step(); return true";

				await EvalLuaDecodedAsync(lua);
				Dictionary<string, object> st = null;
				for (int i = 0; i < 250 && !testPvpCancel.IsCancellationRequested; i++)
				{
					await Task.Delay(40);
					st = await EvalLuaDecodedAsync(
						"local s = _G.__PVP_RUNES; if s and not s.done and s.inflight and (os.clock() - s.ts) > 1.2 then " +
						"s.retries = (s.retries or 0) + 1; if s.retries > 4 then s.done = true; s.ok = false; s.err = '服务器未响应:' .. tostring(s.inflight) else s.inflight = nil; if _G.__PVP_RUNES_STEP then pcall(_G.__PVP_RUNES_STEP) end end end; return s"
					) as Dictionary<string, object>;
					if (st != null && st.ContainsKey("done") && Convert.ToBoolean(st["done"])) break;
				}
				await EvalLuaDecodedAsync("if _G.__ANTIGRA_ORIG_DISPATCH then require('util.network').event_listener.Dispatch = _G.__ANTIGRA_ORIG_DISPATCH end; if _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then require('logic.rune').QueryRuneLoadouts = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS end; return true");
				if (st == null || !Convert.ToBoolean(st["ok"]))
					throw new Exception(st != null && st.ContainsKey("err") ? Convert.ToString(st["err"]) : "换符文超时");
				AddLog(string.Format("符文已无损切换为 [{0}]（转换{1}次 · 装备{2}次 · 重试{3} · 耗时 {4:0.2}s）", label, st["exchanges"], st["equips"], st["retries"], sw.Elapsed.TotalSeconds), GreenColor);
				EndTestPvpWork("符文切换完成", GreenColor);
			}
			catch (Exception ex)
			{
				needRestore = true;
				AddLog("换符文失败：" + ex.Message, RedColor);
				EndTestPvpWork("换符文失败", RedColor);
			}
			if (needRestore)
			{
				try { await EvalLuaDecodedAsync("if _G.__ANTIGRA_ORIG_DISPATCH then require('util.network').event_listener.Dispatch = _G.__ANTIGRA_ORIG_DISPATCH end; if _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then require('logic.rune').QueryRuneLoadouts = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS end; return true"); } catch { }
			}
		}

		private void ShowCustomRuneDialog()
		{
			var choices = new KeyValuePair<int, string>[]
			{
				new KeyValuePair<int, string>(59, "59 防御衰弱 (-100敌防)"),
				new KeyValuePair<int, string>(63, "63 王者衰弱 (-50敌王)"),
				new KeyValuePair<int, string>(69, "69 闪王衰弱 (-60敌闪-30敌王)"),
				new KeyValuePair<int, string>(66, "66 防御王者 (+60防+30王)"),
				new KeyValuePair<int, string>(62, "62 王者成长 (+50王)"),
				new KeyValuePair<int, string>(64, "64 先攻王者 (+60先+30王)"),
				new KeyValuePair<int, string>(67, "67 防王衰弱 (-60敌防-30敌王)"),
				new KeyValuePair<int, string>(75, "75 先攻防御 (+60先+60防)")
			};
			using (var dlg = new Form
			{
				Text = "自定义 5 颗无损符文",
				Width = 360,
				Height = 290,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = SideColor,
				ForeColor = TextColor,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MinimizeBox = false,
				MaximizeBox = false
			})
			{
				var boxes = new ComboBox[5];
				var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12) };
				tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));
				tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
				for (int i = 0; i < 5; i++)
				{
					tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
					tbl.Controls.Add(new SmoothLabel { Text = "槽位 " + (i + 1), Dock = DockStyle.Fill, ForeColor = GoldColor, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
					var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardColor, ForeColor = TextColor };
					foreach (var c in choices) cb.Items.Add(c.Value);
					cb.SelectedIndex = 0;
					boxes[i] = cb;
					tbl.Controls.Add(cb, 1, i);
				}
				tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
				var btnGo = new GameButton { Text = "立即无损切换", Dock = DockStyle.Fill, Skin = GameButtonSkin.Gold, StretchWide = true, Font = UiTheme.GameFont(9f, FontStyle.Bold) };
				btnGo.Click += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
				tbl.Controls.Add(btnGo, 0, 5);
				tbl.SetColumnSpan(btnGo, 2);
				dlg.Controls.Add(tbl);
				if (dlg.ShowDialog(this) == DialogResult.OK)
				{
					var tids = new int[5];
					for (int i = 0; i < 5; i++) tids[i] = choices[boxes[i].SelectedIndex].Key;
					var _ = ApplyRunesInClientAsync(tids, string.Join("/", Array.ConvertAll(tids, delegate(int x) { return x.ToString(); })));
				}
			}
		}

		// =====================================================================
		// Module 3: Strict in_flight=1 Friend Battle Workbench (Single 100 / Multi 80)
		// =====================================================================
		private async void ShowPvpFriendBattleDialog()
		{
			if (!coreReady) { AddLog("请先等待游戏加载完成。", YellowColor); return; }
			var friends = new List<KeyValuePair<string, string>>();
			try
			{
				var raw = await EvalLuaDecodedAsync(
					"local s = require 'logic.social'; local out = {}; " +
					"for _, f in pairs((s and s.friend_list) or {}) do if f.user_id then out[#out+1] = { uid = tostring(f.user_id), name = tostring(f.name or f.user_id) } end end; return out"
				) as object[];
				if (raw != null)
				{
					foreach (object o in raw)
					{
						var fd = o as Dictionary<string, object>;
						if (fd != null && fd.ContainsKey("uid"))
							friends.Add(new KeyValuePair<string, string>(Convert.ToString(fd["uid"]), Convert.ToString(fd["name"])));
					}
				}
			}
			catch { }
			if (friends.Count == 0)
			{
				foreach (var kv in KnownFriendLabels) friends.Add(kv);
			}

			using (var dlg = new Form
			{
				Text = "好友切磋实测台（严格锁死在途=1）",
				Width = 440,
				Height = 460,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = SideColor,
				ForeColor = TextColor,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MinimizeBox = false,
				MaximizeBox = false
			})
			{
				var clb = new CheckedListBox
				{
					Dock = DockStyle.Fill,
					CheckOnClick = true,
					BackColor = CardColor,
					ForeColor = TextColor,
					Font = UiTheme.UiFont(9.5f, FontStyle.Regular)
				};
				foreach (var f in friends) clb.Items.Add(f.Value + " (" + f.Key + ")", f.Key == "3YPN0");

				var top = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(10, 6, 10, 4) };
				var lblTip = new SmoothLabel { Text = "规则：严格在途=1（无并发丢包）· 勾选1人默认100把 · 多人默认各80把", Dock = DockStyle.Top, Height = 22, ForeColor = GoldColor };
				var rowQuick = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent, Padding = new Padding(0, 2, 0, 0) };
				var btnAll = new GameButton { Text = "全选对手", Width = 86, Height = 26, Skin = GameButtonSkin.Stone, Font = UiTheme.GameFont(7.8f, FontStyle.Bold) };
				var btnNone = new GameButton { Text = "清空勾选", Width = 86, Height = 26, Skin = GameButtonSkin.Stone, Font = UiTheme.GameFont(7.8f, FontStyle.Bold) };
				var btnTop7 = new GameButton { Text = "勾选常用强敌", Width = 106, Height = 26, Skin = GameButtonSkin.Gold, Font = UiTheme.GameFont(7.8f, FontStyle.Bold) };
				btnAll.Click += delegate { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, true); };
				btnNone.Click += delegate { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false); };
				btnTop7.Click += delegate
				{
					for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, KnownFriendLabels.ContainsKey(friends[i].Key));
				};
				rowQuick.Controls.Add(btnAll);
				rowQuick.Controls.Add(btnNone);
				rowQuick.Controls.Add(btnTop7);
				var rowOpt = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, BackColor = Color.Transparent };
				var cbFid = new ComboBox { Width = 78, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardColor, ForeColor = TextColor };
				cbFid.Items.AddRange(new object[] { "阵型1", "阵型2", "阵型3", "阵型4", "阵型5" });
				cbFid.SelectedIndex = GetSelectedPvpFid() - 1;
				var numN = new NumericUpDown { Width = 74, Minimum = 10, Maximum = 500, Value = 100, BackColor = CardColor, ForeColor = TextColor };
				var numTarget = new NumericUpDown { Width = 62, Minimum = 1, Maximum = 100, Value = 70, BackColor = CardColor, ForeColor = TextColor };
				clb.ItemCheck += delegate
				{
					dlg.BeginInvoke((MethodInvoker)delegate
					{
						int cnt = clb.CheckedIndices.Count;
						numN.Value = cnt <= 1 ? 100 : 80;
					});
				};
				rowOpt.Controls.Add(new SmoothLabel { Text = "出战:", Width = 40, Height = 24, ForeColor = TextColor, TextAlign = ContentAlignment.MiddleLeft });
				rowOpt.Controls.Add(cbFid);
				rowOpt.Controls.Add(new SmoothLabel { Text = "每好友把数:", Width = 76, Height = 24, ForeColor = TextColor, TextAlign = ContentAlignment.MiddleRight });
				rowOpt.Controls.Add(numN);
				rowOpt.Controls.Add(new SmoothLabel { Text = "达标%:", Width = 48, Height = 24, ForeColor = TextColor, TextAlign = ContentAlignment.MiddleRight });
				rowOpt.Controls.Add(numTarget);
				top.Controls.Add(rowOpt);
				top.Controls.Add(rowQuick);
				top.Controls.Add(lblTip);

				var btnStart = new GameButton { Text = "开始实机切磋 (in_flight=1)", Dock = DockStyle.Bottom, Height = 40, Skin = GameButtonSkin.Green, StretchWide = true, Font = UiTheme.GameFont(9.2f, FontStyle.Bold) };
				btnStart.Click += delegate
				{
					if (clb.CheckedIndices.Count == 0) { MessageBox.Show(dlg, "请至少勾选 1 位好友。", "提示"); return; }
					dlg.DialogResult = DialogResult.OK;
					dlg.Close();
				};

				dlg.Controls.Add(clb);
				dlg.Controls.Add(top);
				dlg.Controls.Add(btnStart);

				if (dlg.ShowDialog(this) == DialogResult.OK)
				{
					var chosen = new List<KeyValuePair<string, string>>();
					foreach (int idx in clb.CheckedIndices) chosen.Add(friends[idx]);
					await RunPvpFriendBattlesAsync(cbFid.SelectedIndex + 1, chosen, (int)numN.Value, (int)numTarget.Value);
				}
			}
		}

		private async Task RunPvpFriendBattlesAsync(int fid, List<KeyValuePair<string, string>> targets, int nEach, int targetPct)
		{
			if (!BeginTestPvpWork(string.Format("实机切磋 F{0} (在途=1)", fid))) return;
			var swAll = Stopwatch.StartNew();
			var reportRows = new List<Dictionary<string, object>>();
			try
			{
				await EvalLuaDecodedAsync(string.Format("local tl = require 'logic.troop'; if tl:GetCurFormationId() ~= {0} then tl:ChangeFormation({0}) end; return true", fid));
				await Task.Delay(300);

				foreach (var target in targets)
				{
					if (testPvpCancel.IsCancellationRequested) break;
					var sw = Stopwatch.StartNew();
					string uid = target.Key;
					string label = target.Value;
					AddLog(string.Format("开打 [F{0} vs {1}({2})] 共 {3} 把（严格在途=1）…", fid, label, uid, nEach), BlueColor);

					string lua =
						"local net = require 'util.network'\n" +
						"if not _G.__ANTIGRA_ORIG_DISPATCH then _G.__ANTIGRA_ORIG_DISPATCH = net.event_listener.Dispatch end\n" +
						"local orig = _G.__ANTIGRA_ORIG_DISPATCH\n" +
						"local ST = { active = true, target = " + nEach + ", uid = " + ToLuaLiteral(uid) + ", wins = 0, losses = 0, resolved = 0, last_ts = os.clock(), err = nil, sample_bp = nil }\n" +
						"_G.__PVP_B1 = ST\n" +
						"net.event_listener.Dispatch = function(self, ev, msg, ...)\n" +
						"  if ev == 'challenge_friend_ret' and ST.active then\n" +
						"    if type(msg) == 'table' and msg.result == 'success' then\n" +
						"      ST.resolved = ST.resolved + 1\n" +
						"      if not ST.sample_bp then ST.sample_bp = msg.battle_property end\n" +
						"      local w = (msg.is_winner == true or msg.is_winner == 1 or msg.is_win == true or msg.is_win == 1)\n" +
						"      if w then ST.wins = ST.wins + 1 else ST.losses = ST.losses + 1 end\n" +
						"      if ST.resolved < ST.target then ST.last_ts = os.clock(); net:Send({ challenge_friend = { user_id = ST.uid } })\n" +
						"      else ST.active = false; net.event_listener.Dispatch = orig end\n" +
						"    else ST.err = tostring(type(msg) == 'table' and msg.result or 'bad_msg'); ST.active = false; net.event_listener.Dispatch = orig end\n" +
						"    return\n" +
						"  end\n" +
						"  return orig(self, ev, msg, ...)\n" +
						"end\n" +
						"net:Send({ challenge_friend = { user_id = ST.uid } }); return true";

					await EvalLuaDecodedAsync(lua);
					Dictionary<string, object> st = null;
					while (!testPvpCancel.IsCancellationRequested)
					{
						await Task.Delay(120);
						st = await EvalLuaDecodedAsync(
							"local s = _G.__PVP_B1; if s and s.active and (os.clock() - s.last_ts) > 3.0 then s.last_ts = os.clock(); require('util.network'):Send({ challenge_friend = { user_id = s.uid } }) end; return s"
						) as Dictionary<string, object>;
						if (st == null || !Convert.ToBoolean(st["active"])) break;
					}
					await EvalLuaDecodedAsync("if _G.__PVP_B1 then _G.__PVP_B1.active = false end; if _G.__ANTIGRA_ORIG_DISPATCH then require('util.network').event_listener.Dispatch = _G.__ANTIGRA_ORIG_DISPATCH end; return true");

					int wins = GetIntField(st, "wins", 0);
					int losses = GetIntField(st, "losses", 0);
					int resolved = GetIntField(st, "resolved", wins + losses);
					double wr = resolved > 0 ? Math.Round(wins * 100.0 / resolved, 2) : 0.0;
					bool pass = resolved >= nEach && wr >= targetPct;
					AddLog(string.Format("  [{0}({1})] {2}胜/{3}负 ({4:0.00}%) {5} · {6:0.2}s", label, uid, wins, losses, wr, pass ? "达标" : "未达", sw.Elapsed.TotalSeconds), pass ? GreenColor : YellowColor);
					reportRows.Add(new Dictionary<string, object>
					{
						{ "fid", fid }, { "uid", uid }, { "label", label }, { "n", resolved },
						{ "wins", wins }, { "losses", losses }, { "winrate_pct", wr },
						{ "pass", pass }, { "sec", Math.Round(sw.Elapsed.TotalSeconds, 2) }
					});
				}

				if (reportRows.Count > 0)
				{
					double minWr = 100.0, sumWr = 0.0;
					bool allPass = true;
					foreach (var r in reportRows)
					{
						double w = Convert.ToDouble(r["winrate_pct"]);
						if (w < minWr) minWr = w;
						sumWr += w;
						if (!Convert.ToBoolean(r["pass"])) allPass = false;
					}
					double avgWr = Math.Round(sumWr / reportRows.Count, 2);
					string repPath = Path.Combine(PvpSimDir, "exports", "helper_live_report_f" + fid + ".json");
					Directory.CreateDirectory(Path.GetDirectoryName(repPath));
					var repObj = new Dictionary<string, object>
					{
						{ "tested_at", DateTime.UtcNow.ToString("o") },
						{ "in_flight", 1 }, { "fid", fid }, { "target_pct", targetPct },
						{ "total_sec", Math.Round(swAll.Elapsed.TotalSeconds, 2) },
						{ "min_winrate_pct", minWr }, { "avg_winrate_pct", avgWr }, { "all_pass", allPass },
						{ "results", reportRows }
					};
					File.WriteAllText(repPath, json.Serialize(repObj), Encoding.UTF8);
					AddLog(string.Format("实测完毕：最低 {0:0.2}% · 均胜 {1:0.2}% · 总耗时 {2:0.2}s（战报已保存，可点 ⑥ 导出一图流）", minWr, avgWr, swAll.Elapsed.TotalSeconds), allPass ? GreenColor : GoldColor);
					EndTestPvpWork(string.Format("实测完成 均胜{0:0.1}%", avgWr), allPass ? GreenColor : GoldColor);
					if (reportRows.Count > 1 && !IsDisposed)
					{
						TestPvpExtras.ShowBattleMatrixDialog(this, fid, targetPct, minWr, avgWr, swAll.Elapsed.TotalSeconds, reportRows);
					}
				}
				else
				{
					EndTestPvpWork("实测已中止", YellowColor);
				}
			}
			catch (Exception ex)
			{
				AddLog("实机切磋异常：" + ex.Message, RedColor);
				EndTestPvpWork("切磋失败", RedColor);
			}
		}

		// =====================================================================
		// Module 4: Retained Test-Server Mercenary Cultivation (Gear/Grow/Contract/Wash)
		// =====================================================================
		private void ShowMercWashDialog()
		{
			using (var dlg = new Form
			{
				Text = "洗界限突破设置",
				Width = 320,
				Height = 190,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = SideColor,
				ForeColor = TextColor,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MinimizeBox = false,
				MaximizeBox = false
			})
			{
				var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
				tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
				tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
				tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
				tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
				tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

				var cbProp = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardColor, ForeColor = TextColor };
				cbProp.Items.AddRange(new object[] { "先攻 (speed)", "防御 (defense)", "闪避 (dodge)", "王者 (authority)" });
				cbProp.SelectedIndex = 0;
				var numVal = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 5, Value = 5, BackColor = CardColor, ForeColor = TextColor };
				cbProp.SelectedIndexChanged += delegate { numVal.Value = cbProp.SelectedIndex == 3 ? 2 : 5; };

				tbl.Controls.Add(new SmoothLabel { Text = "目标属性:", Dock = DockStyle.Fill, ForeColor = GoldColor, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
				tbl.Controls.Add(cbProp, 1, 0);
				tbl.Controls.Add(new SmoothLabel { Text = "目标值 ≥:", Dock = DockStyle.Fill, ForeColor = GoldColor, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
				tbl.Controls.Add(numVal, 1, 1);

				var btnGo = new GameButton { Text = "开始洗练当前上阵阵容", Dock = DockStyle.Fill, Skin = GameButtonSkin.Violet, StretchWide = true, Font = UiTheme.GameFont(9f, FontStyle.Bold) };
				btnGo.Click += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
				tbl.Controls.Add(btnGo, 0, 2);
				tbl.SetColumnSpan(btnGo, 2);
				dlg.Controls.Add(tbl);

				if (dlg.ShowDialog(this) == DialogResult.OK)
				{
					string[] propCodes = new string[] { "speed", "defense", "dodge", "authority" };
					var _ = RunTestMercLoopAsync("wash", "洗界限突破", propCodes[cbProp.SelectedIndex], (int)numVal.Value);
				}
			}
		}

		private async Task RunTestMercLoopAsync(string mode, string label, string washProp, int washVal)
		{
			if (!BeginTestPvpWork(label)) return;
			try
			{
				string startLua;
				if (mode == "gear") startLua = "return __MWA:start_merc_gear()";
				else if (mode == "grow") startLua = "return __MWA:start_merc_grow()";
				else if (mode == "contract") startLua = "return __MWA:start_merc_contract()";
				else startLua = string.Format("return __MWA:start_merc_wash({0}, {1})", ToLuaLiteral(washProp), washVal);

				var r0 = await EvalLuaDecodedAsync(startLua) as Dictionary<string, object>;
				if (r0 != null && r0.ContainsKey("message")) AddLog("[" + label + "] " + Convert.ToString(r0["message"]), BlueColor);
				if (r0 != null && r0.ContainsKey("done") && Convert.ToBoolean(r0["done"]))
				{
					EndTestPvpWork(label + " 完成", GreenColor);
					return;
				}

				string lastMsg = "";
				while (!testPvpCancel.IsCancellationRequested)
				{
					var step = await EvalLuaDecodedAsync("return __MWA:merc_step()") as Dictionary<string, object>;
					if (step == null) { await Task.Delay(120); continue; }
					string msg = step.ContainsKey("message") ? Convert.ToString(step["message"]) : "";
					if (!string.IsNullOrEmpty(msg) && msg != lastMsg)
					{
						lastMsg = msg;
						AddLog("[" + label + "] " + msg, AutoLogColor);
					}
					if (step.ContainsKey("done") && Convert.ToBoolean(step["done"]))
					{
						AddLog("[" + label + "] 完成：" + msg, GreenColor);
						EndTestPvpWork(label + " 完成", GreenColor);
						return;
					}
					int delayMs = 35;
					if (step.ContainsKey("delaySeconds") && step["delaySeconds"] != null)
					{
						double ds;
						if (double.TryParse(Convert.ToString(step["delaySeconds"]), out ds))
							delayMs = Math.Max(15, Math.Min(300, (int)(ds * 1000)));
					}
					await Task.Delay(delayMs);
				}
				EndTestPvpWork(label + " 已停止", YellowColor);
			}
			catch (Exception ex)
			{
				AddLog(label + " 异常：" + ex.Message, RedColor);
				EndTestPvpWork(label + " 失败", RedColor);
			}
		}

		// =====================================================================
		// Module 5: Built-in 10-Core Auto-Optimize Dialog (calls local maowa.mjs)
		// =====================================================================
		private void ShowPvpOptimizeDialog()
		{
			using (var dlg = new Form
			{
				Text = "内置多核一键寻优（10核并行）",
				Width = 420,
				Height = 350,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = SideColor,
				ForeColor = TextColor,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MinimizeBox = false,
				MaximizeBox = false
			})
			{
				var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12) };
				tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
				tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
				for (int i = 0; i < 5; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
				tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

				var txtOpps = new TextBox { Dock = DockStyle.Fill, Text = "02,03,15,16", BackColor = CardColor, ForeColor = TextColor, Font = UiTheme.UiFont(9.5f, FontStyle.Bold) };
				var cbObj = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardColor, ForeColor = TextColor };
				cbObj.Items.AddRange(new object[] { "保底胜率最高 (floor)", "综合均胜最高 (avg)" });
				cbObj.SelectedIndex = 0;
				var cbRunes = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardColor, ForeColor = TextColor };
				cbRunes.Items.AddRange(new object[] { "仅背包现有符文 (bag)", "锁死当前符文不动 (keep)", "允许评估理论全符文 (all)" });
				cbRunes.SelectedIndex = 0;
				var numTarget = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 50, Maximum = 100, Value = 70, BackColor = CardColor, ForeColor = TextColor };
				var chkAutoDeploy = new CheckBox { Text = "算完后自动载入并部署到当前选定阵型", Dock = DockStyle.Fill, Checked = false, ForeColor = GreenColor, BackColor = Color.Transparent };

				tbl.Controls.Add(new SmoothLabel { Text = "对手简称/UID:", Dock = DockStyle.Fill, ForeColor = GoldColor, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
				tbl.Controls.Add(txtOpps, 1, 0);
				tbl.Controls.Add(new SmoothLabel { Text = "优化目标:", Dock = DockStyle.Fill, ForeColor = GoldColor, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
				tbl.Controls.Add(cbObj, 1, 1);
				tbl.Controls.Add(new SmoothLabel { Text = "符文策略:", Dock = DockStyle.Fill, ForeColor = GoldColor, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
				tbl.Controls.Add(cbRunes, 1, 2);
				tbl.Controls.Add(new SmoothLabel { Text = "目标保底%:", Dock = DockStyle.Fill, ForeColor = GoldColor, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
				tbl.Controls.Add(numTarget, 1, 3);
				tbl.Controls.Add(chkAutoDeploy, 0, 4);
				tbl.SetColumnSpan(chkAutoDeploy, 2);

				var btnGo = new GameButton { Text = "开始同步并 10 核寻优", Dock = DockStyle.Fill, Skin = GameButtonSkin.Violet, StretchWide = true, Font = UiTheme.GameFont(9.2f, FontStyle.Bold) };
				btnGo.Click += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
				tbl.Controls.Add(btnGo, 0, 5);
				tbl.SetColumnSpan(btnGo, 2);
				dlg.Controls.Add(tbl);

				if (dlg.ShowDialog(this) == DialogResult.OK)
				{
					string obj = cbObj.SelectedIndex == 0 ? "floor" : "avg";
					string rmode = cbRunes.SelectedIndex == 0 ? "bag" : (cbRunes.SelectedIndex == 1 ? "keep" : "all");
					var _ = RunLocalOptimizerAsync(txtOpps.Text.Trim(), obj, rmode, (int)numTarget.Value, chkAutoDeploy.Checked);
				}
			}
		}

		private async Task RunLocalOptimizerAsync(string oppsCsv, string objective, string runeMode, int targetPct, bool autoDeploy)
		{
			if (!BeginTestPvpWork("10核一键寻优")) return;
			try
			{
				string outJson = Path.Combine(PvpSimDir, "exports", "helper_opt_latest.json");
				Directory.CreateDirectory(Path.GetDirectoryName(outJson));
				AddLog(string.Format("开始同步对手 [{0}] 最新阵容与符文…", oppsCsv), BlueColor);
				await RunExternalCommandAsync("node", string.Format("\"{0}\" sync --opps \"{1}\"", Path.Combine(PvpSimDir, "maowa.mjs"), oppsCsv));
				if (testPvpCancel.IsCancellationRequested) { EndTestPvpWork("寻优已停止", YellowColor); return; }

				AddLog(string.Format("启动 10 核并行寻优（目标={0}% · 策略={1} · 符文={2}）…", targetPct, objective, runeMode), BlueColor);
				string optArgs = string.Format("\"{0}\" opt --opps \"{1}\" --objective {2} --runes {3} --target {4} --save helper_opt_latest --out \"{5}\"",
					Path.Combine(PvpSimDir, "maowa.mjs"), oppsCsv, objective, runeMode, targetPct, outJson);
				int code = await RunExternalCommandAsync("node", optArgs);
				if (code != 0 || !File.Exists(outJson)) throw new Exception("寻优进程返回异常退出码 " + code);

				var res = json.DeserializeObject(File.ReadAllText(outJson, Encoding.UTF8)) as Dictionary<string, object>;
				if (res != null && res.ContainsKey("team"))
				{
					loadedFormation = res["team"] as Dictionary<string, object>;
					loadedFormationSource = "一键寻优:" + oppsCsv;
					UpdateLoadedFormationUi();
					AddLog("寻优完成并已载入卡片①：" + FormatLoadedFormationSummary(), GreenColor);
				}
				EndTestPvpWork("寻优完成", GreenColor);
				if (autoDeploy && loadedFormation != null)
				{
					await DeployLoadedFormationAsync();
				}
			}
			catch (Exception ex)
			{
				AddLog("一键寻优失败：" + ex.Message, RedColor);
				EndTestPvpWork("寻优失败", RedColor);
			}
		}

		private Task<int> RunExternalCommandAsync(string exe, string arguments)
		{
			var tcs = new TaskCompletionSource<int>();
			Task.Run(delegate
			{
				try
				{
					var psi = new ProcessStartInfo
					{
						FileName = exe,
						Arguments = arguments,
						WorkingDirectory = PvpSimDir,
						UseShellExecute = false,
						CreateNoWindow = true,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						StandardOutputEncoding = Encoding.UTF8,
						StandardErrorEncoding = Encoding.UTF8
					};
					using (var p = Process.Start(psi))
					{
						p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
						{
							if (!string.IsNullOrEmpty(e.Data))
							{
								string line = e.Data;
								if (!IsDisposed) BeginInvoke((MethodInvoker)delegate { AddLog("  " + line, AutoLogColor); });
							}
						};
						p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
						{
							if (!string.IsNullOrEmpty(e.Data))
							{
								string line = e.Data;
								if (!IsDisposed) BeginInvoke((MethodInvoker)delegate { AddLog("  [err] " + line, YellowColor); });
							}
						};
						p.BeginOutputReadLine();
						p.BeginErrorReadLine();
						while (!p.WaitForExit(150))
						{
							if (testPvpCancel != null && testPvpCancel.IsCancellationRequested)
							{
								try { p.Kill(); } catch { }
								break;
							}
						}
						tcs.TrySetResult(p.ExitCode);
					}
				}
				catch (Exception ex)
				{
					tcs.TrySetException(ex);
				}
			});
			return tcs.Task;
		}

		// =====================================================================
		// Module 6: Snapshot Live Formation F1~F5 -> Export Reusable 一图流 PNG
		// =====================================================================
		private async Task ExportCurrentFormationSnapshotAsync()
		{
			int fid = GetSelectedPvpFid();
			if (!BeginTestPvpWork("导出阵型" + fid + "一图流")) return;
			try
			{
				string lua =
					"local tl = require 'logic.troop'\n" +
					"local rl = require 'logic.rune'\n" +
					"local wnames = { [1]='speed', [2]='defense', [3]='dodge', [4]='authority' }\n" +
					"local slots = {}\n" +
					"for i, m in ipairs(tl:GetFormationMercenaryList(" + fid + ") or {}) do\n" +
					"  local is_l = m.template_id >= 99000001\n" +
					"  slots[i] = { id = m.template_id, name = (m.template_info and m.template_info.name) or '主角', wash = is_l and '' or (wnames[m.ex_prop_type or 0] or '') }\n" +
					"end\n" +
					"local runes = {}\n" +
					"for pos = 1, 5 do local r = (rl:GetRuneEquipmentList() or {})[pos]; if r then runes[pos] = { r.template_id, r.level } end end\n" +
					"return { name = '实机阵型F" + fid + "快照', weapon = tl:GetFormationWeaponId(" + fid + "), runes = runes, slots = slots }";

				var snap = await EvalLuaDecodedAsync(lua) as Dictionary<string, object>;
				if (snap == null || !snap.ContainsKey("slots")) throw new Exception("未读取到阵型数据，请确认已登录进入主界面。");

				string expDir = Path.Combine(PvpSimDir, "exports");
				Directory.CreateDirectory(expDir);
				string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				string tmpJson = Path.Combine(expDir, string.Format("snapshot_F{0}_{1}.json", fid, stamp));
				string outPng = Path.Combine(expDir, string.Format("snapshot_F{0}_{1}.png", fid, stamp));
				File.WriteAllText(tmpJson, json.Serialize(snap), Encoding.UTF8);

				string repPath = Path.Combine(expDir, "helper_live_report_f" + fid + ".json");
				string repArg = File.Exists(repPath) ? string.Format(" --live-report \"{0}\"", repPath) : "";
				string args = string.Format("\"{0}\" export \"{1}\" --mode server30{2} -o \"{3}\"",
					Path.Combine(PvpSimDir, "sim.py"), tmpJson, repArg, outPng);
				int code = await RunExternalCommandAsync("python", args);
				if (code != 0 || !File.Exists(outPng)) throw new Exception("导出 PNG 失败（请检查 Python 环境）");

				AddLog("已导出可复用一图流 PNG（含内嵌 maowa_formation_v1 元数据）：" + outPng, GreenColor);
				EndTestPvpWork("一图流已导出", GreenColor);
				try { Process.Start("explorer.exe", "/select,\"" + outPng + "\""); } catch { }
			}
			catch (Exception ex)
			{
				AddLog("导出快照失败：" + ex.Message, RedColor);
				EndTestPvpWork("导出失败", RedColor);
			}
		}
	}
}
#endif
