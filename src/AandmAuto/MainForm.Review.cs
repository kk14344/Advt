using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private sealed class ReviewRow
        {
            public string key {get;set;}
            public string name {get;set;}
            public string status {get;set;}
            public string detail {get;set;}
            public bool retry {get;set;}
        }
        private sealed class DailyReview
        {
            public string account {get;set;}
            public int day {get;set;}
            public List<ReviewRow> rows {get;set;}
        }
        private sealed class TaskReview
        {
            public DailyReview Daily {get;set;}
            public List<string> History {get;set;}
            public TaskReview(){History=new List<string>();}
        }

        private TaskReview review;
        private Form runePrompt;
        private string waitOperation="";
        private DateTime waitStartedUtc;
        private Label mineRouteLabel;
        private string trayStatus="";
        private DateTime nextTrayUpdateUtc;

        private TaskReview ReadReview()
        {
            if(review!=null)return review;
            try{
                var path=Path.Combine(DataRoot(),"task-review.json");
                if(File.Exists(path))review=json.Deserialize<TaskReview>(File.ReadAllText(path));
            }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
            if(review==null)review=new TaskReview();
            if(review.History==null)review.History=new List<string>();
            return review;
        }

        private void SaveReview()
        {
            if(previewMode)return;
            try{
                string path=Path.Combine(DataRoot(),"task-review.json"),temp=path+".tmp";
                File.WriteAllText(temp,json.Serialize(review));
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }catch(IOException ex){AddLog("任务记录保存失败 · "+ex.Message,YellowColor);}
        }

        private void RecordTaskReview(string name,string details)
        {
            if(string.IsNullOrEmpty(details))return;
            var data=ReadReview();
            data.History.Insert(0,DateTime.Now.ToString("MM/dd HH:mm")+" · "+name+" · "+details);
            if(data.History.Count>40)data.History.RemoveRange(40,data.History.Count-40);
            SaveReview();
        }

        private void RecordDailyReview(object raw,bool merge)
        {
            if(raw==null)return;
            var incoming=json.Deserialize<DailyReview>(json.Serialize(raw));
            if(incoming==null||incoming.rows==null||incoming.rows.Count==0)return;
            var data=ReadReview();
            var old=data.Daily;
            if(merge&&old!=null&&old.rows!=null&&old.day==incoming.day&&old.account==incoming.account){
                foreach(var row in incoming.rows){
                    int index=old.rows.FindIndex(x=>x.key==row.key);
                    if(index<0)old.rows.Add(row);else old.rows[index]=row;
                }
            }else data.Daily=incoming;
            SaveReview();
        }

        private async void OpenReview(object sender,EventArgs args)
        {
            var data=ReadReview();
            var prior=data.Daily;
            bool glass=UiTheme.CurrentTheme==UiTheme.ThemeLiquidGlass;
            using(var dialog=ReviewDialog("任务记录",840,520))
            {
                var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,ShowItemToolTips=true,
                    BackColor=glass?LiquidGlassEngine.GlassInputFill():Color.FromArgb(70,58,35),
                    ForeColor=glass?LiquidGlassEngine.GlassInk(true):TextColor,Font=UiTheme.GameFont(8.5f)};
                list.Columns.Add("事项",155);list.Columns.Add("结果",106);list.Columns.Add("详情",glass?530:508);
                foreach(var row in prior==null||prior.rows==null?new List<ReviewRow>():prior.rows){
                    var item=new ListViewItem(new[]{row.name,row.status,row.detail}){ToolTipText=row.detail};
                    list.Items.Add(item);
                }
                if(list.Items.Count==0)list.Items.Add(new ListViewItem("等待首次日常结果"));
                string historyText=data.History.Count==0?"等待任务记录":string.Join(Environment.NewLine+Environment.NewLine,data.History);
                var footer=ReviewFooter();
                footer.Controls.Add(ReviewButton("关闭",DialogResult.Cancel,GameButtonSkin.Stone));
                var retry=ReviewButton("补办未完成项",DialogResult.Retry,GameButtonSkin.Green);
                retry.Enabled=prior!=null&&prior.rows!=null&&prior.rows.Any(x=>x.retry)&&coreReady&&!AnyTaskRunning();
                footer.Controls.Add(retry);
                if(glass)
                {
                    var root=new SettingsRootPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(14,10,14,10),Margin=Padding.Empty};
                    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f));
                    root.RowStyles.Add(new RowStyle(SizeType.Absolute,36f));
                    root.RowStyles.Add(new RowStyle(SizeType.Percent,100f));
                    root.RowStyles.Add(new RowStyle(SizeType.Absolute,50f));
                    var seg=new GlassSegmentedBar("日常结果","消耗与收益"){Dock=DockStyle.Left,Width=240,Height=32,Margin=new Padding(0,0,0,4)};
                    var segHost=new Panel{Dock=DockStyle.Fill,BackColor=Color.Transparent,Margin=Padding.Empty};
                    segHost.Controls.Add(seg);
                    var bodyHost=new Panel{Dock=DockStyle.Fill,BackColor=Color.Transparent,Margin=new Padding(0,4,0,4)};
                    var dailyWrap=new CardPanel{Dock=DockStyle.Fill,Padding=new Padding(8),CornerRadius=10};
                    dailyWrap.Controls.Add(list);
                    var recentBox=new GlassReadOnlyTextBox{Dock=DockStyle.Fill,Font=UiTheme.GameFont(9f),Text=historyText,Visible=false};
                    bodyHost.Controls.Add(dailyWrap);
                    bodyHost.Controls.Add(recentBox);
                    seg.SelectedIndexChanged+=delegate{dailyWrap.Visible=seg.SelectedIndex==0;recentBox.Visible=seg.SelectedIndex==1;};
                    footer.Dock=DockStyle.Fill;footer.BackColor=Color.Transparent;
                    root.Controls.Add(segHost,0,0);root.Controls.Add(bodyHost,0,1);root.Controls.Add(footer,0,2);
                    dialog.Padding=Padding.Empty;
                    dialog.Controls.Add(root);
                    GlassControlStyler.ApplyToForm(dialog);
                }
                else
                {
                    var tabs=new TabControl{Dock=DockStyle.Fill};
                    var daily=new TabPage("日常结果");
                    daily.Controls.Add(list);
                    var recent=new TabPage("消耗与收益");
                    recent.Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,
                        BackColor=Color.FromArgb(70,58,35),ForeColor=TextColor,Font=UiTheme.GameFont(9f),Text=historyText});
                    tabs.TabPages.Add(daily);tabs.TabPages.Add(recent);
                    dialog.Controls.Add(tabs);dialog.Controls.Add(footer);
                }
                if(dialog.ShowDialog(this)!=DialogResult.Retry||AnyTaskRunning())return;
            }
            var scope=prior.rows.Where(x=>x.retry).ToDictionary(x=>x.key,x=>true);
            var account=json.Serialize(prior.account);
            var command="__MWA:start_daily_unfinished("+account+", "+prior.day+", "+
                "{"+string.Join(",",scope.Keys.Select(k=>"["+json.Serialize(k)+"]=true"))+"})";
            await StartDailyAsync(false,command);
        }

        private Form ReviewDialog(string title,int width,int height)
        {
            bool glass=UiTheme.CurrentTheme==UiTheme.ThemeLiquidGlass;
            var dlg=new CompositedForm{Text=title,ClientSize=new Size(width,height),StartPosition=FormStartPosition.CenterParent,
                FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,
                BackColor=glass?UiTheme.Palette.BgColor:Color.FromArgb(63,51,32),
                ForeColor=glass?LiquidGlassEngine.GlassInk(true):TextColor,Font=UiTheme.GameFont(9f),Padding=new Padding(12)};
            if(glass)GlassFormTransition.Attach(dlg);
            return dlg;
        }

        private static FlowLayoutPanel ReviewFooter(){
            return new FlowLayoutPanel{Dock=DockStyle.Bottom,FlowDirection=FlowDirection.RightToLeft,Height=49,Padding=new Padding(0,6,0,0),BackColor=Color.Transparent};
        }

        private static GameButton ReviewButton(string title,DialogResult result,GameButtonSkin skin){
            return new GameButton{Text=title,Size=new Size(145,39),Skin=skin,DialogResult=result};
        }

        private async Task HandleRuneChoiceAsync(Dictionary<string,object> result,int generation)
        {
            var rows=GetArray(result,"runeTargets");
            if(rows==null||rows.Length==0||runePrompt!=null||WindowState==FormWindowState.Minimized||!Visible||Form.ActiveForm!=this)return;
            string chosen=null;
            bool glass=UiTheme.CurrentTheme==UiTheme.ThemeLiquidGlass;
            using(var dialog=ReviewDialog("选择本轮要强化的符文",520,340)){
                var list=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false,
                    BackColor=glass?LiquidGlassEngine.GlassInputFill():Color.FromArgb(70,58,35),
                    ForeColor=glass?LiquidGlassEngine.GlassInk(true):TextColor,Font=UiTheme.GameFont(10f)};
                var ids=new List<string>();
                foreach(var raw in rows){
                    var row=raw as Dictionary<string,object>;
                    if(row==null)continue;
                    ids.Add(GetString(row,"id"));
                    list.Items.Add("槽位 "+GetInt(row,"slot")+"  ·  "+GetString(row,"name")+"  ·  Lv."+GetInt(row,"level")+"/"+GetInt(row,"max"));
                }
                if(ids.Count==0)return;
                var footer=ReviewFooter();
                var cancel=ReviewButton("跳过本轮强化",DialogResult.Cancel,GameButtonSkin.Stone);
                var confirm=ReviewButton("确认强化",DialogResult.OK,GameButtonSkin.Green);
                confirm.Enabled=false;
                list.SelectedIndexChanged+=delegate{confirm.Enabled=list.SelectedIndex>=0;};
                footer.Controls.Add(cancel);footer.Controls.Add(confirm);
                if(glass)
                {
                    var root=new SettingsRootPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(14,12,14,10),Margin=Padding.Empty};
                    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f));
                    root.RowStyles.Add(new RowStyle(SizeType.Percent,100f));
                    root.RowStyles.Add(new RowStyle(SizeType.Absolute,50f));
                    var card=new CardPanel{Dock=DockStyle.Fill,Padding=new Padding(8),CornerRadius=10};
                    card.Controls.Add(list);
                    footer.Dock=DockStyle.Fill;footer.BackColor=Color.Transparent;
                    root.Controls.Add(card,0,0);root.Controls.Add(footer,0,1);
                    dialog.Padding=Padding.Empty;
                    dialog.Controls.Add(root);
                    GlassControlStyler.ApplyToForm(dialog);
                }
                else
                {
                    dialog.Controls.Add(list);dialog.Controls.Add(footer);
                }
                dialog.CancelButton=cancel;
                runePrompt=dialog;
                try{
                    DialogResult action=dialog.ShowDialog(this);
                    chosen=action==DialogResult.OK&&list.SelectedIndex>=0?ids[list.SelectedIndex]:"";
                }finally{runePrompt=null;}
            }
            if(generation!=taskGeneration)return;
            var reply=await ExecuteLuaAsync("return __MWA:choose_rune_target("+json.Serialize(chosen)+")");
            if(reply!="true")AddLog("强化目标已变化，正在重新检查出战页。",YellowColor);
        }

        private string TaskWaitText(string message,bool waiting,Dictionary<string,object> result)
        {
            string operation=waiting?GetString(result,"operation"):"";
            if(operation.Length==0){waitOperation="";return message;}
            DateTime now=DateTime.UtcNow;
            if(waitOperation!=operation){waitOperation=operation;waitStartedUtc=now;}
            return WaitText(operation,(int)(now-waitStartedUtc).TotalSeconds,GetString(result,"waitState"));
        }

        private static string WaitText(string operation,int seconds,string state)
        {
            return operation+" · "+state+(seconds>=3?" · 已等 "+seconds+" 秒":"");
        }

        private void UpdateMiningReview(Dictionary<string,object> snapshot)
        {
            object raw;
            var wait=snapshot.TryGetValue("miningWait",out raw)?raw as Dictionary<string,object>:null;
            if(wait!=null){
                lblDetail.Text=WaitText(GetString(wait,"operation"),GetInt(wait,"seconds"),GetString(wait,"state"));
                hints.SetToolTip(lblDetail,lblDetail.Text);
            }
            var route=snapshot.TryGetValue("miningRoute",out raw)?raw as Dictionary<string,object>:null;
            if(mineRouteLabel==null)return;
            bool show;
            options.TryGetValue("showMiningRoute",out show);
            var body=mineRouteLabel.Parent as TableLayoutPanel;
            if(body!=null && body.RowStyles.Count>3 && body.RowStyles[3].Height!=(show?22:0))body.RowStyles[3].Height=show?22:0;
            else mineRouteLabel.Visible=show;
            var text=route==null?"矿区目标 · 等待路线":"目标 "+GetString(route,"label")+" ("+GetInt(route,"x")+", "+GetInt(route,"y")+")";
            if(route!=null&&route.ContainsKey("nextX"))text+="   →   "+(GetBool(route,"blast")?"用雷管":"下一格")+" ("+GetInt(route,"nextX")+", "+GetInt(route,"nextY")+")";
            if(mineRouteLabel.Text!=text){mineRouteLabel.Text=text;hints.SetToolTip(mineRouteLabel,text);}
        }

        private void UpdateTrayStatus(DateTime utcNow)
        {
            if(trayIcon==null)return;
            var parts=new List<string>();
            if((IsTaskRunning(AssistantTaskKind.Growth)||IsTaskRunning(AssistantTaskKind.Daily))&&lastTaskMessage=="等待选择强化目标")parts.Add("符文等待选择");
            else if(lastRunning)parts.Add("正在挖矿");
            else if(AnyTaskRunning())parts.Add("任务进行中");
            foreach(var run in scheduleBook.Harvest.OrderBy(r=>r.DueUtc)){
                if(!scheduleBook.EnabledHarvest(run.Task))continue;
                string name=run.Task=="mine"?"矿山":run.Task=="refine"?"炼化":"矿车";
                var left=run.DueUtc-utcNow;
                if(left<=TimeSpan.Zero)parts.Add(name+"已到点，等待执行");
                else if(left.TotalMinutes<=60)parts.Add(name+" "+Math.Ceiling(left.TotalMinutes)+" 分钟");
                else parts.Add(name+" "+run.DueUtc.ToLocalTime().ToString("MM/dd HH:mm"));
            }
            if(scheduleBook.Pending.Count>0)parts.Add("待执行 "+scheduleBook.Pending.Count+" 项计划");
            string text=parts.Count==0?ProfileTrayText():string.Join(" · ",parts);
            if(text.Length>63)text=text.Substring(0,62)+"…";
            if(text!=trayStatus){trayStatus=text;trayIcon.Text=text;}
        }
    }
}
