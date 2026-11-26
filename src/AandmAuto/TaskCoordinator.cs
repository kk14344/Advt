using System;
using System.Drawing;

namespace AandmAuto
{
	internal enum AssistantTaskKind
	{
		None,
		Daily,
		Arena,
		Void,
		Mine,
		Workshop,
		GuildBoss,
		Growth,
		VoidExchange
	}

	internal sealed partial class MainForm
	{
		private AssistantTaskKind activeTask = AssistantTaskKind.None;
		private bool taskBusy;
		private bool taskWaitLogged;
		private DateTime taskNextAt = DateTime.MinValue;

		private bool HasActiveTask
		{
			get { return activeTask != AssistantTaskKind.None; }
		}

		private bool IsTaskRunning(AssistantTaskKind kind)
		{
			return activeTask == kind;
		}

		private bool IsOtherTaskRunning(AssistantTaskKind kind)
		{
			return activeTask != AssistantTaskKind.None && activeTask != kind;
		}

		private bool CanStartTask(string busyMessage = "已有自动任务在运行，请稍候。")
		{
			if (HasActiveTask)
			{
				AddLog(busyMessage, YellowColor);
				return false;
			}
			if (!coreReady)
			{
				AddLog("内置浏览器尚未加载完成，请稍候。", YellowColor);
				return false;
			}
			return true;
		}

		private int BeginTask(AssistantTaskKind kind)
		{
			taskGeneration++;
			activeTask = kind;
			taskInitializing = true;
			taskBusy = false;
			taskWaitLogged = false;
			taskNextAt = DateTime.Now;
			RefreshTaskControls();
			return taskGeneration;
		}

		private void EndTask(AssistantTaskKind kind)
		{
			taskGeneration++;
			CancelTaskInPage();
			if (activeTask == kind) activeTask = AssistantTaskKind.None;
			taskBusy = false;
			taskWaitLogged = false;
			taskNextAt = DateTime.MinValue;
			taskInitializing = false;
			RefreshTaskControls();
		}

		private void ResetAllTaskStates()
		{
			taskGeneration++;
			activeTask = AssistantTaskKind.None;
			taskInitializing = false;
			taskBusy = false;
			taskWaitLogged = false;
			taskNextAt = DateTime.MinValue;
		}

		private void GetActiveTaskDisplay(out string taskStatus, out Color taskColor, out string badge)
		{
			taskStatus = "进行中";
			taskColor = BlueColor;
			badge = "进行中";
			switch (activeTask)
			{
				case AssistantTaskKind.Arena:
					taskStatus = "竞技场征战";
					taskColor = GoldColor;
					break;
				case AssistantTaskKind.Void:
					taskStatus = "虚空探险";
					break;
				case AssistantTaskKind.GuildBoss:
					taskStatus = "工会首领";
					taskColor = GoldColor;
					badge = "征战中";
					break;
				case AssistantTaskKind.Workshop:
					taskStatus = workshopMode == "both"
						? "工坊收取与生产"
						: (workshopMode == "collect" ? "收取工坊" : "工坊生产");
					taskColor = GoldColor;
					badge = "工坊";
					break;
				case AssistantTaskKind.Growth:
					taskStatus = growthMode == "rune"
						? "符文抽奖与升级"
						: (growthMode == "destiny" ? "宿命武器强化" : "矿区首领挑战");
					taskColor = GoldColor;
					badge = "养成";
					break;
				case AssistantTaskKind.VoidExchange:
					taskStatus = "虚空奖励兑换";
					taskColor = GoldColor;
					badge = "兑换中";
					break;
				case AssistantTaskKind.Daily:
					if (dailyEncounterPhase)
					{
						taskStatus = "奇遇清空";
						badge = "挖矿中";
					}
					else if (!dailyMinePhase)
					{
						taskStatus = "日常委托";
					}
					else
					{
						taskStatus = "采集与研究";
						taskColor = GoldColor;
						badge = "采集中";
					}
					break;
				case AssistantTaskKind.Mine:
					taskStatus = "采集与研究";
					taskColor = GoldColor;
					badge = "采集中";
					break;
			}
		}
	}
}
