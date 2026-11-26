using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class WallpaperEnginePickerForm : CompositedForm
	{
		private sealed class ThumbCard : Panel
		{
			public WallpaperEngineItem Item;
			public Image Thumb;
			public bool Selected;
			public bool ThumbLoading;
			public bool ThumbLoaded;

			public ThumbCard(WallpaperEngineItem item)
			{
				Item = item;
				DoubleBuffered = true;
				SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
				Size = new Size(168, 158);
				Margin = new Padding(4);
				Cursor = Cursors.Hand;
				BackColor = UiTheme.Palette.InsetColor;
			}

			protected override void Dispose(bool disposing)
			{
				if (disposing && Thumb != null)
				{
					Thumb.Dispose();
					Thumb = null;
				}
				base.Dispose(disposing);
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				Graphics g = e.Graphics;
				g.SmoothingMode = SmoothingMode.AntiAlias;
				Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
				Color bg = Selected
					? UiTheme.Mix(UiTheme.Palette.InsetColor, UiTheme.Palette.AccentColor, UiTheme.IsLightTheme ? 0.14f : 0.25f)
					: UiTheme.Palette.InsetColor;
				Color border = Selected
					? (UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? UiTheme.Palette.AccentColor : UiTheme.Palette.GoldColor)
					: UiTheme.Palette.BorderColor;
				using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
				using (var pen = new Pen(border, Selected ? 2f : 1f))
					g.DrawRectangle(pen, Selected ? 1 : 0, Selected ? 1 : 0, Selected ? Math.Max(1, r.Width - 1) : r.Width, Selected ? Math.Max(1, r.Height - 1) : r.Height);

				Rectangle imgRect = new Rectangle(6, 6, Width - 12, 98);
				using (var ib = new SolidBrush(UiTheme.IsLightTheme ? Color.FromArgb(228, 224, 214) : Color.FromArgb(30, 24, 15)))
					g.FillRectangle(ib, imgRect);
				if (Thumb != null)
				{
					float scale = Math.Max((float)imgRect.Width / Thumb.Width, (float)imgRect.Height / Thumb.Height);
					int dw = Math.Max(1, (int)Math.Ceiling(Thumb.Width * scale));
					int dh = Math.Max(1, (int)Math.Ceiling(Thumb.Height * scale));
					int dx = imgRect.X + (imgRect.Width - dw) / 2;
					int dy = imgRect.Y + (imgRect.Height - dh) / 2;
					GraphicsState st = g.Save();
					try
					{
						g.SetClip(imgRect, CombineMode.Intersect);
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage(Thumb, new Rectangle(dx, dy, dw, dh));
					}
					finally { g.Restore(st); }
				}
				else
				{
					using (var f = UiTheme.GameFont(7.75f))
						GameText.Draw(g, "加载中…", f, imgRect, UiTheme.Palette.MutedColor, UiSurface.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
				}

				if (Item != null && Item.HasAnimatedPreview)
				{
					Rectangle tagRect = new Rectangle(imgRect.Right - 34, imgRect.Y + 4, 30, 16);
					using (var tb = new SolidBrush(Color.FromArgb(200, 44, 110, 73))) g.FillRectangle(tb, tagRect);
					using (var tf = UiTheme.GameFont(7f, FontStyle.Bold))
						GameText.Draw(g, "GIF", tf, tagRect, Color.White, UiSurface.TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
				}

				Rectangle titleRect = new Rectangle(6, 108, Width - 12, 24);
				Rectangle typeRect = new Rectangle(6, 132, Width - 12, 20);
				TextFormatFlags flags = UiSurface.TextFlags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
				using (var titleFont = UiTheme.GameFont(8.25f, FontStyle.Bold))
				using (var typeFont = UiTheme.GameFont(7.5f))
				{
					GameText.Draw(g, Item != null ? (Item.Title ?? "") : "", titleFont, titleRect, UiTheme.Palette.TextColor, flags);
					string sub = Item != null ? (Item.TypeLabel + (Item.HasAnimatedPreview ? " · 含动图" : "")) : "";
					GameText.Draw(g, sub, typeFont, typeRect, Selected ? UiTheme.Palette.GoldColor : UiTheme.Palette.MutedColor, flags);
				}
			}
		}

		private string customDir;
		private readonly List<WallpaperEngineItem> allItems = new List<WallpaperEngineItem>();
		private readonly List<ThumbCard> cards = new List<ThumbCard>();
		private int gridGeneration;
		private bool watchingDesktop;
		private string watchBaselinePath = "";
		private Timer desktopWatchTimer;

		private TextBox txtSearch;
		private ComboBox cmbType;
		private FlowLayoutPanel gridPanel;
		private GameCheckBox chkApplyDesktop;
		private SmoothLabel lblStatus;
		private GameButton btnApply;

		public WallpaperEngineItem SelectedWallpaper { get; private set; }
		public bool ApplyToDesktop { get { return chkApplyDesktop != null && chkApplyDesktop.Visible && chkApplyDesktop.Checked; } }
		public string ResolvedCustomDir { get { return customDir ?? ""; } }

		public WallpaperEnginePickerForm(string currentWeDir = "", bool allowDesktopApply = true)
		{
			customDir = currentWeDir ?? "";

			Text = "选择壁纸引擎壁纸";
			ClientSize = new Size(960, 680);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			BackColor = UiTheme.Palette.BgColor;
			ForeColor = UiTheme.Palette.TextColor;
			Font = UiTheme.GameFont(9f);
			Shown += delegate { GlassControlStyler.ApplyToForm(this); };
			GlassFormTransition.Attach(this);

			var root = new SettingsRootPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(14, 10, 14, 10),
				Margin = Padding.Empty
			};
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

			root.Controls.Add(new GameHeader { Title = "壁纸引擎 · 壁纸库", Hint = "支持点选创意工坊壁纸，或打开壁纸引擎后自动同步", Dock = DockStyle.Fill }, 0, 0);

			var topCard = new CardPanel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 4, 0, 4),
				Padding = new Padding(10, 4, 10, 4),
				BackColor = UiTheme.Palette.CardColor,
				BorderColor = UiTheme.Palette.BorderColor,
				CornerRadius = 6
			};
			var topBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			topBar.Controls.Add(new SmoothLabel
			{
				Text = "搜索",
				Location = new Point(2, 6),
				Size = new Size(42, 26),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.Palette.TextColor,
				Font = UiTheme.GameFont(8.5f, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleLeft
			});
			txtSearch = new TextBox
			{
				Location = new Point(46, 6),
				Size = new Size(176, 26),
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = UiTheme.Palette.TextColor,
				Font = UiTheme.UiFont(9f)
			};
			txtSearch.TextChanged += delegate { RebuildGrid(); };
			topBar.Controls.Add(txtSearch);

			cmbType = new ComboBox
			{
				Location = new Point(232, 6),
				Size = new Size(92, 26),
				DropDownStyle = ComboBoxStyle.DropDownList,
				BackColor = UiTheme.Palette.InputBackColor,
				ForeColor = UiTheme.Palette.TextColor,
				Font = UiTheme.UiFont(8.5f)
			};
			cmbType.Items.AddRange(new object[] { "全部类型", "视频", "场景", "网页", "图片" });
			cmbType.SelectedIndex = 0;
			cmbType.SelectedIndexChanged += delegate { RebuildGrid(); };
			topBar.Controls.Add(cmbType);

			var btnLaunch = new GameButton
			{
				Text = "打开壁纸引擎",
				Location = new Point(542, 2),
				Size = new Size(108, 36),
				Skin = GameButtonSkin.Gold,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnLaunch.Click += BtnLaunch_Click;
			topBar.Controls.Add(btnLaunch);

			var btnSync = new GameButton
			{
				Text = "同步桌面当前",
				Location = new Point(660, 2),
				Size = new Size(108, 36),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnSync.Click += BtnSync_Click;
			topBar.Controls.Add(btnSync);

			var btnDir = new GameButton
			{
				Text = "指定目录",
				Location = new Point(778, 4),
				Size = new Size(96, 32),
				Skin = GameButtonSkin.Stone,
				Font = UiTheme.GameFont(8.25f, FontStyle.Bold)
			};
			btnDir.Click += BtnPickDir_Click;
			topBar.Controls.Add(btnDir);

			topCard.Controls.Add(topBar);
			root.Controls.Add(topCard, 0, 1);

			var bodyCard = new CardPanel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 2, 0, 4),
				Padding = new Padding(8, 8, 8, 8),
				BackColor = UiTheme.Palette.CardColor,
				BorderColor = UiTheme.Palette.BorderColor,
				CornerRadius = 6
			};
			gridPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoScroll = true,
				WrapContents = true,
				BackColor = Color.Transparent,
				Margin = Padding.Empty
			};
			gridPanel.Scroll += delegate { RequestVisibleThumbnails(); };
			gridPanel.MouseWheel += delegate { RequestVisibleThumbnails(); };
			gridPanel.SizeChanged += delegate { RequestVisibleThumbnails(); };
			bodyCard.Controls.Add(gridPanel);
			root.Controls.Add(bodyCard, 0, 2);

			var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
			chkApplyDesktop = new GameCheckBox
			{
				Text = "同时应用到桌面",
				Location = new Point(2, 8),
				Size = new Size(164, 30),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.Palette.TextColor,
				Font = UiTheme.GameFont(8.75f),
				Checked = false,
				Visible = allowDesktopApply
			};
			footer.Controls.Add(chkApplyDesktop);

			lblStatus = new SmoothLabel
			{
				Text = "正在扫描壁纸引擎库…",
				Location = new Point(allowDesktopApply ? 170 : 2, 8),
				Size = new Size(allowDesktopApply ? 510 : 678, 30),
				BackColor = Color.Transparent,
				ForeColor = UiTheme.Palette.MutedColor,
				Font = UiTheme.GameFont(8.25f),
				TextAlign = ContentAlignment.MiddleLeft,
				AutoEllipsis = true
			};
			footer.Controls.Add(lblStatus);

			var btnCancel = new GameButton
			{
				Text = "取消",
				Location = new Point(814, 5),
				Size = new Size(114, 38),
				Skin = GameButtonSkin.Stone,
				DialogResult = DialogResult.Cancel,
				Font = UiTheme.GameFont(8.75f, FontStyle.Bold)
			};
			footer.Controls.Add(btnCancel);

			btnApply = new GameButton
			{
				Text = "应用壁纸",
				Location = new Point(690, 5),
				Size = new Size(114, 38),
				Skin = GameButtonSkin.Green,
				Enabled = false,
				Font = UiTheme.GameFont(8.75f, FontStyle.Bold)
			};
			btnApply.Click += delegate
			{
				if (SelectedWallpaper == null) return;
				DialogResult = DialogResult.OK;
				Close();
			};
			footer.Controls.Add(btnApply);

			AcceptButton = btnApply;
			CancelButton = btnCancel;
			root.Controls.Add(footer, 0, 3);
			Controls.Add(root);

			desktopWatchTimer = new Timer { Interval = 1000 };
			desktopWatchTimer.Tick += DesktopWatchTimer_Tick;
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			ReloadLibrary();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && desktopWatchTimer != null)
			{
				desktopWatchTimer.Stop();
				desktopWatchTimer.Dispose();
				desktopWatchTimer = null;
			}
			base.Dispose(disposing);
		}

		private void ReloadLibrary()
		{
			allItems.Clear();
			List<WallpaperEngineItem> scanned = WallpaperEngineLocator.ScanWallpapers(customDir);
			allItems.AddRange(scanned);
			RebuildGrid();

			WallpaperEngineItem active = WallpaperEngineLocator.ReadActiveDesktopWallpaper(customDir);
			if (active != null)
			{
				SelectByProjectPath(active.ProjectJsonPath, active);
				if (lblStatus != null) lblStatus.Text = "共 " + allItems.Count + " 个壁纸 · 已识别桌面当前：" + active.Title;
			}
			else if (allItems.Count == 0)
			{
				if (lblStatus != null) lblStatus.Text = "未找到本地壁纸库，可点击「指定目录」或「打开壁纸引擎」";
			}
		}

		private void RebuildGrid()
		{
			gridGeneration++;
			int gen = gridGeneration;
			string kw = txtSearch != null ? (txtSearch.Text ?? "").Trim().ToLowerInvariant() : "";
			string typeFilter = cmbType != null && cmbType.SelectedIndex > 0 ? Convert.ToString(cmbType.SelectedItem) : "";

			var filtered = new List<WallpaperEngineItem>();
			foreach (WallpaperEngineItem item in allItems)
			{
				if (!string.IsNullOrEmpty(typeFilter) && item.TypeLabel != typeFilter) continue;
				if (!string.IsNullOrEmpty(kw))
				{
					string t = (item.Title ?? "").ToLowerInvariant();
					string id = (item.WorkshopId ?? "").ToLowerInvariant();
					if (!t.Contains(kw) && !id.Contains(kw)) continue;
				}
				filtered.Add(item);
			}

			gridPanel.SuspendLayout();
			try
			{
				foreach (ThumbCard old in cards)
				{
					gridPanel.Controls.Remove(old);
					old.Dispose();
				}
				cards.Clear();

				int cap = Math.Min(160, filtered.Count);
				for (int i = 0; i < cap; i++)
				{
					WallpaperEngineItem item = filtered[i];
					var card = new ThumbCard(item);
					card.Selected = SelectedWallpaper != null && string.Equals(SelectedWallpaper.ProjectJsonPath, item.ProjectJsonPath, StringComparison.OrdinalIgnoreCase);
					card.Click += delegate { SelectCard(card); };
					card.DoubleClick += delegate
					{
						SelectCard(card);
						if (SelectedWallpaper != null)
						{
							DialogResult = DialogResult.OK;
							Close();
						}
					};
					cards.Add(card);
					gridPanel.Controls.Add(card);
				}

				if (filtered.Count > cap && lblStatus != null)
					lblStatus.Text = "共匹配 " + filtered.Count + " 个（显示前 " + cap + " 个，请输入关键词筛选）";
				else if (lblStatus != null && !watchingDesktop)
					lblStatus.Text = "共扫描到 " + allItems.Count + " 个壁纸（当前显示 " + cap + " 个）";
			}
			finally
			{
				gridPanel.ResumeLayout(true);
			}

			gridPanel.PerformLayout();
			RequestVisibleThumbnails();
		}

		private void RequestVisibleThumbnails()
		{
			if (IsDisposed || gridPanel == null || gridPanel.IsDisposed || cards.Count == 0) return;
			int gen = gridGeneration;
			int viewW = Math.Max(1, gridPanel.ClientSize.Width);
			int viewH = Math.Max(200, gridPanel.ClientSize.Height);
			Rectangle viewport = new Rectangle(0, -200, viewW, viewH + 400);
			var batch = new List<ThumbCard>();
			foreach (ThumbCard card in cards)
			{
				if (card.IsDisposed || card.ThumbLoaded || card.ThumbLoading) continue;
				if (card.Bounds.Width <= 0 || card.Bounds.IntersectsWith(viewport))
				{
					card.ThumbLoading = true;
					batch.Add(card);
					if (card.Bounds.Width <= 0 && batch.Count >= 24) break;
				}
			}
			if (batch.Count == 0) return;
			Task.Run(delegate { LoadThumbnailsAsync(batch, gen); });
		}

		private void LoadThumbnailsAsync(List<ThumbCard> snapshot, int gen)
		{
			foreach (ThumbCard card in snapshot)
			{
				if (gen != gridGeneration || IsDisposed) return;
				string path = card.Item != null
					? (!string.IsNullOrEmpty(card.Item.PreviewPath) && File.Exists(card.Item.PreviewPath) ? card.Item.PreviewPath : card.Item.BestStaticImagePath)
					: "";
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
				{
					try
					{
						ThumbCard doneCard = card;
						if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(delegate { doneCard.ThumbLoaded = true; doneCard.ThumbLoading = false; }));
					}
					catch { }
					continue;
				}
				Bitmap thumb = null;
				try
				{
					byte[] bytes = File.ReadAllBytes(path);
					using (var ms = new MemoryStream(bytes))
					using (var raw = Image.FromStream(ms))
					{
						int tw = 156;
						int th = 98;
						thumb = new Bitmap(tw, th);
						using (Graphics g = Graphics.FromImage(thumb))
						{
							float sc = Math.Max((float)tw / raw.Width, (float)th / raw.Height);
							int dw = Math.Max(1, (int)Math.Ceiling(raw.Width * sc));
							int dh = Math.Max(1, (int)Math.Ceiling(raw.Height * sc));
							g.InterpolationMode = InterpolationMode.HighQualityBicubic;
							g.DrawImage(raw, (tw - dw) / 2, (th - dh) / 2, dw, dh);
						}
					}
				}
				catch
				{
					if (thumb != null) { thumb.Dispose(); thumb = null; }
				}

				if (thumb == null)
				{
					try
					{
						ThumbCard failCard = card;
						if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(delegate { failCard.ThumbLoaded = true; failCard.ThumbLoading = false; }));
					}
					catch { }
					continue;
				}
				if (gen != gridGeneration || IsDisposed || !IsHandleCreated)
				{
					thumb.Dispose();
					return;
				}
				Bitmap delivered = thumb;
				ThumbCard target = card;
				try
				{
					BeginInvoke(new Action(delegate
					{
						if (gen != gridGeneration || IsDisposed || target.IsDisposed)
						{
							delivered.Dispose();
							return;
						}
						target.Thumb = delivered;
						target.ThumbLoaded = true;
						target.ThumbLoading = false;
						target.Invalidate();
					}));
				}
				catch
				{
					delivered.Dispose();
				}
			}
		}

		private void SelectCard(ThumbCard target)
		{
			SelectedWallpaper = target != null ? target.Item : null;
			foreach (ThumbCard c in cards)
			{
				bool sel = (c == target);
				if (c.Selected != sel)
				{
					c.Selected = sel;
					c.Invalidate();
				}
			}
			if (btnApply != null) btnApply.Enabled = (SelectedWallpaper != null);
			if (SelectedWallpaper != null && lblStatus != null)
			{
				string source = SelectedWallpaper.PlaybackSource;
				lblStatus.Text = "已选中：" + SelectedWallpaper.Title + "（" + SelectedWallpaper.TypeLabel + "）"
					+ (string.IsNullOrEmpty(source) ? "" : " · 独立播放 " + Path.GetFileName(source));
			}
		}

		private void SelectByProjectPath(string projectPath, WallpaperEngineItem fallbackItem)
		{
			if (string.IsNullOrEmpty(projectPath)) return;
			bool exists = false;
			foreach (WallpaperEngineItem it in allItems)
			{
				if (string.Equals(it.ProjectJsonPath, projectPath, StringComparison.OrdinalIgnoreCase))
				{
					exists = true;
					break;
				}
			}
			if (!exists && fallbackItem != null)
			{
				allItems.Insert(0, fallbackItem);
				SelectedWallpaper = fallbackItem;
				RebuildGrid();
			}
			foreach (ThumbCard card in cards)
			{
				if (card.Item != null && string.Equals(card.Item.ProjectJsonPath, projectPath, StringComparison.OrdinalIgnoreCase))
				{
					SelectCard(card);
					try { gridPanel.ScrollControlIntoView(card); }
					catch { }
					return;
				}
			}
			if (fallbackItem != null)
			{
				SelectedWallpaper = fallbackItem;
				if (btnApply != null) btnApply.Enabled = true;
			}
		}

		private void BtnLaunch_Click(object sender, EventArgs e)
		{
			WallpaperEngineItem before = WallpaperEngineLocator.ReadActiveDesktopWallpaper(customDir);
			watchBaselinePath = before != null ? (before.ProjectJsonPath ?? "") : "";
			bool launched = WallpaperEngineLocator.LaunchWallpaperEngine(customDir);
			watchingDesktop = launched;
			if (launched)
			{
				if (lblStatus != null) lblStatus.Text = "已打开壁纸引擎：在壁纸引擎里切换壁纸后，助手将自动识别并选中（也可点「同步桌面当前」）";
				desktopWatchTimer.Start();
			}
			else
			{
				MessageBox.Show(this, "未自动检测到壁纸引擎安装路径，请点击「指定目录」选择 wallpaper_engine 所在文件夹。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void BtnSync_Click(object sender, EventArgs e)
		{
			WallpaperEngineItem active = WallpaperEngineLocator.ReadActiveDesktopWallpaper(customDir);
			if (active == null)
			{
				MessageBox.Show(this, "暂未读取到壁纸引擎当前桌面壁纸。请确认已安装壁纸引擎，或先点击「打开壁纸引擎」选一张壁纸。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			watchingDesktop = false;
			desktopWatchTimer.Stop();
			SelectByProjectPath(active.ProjectJsonPath, active);
			if (lblStatus != null) lblStatus.Text = "已同步桌面当前壁纸：" + active.Title + "（点「应用壁纸」生效）";
		}

		private void DesktopWatchTimer_Tick(object sender, EventArgs e)
		{
			if (!watchingDesktop || IsDisposed) return;
			WallpaperEngineItem active = WallpaperEngineLocator.ReadActiveDesktopWallpaper(customDir);
			if (active == null || string.IsNullOrEmpty(active.ProjectJsonPath)) return;
			if (!string.Equals(active.ProjectJsonPath, watchBaselinePath, StringComparison.OrdinalIgnoreCase))
			{
				watchBaselinePath = active.ProjectJsonPath;
				watchingDesktop = false;
				desktopWatchTimer.Stop();
				SelectByProjectPath(active.ProjectJsonPath, active);
				if (lblStatus != null) lblStatus.Text = "已检测到壁纸引擎切换：" + active.Title + "（点「应用壁纸」或双击卡片即可生效）";
			}
		}

		private void BtnPickDir_Click(object sender, EventArgs e)
		{
			using (var dlg = new FolderBrowserDialog())
			{
				dlg.Description = "请选择 Wallpaper Engine 安装目录（wallpaper_engine）或创意工坊 431960 目录";
				if (!string.IsNullOrEmpty(customDir) && Directory.Exists(customDir)) dlg.SelectedPath = customDir;
				if (dlg.ShowDialog(this) != DialogResult.OK) return;
				customDir = dlg.SelectedPath;
				ReloadLibrary();
			}
		}
	}
}
