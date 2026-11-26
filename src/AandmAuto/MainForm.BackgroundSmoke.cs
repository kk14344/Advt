using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private async Task<Dictionary<string,object>> BackgroundProbeAsync()
        {
            var read=webView.CoreWebView2.ExecuteScriptAsync("window.bgSnapshot()");
            if(await Task.WhenAny(read,Task.Delay(2200))!=read)throw new TimeoutException("Background probe timed out");
            return json.Deserialize<Dictionary<string,object>>(await read);
        }
        private async Task RunBackgroundSmokeAsync()
        {
            bool baseline=Environment.GetEnvironmentVariable("AANDM_BACKGROUND_BASELINE")=="1";
            int seconds; if(!int.TryParse(Environment.GetEnvironmentVariable("AANDM_BACKGROUND_SECONDS"),out seconds))seconds=90;
            seconds=Math.Max(10,Math.Min(180,seconds));
            string error="";bool freezeInjected=false;bool stableDocument=false;int start=0,finish=0,goodWindows=0,probeTimeouts=0;
            var samples=new List<object>();Dictionary<string,object> last=null;string documentId="";
            try
            {
                pollTimer.Stop();pollCycleBusy=true;autoLoginEnabled=false;
                string page=@"<!doctype html><meta charset='utf-8'><body style='background:#1c2728;color:#f3df99'>后台任务连续性验收（离线）<script>
                window.bg={sent:0,settled:0,frames:0,pending:false,replies:[],id:Math.random().toString(36),freeze:false};
                var nativeRaf=window.requestAnimationFrame.bind(window),blocked=[],nextBlocked=900000;
                window.TEST_BLOCK_RAF=false;
                window.requestAnimationFrame=function(fn){if(window.TEST_BLOCK_RAF){blocked.push(fn);return ++nextBlocked;}return nativeRaf(fn);};
                window.releaseBgRaf=function(){window.TEST_BLOCK_RAF=false;var callbacks=blocked;blocked=[];callbacks.forEach(function(fn){nativeRaf(fn);});};
                var e={running:false,background:false,frameHandle:0,last:0,
                 requestFrame:function(){if(!this.running||this.frameHandle||this.background||document.hidden)return;var self=this;this.frameHandle=requestAnimationFrame(function(t){self.frameHandle=0;self.frame(t);});},
                 frame:function(t){if(!this.running||this.background||document.hidden)return;if(t-this.last<15){this.requestFrame();return;}this.last=t;bg.frames++;while(bg.replies.length){bg.replies.shift();bg.settled++;bg.pending=false;}this.requestFrame();},
                 setBackground:function(value){this.background=value;if(value){cancelAnimationFrame(this.frameHandle);this.frameHandle=0;}else this.requestFrame();},
                 start:function(){this.running=true;this.requestFrame();},stop:function(){this.running=false;cancelAnimationFrame(this.frameHandle);this.frameHandle=0;}};
                document.addEventListener('visibilitychange',function(){e.setBackground(document.hidden===true);});
                window.__AANDM={engine:e,vm:{callGlobal:function(){}}};
                window.__MWA_APP={ready:function(){return true;},backgroundPulse:function(){},cancelTask:function(){},runLua:function(code){
                 if(code.indexOf('daily_step')>=0){if(!bg.pending){bg.pending=true;bg.sent++;setTimeout(function(){bg.replies.push(bg.sent);},20);}return {wait:true,message:'后台任务验收 · 已确认'+bg.settled};}return null;}};
                window.bgSnapshot=function(){var g=window.__MWA_BACKGROUND;return {id:bg.id,sent:bg.sent,settled:bg.settled,frames:bg.frames,running:e.running,background:e.background,visible:document.visibilityState,fallback:g?g.fallbackFrames:0,guardErrors:g?g.errors:0};};
                e.start();</script></body>";
                webView.CoreWebView2.NavigateToString(page);await Task.Delay(800);
                activeTask=AssistantTaskKind.Daily;taskInitializing=false;taskBusy=false;taskPumpBusy=false;taskNextAt=DateTime.Now;dailyTaskTimer.Start();
                await Task.Delay(1800);last=await BackgroundProbeAsync();start=GetInt(last,"settled");documentId=GetString(last,"id");
                WindowState=FormWindowState.Minimized;KeepPageRunning();
                await webView.CoreWebView2.ExecuteScriptAsync("window.TEST_BLOCK_RAF=true");
                int previous=start;
                for(int second=1;second<=seconds;second++)
                {
                    await Task.Delay(1000);
                    if(second==Math.Min(8,seconds/2)) {
                        await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.setWebLifecycleState","{\"state\":\"frozen\"}");freezeInjected=true;
                    }
                    if(second%5!=0&&second!=seconds)continue;
                    try{last=await BackgroundProbeAsync();finish=GetInt(last,"settled");if(finish>previous)goodWindows++;previous=finish;}
                    catch(TimeoutException){probeTimeouts++;}
                    samples.Add(new {second=second,settled=finish,watchdogPulses=runtimePulseCount});
                    File.WriteAllText(Path.Combine(DataRoot(),"background-progress.json"),json.Serialize(new {seconds=second,target=seconds,baseline=baseline,settled=finish,samples=samples}),Encoding.UTF8);
                }
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.setWebLifecycleState","{\"state\":\"active\"}");
                await webView.CoreWebView2.ExecuteScriptAsync("window.releaseBgRaf()");WindowState=FormWindowState.Normal;
                await Task.Delay(600);var restored=await BackgroundProbeAsync();stableDocument=GetString(restored,"id")==documentId;
                await webView.CoreWebView2.ExecuteScriptAsync("window.__AANDM.engine.stop()");
            }
            catch(Exception fault){error=fault.ToString();}
            finally{activeTask=AssistantTaskKind.None;dailyTaskTimer.Stop();pollCycleBusy=false;}
            bool requestsConserved=last!=null&&GetInt(last,"sent")>=finish&&GetInt(last,"sent")-finish<=1;
            bool passed=error.Length==0&&stableDocument&&requestsConserved&&finish-start>=seconds*2&&goodWindows>=Math.Max(1,seconds/5-1)&&last!=null&&GetInt(last,"fallback")>0&&runtimePulseCount>10;
            File.WriteAllText(Path.Combine(DataRoot(),"background-test-results.json"),json.Serialize(new {
                result=baseline?(finish-start<5?"baseline-stall-reproduced":"baseline-did-not-stall"):(passed?"passed":"failed"),
                minimizedSeconds=seconds,progressBeforeMinimize=start,progressWhileMinimized=finish-start,
                goodProgressWindows=goodWindows,probeTimeouts=probeTimeouts,documentPreserved=stableDocument,
                requestsConserved=requestsConserved,
                nativeRafPauseInjected=true,nativeFreezeInjected=freezeInjected,independentOfPollTimer=true,
                runtimePulseCount=runtimePulseCount,runtimePulseErrors=runtimePulseErrors,runtimeResumeCount=runtimeResumeCount,
                last=last,samples=samples,error=error,noRealAccountUsed=true,noRealNetworkMutations=true
            }),Encoding.UTF8);Close();
        }
    }
}
