module.exports=({run,test})=>{
run(`
function vcopy(value)if type(value)~='table' then return value end local out={}for k,v in pairs(value)do out[k]=vcopy(v)end return out end
V123_GUIDE=vcopy(A._guide)
function vmerc(id,count,kind,speed,bp)
 local sk=990000+id;config_manager.active_skill_config[sk]={effect_type=kind or 2,trigger_chance=100}
 return {instance_id=id,battle_num=count or 10,battle_point=bp or 1000,speed=speed or 0,defense=0,
  template_info={name='佣兵'..id,quality=4,skill1=sk}}
end
function setup_v123(week,fights)
 A:cancel_task();A._cancelled_task_pending=nil;A.now=0;clock=1000;weekday=week or 7;A.inBattle=false;sent={};A.stats.wins=0;A.stats.losses=0
 A.cfg.autoSkipBattle=true;resource_counts={[1]=10000};function resource_logic:GetResourceNum(id)return resource_counts[id] or 0 end
 config_manager.active_skill_config=config_manager.active_skill_config or {};config_manager.monster_config={}
 config_manager.vanity_maze_config={[weekday]={}};config_manager.vanity_buy_other_pay_config={[weekday]={recruit_num=6,consume='30|70|100|100|100|100'}}
 fights=fights or 2;A._guide[weekday]={}
 for i=1,fights do A._guide[weekday][i]={{slots='随意'}}end
 VS={last=fights+1,states={4,2},pool={},left=6,order={},grants={},paid=0}
 for i=3,VS.last do VS.states[i]=1 end
 for i=1,VS.last do config_manager.vanity_maze_config[weekday][i]={week=weekday,map_id=i,debut_num=i==1 and 0 or 3,boss_id=41000+i}end
 troop={vanity_troop={0,0,0},vanity_mercenarys_list={},vanity_maze_state_list={},reduce_search_times=6}
 function troop:GetVanityMercenarys(id)for _,m in ipairs(self.vanity_mercenarys_list)do if m.instance_id==id then return m end end end
 function troop:GetVanityTroopProperty()
  local f=self.vanity_formation;f.speed=0;f.defense=0;f.dodge=0;f.authority=0
  for _,m in ipairs(f)do for _,key in ipairs({'speed','defense','dodge','authority'})do f[key]=f[key]+(m[key] or 0)end end
 end
 function troop:QueryVantiyToopInfo()network:Send({query_vanity_troop={}})end
 function troop:GetVanityMercenaryByMazeId(id)network:Send({get_vanity_mercenary={maze_id=id}})end
 function troop:FightingByMazeId(id)network:Send({vanity_challenge={maze_id=id}})end
 package.loaded['logic.troop']=troop
 VRUNE={rune_equipment_list={},GenerateRuneListPropertys=function()return VS.effects or {mine_property={},enemy_property={}}end,
  GetRuneEquipmentList=function(self)return self.rune_equipment_list end};package.loaded['logic.rune']=VRUNE
 network.event_listener.query_vanity_troop_ret=function(msg)troop.vanity_mercenarys_list=vcopy(msg.mercenarys or {});troop.reduce_search_times=msg.reduce_search_times;troop.vanity_troop=vcopy(msg.troop or {0,0,0})end
 network.event_listener.query_vanity_maze_states_ret=function(msg)troop.vanity_maze_state_list=vcopy(msg.states or {})end
 network.event_listener.query_rune_info_ret=function()end
 network.event_listener.vanity_buy_recruit_ret=function(msg)
  if msg.result=='success' then troop.reduce_search_times=msg.reduce_search_times;for _,m in ipairs(msg.mercenary_list or {})do troop.vanity_mercenarys_list[#troop.vanity_mercenarys_list+1]=vcopy(m)end end
 end
 network.event_listener.get_vanity_mercenary_ret=function(msg)
  if msg.result=='success' then troop.vanity_maze_state_list[msg.maze_id]=msg.maze_state;if troop.vanity_maze_state_list[msg.maze_id+1] then troop.vanity_maze_state_list[msg.maze_id+1]=2 end end
 end
 network.event_listener.vanity_sync_troop_ret=function(msg)if msg.result=='success' then troop.vanity_troop=vcopy(troop.change_troop)end end
 network.event_listener.vanity_challenge_ret=function(msg)
  if msg.result=='success' or msg.result=='battle_failure' then
   for _,id in ipairs(troop.vanity_troop)do local m=troop:GetVanityMercenarys(id);if m then m.battle_num=m.battle_num-1 end end
   if msg.result=='success' then troop.vanity_maze_state_list[msg.maze_id]=msg.maze_id==VS.last and 4 or 3 end
  end
 end
end
function service_v123()
 local p=A._task_pending;if not p or p.response then return end
 local key,payload=next(sent[#sent])
 if key=='query_vanity_troop' then respond(p.event,{mercenarys=vcopy(VS.pool),troop={0,0,0},reduce_search_times=VS.left})
 elseif key=='query_vanity_maze_states' then respond(p.event,{states=vcopy(VS.states),finish=VS.states[VS.last]==4})
 elseif key=='query_rune_info' then respond(p.event,{result='success'})
 elseif key=='vanity_sync_troop' then respond(p.event,{result='success'})
 elseif key=='get_vanity_mercenary' then
  local id=payload.maze_id;VS.order[#VS.order+1]='claim:'..id;assert(VS.states[id]==3,'claim only earned rewards')
  VS.states[id]=4;if id<VS.last and not(VS.no_reset and id==VS.last-1 and VS.states[VS.last]==4)then VS.states[id+1]=2 end
  respond(p.event,{result='success',maze_id=id,maze_state=4,mercenary_list={}})
 elseif key=='vanity_buy_recruit' then
  VS.paid=VS.paid+1;VS.order[#VS.order+1]='recruit'
  local add=VS.grants[VS.paid] or {}
  if VS.reject_recruit then respond(p.event,{result='not_enough_blood'});return end
  local prices={30,70,100,100,100,100};resource_counts[1]=resource_counts[1]-prices[7-VS.left];VS.left=VS.left-1
  for _,m in ipairs(add)do VS.pool[#VS.pool+1]=vcopy(m)end
  respond(p.event,{result='success',reduce_search_times=VS.left,mercenary_list=add})
 elseif key=='vanity_challenge' then
  local id=payload.maze_id
  if VS.require_claim and id==VS.last and VS.states[id-1]==3 then respond(p.event,{result='error_maze_state',maze_id=id});return end
  VS.order[#VS.order+1]='fight:'..id
  local result=VS.fail_battles and 'battle_failure' or 'success'
  for _,card in ipairs(troop.vanity_troop)do for _,m in ipairs(VS.pool)do if m.instance_id==card then assert(m.battle_num>0);m.battle_num=m.battle_num-1 end end end
  if result=='success' then VS.states[id]=id==VS.last and 4 or 3 end
  respond(p.event,{result=result,maze_id=id})
 else error('unexpected void request '..key)end
end
function run_v123(limit)
 local result
 for i=1,limit or 2000 do A.now=i*.05;clock=1000+i*.05;result=A:void_step();service_v123();if result.done then return result end end
 error('void route did not terminate')
end
`);
test('void preflights the full route then fights final twice around the delayed penultimate claim',`
setup_v123(7,3);VS.pool={vmerc(1,10)};A:start_void();local r=run_v123()
assert(r.done and not r.error and VS.paid==0)
assert(table.concat(VS.order,',')=='fight:2,claim:2,fight:3,fight:4,claim:3,fight:4',table.concat(VS.order,','))
assert(A.void_state.final_wins==2)
`);
test('void buys one recruitment and reassesses before making another purchase',`
setup_v123();VS.pool={vmerc(1,1)};VS.grants={{vmerc(2,1)},{vmerc(3,1)}};A:start_void();local r=run_v123()
assert(r.done and VS.paid==2 and resource_counts[1]==9900)
assert(VS.order[1]=='recruit' and VS.order[2]=='recruit' and VS.order[3]=='fight:2')
`);
test('void never buys recruitment when the existing cards cover all fights and replay',`
setup_v123();VS.pool={vmerc(1,3)};A:start_void();local r=run_v123();assert(r.done and VS.paid==0)
`);
test('void reserves battle counts across stages instead of checking each stage independently',`
setup_v123();VS.pool={vmerc(1,2)};VS.grants={{vmerc(2,1)}};A:start_void();run_v123();assert(VS.paid==1)
`);
test('void spends nothing if recruitment price information is missing',`
setup_v123();VS.pool={vmerc(1,1)};config_manager.vanity_buy_other_pay_config={}
A:start_void();local r=run_v123();assert(r.done and r.error and VS.paid==0)
`);
test('void stops recruitment at the real remaining allowance',`
setup_v123();VS.pool={};VS.left=0;A:start_void();local r=run_v123();assert(r.done and r.partial and VS.paid==0)
`);
test('void checks the actual blood balance before recruitment',`
setup_v123();VS.pool={vmerc(1,1)};resource_counts[1]=29;A:start_void();local r=run_v123();assert(r.done and VS.paid==0)
`);
test('void recruitment rejection is not retried',`
setup_v123();VS.pool={vmerc(1,1)};VS.reject_recruit=true;A:start_void();local r=run_v123();assert(r.done and r.error and VS.paid==1)
`);
test('void does not trust a client-only final reset when the server keeps it finished',`
setup_v123();VS.pool={vmerc(1,3)};VS.no_reset=true;A:start_void();local r=run_v123()
assert(r.done and r.partial and table.concat(VS.order,',')=='fight:2,fight:3,claim:2')
`);
test('void falls back to normal completion if the server requires claiming before final',`
setup_v123();VS.pool={vmerc(1,3)};VS.require_claim=true;A:start_void();local r=run_v123()
assert(r.done and not r.error and table.concat(VS.order,',')=='fight:2,claim:2,fight:3')
`);
test('void can resume after the first final win with the penultimate reward still held',`
setup_v123();VS.states={4,3,4};VS.pool={vmerc(1,1)};A:start_void();local r=run_v123()
assert(r.done and table.concat(VS.order,',')=='claim:2,fight:3')
`);
test('finished void runs cannot recruit or farm another replay on restart',`
setup_v123();VS.states={4,4,4};VS.pool={};A:start_void();local r=run_v123();assert(r.done and VS.paid==0 and #VS.order==0)
`);
test('a void request timeout preserves the uncertain mutation without retrying',`
setup_v123();VS.pool={vmerc(1,1)};A:start_void()
for i=1,50 do A.now=i*.05;local r=A:void_step();if A._task_pending and A._task_pending.kind=='void_recruit'then break end;service_v123()end
assert(A._task_pending.kind=='void_recruit');local count=#sent;A.now=30
local r=A:void_step();assert(r.wait and not r.done and #sent==count+1 and sent[#sent].query_vanity_troop)
`);
test('a reward acknowledgement for a different maze cannot advance the route',`
setup_v123();VS.states={3,1,1};VS.pool={vmerc(1,3)};A:start_void()
for i=1,10 do A:void_step();if A._task_pending and A._task_pending.kind=='void_claim'then break end;service_v123()end
respond('get_vanity_mercenary_ret',{result='success',maze_id=2,maze_state=4})
assert(A._task_pending and not A._task_pending.response)
`);
test('joint assignment reserves a unique later-stage card instead of spending it as filler',`
setup_v123();A._guide[7]={{{slots='随意'}},{{slots='纯粹'}}}
VS.pool={vmerc(1,2,13,0,100000),vmerc(2,1,2,0,1)}
A:start_void();local r=run_v123();assert(r.done and not r.error and VS.paid==0)
`);
test('joint assignment never uses the same mercenary twice in one formation',`
setup_v123();A._guide[7]={{{slots='随意 随意'}},{{slots='随意 随意'}}};VS.pool={vmerc(1,10)};VS.left=0
A:start_void();local r=run_v123();assert(r.done and r.partial and #VS.order==0)
`);
test('equipped initiative runes can meet a guide threshold without paid recruitment',`
setup_v123();A._guide[7]={{{slots='随意',speedMin=10}},{{slots='随意'}}}
VS.pool={vmerc(1,3,2,9)};VS.effects={mine_property={speed=2},enemy_property={}}
A:start_void();local r=run_v123();assert(r.done and not r.error and VS.paid==0)
`);
test('enemy initiative reduction is also considered for relative initiative',`
setup_v123();A._guide[7]={{{slots='随意',speedMin=10}},{{slots='随意'}}}
VS.pool={vmerc(1,3,2,9)};VS.effects={mine_property={},enemy_property={speed=-2}}
config_manager.monster_config={[41002]={ID=41002,speed=10}}
A:start_void();assert(run_v123().done and VS.paid==0)
`);
test('initiative runes cannot accidentally satisfy a guide requiring the player to move later',`
setup_v123();A._guide[7]={{{slots='随意',speedMax=10}},{{slots='随意'}}}
VS.pool={vmerc(1,3,2,9)};VS.effects={mine_property={speed=2},enemy_property={}};VS.left=0
A:start_void();local r=run_v123();assert(r.done and r.partial and #VS.order==0)
`);
test('the permanent-seal boss requires initiative even if the old guide only listed power',`
setup_v123(6);VS.pool={vmerc(1,3,2,0)};VS.left=0
config_manager.vanity_maze_config[6][2].boss_id=41015;config_manager.monster_config={[41015]={ID=41015,speed=0}}
A:start_void();local r=run_v123();assert(r.done and r.partial and #VS.order==0)
`);
test('native formation properties are restored after full-route assessment',`
setup_v123();VS.pool={vmerc(1,3)};troop.vanity_mercenarys_list=vcopy(VS.pool)
local saved={marker=true};troop.vanity_formation=saved
local search=A:void_plan_begin(troop,{{maze=2,phase='normal',capacity=3}},7)
local result=A:void_plan_next(troop,search);assert(result and troop.vanity_formation==saved and saved.marker)
`);
test('all seven weekdays use their own final and penultimate maze indices',`
for day,count in ipairs({3,5,2,2,4,2,2})do
 setup_v123(day,count);VS.pool={vmerc(1,count+1)};A:start_void();local r=run_v123()
 assert(r.done and not r.error and A.void_state.final_wins==2 and VS.paid==0)
 assert(VS.order[#VS.order]=='fight:'..(count+1) and VS.order[#VS.order-1]=='claim:'..count)
end
`);
test('a partial maze snapshot cannot authorize recruitment or challenge',`
setup_v123();VS.states={4};VS.pool={};A:start_void();local r=run_v123()
assert(r.done and r.error and VS.paid==0 and #VS.order==0)
`);
test('missing guide alternatives cannot trigger recruitment',`
setup_v123();A._guide[7][1]={};VS.pool={vmerc(1,3)};A:start_void();local r=run_v123()
assert(r.done and r.error and VS.paid==0)
`);
test('turn-adding runes invalidate Friday timing plans without spending blood',`
setup_v123(5);VS.pool={vmerc(1,5)}
VRUNE.rune_equipment_list={{template_info={skill_id=999}}};config_manager.active_skill_config[999]={turn_fixed=1}
A:start_void();local r=run_v123();assert(r.done and r.error and VS.paid==0 and #VS.order==0)
`);
test('permanent seal in live boss skills adds a first-strike requirement',`
setup_v123(7);VS.pool={vmerc(1,3,2,0)};VS.left=0
config_manager.vanity_maze_config[7][2].boss_id=41019
config_manager.monster_config={[41019]={ID=41019,speed=0,skill='998'}};config_manager.active_skill_config[998]={seal_turn=99}
A:start_void();local r=run_v123();assert(r.done and r.partial and #VS.order==0)
`);
test('enemy speed debuffs cannot manufacture negative enemy initiative for a first-strike plan',`
setup_v123();A._guide[7]={{{slots='随意',speedMin=0}},{{slots='随意'}}};VS.pool={vmerc(1,3,2,0)};VS.left=0
VS.effects={mine_property={},enemy_property={speed=-10}};config_manager.monster_config={[41002]={ID=41002,speed=0}}
A:start_void();local r=run_v123();assert(r.done and r.partial and #VS.order==0)
`);
run(`for week,guide in pairs(V123_GUIDE)do A._guide[week]=guide end`);
};
