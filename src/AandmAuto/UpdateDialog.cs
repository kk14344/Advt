using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class UpdateDialog : Form
	{
		private readonly Action<string, Color> logCallback;
		private readonly SmoothLabel lblEdition;
		private readonly SmoothLabel lblStatus;
		private readonly TextBox txtNotes;
		private readonly ProgressBar progressBar;
		private readonly SmoothLabel lblProgress;
		private readonly GameButton btnRecheck;
		private readonly GameButton btnOpenWeb;
		private readonly GameButton btnUpdate;
		private readonly GameButton btnClose;

		private CancellationTokenSource cts;
		private UpdateCheckResult currentResult;
		private bool busy;
		private bool downloading;

		internal UpdateDialog(Action<string, Color> onLog)
		{
			logCallback = onLog;
			Text = "检查更新 · " + BuildEdition.EditionDisplayName;
			ClientSize = new Size(520, 372);
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			StartPosition = FormStartPosition.CenterParent;
			BackColor = UiTheme.Palette.SideOuterColor;
			ForeColor = UiTheme.Palette.TextColor;
			Font = UiTheme.GameFont(9f);

			var headerCard = new InsetPanel
			{
				Location = new Point(14, 12),
				Size = new Size(492, 62),
				Padding = new Padding(10, 6, 10, 6)
			};

			lblEdition = new SmoothLabel
			{
				Text = BuildEdition.EditionDisplayName + "   当前版本：v" + BuildEdition.AppVersion + "（通道：" + BuildEdition.UpdateChannel + "）",
				Location = new Point(12, 8),
				Size = new Size(468, 22),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.Palette.GoldColor,
				Font = UiTheme.GameFont(9f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			};
			lblStatus = new SmoothLabel
			{
				Text = "正在连接更新源检查最新版本…",
				Location = new Point(12, 33),
				Size = new Size(468, 22),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.Palette.TextColor,
				Font = UiTheme.GameFont(8.5f),
				TextAlign = ContentAlignment.MiddleLeft
			};
			headerCard.Controls.Add(lblEdition);
			headerCard.Controls.Add(lblStatus);
			Controls.Add(headerCard);

			var lblNotesTitle = new SmoothLabel
			{
				Text = "版本说明与详情",
				Location = new Point(16, 82),
				Size = new Size(200, 20),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.Palette.MutedColor,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			Controls.Add(lblNotesTitle);

			txtNotes = new TextBox
			{
				Location = new Point(14, 104),
				Size = new Size(492, 162),
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BackColor = UiTheme.Palette.InsetColor,
				ForeColor = UiTheme.Palette.TextColor,
				BorderStyle = BorderStyle.FixedSingle,
				Font = UiTheme.UiFont(9f, FontStyle.Regular),
				Text = "正在检查中，请稍候…"
			};
			Controls.Add(txtNotes);

			progressBar = new ProgressBar
			{
				Location = new Point(14, 276),
				Size = new Size(492, 16),
				Style = ProgressBarStyle.Continuous,
				Minimum = 0,
				Maximum = 100,
				Visible = false
			};
			Controls.Add(progressBar);

			lblProgress = new SmoothLabel
			{
				Text = "",
				Location = new Point(14, 294),
				Size = new Size(492, 20),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.Palette.MutedColor,
				Font = UiTheme.UiFont(8.25f, FontStyle.Regular),
				TextAlign = ContentAlignment.MiddleLeft
			};
			Controls.Add(lblProgress);

			btnRecheck = new GameButton
			{
				Text = "重新检查",
				Location = new Point(14, 322),
				Size = new Size(104, 36),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			btnRecheck.Click += delegate { StartCheck(); };
			Controls.Add(btnRecheck);

			btnOpenWeb = new GameButton
			{
				Text = "网页下载",
				Location = new Point(126, 322),
				Size = new Size(104, 36),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
				Enabled = false
			};
			btnOpenWeb.Click += delegate
			{
				if (currentResult != null && !string.IsNullOrEmpty(currentResult.ReleaseUrl))
				{
					UpdateManager.OpenUrlInBrowser(currentResult.ReleaseUrl);
				}
			};
			Controls.Add(btnOpenWeb);

			btnUpdate = new GameButton
			{
				Text = "立即更新",
				Location = new Point(282, 322),
				Size = new Size(116, 36),
				Skin = GameButtonSkin.Green,
				Font = UiTheme.GameFont(8.75f, FontStyle.Bold),
				Enabled = false
			};
			btnUpdate.Click += delegate
			{
				if (downloading)
				{
					CancelActive();
				}
				else
				{
					StartDownloadAndApply();
				}
			};
			Controls.Add(btnUpdate);

			btnClose = new GameButton
			{
				Text = "关闭",
				Location = new Point(406, 322),
				Size = new Size(100, 36),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold)
			};
			btnClose.Click += delegate { Close(); };
			Controls.Add(btnClose);

			Shown += delegate { StartCheck(); };
			FormClosing += delegate { CancelActive(); };
		}

		private void CancelActive()
		{
			if (cts != null)
			{
				try { cts.Cancel(); } catch { }
			}
		}

		private void StartCheck()
		{
			if (busy) return;
			busy = true;
			downloading = false;
			CancelActive();
			cts = new CancellationTokenSource();
			CancellationToken token = cts.Token;

			btnRecheck.Enabled = false;
			btnUpdate.Enabled = false;
			btnUpdate.Text = "立即更新";
			progressBar.Visible = false;
			lblProgress.Text = "正在检测更新源（直连优先，自动镜像回退）…";
			lblStatus.Text = "正在检查最新版本…";
			lblStatus.ForeColor = UiTheme.Palette.TextColor;
			txtNotes.Text = "正在请求静态更新清单，请稍候…";

			ThreadPool.QueueUserWorkItem(delegate
			{
				UpdateCheckResult result = UpdateManager.CheckForUpdates(token);
				if (IsDisposed || !IsHandleCreated) return;
				try
				{
					BeginInvoke((MethodInvoker)delegate { ApplyCheckResult(result); });
				}
				catch
				{
				}
			});
		}

		private void ApplyCheckResult(UpdateCheckResult result)
		{
			busy = false;
			currentResult = result;
			btnRecheck.Enabled = true;
			btnOpenWeb.Enabled = result != null && !string.IsNullOrEmpty(result.ReleaseUrl);
			lblProgress.Text = "";

			if (result == null)
			{
				lblStatus.Text = "检查更新失败";
				lblStatus.ForeColor = UiTheme.Palette.RedColor;
				txtNotes.Text = "未知错误。";
				return;
			}

			switch (result.State)
			{
			case UpdateCheckState.NotConfigured:
				lblStatus.Text = "尚未绑定公开更新仓库";
				lblStatus.ForeColor = UiTheme.Palette.GoldColor;
				txtNotes.Text = result.ErrorMessage
					+ "\r\n\r\n配置方法：\r\n在源码目录执行：\r\npython tools/configure_repo.py <你的公开仓库URL>\r\n脚本会自动校验仓库可匿名访问、写入 src/update-config.json 并重新编译双端。";
				btnUpdate.Enabled = false;
				break;

			case UpdateCheckState.UpToDate:
				lblStatus.Text = "当前已是最新版本（v" + result.CurrentVersion + "）";
				lblStatus.ForeColor = UiTheme.Palette.GreenColor;
				txtNotes.Text = "当前通道：" + BuildEdition.EditionDisplayName
					+ "\r\n最新版本：v" + result.LatestVersion
					+ (string.IsNullOrEmpty(result.PublishedAt) ? "" : ("\r\n发布时间：" + result.PublishedAt))
					+ (string.IsNullOrEmpty(result.Notes) ? "" : ("\r\n\r\n更新说明：\r\n" + result.Notes));
				btnUpdate.Enabled = false;
				if (logCallback != null) logCallback("检查更新：当前已是最新版本 v" + result.CurrentVersion + "。", UiTheme.Palette.BlueColor);
				break;

			case UpdateCheckState.UpdateAvailable:
				lblStatus.Text = "发现新版本：v" + result.LatestVersion + "（当前 v" + result.CurrentVersion + "）";
				lblStatus.ForeColor = UiTheme.Palette.GreenColor;
				string sizeText = result.Size > 0 ? (Math.Round(result.Size / 1048576.0, 2).ToString("0.00") + " MB") : "未知";
				txtNotes.Text = "可升级版本：v" + result.LatestVersion
					+ "\r\n文件大小：" + sizeText
					+ (string.IsNullOrEmpty(result.PublishedAt) ? "" : ("\r\n发布时间：" + result.PublishedAt))
					+ "\r\n安全校验：SHA-256 强制校验（原地替换，保留全部账号与定时计划）"
					+ "\r\n\r\n更新说明：\r\n" + (string.IsNullOrEmpty(result.Notes) ? "（本次发布未填写详细更新说明）" : result.Notes);
				btnUpdate.Enabled = !string.IsNullOrEmpty(result.DownloadUrl);
				if (logCallback != null) logCallback("发现新版本 v" + result.LatestVersion + "（当前 v" + result.CurrentVersion + "），可在更新窗口选择立即更新。", UiTheme.Palette.GreenColor);
				break;

			default:
				lblStatus.Text = "检查更新失败";
				lblStatus.ForeColor = UiTheme.Palette.RedColor;
				txtNotes.Text = result.ErrorMessage + "\r\n\r\n提示：如果网络环境受限，也可以点击「网页下载」手动获取最新版。";
				btnUpdate.Enabled = false;
				break;
			}
		}

		private void StartDownloadAndApply()
		{
			if (busy || currentResult == null || currentResult.State != UpdateCheckState.UpdateAvailable) return;
			try
			{
				UpdateManager.VerifyWritePermission(Application.ExecutablePath);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "无法原地更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			busy = true;
			downloading = true;
			CancelActive();
			cts = new CancellationTokenSource();
			CancellationToken token = cts.Token;

			btnRecheck.Enabled = false;
			btnClose.Enabled = false;
			btnUpdate.Text = "取消下载";
			btnUpdate.Skin = GameButtonSkin.Orange;
			progressBar.Value = 0;
			progressBar.Visible = true;
			lblStatus.Text = "正在下载新版本 v" + currentResult.LatestVersion + "…";
			lblProgress.Text = "正在连接下载源…";

			UpdateCheckResult info = currentResult;
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					string verifiedPath = UpdateManager.DownloadAndVerify(
						info,
						delegate(int pct, long done, long total, string src)
						{
							if (IsDisposed || !IsHandleCreated) return;
							try
							{
								BeginInvoke((MethodInvoker)delegate
								{
									progressBar.Value = Math.Max(0, Math.Min(100, pct));
									string detail = total > 0
										? string.Format("{0} · {1:0.00} MB / {2:0.00} MB ({3}%)", src, done / 1048576.0, total / 1048576.0, pct)
										: string.Format("{0} · 已下载 {1:0.00} MB", src, done / 1048576.0);
									lblProgress.Text = detail;
								});
							}
							catch
							{
							}
						},
						token);

					if (IsDisposed || !IsHandleCreated) return;
					BeginInvoke((MethodInvoker)delegate
					{
						lblStatus.Text = "校验通过！正在重启并替换新版本…";
						lblProgress.Text = "SHA-256 校验通过，窗口关闭后将自动启动新版。";
						try
						{
							UpdateManager.LaunchSelfReplaceAndRestart(verifiedPath, Application.ExecutablePath);
							if (logCallback != null) logCallback("已下载并校验 v" + info.LatestVersion + "，正在自动替换重启…", UiTheme.Palette.GreenColor);
							DialogResult = DialogResult.OK;
							Close();
							Application.Exit();
						}
						catch (Exception launchEx)
						{
							ResetAfterDownloadError("启动替换程序失败：" + launchEx.Message);
						}
					});
				}
				catch (OperationCanceledException)
				{
					if (IsDisposed || !IsHandleCreated) return;
					try
					{
						BeginInvoke((MethodInvoker)delegate { ResetAfterDownloadError("已取消下载。"); });
					}
					catch
					{
					}
				}
				catch (Exception ex)
				{
					if (IsDisposed || !IsHandleCreated) return;
					try
					{
						BeginInvoke((MethodInvoker)delegate { ResetAfterDownloadError(ex.Message); });
					}
					catch
					{
					}
				}
			});
		}

		private void ResetAfterDownloadError(string message)
		{
			busy = false;
			downloading = false;
			btnRecheck.Enabled = true;
			btnClose.Enabled = true;
			btnUpdate.Text = "立即更新";
			btnUpdate.Skin = GameButtonSkin.Green;
			btnUpdate.Enabled = currentResult != null && currentResult.State == UpdateCheckState.UpdateAvailable;
			progressBar.Visible = false;
			lblStatus.Text = "下载未完成";
			lblStatus.ForeColor = UiTheme.Palette.RedColor;
			lblProgress.Text = message;
		}
	}
}
