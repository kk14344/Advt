module.exports=({run,test})=>{
run(`
 D128_R=constants.RESOURCE_TYPE;D128_M=constants.MERCHANT_TYPE
 D128_CONTEXT=A.read_context;D128_GRAPHIC_MODULE=package.loaded['logic.graphic'];D128_GRAPHIC_GLOBAL=graphic
 D128_GOLD_CFG=A.cfg.voidExchangeGoldSoul;D128_PURPLE_CFG=A.cfg.voidExchangePurpleSoul
 D128_START_VOID=A.start_void;D128_VOID_STEP=A.void_step;D128_SHOP=A.start_void_exchange;D128_SHOP_STEP=A.void_exchange_step
 D128_MINE=A.mine_step
 function daily128_fixture(orders)
  setup_119();A.cfg.dailyVoidChallenge=false;A.cfg.dailyMarketExchange=true
  A.cfg.dailyMail=false;A.cfg.dailyGoldSummon=false;A.cfg.dailyRefine=false;A.cfg.dailyActivityRewards=false
  A.inBattle=false;A.cfg.voidExchangeGoldSoul=false;A.cfg.voidExchangePurpleSoul=false
  graphic={DispatchEvent=function()end};package.loaded['logic.graphic']=graphic
  constants.RESOURCE_TYPE={gold_coin=1,blood_diamond=2,copper=7,iron=8,silver=9,tin=10,gold=11,
   diamond=12,titan_iron=13,ruby=14,purple_gem=15,emerald=16,topaz=17,red_soul_crystal=19,
   green_soul_crystal=20,light_soul_crystal=21,dark_soul_crystal=22,inferno_brimstone=23,time_sand=24,
   golem=37,golem2=42,golem3=43,golem4=44,vanity_adventure=94}
  constants.MERCHANT_TYPE={DARK=1,WHITE=2,dark1=3,dark2=4,dark3=5}
  resource_counts={};for id=1,100 do resource_counts[id]=1000 end
  M128_ORDERS=orders or {};M128_BONUS=false;M128_RESET=8000;M128_FAIL={};M128_TRACE={};M128_SNAPSHOT_STALE=false
  M128_CLOSED=false;M128_MSG_CLOSED=false;M128_MAIN=false;M128_POPUP_ON_MAIN=false
  M128_SCENE={HideAllSubPanels=function()M128_CLOSED=true;M128_TRACE[#M128_TRACE+1]='close' end,
   ShowMainSubScene=function()M128_MAIN=true;M128_TRACE[#M128_TRACE+1]='main'
    if M128_POPUP_ON_MAIN then M128_CLOSED=false;M128_MSG_CLOSED=false end end,
   msgbox={Hide=function()M128_MSG_CLOSED=true;M128_TRACE[#M128_TRACE+1]='msgbox' end}}
  A.read_context=function()return {scene=M128_SCENE,sub_name='merchant_sub_scene'}end
  package.loaded['logic.merchant']={}
  network.event_listener.query_merchant_ret=function()end
  network.event_listener.merchant_exchange_ret=function(msg)
   if msg.result~='success' or M128_SNAPSHOT_STALE then return end
   for _,o in ipairs(M128_ORDERS)do if o.order_id==msg.order_id and ((o.merchant_type==2 and msg.merchant_type==2)or(o.merchant_type~=2 and msg.merchant_type==1)) then
    o.is_done=true
    for i=1,2 do local id=o['need_type'..i];if id and id>0 then resource_counts[id]=resource_counts[id]-o['need_num'..i] end end
   end end
  end
  network.event_listener.merchant_soul_chip_ret=function(msg)if msg.result=='success' then M128_BONUS=true end end
  start_119()
 end
 function order128(id,kind,res,num,res2,num2)
  return {order_id=id,merchant_type=kind,is_done=false,need_type1=res,need_num1=num,need_type2=res2 or 0,need_num2=num2 or 0}
 end
 function closed128()return M128_MAIN and M128_CLOSED and M128_MSG_CLOSED end
 function service128()
  local p=A._task_pending;if not p or p.response then return end
  if p.kind~='daily_market' then service_daily_request();return end
  local response={result='success'}
  if p.event=='query_merchant_ret' then
   response={order_list={},ext_soul_chip_flag=M128_BONUS,reset_time=M128_RESET}
   for _,o in ipairs(M128_ORDERS)do local n={};for k,v in pairs(o)do n[k]=v end;response.order_list[#response.order_list+1]=n end
   M128_TRACE[#M128_TRACE+1]='market'
  elseif p.event=='merchant_exchange_ret' then
   local m=sent[#sent].merchant_exchange;response.order_id=m.order_id;response.merchant_type=m.merchant_type
   response.result=M128_FAIL[m.order_id] or 'success'
  end
  respond(p.event,response)
 end
 function run128(limit)
  local result
  for tick=1,limit or 500 do A.now=tick;clock=1000+tick;result=A:daily_step();service128();if result.done then break end end
  return result
 end
`);
test('daily exchanges the three pictured mineral orders and then claims Bonus once',`
 daily128_fixture({order128(1,3,9,128,14,16),order128(2,4,7,24,8,16),order128(3,5,7,96,8,64)})
 local merchant_open=true
 A.read_context=function()return {scene=M128_SCENE,sub_name=merchant_open and 'merchant_sub_scene' or 'main_sub_scene'}end
 graphic.DispatchEvent=function(self,name)
  if name=='hide_world_sub_scene' then assert(M128_BONUS);merchant_open=false end
 end
 local result=run128();assert(result.done and not result.error)
 assert(count_event('merchant_exchange')==3 and count_event('merchant_soul_chip')==1 and M128_BONUS)
 assert(resource_counts[9]==872 and resource_counts[14]==984 and resource_counts[7]==880 and resource_counts[8]==920)
 for _,packet in ipairs(sent)do if packet.merchant_exchange then assert(packet.merchant_exchange.merchant_type==1,'native trade market uses the DARK group id')end end
 assert(closed128() and not merchant_open)
`);
test('the full pictured mining resource category includes all three special statues',`
 daily128_fixture()
 for _,id in ipairs({7,8,9,10,11,12,13,14,15,16,17,19,20,21,22,23,24,37,42,43,44})do
  assert(A:daily_market_order_allowed(order128(1,3,id,1)),'missing mining resource '..id)
 end
 for _,id in ipairs({1,2,3,18,25,26,27,28,29,38,39,40,54,94})do
  assert(not A:daily_market_order_allowed(order128(1,3,id,1)),'non-pictured resource allowed '..id)
 end
`);
test('a mineral plus blood-diamond order is skipped while other eligible orders still complete',`
 daily128_fixture({order128(1,3,9,10,2,1),order128(2,4,7,1),order128(3,5,8,1)})
 local result=run128();assert(result.done and count_event('merchant_exchange')==2 and not M128_BONUS)
 assert(resource_counts[2]==1000 and count_event('merchant_soul_chip')==0)
`);
test('insufficient inventory cannot buy missing resources or force Bonus',`
 daily128_fixture({order128(1,3,9,1001),order128(2,4,7,1),order128(3,5,8,1)})
 local result=run128();assert(result.done and count_event('merchant_exchange')==2)
 assert(count_event('merchant_reset')==0 and count_event('merchant_soul_chip')==0)
`);
test('duplicate resource slots are added before checking the available inventory',`
 daily128_fixture();resource_counts[7]=40
 assert(not A:daily_market_order_allowed(order128(1,3,7,25,7,20)))
 assert(A:daily_market_order_allowed(order128(1,3,7,20,7,20)))
`);
test('completed orders are not exchanged again and their unclaimed Bonus is collected',`
 local o1=order128(1,3,2,100);o1.is_done=true
 local o2=order128(2,4,7,1);o2.is_done=true
 local o3=order128(3,5,8,1);o3.is_done=true
 daily128_fixture({o1,o2,o3});local result=run128()
 assert(result.done and count_event('merchant_exchange')==0 and count_event('merchant_soul_chip')==1)
`);
test('an already claimed Bonus is never requested again',`
 local o=order128(1,3,7,1);o.is_done=true;daily128_fixture({o});M128_BONUS=true
 assert(run128().done and count_event('merchant_soul_chip')==0)
`);
test('mystery-shop mineral orders do not invent a trade-market Bonus',`
 daily128_fixture({order128(1,2,7,1)});assert(run128().done)
 assert(count_event('merchant_exchange')==1 and count_event('merchant_soul_chip')==0)
`);
test('empty or malformed orders cannot consume resources',`
 daily128_fixture({order128(1,3,0,0),order128(2,4,7,-1),order128(3,99,7,1)})
 assert(run128().done and count_event('merchant_exchange')==0 and count_event('merchant_soul_chip')==0)
`);
test('a rejected market order runs after other orders and is retried until accepted',`
 daily128_fixture({order128(1,3,7,1),order128(2,4,8,1),order128(3,5,9,1)});M128_FAIL[1]='not_enough_resource'
 local result;for tick=1,500 do A.now=tick;clock=1000+tick;result=A:daily_step();service128();if M128_ORDERS[3].is_done then M128_FAIL[1]=nil end;if result.done then break end end
 assert(result.done and count_event('merchant_exchange')==4 and M128_BONUS)
 assert(M128_ORDERS[1].is_done and M128_ORDERS[2].is_done and M128_ORDERS[3].is_done)
`);
test('success replies without applied orders remain queued until fresh state confirms execution',`
 daily128_fixture({order128(1,3,7,1)});M128_SNAPSHOT_STALE=true
 local result;for tick=1,500 do A.now=tick;clock=1000+tick;result=A:daily_step();if count_event('merchant_exchange')>=4 then M128_SNAPSHOT_STALE=false end;service128();if result.done then break end end
 assert(result.done and count_event('merchant_exchange')==4 and M128_BONUS)
`);
test('wrong-order success replies cannot acknowledge the current exchange',`
 daily128_fixture({order128(1,3,7,1)});A.now=1;A:daily_step();service128();A.now=2;A:daily_step()
 assert(A._task_pending.event=='merchant_exchange_ret')
 respond('merchant_exchange_ret',{result='success',order_id=999,merchant_type=3})
 assert(not A._task_pending.response)
 service128();assert(run128().done and count_event('merchant_exchange')==1)
`);
test('a definite market failure without echo fields is retried after a fresh query',`
 daily128_fixture({order128(1,3,7,1)});A.now=1;A:daily_step();service128();A.now=2;A:daily_step()
 respond('merchant_exchange_ret',{result='not_enough_resource'})
 assert(A._task_pending.response and run128().done and count_event('merchant_exchange')==2)
`);
test('a lost exchange acknowledgement is rechecked and an applied order is not charged twice',`
 daily128_fixture({order128(1,3,7,1)});A.now=1;A:daily_step();service128();A.now=2;A:daily_step()
 local request=A._task_pending;A.now=30;local result=A:daily_step()
 assert(not result.done and count_event('merchant_exchange')==1)
 M128_ORDERS[1].is_done=true
 for tick=31,200 do A.now=tick;clock=1000+tick;result=A:daily_step();service128();if result.done then break end end
 assert(result.done and count_event('merchant_exchange')==1 and M128_BONUS and closed128())
`);
test('a new market batch is not accidentally consumed by the same daily run',`
 daily128_fixture({order128(1,3,7,1),order128(2,4,8,1)})
 local old=network.event_listener.merchant_exchange_ret
 network.event_listener.merchant_exchange_ret=function(msg)old(msg);M128_RESET=M128_RESET+1 end
 A:install_task_hooks();assert(run128().done and count_event('merchant_exchange')==1)
`);
test('turning market exchange off avoids all market requests but still closes windows',`
 daily128_fixture({order128(1,3,7,1)});A.daily_state.market_exchange=false
 assert(run128().done and count_event('query_merchant')==0 and closed128())
`);
test('daily void challenge precedes void exchange and market completion',`
 daily128_fixture();A:cancel_task();A.cfg.dailyVoidChallenge=true;A.cfg.dailyVoidExchange=true
 A.start_void=function(self)self:cancel_task();self.void_state={};M128_TRACE[#M128_TRACE+1]='void';return true end
 A.void_step=function()return {done=true,message='void done'}end
 A.start_void_exchange=function(self)self:cancel_task();self.void_exchange_state={};M128_TRACE[#M128_TRACE+1]='shop';return true end
 A.void_exchange_step=function()return {done=true,message='shop done'}end
 start_119();assert(run128().done)
 assert(table.concat(M128_TRACE,','):find('void,shop,market',1,true))
 A.start_void=D128_START_VOID;A.void_step=D128_VOID_STEP;A.start_void_exchange=D128_SHOP;A.void_exchange_step=D128_SHOP_STEP
`);
test('optional void challenge can be disabled without disabling other daily phases',`
 daily128_fixture();local calls=0;A.start_void=function()calls=calls+1 end
 assert(run128().done and calls==0);A.start_void=D128_START_VOID
`);
test('only the active daily void challenge may use its existing recruit flow',`
 daily128_fixture();assert(not A:daily_message_is_free('vanity_buy_recruit',{}))
 A.daily_state.child_void_challenge=true;A.void_state={};A._task_pending={kind='void_recruit'}
 assert(A:daily_message_is_free('vanity_buy_recruit',{}));A._task_pending=nil;A.void_state=nil
`);
test('a final mining child closes residual panels instead of bypassing daily completion',`
 daily128_fixture();A.daily_state.child_mine=true;A.daily_state.summary='daily done';A.daily_state.market_exchange=false
 A.mine_step=function()return {done=true,message='mine done'}end
 assert(A:daily_step().done and closed128());A.mine_step=D128_MINE
`);
test('an unexpected child exception cannot silently skip an independently selected market phase',`
 daily128_fixture();A.daily_state.child_mine=true;A.mine_step=function()error('child failed')end
 local result=run128();assert(result.done and result.error and closed128())
 assert(count_event('query_merchant')==1);A.mine_step=D128_MINE
`);
test('manual cancellation cannot trigger market exchange or automatic cleanup afterward',`
 daily128_fixture();A:cancel_task();M128_TRACE={};local result=A:daily_step()
 assert(result.done and #M128_TRACE==0 and count_event('query_merchant')==0)
`);
test('a prompt emitted while returning to the main scene is also closed',`
 daily128_fixture();A.daily_state.market_exchange=false;M128_POPUP_ON_MAIN=true
 assert(run128().done and closed128() and #sent==0)
`);
run(`
 function shop128_choice(gold,purple,goods)
  daily128_fixture();A:cancel_task();A.cfg.voidExchangeGoldSoul=gold;A.cfg.voidExchangePurpleSoul=purple
  resource_counts[94]=100;troop.vanity_good_list=goods or {
   {good_id=1,price=1,max_count=1,cur_count=0,reward_list={{reward_type=2,param1=95}}},
   {good_id=2,price=1,max_count=1,cur_count=0,reward_list={{reward_type=2,param1=96}}},
   {good_id=3,price=1,max_count=1,cur_count=0,reward_list={{reward_type=2,param1=97}}}}
  A:start_void_exchange();A.void_exchange_state.loaded=true;A:void_exchange_step()
  local choice=sent[#sent].vanity_exchange_goods.good_id
  A._task_pending.response={result='success'};A:cancel_task();return choice
 end
`);
test('void exchange preserves the previous defaults of excluding gold and purple soul fragments',`
 assert(shop128_choice(false,false)==3)
`);
test('gold and purple soul-fragment exchange switches operate independently',`
 assert(shop128_choice(true,false)==2);assert(shop128_choice(false,true)==1);assert(shop128_choice(true,true)==1)
`);
test('a disabled fragment in a multi-reward bundle cannot slip past the exchange filter',`
 local goods={
  {good_id=1,price=1,max_count=1,cur_count=0,reward_list={{reward_type=2,param1=7},{reward_type=2,param1=95}}},
  {good_id=2,price=1,max_count=1,cur_count=0,reward_list={{reward_type=2,param1=8}}}}
 assert(shop128_choice(true,false,goods)==2 and shop128_choice(true,true,goods)==1)
`);
test('named item versions of soul fragments follow the same switches',`
 config_manager.item_config=config_manager.item_config or {}
 config_manager.item_config[99001]={name='金色灵魂碎片'};config_manager.item_config[99002]={name='紫色灵魂碎片'}
 local goods={
  {good_id=1,price=1,max_count=1,cur_count=0,reward_list={{reward_type=1,param1=99001}}},
  {good_id=2,price=1,max_count=1,cur_count=0,reward_list={{reward_type=1,param1=99002}}},
  {good_id=3,price=1,max_count=1,cur_count=0,reward_list={{reward_type=2,param1=97}}}}
 assert(shop128_choice(false,false,goods)==3 and shop128_choice(true,false,goods)==1 and shop128_choice(false,true,goods)==2)
`);
run(`constants.RESOURCE_TYPE=D128_R;constants.MERCHANT_TYPE=D128_M;A.read_context=D128_CONTEXT;
 A.cfg.voidExchangeGoldSoul=D128_GOLD_CFG;A.cfg.voidExchangePurpleSoul=D128_PURPLE_CFG;
 package.loaded['logic.graphic']=D128_GRAPHIC_MODULE;graphic=D128_GRAPHIC_GLOBAL;A:cancel_task()`);
};
