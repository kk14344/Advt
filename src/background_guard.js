(function () {
  'use strict';
  if (typeof document === 'undefined' || typeof document.addEventListener !== 'function' || window.__MWA_DISABLE_BG_GUARD) return;
  if (window.__MWA_BACKGROUND && window.__MWA_BACKGROUND.version === '1.33.0') return;
  var nativeHidden;
  for (var proto=document;proto&&!nativeHidden;proto=Object.getPrototypeOf(proto)) {
    var descriptor=Object.getOwnPropertyDescriptor(proto,'hidden');
    if(descriptor&&typeof descriptor.get==='function')nativeHidden=descriptor.get;
  }
  var stats={version:'1.33.0',nativeFrames:0,fallbackFrames:0,skippedNativeFrames:0,wakes:0,errors:0,lastError:'',lastNativeFrame:0};
  var watched=null,manual=false,idle=false,lastFallbackFrame=-Infinity;
  var extremePerf=false,suspendRender=false,extremeTaskActive=false,lastExecutedFrame=-Infinity;
  var lowDefinition=false,scaleCapable=false,scaledFrame=false,nativeRatioOwn,ratioGetter;
  // The game sizes its 2D backing canvas using DPR, then transforms the same
  // design coordinates into that canvas. Pointer mapping uses its CSS rect.
  function setRenderRatio() {
    var own=Object.getOwnPropertyDescriptor(window,'devicePixelRatio');
    if(ratioGetter && (!own || own.get!==ratioGetter))ratioGetter=null;
    if(!lowDefinition || !scaleCapable) {
      if(ratioGetter) {
        if(nativeRatioOwn)Object.defineProperty(window,'devicePixelRatio',nativeRatioOwn);
        else delete window.devicePixelRatio;
        ratioGetter=null;
      }
      return;
    }
    if(ratioGetter || (own && !own.configurable))return;
    var native=own;
    for(var proto=Object.getPrototypeOf(window);proto&&!native;proto=Object.getPrototypeOf(proto))
      native=Object.getOwnPropertyDescriptor(proto,'devicePixelRatio');
    if(!native || typeof native.get!=='function')return;
    var getter=function(){
      var ratio=native.get.call(window);
      return scaledFrame ? ratio*0.67 : ratio;
    };
    try {
      Object.defineProperty(window,'devicePixelRatio',{configurable:true,enumerable:native.enumerable,get:getter});
      nativeRatioOwn=own;
      ratioGetter=getter;
    } catch(e) {stats.errors++;stats.lastError=String(e);}
  }
  // The game stops its native update/receive loop when document.hidden flips.
  // This WebView is a persistent game client, including while its host is minimized.
  try {
    Object.defineProperty(document,'hidden',{configurable:true,get:function(){return false;}});
    Object.defineProperty(document,'visibilityState',{configurable:true,get:function(){return 'visible';}});
  } catch(e) {stats.errors++;stats.lastError=String(e);}
  function pulse(stamp) {
    var app=window.__AANDM,engine=app&&app.engine;
    if(!engine||typeof engine.frame!=='function'||typeof engine.setBackground!=='function')return;
    stamp=typeof stamp==='number'?stamp:performance.now();
    if(watched!==engine) {
      watched=engine;var frame=engine.frame;
      // Match the native sizing and transform together. An unfamiliar engine
      // keeps its original resolution until the adapter can be verified.
      var source=Function.prototype.toString.call(frame);
      scaleCapable=source.indexOf('window.devicePixelRatio,this.visibleClippingDepth(')>=0
        && source.indexOf('this.canvas.width')>=0 && source.indexOf('.scaleX,0,0,-')>=0;
      setRenderRatio();
      stats.lastNativeFrame=stamp;
      lastFallbackFrame=-Infinity;
      lastExecutedFrame=-Infinity;
      engine.frame=function(){
        var now=performance.now();
        if(!manual){
          stats.lastNativeFrame=now;
          if(extremePerf && !lowDefinition){
            var minGap=suspendRender?(extremeTaskActive?250:500):66;
            if(now-lastExecutedFrame<minGap){
              stats.skippedNativeFrames++;
              // The native callback clears its handle before invoking frame.
              if(typeof this.requestFrame==='function')this.requestFrame();
              return;
            }
          }
          stats.nativeFrames++;
        }
        lastExecutedFrame=now;
        if(!lowDefinition || !ratioGetter || this!==engine)return frame.apply(this,arguments);
        var previous=scaledFrame;
        scaledFrame=true;
        try{return frame.apply(this,arguments);}
        finally{scaledFrame=previous;}
      };
    }
    if(engine.running!==true)return; // Never restart an intentionally stopped game.
    try {
      if(engine.background===true) {
        try{if(app.vm&&typeof app.vm.callGlobal==='function')app.vm.callGlobal('applicationDidEnterForeground');}
        finally{engine.setBackground(false);stats.wakes++;}
      }
      // Native rAF may remain suspended even though JS reports visible. Only
      // supply frames while native callbacks are absent; the game's own frame
      // deadlines prevent duplicate logical updates when native rAF returns.
      if(stamp-stats.lastNativeFrame>=150) {
        var shouldFallback;
        if(extremePerf && !lowDefinition) {
          var fallbackGap=suspendRender?(extremeTaskActive?250:500):(idle?400:66);
          shouldFallback=stamp-lastFallbackFrame>=fallbackGap;
        } else {
          shouldFallback=lowDefinition || !idle || stamp-lastFallbackFrame>=200;
        }
        if(shouldFallback) {
          manual=true;
          lastFallbackFrame=stamp;
          try{engine.frame(stamp);stats.fallbackFrames++;}finally{manual=false;}
        }
      }
    } catch(e){manual=false;stats.errors++;stats.lastError=String(e&&e.message||e);}
  }
  stats.pulse=pulse;
  stats.setIdle=function(value){idle=value===true;};
  stats.setExtremePerf=function(enabled,suspend,taskActive){
    var nextExtreme=enabled===true;
    var nextSuspend=nextExtreme && suspend===true;
    var nextTask=taskActive===true;
    if((suspendRender && !nextSuspend) || (extremePerf && !nextExtreme)) {
      lastExecutedFrame=-Infinity;
      lastFallbackFrame=-Infinity;
    }
    extremePerf=nextExtreme;
    suspendRender=nextSuspend;
    extremeTaskActive=nextTask;
  };
  stats.setLowDefinition=function(enabled){
    var next=enabled===true;
    if(next===lowDefinition)return;
    lowDefinition=next;
    lastExecutedFrame=-Infinity;
    lastFallbackFrame=-Infinity;
    setRenderRatio();
  };
  stats.getLowDefinitionStatus=function(){
    return !lowDefinition?'off':(!watched?'pending':(ratioGetter?'active':'unsupported'));
  };
  stats.getMode=function(){
    return {
      extremePerf:extremePerf,
      suspendRender:suspendRender,
      taskActive:extremeTaskActive,
      skippedNativeFrames:stats.skippedNativeFrames
    };
  };
  stats.nativeHidden=function(){try{return nativeHidden?nativeHidden.call(document):false;}catch(e){return null;}};
  window.__MWA_BACKGROUND=stats;
  setInterval(function(){pulse(performance.now());},50);
})();
