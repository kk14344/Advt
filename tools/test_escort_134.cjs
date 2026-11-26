const fs=require('fs'),path=require('path'),assert=require('assert');
module.exports=({run,test,toLua})=>{
const primary=JSON.parse(fs.readFileSync(path.join(__dirname,'../tests/fixtures/escort-primary-134.json'),'utf8'));
assert.deepEqual(primary.tables.tramcar.map(x=>x.ID),[1,2,3,4]);
assert.equal(primary.constants.SPECITY_TRAMCAR_ID,4);
assert.equal(primary.tables.tramcar_buy_escort.length,5);
// Execute the downloaded native handler, including the game's published patch
// that adds its missing resource dependency. The server below is an isolated model.
const native=primary.nativeEscortLua.replace('local graphic = require "logic.graphic"','local graphic = require "logic.graphic"\nlocal resource_logic = require "logic.resource"');
run(`
 CART_PRIMARY=${toLua(primary.constants)};CART_PRICES=${toLua(primary.tables.tramcar_buy_escort)}
 function cart_copy(v)if type(v)~='table' then return v end;local o={} for k,x in pairs(v)do o[k]=cart_copy(x)end return o end
 function cart_fixture(id)
  reset_daily_fixture();A._request_probe=nil;A._cancelled_task_pending=nil;A.now=0;clock=1000
  network.session=nil;network.internal_session=nil;network.IsConnected=function()return true end
  package.loaded['logic.user']={query_queue={},user_id='ME'}
  for k,v in pairs(CART_PRIMARY)do constants[k]=v end
  constants.RESOURCE_TYPE.blood_diamond=1
  constants.ESCORT_AUTO_REFRESH_TRAMCAR={TRUE=1,FALSE=2};constants.REFRESH_ROB_TARGET_CD=600
  config_manager.tramcar_buy_escort_config=cart_copy(CART_PRICES)
  for i=1,15 do A.cfg['dailyTask'..i]=false end;A.cfg['dailyTask'..id]=true
  for _,k in ipairs({'dailyRewards','dailyGiftAll','dailyCheckinAll','dailyVipReward','dailyVoidExchange','dailyIncludeMine','dailyGuildBoss','dailyEvents','dailyFreePrayer','dailyFreeAlchemy','dailyVoidChallenge','dailyMarketExchange','dailyRefine','dailyMail','dailyActivityRewards'})do A.cfg[k]=false end
  A.cfg.cartUpgradeMax=true;A.cfg.cartBuyEscort=true;A.cfg.cartPaidTargetRefresh=true;A.cfg.autoSkipBattle=true
  A.cfg.dailyGoldSummon=false;A.cfg.spendBase=0;A.cfg.spendDay=0;A.spend_session=0
  function network:RegisterEvent(name,fn)self.event_listener[name]=fn end
  CART_GRAPHICS={};local g=package.loaded['logic.graphic'] or {};package.loaded['logic.graphic']=g
  package.loaded['util.client_constants']=package.loaded['util.client_constants'] or {}
  package.loaded['util.client_constants'].BATTLE_TYPE={vs_escort_target=77}
  package.loaded['logic.config_manager']=config_manager;package.loaded['util.constants']=constants
  package.loaded['logic.time']=time_logic;package.loaded['logic.resource']=resource_logic
  resource_logic.GetResourceNum=function(self,id)return resource_counts[id] or 0 end
  g.DispatchEvent=function(self,name,...)CART_GRAPHICS[#CART_GRAPHICS+1]={name,...};return true end;g._mwa_fast_dispatch=nil
  escort=assert(load(${toLua(native)}))();package.loaded['logic.escort']=escort;escort:Init()
  A._task_hooks={};A:install_task_hooks()
  CS={info={status=1,tramcar_id=1,auto_refresh_tramcar=2,refresh_rob_target_time=0,escort_beg_time=0,escort_end_time=0},
      times={remain_escort=1,remain_rob=3,refresh_tramcar=0,buy_escort=0,buy_rob=0},targets={},rolls={2,3,4},nextTargets={},
      failures={},drops={},dropUsed={},refreshCount=0}
  resource_counts[constants.RESOURCE_TYPE.blood_diamond]=100000
  sent={};network.Send=function(self,msg)sent[#sent+1]=cart_copy(msg);return true end
  A:start_daily();A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
  daily.liveness_list={{liveness_id=id,completion_count=99,is_reward=true}};daily.activity_receive_list={}
 end
 function cart_service()
  local p=A._task_pending;if not p or p.response or p.queued then return end
  if p.kind~='daily_cart' and p.kind~='request_probe' then return service_daily_request() end
  if p._cart_service_sent_count==#sent then return end;p._cart_service_sent_count=#sent
  local key=p.command;local payload=p.message and p.message[key] or {}
  local msg={result='success'}
  local fail=CS.failures[key]
  if fail then CS.failures[key]=nil;respond(p.event,{result=fail});return end
  if key=='query_escort_info' then msg={escort_info=cart_copy(CS.info)}
  elseif key=='query_escort_times' then msg={escort_times=cart_copy(CS.times)}
  elseif key=='query_rob_target_list' then msg={rob_target_list=cart_copy(CS.targets)}
  elseif key=='escort_refresh_tramcar' then
   local kind=payload.refresh_type;msg.refresh_type=kind
   if kind=='random' then CS.times.refresh_tramcar=CS.times.refresh_tramcar+1;CS.info.tramcar_id=table.remove(CS.rolls,1) or 1
   elseif kind=='specify' then CS.info.tramcar_id=4
   else CS.info.tramcar_id=CS.autoRoll or 1 end
   CS.info.auto_refresh_tramcar=2;msg.tramcar_id=CS.info.tramcar_id
  elseif key=='escort_buy_escort_times' then
   CS.times.buy_escort=CS.times.buy_escort+payload.times;CS.times.remain_escort=CS.times.remain_escort+payload.times
   msg.buy_escort=CS.times.buy_escort;msg.remain_escort=CS.times.remain_escort
  elseif key=='escort_receive_reward' then CS.info.status=1;CS.info.auto_refresh_tramcar=1;msg.escort_info=cart_copy(CS.info)
  elseif key=='escort_start' then
   CS.info.status=2;CS.info.escort_beg_time=clock;CS.info.escort_end_time=clock+2400
   CS.times.remain_escort=CS.times.remain_escort-1;msg.escort_info=cart_copy(CS.info);server_counts[7]=server_counts[7]+1
  elseif key=='escort_rob_target' then
   CS.times.remain_rob=CS.times.remain_rob-1;msg.target_user_id=payload.target_user_id;msg.is_winner=CS.winner~=false
   msg.target_info={};msg.battle_property='';msg.battle_record='';server_counts[14]=server_counts[14]+1
   for _,t in ipairs(CS.targets)do if t.user_id==payload.target_user_id then t.be_robbed_list=t.be_robbed_list or {};t.be_robbed_list[#t.be_robbed_list+1]={user_id='ME',be_robbed_time=clock,result=msg.is_winner and 1 or 2}end end
  elseif key=='escort_refresh_rob_target_list' then
   CS.refreshCount=CS.refreshCount+1
   local nextList=table.remove(CS.nextTargets,1);if nextList then CS.targets=nextList end
   CS.info.refresh_rob_target_time=clock+600
   msg.refresh_rob_target_time=CS.info.refresh_rob_target_time;msg.rob_target_list=cart_copy(CS.targets)
  else error('unexpected cart command '..tostring(key)) end
  if CS.drops[key] and not CS.dropUsed[key] then CS.dropUsed[key]=true;return end
  if p.cart_cost and p.cart_cost>0 then resource_counts[constants.RESOURCE_TYPE.blood_diamond]=resource_counts[constants.RESOURCE_TYPE.blood_diamond]-p.cart_cost end
  respond(p.event,msg)
 end
 function cart_run(limit)
  local r
  for tick=1,limit or 400 do A.now=A.now+1;clock=1000+A.now;r=A:daily_step();cart_service();if r.done then return r end end
  return r
 end
 function cart_count(key)return count_event(key)end
 function escort_followup_run()
  A.harvest_updates=nil;A:start_escort_followup()
  for tick=1,100 do
   A.now=A.now+1;clock=1000+A.now
   local r=A:escort_followup_step();cart_service()
   if r.done then return A:harvest_result(r) end
  end
  error('escort continuation did not finish')
 end
 function cart_until(key,limit)
  for i=1,limit or 100 do A.now=A.now+1;clock=1000+A.now;A:daily_step();if A._task_pending and A._task_pending.command==key then return A._task_pending end;cart_service()end
  error('command not reached '..key)
 end
 function cart_target(uid,level,beg)return {user_id=uid,tramcar_id=level,battle_point=999999999,escort_beg_time=beg or 900,escort_end_time=9000,be_robbed_list={}}end
`);
test('cart refreshes free rolls to level four even after liveness is complete',`
 cart_fixture(7);local r=cart_run();assert(r.done);assert(cart_count('escort_refresh_tramcar')==3 and cart_count('escort_start')==1)
 assert(CS.info.tramcar_id==4 and CS.times.remain_escort==0 and CS.times.buy_escort==0)
 for _,m in ipairs(sent)do if m.escort_refresh_tramcar then assert(m.escort_refresh_tramcar.refresh_type=='random')end end
`);
test('already highest level starts without any refresh or upgrade charge',`
 cart_fixture(7);CS.info.tramcar_id=4;assert(cart_run().done);assert(cart_count('escort_refresh_tramcar')==0 and cart_count('escort_start')==1)
`);
test('free exhaustion uses one native specify upgrade and starts one transport',`
 cart_fixture(7);CS.rolls={1,2,3};assert(cart_run().done)
 assert(cart_count('escort_refresh_tramcar')==4 and CS.info.tramcar_id==4 and cart_count('escort_start')==1,
  'refresh='..cart_count('escort_refresh_tramcar')..' grade='..CS.info.tramcar_id..' start='..cart_count('escort_start'))
 assert(A:spend_total()==constants.ESCORT_REFRESH_TRAMCAR_SPECIFY_COST)
`);
test('upgrade disabled sends the current lower grade after exhausting free refreshes',`
 cart_fixture(7);A.cfg.cartUpgradeMax=false;CS.rolls={1,2,3};assert(cart_run().done)
 assert(CS.info.tramcar_id==3 and cart_count('escort_refresh_tramcar')==3 and A:spend_total()==0)
`);
test('insufficient upgrade funds still starts current grade without paid random rolls',`
 cart_fixture(7);CS.times.refresh_tramcar=3;CS.info.tramcar_id=2;resource_counts[1]=0;assert(cart_run().done)
 assert(cart_count('escort_refresh_tramcar')==0 and cart_count('escort_start')==1 and CS.info.tramcar_id==2)
`);
test('server upgrade rejection falls back rather than abandoning transport',`
 cart_fixture(7);CS.times.refresh_tramcar=3;CS.failures.escort_refresh_tramcar='not_enough_resource';assert(cart_run().done)
 assert(cart_count('escort_refresh_tramcar')==1 and cart_count('escort_start')==1 and CS.info.tramcar_id==1)
`);
test('empty transport quota is purchased in one affordable native batch up to table limit',`
 cart_fixture(7);CS.times.remain_escort=0;CS.times.buy_escort=2;CS.info.tramcar_id=4
 assert(cart_run().done);assert(cart_count('escort_buy_escort_times')==1 and CS.times.buy_escort==5 and CS.times.remain_escort==2)
 local buy;for _,m in ipairs(sent)do if m.escort_buy_escort_times then buy=m.escort_buy_escort_times.times end end;assert(buy==3)
 assert(cart_count('escort_start')==1 and A:spend_total()==1200)
`);
test('purchase uses only affordable attempts and never exceeds available diamonds',`
 cart_fixture(7);CS.times.remain_escort=0;CS.info.tramcar_id=4;resource_counts[1]=850
 assert(cart_run().done);assert(CS.times.buy_escort==2 and resource_counts[1]==50 and cart_count('escort_start')==1)
`);
test('max purchased quota or disabled purchase skips without sending another purchase',`
 cart_fixture(7);CS.times.remain_escort=0;CS.times.buy_escort=5;assert(cart_run().done);assert(cart_count('escort_buy_escort_times')==0)
 cart_fixture(7);CS.times.remain_escort=0;A.cfg.cartBuyEscort=false;assert(cart_run().done);assert(cart_count('escort_buy_escort_times')==0)
`);
test('completed rewards are collected before replenishing an empty transport quota',`
 cart_fixture(7);CS.info.status=3;CS.times.remain_escort=0;CS.info.tramcar_id=4;CS.autoRoll=4
 assert(cart_run().done);local reward,buy
 for i,m in ipairs(sent)do if m.escort_receive_reward then reward=i elseif m.escort_buy_escort_times then buy=i end end
 assert(reward and buy and reward<buy and cart_count('escort_start')==1)
`);
test('scheduled escort collects, applies purchase settings, and records the next arrival',`
 cart_fixture(7);CS.info.status=3;CS.info.tramcar_id=4;CS.autoRoll=4;CS.times.remain_escort=0
 local r=escort_followup_run();assert(r.harvest.escort.account=='ME' and r.harvest.escort.seconds==2399)
 assert(cart_count('escort_receive_reward')==1 and cart_count('escort_buy_escort_times')==1 and cart_count('escort_start')==1)
 for _,request in ipairs(sent)do assert(next(request):match('^escort_') or request.query_escort_info or request.query_escort_times)end
`);
test('scheduled escort claims the last reward and stops at the purchase cap or an empty balance',`
 for _,limit in ipairs({true,false})do
  cart_fixture(7);CS.info.status=3;CS.times.remain_escort=0
  if limit then CS.times.buy_escort=5 else resource_counts[1]=0 end
  local r=escort_followup_run();assert(r.harvest.escort.seconds==0)
  assert(cart_count('escort_receive_reward')==1 and cart_count('escort_buy_escort_times')==0 and cart_count('escort_start')==0)
 end
`);
test('an in-flight cart does not hold up daily or buy unused transport attempts',`
 cart_fixture(7);CS.info.status=2;CS.info.escort_end_time=9000;CS.times.remain_escort=0
 assert(cart_run().done);assert(cart_count('escort_buy_escort_times')==0 and cart_count('escort_start')==0)
`);
test('expired local countdown is queried without pretending server completion',`
 cart_fixture(7);CS.info.status=2;CS.info.escort_end_time=900
 assert(cart_run().done);assert(cart_count('query_escort_info')==2 and cart_count('escort_receive_reward')==0)
`);
test('native automatic selection runs before quality refresh on a new trip',`
 cart_fixture(7);CS.info.auto_refresh_tramcar=1;CS.info.tramcar_id=4;CS.autoRoll=2
 assert(cart_run().done);local first;for _,m in ipairs(sent)do if m.escort_refresh_tramcar then first=m.escort_refresh_tramcar;break end end
 assert(first.refresh_type=='auto' and CS.info.tramcar_id==4)
`);
test('interception prioritizes level four over three and ignores power and lower grades',`
 cart_fixture(14);CS.times.remain_rob=2;CS.targets={cart_target('3',3),cart_target('1',1),cart_target('4',4),cart_target('2',2)}
 assert(cart_run().done);local ids={};for _,m in ipairs(sent)do if m.escort_rob_target then ids[#ids+1]=m.escort_rob_target.target_user_id end end
 assert(#ids==2 and ids[1]=='4' and ids[2]=='3' and cart_count('escort_buy_rob_times')==0)
`);
test('a lost battle still marks that trip attempted and changes targets',`
 cart_fixture(14);CS.times.remain_rob=2;CS.winner=false;CS.targets={cart_target('4',4),cart_target('3',3)}
 assert(cart_run().done);assert(cart_count('escort_rob_target')==2 and CS.times.remain_rob==0 and cart_count('escort_refresh_rob_target_list')==0)
`);
test('target refresh keeps past attempts and pays only while native cooldown is active',`
 cart_fixture(14);CS.times.remain_rob=2;CS.targets={cart_target('4',4)};CS.info.refresh_rob_target_time=8000
 CS.nextTargets={{cart_target('4',4),cart_target('3',3)}}
 assert(cart_run().done);assert(CS.refreshCount==1 and A:spend_total()==constants.ESCORT_REFRESH_ROB_TARGET_LIST_IMMEDIATELY_COST)
 local ids={};for _,m in ipairs(sent)do if m.escort_rob_target then ids[#ids+1]=m.escort_rob_target.target_user_id end end;assert(ids[1]=='4' and ids[2]=='3')
`);
test('free native target refresh is used before paid refresh when cooldown is over',`
 cart_fixture(14);CS.times.remain_rob=1;CS.targets={cart_target('low',2)};CS.nextTargets={{cart_target('high',4)}}
 assert(cart_run().done);assert(CS.refreshCount==1 and A:spend_total()==0)
`);
test('target refresh continues past repeated unsuitable lists without buying interception attempts',`
 cart_fixture(14);CS.times.remain_rob=1;CS.info.refresh_rob_target_time=8000
 for i=1,12 do CS.nextTargets[i]={cart_target('low',1)}end;CS.nextTargets[13]={cart_target('high',3)}
 assert(cart_run(600).done);assert(CS.refreshCount==13 and cart_count('escort_buy_rob_times')==0 and cart_count('escort_rob_target')==1)
`);
test('disabled paid refresh or insufficient balance ends without spending or attacking a lower grade',`
 cart_fixture(14);CS.info.refresh_rob_target_time=8000;A.cfg.cartPaidTargetRefresh=false;CS.targets={cart_target('low',2)}
 assert(cart_run().done);assert(CS.refreshCount==0 and cart_count('escort_rob_target')==0)
 cart_fixture(14);CS.info.refresh_rob_target_time=8000;resource_counts[1]=0;assert(cart_run().done);assert(CS.refreshCount==0)
`);
test('expired, self, already attempted in server history, and exhausted targets are skipped',`
 cart_fixture(14);CS.times.remain_rob=1
 local old=cart_target('old',4);old.escort_end_time=900
 local history=cart_target('history',4);history.be_robbed_list={{user_id='ME',be_robbed_time=950,result=2}}
 local full=cart_target('full',4);for i=1,6 do full.be_robbed_list[i]={user_id='other'..i,be_robbed_time=950}end
 CS.targets={old,history,full,cart_target('ME',4),cart_target('valid',3)}
 assert(cart_run().done);for _,m in ipairs(sent)do if m.escort_rob_target then assert(m.escort_rob_target.target_user_id=='valid')end end
`);
test('rejected target moves to another target without consuming an attempt or retrying the same trip',`
 cart_fixture(14);CS.times.remain_rob=1;CS.targets={cart_target('4',4),cart_target('3',3)};CS.failures.escort_rob_target='has_robbed_this_target'
 assert(cart_run().done);assert(cart_count('escort_rob_target')==2 and CS.times.remain_rob==0)
`);
test('uncertain paid upgrade reconciles highest grade before any resend',`
 cart_fixture(7);CS.times.refresh_tramcar=3;CS.drops.escort_refresh_tramcar=true
 cart_until('escort_refresh_tramcar');cart_service();A.now=A.now+25;clock=1000+A.now
 assert(cart_run().done);assert(cart_count('escort_refresh_tramcar')==1 and cart_count('escort_start')==1 and CS.info.tramcar_id==4)
`);
test('uncertain bulk purchase reconciles bought counter and never spends twice',`
 cart_fixture(7);CS.times.remain_escort=0;CS.info.tramcar_id=4;CS.drops.escort_buy_escort_times=true
 cart_until('escort_buy_escort_times');cart_service();A.now=A.now+25;clock=1000+A.now
 assert(cart_run().done);assert(cart_count('escort_buy_escort_times')==1 and CS.times.buy_escort==5 and cart_count('escort_start')==1)
`);
test('uncertain accepted interception uses remaining quota proof before changing target',`
 cart_fixture(14);CS.times.remain_rob=1;CS.targets={cart_target('4',4)};CS.drops.escort_rob_target=true
 cart_until('escort_rob_target');cart_service();A.now=A.now+25;clock=1000+A.now
 assert(cart_run().done);assert(cart_count('escort_rob_target')==1 and CS.times.remain_rob==0)
`);
test('uncertain target refresh reads targets before considering another paid search',`
 cart_fixture(14);CS.times.remain_rob=1;CS.info.refresh_rob_target_time=8000;CS.nextTargets={{cart_target('4',4)}};CS.drops.escort_refresh_rob_target_list=true
 cart_until('escort_refresh_rob_target_list');cart_service();A.now=A.now+25;clock=1000+A.now
 assert(cart_run().done);assert(CS.refreshCount==1 and cart_count('escort_rob_target')==1)
`);
test('transient cart failure is retried in completion queue after other actions',`
 cart_fixture(7);CS.info.tramcar_id=4;CS.failures.escort_start='failure'
 assert(cart_run(700).done);assert(cart_count('escort_start')==2 and CS.times.remain_escort==0)
`);
test('an unaccepted interception does not mark its target tried and is retried after the daily pass',`
 cart_fixture(14);CS.times.remain_rob=1;CS.targets={cart_target('4',4)};CS.failures.escort_rob_target='failure'
 assert(cart_run(700).done);assert(cart_count('escort_rob_target')==2 and CS.times.remain_rob==0)
`);
test('balance loss while queued resolves the spending guard without hanging',`
 cart_fixture(7);CS.times.refresh_tramcar=3;cart_until('escort_refresh_tramcar')
 local p=A._task_pending;p.blocked=true;p.queued=true;resource_counts[1]=0
 assert(cart_run().done);assert(cart_count('escort_start')==1 and CS.info.tramcar_id==1)
`);
test('a queued free refresh cannot become a paid random refresh after quota changes',`
 cart_fixture(7);cart_until('escort_refresh_tramcar');local p=A._task_pending
 CS.times.refresh_tramcar=3;escort.escort_times.refresh_tramcar=3
 assert(network:Send({escort_refresh_tramcar={refresh_type='random'}})==false)
 assert(p.blocked and p.cart_blocked_result=='refresh_quota_changed')
 assert(cart_run().done);assert(CS.times.refresh_tramcar==3 and CS.info.tramcar_id==4)
`);
test('incomplete authoritative transport state cannot prove that an uncertain start succeeded',`
 cart_fixture(7);CS.info.tramcar_id=4;cart_until('escort_start');local p=A._task_pending
 escort.escort_info={};assert(p.proof.read({})==nil);assert(not p.response)
 A:cancel_task()
`);
test('manual stop cancels queued and continuous target refresh work',`
 cart_fixture(14);CS.info.refresh_rob_target_time=8000
 for i=1,18 do A.now=i;clock=1000+i;A:daily_step();cart_service()end
 local count=#sent;A:cancel_task();for i=1,20 do A:daily_step()end
 assert(#sent==count and not A.daily_state)
`);
test('paid writes outside the authorized cart request remain blocked by daily guard',`
 cart_fixture(7);assert(network:Send({escort_buy_escort_times={times=5}})==false)
 assert(network:Send({escort_buy_rob_times={times=1}})==false)
 assert(network:Send({escort_refresh_tramcar={refresh_type='specify'}})==false)
 assert(network:Send({escort_refresh_rob_target_list={refresh_type='immediately'}})==false)
`);
test('cart interception skips native pre-battle animation through the original settlement callback',`
 cart_fixture(14);CS.times.remain_rob=1;CS.targets={cart_target('4',4)};assert(cart_run().done)
 for _,event in ipairs(CART_GRAPHICS)do assert(not(event[1]=='show_world_sub_panel' and event[2]=='escort_start_rob_panel'))end
 assert(A._fast_settlements and #A._fast_settlements==0)
`);
test('disabling battle skip waits for both cart intro and native battle settlement before the next interception',`
 cart_fixture(14);A.cfg.autoSkipBattle=false;CS.times.remain_rob=2;CS.targets={cart_target('4',4),cart_target('3',3)}
 cart_until('escort_rob_target');cart_service();assert(A._cart_visual_battle=='intro')
 local before=cart_count('escort_rob_target')
 for i=1,10 do A.now=A.now+1;A:daily_step();cart_service()end;assert(cart_count('escort_rob_target')==before)
 local g=package.loaded['logic.graphic'];g:DispatchEvent('hide_world_sub_panel','escort_start_rob_panel')
 local settled=false;g:DispatchEvent('show_battle_room',77,{},'','',true,function()settled=true end)
 assert(A._cart_visual_battle=='battle');for i=1,10 do A.now=A.now+1;A:daily_step();cart_service()end
 assert(cart_count('escort_rob_target')==before)
 local callback=CART_GRAPHICS[#CART_GRAPHICS][7];assert(type(callback)=='function');callback()
 assert(settled and not A._cart_visual_battle)
 A.cfg.autoSkipBattle=true;assert(cart_run().done);assert(cart_count('escort_rob_target')==2)
`);
};
