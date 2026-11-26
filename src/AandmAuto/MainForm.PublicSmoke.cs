using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private async Task RunPublicStartupSmokeAsync()
        {
            pollTimer.Stop();dailyTaskTimer.Stop();autoLoginEnabled=false;
            var watch=Stopwatch.StartNew();string probe="null";bool ready=false;bool captured=false;bool sceneReady=false;double coreSeconds=0;
            int backgroundTicks=0;bool backgroundRunning=false;
            string startupResources="null";
            try
            {
                while(watch.Elapsed.TotalSeconds<75)
                {
                    await Task.Delay(500);
                    probe=await ExecuteScriptAsync("(function(){var a=window.__MWA_APP;var r=!!(a&&a.ready());return {ready:r,api:!!a,scene:r?a.runLua('return require(\"scene.scene_manager\"):GetCurrentSceneName()'):null,enabled:!!(a&&a.getConfig().enabled),canvas:document.querySelectorAll('canvas').length};})()");
                    var state=json.Deserialize<Dictionary<string,object>>(probe);
                    ready=GetBool(state,"ready");
                    if(ready && coreSeconds==0)coreSeconds=watch.Elapsed.TotalSeconds;
                    object scene;sceneReady=state.TryGetValue("scene",out scene) && scene!=null && !string.IsNullOrEmpty(Convert.ToString(scene));
                    if(ready && sceneReady)break;
                }
                if(ready)
                {
                    startupResources=await ExecuteScriptAsync("(function(){var p=performance.getEntriesByType('resource').filter(function(e){return new URL(e.name).pathname.indexOf('/assets-bootstrap/part.')===0});return {requests:p.length,transferredBytes:p.reduce(function(n,e){return n+e.transferSize},0),decodedBytes:p.reduce(function(n,e){return n+e.decodedBodySize},0)}})()");
                    await ExecuteScriptAsync("(function(){var e=window.__AANDM.engine;window.__realBgCount=0;window.__realBgOriginal=e.onTick;window.__realBgWrapped=function(){window.__realBgCount++;if(window.__realBgOriginal)return window.__realBgOriginal.apply(this,arguments);};e.onTick=window.__realBgWrapped;})()");
                    WindowState=System.Windows.Forms.FormWindowState.Minimized;KeepPageRunning();
                    await Task.Delay(15000);
                    var live=json.Deserialize<Dictionary<string,object>>(await ExecuteScriptAsync("({ticks:window.__realBgCount,running:window.__AANDM.engine.running,background:window.__AANDM.engine.background})"));
                    backgroundTicks=GetInt(live,"ticks");backgroundRunning=backgroundTicks>40&&GetBool(live,"running")&&!GetBool(live,"background");
                    await ExecuteScriptAsync("(function(){var e=window.__AANDM.engine;if(e.onTick===window.__realBgWrapped)e.onTick=window.__realBgOriginal;})()");
                    WindowState=System.Windows.Forms.FormWindowState.Normal;
                    await Task.Delay(1500);
                    using(var stream=File.Create(Path.Combine(DataRoot(),"public-login-preview.png")))
                        await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
                    captured=true;
                }
            }
            catch(Exception e){probe=json.Serialize(new {error=e.Message});}
            File.WriteAllText(Path.Combine(DataRoot(),"public-game-check.json"),json.Serialize(new {
                result=ready&&sceneReady&&captured&&backgroundRunning?"passed":"incomplete",coreReady=ready,captured=captured,sceneReady=sceneReady,
                realEngineBackgroundTicks=backgroundTicks,realEngineBackgroundRunning=backgroundRunning,backgroundSeconds=15,
                coreSeconds=Math.Round(coreSeconds,2),seconds=Math.Round(watch.Elapsed.TotalSeconds,2),probe=probe,
                resourceTiming=json.DeserializeObject(startupResources),
                hostCache=bootstrapAssets==null?null:new {
                    hits=bootstrapAssets.Hits,misses=bootstrapAssets.Misses,stored=bootstrapAssets.Saved,
                    eligible=bootstrapAssets.EligibleResponses,errors=bootstrapAssets.ReadErrors,rejected=bootstrapAssets.RejectedBodies},
                noAccountLoaded=true,noLoginPerformed=true,noTaskStarted=true
            }),Encoding.UTF8);
            Close();
        }
    }
}
