using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private async Task RunBrowserVideoProbeAsync(string projectPath)
        {
            var result = new Dictionary<string, object> { { "backend", "WebView2 direct video prototype" } };
            try
            {
                pollTimer.Stop();
                dailyTaskTimer.Stop();
                autoLoginEnabled = false;
                WindowState = FormWindowState.Normal;
                ClientSize = new Size(1920, 1080);
                CenterToScreen();
                var item = WallpaperEngineLocator.ParseProjectJson(projectPath);
                if (item == null || item.Type != "video" || !File.Exists(item.PlaybackSource))
                    throw new InvalidOperationException("需要可读取的本地视频壁纸。");
                string file = item.PlaybackSource;
                var core = webView.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("aandm-video-probe.local", Path.GetDirectoryName(file),
                    CoreWebView2HostResourceAccessKind.DenyCors);
                string url = json.Serialize("https://aandm-video-probe.local/" + Uri.EscapeDataString(Path.GetFileName(file)));
                core.NavigateToString(@"<!doctype html><meta charset='utf-8'><style>
html,body{margin:0;width:100%;height:100%;background:#131c27}
video{position:fixed;inset:0;width:100%;height:100%;object-fit:cover;opacity:0;pointer-events:none}
canvas{position:fixed;left:38%;top:15%;width:24%;height:70%;background:#18352a;image-rendering:pixelated}
</style><video muted loop autoplay playsinline></video><canvas width='100' height='160'></canvas><script>
const v=document.querySelector('video'),c=document.querySelector('canvas'),g=c.getContext('2d');
window.frameTimes=[];window.clicked=false;window.mediaError='';
g.fillStyle='#a67934';g.fillRect(29,45,42,58);c.onclick=()=>window.clicked=true;
v.addEventListener('error',()=>window.mediaError=String(v.error&&v.error.message||'video error'));
v.src=" + url + @";
if(typeof v.requestVideoFrameCallback==='function'){
 const presented=t=>{frameTimes.push(t);v.requestVideoFrameCallback(presented);};
 v.requestVideoFrameCallback(()=>{v.style.opacity='1';});
 v.requestVideoFrameCallback(presented);
}else window.mediaError='Video frame callback unavailable';
</script>");
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed.TotalSeconds < 18)
                {
                    await Task.Delay(250);
                    string ready = await ExecuteScriptAsync("Boolean(window.frameTimes && window.frameTimes.length)");
                    if (ready == "true") break;
                    string error = await ExecuteScriptAsync("String(window.mediaError || '')");
                    if (error != "\"\"") throw new InvalidOperationException(error);
                }
                if (clock.Elapsed.TotalSeconds >= 18) throw new TimeoutException("视频未提交首帧。");
                result["firstFrameMs"] = clock.ElapsedMilliseconds;
                await Task.Delay(10000);
                int seconds;
                if (!int.TryParse(Environment.GetEnvironmentVariable("AANDM_WALLPAPER_SECONDS"), out seconds)) seconds = 35;
                seconds = Math.Max(10, Math.Min(60, seconds));
                await ExecuteScriptAsync("window.frameTimes.length=0");
                File.WriteAllText(Path.Combine(DataRoot(), "wallpaper-metrics-start"), "");
                await Task.Delay(seconds * 1000);
                File.WriteAllText(Path.Combine(DataRoot(), "wallpaper-metrics-end"), "");
                string raw = await ExecuteScriptAsync(@"(function(){
const q=document.querySelector('video').getVideoPlaybackQuality();
const t=frameTimes.slice(),delta=t.slice(1).map((value,i)=>value-t[i]).sort((a,b)=>a-b);
const rect=c.getBoundingClientRect(),target=document.elementFromPoint(rect.x+rect.width/2,rect.y+rect.height/2);
if(target)target.click();
return {fps:t.length>1?(t.length-1)*1000/(t[t.length-1]-t[0]):0,
 p95Ms:delta.length?delta[Math.ceil(delta.length*.95)-1]:0,width:v.videoWidth,height:v.videoHeight,
 totalVideoFrames:q.totalVideoFrames,droppedVideoFrames:q.droppedVideoFrames,input:target===c&&window.clicked,
 canvas:Array.from(g.getImageData(50,65,1,1).data)};
})()");
                var video = json.Deserialize<Dictionary<string, object>>(raw);
                result["video"] = video;
                result["result"] = Convert.ToBoolean(video["input"]) && Convert.ToDouble(video["fps"]) > 0 ? "passed" : "failed";
            }
            catch (Exception error)
            {
                result["result"] = "failed";
                result["error"] = error.Message;
            }
            File.WriteAllText(Path.Combine(DataRoot(), "wallpaper-smoke.json"), json.Serialize(result));
            Close();
        }
    }
}
