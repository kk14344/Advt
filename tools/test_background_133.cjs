const assert=require('assert'),fs=require('fs'),vm=require('vm'),path=require('path');
const root=path.resolve(__dirname,'..'),source=fs.readFileSync(path.join(root,'src/background_guard.js'),'utf8');
function setup(){
 let now=0,hidden=true,nativeCalls=0,ticks=0,wakes=0;
 const proto={};Object.defineProperty(proto,'hidden',{get:()=>hidden});
 const document=Object.assign(Object.create(proto),{addEventListener(){}}),intervals=[];
 const engine={running:true,background:true,last:0,setBackground(value){this.background=value;},frame(stamp){nativeCalls++;if(!this.running||this.background||document.hidden)return;if(stamp-this.last>=15){this.last=stamp;ticks++;}}};
 const context={document,window:{document,__AANDM:{engine,vm:{callGlobal(){wakes++;}}}},performance:{now:()=>now},setInterval(f){intervals.push(f);}};
 vm.runInNewContext(source,context);
 return {context,engine,intervals,get ticks(){return ticks},get wakes(){return wakes},setTime(n){now=n},hide(v){hidden=v},advance(ms,native=false){for(let i=0;i<ms;i+=10){now+=10;if(native&&now%20===0)engine.frame(now);if(now%50===0)intervals.forEach(f=>f());}}};
}
let checks=0;
function test(name,fn){fn();checks++;console.log('PASS '+name);}
test('hidden host stays visible to the game while retaining native visibility for diagnostics',()=>{const s=setup();assert.equal(s.context.document.hidden,false);assert.equal(s.context.document.visibilityState,'visible');assert.equal(s.context.window.__MWA_BACKGROUND.nativeHidden(),true);});
test('a paused game is resumed once without restarting an intentionally stopped engine',()=>{const s=setup();s.advance(1000);assert.equal(s.wakes,1);assert(s.ticks>=15);s.engine.running=false;const n=s.ticks;s.advance(1000);assert.equal(s.ticks,n);assert.equal(s.engine.running,false);});
test('fallback frames keep advancing when native animation callbacks are suspended for three minutes',()=>{const s=setup();s.advance(180000);assert(s.ticks>3500);assert(s.context.window.__MWA_BACKGROUND.fallbackFrames>3500);});
test('a healthy native loop never receives additional fallback frames',()=>{const s=setup();s.engine.background=false;s.advance(10000,true);assert.equal(s.context.window.__MWA_BACKGROUND.fallbackFrames,0);assert.equal(s.ticks,500);});
test('idle hidden frames slow down and resume full cadence immediately for work',()=>{
 const s=setup(),guard=s.context.window.__MWA_BACKGROUND;
 guard.setIdle(true);s.advance(3000);
 assert(s.ticks>=14&&s.ticks<=16);
 const idle=s.ticks;guard.setIdle(false);s.advance(1000);
 assert(s.ticks-idle>=19);
});
test('resuming native animation immediately retires the fallback clock',()=>{const s=setup();s.advance(1000);const n=s.context.window.__MWA_BACKGROUND.fallbackFrames;s.advance(2000,true);assert.equal(s.context.window.__MWA_BACKGROUND.fallbackFrames,n);});
test('reinstalling the script cannot stack heartbeat timers or frame wrappers',()=>{const s=setup();const fn=s.engine.frame;s.advance(50);const wrapped=s.engine.frame;vm.runInNewContext(source,s.context);assert.equal(s.intervals.length,1);assert.equal(s.engine.frame,wrapped);assert.notEqual(fn,wrapped);});
test('a foreground lifecycle callback error still releases the background latch',()=>{const s=setup();s.context.window.__AANDM.vm.callGlobal=()=>{throw Error('fixture')};s.advance(1000);assert.equal(s.engine.background,false);assert(s.ticks>10);});
test('replacing the game engine attaches one monitor to the new instance',()=>{const s=setup();s.advance(1000);let ticks=0;const next={running:true,background:false,setBackground(){},frame(){ticks++;}};s.context.window.__AANDM.engine=next;s.advance(1000);assert(ticks>10);});
test('extreme performance foreground throttles 50fps native frames to ~15fps without false fallbacks',()=>{
 const s=setup(),guard=s.context.window.__MWA_BACKGROUND;
 s.engine.background=false;
 guard.pulse(0);
 guard.setExtremePerf(true,false,false);
 const mode=guard.getMode();
 assert.equal(mode.extremePerf,true);
 assert.equal(mode.suspendRender,false);
 assert.equal(mode.taskActive,false);
 s.advance(2000,true);
 assert(s.ticks>=24&&s.ticks<=32,'expected ~12.5-15fps over 2s, got '+s.ticks);
 assert(guard.skippedNativeFrames>=68,'expected skippedNativeFrames>=68, got '+guard.skippedNativeFrames);
 assert.equal(guard.getMode().skippedNativeFrames,guard.skippedNativeFrames);
 assert.equal(guard.fallbackFrames,0);
 assert.equal(guard.nativeFrames,s.ticks);
});
test('skipping an engine update still schedules the next native callback',()=>{
 const s=setup(),bg=s.context.window.__MWA_BACKGROUND,e=s.engine;
 let callback=null;
 e.background=false;
 e.requestFrame=function(){if(!callback)callback=stamp=>{callback=null;e.frame(stamp);};};
 const original=e.frame;
 e.frame=function(stamp){original.call(this,stamp);this.requestFrame();};
 bg.pulse(0);
 bg.setExtremePerf(true,false,false);
 e.requestFrame();
 for(let t=0;t<=120;t++){
   const stamp=t*1000/60;s.setTime(stamp);
   assert(callback,'native callback chain was broken');
   callback(stamp);
   if(t%3===0)bg.pulse(stamp);
 }
 assert.equal(bg.fallbackFrames,0);
 assert(bg.nativeFrames>=29&&bg.nativeFrames<=32);
});
test('extreme performance suspend-render throttles native frames to ~2fps idle and ~4fps task-active',()=>{
 const s=setup(),guard=s.context.window.__MWA_BACKGROUND;
 s.engine.background=false;
 guard.pulse(0);
 guard.setExtremePerf(true,true,false);
 assert.deepEqual(guard.getMode(),{extremePerf:true,suspendRender:true,taskActive:false,skippedNativeFrames:0});
 s.advance(2000,true);
 assert(s.ticks>=4&&s.ticks<=5,'expected ~2fps idle suspend native ticks over 2s, got '+s.ticks);
 assert.equal(guard.fallbackFrames,0);
 assert(guard.skippedNativeFrames>=95,'expected skippedNativeFrames>=95, got '+guard.skippedNativeFrames);
 const idleTicks=s.ticks;
 guard.setExtremePerf(true,true,true);
 assert.equal(guard.getMode().taskActive,true);
 s.advance(2000,true);
 const activeTicks=s.ticks-idleTicks;
 assert(activeTicks>=7&&activeTicks<=9,'expected ~4fps active suspend native ticks over 2s, got '+activeTicks);
 assert.equal(guard.fallbackFrames,0);
});
test('extreme performance suspend-render throttles fallback timer to ~2fps idle and ~4fps task-active',()=>{
 const s=setup(),guard=s.context.window.__MWA_BACKGROUND;
 guard.setExtremePerf(true,true,false);
 s.advance(3000,false);
 assert(s.ticks>=5&&s.ticks<=7,'expected ~2fps idle suspend fallback ticks over 3s, got '+s.ticks);
 assert.equal(guard.fallbackFrames,s.ticks);
 const idleTicks=s.ticks;
 guard.setExtremePerf(true,true,true);
 s.advance(3000,false);
 const activeTicks=s.ticks-idleTicks;
 assert(activeTicks>=11&&activeTicks<=13,'expected ~4fps active suspend fallback ticks over 3s, got '+activeTicks);
 assert.equal(guard.fallbackFrames,s.ticks);
});
test('returning to foreground or disabling extreme performance renders the next frame immediately and restores cadence',()=>{
 const s1=setup(),g1=s1.context.window.__MWA_BACKGROUND;
 s1.engine.background=false;
 g1.pulse(0);
 g1.setExtremePerf(true,true,false);
 s1.advance(20,true);
 assert.equal(s1.ticks,1);
 g1.setExtremePerf(true,false,true);
 s1.advance(20,true);
 assert.equal(s1.ticks,2,'returning to foreground extremePerf must render next native frame immediately');
 s1.advance(2000,true);
 assert(s1.ticks-2>=24&&s1.ticks-2<=32,'foreground extremePerf must restore ~12.5-15fps cadence, got '+(s1.ticks-2));
 assert.equal(g1.fallbackFrames,0);

 const s2=setup(),g2=s2.context.window.__MWA_BACKGROUND;
 g2.setExtremePerf(true,true,false);
 s2.advance(200,false);
 assert.equal(s2.ticks,1);
 g2.setExtremePerf(true,false,true);
 s2.advance(50,false);
 assert.equal(s2.ticks,2,'returning to foreground extremePerf must render next fallback frame immediately');

 const s3=setup(),g3=s3.context.window.__MWA_BACKGROUND;
 s3.engine.background=false;
 g3.pulse(0);
 g3.setExtremePerf(true,true,false);
 s3.advance(20,true);
 assert.equal(s3.ticks,1);
 g3.setExtremePerf(false,false,false);
 assert.equal(g3.getMode().extremePerf,false);
 assert.equal(g3.getMode().suspendRender,false);
 s3.advance(20,true);
 assert.equal(s3.ticks,2,'disabling extremePerf must render next native frame immediately');
 s3.advance(1000,true);
 assert.equal(s3.ticks-2,50,'disabling extremePerf must restore full 50fps native cadence');
});
test('live resolution scales only the matching native frame and restores its descriptor',()=>{
 const s=setup(),window=s.context.window,guard=window.__MWA_BACKGROUND;
 let ratio=1.5, frames=0;
 Object.defineProperty(window,'devicePixelRatio',{configurable:true,get:()=>ratio});
 const original=Object.getOwnPropertyDescriptor(window,'devicePixelRatio').get;
 let canvas={width:0,height:0,clientWidth:640,clientHeight:1136};
 const engine={running:true,background:false,canvas,setBackground(){},root:{},visibleClippingDepth(){return 0;},
   context:{setTransform(){}},frame(stamp){
     const t=((dpr)=>({width:Math.floor(this.canvas.clientWidth*dpr),scaleX:dpr}))(
       [window.devicePixelRatio,this.visibleClippingDepth(this.root)][0]);
     this.canvas.width=t.width;
     this.context.setTransform(t.scaleX,0,0,-t.scaleX,0,0);
     frames++;
   }};
 window.__AANDM.engine=engine;
 guard.setExtremePerf(true,true,false);
 guard.setLowDefinition(true);guard.pulse(0);
 assert.equal(guard.getLowDefinitionStatus(),'active');
 for(let stamp=20;stamp<=1000;stamp+=20){s.setTime(stamp);engine.frame(stamp);}
 assert.equal(frames,50);
 assert.equal(canvas.width,643);
 assert.equal(window.devicePixelRatio,1.5);
 ratio=2;
 s.setTime(1020);engine.frame(1020);
 assert.equal(canvas.width,857);
 assert.equal(window.devicePixelRatio,2);
 const onDraw=engine.context.setTransform;
 engine.context.setTransform=()=>{throw Error('render error');};
 assert.throws(()=>engine.frame(1030),/render error/);
 assert.equal(window.devicePixelRatio,2);
 engine.context.setTransform=onDraw;
 guard.setLowDefinition(false);guard.setExtremePerf(false,false,false);
 assert.strictEqual(Object.getOwnPropertyDescriptor(window,'devicePixelRatio').get,original);
 engine.frame(1040);
 assert.equal(canvas.width,1280);
 assert.equal(guard.getLowDefinitionStatus(),'off');
});
test('live resolution leaves unfamiliar engines at original resolution with native cadence',()=>{
 const s=setup(),guard=s.context.window.__MWA_BACKGROUND;
 s.engine.background=false;guard.setExtremePerf(true,true,false);guard.setLowDefinition(true);
 guard.pulse(0);s.advance(1000,true);
 assert.equal(guard.getLowDefinitionStatus(),'unsupported');
 assert.equal(s.ticks,50);
});
test('bridge idle tick slows to 500ms in extreme performance while active tasks keep 45ms full speed',()=>{
 const bridgeSource=fs.readFileSync(path.join(root,'src/bridge.js'),'utf8').replace('/* @LUA_CORE@ */','""');
 let now=0,ticks=0,extreme=false,nextId=0;
 const intervals=[],timeouts=new Map();
 const lua={execute(code){
  if(code.includes('VERSION or nil'))return '1.1.1';
  if(code.includes('__MWA:tick(')){ticks++;return null;}
  if(code.includes('pending_contract_captures'))return [];
  if(code.includes(':start_'))return true;
  return null;
 }};
 const window={__AANDM:{vm:lua,engine:{hasVisibleBattle:()=>false}},__MWA_BACKGROUND:{getMode:()=>({extremePerf:extreme,suspendRender:false,taskActive:false,skippedNativeFrames:0})}};
 const context={window,performance:{now:()=>now},setInterval:(f,ms)=>intervals.push({f,ms}),setTimeout:(f,ms)=>{const id=++nextId;timeouts.set(id,{f,at:now+ms});return id;},clearTimeout:id=>timeouts.delete(id)};
 vm.runInNewContext(bridgeSource,context);
 const advance=ms=>{for(let t=0;t<ms;t+=10){now+=10;for(const [id,item] of Array.from(timeouts))if(now>=item.at){timeouts.delete(id);item.f();}}};
 assert(intervals.some(i=>i.ms===20),'battle skip interval must stay 20ms');
 ticks=0;advance(2000);
 assert(ticks>=7&&ticks<=9,'normal idle bridge ticks should run at ~250ms, got '+ticks);
 extreme=true;advance(500);
 ticks=0;advance(2000);
 assert(ticks>=3&&ticks<=5,'extremePerf idle bridge ticks should run at ~500ms, got '+ticks);
 context.window.__MWA_APP.start();
 ticks=0;advance(1000);
 assert(ticks>=20,'active auto mode must stay at 45ms full speed under extremePerf, got '+ticks);
 context.window.__MWA_APP.stop();
 context.window.__MWA_APP.runLua('return __MWA:start_daily()');
 ticks=0;advance(1000);
 assert(ticks>=20,'active one-shot task must stay at 45ms full speed under extremePerf, got '+ticks);
});
test('background boost accelerates fallback cadence to ~33Hz and scales hidden DPR to 0.5x cleanly',()=>{
 const s=setup(),window=s.context.window,guard=window.__MWA_BACKGROUND;
 let ratio=2.0,frames=0,canvas={width:0,height:0,clientWidth:640,clientHeight:1136};
 Object.defineProperty(window,'devicePixelRatio',{configurable:true,get:()=>ratio});
 const original=Object.getOwnPropertyDescriptor(window,'devicePixelRatio').get;
 const engine={running:true,background:false,canvas,setBackground(){},root:{},visibleClippingDepth(){return 0;},
   context:{setTransform(){}},frame(stamp){
     const t=((dpr)=>({width:Math.floor(this.canvas.clientWidth*dpr),scaleX:dpr}))(
       [window.devicePixelRatio,this.visibleClippingDepth(this.root)][0]);
     this.canvas.width=t.width;
     this.context.setTransform(t.scaleX,0,0,-t.scaleX,0,0);
     frames++;
   }};
 window.__AANDM.engine=engine;
 guard.pulse(0);
 guard.setExtremePerf(true,true,true);
 guard.setBackgroundBoost(true,true);
 for(let ms=30;ms<=1000;ms+=30){s.setTime(ms);guard.pulse(ms);}
 assert(frames>=28,'expected >=28Hz boosted fallback frames even under extremePerf suspend, got '+frames);
 assert.equal(canvas.width,640,'expected 0.5x DPR (640*2*0.5=640) while bgLowRes is active');
 assert.equal(window.devicePixelRatio,2.0,'external DPR getter must stay untouched');
 guard.setBackgroundBoost(false,false);
 assert.strictEqual(Object.getOwnPropertyDescriptor(window,'devicePixelRatio').get,original);
 engine.frame(1030);
 assert.equal(canvas.width,1280,'restoring foreground must immediately restore full HD canvas width');
});
fs.mkdirSync(path.join(root,'dist'),{recursive:true});
fs.writeFileSync(path.join(root,'dist/background-js-test-results.json'),JSON.stringify({result:'passed',checks,simulatedHiddenSeconds:180},null,2));
