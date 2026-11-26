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
        private async Task<bool> VerifyWallpaperCanvasInputAsync()
        {
            if (!ControllerIsVisible()) return false;
            const string create = @"(function(){
              var previous=document.getElementById('acceptance-canvas');
              if(previous)previous.remove();
              var canvas=document.createElement('canvas');
              canvas.id='acceptance-canvas';
              canvas.style.cssText='position:fixed;inset:0;width:100%;height:100%;z-index:9999';
              canvas.addEventListener('click',function(){window.acceptanceCanvasClicked=true});
              window.acceptanceCanvasClicked=false;
              document.body.appendChild(canvas);
              return {x:Math.floor(innerWidth/2),y:Math.floor(innerHeight/2)};
            })()";
            var point = json.Deserialize<Dictionary<string, object>>(await ExecuteScriptAsync(create));
            var core = webView.CoreWebView2;
            foreach (string type in new[] { "mousePressed", "mouseReleased" })
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", json.Serialize(new
                {
                    type, x = point["x"], y = point["y"], button = "left", clickCount = 1
                }));
            return await ExecuteScriptAsync(@"(function(){
                document.getElementById('acceptance-canvas').remove();
                return Boolean(window.acceptanceCanvasClicked);
            })()") == "true";
        }

        private async Task RunWakeRestoreTrialsAsync(Dictionary<string, object> result, int count)
        {
            var trials = new List<object>();
            result["trials"] = trials;
            for (int index = 0; index < count; index++)
            {
                CancelScheduledQueue();
                WindowState = FormWindowState.Minimized;
                await Task.Delay(150);
                if (WindowState != FormWindowState.Minimized) throw new InvalidOperationException("窗口未进入最小化。");

                int startsBefore = int.Parse(await ExecuteScriptAsync("window.multiProbe.starts.length"));
                var wake = Stopwatch.StartNew();
                DateTime now = DateTime.Now;
                DateTime due = now.AddTicks(TimeSpan.TicksPerSecond - now.Ticks % TimeSpan.TicksPerSecond);
                long dueOffsetMs = (long)(due - now).TotalMilliseconds;
                var plan = new SchedulePlan
                {
                    Id = "acceptance-" + index,
                    Name = "离线计划验收",
                    Enabled = true,
                    Kind = "interval",
                    IntervalMinutes = 1,
                    Anchor = ScheduleBook.Stamp(due),
                    Cursor = ScheduleBook.Stamp(now),
                    MaxWaitMinutes = 1,
                    Steps = new List<ScheduleStep> { new ScheduleStep { Task = "push", Minutes = 1 } }
                };
                scheduleBook.Plans.Add(plan);
                SaveSchedule();
                bool started = false;
                bool forceWriteConflict = index == 0;
                string previousLog = txtLog.Text;
                FileStream heldFile = forceWriteConflict
                    ? File.Open(SchedulePath(), FileMode.Open, FileAccess.Read, FileShare.Read)
                    : null;
                try
                {
                    while (wake.ElapsedMilliseconds < 7000)
                    {
                        await Task.Delay(100);
                        if (heldFile != null && wake.ElapsedMilliseconds >= dueOffsetMs + 2000)
                        {
                            heldFile.Dispose();
                            heldFile = null;
                        }
                        int starts = int.Parse(await ExecuteScriptAsync("window.multiProbe.starts.length"));
                        if (starts == startsBefore + 1 && HasActiveTask && !browserMemoryLow)
                        {
                            started = true;
                            break;
                        }
                    }
                }
                finally { if (heldFile != null) heldFile.Dispose(); }
                if (!started) throw new InvalidOperationException("第 " + (index + 1) + " 次计划任务未按时启动。已排队 "
                    + scheduleBook.Pending.Count + "，当前计划 " + (activeSchedule == null ? "" : activeSchedule.PlanId));
                if (forceWriteConflict)
                {
                    string currentLog = txtLog.Text;
                    string recent = currentLog.StartsWith(previousLog, StringComparison.Ordinal)
                        ? currentLog.Substring(previousLog.Length) : currentLog;
                    if (!recent.Contains("任务队列：")) throw new InvalidOperationException("未观察到预期的计划文件写入冲突。");
                    result["recoveredScheduleWriteConflict"] = true;
                }
                long wakeMs = Math.Max(0, wake.ElapsedMilliseconds - dueOffsetMs);
                scheduleBook.Plans.Remove(plan);
                CancelScheduledQueue();
                await StopCurrentTasksAsync();

                var restore = Stopwatch.StartNew();
                WindowState = FormWindowState.Normal;
                bool clicked = false;
                while (!clicked && restore.ElapsedMilliseconds < 4000)
                {
                    await Task.Delay(75);
                    clicked = await VerifyWallpaperCanvasInputAsync();
                }
                if (!clicked) throw new InvalidOperationException("第 " + (index + 1) + " 次恢复后画布未收到点击。");
                trials.Add(new { iteration = index + 1, wakeMs, restoreClickMs = restore.ElapsedMilliseconds });
            }
            result["trialCount"] = trials.Count;
            result["scheduleMethod"] = "interval-plan";
        }
    }
}
