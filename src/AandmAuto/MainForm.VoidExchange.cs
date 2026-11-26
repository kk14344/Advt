using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private GameButton btnVoidExchange = null;

		private async void BtnVoidExchange_Click(object sender, EventArgs e)
		{
			if (IsTaskRunning(AssistantTaskKind.VoidExchange)) { CancelScheduledQueue(); StopVoidExchange(); AddLog("已停止虚空奖励兑换。", YellowColor); return; }
			if (!CanStartTask()) return;
			await StartVoidExchangeAsync();
		}

		private async Task StartVoidExchangeAsync()
		{
			int generation = BeginTask(AssistantTaskKind.VoidExchange);
			AddLog("开始一键兑换虚空奖励：使用虚空积分兑换可购买的奖励", BlueColor);
			try
			{
				if (lastRunning || startRequested) await StopAutomationAsync();
				if (generation != taskGeneration) return;
				if (!await InitializeTaskAsync("__MWA:start_void_exchange(); return true")) return;
				await TryVoidExchangeStepAsync();
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("虚空奖励兑换失败：" + ex.Message, RedColor);
				StopVoidExchange();
			}
		}

		private async Task TryVoidExchangeStepAsync()
		{
			if (!IsTaskRunning(AssistantTaskKind.VoidExchange) || taskInitializing || taskBusy || !coreReady || (DateTime.Now - taskNextAt).TotalMilliseconds < 0.0)
			{
				return;
			}
			taskBusy = true;
			int generation = taskGeneration;
			try
			{
				string resultJson = await ExecuteLuaAsync("return __MWA:void_exchange_step()");
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
					NotifyTaskDone("虚空兑换", message);
					StopVoidExchange();
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
					AddLog(string.IsNullOrEmpty(message) ? "等待虚空商店数据…" : message, YellowColor);
				}
				taskNextAt = NextTaskAt(result);
			}
			catch (Exception ex)
			{
				if (generation != taskGeneration) return;
				AddLog("虚空奖励兑换失败：" + ex.Message, RedColor);
				StopVoidExchange();
			}
			finally
			{
				if (generation == taskGeneration) taskBusy = false;
			}
		}

		private void StopVoidExchange()
		{
			EndTask(AssistantTaskKind.VoidExchange);
		}
	}
}
