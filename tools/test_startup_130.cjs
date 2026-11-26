const fs=require('fs'),vm=require('vm'),path=require('path'),assert=require('assert');
const root=path.resolve(__dirname,'..');
function trial(file){
  let time=0,loaded=false,installed=false,heavy=0,bulk=0,options=0,ticks=0,calls=0;
  const intervals=[];
  const lua={execute(code){calls++;
    if(code.length>100000){heavy++;if(!loaded)throw Error('modules still loading');installed=true;return;}
    if(code.includes('VERSION or nil'))return installed?'1.38.0':null;
    if(code.includes('type(package.loaded')||code.includes('local c=require'))return loaded;
    if(code.includes('local options=')){bulk++;return;}
    if(code.includes('__MWA:set(')){options++;return;}
    if(code.includes('__MWA:tick(')){ticks++;return;}
    if(code.includes('pending_contract_captures'))return [];
    return null;
  }};
  const context={window:{__AANDM:{vm:lua,engine:{hasVisibleBattle:()=>false}}},performance:{now:()=>time},setInterval:(f,ms)=>{intervals.push({f,ms});}};
  vm.runInNewContext(fs.readFileSync(file,'utf8'),context);
  function advance(ms){for(let t=0;t<ms;t+=10){time+=10;for(const i of intervals)if(time%i.ms===0)i.f();}}
  const app=context.window.__MWA_APP;
  if(app.setOptions){
    const before=calls;
    app.setOptions({workshopDepth:260,workshopMinutes:240,autoGolem:false,unknownSetting:true});
    assert.equal(calls,before,'initial settings must not query or start Lua while booting');
    assert.equal(app.getConfig().workshopDepth,260);
    assert.equal(app.getConfig().autoGolem,false);
    assert.equal(app.getConfig().unknownSetting,undefined);
  }
  advance(5000);const cold={heavy,calls};loaded=true;advance(3000);
  const hot={heavy,bulk,options,ticks,calls};
  if(app.setOptions){
    app.setOptions({workshopDepth:260,autoGolem:false});
    assert.equal(bulk,hot.bulk,'identical settings need no VM call');
    app.setOptions({workshopDepth:150,autoGolem:true});
    assert.equal(bulk,hot.bulk+1,'change multiple options with one VM call');
  }
  return {cold,hot};
}
const current=trial(path.join(root,'src/AandmAuto.inject.js'));
const old=trial(path.join(root,'../assistant-1.29/src/AandmAuto.inject.js'));
assert.equal(current.cold.heavy,0,'full core must not compile while game modules are incomplete');
assert(current.cold.calls<=24,'initialization probes must be rate limited');
assert.equal(current.hot.heavy,1,'complete core must be installed only once');
assert.equal(current.hot.bulk,1,'initial options must be sent in one VM call');
assert.equal(current.hot.options,0,'initial options must not compile separate Lua snippets');
assert(current.hot.ticks<=15,'paused engine must not do twenty full VM ticks per second');
assert(old.cold.heavy>=90,'baseline must reproduce repeated cold-start compilation');
const report={result:'passed',simulationSeconds:8,coldStartOld:old.cold,coldStartNew:current.cold,readyNew:current.hot,checks:13,
  measurement:'Deterministic offline VM loading workload, not a live-game frame-rate claim'};
fs.writeFileSync(path.join(root,'dist/startup-performance.json'),JSON.stringify(report,null,2));
console.log(JSON.stringify(report));
