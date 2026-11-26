using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed partial class MainForm
	{
		private async Task RunIdleSmokeAsync()
		{
			var result = new Dictionary<string, object>();
			try
			{
				if (!coreReady) throw new InvalidOperationException("WebView2 未初始化。");
				await RunMultiChildFixtureAsync();
                gameSnapshotReady = true;
				SwitchUiAppearance(UiLayoutList, UiTheme.ThemeLiquidGlass, "", 65, UiTheme.BgModeStatic, 12, UiTheme.BgFitCover);
				SwitchBottomTab("log");
				AddLog("日志选择验收", BlueColor);
				txtLog.Select(0, 5);
				string selected = txtLog.SelectedText;
				var batch = new Dictionary<string, object> { {"logSeq", 2}, {"logEpoch", 1},
					{"logs", new object[] {"测试增量第二条", "测试增量第一条"}} };
				UpdateLogs(batch);
				UpdateLogs(new Dictionary<string, object> { {"logSeq", 2}, {"logEpoch", 1}, {"logs", new object[0]} });
				bool logPassed = selected.Length == 5 && txtLog.SelectedText == selected && txtLog.ShortcutsEnabled
					&& txtLog.Text.IndexOf("测试增量第一条", StringComparison.Ordinal) >= 0
					&& txtLog.Text.IndexOf("测试增量第一条", StringComparison.Ordinal) < txtLog.Text.IndexOf("测试增量第二条", StringComparison.Ordinal);
				result["logSelectionAndOrder"] = logPassed;
				if (!logPassed) throw new InvalidOperationException("日志追加时选择或顺序异常。");
				int pulsesBefore = runtimePulseCount;
				WindowState = FormWindowState.Minimized;
                var lowMemoryWait = Stopwatch.StartNew();
                while ((!browserMemoryLow && !memoryPolicyUnsupported) && lowMemoryWait.ElapsedMilliseconds < 32500)
                    await Task.Delay(200);
                result["lowMemoryWaitMs"] = lowMemoryWait.ElapsedMilliseconds;
                result["idleHeartbeatPulses"] = runtimePulseCount - pulsesBefore;
				result["lowMemoryRequested"] = browserMemoryLow;
                result["idleControllerHidden"] = quietGamePresentation && !ControllerIsVisible();
                if (runtimeControllerVisibility != null && ControllerIsVisible())
                    throw new InvalidOperationException("最小化空闲后仍在显示游戏画面。");
				result["memoryPolicyUnsupported"] = memoryPolicyUnsupported;
				if (!browserMemoryLow && !memoryPolicyUnsupported)
					throw new InvalidOperationException("空闲最小化后未切换到低内存模式。窗口状态 " + WindowState + "，任务活动 " + AnyTaskRunning() + "，重连 " + autoLoginReconnecting);
				pulsesBefore = runtimePulseCount;
				var wake = Stopwatch.StartNew();
				EnqueueSchedule(new ScheduleRun { PlanId = "idle-smoke", Name = "最小化唤醒验收", Due = DateTime.Now,
					MaxWaitMinutes = 1, Steps = new List<ScheduleStep> { new ScheduleStep { Task = "push", Minutes = 1 } } });
				while (!HasActiveTask && wake.ElapsedMilliseconds < 7000) await Task.Delay(100);
				result["queuedTaskWakeMs"] = wake.ElapsedMilliseconds;
				result["memoryNormalDuringTask"] = !browserMemoryLow;
                result["controllerVisibleDuringTask"] = ControllerIsVisible();
				bool wakePassed = HasActiveTask && !browserMemoryLow && ControllerIsVisible();
				await Task.Delay(2500);
				result["activeHeartbeatPulses"] = runtimePulseCount - pulsesBefore;
				string starts = await ExecuteScriptAsync("JSON.stringify(window.multiProbe.starts)");
				string steps = await ExecuteScriptAsync("window.multiProbe.steps");
				result["starts"] = starts;
				result["steps"] = steps;
				int completed;
				if (!wakePassed || !starts.Contains("push") || !int.TryParse(steps, out completed) || completed < 1)
					throw new InvalidOperationException("最小化后计划没有唤醒任务。");
				await StopCurrentTasksAsync();
				WindowState = FormWindowState.Normal;
				await Task.Delay(400);
				if (browserMemoryLow || HasActiveTask) throw new InvalidOperationException("停止或还原窗口后状态未恢复。");
				int repetitions;
                if (int.TryParse(Environment.GetEnvironmentVariable("AANDM_ACCEPT_REPEAT"), out repetitions)
                    && repetitions > 0 && repetitions <= 20)
                    await RunWakeRestoreTrialsAsync(result, repetitions);
                result["result"] = "passed";
			}
			catch (Exception ex)
			{
				result["result"] = "failed";
				result["error"] = ex.ToString();
			}
			File.WriteAllText(Path.Combine(DataRoot(), "idle-smoke.json"), json.Serialize(result));
			Close();
		}
	}
}
