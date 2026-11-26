using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AandmAuto
{
	// ------------------------------------------------------------
	// 挖矿统计（1.16）
	// 仿游戏「托管挖矿」面板：上方为掉落收益（两列 ×N，顺序与游戏产物清单
	// 一致，净变化显示），随后是「已消耗矿镐 X 矿工包 Y」，下方为挖矿战报
	// （每场战斗一条，最近 20 条/页，上一页/下一页翻页）。
	//
	// 数据按自然日累计，保存在 exe 自己的数据目录
	// （%LOCALAPPDATA%\AandmAuto\mining-stats.json；不可写时退回到 exe 同级的
	//   AandmAutoData）：
	//   { "days": { "2026-11-02": { "picks": 321, "strength": 420, "bp": 1500,
	//                               "res": { "7": 500 }, "spent": { "28": 12 } } },
	//     "names": { "7": "铜矿" } }
	// 战报为本次会话数据，不落盘。Lua 只报告「本次会话」的累计值，C# 按差值
	// 累加到当天；会话号变化或矿镐计数回退即整体归零，不会重复计数。
	// ------------------------------------------------------------
	internal sealed partial class MainForm
	{
		private Label lblMiningStatsTitle;
		private Panel dropGridHost;
		private TableLayoutPanel dropGrid;
		private readonly List<Label> dropGridLabels = new List<Label>();
		private string lastDropKey;
		private Label lblMiningStatsSpend;
		private RichTextBox rtbReport;
		private Label lblReportPage;
		private GameButton btnReportPrev;
		private GameButton btnReportNext;

		private readonly List<string[]> currentReport = new List<string[]>();
		private int reportPage;
		private int reportPageSize = 20;

		private readonly Dictionary<string, Dictionary<string, int>> miningStatsDays =
			new Dictionary<string, Dictionary<string, int>>();
		private readonly Dictionary<string, Dictionary<string, int>> miningStatsRes =
			new Dictionary<string, Dictionary<string, int>>();
		private readonly Dictionary<string, Dictionary<string, int>> miningStatsSpent =
			new Dictionary<string, Dictionary<string, int>>();
		private readonly Dictionary<string, string> miningStatsNames = new Dictionary<string, string>();

		private int lastStatsPicks;
		private int lastStatsStrength;
		private int lastStatsBp;
		private int lastStatsSession = -1;
		private readonly Dictionary<string, int> lastStatsRes = new Dictionary<string, int>();
		private readonly Dictionary<string, int> lastStatsSpent = new Dictionary<string, int>();
		private bool miningStatsLoaded;
		private bool miningStatsDirty;

		// 与游戏「产物」面板一致的顺序（资源 ID）。
		private static readonly string[] StatsProductIds =
		{
			"2", "29", "1", "36", "26", "27", "18", "42", "43", "35", "97", "28", "13", "23"
		};
		private static readonly string[] StatsProductNames =
		{
			"血钻", "矿工包", "金币", "经验", "中级矿石包", "高级矿石包",
			"巨魔雕像", "树木雕像", "染色雕像", "契约石", "闪金灵魂碎片",
			"雷管", "泰坦铁", "地狱硫磺"
		};
		private static readonly string[] StatsKeyIds = { "38", "39", "40" };
		private static readonly string[] StatsKeyNames = { "黏土钥匙", "白银钥匙", "恶魔钥匙" };

		private string MiningStatsPath()
		{
			return Path.Combine(DataRoot(), "mining-stats.json");
		}

		private static string StatsTodayKey()
		{
			// 按本机自然日归档：用户看到的就是自己的「今天」。
			return DateTime.Now.ToString("yyyy-MM-dd");
		}

		// 游戏风格的数字：23M / 3.92K / 28.6K / 1.08K / 171
		private static string FormatGame(long value)
		{
			bool negative = value < 0;
			double v = Math.Abs((double)value);
			string text;
			if (v >= 1e6)
			{
				text = TrimZeros((v / 1e6).ToString("0.##")) + "M";
			}
			else if (v >= 1000)
			{
				text = TrimZeros((v / 1e3).ToString("0.##")) + "K";
			}
			else
			{
				text = ((long)v).ToString();
			}
			return negative ? "-" + text : text;
		}

		private static string TrimZeros(string text)
		{
			if (text.IndexOf('.') < 0) return text;
			return text.TrimEnd('0').TrimEnd('.');
		}

		private Control BuildMiningStatsCard()
		{
			var card = new CardPanel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 0, 0, 6),
				Padding = new Padding(8, 5, 8, 6),
				BackColor = CardColor,
				BorderColor = Color.FromArgb(92, 105, 72),
				CornerRadius = 6
			};
			card.Controls.Add(BuildMiningStatsBody());
			return card;
		}

		private Control BuildMiningStatsBody()
		{
			Color innerBg = Color.FromArgb(68, 57, 33);
			bool glass = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass;
			Color bodyInk = glass ? TextColor : Color.FromArgb(198, 214, 178);
			var layout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				BackColor = Color.Transparent,
				ColumnCount = 1,
				RowCount = 6,
				Margin = Padding.Empty
			};
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, glass ? 38f : 32f));

			lblMiningStatsTitle = new SmoothLabel
			{
				Text = "挖矿统计 · 今日 矿镐 0 次",
				Dock = DockStyle.Fill,
				AutoEllipsis = true,
				BackColor = Color.Transparent,
				ForeColor = GoldColor,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft,
				Margin = Padding.Empty
			};
			hints.SetToolTip(lblMiningStatsTitle,
				"统计自动挖矿期间的矿镐消耗与全部掉落（净变化），并记录每场战斗的战报；按自然日累计保存在本机。");

			layout.Controls.Add(lblMiningStatsTitle, 0, 0);

			// 掉落收益：两列网格
			dropGridHost = glass ? new InsetPanel() : new Panel { BackColor = innerBg };
			dropGridHost.Dock = DockStyle.Fill;
			dropGridHost.AutoScroll = true;
			dropGridHost.Padding = new Padding(6, 2, 4, 2);
			dropGridHost.Margin = new Padding(0, 1, 0, 2);
			dropGrid = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 2,
				RowCount = 18,
				BackColor = Color.Transparent,
				Margin = Padding.Empty,
				AutoSizeMode = AutoSizeMode.GrowAndShrink
			};
			dropGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			dropGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			for (int i = 0; i < 36; i++)
			{
				var cell = new SmoothLabel
				{
					Dock = DockStyle.Fill,
					AutoSize = false,
					Height = 22,
					TextAlign = ContentAlignment.MiddleLeft,
					BackColor = Color.Transparent,
					ForeColor = bodyInk,
					Font = UiTheme.GameFont(7.75f, FontStyle.Regular),
					Visible = false,
					Margin = Padding.Empty,
					AutoEllipsis = true
				};
				dropGridLabels.Add(cell);
				dropGrid.Controls.Add(cell, i % 2, i / 2);
			}
			dropGridHost.Controls.Add(dropGrid);
			layout.Controls.Add(dropGridHost, 0, 1);

			lblMiningStatsSpend = new SmoothLabel
			{
				Text = "已消耗矿镐 0",
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleLeft,
				BackColor = Color.Transparent,
				ForeColor = glass ? GoldColor : Color.FromArgb(232, 189, 88),
				Font = UiTheme.GameFont(7.75f, FontStyle.Bold),
				Margin = Padding.Empty,
				AutoEllipsis = true
			};
			layout.Controls.Add(lblMiningStatsSpend, 0, 2);

			// 战报标题行
			var reportHead = new SmoothLabel
			{
				Text = "挖矿战报",
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleLeft,
				BackColor = Color.Transparent,
				ForeColor = GoldColor,
				Font = UiTheme.GameFont(8f, FontStyle.Bold),
				Margin = Padding.Empty
			};
			layout.Controls.Add(reportHead, 0, 3);

			rtbReport = GlassRichTextBox.Create(innerBg);
			rtbReport.Dock = DockStyle.Fill;
			rtbReport.ReadOnly = true;
			rtbReport.BorderStyle = BorderStyle.None;
			rtbReport.ForeColor = bodyInk;
			rtbReport.Font = UiTheme.GameFont(7.5f, FontStyle.Regular);
			rtbReport.WordWrap = true;
			rtbReport.DetectUrls = false;
			rtbReport.HideSelection = false;
			rtbReport.ScrollBars = RichTextBoxScrollBars.Vertical;
			rtbReport.ShortcutsEnabled = true;
			rtbReport.Margin = new Padding(0, 2, 0, 2);
			rtbReport.Text = "开始自动挖矿后，这里显示每场战斗与掉落。";
			layout.Controls.Add(GlassRichTextBox.WithBackdrop(rtbReport), 0, 4);

			// 翻页行（居中保持 3:1 比例）
			var pager = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 1,
				BackColor = Color.Transparent,
				Margin = Padding.Empty
			};
			pager.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
			pager.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f));
			pager.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
			pager.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			btnReportPrev = new GameButton
			{
				Text = "上一页",
				Size = new Size(84, 28),
				Anchor = AnchorStyles.None,
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(7.5f, FontStyle.Bold),
				Enabled = false
			};
			btnReportPrev.Click += delegate { if (reportPage > 0) { reportPage--; RenderReport(); } };
			btnReportNext = new GameButton
			{
				Text = "下一页",
				Size = new Size(84, 28),
				Anchor = AnchorStyles.None,
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(7.5f, FontStyle.Bold),
				Enabled = false
			};
			btnReportNext.Click += delegate { if (reportPage < TotalReportPages() - 1) { reportPage++; RenderReport(); } };
			lblReportPage = new SmoothLabel
			{
				Text = "0/0",
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleCenter,
				BackColor = Color.Transparent,
				ForeColor = GoldColor,
				Font = UiTheme.GameFont(8f, FontStyle.Bold),
				Margin = Padding.Empty
			};
			pager.Controls.Add(btnReportPrev, 0, 0);
			pager.Controls.Add(lblReportPage, 1, 0);
			pager.Controls.Add(btnReportNext, 2, 0);
			layout.Controls.Add(pager, 0, 5);

			return layout;
		}

		private int TotalReportPages()
		{
			int pages = (currentReport.Count + reportPageSize - 1) / reportPageSize;
			return Math.Max(1, pages);
		}

		private void LoadMiningStats()
		{
			if (miningStatsLoaded) return;
			miningStatsLoaded = true;
			try
			{
				string path = MiningStatsPath();
				if (!File.Exists(path)) { RenderMiningStats(); return; }
				string input = File.ReadAllText(path, Encoding.UTF8);
				Dictionary<string, object> root = json.Deserialize<Dictionary<string, object>>(input);
				if (root == null) { RenderMiningStats(); return; }
				Dictionary<string, object> days = GetDictionary(root, "days");
				if (days != null)
				{
					foreach (KeyValuePair<string, object> pair in days)
					{
						Dictionary<string, object> day = pair.Value as Dictionary<string, object>;
						if (day == null) continue;
						var bucket = new Dictionary<string, int>();
						bucket["picks"] = GetInt(day, "picks");
						bucket["strength"] = GetInt(day, "strength");
						bucket["bp"] = GetInt(day, "bp");
						miningStatsDays[pair.Key] = bucket;
						miningStatsRes[pair.Key] = ReadIntMap(GetDictionary(day, "res"));
						miningStatsSpent[pair.Key] = ReadIntMap(GetDictionary(day, "spent"));
					}
				}
				LoadStringMap(GetDictionary(root, "names"), miningStatsNames);
			}
			catch
			{
			}
			RenderMiningStats();
		}

		private static Dictionary<string, int> ReadIntMap(Dictionary<string, object> source)
		{
			var map = new Dictionary<string, int>();
			if (source == null) return map;
			foreach (KeyValuePair<string, object> item in source)
			{
				try { map[item.Key] = Convert.ToInt32(item.Value); }
				catch { }
			}
			return map;
		}

		private static void LoadStringMap(Dictionary<string, object> source, Dictionary<string, string> target)
		{
			if (source == null) return;
			foreach (KeyValuePair<string, object> item in source)
			{
				if (item.Value != null) target[item.Key] = Convert.ToString(item.Value);
			}
		}

		private static Dictionary<string, object> ToObjectMap(Dictionary<string, string> source)
		{
			var map = new Dictionary<string, object>();
			foreach (KeyValuePair<string, string> pair in source) map[pair.Key] = pair.Value;
			return map;
		}

		private static Dictionary<string, object> ToObjectMap(Dictionary<string, int> source)
		{
			var map = new Dictionary<string, object>();
			foreach (KeyValuePair<string, int> pair in source)
			{
				if (pair.Value != 0) map[pair.Key] = pair.Value;
			}
			return map;
		}

		private void SaveMiningStats()
		{
			try
			{
				var days = new Dictionary<string, object>();
				foreach (KeyValuePair<string, Dictionary<string, int>> pair in miningStatsDays)
				{
					var entry = new Dictionary<string, object>();
					entry["picks"] = BucketValue(pair.Value, "picks");
					entry["strength"] = BucketValue(pair.Value, "strength");
					entry["bp"] = BucketValue(pair.Value, "bp");
					Dictionary<string, int> source;
					entry["res"] = miningStatsRes.TryGetValue(pair.Key, out source)
						? ToObjectMap(source) : new Dictionary<string, object>();
					entry["spent"] = miningStatsSpent.TryGetValue(pair.Key, out source)
						? ToObjectMap(source) : new Dictionary<string, object>();
					days[pair.Key] = entry;
				}
				var root = new Dictionary<string, object>();
				root["days"] = days;
				root["names"] = ToObjectMap(miningStatsNames);
				File.WriteAllText(MiningStatsPath(), json.Serialize(root), Encoding.UTF8);
				miningStatsDirty = false;
			}
			catch
			{
			}
		}

		private static int BucketValue(Dictionary<string, int> bucket, string key)
		{
			int value;
			return bucket != null && bucket.TryGetValue(key, out value) ? value : 0;
		}

		private static int MapValue(Dictionary<string, int> map, string key)
		{
			int value;
			return map != null && map.TryGetValue(key, out value) ? value : 0;
		}

		private Dictionary<string, int> TodayStats()
		{
			string key = StatsTodayKey();
			Dictionary<string, int> bucket;
			if (!miningStatsDays.TryGetValue(key, out bucket))
			{
				bucket = new Dictionary<string, int>();
				bucket["picks"] = 0;
				bucket["strength"] = 0;
				bucket["bp"] = 0;
				miningStatsDays[key] = bucket;
			}
			if (!miningStatsRes.ContainsKey(key)) miningStatsRes[key] = new Dictionary<string, int>();
			if (!miningStatsSpent.ContainsKey(key)) miningStatsSpent[key] = new Dictionary<string, int>();
			return bucket;
		}

		private void ApplyMiningStats(Dictionary<string, object> stats)
		{
			LoadMiningStats();
			if (stats == null) return;
			int session = GetInt(stats, "session");
			int picks = GetInt(stats, "picks");
			int strength = GetInt(stats, "strength");
			int bp = GetInt(stats, "bp");
			bool reset = session != lastStatsSession || picks < lastStatsPicks;
			if (reset)
			{
				// 新的自动挖矿会话（或页面重载）：本次计数从 0 开始。
				lastStatsSession = session;
				lastStatsPicks = 0;
				lastStatsStrength = 0;
				lastStatsBp = 0;
				lastStatsRes.Clear();
				lastStatsSpent.Clear();
			}
			int picksDelta = picks - lastStatsPicks;
			int strengthDelta = strength - lastStatsStrength;
			int bpDelta = bp - lastStatsBp;
			lastStatsPicks = picks;
			lastStatsStrength = strength;
			lastStatsBp = bp;

			var resDelta = new Dictionary<string, int>();
			object[] list = GetArray(stats, "list");
			if (list != null)
			{
				for (int i = 0; i < list.Length; i++)
				{
					Dictionary<string, object> row = list[i] as Dictionary<string, object>;
					if (row == null) continue;
					string id = Convert.ToString(GetInt(row, "id"));
					int amount = GetInt(row, "num");
					string name = GetString(row, "name");
					if (!string.IsNullOrEmpty(name) && !miningStatsNames.ContainsKey(id)) miningStatsNames[id] = name;
					int previous;
					lastStatsRes.TryGetValue(id, out previous);
					if (reset || amount < previous) previous = 0;
					int delta = amount - previous;
					lastStatsRes[id] = amount;
					if (delta > 0) resDelta[id] = delta;
				}
			}

			var spentDelta = new Dictionary<string, int>();
			Dictionary<string, object> spentMap = GetDictionary(stats, "spent");
			if (spentMap != null)
			{
				foreach (KeyValuePair<string, object> item in spentMap)
				{
					int amount;
					try { amount = Convert.ToInt32(item.Value); }
					catch { continue; }
					int previous;
					lastStatsSpent.TryGetValue(item.Key, out previous);
					if (reset || amount < previous) previous = 0;
					int delta = amount - previous;
					lastStatsSpent[item.Key] = amount;
					if (delta > 0) spentDelta[item.Key] = delta;
				}
			}

			// 战报：本次会话数据，整表替换
			object[] reportArr = GetArray(stats, "report");
			if (reportArr != null)
			{
				currentReport.Clear();
				for (int i = 0; i < reportArr.Length; i++)
				{
					Dictionary<string, object> row = reportArr[i] as Dictionary<string, object>;
					if (row == null) continue;
					currentReport.Add(new string[]
					{
						GetString(row, "t"),
						GetString(row, "text"),
						GetString(row, "rewards")
					});
				}
				if (reportPage > TotalReportPages() - 1) reportPage = TotalReportPages() - 1;
			}

			BumpTodayStats(picksDelta, strengthDelta, bpDelta, resDelta, spentDelta);
			if (miningStatsDirty) SaveMiningStats();
			RenderMiningStats();
		}

		private void BumpTodayStats(int picksDelta, int strengthDelta, int bpDelta,
			Dictionary<string, int> resDelta, Dictionary<string, int> spentDelta)
		{
			bool any = picksDelta != 0 || strengthDelta != 0 || bpDelta != 0
				|| (resDelta != null && resDelta.Count > 0)
				|| (spentDelta != null && spentDelta.Count > 0);
			if (!any) return;
			string key = StatsTodayKey();
			Dictionary<string, int> bucket = TodayStats();
			bucket["picks"] = BucketValue(bucket, "picks") + picksDelta;
			bucket["strength"] = BucketValue(bucket, "strength") + strengthDelta;
			bucket["bp"] = BucketValue(bucket, "bp") + bpDelta;
			AccumulateInto(miningStatsRes[key], resDelta);
			AccumulateInto(miningStatsSpent[key], spentDelta);
			miningStatsDirty = true;
		}

		private static void AccumulateInto(Dictionary<string, int> target, Dictionary<string, int> delta)
		{
			if (target == null || delta == null) return;
			foreach (KeyValuePair<string, int> pair in delta)
			{
				if (pair.Value == 0) continue;
				target[pair.Key] = (target.ContainsKey(pair.Key) ? target[pair.Key] : 0) + pair.Value;
			}
		}

		private string ItemName(string id)
		{
			string name;
			if (miningStatsNames.TryGetValue(id, out name) && !string.IsNullOrEmpty(name)) return name;
			return "资源" + id;
		}

		private void RenderMiningStats()
		{
			if (lblMiningStatsTitle == null || lblMiningStatsSpend == null) return;
			string key = StatsTodayKey();
			Dictionary<string, int> bucket = TodayStats();
			Dictionary<string, int> res = miningStatsRes[key];
			Dictionary<string, int> spent = miningStatsSpent[key];
			int picks = BucketValue(bucket, "picks");
			int strength = BucketValue(bucket, "strength");
			int bp = BucketValue(bucket, "bp");

			lblMiningStatsTitle.Text = string.Format("挖矿统计 · 今日 矿镐 {0} · 力量 +{1}",
				FormatGame(picks), FormatGame(strength));

			// 掉落收益网格：产物清单顺序（净变化），随后是钥匙净消耗与其他资源
			var items = new List<string>();
			var listed = new Dictionary<string, bool>();
			for (int i = 0; i < StatsProductIds.Length; i++)
			{
				string id = StatsProductIds[i];
				listed[id] = true;
				long net = (long)MapValue(res, id) - MapValue(spent, id);
				if (net == 0) continue;
				items.Add(StatsProductNames[i] + "  ×" + FormatGame(net));
			}
			if (bp > 0) items.Add("主角战力增加  +" + FormatGame(bp));

			// 钥匙：开箱获得的钥匙抵扣后，正数=净消耗，负数=净获得
			bool anyKey = false;
			for (int i = 0; i < StatsKeyIds.Length; i++)
			{
				string id = StatsKeyIds[i];
				listed[id] = true;
				int gained = MapValue(res, id);
				int consumed = MapValue(spent, id);
				if (gained == 0 && consumed == 0) continue;
				int net = consumed - gained;
				if (!anyKey)
				{
					items.Add("· 钥匙净消耗（开箱抵扣后）");
					anyKey = true;
				}
				string direction = net > 0 ? "净消耗 " : (net < 0 ? "净获得 " : "收支平衡 ");
				items.Add(StatsKeyNames[i] + "  " + direction + FormatGame(Math.Abs(net)));
			}

			var others = new List<KeyValuePair<string, int>>();
			foreach (KeyValuePair<string, int> pair in res)
			{
				if (!listed.ContainsKey(pair.Key) && pair.Value != 0) others.Add(pair);
			}
			others.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
			{
				if (a.Value != b.Value) return b.Value.CompareTo(a.Value);
				return string.CompareOrdinal(a.Key, b.Key);
			});
			foreach (KeyValuePair<string, int> pair in others)
			{
				items.Add(ItemName(pair.Key) + "  ×" + FormatGame(pair.Value));
			}

			var keyBuilder = new StringBuilder();
			for (int i = 0; i < items.Count; i++)
			{
				if (i > 0) keyBuilder.Append('|');
				keyBuilder.Append(items[i]);
			}
			string dropKey = keyBuilder.ToString();
			if (dropKey != lastDropKey)
			{
				lastDropKey = dropKey;
				int index = 0;
				for (int i = 0; i < dropGridLabels.Count && index < items.Count; i++, index++)
				{
					dropGridLabels[i].Text = items[index];
					dropGridLabels[i].Visible = true;
				}
				for (int i = index; i < dropGridLabels.Count; i++) dropGridLabels[i].Visible = false;
				int rows = Math.Max(1, (items.Count + 1) / 2);
				dropGrid.Height = rows * 22;
			}

			lblMiningStatsSpend.Text = string.Format("已消耗矿镐 {0}  矿工包 {1}",
				FormatGame(picks), FormatGame(MapValue(spent, "29")));
			if (strength > 0 || bp > 0)
			{
				lblMiningStatsSpend.Text += string.Format("  力量点数 +{0}  主角战力 +{1}",
					FormatGame(strength), FormatGame(bp));
			}

			RenderReport();
		}

		private void RenderReport()
		{
			if (rtbReport == null || lblReportPage == null) return;
			int pages = TotalReportPages();
			if (reportPage > pages - 1) reportPage = pages - 1;
			if (reportPage < 0) reportPage = 0;
			lblReportPage.Text = (currentReport.Count == 0 ? "0" : (reportPage + 1).ToString())
				+ "/" + (currentReport.Count == 0 ? "0" : pages.ToString());
			if (btnReportPrev != null) btnReportPrev.Enabled = reportPage > 0;
			if (btnReportNext != null) btnReportNext.Enabled = currentReport.Count > 0 && reportPage < pages - 1;
			var builder = new StringBuilder();
			if (currentReport.Count == 0)
			{
				builder.Append("开始自动挖矿后，这里显示每场战斗与掉落。");
			}
			else
			{
				int start = reportPage * reportPageSize;
				int end = Math.Min(currentReport.Count, start + reportPageSize);
				for (int i = start; i < end; i++)
				{
					string[] entry = currentReport[i];
					builder.Append(entry[0]).Append("  ").AppendLine(entry[1]);
					if (!string.IsNullOrEmpty(entry[2])) builder.Append("   ").AppendLine(entry[2]);
				}
			}
			string next = builder.ToString();
			if (rtbReport.Text != next) rtbReport.Text = next;
		}

		// 自检（AANDM_STATS_TEST=1）：用三段合成快照跑一遍增量合并、归零与持久化，
		// 结果写入 stats-selftest.json，便于在没有游戏账号时验证统计板块。
		internal void RunMiningStatsSelfTest()
		{
			var report = new Dictionary<string, object>();
			try
			{
				ResetStatsState();
				const string s1 = "{\"picks\":1080,\"session\":1,\"strength\":420,\"bp\":28600,\"active\":true,\"list\":[" +
					"{\"id\":2,\"name\":\"血钻\",\"num\":3920},{\"id\":1,\"name\":\"金币\",\"num\":23000000}," +
					"{\"id\":35,\"name\":\"契约石\",\"num\":2},{\"id\":7,\"name\":\"铜\",\"num\":520}," +
					"{\"id\":13,\"name\":\"泰坦铁\",\"num\":40},{\"id\":23,\"name\":\"地狱硫磺\",\"num\":105}," +
					"{\"id\":18,\"name\":\"巨魔雕像\",\"num\":171},{\"id\":43,\"name\":\"染色雕像\",\"num\":16}," +
					"{\"id\":36,\"name\":\"经验\",\"num\":11000000},{\"id\":11,\"name\":\"黄金\",\"num\":159}]," +
					"\"spent\":{\"1\":2000,\"38\":3,\"28\":12,\"29\":211}," +
					"\"report\":[{\"t\":\"23:01\",\"text\":\"战胜 巨魔杂兵\",\"rewards\":\"巨魔雕像×1 主角战力×500 染色雕像×2\"}]}";
				const string s2 = "{\"picks\":1090,\"session\":1,\"strength\":430,\"bp\":28600,\"active\":true,\"list\":[" +
					"{\"id\":2,\"name\":\"血钻\",\"num\":3920},{\"id\":1,\"name\":\"金币\",\"num\":23000000}," +
					"{\"id\":35,\"name\":\"契约石\",\"num\":2},{\"id\":7,\"name\":\"铜\",\"num\":600}," +
					"{\"id\":13,\"name\":\"泰坦铁\",\"num\":40},{\"id\":23,\"name\":\"地狱硫磺\",\"num\":105}," +
					"{\"id\":18,\"name\":\"巨魔雕像\",\"num\":171},{\"id\":43,\"name\":\"染色雕像\",\"num\":16}," +
					"{\"id\":36,\"name\":\"经验\",\"num\":11000000},{\"id\":11,\"name\":\"黄金\",\"num\":159}]," +
					"\"spent\":{\"1\":2000,\"38\":3,\"28\":12,\"29\":211}," +
					"\"report\":[{\"t\":\"23:02\",\"text\":\"战胜 魇魔\",\"rewards\":\"地狱硫磺×3 主角战力×300\"}," +
					"{\"t\":\"23:01\",\"text\":\"战胜 巨魔杂兵\",\"rewards\":\"巨魔雕像×1 主角战力×500 染色雕像×2\"}]}";
				const string s3 = "{\"picks\":3,\"session\":2,\"strength\":9,\"bp\":100,\"active\":true,\"list\":[" +
					"{\"id\":35,\"name\":\"契约石\",\"num\":1}],\"spent\":{},\"report\":[]}";
				ApplyMiningStats(json.Deserialize<Dictionary<string, object>>(s1));
				ApplyMiningStats(json.Deserialize<Dictionary<string, object>>(s2));
				report["reportRows"] = currentReport.Count;
				report["text"] = rtbReport != null ? rtbReport.Text : "";
				ApplyMiningStats(json.Deserialize<Dictionary<string, object>>(s3));

				string key = StatsTodayKey();
				Dictionary<string, int> today = TodayStats();
				report["picks"] = BucketValue(today, "picks");
				report["strength"] = BucketValue(today, "strength");
				report["bp"] = BucketValue(today, "bp");
				report["blood"] = MapValue(miningStatsRes[key], "2");
				report["goldNet"] = MapValue(miningStatsRes[key], "1") - MapValue(miningStatsSpent[key], "1");
				report["contract"] = MapValue(miningStatsRes[key], "35");
				report["copper"] = MapValue(miningStatsRes[key], "7");
				report["keyNetConsume"] = MapValue(miningStatsSpent[key], "38") - MapValue(miningStatsRes[key], "38");
				report["tntSpent"] = MapValue(miningStatsSpent[key], "28");
				report["bagsSpent"] = MapValue(miningStatsSpent[key], "29");
				report["finalReportRows"] = currentReport.Count;
				var gridText = new StringBuilder();
				foreach (Label label in dropGridLabels)
				{
					if (label.Visible && label.Text.Length > 0) gridText.AppendLine(label.Text);
				}
				report["grid"] = gridText.ToString();
				report["page"] = lblReportPage != null ? lblReportPage.Text : "";
				report["spend"] = lblMiningStatsSpend != null ? lblMiningStatsSpend.Text : "";
				report["fileExists"] = File.Exists(MiningStatsPath());

				// 重新载入，验证落盘后可恢复
				ResetStatsState();
				LoadMiningStats();
				Dictionary<string, int> again = TodayStats();
				long goldNet = MapValue(miningStatsRes[StatsTodayKey()], "1")
					- MapValue(miningStatsSpent[StatsTodayKey()], "1");
				report["reloadedPicks"] = BucketValue(again, "picks");
				report["reloadedCopper"] = MapValue(miningStatsRes[StatsTodayKey()], "7");
				report["reloadedGoldNet"] = goldNet;
				report["result"] = (BucketValue(again, "picks") == 1093
					&& MapValue(miningStatsRes[StatsTodayKey()], "7") == 600
					&& goldNet == 22998000) ? "passed" : "failed";
			}
			catch (Exception ex)
			{
				report["error"] = ex.ToString();
				report["result"] = "error";
			}
			try
			{
				File.WriteAllText(Path.Combine(DataRoot(), "stats-selftest.json"),
					json.Serialize(report), Encoding.UTF8);
			}
			catch
			{
			}
		}

		private void ResetStatsState()
		{
			miningStatsLoaded = false;
			miningStatsDays.Clear();
			miningStatsRes.Clear();
			miningStatsSpent.Clear();
			miningStatsNames.Clear();
			lastStatsRes.Clear();
			lastStatsSpent.Clear();
			lastStatsSession = -1;
			lastStatsPicks = 0;
			lastStatsStrength = 0;
			lastStatsBp = 0;
			currentReport.Clear();
			reportPage = 0;
			lastDropKey = null;
		}
	}
}
