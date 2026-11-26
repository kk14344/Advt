module.exports=({run,test})=>{
run(`
function setup_120()
 setup_119();A.cfg.autoSkipBattle=true
 bag_logic.GetSpaceCount=function()return 50 end
 package.loaded['logic.carnival']=nil
end
function activity_fixture()
 setup_120();A.cfg.dailyActivityRewards=true
 carnival_test={all_config={a={key='a',name='活动任务',carnival_type=2}},info_map={a={step_reward={1},step_flag={1}}},
  stages_reward_mark={a={2}},battle_pass={id=0},first_payment_map={}}
 function carnival_test:UpdateStageRewardMark()end
 function carnival_test:CanTakeReward(conf,step)return true end
 package.loaded['logic.carnival']=carnival_test
 network.event_listener.query_carnival_info_ret=function(msg)end
 network.event_listener.query_battle_pass_ret=function(msg) if msg.id then carnival_test.battle_pass=msg end end
 network.event_listener.query_first_payment_ret=function(msg)end
 network.event_listener.take_carnival_reward_ret=function(msg)
  if msg.result=='success' and carnival_test.stages_reward_mark[msg.key] then carnival_test.stages_reward_mark[msg.key][msg.step]=3 end
 end
 network.event_listener.ret_battle_pass_reward_receive=function(msg)end
 network.event_listener.revice_first_payment_ret=function(msg)end
end
function service_120()
 local p=A._task_pending;if not p or p.response then return end
 local key,payload=next(sent[#sent])
 if key=='query_carnival_info' then respond(p.event,{carnival_info={}})
 elseif key=='query_battle_pass' then respond(p.event,carnival_test.battle_pass)
 elseif key=='query_first_payment' then respond(p.event,{list={}})
 elseif key=='take_carnival_reward' then respond(p.event,{result='success',key=payload.key,step=payload.step})
 elseif key=='battle_pass_reward_receive' then
  local bp=carnival_test.battle_pass
  if payload.is_gift==0 then bp.normal_revice_lv=bp.level else bp.vip_revice_lv=bp.level end
  respond(p.event,{reward_list={}})
 elseif key=='revice_first_payment' then respond(p.event,{result='success'})
 else service_daily_request() end
end
function run_120(limit)
 local result
 for i=1,limit or 500 do A.now=i;clock=1000+i;result=A:daily_step();service_120();if result.done then return result end end
 return result
end
`);
test('battle fast path skips creation and executes the real settlement callback exactly once',`
setup_120();local shown,settled,other=0,0,0
local graphics={DispatchEvent=function(self,name)if name=='show_battle_room' then shown=shown+1 else other=other+1 end end}
package.loaded['logic.graphic']=graphics
A:install_fast_graphics();A:install_fast_graphics()
graphics:DispatchEvent('show_battle_room',1,{},'real_properties',{},true,function()settled=settled+1 end)
A:drain_fast_settlements();A:drain_fast_settlements()
assert(shown==0 and settled==1 and not A.inBattle)
graphics:DispatchEvent('ordinary_event');assert(other==1)
`);
test('turning auto skip off preserves the original battle handler',`
setup_120();A.cfg.autoSkipBattle=false;local shown=0
local graphics={DispatchEvent=function(self,name)if name=='show_battle_room' then shown=shown+1 end end}
package.loaded['logic.graphic']=graphics;A:install_fast_graphics()
graphics:DispatchEvent('show_battle_room',1,{},'',{},true,function()error('must remain in original battle room')end)
assert(shown==1);A.cfg.autoSkipBattle=true
`);
test('instant settlement failures cannot replay the same battle callback',`
setup_120();local tries=0;package.loaded['logic.graphic']={DispatchEvent=function()error('must not open battle room')end}
A:install_fast_graphics()
package.loaded['logic.graphic']:DispatchEvent('show_battle_room',1,{},'',{},true,function()tries=tries+1;error('settlement exception')end)
A:drain_fast_settlements();A:drain_fast_settlements()
assert(tries==1)
package.loaded['logic.graphic']=nil
`);
test('daily first network request is campaign information before ordinary dailies',`
setup_daily_events(0)
A:daily_step();A:daily_step()
assert(#sent==1 and sent[1].query_campaign_info,'campaign must run before query_daily or rewards')
`);
test('battle settlement runs after the complete native response handler without waiting for visibility',`
setup_120();local phase='before';local settled=0
local graphics={DispatchEvent=function()error('battle playback is unnecessary')end}
package.loaded['logic.graphic']=graphics
network.event_listener.dig_block_ret=function(msg)
 phase='partial'
 graphics:DispatchEvent('show_battle_room',1,{},'',{},true,function()assert(phase=='complete');settled=settled+1 end)
 phase='complete'
end
A:install_task_hooks();respond('dig_block_ret',{result='success'})
assert(settled==1 and phase=='complete');A:drain_fast_settlements();assert(settled==1)
package.loaded['logic.graphic']=nil
`);
test('campaign query messages without result advance through buying fighting and conversion',`
setup_daily_events(0)
local result=run_daily_events(500)
assert(result.done and count_event('buy_campaign_overtime')==2 and count_event('battle_campaign')==4)
assert(count_event('convert_campaign_exp')>0 and count_event('expedition_apend_times')==1)
assert(result.message:find('合战与宿命远征阶段结束'))
`);
test('legacy zero budgets cannot block full expedition challenge purchases',`
setup_daily_events(0);campaign_state.status=1
local result=run_daily_events(500)
assert(result.done)
for _,m in ipairs(sent)do if m.expedition_apend_times then assert(m.expedition_apend_times.num==3) end end
assert(count_event('expedition_apend_times')==1 and count_event('expedition_fight')==5)
`);
test('malformed campaign query data never authorizes buying while other daily phases continue',`
setup_daily_events(0);local result
for i=1,300 do
 A.now=i;clock=1000+i;result=A:daily_step();local p=A._task_pending
 if p and not p.response and p.ev_kind=='c_info' then respond(p.event,{}) else service_daily_request() end
 if result.done then break end
end
assert(result.done and count_event('buy_campaign_overtime')==0 and count_event('expedition_apend_times')==1)
`);
test('campaign overtime query supplies the allowance when the info snapshot omits it',`
setup_daily_events(0);campaign_state.limit_count=0
local service=service_daily_request
local result
for i=1,400 do
 A.now=i;clock=1000+i;result=A:daily_step()
 local p=A._task_pending
 if p and not p.response and p.ev_kind=='c_price' then
  respond(p.event,{req_value=20,reward_value=1,limit_count=math.max(0,2-(A.event_activity_state.campaign.buys or 0))})
 else service() end
 if result.done then break end
end
assert(result.done and count_event('buy_campaign_overtime')==2)
`);
test('campaign prefers a genuinely new numeric-status level over an old boss',`
setup_daily_events(0);A:start_daily_events()
local st=A.event_activity_state
st.campaign.queried=true;st.campaign.levels=true;st.campaign.buffs=true;st.campaign.price_queried=true;st.campaign.buy_stop=true
campaign_state.level_info_list={{level_id=9,status=2,is_boss=1},{level_id=4,status=4},{level_id=12,status=1},{level_id=11,status=3}}
A:daily_campaign_step(st);assert(sent[#sent].battle_campaign.level_id==4)
`);
test('daily fills every workshop slot even when its liveness task was claimed already',`
setup_120();A.cfg.dailyTask2=true;mining.max_project_count=3;mining.dig_count=500
start_119();daily.liveness_list={{liveness_id=2,completion_count=1,is_reward=true}}
local result=run_119();assert(result.done and count_event('add_mining_project')==3 and #mining.project_list==3)
`);
test('daily workshop capacity bounds a refill plan without abandoning the remaining slots',`
setup_120();A.cfg.dailyTask2=true;mining.max_project_count=3;mining.dig_count=0;mining.dig_max_count=100
start_119();daily.liveness_list={{liveness_id=2,completion_count=1,is_reward=true}}
local result=run_119();assert(result.done and #mining.project_list==3)
for _,m in ipairs(sent)do if m.use_mining_tool then assert(m.use_mining_tool.num<=5) end end
`);
test('activity rewards accept result-less info and claim only completed earned rewards',`
activity_fixture();start_119();local result=run_120()
assert(result.done and count_event('take_carnival_reward')==1 and A.daily_state.activity_claims==1)
`);
test('activity reward collection never treats a shop or exchange as a free earned reward',`
activity_fixture();carnival_test.all_config.a.carnival_type=11
start_119();local result=run_120();assert(result.done and count_event('take_carnival_reward')==0)
`);
test('activity reward claims require the matching key and step acknowledgement',`
activity_fixture();start_119()
for i=1,20 do A.now=i;A:daily_step();local p=A._task_pending;if p and p.extra=='activity_claim' then break end;service_120() end
assert(A._task_pending and A._task_pending.extra=='activity_claim')
respond('take_carnival_reward_ret',{result='success',key='other',step=1})
assert(not A._task_pending.response)
respond('take_carnival_reward_ret',{result='success',key='a',step=1})
assert(run_120().done and A.daily_state.activity_claims==1)
`);
test('activity reward rejection continues with the next eligible activity',`
activity_fixture();carnival_test.all_config.b={key='b',name='第二活动',carnival_type=2}
carnival_test.stages_reward_mark.b={2};carnival_test.info_map.b={step_reward={1}}
network.event_listener.take_carnival_reward_ret=function(msg)if msg.result=='success' and carnival_test.stages_reward_mark[msg.key] then carnival_test.stages_reward_mark[msg.key][msg.step]=3 end end
start_119();local result;local refused=false
for i=1,200 do
 A.now=i;result=A:daily_step();local p=A._task_pending
 if p and not p.response and p.extra=='activity_claim' and sent[#sent].take_carnival_reward.key=='a' and not refused then
  refused=true
  respond(p.event,{result='bag_full',key='a',step=1})
 else service_120() end
 if result.done then break end
end
assert(result.done and count_event('take_carnival_reward')==3 and A.daily_state.activity_claims==2)
`);
test('activity rewards claim normal and already-owned premium battle pass levels in bulk',`
activity_fixture();carnival_test.stages_reward_mark.a={3}
carnival_test.battle_pass={id=1,level=9,normal_revice_lv=0,vip_revice_lv=0,is_has=1,gift_count=0}
start_119();local result=run_120()
assert(result.done and count_event('battle_pass_reward_receive')==2 and A.daily_state.activity_claims==2)
`);
test('battle pass collection never purchases or claims an unowned premium tier',`
activity_fixture();carnival_test.stages_reward_mark.a={3}
carnival_test.battle_pass={id=1,level=9,normal_revice_lv=0,vip_revice_lv=0,is_has=0,gift_count=0}
start_119();local result=run_120()
assert(result.done and count_event('battle_pass_reward_receive')==1)
for _,m in ipairs(sent)do if m.battle_pass_reward_receive then assert(m.battle_pass_reward_receive.is_gift==0) end end
`);
test('activity checklist switch prevents reward queries and claims',`
activity_fixture();A.cfg.dailyActivityRewards=false;start_119();local result=run_120()
assert(result.done and count_event('query_carnival_info')==0 and count_event('take_carnival_reward')==0)
`);
test('activity rewards never activate a fund that has not been purchased',`
activity_fixture();carnival_test.all_config.a.carnival_type=15
carnival_test.info_map.a.cur_value_multi={0}
start_119();local result=run_120()
assert(result.done and count_event('take_fund_profit')==0)
`);
test('activity rewards collect already activated first-payment daily rewards without purchase',`
activity_fixture();carnival_test.stages_reward_mark.a={3}
carnival_test.first_payment_map={{status=2,price=6,count=0,revice_time=0}}
config_manager.first_payment_config={[6]={count=7}}
time_logic.GetNowDayTime=function()return 1000 end
start_119();local result=run_120()
assert(result.done and count_event('revice_first_payment')==1 and A.daily_state.activity_claims==1)
`);
test('activity rewards revisit goals completed during the same daily run',`
activity_fixture();A.cfg.dailyTask3=true;carnival_test.stages_reward_mark.a={1}
function carnival_test:UpdateStageRewardMark()self.stages_reward_mark.a[1]=server_counts[3]>=1 and (self.info_map.a.taken and 3 or 2) or 1 end
network.event_listener.take_carnival_reward_ret=function(msg)if msg.result=='success' then carnival_test.info_map.a.taken=true;carnival_test.stages_reward_mark.a[1]=3 end end
start_119();daily.liveness_list={{liveness_id=3,completion_count=0,is_reward=false}}
local result=run_120()
assert(result.done and count_event('send_gift')>=1 and count_event('take_carnival_reward')==1)
`);
test('destiny upgrades use the native auto interface',`
reset_growth();A:start_destiny();A:destiny_step();respond('query_weapon_star_info_ret',{})
A:destiny_step();assert(sent[#sent].weapon_star_upgrade.upgrade_type=='auto')
`);
test('native weapon animation cannot start a second automatic upgrade loop',`
setup_120();local events=0
package.loaded['logic.graphic']={DispatchEvent=function()events=events+1 end};A:install_fast_graphics()
A.destiny_state={};A._task_pending={kind='growth_star_upgrade'}
destiny.show_animation=true
package.loaded['logic.graphic']:DispatchEvent('weapon_upgrade_star_success',{})
assert(events==0 and destiny.show_animation==false)
A.destiny_state=nil;A._task_pending=nil;package.loaded['logic.graphic']=nil
`);
test('mining boss challenge consumes existing medals until none remain',`
setup_120();mining.cave_boss_lv=1;resource_counts[6]=3
network.event_listener.query_mining_info_ret=function()end
network.event_listener.challenge_cave_boss_ret=function(msg)if msg.result=='success' then resource_counts[6]=resource_counts[6]-1;mining.cave_boss_lv=mining.cave_boss_lv+1 end end
A:start_mining_boss();local result
for i=1,30 do A.now=i;result=A:mining_boss_step();local p=A._task_pending;if p and not p.response then respond(p.event,{result='success'}) end;if result.done then break end end
assert(result.done and A.mining_boss_state.count==3 and count_event('challenge_cave_boss')==3)
`);
test('mining boss never resends a challenge with an uncertain response',`
setup_120();mining.cave_boss_lv=1;resource_counts[6]=10
network.event_listener.query_mining_info_ret=function()end
network.event_listener.challenge_cave_boss_ret=function()end
A:start_mining_boss();A:mining_boss_step();respond('query_mining_info_ret',{})
A:mining_boss_step();for i=1,10 do A.now=i;A:mining_boss_step() end
assert(count_event('challenge_cave_boss')==1)
A.now=40;local result=A:mining_boss_step();assert(result.wait and not result.done and count_event('challenge_cave_boss')==1 and sent[#sent].query_mining_info)
`);
};
