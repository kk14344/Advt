using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class WallpaperCropForm : Form
	{
		private sealed class CropCanvas : Control
		{
			private enum DragMode
			{
				None,
				Move,
				Left,
				Right,
				Top,
				Bottom,
				TopLeft,
				TopRight,
				BottomLeft,
				BottomRight
			}

			private readonly Image sourceImage;
			private readonly Size targetWindowSize;
			private RectangleF normRect;
			private bool lockAspect = true;
			private DragMode dragMode = DragMode.None;
			private Point dragStartPt;
			private RectangleF dragStartNorm;

			public event EventHandler CropChanged;

			public float NormX { get { return normRect.X; } }
			public float NormY { get { return normRect.Y; } }
			public float NormW { get { return normRect.Width; } }
			public float NormH { get { return normRect.Height; } }

			public bool LockAspect
			{
				get { return lockAspect; }
				set
				{
					lockAspect = value;
					if (lockAspect) FitToTargetRatio();
					Invalidate();
				}
			}

			public CropCanvas(Image img, int x, int y, int w, int h, Size windowSize)
			{
				sourceImage = img;
				targetWindowSize = (windowSize.Width > 100 && windowSize.Height > 100) ? windowSize : new Size(1440, 940);
				DoubleBuffered = true;
				SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
				float nx = Math.Max(0f, Math.Min(0.95f, x / 10000f));
				float ny = Math.Max(0f, Math.Min(0.95f, y / 10000f));
				float nw = Math.Max(0.05f, Math.Min(1f - nx, w / 10000f));
				float nh = Math.Max(0.05f, Math.Min(1f - ny, h / 10000f));
				normRect = new RectangleF(nx, ny, nw, nh);
			}

			public void ResetFull()
			{
				normRect = new RectangleF(0f, 0f, 1f, 1f);
				Invalidate();
				NotifyCropChanged();
			}

			public void FitToTargetRatio()
			{
				if (sourceImage == null) return;
				float imgRatio = (float)sourceImage.Width / Math.Max(1, sourceImage.Height);
				float winRatio = (float)targetWindowSize.Width / Math.Max(1, targetWindowSize.Height);
				float cx = normRect.X + normRect.Width * 0.5f;
				float cy = normRect.Y + normRect.Height * 0.5f;
				float nw, nh;
				if (imgRatio >= winRatio)
				{
					nh = 1f;
					nw = Math.Max(0.05f, Math.Min(1f, winRatio / imgRatio));
				}
				else
				{
					nw = 1f;
					nh = Math.Max(0.05f, Math.Min(1f, imgRatio / winRatio));
				}
				float nx = Math.Max(0f, Math.Min(1f - nw, cx - nw * 0.5f));
				float ny = Math.Max(0f, Math.Min(1f - nh, cy - nh * 0.5f));
				normRect = new RectangleF(nx, ny, nw, nh);
				Invalidate();
				NotifyCropChanged();
			}

			private RectangleF ImageDisplayRect()
			{
				if (sourceImage == null || Width < 32 || Height < 32) return RectangleF.Empty;
				float pad = 16f;
				float availW = Math.Max(16f, Width - pad * 2f);
				float availH = Math.Max(16f, Height - pad * 2f);
				float sc = Math.Min(availW / sourceImage.Width, availH / sourceImage.Height);
				float dw = sourceImage.Width * sc;
				float dh = sourceImage.Height * sc;
				return new RectangleF((Width - dw) * 0.5f, (Height - dh) * 0.5f, dw, dh);
			}

			private RectangleF SelectionScreenRect(RectangleF imgRect)
			{
				return new RectangleF(
					imgRect.X + normRect.X * imgRect.Width,
					imgRect.Y + normRect.Y * imgRect.Height,
					normRect.Width * imgRect.Width,
					normRect.Height * imgRect.Height);
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				Graphics g = e.Graphics;
				g.Clear(Color.FromArgb(18, 22, 30));
				RectangleF imgRect = ImageDisplayRect();
				if (sourceImage == null || imgRect.Width < 4 || imgRect.Height < 4) return;

				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.DrawImage(sourceImage, imgRect);

				RectangleF sel = SelectionScreenRect(imgRect);
				using (var mask = new SolidBrush(Color.FromArgb(155, 8, 10, 16)))
				{
					g.FillRectangle(mask, imgRect.X, imgRect.Y, imgRect.Width, Math.Max(0f, sel.Y - imgRect.Y));
					g.FillRectangle(mask, imgRect.X, sel.Bottom, imgRect.Width, Math.Max(0f, imgRect.Bottom - sel.Bottom));
					g.FillRectangle(mask, imgRect.X, sel.Y, Math.Max(0f, sel.X - imgRect.X), sel.Height);
					g.FillRectangle(mask, sel.Right, sel.Y, Math.Max(0f, imgRect.Right - sel.Right), sel.Height);
				}

				float sidebarFrac = Math.Max(0.20f, Math.Min(0.55f, 480f / Math.Max(960, targetWindowSize.Width)));
				float splitX = sel.X + sel.Width * sidebarFrac;
				g.SmoothingMode = SmoothingMode.AntiAlias;
				using (var guidePen = new Pen(Color.FromArgb(185, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash })
				{
					g.DrawLine(guidePen, splitX, sel.Y + 2, splitX, sel.Bottom - 2);
					float stageW = sel.Width - (splitX - sel.X);
					float stageH = sel.Height;
					float gH = Math.Max(20f, stageH * 0.84f);
					float gW = Math.Min(stageW * 0.78f, gH * 0.56f);
					float gx = splitX + (stageW - gW) * 0.5f;
					float gy = sel.Y + (stageH - gH) * 0.5f;
					if (gW > 20f && gH > 20f)
						g.DrawRectangle(guidePen, gx, gy, gW, gH);
				}

				using (var tagBg = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
				using (var tagFont = UiTheme.CleanFont(8f, FontStyle.Bold))
				{
					if (splitX - sel.X > 66)
					{
						g.FillRectangle(tagBg, sel.X + 6, sel.Y + 6, 56, 20);
						TextRenderer.DrawText(g, "左侧栏", tagFont, new Rectangle((int)sel.X + 6, (int)sel.Y + 6, 56, 20), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
					}
					if (sel.Right - splitX > 88)
					{
						g.FillRectangle(tagBg, splitX + 6, sel.Y + 6, 80, 20);
						TextRenderer.DrawText(g, "右侧游戏区", tagFont, new Rectangle((int)splitX + 6, (int)sel.Y + 6, 80, 20), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
					}
				}

				using (var frame = new Pen(Color.White, 2f))
					g.DrawRectangle(frame, sel.X, sel.Y, sel.Width, sel.Height);

				using (var hb = new SolidBrush(Color.White))
				using (var hp = new Pen(Color.FromArgb(24, 30, 40), 1f))
				{
					PointF[] handles = {
						new PointF(sel.X, sel.Y),
						new PointF(sel.X + sel.Width * 0.5f, sel.Y),
						new PointF(sel.Right, sel.Y),
						new PointF(sel.Right, sel.Y + sel.Height * 0.5f),
						new PointF(sel.Right, sel.Bottom),
						new PointF(sel.X + sel.Width * 0.5f, sel.Bottom),
						new PointF(sel.X, sel.Bottom),
						new PointF(sel.X, sel.Y + sel.Height * 0.5f)
					};
					for (int i = 0; i < handles.Length; i++)
					{
						RectangleF hr = new RectangleF(handles[i].X - 4f, handles[i].Y - 4f, 8f, 8f);
						g.FillRectangle(hb, hr);
						g.DrawRectangle(hp, hr.X, hr.Y, hr.Width, hr.Height);
					}
				}
			}

			private DragMode HitTest(Point pt, RectangleF sel)
			{
				const float r = 9f;
				bool nearL = Math.Abs(pt.X - sel.X) <= r;
				bool nearR = Math.Abs(pt.X - sel.Right) <= r;
				bool nearT = Math.Abs(pt.Y - sel.Y) <= r;
				bool nearB = Math.Abs(pt.Y - sel.Bottom) <= r;
				bool inX = pt.X >= sel.X - r && pt.X <= sel.Right + r;
				bool inY = pt.Y >= sel.Y - r && pt.Y <= sel.Bottom + r;
				if (nearL && nearT) return DragMode.TopLeft;
				if (nearR && nearT) return DragMode.TopRight;
				if (nearL && nearB) return DragMode.BottomLeft;
				if (nearR && nearB) return DragMode.BottomRight;
				if (nearL && inY) return DragMode.Left;
				if (nearR && inY) return DragMode.Right;
				if (nearT && inX) return DragMode.Top;
				if (nearB && inX) return DragMode.Bottom;
				if (sel.Contains(pt)) return DragMode.Move;
				return DragMode.None;
			}

			protected override void OnMouseDown(MouseEventArgs e)
			{
				if (e.Button == MouseButtons.Left)
				{
					RectangleF imgRect = ImageDisplayRect();
					if (imgRect.Width > 0)
					{
						dragMode = HitTest(e.Location, SelectionScreenRect(imgRect));
						dragStartPt = e.Location;
						dragStartNorm = normRect;
						Capture = true;
					}
				}
				base.OnMouseDown(e);
			}

			protected override void OnMouseMove(MouseEventArgs e)
			{
				RectangleF imgRect = ImageDisplayRect();
				if (imgRect.Width > 4 && imgRect.Height > 4)
				{
					if (dragMode == DragMode.None)
					{
						DragMode hover = HitTest(e.Location, SelectionScreenRect(imgRect));
						switch (hover)
						{
						case DragMode.TopLeft:
						case DragMode.BottomRight: Cursor = Cursors.SizeNWSE; break;
						case DragMode.TopRight:
						case DragMode.BottomLeft: Cursor = Cursors.SizeNESW; break;
						case DragMode.Left:
						case DragMode.Right: Cursor = Cursors.SizeWE; break;
						case DragMode.Top:
						case DragMode.Bottom: Cursor = Cursors.SizeNS; break;
						case DragMode.Move: Cursor = Cursors.SizeAll; break;
						default: Cursor = Cursors.Default; break;
						}
					}
					else
					{
						ApplyDrag(e.Location, imgRect);
					}
				}
				base.OnMouseMove(e);
			}

			private void ApplyDrag(Point pt, RectangleF imgRect)
			{
				float dx = (pt.X - dragStartPt.X) / imgRect.Width;
				float dy = (pt.Y - dragStartPt.Y) / imgRect.Height;
				const float minS = 0.05f;
				RectangleF n = dragStartNorm;

				if (dragMode == DragMode.Move)
				{
					float nx = Math.Max(0f, Math.Min(1f - n.Width, n.X + dx));
					float ny = Math.Max(0f, Math.Min(1f - n.Height, n.Y + dy));
					normRect = new RectangleF(nx, ny, n.Width, n.Height);
				}
				else
				{
					float l = n.X, t = n.Y, r = n.Right, b = n.Bottom;
					if (dragMode == DragMode.Left || dragMode == DragMode.TopLeft || dragMode == DragMode.BottomLeft)
						l = Math.Max(0f, Math.Min(r - minS, n.X + dx));
					if (dragMode == DragMode.Right || dragMode == DragMode.TopRight || dragMode == DragMode.BottomRight)
						r = Math.Min(1f, Math.Max(l + minS, n.Right + dx));
					if (dragMode == DragMode.Top || dragMode == DragMode.TopLeft || dragMode == DragMode.TopRight)
						t = Math.Max(0f, Math.Min(b - minS, n.Y + dy));
					if (dragMode == DragMode.Bottom || dragMode == DragMode.BottomLeft || dragMode == DragMode.BottomRight)
						b = Math.Min(1f, Math.Max(t + minS, n.Bottom + dy));

					float nw = Math.Max(minS, r - l);
					float nh = Math.Max(minS, b - t);

					if (lockAspect && sourceImage != null)
					{
						float imgRatio = (float)sourceImage.Width / Math.Max(1, sourceImage.Height);
						float winRatio = (float)targetWindowSize.Width / Math.Max(1, targetWindowSize.Height);
						float desiredNormRatio = winRatio / imgRatio;
						bool driveByHeight = dragMode == DragMode.Top || dragMode == DragMode.Bottom;
						if (driveByHeight)
							nw = Math.Max(minS, nh * desiredNormRatio);
						else
							nh = Math.Max(minS, nw / desiredNormRatio);

						if (dragMode == DragMode.Left || dragMode == DragMode.TopLeft || dragMode == DragMode.BottomLeft)
							l = r - nw;
						if (dragMode == DragMode.Top || dragMode == DragMode.TopLeft || dragMode == DragMode.TopRight)
							t = b - nh;

						if (l < 0f) { nw += l; l = 0f; nh = nw / desiredNormRatio; if (dragMode == DragMode.Top || dragMode == DragMode.TopLeft || dragMode == DragMode.TopRight) t = b - nh; }
						if (l + nw > 1f) { nw = 1f - l; nh = nw / desiredNormRatio; if (dragMode == DragMode.Top || dragMode == DragMode.TopLeft || dragMode == DragMode.TopRight) t = b - nh; }
						if (t < 0f) { nh += t; t = 0f; nw = nh * desiredNormRatio; if (dragMode == DragMode.Left || dragMode == DragMode.TopLeft || dragMode == DragMode.BottomLeft) l = r - nw; }
						if (t + nh > 1f) { nh = 1f - t; nw = nh * desiredNormRatio; if (dragMode == DragMode.Left || dragMode == DragMode.TopLeft || dragMode == DragMode.BottomLeft) l = r - nw; }
					}
					normRect = new RectangleF(Math.Max(0f, l), Math.Max(0f, t), Math.Max(minS, Math.Min(1f - l, nw)), Math.Max(minS, Math.Min(1f - t, nh)));
				}
				Invalidate();
				NotifyCropChanged();
			}

			protected override void OnMouseUp(MouseEventArgs e)
			{
				dragMode = DragMode.None;
				Capture = false;
				base.OnMouseUp(e);
			}

			private void NotifyCropChanged()
			{
				EventHandler handler = CropChanged;
				if (handler != null) handler(this, EventArgs.Empty);
			}
		}

		private readonly Image loadedImage;
		private readonly CropCanvas canvas;
		private readonly Label lblInfo;
		private readonly Size outputSize;
		private readonly bool fitInside;

		public int CropX { get; private set; }
		public int CropY { get; private set; }
		public int CropW { get; private set; }
		public int CropH { get; private set; }

		public WallpaperCropForm(string imagePath, int cropX, int cropY, int cropW, int cropH, Size targetWindowSize, Image sample = null, bool fitInside = false)
		{
			outputSize = targetWindowSize;
			this.fitInside = fitInside;
			CropX = cropX;
			CropY = cropY;
			CropW = cropW;
			CropH = cropH;

			Text = "壁纸显示区域裁剪（虚线右侧为游戏区，左侧为操作栏）";
			ClientSize = new Size(920, 640);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.Sizable;
			MinimumSize = new Size(760, 520);
			ShowInTaskbar = false;
			BackColor = Color.FromArgb(18, 22, 30);
			ForeColor = Color.White;
			Font = UiTheme.CleanFont(9f);

			if (sample != null) loadedImage = new Bitmap(sample);
			else if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
			{
				byte[] bytes = File.ReadAllBytes(imagePath);
				using (var ms = new MemoryStream(bytes))
				using (var img = Image.FromStream(ms))
				{
					loadedImage = new Bitmap(img);
				}
			}

			canvas = new CropCanvas(loadedImage, cropX, cropY, cropW, cropH, targetWindowSize)
			{
				Dock = DockStyle.Fill
			};

			var footer = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 56,
				BackColor = Color.FromArgb(24, 29, 39),
				Padding = new Padding(14, 10, 14, 10)
			};

			var chkLock = new CheckBox
			{
				Text = "锁定当前助手窗口比例",
				Checked = true,
				AutoSize = true,
				Location = new Point(14, 17),
				ForeColor = Color.FromArgb(235, 240, 248),
				Font = UiTheme.CleanFont(9f)
			};
			chkLock.CheckedChanged += delegate
			{
				canvas.LockAspect = chkLock.Checked;
			};

			var btnFit = new Button
			{
				Text = "适配窗口",
				Location = new Point(206, 12),
				Size = new Size(88, 32),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(46, 54, 70),
				ForeColor = Color.White,
				Cursor = Cursors.Hand
			};
			btnFit.FlatAppearance.BorderColor = Color.FromArgb(92, 106, 130);
			btnFit.Click += delegate { canvas.FitToTargetRatio(); };

			var btnReset = new Button
			{
				Text = "重置全图",
				Location = new Point(302, 12),
				Size = new Size(88, 32),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(46, 54, 70),
				ForeColor = Color.White,
				Cursor = Cursors.Hand
			};
			btnReset.FlatAppearance.BorderColor = Color.FromArgb(92, 106, 130);
			btnReset.Click += delegate
			{
				chkLock.Checked = false;
				canvas.LockAspect = false;
				canvas.ResetFull();
			};

			lblInfo = new Label
			{
				Location = new Point(402, 12),
				Size = new Size(290, 50),
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = Color.FromArgb(196, 208, 226),
				Font = UiTheme.CleanFont(8.5f)
			};

			var btnOk = new Button
			{
				Text = "确定裁剪",
				Anchor = AnchorStyles.Top | AnchorStyles.Right,
				Location = new Point(ClientSize.Width - 208, 12),
				Size = new Size(96, 32),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(62, 122, 198),
				ForeColor = Color.White,
				Cursor = Cursors.Hand
			};
			btnOk.FlatAppearance.BorderSize = 0;
			btnOk.Click += delegate
			{
				SyncCropOutput();
				DialogResult = DialogResult.OK;
				Close();
			};

			var btnCancel = new Button
			{
				Text = "取消",
				Anchor = AnchorStyles.Top | AnchorStyles.Right,
				Location = new Point(ClientSize.Width - 104, 12),
				Size = new Size(90, 32),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(46, 54, 70),
				ForeColor = Color.White,
				DialogResult = DialogResult.Cancel,
				Cursor = Cursors.Hand
			};
			btnCancel.FlatAppearance.BorderColor = Color.FromArgb(92, 106, 130);

			footer.Controls.Add(chkLock);
			footer.Controls.Add(btnFit);
			footer.Controls.Add(btnReset);
			footer.Controls.Add(lblInfo);
			footer.Controls.Add(btnOk);
			footer.Controls.Add(btnCancel);

			AcceptButton = btnOk;
			CancelButton = btnCancel;
			Controls.Add(canvas);
			Controls.Add(footer);

			canvas.CropChanged += delegate { UpdateInfoLabel(); };
			UpdateInfoLabel();
		}

		private void SyncCropOutput()
		{
			int w = Math.Max(500, Math.Min(10000, (int)Math.Round(canvas.NormW * 10000f)));
			int h = Math.Max(500, Math.Min(10000, (int)Math.Round(canvas.NormH * 10000f)));
			int x = Math.Max(0, Math.Min(10000 - w, (int)Math.Round(canvas.NormX * 10000f)));
			int y = Math.Max(0, Math.Min(10000 - h, (int)Math.Round(canvas.NormY * 10000f)));
			CropX = x;
			CropY = y;
			CropW = w;
			CropH = h;
		}

		private void UpdateInfoLabel()
		{
			if (loadedImage == null)
			{
				lblInfo.Text = "未加载到壁纸图像";
				return;
			}
			int pw = Math.Max(1, (int)Math.Round(loadedImage.Width * canvas.NormW));
			int ph = Math.Max(1, (int)Math.Round(loadedImage.Height * canvas.NormH));
			double sx = outputSize.Width / (double)pw;
			double sy = outputSize.Height / (double)ph;
			double scale = fitInside ? Math.Min(sx, sy) : Math.Max(sx, sy);
			lblInfo.Text = "来源 " + loadedImage.Width + "×" + loadedImage.Height + "   裁剪 " + pw + "×" + ph
				+ Environment.NewLine + "窗口 " + outputSize.Width + "×" + outputSize.Height + "   缩放 " + scale.ToString("0.00") + "×";
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && loadedImage != null)
			{
				loadedImage.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
