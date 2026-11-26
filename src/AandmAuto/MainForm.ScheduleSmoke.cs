using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private async Task RunRefineSmokeAsync()
        {
            string path=Path.Combine(DataRoot(),"refine-smoke.json");
            var checks=new Dictionary<string,bool>();
            string error="";
            bool foreground=Environment.GetEnvironmentVariable("AANDM_REFINE_FOREGROUND")=="1";
            bool minimizeDuring=Environment.GetEnvironmentVariable("AANDM_REFINE_MINIMIZE")=="1";
            if(foreground)WindowState=FormWindowState.Normal;
            bool minimized=!foreground&&Program.HarvestLaunch&&WindowState==FormWindowState.Minimized&&GetForegroundWindow()!=Handle;
            bool minimizedObserved=false;
            int pointerTaps=0,touchTaps=0,mouseTaps=0,captureErrors=0;
            string calls="",dueState="";
            FormClosed+=delegate{
                checks["windowMode"]=minimizeDuring?minimizedObserved:(Program.HarvestLaunch?minimized:foreground);
                checks["pointerChannelReachedGame"]=pointerTaps>=40;
                checks["touchFallbackReachedGame"]=touchTaps>=40;
                checks["mouseFallbackReachedGame"]=mouseTaps>=40;
                checks["pointerCaptureGuard"]=captureErrors==0;
                checks["counterReachedTarget"]=pointerTaps+touchTaps+mouseTaps>=120;
                checks["refineFinished"]=refineFinished;
                checks["refineRescheduled"]=scheduleBook.Harvest.Exists(r=>r.Task=="refine"&&r.DueUtc>DateTime.UtcNow.AddMinutes(5));
                bool passed=checks.Count==8&&!checks.ContainsValue(false)&&error.Length==0;
                File.WriteAllText(path,json.Serialize(new {result=passed?"passed":"failed",foreground,minimizeDuring,minimized,
                    taps=pointerTaps+touchTaps+mouseTaps,pointerTaps,touchTaps,mouseTaps,captureErrors,calls,dueState,checks,error}));
            };
            try {
                pollTimer.Stop();
                taskInitializing=true;
                if(scheduleBook.Harvest.Count>0)dueState=scheduleBook.Harvest[0].DueUtc.ToString("o")+"|"+
                    scheduleBook.Harvest[0].DueUtc.Kind+"|now="+DateTime.UtcNow.ToString("o");
                webView.CoreWebView2.NavigateToString(
                    "<!doctype html><meta charset='utf-8'><canvas width='400' height='500' "+
                    "style='display:block;touch-action:none'></canvas><script>"+
                    "window.pointerTaps=0;window.touchTaps=0;window.mouseTaps=0;window.captureErrors=0;window.mode='pointer';"+
                    "var target=document.querySelector('canvas');"+
                    "target.addEventListener('pointerdown',function(e){pointerTaps++;try{target.setPointerCapture(e.pointerId);}catch(err){captureErrors++;}});"+
                    "target.addEventListener('touchstart',function(e){touchTaps++;e.preventDefault();},{passive:false});"+
                    "target.addEventListener('mousedown',function(){mouseTaps++;});"+
                    "</script>");
                for(int i=0;i<30;i++){
                    if((await ExecuteScriptAsync("Boolean(document.querySelector('canvas') && window.__MWA_APP_VERSION)"))=="true")break;
                    await Task.Delay(100);
                }
                await ExecuteScriptAsync(@"
                    window.finish=false;
                    window.calls=[];
                    window.__MWA_APP={ready:function(){return true;},cancelTask:function(){},runLua:function(code){
                        if(code.indexOf('harvest_account()')>=0){calls.push('account');return 'world:TEST';}
                        if(code.indexOf('start_daily(true,false,true)')>=0){calls.push('start');return true;}
                        if(code.indexOf('daily_step')>=0)calls.push('step');
                        if(code.indexOf('daily_step')>=0){
                            var total=pointerTaps+touchTaps+mouseTaps;
                            if(!window.finish){
                                if(pointerTaps>=40&&touchTaps<40)window.mode='touch';
                                else if(touchTaps>=40&&mouseTaps<40)window.mode='mouse';
                                return {wait:true,message:'炼化点击测试',refineTap:true,refineInput:window.mode,
                                    refineTapBatch:8,refineClickCount:total,delaySeconds:.02};
                            }
                            return {done:true,message:'炼化续跑测试',harvest:{refine:{account:'TEST',seconds:900,heat:9600,estimated:false}}};
                        }
                        return null;
                    }};");
                taskInitializing=false;
                if(minimizeDuring){
                    WindowState=FormWindowState.Minimized;
                    await Task.Delay(500);
                }
                for(int i=0;i<160;i++){
                    if(minimizeDuring&&WindowState==FormWindowState.Minimized&&GetForegroundWindow()!=Handle)minimizedObserved=true;
                    pointerTaps=GetScriptInt(await ExecuteScriptAsync("window.pointerTaps"));
                    touchTaps=GetScriptInt(await ExecuteScriptAsync("window.touchTaps"));
                    mouseTaps=GetScriptInt(await ExecuteScriptAsync("window.mouseTaps"));
                    captureErrors=GetScriptInt(await ExecuteScriptAsync("window.captureErrors"));
                    if(pointerTaps+touchTaps+mouseTaps>=120)break;
                    await Task.Delay(200);
                }
                await ExecuteScriptAsync("window.finish=true");
                try{calls=await ExecuteScriptAsync("JSON.stringify(window.calls)");}catch(Exception){}
                await Task.Delay(9000);
                if(!IsDisposed){
                    if(Program.HarvestLaunch)error="炼化完成后窗口仍打开 · "+WindowState+" · manual="+keepScheduledWindowOpen+
                        " · busy="+AnyTaskRunning()+" · due="+(scheduleBook.DueHarvest(DateTime.UtcNow)!=null);
                    Close();
                }
            }catch(Exception ex){if(!IsDisposed){error=ex.ToString();Close();}}
        }

        private static int GetScriptInt(string raw)
        {
            int value;
            return int.TryParse(raw,out value)?value:0;
        }

        private async Task RunCheckinSmokeAsync()
        {
            string path=Path.Combine(DataRoot(),"checkin-smoke.json");
            var checks=new Dictionary<string,bool>();
            string error="";
            FormClosed+=delegate{
                checks["slotConsumed"]=!scheduleBook.CheckinDue(DateTime.UtcNow);
                checks["dailyStopped"]=checkinFinished&&!IsTaskRunning(AssistantTaskKind.Daily);
                bool passed=checks.Count==8&&!checks.ContainsValue(false)&&error.Length==0;
                File.WriteAllText(path,json.Serialize(new {result=passed?"passed":"failed",checks,error}));
            };
            try {
                checks["startsMinimized"]=Program.CheckinLaunch&&WindowState==FormWindowState.Minimized&&GetForegroundWindow()!=Handle;
                checks["switchReflectsSettings"]=scheduleBook.AutoCheckin;
                pollTimer.Stop();
                webView.CoreWebView2.NavigateToString("<!doctype html><meta charset='utf-8'>Check-in test");
                for(int tick=0;tick<30;tick++){
                    if((await ExecuteScriptAsync("Boolean(window.__MWA_APP_VERSION)"))=="true")break;
                    await Task.Delay(100);
                }
                await ExecuteScriptAsync(@"
                  window.account='0';window.canFinish=false;window.calls=0;
                  window.__MWA_APP={ready:function(){return true;},cancelTask:function(){},runLua:function(code){
                    if(code.indexOf('harvest_account()')>=0)return 'world:'+window.account;
                    if(code.indexOf('start_daily(true,true)')>=0){calls++;return true;}
                    if(code.indexOf('daily_step')>=0)return {
                      done:window.canFinish,wait:!window.canFinish,message:'定时签到验收',
                      dailyReport:{account:'TEST',day:1,rows:[{key:'1',name:'签到',status:'已处理'}]}
                    };
                    return null;
                  }};");
                await Task.Delay(3300);
                checks["waitsForLogin"]=(await ExecuteScriptAsync("window.calls"))=="0";
                taskInitializing=true;
                await ExecuteScriptAsync("window.account='TEST'");
                await Task.Delay(1000);
                checks["waitsWhileBusy"]=(await ExecuteScriptAsync("window.calls"))=="0";
                taskInitializing=false;
                for(int tick=0;tick<24;tick++){
                    if((await ExecuteScriptAsync("window.calls"))=="1"&&IsTaskRunning(AssistantTaskKind.Daily))break;
                    await Task.Delay(250);
                }
                await Task.Delay(800);
                checks["startsDailyOnce"]=IsTaskRunning(AssistantTaskKind.Daily)&&(await ExecuteScriptAsync("window.calls"))=="1";
                checks["staysMinimizedUntilComplete"]=WindowState==FormWindowState.Minimized&&GetForegroundWindow()!=Handle;
                await ExecuteScriptAsync("window.canFinish=true");
                await Task.Delay(3000);
                if(!IsDisposed){error="签到完成后窗口仍打开";Close();}
            }catch(Exception ex){if(!IsDisposed){error=ex.ToString();Close();}}
        }

        private async Task RunScheduleSmokeAsync()
        {
            var checks=new Dictionary<string,bool>();
            string error="";
            string trace="";
            try
            {
                checks["startsMinimized"]=Program.ScheduledLaunch&&WindowState==FormWindowState.Minimized;
                checks["foregroundPreserved"]=GetForegroundWindow()!=Handle;
                pollTimer.Stop();
                const string script=@"
                window.account='OTHER';window.calls=[];
                window.__MWA_APP={ready:function(){return true;},cancelTask:function(){},runLua:function(code){
                  if(code.indexOf('harvest_account()')>=0)return 'world:'+window.account;
                  if(code.indexOf('start_mine_collect')>=0){calls.push('mine');return true;}
                  if(code.indexOf('start_escort_followup')>=0){calls.push('escort');return true;}
                  if(code.indexOf('mine_step')>=0)return {done:true,message:'矿山验收完成',harvest:{mine:{account:'TEST',seconds:1800}}};
                  if(code.indexOf('escort_followup_step')>=0)return {done:true,message:'矿车验收完成',harvest:{escort:{account:'TEST',seconds:2400}}};
                  return null;
                }};";
                webView.CoreWebView2.NavigateToString("<!doctype html><meta charset='utf-8'>Schedule test");
                for(int i=0;i<30;i++){
                    if((await ExecuteScriptAsync("Boolean(window.__MWA_APP_VERSION)"))=="true")break;
                    await Task.Delay(100);
                }
                await ExecuteScriptAsync(script);
                await Task.Delay(3500);
                checks["waitsForRightAccount"]=(await ExecuteScriptAsync("window.calls.length"))=="0";
                taskInitializing=true;
                await ExecuteScriptAsync("window.account='TEST'");
                await Task.Delay(3500);
                checks["waitsWhileBusy"]=(await ExecuteScriptAsync("window.calls.length"))=="0";
                taskInitializing=false;
                for(int tick=0;tick<40;tick++){
                    if((await ExecuteScriptAsync("window.calls.length"))=="2"&&!AnyTaskRunning())break;
                    await Task.Delay(250);
                }
                trace=await ExecuteScriptAsync("window.calls");
                checks["bothTasksRunOnce"]=trace=="[\"mine\",\"escort\"]";
                checks["staysMinimized"]=WindowState==FormWindowState.Minimized&&GetForegroundWindow()!=Handle;
                var saved=json.Deserialize<ScheduleBook>(File.ReadAllText(SchedulePath()));
                checks["reschedulesBoth"]=saved.Harvest.Count==2&&saved.Harvest.TrueForAll(r=>r.DueUtc>DateTime.UtcNow.AddMinutes(20));
                checks["registered"]=harvestRegistrationRetryUtc==DateTime.MaxValue&&File.Exists(Path.Combine(DataRoot(),"windows-harvest-task.xml"));
                checks["trayShowsNextRuns"]=trayIcon.Text.Contains("矿山")&&trayIcon.Text.Contains("矿车");
                await HandleRuneChoiceAsync(json.Deserialize<Dictionary<string,object>>(
                    "{\"runeTargets\":[{\"id\":\"1\",\"name\":\"测试符文\",\"slot\":1,\"level\":1,\"max\":5}]}"),taskGeneration);
                checks["minimizedRuneChoiceStaysPending"]=runePrompt==null&&WindowState==FormWindowState.Minimized;
                RecordDailyReview(json.DeserializeObject(
                    "{\"account\":\"TEST\",\"day\":1,\"rows\":[{\"key\":\"9\",\"name\":\"巨魔巢穴\",\"status\":\"待补办\",\"retry\":true}]}"),false);
                review=null;
                var stored=ReadReview();
                checks["savedDailyReviewAndCartHistory"]=stored.Daily!=null&&stored.Daily.rows.Count==1
                    &&stored.Daily.rows[0].retry&&stored.History.Count==1;
            }
            catch(Exception ex){error=ex.ToString();}
            string result=error.Length==0&&checks.Count==11&&!checks.ContainsValue(false)?"passed":"failed";
            if(result=="failed")error+="\nCalls: "+trace+"\n"+txtLog.Text;
            File.WriteAllText(Path.Combine(DataRoot(),"schedule-smoke.json"),json.Serialize(new {result,checks,error}));
            Close();
        }
    }
}
