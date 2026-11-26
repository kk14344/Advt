module.exports = ({run, test}) => {
run(`
 SHOP_RESOURCE=constants.RESOURCE_TYPE;SHOP_MERCHANT=constants.MERCHANT_TYPE
 SHOP_ACCOUNT=A.harvest_account;SHOP_MODULE=package.loaded['logic.merchant']
 function shop_order(id,kind,product,cost)
  return {order_id=id,merchant_type=kind,need_type1=2,need_num1=cost,
    is_done=false,reward_info={{reward_type=2,param1=product,param2=10}}}
 end
 function shop_fixture(buy,refresh,orders)
  constants.RESOURCE_TYPE={blood_diamond=2};constants.MERCHANT_TYPE={WHITE=2,DARK=1}
  resource_counts[2]=500;A.cfg.dailyMysteryShop=true;A.cfg.mysteryBuyBudget=buy;A.cfg.mysteryRefreshBudget=refresh
  A:set_mystery_products({'2:9'});A.harvest_account=function()return '4521' end
  SHOP={orders=orders,roll=0,reset=10000,balance={buy=buy,refresh=refresh}}
  SHOP.state={running=true,completion_phase='mystery_shop',mystery_shop_enabled=true}
  A.daily_state=SHOP.state
  package.loaded['logic.merchant']={GetResetPrice=function()return 20+SHOP.roll*10 end}
  network.event_listener.query_merchant_ret=function() end
  network.event_listener.merchant_exchange_ret=function() end
  network.event_listener.merchant_reset_ret=function() end
 end
 function shop_snapshot()
  local orders={}
  for _,o in ipairs(SHOP.orders) do
   local copy={};for k,v in pairs(o)do copy[k]=v end
   orders[#orders+1]=copy
  end
  return {result='success',order_list=orders,buy_refresh_times=SHOP.roll,reset_time=SHOP.reset}
 end
 function shop_step() A.now=A.now+0.125;return A:daily_mystery_step(SHOP.state) end
 function shop_quote()
  local result=shop_step()
  assert(A._task_pending and A._task_pending.command=='query_merchant')
  respond('query_merchant_ret',shop_snapshot())
  return shop_step()
 end
 function shop_ack(next,expected)
  local m=next.mysteryBudget;assert(m and m.kind==expected)
  local remaining=SHOP.balance[m.kind]
  local okay=m.operation=='refund' or m.amount<=remaining
  if okay then remaining=remaining+(m.operation=='refund' and m.amount or -m.amount) end
  SHOP.balance[m.kind]=remaining
  assert(A:mystery_budget_ack(m.token,m.operation,okay and 'ok' or 'cap',remaining))
  return shop_step()
 end
`);
test('mystery shop buys selected live products and charges shared request only after persisted approval', `
 shop_fixture(120,0,{shop_order(1,3,9,10),shop_order(4,2,11,5),shop_order(5,2,9,40),shop_order(6,2,9,60)})
 local request=shop_quote()
 assert(count_event('merchant_exchange')==0 and request.mysteryBudget.amount==40)
 shop_ack(request,'buy')
 assert(count_event('merchant_exchange')==1 and sent[#sent].merchant_exchange.order_id==5)
 respond('merchant_exchange_ret',{result='success',order_id=4,merchant_type=2})
 assert(not A._task_pending.response and shop_step().wait)
 respond('merchant_exchange_ret',{result='success',order_id=5,merchant_type=2})
 request=shop_step()
 assert(request.mysteryBudget.amount==60 and count_event('query_merchant')==1)
 shop_ack(request,'buy')
 respond('merchant_exchange_ret',{result='success',order_id=6,merchant_type=2})
 assert(shop_step().done and count_event('merchant_exchange')==2 and SHOP.balance.buy==20)
`);
test('mystery shop keeps refreshing within a separate daily cap with no matching products', `
 shop_fixture(0,58,{shop_order(4,2,11,5)})
 local request=shop_quote()
 for _,price in ipairs({20,30}) do
  assert(request.mysteryBudget.kind=='refresh' and request.mysteryBudget.amount==price)
  shop_ack(request,'refresh');SHOP.roll=SHOP.roll+1
  respond('merchant_reset_ret',shop_snapshot());request=shop_step()
 end
 assert(request.done and not request.partial and SHOP.balance.refresh==8)
 assert(count_event('merchant_reset')==2 and count_event('merchant_exchange')==0 and count_event('query_merchant')==1)
`);
test('mystery shop respects an already used budget after starting another daily run', `
 shop_fixture(60,0,{shop_order(4,2,9,40)});SHOP.balance.buy=39
 local r=shop_ack(shop_quote(),'buy')
 assert(r.done and count_event('merchant_exchange')==0 and SHOP.balance.buy==39)
`);
test('mystery refresh timeout checks a new snapshot without a second paid refresh', `
 shop_fixture(0,100,{shop_order(4,2,11,10)})
 shop_ack(shop_quote(),'refresh')
 A.now=A.now+4.01;local r=shop_step()
 assert(r.wait and count_event('merchant_reset')==1 and count_event('query_merchant')==2)
 SHOP.roll=1;respond('query_merchant_ret',shop_snapshot())
 r=shop_step();assert(r.mysteryBudget.amount==30 and SHOP.balance.refresh==80)
 assert(count_event('merchant_reset')==1)
`);
test('mystery purchase returns the reservation after an explicit refusal', `
 shop_fixture(100,20,{shop_order(4,2,9,40)})
 shop_ack(shop_quote(),'buy')
 respond('merchant_exchange_ret',{result='not_enough_resource',order_id=4,merchant_type=2})
 local r=shop_step();assert(r.mysteryBudget.operation=='refund' and SHOP.balance.buy==60)
 r=shop_ack(r,'buy');assert(r.done and r.partial and SHOP.balance.buy==100)
 assert(count_event('merchant_exchange')==1 and count_event('merchant_reset')==0)
`);
test('mystery purchase refuses mixed currencies and confirms a changed budget before sending', `
 local mixed=shop_order(4,2,9,20);mixed.need_type2=7;mixed.need_num2=3
 shop_fixture(100,0,{mixed,shop_order(5,2,9,40)})
 local r=shop_quote();assert(r.mysteryBudget.amount==40)
 A.cfg.mysteryBuyBudget=0
 r=shop_ack(r,'buy');assert(r.mysteryBudget.operation=='refund' and count_event('merchant_exchange')==0)
 assert(shop_ack(r,'buy').done)
`);
test('mystery shop uses the just-read market snapshot without a redundant query', `
 shop_fixture(80,0,{shop_order(4,2,9,40)})
 SHOP.state.market={snapshot=shop_snapshot(),snapshot_at=A.now}
 local r=shop_step()
 assert(r.mysteryBudget.amount==40 and count_event('query_merchant')==0)
`);
test('mystery shop matches fixed resource and item keys independently', `
 shop_fixture(100,0,{shop_order(1,2,38,10),shop_order(2,2,39,20),shop_order(3,2,25,25)})
 local special=shop_order(4,2,10000048,30);special.reward_info[1].reward_type=1
 SHOP.orders[#SHOP.orders+1]=special
 A:set_mystery_products({'2:39','1:10000048'})
 local r=shop_quote();assert(r.mysteryBudget.amount==20)
 shop_ack(r,'buy')
 assert(sent[#sent].merchant_exchange.order_id==2)
 respond('merchant_exchange_ret',{result='success',order_id=2,merchant_type=2})
 r=shop_step();assert(r.mysteryBudget.amount==30)
 shop_ack(r,'buy')
 assert(sent[#sent].merchant_exchange.order_id==4 and count_event('merchant_exchange')==2)
 respond('merchant_exchange_ret',{result='success',order_id=4,merchant_type=2})
 assert(shop_step().done)
`);
run(`A.cfg.dailyMysteryShop=false;A:set_mystery_products({});A.harvest_account=SHOP_ACCOUNT
constants.RESOURCE_TYPE=SHOP_RESOURCE;constants.MERCHANT_TYPE=SHOP_MERCHANT;package.loaded['logic.merchant']=SHOP_MODULE`);
};
