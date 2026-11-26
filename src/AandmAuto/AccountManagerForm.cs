using System;
using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed class AccountManagerForm:CompositedForm
    {
        private readonly AccountProfiles profiles;
        private readonly ListView list;
        internal readonly System.Collections.Generic.List<string> ScreenProfiles=new System.Collections.Generic.List<string>();
        public AccountManagerForm(AccountProfiles store) {
            bool glass=UiTheme.CurrentTheme==UiTheme.ThemeLiquidGlass;
            profiles=store;Text="账号管理与多开";ClientSize=new Size(690,456);StartPosition=FormStartPosition.CenterParent;
            MinimizeBox=MaximizeBox=false;FormBorderStyle=FormBorderStyle.FixedDialog;
            BackColor=glass?UiTheme.Palette.BgColor:Color.FromArgb(63,51,32);
            ForeColor=glass?LiquidGlassEngine.GlassInk(true):Color.FromArgb(237,226,196);
            Font=UiTheme.GameFont(9f);
            Control host=this;
            if(glass)
            {
                ClientSize=new Size(710,476);
                var bgRoot=new SettingsRootPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=1,Margin=Padding.Empty,Padding=new Padding(10)};
                bgRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f));
                bgRoot.RowStyles.Add(new RowStyle(SizeType.Percent,100f));
                var card=new CardPanel{Dock=DockStyle.Fill,CornerRadius=10,Margin=Padding.Empty,Padding=Padding.Empty};
                var inner=new Panel{Dock=DockStyle.Fill,BackColor=Color.Transparent,Margin=Padding.Empty};
                card.Controls.Add(inner);
                bgRoot.Controls.Add(card,0,0);
                Controls.Add(bgRoot);
                host=inner;
            }
            host.Controls.Add(new SmoothLabel{Text="账号独立保存；勾选最多四个账号可同屏显示、同步下发任务。",Location=new Point(16,12),Size=new Size(658,30),ForeColor=ForeColor,BackColor=Color.Transparent});
            list=new ListView{Location=new Point(16,50),Size=new Size(658,280),View=View.Details,CheckBoxes=true,FullRowSelect=true,MultiSelect=false,HideSelection=false,BackColor=glass?LiquidGlassEngine.GlassInputFill():Color.FromArgb(87,73,42),ForeColor=ForeColor};
            list.Columns.Add("账号",326);list.Columns.Add("窗口",130);list.Columns.Add("自动登录",glass?200:155);host.Controls.Add(list);
            AddButton(host,"新增账号",16,348,Add);AddButton(host,"编辑账号",180,348,Edit);AddButton(host,"打开此账号",344,348,OpenOne);AddButton(host,"批量打开",508,348,OpenChecked);
            var close=new GameButton{Text="关闭",Location=new Point(554,400),Size=new Size(120,40),DialogResult=DialogResult.Cancel,Skin=GameButtonSkin.Stone};host.Controls.Add(close);CancelButton=close;
            Reload();
            AddButton(host,"多账号同屏",16,400,delegate{ScreenProfiles.Clear();foreach(ListViewItem row in list.CheckedItems)ScreenProfiles.Add(((AccountProfile)row.Tag).Id);if(ScreenProfiles.Count<1||ScreenProfiles.Count>MultiAccountForm.MaxAccounts){MessageBox.Show(this,"请勾选 1—"+MultiAccountForm.MaxAccounts+" 个账号。","同屏账号");return;}DialogResult=DialogResult.Yes;Close();});
            GlassControlStyler.ApplyToForm(this);
            if(glass)GlassFormTransition.Attach(this);
        }
        private void AddButton(Control host,string text,int x,int y,EventHandler action){var b=new GameButton{Text=text,Location=new Point(x,y),Size=new Size(150,50),Skin=GameButtonSkin.Gold};b.Click+=action;host.Controls.Add(b);}
        private void Reload(){list.Items.Clear();foreach(var p in profiles.Read()){string user;bool auto=false;try{var c=profiles.ReadCredential(p.Id);user=string.IsNullOrEmpty(c.Username)?p.Name:c.Username;auto=c.AutoLogin;}catch{user=p.Name+"（请重新设置登录）";}var row=new ListViewItem(user){Tag=p};row.SubItems.Add(profiles.IsRunning(p.Id)?(p.Id==AppPaths.ProfileId?"当前窗口":"已打开"):"未打开");row.SubItems.Add(auto?"开启":"关闭");list.Items.Add(row);}}
        private AccountProfile Selected(){return list.SelectedItems.Count==0?null:(AccountProfile)list.SelectedItems[0].Tag;}
        private void Add(object sender,EventArgs args){try{using(var dialog=new AccountSettingsForm("","",true))if(dialog.ShowDialog(this)==DialogResult.OK&&!dialog.Cleared){profiles.Save(null,dialog.AccountUsername,dialog.AccountPassword,dialog.AutoLogin,AppPaths.DataRoot);Reload();}}catch(Exception e){MessageBox.Show(this,e.Message,"账号未保存");}}
        private void Edit(object sender,EventArgs args){var p=Selected();if(p==null)return;if(p.Id==AppPaths.ProfileId){DialogResult=DialogResult.Retry;Close();return;}if(profiles.IsRunning(p.Id)){MessageBox.Show(this,"请先关闭该账号窗口，再修改登录信息。","账号正在运行");return;}try{var c=profiles.ReadCredential(p.Id);using(var dialog=new AccountSettingsForm(c.Username,c.Password,c.AutoLogin))if(dialog.ShowDialog(this)==DialogResult.OK&&!dialog.Cleared){profiles.Save(p.Id,dialog.AccountUsername,dialog.AccountPassword,dialog.AutoLogin,null);Reload();}}catch(Exception e){MessageBox.Show(this,e.Message,"账号未保存");}}
        private void OpenOne(object sender,EventArgs args){var p=Selected();if(p==null)return;try{profiles.Open(p.Id);Reload();}catch(Exception e){MessageBox.Show(this,e.Message,"打开失败");}}
        private void OpenChecked(object sender,EventArgs args){try{foreach(ListViewItem row in list.CheckedItems)profiles.Open(((AccountProfile)row.Tag).Id);Reload();}catch(Exception e){MessageBox.Show(this,e.Message,"打开失败");}}
    }
}
