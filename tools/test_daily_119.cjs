module.exports=({run,test})=>{
run(`
function setup_119()
 reset_daily_fixture()
 for id=1,15 do A.cfg['dailyTask'..id]=false end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
 A.cfg.dailyGiftAll=false;A.cfg.dailyCheckinAll=false;A.cfg.dailyVipReward=false
 A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 A.cfg.dailyIncludeMine=false;A.cfg.workshopDepth=150;A.cfg.workshopMinutes=120
 mining.dig_depth=200;mining.max_project_count=2
 config_manager.mining_quarry_config={{ID=1,name='深层',need_layer=120,need_time=120,dig_count=60,tnt_count=0}}
 function resource_logic:CheckResourceNum(id,n)return (resource_counts[id] or 0)>=n end
end
function start_119()
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,prayer=true,alchemy=true,mining=true,caves=true}
 daily.liveness_list={};daily.activity_receive_list={}
end
function run_119(limit)
 return run_daily_events(limit or 400)
end
`);

test('daily mail claims system and friendship mail once per ID and ignores read mail',`
setup_119();A.cfg.dailyMail=true
mail_server={{id=11,mark_read=false},{id=12,mark_read=false},{id=11,mark_read=false},{id=13,mark_read=true}}
mail_state.not_read_list={{id=11,mark_read=false}}
start_119();local result=run_119()
assert(result.done and count_event('query_mail_info')==1)
assert(count_event('open_mail')==2,'duplicate and read mail must not be opened')
assert(A.daily_state.mail_claimed==2 and A.daily_state.mail_done)
local seen={}
for _,m in ipairs(mail_state.not_read_list) do assert(not seen[m.id]);seen[m.id]=true end
`);

test('daily empty mailbox ends cleanly without an open request',`
setup_119();A.cfg.dailyMail=true;start_119()
local result=run_119()
assert(result.done and A.daily_state.mail_claimed==0 and count_event('open_mail')==0)
`);

test('daily unavailable mail hooks cannot block the gold phase',`
setup_119();A.cfg.dailyMail=true;A.cfg.dailyGoldSummon=true
network.event_listener.query_mail_info_ret=nil;A._task_hooks.query_mail_info_ret=nil
start_119();local result=run_119()
assert(result.done and A.daily_state.mail_done and count_event('recruit_mercenary')==4)
`);

test('daily mail rejection continues with other mail and the gold phase',`
setup_119();A.cfg.dailyMail=true;A.cfg.dailyGoldSummon=true
mail_server={{id=21},{id=22}};mail_fail_ids[21]='bag_full'
start_119();local result
for tick=1,300 do A.now=tick;clock=1000+tick;result=A:daily_step();service_daily_request();if A.daily_state.mail_claimed>=1 then mail_fail_ids[21]=nil end;if result.done then break end end
assert(result.done and A.daily_state.mail_claimed==2 and A.daily_state.mail_failures==1)
assert(count_event('open_mail')==3 and count_event('recruit_mercenary')==4)
`);

test('daily mail response must match the requested mail ID',`
setup_119();A.cfg.dailyMail=true;mail_server={{id=31}};start_119()
for tick=1,100 do A.now=tick;A:daily_step();if A._task_pending and A._task_pending.mail_id==31 then break end;service_daily_request() end
assert(A._task_pending and A._task_pending.mail_id==31)
respond('open_mail_ret',{result='success',id=999})
assert(not A._task_pending.response and A.daily_state.mail_claimed==0)
respond('open_mail_ret',{result='success',id=31})
local result=run_119();assert(result.done and A.daily_state.mail_claimed==1)
`);

test('daily timed-out mail is rechecked at the tail and retried when still unread',`
setup_119();A.cfg.dailyMail=true;A.cfg.dailyGoldSummon=true;mail_server={{id=41}};start_119()
for tick=1,100 do A.now=tick;A:daily_step();if A._task_pending and A._task_pending.mail_id==41 then break end;service_daily_request() end
assert(A._task_pending and A._task_pending.mail_id==41)
A.now=A.now+21;A:daily_step();assert(A.daily_state.mail_done)
-- Use increasing time after the timeout; no second open may be sent.
local result
for i=31,100 do A.now=i;clock=1000+i;result=A:daily_step();service_daily_request();if result.done then break end end
assert(result.done and count_event('open_mail')==2 and count_event('recruit_mercenary')==4)
`);

test('daily gold phase makes four gold summons even after liveness was already claimed',`
setup_119();A.cfg.dailyGoldSummon=true;start_119()
daily.liveness_list={{liveness_id=8,completion_count=4,is_reward=true}}
local result=run_119();assert(result.done and A.daily_state.gold_summons==4)
assert(count_event('recruit_mercenary')==4 and resource_counts[3]==99000)
for _,m in ipairs(sent)do if m.recruit_mercenary then assert(m.recruit_mercenary.door_type=='recruiting_door') end end
assert(A.daily_state.gold_done and not A._task_pending)
`);

test('daily gold phase rechecks increasing prices and stops at the affordable count',`
setup_119();A.cfg.dailyGoldSummon=true;resource_counts[3]=300;start_119()
local result=run_119()
assert(result.done and A.daily_state.gold_summons==2 and count_event('recruit_mercenary')==2)
assert(resource_counts[3]==0,'only the 100 and 200 gold summons are affordable')
`);

test('daily gold phase accepts a free initial gold summon',`
setup_119();A.cfg.dailyGoldSummon=true;gold_cost=0;resource_counts[3]=0;start_119()
local result=run_119()
assert(result.done and count_event('recruit_mercenary')==1 and A.daily_state.gold_summons==1)
`);

test('daily rejected gold summon is never counted or retried',`
setup_119();A.cfg.dailyGoldSummon=true;gold_fail=true;start_119()
local result=run_119()
assert(result.done and count_event('recruit_mercenary')==1 and A.daily_state.gold_summons==0)
`);

test('daily gold summon stops before sending when mercenary storage is full',`
setup_119();A.cfg.dailyGoldSummon=true
local original=troop.CheckMercenaryNum;troop.CheckMercenaryNum=function()return false end
start_119();local result=run_119();troop.CheckMercenaryNum=original
assert(result.done and count_event('recruit_mercenary')==0)
`);

test('daily mail and fixed gold phases respect their checklist switches',`
setup_119();mail_server={{id=51}};start_119()
local result=run_119()
assert(result.done and count_event('query_mail_info')==0 and count_event('recruit_mercenary')==0)
`);

test('daily workshop fills both empty slots with five exact refill batches',`
setup_119();A.cfg.dailyTask2=true;mining.dig_count=0;start_119()
daily.liveness_list={{liveness_id=2,completion_count=0,is_reward=false}}
local result=run_119();assert(result.done)
local batches={}
for _,m in ipairs(sent)do if m.use_mining_tool then batches[#batches+1]=m.use_mining_tool.num end end
assert(#batches==5 and batches[1]==5 and batches[2]==5 and batches[3]==5 and batches[4]==5 and batches[5]==4,'need 5+5+5+5+4 bags for both slots')
assert(count_event('add_mining_project')==2 and resource_counts[7]==76)
assert(count_event('query_mining_info')==1,'one verification snapshot for the full planned refill')
`);

test('daily refill uses one bag for a one-pickaxe shortage',`
setup_119();mining.dig_count=59;start_119()
A:daily_add_tools(60,2)
assert(sent[#sent].use_mining_tool.num==1)
`);

test('daily refill never requests more bags than remain in inventory',`
setup_119();mining.dig_count=0;resource_counts[7]=3;start_119()
A:daily_add_tools(60,2);assert(sent[#sent].use_mining_tool.num==3)
`);

test('daily cave plans pickaxes for all remaining attempts before the first fight',`
setup_119();A.cfg.dailyTask9=true;A.cfg.spendAllowCaveBuy=false;mining.cave_buy_challenge_nums={[3]=0}
mining.cave_config_info={[7]={3}};mining.cave_challenge_nums={[3]=3};mining.dig_count=0
config_manager.mining_event_config={[3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=5,name='巨魔'}}}
start_119();daily.liveness_list={{liveness_id=9,completion_count=4,is_reward=true}}
local result=run_119();assert(result.done)
assert(count_event('use_mining_tool')==1 and count_event('solve_cave_event')==3)
for _,m in ipairs(sent)do if m.use_mining_tool then assert(m.use_mining_tool.num==3,'3 fights x5 picks require 3 bags') end end
A.cfg.spendAllowCaveBuy=true
`);

test('daily workshop skips when the configured depth tier has not been reached',`
setup_119();mining.dig_depth=50
config_manager.mining_quarry_config={
 {ID=1,name='浅层',need_layer=0,need_time=120,dig_count=20,tnt_count=0},
 {ID=2,name='深层',need_layer=120,need_time=120,dig_count=60,tnt_count=0}}
start_119();A:daily_action(2,daily_conf_for_test[2],{completion_count=0})
assert(A.daily_state.skipped[2] and count_event('add_mining_project')==0 and count_event('use_mining_tool')==0)
`);

test('daily caves spend usable pickaxes even when bags cannot fund the entire phase',`
setup_119();A.cfg.dailyTask9=true;A.cfg.spendAllowCaveBuy=false;mining.cave_buy_challenge_nums={[3]=0}
mining.cave_config_info={[7]={3}};mining.cave_challenge_nums={[3]=3};mining.dig_count=10;resource_counts[7]=0
config_manager.mining_event_config={[3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=5,name='巨魔'}}}
start_119();daily.liveness_list={{liveness_id=9,completion_count=4,is_reward=true}}
local result=run_119()
assert(result.done and count_event('solve_cave_event')==2 and count_event('use_mining_tool')==0)
A.cfg.spendAllowCaveBuy=true
`);

test('daily workshop does not lower its tier to save dynamite',`
setup_119();resource_counts[5]=0
config_manager.mining_quarry_config={
 {ID=1,name='浅层',need_layer=0,need_time=120,dig_count=20,tnt_count=0},
 {ID=2,name='深层',need_layer=120,need_time=120,dig_count=60,tnt_count=3}}
start_119();A:daily_action(2,daily_conf_for_test[2],{completion_count=0})
assert(A.daily_state.skipped[2] and count_event('add_mining_project')==0)
`);

test('daily workshop skips a missing duration instead of choosing the nearest duration',`
setup_119();A.cfg.workshopMinutes=240
start_119();A:daily_action(2,daily_conf_for_test[2],{completion_count=0})
assert(A.daily_state.skipped[2] and count_event('add_mining_project')==0)
`);

test('daily workshop never consumes bags for a project above the pickaxe cap',`
setup_119();mining.dig_max_count=50;mining.dig_count=0;start_119()
A:daily_action(2,daily_conf_for_test[2],{completion_count=0})
assert(A.daily_state.skipped[2] and count_event('use_mining_tool')==0)
`);

test('daily workshop skips immediately when all owned bags cannot meet the tier',`
setup_119();mining.dig_count=0;resource_counts[7]=3;start_119()
A:daily_action(2,daily_conf_for_test[2],{completion_count=0})
assert(A.daily_state.skipped[2] and count_event('use_mining_tool')==0 and count_event('add_mining_project')==0)
`);

test('daily cave purchase rejection is isolated to that cave kind',`
setup_119();A.cfg.spendBudget=1000;A.cfg.spendAllowCaveBuy=true
mining.cave_config_info={[7]={3,4}};mining.cave_buy_challenge_nums={[3]=1,[4]=1}
mining.cave_challenge_nums={[3]=0,[4]=0};start_119()
A:daily_action(9,daily_conf_for_test[9],{completion_count=0})
assert(sent[#sent].buy_cave_challenge.cave_type==3)
respond('buy_cave_challenge_ret',{result='not_enough_resource',cave_type=3})
A:daily_step();A.daily_state.loaded.mining=true
A:daily_action(9,daily_conf_for_test[9],{completion_count=0})
assert(sent[#sent].buy_cave_challenge.cave_type==4,'other caves must still be purchased')
`);

test('daily cave ignores old budget and still buys the expensive remaining kind',`
setup_119();A.cfg.spendBudget=30;A.cfg.spendAllowCaveBuy=true
mining.cave_config_info={[7]={3,4}};mining.cave_buy_challenge_nums={[3]=1,[4]=3};start_119()
A:daily_action(9,daily_conf_for_test[9],{completion_count=0})
assert(sent[#sent].buy_cave_challenge.cave_type==3,'old 30 budget must not block the 60-diamond purchase')
`);

test('daily guild purchase subtracts already bought tickets from the full daily allowance',`
setup_119();A.cfg.spendBudget=2000;guild.guild_boss_buy_count=4
start_119();A:start_daily_guild_boss(0,1)
A:daily_guild_ticket_step();service_daily_request();A:daily_guild_ticket_step()
assert(sent[#sent].guild_boss_buy_ticket.count==6,'10 daily limit minus 4 already bought')
`);

test('daily guild purchase refuses unknown pricing rather than assuming zero cost',`
setup_119();A.cfg.spendBudget=2000;start_119()
local original=guild.BuyTicketCost;guild.BuyTicketCost=nil
A:start_daily_guild_boss(0,1);A:daily_guild_ticket_step();service_daily_request()
local result=A:daily_guild_ticket_step();guild.BuyTicketCost=original
assert(result.done and count_event('guild_boss_buy_ticket')==0)
`);

test('daily guild purchase stops when every ticket was already bought today',`
setup_119();A.cfg.spendBudget=2000;guild.guild_boss_buy_count=10;start_119()
A:start_daily_guild_boss(0,1);A:daily_guild_ticket_step();service_daily_request()
local result=A:daily_guild_ticket_step()
assert(result.done and count_event('guild_boss_buy_ticket')==0)
`);

run(`A:cancel_task();A.cfg.workshopDepth=150;A.cfg.workshopMinutes=120;A.cfg.spendAllowCaveBuy=true;mining.dig_max_count=nil`);
};
