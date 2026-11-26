using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private GameButton btnWorkshop;

		private GameButton btnWorkshopOptions;

		private GameButton btnGuildBoss;

		private string workshopMode = "both";
		private string workshopBlockedTitle = "";
		private string workshopBlockedDetail = "";

		private async void BtnWorkshop_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Workshop))
			{
				CancelScheduledQueue(); StopWorkshop();
				AddLog("已停止工坊任务。", YellowColor);
				return;
			}
			if (!CanStartTask()) return;
			await StartWorkshopAsync("both");
		}

		private async Task StartWorkshopAsync(string mode)
		{
			ClearWorkshopBlock();
			workshopMode = (mode == "produce" || mode == "both") ? mode : "collect";
			int generation = BeginTask(AssistantTaskKind.Workshop);
			AddLog(workshopMode == "both"
				? "开始工坊：先收取全部已完成工程，再按 " + GetNumberOption("workshopDepth", 150) + " 层 / " + GetNumberOption("workshopMinutes", 120) + " 分钟自动生产补满空槽位"
				: (workshopMode == "collect"
					? "开始收取工坊：领取全部已完成工程"
					: "开始工坊生产：默认按 " + GetNumberOption("workshopDepth", 150) + " 层 / " + GetNumberOption("workshopMinutes", 120) + " 分钟选工程；矿镐不足时自动补足"), BlueColor);
			try
			{
				if (lastRunning || startRequested) await StopAutomationAsync();
				if (generation != taskGeneration) return;
				string code = "__MWA:start_workshop('" + workshopMode + "'); return true";
				if (!await InitializeTaskAsync(code)) return;
				await TryWorkshopStepAsync();
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("工坊任务失败：" + ex.Message, RedColor);
				StopWorkshop();
			}
		}

		private async Task TryWorkshopStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.Workshop) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
			int generation = taskGeneration;
			try
			{
				string resultJson = await ExecuteLuaAsync("return __MWA:workshop_step()");
				if (generation != taskGeneration) return;
				if (string.IsNullOrEmpty(resultJson) || resultJson == "null")
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				Dictionary<string, object> result = json.Deserialize<Dictionary<string, object>>(resultJson);
				if (result == null)
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				string message = GetString(result, "message");
				bool waiting = GetBool(result, "wait");
				ReportTaskMessage(message, waiting);
				RecordWorkshopBlock(result);
				if (GetBool(result, "done"))
				{
					AddLog(message, GetBool(result, "error") ? RedColor : (GetBool(result, "partial") ? YellowColor : GreenColor));
					NotifyTaskDone(workshopMode == "both" ? "工坊" : (workshopMode == "collect" ? "收取工坊" : "生产工坊"), message);
					StopWorkshop();
					return;
				}
				if (!string.IsNullOrEmpty(message) && !waiting)
				{
					AddLog(message, BlueColor);
					taskWaitLogged = false;
				}
				else if (waiting && !taskWaitLogged)
				{
					taskWaitLogged = true;
					AddLog(string.IsNullOrEmpty(message) ? "等待工坊数据…" : message, YellowColor);
				}
				taskNextAt = NextTaskAt(result);
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("工坊任务失败：" + ex.Message, RedColor);
				StopWorkshop();
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopWorkshop()
		{
			EndTask(AssistantTaskKind.Workshop);
			ShowWorkshopBlock();
		}

		private void ClearWorkshopBlock()
		{
			workshopBlockedTitle = "";
			workshopBlockedDetail = "";
		}

		private void RecordWorkshopBlock(Dictionary<string, object> result)
		{
			string title = GetString(result, "workshop_block");
			if (string.IsNullOrEmpty(title)) return;
			workshopBlockedTitle = title;
			workshopBlockedDetail = GetString(result, "workshop_detail");
			ShowWorkshopBlock();
		}

		private void ShowWorkshopBlock()
		{
			if (string.IsNullOrEmpty(workshopBlockedTitle)) return;
			if (IsTaskRunning(AssistantTaskKind.Daily))
			{
				SetStatusUi("日常委托 · " + workshopBlockedTitle, YellowColor, null);
				if (lblStatus != null) hints.SetToolTip(lblStatus, workshopBlockedDetail);
			}
			else if (!AnyTaskRunning())
			{
				SetStatusUi(workshopBlockedTitle, YellowColor, workshopBlockedDetail);
			}
		}

		private async void BtnGuildBoss_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.GuildBoss))
			{
				CancelScheduledQueue(); StopGuild();
				AddLog("已停止工会首领任务。", YellowColor);
				return;
			}
			if (!CanStartTask()) return;
			await StartGuildAsync(GetNumberOption("guildBossCount", 3));
		}

		private async Task StartGuildAsync(int count)
		{
			count = Math.Max(1, Math.Min(99, count));
			int generation = BeginTask(AssistantTaskKind.GuildBoss);
			AddLog("开始工会首领：计划 " + count + " 次；每次消耗1张试炼许可证，不自动购买", BlueColor);
			try
			{
				if (lastRunning || startRequested) await StopAutomationAsync();
				if (generation != taskGeneration) return;
				if (!await InitializeTaskAsync("__MWA:start_guild_boss(" + count + "); return true")) return;
				await TryGuildStepAsync();
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("工会首领任务失败：" + ex.Message, RedColor);
				StopGuild();
			}
		}

		private async Task TryGuildStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.GuildBoss) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
			int generation = taskGeneration;
			try
			{
				string resultJson = await ExecuteLuaAsync("return __MWA:guild_boss_step()");
				if (generation != taskGeneration) return;
				if (string.IsNullOrEmpty(resultJson) || resultJson == "null")
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				Dictionary<string, object> result = json.Deserialize<Dictionary<string, object>>(resultJson);
				if (result == null)
				{
					taskNextAt = DateTime.Now.AddSeconds(2.0);
					return;
				}
				string message = GetString(result, "message");
				bool waiting = GetBool(result, "wait");
				ReportTaskMessage(message, waiting);
				if (GetBool(result, "done"))
				{
					AddLog(message, GetBool(result, "error") ? RedColor : (GetBool(result, "partial") ? YellowColor : GreenColor));
					NotifyTaskDone("工会首领", message);
					StopGuild();
					return;
				}
				if (!string.IsNullOrEmpty(message) && !waiting)
				{
					AddLog(message, BlueColor);
					taskWaitLogged = false;
				}
				else if (waiting && !taskWaitLogged)
				{
					taskWaitLogged = true;
					AddLog(string.IsNullOrEmpty(message) ? "等待工会首领数据…" : message, YellowColor);
				}
				taskNextAt = NextTaskAt(result);
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("工会首领任务失败：" + ex.Message, RedColor);
				StopGuild();
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopGuild()
		{
			EndTask(AssistantTaskKind.GuildBoss);
		}

		private async void BtnWorkshopOptions_Click(object sender, EventArgs e)
		{
			await OpenUnifiedSettingsAsync("workshop");
		}

		// ------------------------------------------------------------
		// 研究细分小类子页面（1.15）：矿山生产与研究所升级只服务指定分支。
		// cfg.mineResearch = kind*10+branch；0 = 按全部研究缺口自动选层。
		// ------------------------------------------------------------
		internal static string ResearchTitleOf(int sel)
		{
			string[] buildings = new string[] { "", "战士竞技场", "魔法研究院", "牧师祈祷院", "潜行者工会", "炼金工坊", "骑士学院" };
			int kind = sel / 10;
			int branch = sel % 10;
			string name = (kind >= 1 && kind <= 6) ? buildings[kind] : ("研究" + kind);
			return name + (branch == 2 ? "·抵抗" : "·加成");
		}

        internal static int LegacyResearchMask(int sel)
        {
            int kind=sel/10,branch=sel%10;
            return kind>=1&&kind<=6&&(branch==1||branch==2)?1<<((kind-1)*2+branch-1):4095;
        }
        private int MineResearchMask()
        {
            int mask=GetNumberOption("mineResearchMask",-1);
            return mask<0?LegacyResearchMask(GetNumberOption("mineResearch",0)):mask;
        }
        private string MineResearchText()
        {
            int mask=MineResearchMask(),count=0,last=0;
            for(int i=0;i<12;i++)if((mask&(1<<i))!=0){count++;last=i;}
            if(count==12)return "研究目标 · 全部十二项";
            if(count==1)return "研究目标 · "+ResearchTitleOf((last/2+1)*10+last%2+1);
            return "研究目标 · 已选 "+count+" / 12 项";
        }

		private void UpdateMineResearchButton()
		{
			UpdateSettingsSummary();
		}

		private async void BtnMineResearch_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Mine) || IsTaskRunning(AssistantTaskKind.Daily))
			{
				AddLog("矿山或日常任务运行中，请先停止再修改研究目标。", YellowColor);
				return;
			}
			await OpenUnifiedSettingsAsync("mine");
		}
	}

    // All twelve choices stay visible even before login or with partial data.
    internal sealed class ResearchSelectForm : CompositedForm
    {
        private readonly Dictionary<int,GameCheckBox> checks=new Dictionary<int,GameCheckBox>();
        public int SelectionMask {get {int mask=0;foreach(var pair in checks)if(pair.Value.Checked)mask|=1<<pair.Key;return mask;}}
        public int SelectedCount {get {int n=0;foreach(var check in checks.Values)if(check.Checked)n++;return n;}}
        internal int OptionCount {get {return checks.Count;}}
        public ResearchSelectForm(Dictionary<int,string> titles,int mask)
        {
            Text="矿山研究 · 十二项选择";ClientSize=new Size(740,560);
            StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=MinimizeBox=ShowInTaskbar=false;
            BackColor=Color.FromArgb(63,51,32);ForeColor=Color.FromArgb(242,232,205);Font=UiTheme.GameFont(9f);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(14)};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int h in new[]{34,40,0,48,46})root.RowStyles.Add(h==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));
            root.Controls.Add(new GameHeader {Title="矿山研究目标",Hint="十二项可分别勾选，可多选",Dock=DockStyle.Fill},0,0);
            var tools=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var all=Button("全选十二项");all.Click+=delegate{foreach(var check in checks.Values)check.Checked=true;};
            var none=Button("全部清空");none.Click+=delegate{foreach(var check in checks.Values)check.Checked=false;};
            tools.Controls.Add(all);tools.Controls.Add(none);root.Controls.Add(tools,0,1);
            var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=6,Margin=Padding.Empty};
            for(int col=0;col<2;col++)grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            for(int row=0;row<6;row++)grid.RowStyles.Add(new RowStyle(SizeType.Percent,100f/6));
            for(int kind=1;kind<=6;kind++)for(int branch=1;branch<=2;branch++) {
                int index=(kind-1)*2+branch-1,key=kind*10+branch;
                string text; if(!titles.TryGetValue(key,out text))text=MainForm.ResearchTitleOf(key)+"（等级待同步）";
                int split=text.IndexOf('（');string title=split>=0?text.Substring(0,split):text;
                string level=split>=0?text.Substring(split).Trim('（','）'):"等级待同步";
                var card=new CardPanel {Dock=DockStyle.Fill,Margin=new Padding(3),Padding=new Padding(10,3,8,3)};
                var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=Color.Transparent,Margin=Padding.Empty};
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,29));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                var check=new GameCheckBox {Text=title,Tag=index,Dock=DockStyle.Fill,Margin=Padding.Empty,ForeColor=ForeColor,Font=UiTheme.GameFont(9f),Checked=(mask&(1<<index))!=0};
                checks[index]=check;body.Controls.Add(check,0,0);
                var info=new SmoothLabel {Text=level,Dock=DockStyle.Fill,BackColor=Color.Transparent,ForeColor=Color.FromArgb(206,190,153),Font=UiTheme.GameFont(8f),Padding=new Padding(23,0,0,0),Cursor=Cursors.Hand};
                info.Click+=delegate{check.Checked=!check.Checked;};body.Controls.Add(info,0,1);card.Controls.Add(body);grid.Controls.Add(card,branch-1,kind-1);
            }
            root.Controls.Add(grid,0,2);
            root.Controls.Add(new SmoothLabel {Text="只按勾选研究的材料缺口派队与升级；全选按全部研究安排。\r\n清空则只领取成熟矿队奖励；数据未同步时仍可保存选择。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(206,190,153),Padding=new Padding(4,4,0,0)},0,3);
            var footer=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Margin=Padding.Empty};
            var cancel=Button("取消");cancel.DialogResult=DialogResult.Cancel;
            var save=Button("保存目标");save.Skin=GameButtonSkin.Green;save.DialogResult=DialogResult.OK;
            footer.Controls.Add(cancel);footer.Controls.Add(save);AcceptButton=save;CancelButton=cancel;root.Controls.Add(footer,0,4);Controls.Add(root);
        }
        private GameButton Button(string text) {return new GameButton {Text=text,Size=new Size(130,34),Margin=new Padding(0,2,8,2),Skin=GameButtonSkin.Stone};}
    }
}
