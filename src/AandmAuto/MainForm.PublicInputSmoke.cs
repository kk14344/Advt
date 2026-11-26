using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        // Reads the public client (login screen only, no account) and checks that
        // the game's own native touch simulation exists and reaches a scene-graph
        // touch listener. This is input-path evidence, not a mining/refine run.
        private async Task RunPublicInputSmokeAsync()
        {
            pollTimer.Stop();dailyTaskTimer.Stop();autoLoginEnabled=false;
            var report=new Dictionary<string,object>();
            string error="";
            try
            {
                bool ready=false;
                for(int i=0;i<160;i++)
                {
                    await Task.Delay(500);
                    if(await ExecuteScriptAsync("Boolean(window.__AANDM && window.__AANDM.vm)")=="true"){ready=true;break;}
                }
                report["gameVmReady"]=ready;
                if(ready)
                {
                    const string lua=@"
local result={scene=require('scene.scene_manager'):GetCurrentSceneName()}
result.aandm=type(aandm)
result.sim_touch=type(aandm)=='table' and type(aandm.simTouch) or 'nil'
local ok_panel,panel=pcall(function() return require('ui.resource_recycle_panel') end)
result.refine_panel=ok_panel and type(panel) or 'error'
if ok_panel and type(panel)=='table' then
    result.refine_touch_layer=type(panel.TouchEventLayer)
end
local ok_adv,adv=pcall(function() return require('logic.adventure') end)
result.adventure_open_box=ok_adv and type(adv) and type(adv.OpenBox) or 'error'
local ok_layer,layer=pcall(function() return cc.Layer:create() end)
if ok_layer and layer then
    local got=0
    layer:setTouchEnabled(true)
    local listener=cc.EventListenerTouchAllAtOnce:create()
    listener:registerScriptHandler(function(touches,event) got=got+#touches end,cc.Handler.EVENT_TOUCHES_ENDED)
    layer:getEventDispatcher():addEventListenerWithSceneGraphPriority(listener,layer)
    local scene=cc.Director:getInstance():getRunningScene()
    scene:addChild(layer,99999)
    local ok=pcall(function() aandm.simTouch(320,568) end)
    result.touch_ok=ok
    result.touch_events=got
    _G.__mwa_touch_probe={layer=layer,got=0}
    listener:registerScriptHandler(function(touches,event) _G.__mwa_touch_probe.got=_G.__mwa_touch_probe.got+#touches end,cc.Handler.EVENT_TOUCHES_ENDED)
end
return result";
                    string raw=await ExecuteScriptAsync("(function(){try{return window.__AANDM.vm.execute("+
                        json.Serialize(lua)+");}catch(e){return {error:String(e&&e.message||e)};}})()");
                    var probe=json.Deserialize<Dictionary<string,object>>(raw);
                    if(probe!=null)foreach(var pair in probe)report[pair.Key]=pair.Value;

                    // 真实客户端的画布只监听 pointer 事件；这里按产品代码的方式补一次
                    // pointerdown/up（含 setPointerCapture 兜底），确认兜底通道也能送达。
                    string pointerJs=@"(function(){
                        var c=document.querySelector('canvas');
                        if(!c)return {error:'no canvas'};
                        var r=c.getBoundingClientRect();
                        var x=r.left+16,y=r.top+r.height-16;
                        var saved=c.setPointerCapture;
                        var had=Object.prototype.hasOwnProperty.call(c,'setPointerCapture');
                        c.setPointerCapture=function(){};
                        try{
                            c.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,cancelable:true,clientX:x,clientY:y,pointerId:7,pointerType:'touch',isPrimary:true,button:0,buttons:1,pressure:0.5}));
                            c.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,cancelable:true,clientX:x,clientY:y,pointerId:7,pointerType:'touch',isPrimary:true,button:0,buttons:0,pressure:0}));
                            return {ok:true};
                        }catch(e){return {ok:false,error:String(e&&e.message||e)};}
                        finally{if(had)c.setPointerCapture=saved;else{try{delete c.setPointerCapture;}catch(e){}}}
                    })()";
                    string pointerRaw=await ExecuteScriptAsync(pointerJs);
                    var pointer=json.Deserialize<Dictionary<string,object>>(pointerRaw);
                    report["pointer_dispatch"]=pointer!=null&&pointer.ContainsKey("ok")&&Convert.ToBoolean(pointer["ok"]);

                    string readLua=@"local p=_G.__mwa_touch_probe
local got=p and p.got or 0
if p and p.layer then pcall(function() p.layer:removeFromParent() end) end
_G.__mwa_touch_probe=nil
return {pointer_events=got}";
                    string readRaw=await ExecuteScriptAsync("(function(){try{return window.__AANDM.vm.execute("+
                        json.Serialize(readLua)+");}catch(e){return {error:String(e&&e.message||e)};}})()");
                    var read=json.Deserialize<Dictionary<string,object>>(readRaw);
                    if(read!=null)foreach(var pair in read)report[pair.Key]=pair.Value;
                }
            }
            catch(Exception ex){error=ex.ToString();}
            bool sim=report.ContainsKey("sim_touch")&&Convert.ToString(report["sim_touch"])=="function";
            int events=report.ContainsKey("touch_events")?Convert.ToInt32(report["touch_events"]):0;
            int pointerEvents=report.ContainsKey("pointer_events")?Convert.ToInt32(report["pointer_events"]):0;
            report["simTouchReachedEngine"]=events>0;
            report["pointerFallbackReachedEngine"]=pointerEvents>0;
            report["result"]=(sim&&events>0&&error.Length==0)?"passed":"failed";
            report["note"]="public client, login scene only; no account and no game task";
            report["error"]=error;
            File.WriteAllText(Path.Combine(DataRoot(),"public-input-check.json"),
                json.Serialize(report),Encoding.UTF8);
            Close();
        }
    }
}
