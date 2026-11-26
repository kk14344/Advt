using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private GameButton btnRune;

		private GameButton btnDestiny;

		private GameButton btnEarth;

		private GameButton btnGrowthOptions;
        private GameButton btnOrePacks;
        private GameButton btnPush;

		private string growthMode = "rune";

		private async void BtnRune_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Growth)) { CancelScheduledQueue(); StopGrowth(); AddLog("已停止符文任务。", YellowColor); return; }
			if (!CanStartTask()) return;
			await StartGrowthAsync("rune");
		}

		private async void BtnDestiny_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Growth)) { CancelScheduledQueue(); StopGrowth(); AddLog("已停止宿命武器任务。", YellowColor); return; }
			if (!CanStartTask()) return;
			await StartGrowthAsync("destiny");
		}

		private async void BtnEarth_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.Growth)) { CancelScheduledQueue(); StopGrowth(); AddLog("已停止矿区首领任务。", YellowColor); return; }
			if (!CanStartTask()) return;
            await StartGrowthAsync("miningBoss");
		}

        private async void BtnOrePacks_Click(object sender,EventArgs e)
        {
            if(IsTaskRunning(AssistantTaskKind.Growth) && growthMode=="orepacks") { CancelScheduledQueue();StopGrowth();AddLog("已停止使用矿石包。",YellowColor);return; }
            if(!CanStartTask())return;
            await StartGrowthAsync("orepacks");
        }
        private async void BtnPush_Click(object sender,EventArgs e)
        {
            if(IsTaskRunning(AssistantTaskKind.Growth)&&growthMode=="push"){CancelScheduledQueue();StopGrowth();AddLog("已停止自动推关。",YellowColor);return;}
            if(CanStartTask())await StartGrowthAsync("push");
        }

		private async Task StartGrowthAsync(string mode, bool continuousRune = true)
		{
			growthMode = mode;
			int generation = BeginTask(AssistantTaskKind.Growth);
			string description = mode == "rune" ? (continuousRune ? "开始符文连抽，余量单抽后强化已装备符文" : "执行定时符文单轮抽取与强化")
				: (mode == "destiny" ? "开始宿命武器强化：选择升星期望结晶最低的武器" : mode=="orepacks" ? "开始一键使用全部低级、中级、高级矿石包，自动关闭获得窗口" : "开始自动挑战矿区首领");
            if(mode=="push")description="开始主线自动推关：三档难度，当前阵容持续重试，金币按游戏规则消耗";
			AddLog(description, BlueColor);
			try
			{
				if (lastRunning || startRequested) await StopAutomationAsync();
				if (generation != taskGeneration) return;
				string code = mode == "rune" ? "__MWA:start_rune("+(continuousRune?"true":"false")+",true); return true"
                    : (mode == "destiny" ? "__MWA:start_destiny(); return true" : mode=="orepacks" ? "__MWA:start_orepacks(); return true" : "__MWA:start_mining_boss(); return true");
                if(mode=="push")code="__MWA:start_push(); return true";
				if (!await InitializeTaskAsync(code)) return;
				await TryGrowthStepAsync();
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("养成任务失败：" + ex.Message, RedColor);
				StopGrowth();
			}
		}

		private async Task TryGrowthStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.Growth) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
			int generation = taskGeneration;
			try
			{
				string lua = growthMode == "rune" ? "return __MWA:rune_step()"
                    : (growthMode == "destiny" ? "return __MWA:destiny_step()" : growthMode=="orepacks" ? "return __MWA:orepacks_step()" : "return __MWA:mining_boss_step()");
                if(growthMode=="push")lua="return __MWA:push_step()";
				string resultJson = await ExecuteLuaAsync(lua);
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
				ReportTaskMessage(TaskWaitText(message,waiting,result), waiting);
                await HandleRuneChoiceAsync(result,generation);
                if(generation!=taskGeneration)return;
				if (GetBool(result, "done"))
				{
                    if(growthMode=="rune")RecordTaskReview("符文抽取与强化",message);
					AddLog(message, GetBool(result, "error") ? RedColor : (GetBool(result, "partial") ? YellowColor : GreenColor));
					NotifyTaskDone(growthMode == "rune" ? "符文" : (growthMode == "destiny" ? "宿命武器" : growthMode=="orepacks" ? "矿石包" : "矿区首领"), message);
					StopGrowth();
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
					AddLog(string.IsNullOrEmpty(message) ? "等待养成数据…" : message, YellowColor);
				}
				taskNextAt = NextTaskAt(result);
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("养成任务失败：" + ex.Message, RedColor);
				StopGrowth();
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopGrowth()
		{
			if (runePrompt != null) runePrompt.Close();
			EndTask(AssistantTaskKind.Growth);
		}

		private async void BtnGrowthOptions_Click(object sender, EventArgs e)
		{
			await OpenUnifiedSettingsAsync("growth");
		}
	}
}
