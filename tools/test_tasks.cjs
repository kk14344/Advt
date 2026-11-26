const fs=require('fs');
const path=require('path');
global.window=global;
global.HTMLDocument=class {};
global.document={querySelectorAll:()=>[],addEventListener:()=>{},readyState:'loading'};
global.MutationObserver=class {observe(){}};
const fengari=require('../tests/vendor/fengari-web.js');
const {lua,lauxlib,lualib,to_luastring,to_jsstring}=fengari;
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
const root=path.resolve(__dirname,'..');
function run(s){
  if(lauxlib.luaL_loadstring(L,to_luastring(s))!==lua.LUA_OK || lua.lua_pcall(L,0,0,0)!==lua.LUA_OK)
    throw Error(to_jsstring(lua.lua_tolstring(L,-1)));
}
function toLua(v){
  if(v==null)return 'nil';if(typeof v==='string')return JSON.stringify(v);
  if(typeof v!=='object')return String(v);
  if(Array.isArray(v))return '{'+v.map(toLua).join(',')+'}';
  return '{'+Object.entries(v).map(([k,x])=>'['+toLua(/^\d+$/.test(k)?Number(k):k)+']='+toLua(x)).join(',')+'}';
}
const tables=JSON.parse(fs.readFileSync(path.join(root,'tests/fixtures/game-tables.json'),'utf8'));
// Compile the complete shipping Lua script as well as the task-focused fixtures.
const fullLua=fs.readFileSync(path.join(root,'dist/automation.generated.lua'),'utf8');
if(lauxlib.luaL_loadstring(L,to_luastring(fullLua))!==lua.LUA_OK)throw Error(to_jsstring(lua.lua_tolstring(L,-1)));
lua.lua_pop(L,1);
const vm=require('vm');
const context={window:{},setInterval:()=>0,clearInterval:()=>{},setTimeout:()=>0,clearTimeout:()=>{},performance:{now:()=>0}};
vm.runInNewContext(fs.readFileSync(path.join(root,'src/AandmAuto.inject.js'),'utf8'),context);
if(context.window.__MWA_APP.snapshot().ready!==false)throw Error('Unloaded game must remain waiting');
const app=context.window.__MWA_APP;
if(app.getConfig().workshopDepth!==150||app.getConfig().workshopMinutes!==120||app.getConfig().guildBossCount!==3)throw Error('numeric defaults are wrong');
if(!app.setOption('workshopDepth','165')||app.getConfig().workshopDepth!==165)throw Error('numeric option was not stored');
if(!app.setOption('guildBossCount',7)||app.getConfig().guildBossCount!==7)throw Error('guild count option was not stored');
if(!app.setOption('autoUseEarthHeart',false)||app.getConfig().autoUseEarthHeart!==false)throw Error('boolean option was not stored');
if(app.getConfig().horizontalStartDepth!==150||!app.setOption('horizontalStartDepth',220)||app.getConfig().horizontalStartDepth!==220)throw Error('selected depth must reach the bridge');
app.setOption('horizontalStartDepth',0);if(app.getConfig().horizontalStartDepth!==1)throw Error('selected depth must be clamped');
if(app.getConfig().dailyAlchemyTier!==1||app.getConfig().dailyTrollLevel!==0||app.getConfig().dailyUndergroundLevel!==0)throw Error('1.44 tier defaults are wrong');
if(!app.setOption('dailyAlchemyTier',3)||app.getConfig().dailyAlchemyTier!==3)throw Error('dailyAlchemyTier option was not stored');
if(!app.setOption('dailyTrollLevel',8)||app.getConfig().dailyTrollLevel!==8)throw Error('dailyTrollLevel option was not stored');
if(!app.setOption('dailyUndergroundLevel',12)||app.getConfig().dailyUndergroundLevel!==12)throw Error('dailyUndergroundLevel option was not stored');
context.window.__AANDM={vm:{execute(){throw Error('client modules not ready')}}};
if(context.window.__MWA_APP.ready()!==false)throw Error('Partially loaded game must remain waiting');
run(`TABLES=${toLua(tables)}
config_manager={}
for name,rows in pairs(TABLES) do
 local conf={} for _,r in ipairs(rows)do conf[r.ID]=r end
 config_manager[name..'_config']=conf
end
config_manager.cultivation_config={}
for _,r in ipairs(TABLES.cultivation)do
 local list=config_manager.cultivation_config[r.cultivation_type] or {}
 list[r.level]=r
 config_manager.cultivation_config[r.cultivation_type]=list
end
constants={RESOURCE_TYPE={blood_diamond=1},ARENA_REFRESH_COST_BLOOD_DIAMOND=60,
 VANITY_MAX_MAZE_ID=6,VANITY_MAZE_STATE={unlock=1,challenge_able=2,challenge_success=3,maze_finish=4}}
client_constants={MINE_STATE={lock=0,ready=1,mining=2,finish=3}}
clock=1000; weekday=7; cave_wday=7
time_logic={Now=function()return clock end,time_zone_offset=28800,
 GetDateInfo=function(self,t) return {wday=cave_wday or 7} end}
utils={getWDay=function()return weekday end}
scene_manager={GetCurrentSceneName=function()return 'world' end}
resource_counts={}
resource_logic={CheckResourceNum=function()return enough_blood~=false end,
 GetResourceNum=function(self,id)return resource_counts[id] or 0 end}
sent={}; originals={}; network={event_listener={}}
function network:Send(msg)sent[#sent+1]=msg end
local names={'query_arena_rival_ret','refresh_arena_rival_ret','challenge_arena_rival_ret',
 'query_vanity_troop_ret','query_vanity_maze_states_ret','get_vanity_mercenary_ret','vanity_sync_troop_ret','vanity_challenge_ret',
 'query_mine_info_ret','mine_start_ret','mine_receive_reward_ret','query_cultivation_info_ret',
 'query_all_mine_rewards_ret','query_resource_list_ret','cultivate_ret'}
for _,name in ipairs(names)do
 network.event_listener[name]=function(msg)
  originals[name]=(originals[name] or 0)+1
  if name=='query_arena_rival_ret' then
   arena.rival_list=msg.rival or {};arena.challenge_num=msg.challenge_num or 0;arena.refresh_time=msg.refresh_time or 2000
  elseif name=='vanity_sync_troop_ret' and msg.result=='success' then
   troop.vanity_troop=troop.change_troop
  elseif name=='query_mine_info_ret' then
   if msg.mine_info_list then mine_logic.mine_info_list=msg.mine_info_list end
  elseif name=='mine_start_ret' and msg.result=='success' and msg.mine_info then
   for i,info in ipairs(mine_logic.mine_info_list)do
    if info.mine_index==msg.mine_info.mine_index then mine_logic.mine_info_list[i]=msg.mine_info end
   end
  elseif name=='query_all_mine_rewards_ret' then mine_logic.all_mine_rewards=msg.all_mine_rewards
  elseif name=='query_resource_list_ret' then resource_counts=msg.stocks or resource_counts
  elseif name=='cultivate_ret' and msg.result=='success' then
   cultivation_logic.cultivation_info[cultivation_logic.selectType]['coefficient'..cultivation_logic.coefficient]=msg.cur_state
  elseif name=='mine_receive_reward_ret' and msg.result=='success' then
   for _,info in ipairs(mine_logic.mine_info_list)do
    if info.mine_index==msg.mine_index then info.status=1;info.mine_level=0;info.end_time=0 end
   end
  elseif name=='query_cultivation_info_ret' and msg.cultivation_states then
   cultivation_logic.cultivation_info=msg.cultivation_states
  end
 end
end
function respond(name,msg)network.event_listener[name](msg)end
arena={rival_list={},challenge_num=20,refresh_time=2000}
function arena:ChallengeRival(pos)network:Send({challenge_arena_rival={rival_pos=pos}})end
function arena:RefreshRival()network:Send({refresh_arena_rival={}})end
troop={vanity_mercenarys_list={},vanity_troop={},vanity_maze_state_list={}}
function troop:GetVanityMercenarys(id)
 for _,m in ipairs(self.vanity_mercenarys_list)do if m.instance_id==id then return m end end
end
function troop:GetVanityTroopProperty()
 local f=self.vanity_formation;f.speed=0
 for _,m in ipairs(f)do f.speed=f.speed+(m.speed or 0) end
end
function troop:QueryVantiyToopInfo()network:Send({query_vanity_troop={}})end
function troop:GetVanityMercenaryByMazeId(id)network:Send({get_vanity_mercenary={maze_id=id}})end
function troop:FightingByMazeId(id)network:Send({vanity_challenge={maze_id=id}})end
mine_logic={mine_info_list={{mine_index=1,status=1},{mine_index=2,status=1},{mine_index=3,status=1}},last_level=1}
function mine_logic:QueryMineInfo()network:Send({query_mine_info={}});return true end
function mine_logic:StartMine(index,level)
 self.last_level=level
 for _,info in ipairs(self.mine_info_list)do
  if info.mine_index==index then info.pending_level=level end
 end
 network:Send({mine_start={mine_index=index,mine_level=level}});return true
end
function mine_logic:MineReceiveReward(index)network:Send({mine_receive_reward={mine_index=index}});return true end
function mine_logic:BattlePointIsFull(index,level,tips)return bp_ok~=false end
function mine_logic:CheckFormation(index)return formation_ok~=false end
function mine_logic:GetMinesStatus(index)
 for _,info in ipairs(self.mine_info_list)do
  if info.mine_index==index then
   if info.status==2 and (info.end_time or 0)>0 and info.end_time<=clock then return 3 end
   return info.status
  end
 end
 return 0
end
function mine_logic:GetMineEndTime(info)return info.end_time or 0 end
cultivation_logic={cultivation_info={}}
function cultivation_logic:SendCultivation(kind,branch)
 self.selectType=kind;self.coefficient=tonumber(branch:sub(-1))
 network:Send({cultivate={cultivation_type=kind,coefficient_type=branch}})
end
package.loaded['util.network']=network;package.loaded['util.utils']=utils
package.loaded['util.client_constants']=client_constants
package.loaded['logic.arena']=arena;package.loaded['logic.troop']=troop
package.loaded['logic.mine']=mine_logic;package.loaded['logic.cultivation']=cultivation_logic
A={stats={wins=0,losses=0},cfg={arenaAllowBloodRefresh=true,spendBudget=360,spendBase=0,spendDay=0,spendAllowCaveBuy=true,reserveTnt=0},now=0}
function A:log(s)end
function A:hide_panel(name)end
__MWA=A
`);
run(fs.readFileSync(path.join(root,'dist/tasks.generated.lua'),'utf8'));
let passed=0, runningRetry130=false;
function test(name,s){if(process.env.AANDM_ONLY_RETRY&&!runningRetry130)return;run(`A:cancel_task();A._cancelled_task_pending=nil;sent={};A.now=0;clock=1000;weekday=7;A.inBattle=false;enough_blood=true;A.spend_session=0;A.cfg.spendBase=0;A.cfg.spendDay=0;`+s);passed++;console.log('PASS '+name);}
test('paid refresh queries new opponents and pays once',`
A:start_arena(2);A:arena_step()
respond('query_arena_rival_ret',{rival={{state=true}},challenge_num=10,refresh_time=2000})
A:arena_step();assert(sent[#sent].refresh_arena_rival,'a round with every rival beaten needs a refresh')
A:arena_step();assert(#sent==2,'duplicate payment while awaiting reply')
respond('refresh_arena_rival_ret',{result='success'})
A:arena_step();assert(sent[#sent].query_arena_rival,'must query after refresh acknowledgement')
respond('query_arena_rival_ret',{rival={{state=false,battle_point=20},{state=false,battle_point=10}},challenge_num=10})
A:arena_step();assert(sent[#sent].challenge_arena_rival.rival_pos==1,'rounds challenge rivals in fixed order')
assert(A.arena_state.count==0,'unacknowledged request must not count')
respond('challenge_arena_rival_ret',{result='success'})
A:arena_step();assert(A.arena_state.count==1 and A.arena_state.cost==60)
assert(originals.refresh_arena_rival_ret==1,'original game handler must run')
`);
test('rounds challenge all nine rivals in order, then refresh for the next round',`
A:start_arena(2);A:arena_step()
local rivals={} for i=1,9 do rivals[i]={state=false,battle_point=100-i} end
respond('query_arena_rival_ret',{rival=rivals,challenge_num=99,refresh_time=2000})
for pos=1,9 do
 A:arena_step();assert(sent[#sent].challenge_arena_rival and sent[#sent].challenge_arena_rival.rival_pos==pos,
  'expected fixed position '..pos..', got '..tostring(sent[#sent].challenge_arena_rival and sent[#sent].challenge_arena_rival.rival_pos))
 respond('challenge_arena_rival_ret',{result='success',rival_pos=pos})
end
A:arena_step();assert(sent[#sent].refresh_arena_rival,'round 2 needs refreshed rivals')
respond('refresh_arena_rival_ret',{result='success'})
A:arena_step();respond('query_arena_rival_ret',{rival=rivals,challenge_num=99,refresh_time=2000})
for pos=1,9 do
 A:arena_step();assert(sent[#sent].challenge_arena_rival.rival_pos==pos,'round 2 position '..pos)
 respond('challenge_arena_rival_ret',{result='success',rival_pos=pos})
end
local r=A:arena_step();assert(r.done and A.arena_state.count==18,tostring(r.message))
`);
test('free-only mode waits, then refreshes automatically',`
A.cfg.arenaAllowBloodRefresh=false;A:start_arena(2);A:arena_step()
respond('query_arena_rival_ret',{rival={{state=true}},challenge_num=10,refresh_time=1010})
local r=A:arena_step();assert(r.wait and r.waitFree and #sent==1,'a finished round must wait for the free refresh')
clock=1011;A:arena_step();assert(sent[#sent].query_arena_rival)
A.cfg.arenaAllowBloodRefresh=true
`);
test('insufficient blood stops before payment',`
A:start_arena(2);A:arena_step()
respond('query_arena_rival_ret',{rival={{state=true}},challenge_num=10})
enough_blood=false;local r=A:arena_step();assert(r.done and #sent==1)
`);
test('explicit arena refresh rejection remains active and retries the unaccepted request',`
A:start_arena(2);A:arena_step()
respond('query_arena_rival_ret',{rival={{state=true}},challenge_num=10})
A:arena_step();respond('refresh_arena_rival_ret',{result='failure'})
local r=A:arena_step();assert(r.wait and not r.done and #sent==3)
`);
test('arena refreshes when challenge tickets run out instead of stopping',`
A:start_arena(2);A:arena_step()
respond('query_arena_rival_ret',{rival={{state=false},{state=false},{state=false}},challenge_num=1,refresh_time=2000})
A:arena_step();assert(sent[#sent].challenge_arena_rival.rival_pos==1)
respond('challenge_arena_rival_ret',{result='success',rival_pos=1})
arena.challenge_num=0
local r=A:arena_step()
assert(not r.done,'must not stop when tickets run out: '..tostring(r.message))
assert(sent[#sent].refresh_arena_rival,'must refresh instead of stopping')
`);
test('arena treats not_enough_count as a refresh trigger',`
A:start_arena(2);A:arena_step()
respond('query_arena_rival_ret',{rival={{state=false}},challenge_num=5,refresh_time=2000})
A:arena_step();assert(sent[#sent].challenge_arena_rival)
respond('challenge_arena_rival_ret',{result='not_enough_count',rival_pos=1})
local r=A:arena_step()
assert(not r.done and not r.error,'not_enough_count is not a fatal error: '..tostring(r.message))
r=A:arena_step()
assert(not r.done,'must not stop after not_enough_count: '..tostring(r.message))
assert(sent[#sent].refresh_arena_rival,'must refresh after not_enough_count')
`);
test('battle playback gates requests only when fast skip is disabled',`
A.cfg.autoSkipBattle=false;
A:start_arena(1);A:arena_step()
respond('query_arena_rival_ret',{rival={{state=false,battle_point=1}},challenge_num=10})
A:arena_step();respond('challenge_arena_rival_ret',{result='success'})
A.inBattle=true;local r=A:arena_step();assert(r.wait and A.arena_state.count==0)
A.inBattle=false;A:arena_step();assert(A.arena_state.count==1)
assert(A:arena_step().done and #sent==2);A.cfg.autoSkipBattle=true
`);
test('read-only request timeout stays active and retries automatically',`
A:start_arena(1);A:arena_step();A.now=21
local r=A:arena_step();assert(r.wait and not r.done and #sent==2)
`);
test('real client skill categories and Friday ban',`
for _,template in pairs(config_manager.mercenary_config)do
 if template.name=='铃彦姬' then
  local tags,adds=A._skill_tags({template_info=template})
  assert(adds and tags['铃铛'])
 elseif template.name=='真田幸村' then
  assert(A._skill_tags({template_info=template})['虚弱'])
 end
end
`);
// Every guide alternative is exercised independently using distinct mock cards.
const plans=JSON.parse(fs.readFileSync(path.join(root,'src/guide-plans.json'),'utf8'));
let planCount=0;
for(const [week,stages] of Object.entries(plans))stages.forEach((alternatives,stage)=>alternatives.forEach((plan,idx)=>{
  const slots=plan.slots.split(' ');
  const effects={'虚弱':3,'暴击':2,'高暴击':2,'纯粹':13,'当前':5,'自总回':10,'恢复':10,'敌回':11,'敌回25':11,'自回':10,'瞎流':3,'有技能':3,'虚弱或封印':3};
  const names={'真田':'真田幸村','铃铛':'铃彦姬'};
  const pool=slots.map((tag,i)=>({instance_id:i+1,battle_num:2,battle_point:1000000,
    speed:plan.speedMax?0:20, template_info:{name:names[tag]||tag,quality:4,genre:tag==='瞎流'?4:0,skill1:9000000+i}}));
  const skills=slots.map((tag,i)=>({ID:9000000+i,effect_type:effects[tag]||3,param:250,
    exhaust_turn:tag==='虚弱'||tag==='虚弱或封印'?1:0,turn_fixed:0}));
  run(`troop.vanity_mercenarys_list=${toLua(pool)}
  for _,s in ipairs(${toLua(skills)})do config_manager.active_skill_config[s.ID]=s end
  local saved=A._guide[${week}][${stage+1}]
  A._guide[${week}][${stage+1}]={${toLua(plan)}}
  local selected,reason=A:match_guide(troop,${week},${stage+1},15)
  A._guide[${week}][${stage+1}]=saved
  assert(selected,'day ${week} stage ${stage+1} plan ${idx+1}: '..tostring(reason))
  assert(#selected.chosen==${slots.length})
  local ids={} for _,m in ipairs(selected.chosen)do assert(not ids[m.id]);ids[m.id]=true end
  `);planCount++;
}));
test('Friday excludes turn-increasing filler cards',`
local sk=9000100;config_manager.active_skill_config[sk]={effect_type=3,turn_fixed=5}
troop.vanity_mercenarys_list={{instance_id=1,battle_num=2,battle_point=1000000,speed=20,
 template_info={name='加回合',quality=4,skill1=sk}}}
local old=A._guide[5][1];A._guide[5][1]={{slots='随意'}}
local plan,reason=A:match_guide(troop,5,1,8);assert(not plan)
A._guide[5][1]=old
`);
test('missing roles, quality, initiative and battle power stop',`
troop.vanity_mercenarys_list={}
assert(not A:match_guide(troop,7,1,15))
local old=A._guide[1][1];A._guide[1][1]={{slots='随意',speedMin=10,bpMin=100}}
troop.vanity_mercenarys_list={{instance_id=1,battle_num=2,battle_point=500,speed=10,template_info={name='普通',quality=4}}}
assert(not A:match_guide(troop,1,1,15),'initiative threshold is strict')
troop.vanity_mercenarys_list[1].speed=11;troop.vanity_mercenarys_list[1].battle_point=100
assert(not A:match_guide(troop,1,1,15),'battle power threshold is strict')
troop.vanity_mercenarys_list[1].battle_point=101;troop.vanity_mercenarys_list[1].template_info.quality=3
assert(not A:match_guide(troop,1,1,15),'purple minimum must apply')
A._guide[1][1]=old
`);
test('virtual map 1 claims the initial pool; map 2 uses guide stage 1',`
troop.vanity_maze_state_list={3,1,1};troop.vanity_troop={0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}
A:start_void();A:void_step();respond('query_vanity_troop_ret',{})
A:void_step();respond('query_vanity_maze_states_ret',{states={3,1,1}})
A:void_step();assert(sent[#sent].get_vanity_mercenary.maze_id==1)
respond('get_vanity_mercenary_ret',{result='success',maze_id=1,maze_state=4})
troop.vanity_maze_state_list={4,2,1}
local old=A._guide[7];A._guide[7]={{{slots='随意'}},{{slots='随意'}}}
troop.vanity_mercenarys_list={{instance_id=123,battle_num=3,battle_point=999,speed=20,
 template_info={name='可用佣兵',quality=4}}}
for i=1,10 do
 A:void_step()
 if A._task_pending and A._task_pending.kind=='void_query' then respond('query_vanity_troop_ret',{}) end
 if A._task_pending and A._task_pending.kind=='void_team' then break end
end
assert(sent[#sent].vanity_sync_troop.troop[1]==123)
assert(troop.change_troop[1]==123,'game callback requires change_troop')
respond('vanity_sync_troop_ret',{result='success'})
A:void_step();A:void_step();assert(sent[#sent].vanity_challenge.maze_id==2)
A._guide[7]=old
`);
test('server weekday change stops before applying the wrong guide',`
A:start_void();weekday=1;local r=A:void_step();assert(r.done and #sent==0)
`);
test('weighted matching retains the strongest valid combination',`
local old=A._guide[7][2];A._guide[7][2]={{slots='暴击 虚弱',bpMin=170}}
config_manager.active_skill_config[9000201]={effect_type=2,exhaust_turn=1}
config_manager.active_skill_config[9000202]={effect_type=2}
config_manager.active_skill_config[9000203]={effect_type=3,exhaust_turn=1}
troop.vanity_mercenarys_list={
 {instance_id=1,battle_num=2,battle_point=100,speed=0,template_info={name='兼任',quality=4,skill1=9000201}},
 {instance_id=2,battle_num=2,battle_point=10,speed=0,template_info={name='低战力暴击',quality=4,skill1=9000202}},
 {instance_id=3,battle_num=2,battle_point=80,speed=0,template_info={name='高战力虚弱',quality=4,skill1=9000203}}}
local plan,reason=A:match_guide(troop,7,2,15)
assert(plan and plan.stats.bp==180,tostring(reason))
A._guide[7][2]=old
`);
const mineBefore=passed;
require('./test_research_mine.cjs')({run,test});
const mineScenarios=passed-mineBefore;
const dailyBefore=passed;
require('./test_daily.cjs')({run,test,toLua});
if(process.env.AANDM_ONLY_CART){
 if(process.env.AANDM_CART_AFTER_PUSH){run("package.loaded['logic.bag']=bag_logic");require('./test_push_132.cjs')({run,test,toLua});}
 const before=passed;require('./test_escort_134.cjs')({run,test,toLua});
 fs.writeFileSync(path.join(root,'dist/escort-test-results.json'),JSON.stringify({result:'passed',scenarios:passed-before,nativeHandlers:true,realAccountUsed:false},null,2));
 console.log('Targeted cart checks: '+(passed-before));process.exit(0);
}
require('./test_daily_119.cjs')({run,test,toLua});
const daily128Before=passed;
require('./test_daily_128.cjs')({run,test,toLua});
const daily128Scenarios=passed-daily128Before;
require('./test_caves_127.cjs')({run,test,toLua});
const daily129Before=passed;
require('./test_daily_129.cjs')({run,test,toLua});
const daily129Scenarios=passed-daily129Before;
const workshopBefore=passed;
require('./test_workshop_guild.cjs')({run,test,toLua});
const growthBefore=passed;
require('./test_growth.cjs')({run,test,toLua});
const growthScenarios=passed-growthBefore;
const miningBandScenarios=process.env.AANDM_ONLY_RETRY?0:require('./test_mining_band.cjs')({run,test,toLua});
const spendBefore=passed;
require('./test_spend_compat.cjs')({run,test,toLua});
const spendScenarios=passed-spendBefore;
const releaseBefore=passed;
require('./test_120.cjs')({run,test,toLua});
const release120Scenarios=passed-releaseBefore;
const release121Before=passed;
require('./test_121.cjs')({run,test,toLua});
const release121Scenarios=passed-release121Before;
const release123Before=passed;
require('./test_void_123.cjs')({run,test,toLua});
require('./test_rune_loop_123.cjs')({run,test,toLua});
const release123Scenarios=passed-release123Before;
require('./test_rune_124.cjs')({run,test,toLua});
require('./test_refine_126.cjs')({run,test,toLua});
const refine142Before=passed;
require('./test_refine_142.cjs')({run,test,toLua});
const refine142Scenarios=passed-refine142Before;
const daily142Before=passed;
require('./test_daily_142.cjs')({run,test,toLua});
const daily142Scenarios=passed-daily142Before;
const retry130Before=passed;
runningRetry130=true;require('./test_retry_130.cjs')({run,test,toLua});
const retry130Scenarios=passed-retry130Before;
if(process.env.AANDM_ONLY_RETRY){console.log("Targeted retry checks: "+passed);process.exit(0);}
const ore131Before=passed;
require('./test_ore_packs_131.cjs')({run,test,toLua});
const ore131Scenarios=passed-ore131Before;
const push132Before=passed;
require('./test_push_132.cjs')({run,test,toLua});
const push132Scenarios=passed-push132Before;
const escort134Before=passed;
require('./test_escort_134.cjs')({run,test,toLua});
const escort134Scenarios=passed-escort134Before;
const release144Before=passed;
require('./test_144.cjs')({run,test,toLua});
const release144Scenarios=passed-release144Before;
const report={workflowTests:passed,release144Scenarios,refine142Scenarios,daily142Scenarios,daily129Scenarios,retry130Scenarios,guideAlternatives:planCount,mineScenarios,dailyScenarios:workshopBefore-dailyBefore,daily128Scenarios,
 ore131Scenarios,
 push132Scenarios,
 escort134Scenarios,
 workshopScenarios:growthBefore-workshopBefore,growthScenarios,
 miningBandScenarios,spendScenarios,release120Scenarios,release121Scenarios,release123Scenarios,result:'passed'};
fs.writeFileSync(path.join(root,'dist/task-test-results.json'),JSON.stringify(report,null,2));
console.log(JSON.stringify(report));
