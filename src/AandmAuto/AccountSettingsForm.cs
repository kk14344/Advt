using System;
using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class AccountSettingsForm : CompositedForm
	{
		private readonly TextBox txtUsername;

		private readonly TextBox txtPassword;

		private readonly CheckBox chkAutoLogin;

		public string AccountUsername { get; private set; }

		public string AccountPassword { get; private set; }

		public bool AutoLogin { get; private set; }

		public bool Cleared { get; private set; }

		public AccountSettingsForm(string username, string password, bool autoLogin)
		{
			AccountUsername = username ?? string.Empty;
			AccountPassword = password ?? string.Empty;
			AutoLogin = autoLogin;
			Text = "账号设置";
			base.Width = 454;
			base.Height = 308;
			base.StartPosition = FormStartPosition.CenterParent;
			base.FormBorderStyle = FormBorderStyle.FixedDialog;
			base.MaximizeBox = false;
			base.MinimizeBox = false;
			base.ShowInTaskbar = false;
			BackColor = Color.FromArgb(28, 23, 15);
			ForeColor = Color.FromArgb(233, 229, 207);
			Font = UiTheme.GameFont(9f, FontStyle.Regular);
			Label value = new SmoothLabel
			{
				Text = "自动登录账号",
				Location = new Point(18, 14),
				Size = new Size(400, 28),
				Font = UiTheme.GameFont(13f, FontStyle.Bold),
				ForeColor = Color.FromArgb(240, 211, 122),
				BackColor = BackColor
			};
			base.Controls.Add(value);
			Label value2 = new SmoothLabel
			{
				Text = "账号",
				Location = new Point(18, 53),
				Size = new Size(52, 24),
				ForeColor = Color.FromArgb(200, 212, 184),
				BackColor = BackColor
			};
			base.Controls.Add(value2);
			txtUsername = new TextBox();
			txtUsername.Location = new Point(72, 50);
			txtUsername.Size = new Size(352, 26);
			txtUsername.Text = AccountUsername;
			txtUsername.BackColor = Color.FromArgb(15, 12, 8);
			txtUsername.ForeColor = Color.FromArgb(242, 232, 205);
			txtUsername.BorderStyle = BorderStyle.FixedSingle;
			txtUsername.Font = UiTheme.GameFont(10f, FontStyle.Regular);
			base.Controls.Add(txtUsername);
			Label value3 = new SmoothLabel
			{
				Text = "密码",
				Location = new Point(18, 91),
				Size = new Size(52, 24),
				ForeColor = Color.FromArgb(200, 212, 184),
				BackColor = BackColor
			};
			base.Controls.Add(value3);
			txtPassword = new TextBox();
			txtPassword.Location = new Point(72, 88);
			txtPassword.Size = new Size(352, 26);
			txtPassword.Text = AccountPassword;
			txtPassword.UseSystemPasswordChar = true;
			txtPassword.BackColor = Color.FromArgb(15, 12, 8);
			txtPassword.ForeColor = Color.FromArgb(242, 232, 205);
			txtPassword.BorderStyle = BorderStyle.FixedSingle;
			txtPassword.Font = UiTheme.GameFont(10f, FontStyle.Regular);
			base.Controls.Add(txtPassword);
			chkAutoLogin = new GameCheckBox();
			chkAutoLogin.Text = "启动程序时自动登录";
			chkAutoLogin.Location = new Point(72, 124);
			chkAutoLogin.Size = new Size(230, 24);
			chkAutoLogin.Checked = autoLogin;
			chkAutoLogin.ForeColor = Color.FromArgb(225, 215, 187);
			chkAutoLogin.BackColor = BackColor;
			chkAutoLogin.FlatStyle = FlatStyle.Flat;
			base.Controls.Add(chkAutoLogin);
			Label value4 = new SmoothLabel
			{
				Text = "密码使用 Windows DPAPI 加密保存在本机，只有当前 Windows 用户可以解密。\r\n勾选自动登录后，程序启动并进入登录界面时会自动填写账号并登录。",
				Location = new Point(18, 154),
				Size = new Size(406, 48),
				ForeColor = Color.FromArgb(156, 140, 107),
				BackColor = BackColor,
				Font = UiTheme.GameFont(8f, FontStyle.Regular)
			};
			base.Controls.Add(value4);
			FlatButton flatButton = new FlatButton();
			flatButton.Text = "保存";
			flatButton.Location = new Point(190, 215);
			flatButton.Size = new Size(92, 34);
			flatButton.Font = UiTheme.GameFont(9f, FontStyle.Bold);
			flatButton.ForeColor = Color.FromArgb(255, 247, 206);
			flatButton.NormalColor = Color.FromArgb(89, 101, 31);
			flatButton.HoverColor = Color.FromArgb(110, 124, 40);
			flatButton.PressedColor = Color.FromArgb(70, 80, 22);
			flatButton.FlatAppearance.BorderColor = Color.FromArgb(198, 176, 78);
			flatButton.Click += Save_Click;
			base.Controls.Add(flatButton);
			FlatButton flatButton2 = new FlatButton();
			flatButton2.Text = "取消";
			flatButton2.Location = new Point(290, 215);
			flatButton2.Size = new Size(80, 34);
			flatButton2.Font = UiTheme.GameFont(9f, FontStyle.Regular);
			flatButton2.ForeColor = Color.FromArgb(203, 214, 176);
			flatButton2.NormalColor = Color.FromArgb(42, 34, 23);
			flatButton2.HoverColor = Color.FromArgb(58, 47, 30);
			flatButton2.PressedColor = Color.FromArgb(30, 24, 16);
			flatButton2.FlatAppearance.BorderColor = Color.FromArgb(100, 78, 40);
			flatButton2.DialogResult = DialogResult.Cancel;
			base.Controls.Add(flatButton2);
			FlatButton flatButton3 = new FlatButton();
			flatButton3.Text = "清除账号";
			flatButton3.Location = new Point(18, 215);
			flatButton3.Size = new Size(96, 34);
			flatButton3.Font = UiTheme.GameFont(8.5f, FontStyle.Regular);
			flatButton3.ForeColor = Color.FromArgb(224, 150, 130);
			flatButton3.NormalColor = Color.FromArgb(48, 28, 24);
			flatButton3.HoverColor = Color.FromArgb(70, 38, 31);
			flatButton3.PressedColor = Color.FromArgb(35, 20, 17);
			flatButton3.FlatAppearance.BorderColor = Color.FromArgb(110, 58, 47);
			flatButton3.Click += Clear_Click;
			base.Controls.Add(flatButton3);
			base.AcceptButton = flatButton;
			base.CancelButton = flatButton2;
		}

		private void Save_Click(object sender, EventArgs e)
		{
			string text = txtUsername.Text.Trim();
			string text2 = txtPassword.Text;
			bool autoLogin = chkAutoLogin.Checked;
			if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(text2))
			{
				Cleared = true;
				AccountUsername = string.Empty;
				AccountPassword = string.Empty;
				AutoLogin = false;
				base.DialogResult = DialogResult.OK;
				Close();
			}
			else if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(text2))
			{
				MessageBox.Show(this, "请同时填写账号和密码。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			else
			{
				AccountUsername = text;
				AccountPassword = text2;
				AutoLogin = autoLogin;
				Cleared = false;
				base.DialogResult = DialogResult.OK;
				Close();
			}
		}

		private void Clear_Click(object sender, EventArgs e)
		{
			Cleared = true;
			AccountUsername = string.Empty;
			AccountPassword = string.Empty;
			AutoLogin = false;
			base.DialogResult = DialogResult.OK;
			Close();
		}
	}
}
