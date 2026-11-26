(function () {
  'use strict';
  if (typeof document === 'undefined' || typeof document.addEventListener !== 'function' || window.__MWA_DISABLE_BG_GUARD) return;
  if (window.__MWA_BACKGROUND && window.__MWA_BACKGROUND.version === '1.33.0') return;
  var nativeHidden;
  for (var proto=document;proto&&!nativeHidden;proto=Object.getPrototypeOf(proto)) {
    var descriptor=Object.getOwnPropertyDescriptor(proto,'hidden');
    if(descriptor&&typeof descriptor.get==='function')nativeHidden=descriptor.get;
  }
  var stats={version:'1.33.0',nativeFrames:0,fallbackFrames:0,wakes:0,errors:0,lastError:'',lastNativeFrame:0};
  var watched=null,manual=false,idle=false,lastFallbackFrame=-Infinity;
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
      stats.lastNativeFrame=stamp;
      lastFallbackFrame=-Infinity;
      engine.frame=function(){
        if(!manual){stats.nativeFrames++;stats.lastNativeFrame=performance.now();}
        return frame.apply(this,arguments);
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
      if(stamp-stats.lastNativeFrame>=150 && (!idle || stamp-lastFallbackFrame>=200)) {
        manual=true;
        lastFallbackFrame=stamp;
        try{engine.frame(stamp);stats.fallbackFrames++;}finally{manual=false;}
      }
    } catch(e){manual=false;stats.errors++;stats.lastError=String(e&&e.message||e);}
  }
  stats.pulse=pulse;
  stats.setIdle=function(value){idle=value===true;};
  stats.nativeHidden=function(){try{return nativeHidden?nativeHidden.call(document):false;}catch(e){return null;}};
  window.__MWA_BACKGROUND=stats;
  setInterval(function(){pulse(performance.now());},50);
})();
