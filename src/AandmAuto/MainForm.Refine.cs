using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private bool refineTapNotice;

        // The Lua core decides *which* channel to use from the game's own click
        // counter; this method only executes the requested batch. Keeping one
        // owner avoids the 1.41 mix of a Lua click target and a C# retry counter.
        private async Task SendRefineTapAsync(Dictionary<string,object> plan,int generation)
        {
            if(generation!=taskGeneration||plan==null)return;
            if(GetInt(plan,"refineClickCount")>=120)return;
            string mode=GetString(plan,"refineInput");
            if(mode!="pointer"&&mode!="touch"&&mode!="mouse")mode="pointer";
            int batch=GetInt(plan,"refineTapBatch");
            if(batch<1)batch=1;
            if(batch>16)batch=16;
            try{
                string sent=await ExecuteScriptAsync(RefineTapScript(mode,batch));
                if(sent=="true")refineTapNotice=false;
                else if(!refineTapNotice){
                    refineTapNotice=true;
                    AddLog("炼化点击暂未找到可点击的游戏画布，下一步重试。",YellowColor);
                }
            }catch(Exception){
                // A failed synthetic event never changes the game's counter or
                // deadline, so the next step can simply try again or switch.
            }
        }

        private static string RefineTapScript(string mode,int batch)
        {
            return @"(function(mode,n){
                var list=[].slice.call(document.querySelectorAll('canvas')),canvas=null,rect=null,i;
                for(i=0;i<list.length;i++){
                    rect=list[i].getBoundingClientRect();
                    if(rect.width>0&&rect.height>0&&document.elementFromPoint(rect.left+rect.width/2,rect.top+rect.height/2)===list[i]){canvas=list[i];break;}
                }
                if(!canvas){
                    for(i=0;i<list.length;i++){
                        rect=list[i].getBoundingClientRect();
                        if(rect.width>0&&rect.height>0){canvas=list[i];break;}
                    }
                }
                if(!canvas)return false;
                rect=canvas.getBoundingClientRect();
                var x=rect.left+rect.width/2,y=rect.top+rect.height/2;
                function mouse(){
                    var p={bubbles:true,cancelable:true,view:window,clientX:x,clientY:y,button:0,buttons:1};
                    canvas.dispatchEvent(new MouseEvent('mousedown',p));
                    p.buttons=0;
                    canvas.dispatchEvent(new MouseEvent('mouseup',p));
                }
                function touch(){
                    if(typeof Touch!=='function'||typeof TouchEvent!=='function'){mouse();return;}
                    var t=new Touch({identifier:1,target:canvas,clientX:x,clientY:y,
                        pageX:x+window.scrollX,pageY:y+window.scrollY,
                        screenX:x+window.screenX,screenY:y+window.screenY,radiusX:1,radiusY:1,force:1});
                    canvas.dispatchEvent(new TouchEvent('touchstart',{bubbles:true,cancelable:true,touches:[t],targetTouches:[t],changedTouches:[t]}));
                    canvas.dispatchEvent(new TouchEvent('touchend',{bubbles:true,cancelable:true,touches:[],targetTouches:[],changedTouches:[t]}));
                }
                function pointer(){
                    if(typeof PointerEvent!=='function'){touch();return;}
                    var p={bubbles:true,cancelable:true,composed:true,clientX:x,clientY:y,pointerId:1,
                        pointerType:'touch',isPrimary:true,button:0,buttons:1,pressure:0.5,width:1,height:1};
                    canvas.dispatchEvent(new PointerEvent('pointerdown',p));
                    p.buttons=0;p.pressure=0;
                    canvas.dispatchEvent(new PointerEvent('pointerup',p));
                }
                var restore=null;
                if(mode==='pointer'){
                    // The current client calls setPointerCapture() inside its
                    // pointerdown handler. A synthetic pointer id is unknown to
                    // Chromium, so the call would throw before the touch is
                    // dispatched. Suppress capture for this batch only.
                    var saved=canvas.setPointerCapture;
                    var had=Object.prototype.hasOwnProperty.call(canvas,'setPointerCapture');
                    canvas.setPointerCapture=function(){};
                    restore=function(){if(had){canvas.setPointerCapture=saved;}else{try{delete canvas.setPointerCapture;}catch(e){canvas.setPointerCapture=saved;}}};
                }
                try{
                    for(var k=0;k<n;k++){
                        if(mode==='pointer')pointer();
                        else if(mode==='touch')touch();
                        else mouse();
                    }
                }finally{if(restore)restore();}
                return true;
            })('" + mode + "'," + batch.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }
}
