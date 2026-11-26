using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class PresetLoadResult
	{
		public string RawJson = "";
		public string SourceLabel = "";
		public int PresetCount;
	}

	/// <summary>
	/// 测试服 PvP 扩展能力：
	/// 1. 预设阵型库在线热同步（支持开发目录 -> 云端热更缓存 presets-synced.json -> 内嵌离线资源三级回退，走直连+镜像）；
	/// 2. 多对手批量实测胜率矩阵汇总窗 + 一键复制 Markdown 战报。
	/// </summary>
	internal static class TestPvpExtras
	{
		internal static string SyncedPresetsPath()
		{
			return Path.Combine(AppPaths.DataRoot, "presets-synced.json");
		}

		internal static PresetLoadResult LoadPresetsWithFallback(string devSimDir)
		{
			var js = new JavaScriptSerializer();
			string devPath = string.IsNullOrEmpty(devSimDir) ? "" : Path.Combine(devSimDir, "presets.json");
			string syncedPath = SyncedPresetsPath();

			if (!string.IsNullOrEmpty(devPath) && File.Exists(devPath))
			{
				string raw = File.ReadAllText(devPath, Encoding.UTF8);
				int cnt = CountFormations(js, raw);
				if (cnt > 0) return new PresetLoadResult { RawJson = raw, SourceLabel = "本地开发目录 presets.json（" + cnt + " 套）", PresetCount = cnt };
			}

			if (File.Exists(syncedPath))
			{
				try
				{
					string raw = File.ReadAllText(syncedPath, Encoding.UTF8);
					int cnt = CountFormations(js, raw);
					if (cnt > 0) return new PresetLoadResult { RawJson = raw, SourceLabel = "云端热更预设库（" + cnt + " 套）", PresetCount = cnt };
				}
				catch
				{
				}
			}

			using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.presets.json"))
			{
				if (s != null)
				{
					using (var r = new StreamReader(s, Encoding.UTF8))
					{
						string raw = r.ReadToEnd();
						int cnt = CountFormations(js, raw);
						return new PresetLoadResult { RawJson = raw, SourceLabel = "内置离线预设库（" + cnt + " 套）", PresetCount = cnt };
					}
				}
			}
			return new PresetLoadResult();
		}

		internal static List<string> BuildPresetSyncUrls()
		{
			var urls = new List<string>();
			string envUrl = Environment.GetEnvironmentVariable("AANDM_PRESETS_SYNC_URL");
			if (!string.IsNullOrEmpty(envUrl))
			{
				urls.Add(envUrl.Trim());
				return urls;
			}

			UpdateConfig cfg = UpdateManager.LoadConfig();
			string repo = (cfg.RepoUrl ?? "").Trim().TrimEnd('/');
			string rawUrl = "";
			if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
			{
				string ownerRepo = repo.Substring("https://github.com/".Length);
				rawUrl = "https://raw.githubusercontent.com/" + ownerRepo + "/main/pvp-simulator/presets.json";
			}
			else if (repo.StartsWith("https://gitee.com/", StringComparison.OrdinalIgnoreCase)
				|| repo.StartsWith("https://gitcode.com/", StringComparison.OrdinalIgnoreCase))
			{
				rawUrl = repo + "/raw/main/pvp-simulator/presets.json";
			}
			if (string.IsNullOrEmpty(rawUrl)) return urls;
			return UpdateManager.BuildCandidateUrls(rawUrl, cfg.DownloadMirrors);
		}

		internal static PresetLoadResult SyncPresetsOnline(CancellationToken cancel)
		{
			try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls; } catch { }
			List<string> candidates = BuildPresetSyncUrls();
			if (candidates.Count == 0)
			{
				throw new InvalidOperationException("当前尚未配置公开更新仓库地址，无法在线同步预设库。");
			}

			var js = new JavaScriptSerializer();
			string lastErr = "所有线路均连接失败";
			for (int i = 0; i < candidates.Count; i++)
			{
				cancel.ThrowIfCancellationRequested();
				string url = candidates[i];
				try
				{
					var req = (HttpWebRequest)WebRequest.Create(url);
					req.Method = "GET";
					req.UserAgent = "AandmAuto/" + BuildEdition.AppVersion;
					req.Timeout = 10000;
					req.ReadWriteTimeout = 15000;
					req.AllowAutoRedirect = true;
					req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
					using (cancel.Register(delegate { try { req.Abort(); } catch { } }))
					using (var resp = (HttpWebResponse)req.GetResponse())
					using (Stream stream = resp.GetResponseStream())
					using (var reader = new StreamReader(stream, Encoding.UTF8))
					{
						string body = reader.ReadToEnd();
						int cnt = CountFormations(js, body);
						if (cnt <= 0)
						{
							lastErr = "下载内容不是有效的预设库 JSON";
							continue;
						}
						string dst = SyncedPresetsPath();
						string tmp = dst + ".tmp";
						Directory.CreateDirectory(Path.GetDirectoryName(dst));
						File.WriteAllText(tmp, body, Encoding.UTF8);
						if (File.Exists(dst)) File.Delete(dst);
						File.Move(tmp, dst);
						return new PresetLoadResult
						{
							RawJson = body,
							SourceLabel = "云端热更预设库（" + cnt + " 套 · 刚同步）",
							PresetCount = cnt
						};
					}
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					lastErr = ex.Message;
				}
			}
			throw new InvalidOperationException("在线同步预设库失败：" + lastErr);
		}

		private static int CountFormations(JavaScriptSerializer js, string rawJson)
		{
			if (string.IsNullOrEmpty(rawJson)) return 0;
			try
			{
				var map = js.DeserializeObject(rawJson) as Dictionary<string, object>;
				if (map == null) return 0;
				int cnt = 0;
				foreach (var kv in map)
				{
					if (kv.Key.StartsWith("friend_", StringComparison.OrdinalIgnoreCase)) continue;
					var d = kv.Value as Dictionary<string, object>;
					if (d != null && d.ContainsKey("slots")) cnt++;
				}
				return cnt;
			}
			catch
			{
				return 0;
			}
		}

		internal static string FormatBattleMatrixMarkdown(int fid, int targetPct, double minWr, double avgWr, double totalSec, List<Dictionary<string, object>> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine(string.Format("### 阵型 F{0} 实机批量切磋战报（在途=1 · 达标线 {1}%）", fid, targetPct));
			sb.AppendLine(string.Format("- **最低保底**：{0:0.00}% ｜ **平均胜率**：{1:0.00}% ｜ **总耗时**：{2:0.2}s", minWr, avgWr, totalSec));
			sb.AppendLine();
			sb.AppendLine("| 对手 | 编号 | 胜 / 负 | 胜率 | 耗时 | 达标 |");
			sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: |");
			if (rows != null)
			{
				foreach (var r in rows)
				{
					string label = r.ContainsKey("label") ? Convert.ToString(r["label"]) : "";
					string uid = r.ContainsKey("uid") ? Convert.ToString(r["uid"]) : "";
					int w = r.ContainsKey("wins") ? Convert.ToInt32(r["wins"]) : 0;
					int l = r.ContainsKey("losses") ? Convert.ToInt32(r["losses"]) : 0;
					double wr = r.ContainsKey("winrate_pct") ? Convert.ToDouble(r["winrate_pct"]) : 0.0;
					double sec = r.ContainsKey("sec") ? Convert.ToDouble(r["sec"]) : 0.0;
					bool pass = r.ContainsKey("pass") && Convert.ToBoolean(r["pass"]);
					sb.AppendLine(string.Format("| {0} | `{1}` | {2} / {3} | **{4:0.00}%** | {5:0.2}s | {6} |",
						label, uid, w, l, wr, sec, pass ? "✅" : "⚠️"));
				}
			}
			return sb.ToString();
		}

		internal static void ShowBattleMatrixDialog(IWin32Window owner, int fid, int targetPct, double minWr, double avgWr, double totalSec, List<Dictionary<string, object>> rows, Action onExportSnapshot = null)
		{
			if (rows == null || rows.Count == 0) return;
			string md = FormatBattleMatrixMarkdown(fid, targetPct, minWr, avgWr, totalSec, rows);

			using (var dlg = new Form
			{
				Text = string.Format("批量对手胜率矩阵 · F{0}（保底 {1:0.1}% / 均胜 {2:0.1}%）", fid, minWr, avgWr),
				ClientSize = new Size(640, 420),
				StartPosition = FormStartPosition.CenterParent,
				BackColor = UiTheme.Palette.SideOuterColor,
				ForeColor = UiTheme.Palette.TextColor,
				Font = UiTheme.GameFont(9f),
				MinimizeBox = false,
				MaximizeBox = false,
				FormBorderStyle = FormBorderStyle.FixedDialog
			})
			{
				var banner = new InsetPanel
				{
					Location = new Point(12, 10),
					Size = new Size(616, 46),
					Padding = new Padding(10, 6, 10, 6)
				};
				banner.Controls.Add(new SmoothLabel
				{
					Text = string.Format("出战阵型 F{0}  ·  对手 {1} 位  ·  保底胜率 {2:0.2}%  ·  平均胜率 {3:0.2}%  ·  总耗时 {4:0.2}s",
						fid, rows.Count, minWr, avgWr, totalSec),
					Dock = DockStyle.Fill,
					BackColor = Color.Transparent,
					ForeColor = minWr >= targetPct ? UiTheme.Palette.GreenColor : UiTheme.Palette.GoldColor,
					Font = UiTheme.GameFont(9f, FontStyle.Bold),
					TextAlign = ContentAlignment.MiddleLeft
				});
				dlg.Controls.Add(banner);

				var grid = new DataGridView
				{
					Location = new Point(12, 64),
					Size = new Size(616, 294),
					ReadOnly = true,
					AllowUserToAddRows = false,
					AllowUserToDeleteRows = false,
					AllowUserToResizeRows = false,
					RowHeadersVisible = false,
					SelectionMode = DataGridViewSelectionMode.FullRowSelect,
					AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
					BackgroundColor = UiTheme.Palette.InsetColor,
					GridColor = UiTheme.Palette.LineColor,
					BorderStyle = BorderStyle.FixedSingle,
					EnableHeadersVisualStyles = false
				};
				grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Palette.CardColor;
				grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Palette.GoldColor;
				grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.GameFont(8.5f, FontStyle.Bold);
				grid.DefaultCellStyle.BackColor = UiTheme.Palette.InsetColor;
				grid.DefaultCellStyle.ForeColor = UiTheme.Palette.TextColor;
				grid.DefaultCellStyle.SelectionBackColor = UiTheme.Palette.AccentColor;
				grid.DefaultCellStyle.SelectionForeColor = UiTheme.Palette.TextColor;
				grid.DefaultCellStyle.Font = UiTheme.UiFont(9f, FontStyle.Regular);

				grid.Columns.Add("label", "对手");
				grid.Columns.Add("uid", "编号");
				grid.Columns.Add("wl", "胜 / 负");
				grid.Columns.Add("wr", "胜率");
				grid.Columns.Add("sec", "耗时");
				grid.Columns.Add("pass", "结论");
				grid.Columns["label"].FillWeight = 24;
				grid.Columns["uid"].FillWeight = 16;
				grid.Columns["wl"].FillWeight = 18;
				grid.Columns["wr"].FillWeight = 16;
				grid.Columns["sec"].FillWeight = 14;
				grid.Columns["pass"].FillWeight = 12;

				foreach (var r in rows)
				{
					int w = r.ContainsKey("wins") ? Convert.ToInt32(r["wins"]) : 0;
					int l = r.ContainsKey("losses") ? Convert.ToInt32(r["losses"]) : 0;
					double wr = r.ContainsKey("winrate_pct") ? Convert.ToDouble(r["winrate_pct"]) : 0.0;
					double sec = r.ContainsKey("sec") ? Convert.ToDouble(r["sec"]) : 0.0;
					bool pass = r.ContainsKey("pass") && Convert.ToBoolean(r["pass"]);
					int idx = grid.Rows.Add(
						r.ContainsKey("label") ? Convert.ToString(r["label"]) : "",
						r.ContainsKey("uid") ? Convert.ToString(r["uid"]) : "",
						w + " / " + l,
						wr.ToString("0.00") + "%",
						sec.ToString("0.2") + "s",
						pass ? "达标" : "未达"
					);
					grid.Rows[idx].Cells["wr"].Style.ForeColor = pass ? UiTheme.Palette.GreenColor : UiTheme.Palette.YellowColor;
					grid.Rows[idx].Cells["pass"].Style.ForeColor = pass ? UiTheme.Palette.GreenColor : UiTheme.Palette.YellowColor;
				}
				dlg.Controls.Add(grid);

				var btnCopy = new GameButton
				{
					Text = "复制 Markdown 战报",
					Location = new Point(12, 368),
					Size = new Size(168, 38),
					Skin = GameButtonSkin.Gold,
					Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
				};
				btnCopy.Click += delegate
				{
					try
					{
						Clipboard.SetText(md);
						btnCopy.Text = "已复制到剪贴板 ✓";
					}
					catch
					{
					}
				};
				dlg.Controls.Add(btnCopy);
				if (onExportSnapshot != null)
				{
					var btnSnap = new GameButton
					{
						Text = "带战报导出一图流 PNG",
						Location = new Point(190, 368),
						Size = new Size(192, 38),
						Skin = GameButtonSkin.Green,
						Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
					};
					btnSnap.Click += delegate
					{
						dlg.Close();
						onExportSnapshot();
					};
					dlg.Controls.Add(btnSnap);
				}
				var btnClose = new GameButton
				{
					Text = "关闭",
					Location = new Point(518, 368),
					Size = new Size(110, 38),
					Skin = GameButtonSkin.Stone,
					Font = UiTheme.GameFont(8.75f, FontStyle.Bold)
				};
				btnClose.Click += delegate { dlg.Close(); };

				dlg.Controls.Add(btnClose);
				dlg.ShowDialog(owner);
			}
		}

		internal static void RunSmokeSelfTest(string outDir)
		{
			Directory.CreateDirectory(outDir);
			var report = new Dictionary<string, object>();
			report["channel"] = BuildEdition.UpdateChannel;

			// Test Markdown matrix formatting
			var rows = new List<Dictionary<string, object>>
			{
				new Dictionary<string, object> { { "label", "测试15" }, { "uid", "3YPN0" }, { "wins", 72 }, { "losses", 8 }, { "winrate_pct", 90.0 }, { "sec", 2.85 }, { "pass", true } },
				new Dictionary<string, object> { { "label", "测试02" }, { "uid", "73ID6" }, { "wins", 64 }, { "losses", 16 }, { "winrate_pct", 80.0 }, { "sec", 2.91 }, { "pass", true } }
			};
			string md = FormatBattleMatrixMarkdown(5, 70, 80.0, 85.0, 5.76, rows);
			bool mdOk = md.Contains("测试15") && md.Contains("90.00%") && md.Contains("80.00%") && md.Contains("F5");
			report["matrixMarkdownOk"] = mdOk;

			// Test preset sync if AANDM_PRESETS_SYNC_URL is set
			bool presetSyncOk = true;
			string envUrl = Environment.GetEnvironmentVariable("AANDM_PRESETS_SYNC_URL");
			if (!string.IsNullOrEmpty(envUrl))
			{
				PresetLoadResult synced = SyncPresetsOnline(CancellationToken.None);
				PresetLoadResult loaded = LoadPresetsWithFallback("");
				presetSyncOk = synced.PresetCount >= 1 && loaded.PresetCount == synced.PresetCount && File.Exists(SyncedPresetsPath());
				report["syncedCount"] = synced.PresetCount;
			}
			report["presetSyncOk"] = presetSyncOk;
			report["passed"] = mdOk && presetSyncOk;

			var js = new JavaScriptSerializer();
			File.WriteAllText(Path.Combine(outDir, "p2-extras-smoke-" + BuildEdition.UpdateChannel + ".json"), js.Serialize(report), Encoding.UTF8);
		}
	}
}
