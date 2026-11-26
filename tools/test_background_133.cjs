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
test('resuming native animation immediately retires the fallback clock',()=>{const s=setup();s.advance(1000);const n=s.context.window.__MWA_BACKGROUND.fallbackFrames;s.advance(2000,true);assert.equal(s.context.window.__MWA_BACKGROUND.fallbackFrames,n);});
test('reinstalling the script cannot stack heartbeat timers or frame wrappers',()=>{const s=setup();const fn=s.engine.frame;s.advance(50);const wrapped=s.engine.frame;vm.runInNewContext(source,s.context);assert.equal(s.intervals.length,1);assert.equal(s.engine.frame,wrapped);assert.notEqual(fn,wrapped);});
test('a foreground lifecycle callback error still releases the background latch',()=>{const s=setup();s.context.window.__AANDM.vm.callGlobal=()=>{throw Error('fixture')};s.advance(1000);assert.equal(s.engine.background,false);assert(s.ticks>10);});
test('replacing the game engine attaches one monitor to the new instance',()=>{const s=setup();s.advance(1000);let ticks=0;const next={running:true,background:false,setBackground(){},frame(){ticks++;}};s.context.window.__AANDM.engine=next;s.advance(1000);assert(ticks>10);});
fs.writeFileSync(path.join(root,'dist/background-js-test-results.json'),JSON.stringify({result:'passed',checks,simulatedHiddenSeconds:180},null,2));
