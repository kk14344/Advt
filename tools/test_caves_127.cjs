module.exports=({run,test})=>{
run(`
 C127_BUY_LIMITS=constants.CAVE_DAILY_BUY_CHALLENGE_NUM;C127_BUY_PRICES=constants.CAVE_DAILY_BUY_PRICE
 function caves127(opened,count,buy)
  setup_119();A.cfg.dailyTask9=true;A.cfg.dailyTask10=true;A.cfg.dailyMail=false
  A.cfg.dailyGoldSummon=false;A.cfg.dailyActivityRewards=false;A.cfg.dailyRefine=false
  A.cfg.spendBudget=-1;mining.cave_config_info={[7]=opened};cave_wday=7
  mining.cave_levels={};mining.cave_challenge_nums={};mining.cave_buy_challenge_nums={}
  config_manager.mining_event_config={};mining.dig_count=1000;mining.dig_max_count=1250
  resource_counts[1]=100000;resource_counts[7]=1000
  constants.CAVE_DAILY_BUY_CHALLENGE_NUM={};constants.CAVE_DAILY_BUY_PRICE={}
  for kind=1,5 do
   mining.cave_levels[kind]=2;mining.cave_challenge_nums[kind]=count or 1
   mining.cave_buy_challenge_nums[kind]=buy or 1
   constants.CAVE_DAILY_BUY_CHALLENGE_NUM[kind]=buy or 1
   constants.CAVE_DAILY_BUY_PRICE[kind]={10,10,10,10,10,10}
   config_manager.mining_event_config[kind]={
    {cave_type=kind,level=1,bp_limit=0,pickaxe_count=1,name='entry'},
    {cave_type=kind,level=2,bp_limit=0,pickaxe_count=1,name='highest'}}
  end
  start_119()
 end
 function cave127_counts()
  local buys,fights={},{}
  for _,m in ipairs(sent)do
   if m.buy_cave_challenge then local k=m.buy_cave_challenge.cave_type;buys[k]=(buys[k] or 0)+1 end
   if m.solve_cave_event then local k=m.solve_cave_event.cave_type;fights[k]=(fights[k] or 0)+1 end
  end
  return buys,fights
 end
`);
test('daily buys and exhausts all three open troll projects including the gem shop',`
 caves127({3,4,5},1,2);local result=run_119(700);assert(result.done)
 local buys,fights=cave127_counts()
 for k=3,5 do assert(buys[k]==2 and fights[k]==3 and mining.cave_challenge_nums[k]==0,'missing troll project '..k)end
 assert(not buys[1] and not fights[2])
`);
test('daily buys and exhausts both open underground projects',`
 caves127({1,2},1,2);local result=run_119(500);assert(result.done)
 local buys,fights=cave127_counts()
 for k=1,2 do assert(buys[k]==2 and fights[k]==3 and mining.cave_challenge_nums[k]==0)end
 assert(not buys[3])
`);
test('closed projects are not purchased or challenged and duplicate open entries are deduplicated',`
 caves127({1,3,5,3},1,1);assert(#A:daily_cave_kinds(9)==2)
 local result=run_119(500);assert(result.done);local buys,fights=cave127_counts()
 assert(buys[1]==1 and buys[3]==1 and buys[5]==1 and not buys[2] and not buys[4])
 assert(fights[1]==2 and fights[3]==2 and fights[5]==2 and not fights[4])
`);
test('claimed or absent liveness rows cannot suppress open cave projects',`
 caves127({1,2,3,4,5},1,1);daily.liveness_list={}
 local result=run_119(700);assert(result.done);local buys,fights=cave127_counts()
 for k=1,5 do assert(buys[k]==1 and fights[k]==2)end
`);
test('a failure in one troll project leaves the other two playable',`
 caves127({3,4,5},1,1)
 local old=network.event_listener.solve_cave_event_ret
 network.event_listener.solve_cave_event_ret=function(msg)if msg.cave_type==3 then msg.result='not_open' else old(msg)end end
 A:install_task_hooks();local result=run_119(700);assert(not result.done)
 local buys,fights=cave127_counts();assert(fights[3]>2 and fights[4]==2 and fights[5]==2)
`);
test('battle defeat downgrades only that project and still consumes all remaining playable attempts',`
 caves127({3,4,5},1,1)
 local old=network.event_listener.solve_cave_event_ret
 network.event_listener.solve_cave_event_ret=function(msg)
  if msg.cave_type==3 and msg.cave_lv==2 then msg.is_winner=false else old(msg)end
 end
 A:install_task_hooks();local result=run_119(700);assert(result.done)
 local buys,fights=cave127_counts();assert(fights[3]==3 and fights[4]==2 and fights[5]==2)
 local high=0;for _,m in ipairs(sent)do if m.solve_cave_event and m.solve_cave_event.cave_type==3 and m.solve_cave_event.cave_lv==2 then high=high+1 end end
 assert(high==1 and mining.cave_challenge_nums[3]==0)
`);
test('unchanged counts remain queued without blocking other projects until manually stopped',`
 caves127({3,4,5},1,0);local old=network.event_listener.solve_cave_event_ret
 network.event_listener.solve_cave_event_ret=function(msg)if msg.cave_type~=3 then old(msg)end end
 A:install_task_hooks();local result=run_119(500);assert(not result.done)
 local buys,fights=cave127_counts();assert(fights[3]>3 and fights[4]==1 and fights[5]==1)
 A:cancel_task();local count=#sent;A.now=1000;assert(A:daily_step().done and #sent==count)
`);
test('cave execution follows remaining attempts even when liveness does not advance',`
 caves127({3,4,5},4,1);daily.liveness_list={{liveness_id=9,completion_count=0,is_reward=false}}
 local old=network.event_listener.solve_cave_event_ret
 network.event_listener.solve_cave_event_ret=function(msg)old(msg);server_counts[9]=0 end
 A:install_task_hooks();local result=run_119(700);assert(result.done)
 local buys,fights=cave127_counts();for k=3,5 do assert(fights[k]==5)end
`);
test('more than forty total fights across different projects are not cut off by a shared cap',`
 caves127({3,4,5},18,0);local result=run_119(1000);assert(result.done)
 local buys,fights=cave127_counts();for k=3,5 do assert(fights[k]==18)end
`);
test('buying more than twenty attempts across five open projects has no shared cutoff',`
 caves127({1,2,3,4,5},0,5);local result=run_119(1000);assert(result.done)
 local buys,fights=cave127_counts();for k=1,5 do assert(buys[k]==5 and fights[k]==5)end
`);
run(`constants.CAVE_DAILY_BUY_CHALLENGE_NUM=C127_BUY_LIMITS;constants.CAVE_DAILY_BUY_PRICE=C127_BUY_PRICES`);
};
