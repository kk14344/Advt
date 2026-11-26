module.exports=({run,test,toLua})=>{
run(`
daily={liveness_list={},activity_receive_list={},prayer_info={mark=0},alchemy_info={mark=0}}
function daily:AlreadyCheckin()return checked_in end
gold_cost=100
function daily:GetRecruitCost()return gold_cost end
function daily:CheckActivityComplet(id,count)
 local conf=daily_conf_for_test[id]
 return conf and count>=conf.condition_count
end
package.loaded['logic.daily']=daily
bag_logic={GetSpaceCount=function()return 50 end}
mining={dig_count=100,dig_depth=200,max_project_count=2,project_list={},event_count=25,
 cave_config_info={[7]={1,3}},cave_levels={[1]=3,[3]=3},cave_challenge_nums={[1]=5,[3]=5},cave_boss_lv=1}
guild={guild_cur_boss_id=3,guild_boss_over=false,fright=0,guild_boss_buy_count=0}
function guild:GetFrightBossState() return self.fright or 0 end
function guild:IsOpenGuildBoss() return true end
function guild:GetBuyTicketMax() return math.max(0,10-(self.guild_boss_buy_count or 0)) end
function guild:BuyTicketCost(num)
 local cost=0
 for i=(self.guild_boss_buy_count or 0)+1,(self.guild_boss_buy_count or 0)+num do
  cost=cost+(i<=3 and 20 or (i<=6 and 40 or 60))
 end
 return cost
end
package.loaded['logic.guild']=guild
social={unsend_gift_list={1,2,3,4,5}}
function social:GetUnsendGiftList()return self.unsend_gift_list end
package.loaded['logic.social']=social
ladder={challenge_num=5,challenge_cd=0,rival_list={{rank=10,battle_point=1}}}
package.loaded['logic.ladder']=ladder
escort={escort_info={status=1,tramcar_id=4,auto_refresh_tramcar=0,refresh_rob_target_time=0},escort_times={remain_escort=1,remain_rob=3,refresh_tramcar=0},
 rob_target_list={{user_id=11,tramcar_id=4},{user_id=12,tramcar_id=3},{user_id=13,tramcar_id=4}}}
function escort:IsAutoRefreshTramcar()return self.escort_info.auto_refresh_tramcar==1 end
package.loaded['logic.escort']=escort
rune={rune_list={}}
package.loaded['logic.rune']=rune
function troop:CheckMercenaryNum()return true end
function troop:CheckForgeWeaponResource()return true end
function troop:GetTroopBP()return 10000000 end
function resource_logic:GetResourceNum(id)return resource_counts[id] or 0 end
constants.PRAYER_CONFIG={{req_value=0},{req_value=30}}
constants.ALCHEMY_CONFIG={{req_value=0},{req_value=60}}
constants.RECRUIT_COST={friendship_door=150}
constants.MAX_LEVEL=100;constants.MAX_WEAPON_LV=30;constants.CAN_WAKEUP_LEVEL=30
constants.RESOURCE_TYPE.friendship_pt=2;constants.RESOURCE_TYPE.gold_coin=3
constants.RESOURCE_TYPE.exp=4;constants.RESOURCE_TYPE.tnt=5;constants.RESOURCE_TYPE.demon_medal=6
constants.RESOURCE_TYPE.ultimate_tool=7
constants.CAVE_BOSS_CHALLANGE_SUB=1;constants.OPEN_CAVE_BOSS_DEMON_MEDAL=1
constants.CAVE_DAILY_BUY_CHALLENGE_NUM={[1]=3,[2]=3,[3]=3,[4]=3,[5]=3}
constants.CAVE_DAILY_BUY_PRICE={[1]={20,40,60},[2]={20,40,60},[3]={20,40,60},[4]={20,40,60},[5]={20,40,60}}
constants.FREE_REFRESH_TRAMCAR_TIMES=3;constants.ESCORT_STATUS={READY=1,FINISH=3,ESCORTING=2}
constants.CAMPAIGN_STATUS={unknown=1,game=2,reward=3};constants.CAMPAIGN_CONVERT_SCORE=6
constants.EXPEDITION_TYPE={challenge=1,search=2}
-- 合战 / 宿命远征 mock（1.18）：按真实协议字段模拟次数、关卡与兑换递减
campaign_state={cur_campaign_id=7,status=2,end_time=11000,challenge_num=2,limit_count=2,
 exp=0,score=0,level_info_list={},buff_info_map={}}
function campaign_state:IsOpen()return self.status~=1 and self.end_time>clock end
package.loaded['logic.campaign']=campaign_state
expedition_state={players={},figthing_count=2,buy_figthing_count=0,search_count=2,
 countdown=990,duration=11000,group=1,integral_num=0}
package.loaded['logic.ladder_tower']=expedition_state
feature_config={ladder_grade=true}
function feature_config:IsFeatureOpen(key)return self[key]~=false end
package.loaded['logic.feature_config']=feature_config
campaign_price=20;expedition_enemy_seq=100;campaign_buy_fail=false
config_manager.activity_config=TABLES.liveness
config_manager.mercenary_exp_config={[1]={wakeup_factor1=10},[2]={wakeup_factor1=20},[3]={wakeup_factor1=30},[4]={wakeup_factor1=40}}
config_manager.weapon_forge_config={[1]={}}
config_manager.mining_quarry_config={{ID=1,name='日常生产',need_layer=0,need_time=120,dig_count=60,tnt_count=0}}
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔巢穴'}}}
daily_conf_for_test={}
for _,conf in pairs(config_manager.activity_config)do daily_conf_for_test[conf.liveness_id]=conf end
function reset_daily_fixture()
 A.cfg.dailyVoidChallenge=false;A.cfg.dailyMarketExchange=false
 A.cfg.dailyRefine=false;A.cfg.earthUseAll=false;constants.RESOURCE_TYPE.ultimate_tool=7
 A:cancel_task();A._cancelled_task_pending=nil;A.now=0;clock=1000;weekday=7;A.inBattle=false;sent={};checked_in=false
 for id=1,15 do A.cfg['dailyTask'..id]=true end
 A.cfg.dailyIncludeMine=false;A.cfg.dailyRewards=true;A.cfg.dailyFreePrayer=true;A.cfg.dailyFreeAlchemy=true
 A.cfg.dailyAlchemyTier=1;A.cfg.dailyTrollLevel=0;A.cfg.dailyUndergroundLevel=0;A._guild_boss_done_day={}
 A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 A.cfg.dailyMail=false;A.cfg.dailyGoldSummon=false;A.cfg.dailyActivityRewards=false;A.cfg.reserveTnt=0
 gold_cost=100;mail_server={};mail_fail_ids={};gold_fail=false
 mail_state={not_read_list={},system_mail_not_read_list={},friend_mail_not_read_list={}}
 package.loaded['logic.mail']=mail_state
 server_counts={};server_claimed={};server_milestone=false;month_cards=nil
 for id in pairs(daily_conf_for_test)do server_counts[id]=0 end
 daily.prayer_info={mark=0};daily.alchemy_info={mark=0};daily.liveness_list={};daily.activity_receive_list={}
 social.unsend_gift_list={1,2,3,4,5};mining.dig_count=100;mining.dig_max_count=nil;mining.project_list={}
 mining.cave_challenge_nums={[1]=5,[3]=5};mining.cave_buy_challenge_nums=nil
 resource_counts={[1]=10000,[2]=10000,[3]=100000,[7]=100,[51]=0}
 guild.guild_cur_boss_id=3;guild.guild_boss_over=false;guild.fright=0;guild.guild_boss_buy_count=0
 campaign_state.cur_campaign_id=7;campaign_state.status=2;campaign_state.end_time=clock+10000
 campaign_state.challenge_num=2;campaign_state.limit_count=2;campaign_state.exp=0;campaign_state.score=0
 campaign_state.level_info_list={}
 for i=1,8 do campaign_state.level_info_list[i]={level_id=i,status=(i<=3 and 'new' or 'lock'),
  is_boss=(i==5 and 1 or 0),next_battle_time=0,start_battle_time=0} end
 campaign_state.buff_info_map={[6]={type=6,req_exp=10,level=0,next_value=5}}
 expedition_state.players={{enemy_id=101},{enemy_id=102},{enemy_id=103}}
 expedition_state.figthing_count=2;expedition_state.buy_figthing_count=0
 expedition_state.search_count=2;expedition_state.buy_search_count=0
 expedition_state.countdown=clock-10;expedition_state.duration=clock+10000
 expedition_state.integral_num=0;expedition_enemy_seq=100;campaign_buy_fail=false
 expedition_fixed_enemies=false
 feature_config.ladder_grade=true
 config_manager.ladder_buy_config={[1]={{price=100},{price=150},{price=300}},[2]={{price=50},{price=100}}}
 troop.vanity_good_list={}
 network.event_listener.query_vanity_goods_ret=function(msg)troop.vanity_good_list=msg.good_list or {}end
 network.event_listener.vanity_exchange_goods_ret=function(msg)end
 network.event_listener.guild_boss_buy_ticket_ret=function(msg)
  if msg.result=='success' then
   guild.guild_boss_buy_count=(guild.guild_boss_buy_count or 0)+(tonumber(msg.count) or 0)
   resource_counts[51]=(resource_counts[51] or 0)+(tonumber(msg.count) or 0)
  end
 end
 network.event_listener.query_guild_boss_info_ret=function(msg)
  if msg.result=='success' then guild.guild_cur_boss_id=msg.boss_id or 3;guild.guild_boss_over=false end
 end
 network.event_listener.guild_boss_challenge_ret=function(msg)
  if msg.result=='success' then guild.fright=0;resource_counts[51]=math.max(0,(resource_counts[51] or 0)-1) end
 end
 network.event_listener.buy_cave_challenge_ret=function(msg)
  if msg.result=='success' then
   if mining.cave_buy_challenge_nums and mining.cave_buy_challenge_nums[msg.cave_type] then
    mining.cave_buy_challenge_nums[msg.cave_type]=mining.cave_buy_challenge_nums[msg.cave_type]-1
   end
   mining.cave_challenge_nums[msg.cave_type]=(mining.cave_challenge_nums[msg.cave_type] or 0)+1
  end
 end
 A.cfg.dailyGiftAll=true;A.cfg.dailyCheckinAll=true;A.cfg.dailyVipReward=true;A.cfg.dailyVoidExchange=true
 troop.mercenary_list={[1]={instance_id=1,level=1,wakeup=1,exp=0,weapon_lv=0,template_info={name='测试佣兵',max_wakeup=1}}}
 troop.forge_weapon_inflight=nil
 ladder.challenge_num=5;ladder.challenge_cd=0
 escort.escort_info={status=1,tramcar_id=4,auto_refresh_tramcar=0,refresh_rob_target_time=0}
 escort.escort_times={remain_escort=1,remain_rob=3,refresh_tramcar=0}
 rune.rune_list={{rune_id=20,level=1,equip_pos=1,template_info={type=2,quality=4,level_limit=100}},
  {rune_id=21,level=1,equip_pos=0,template_info={type=1,quality=3,out_exp=500}},
  {rune_id=22,level=1,equip_pos=0,template_info={type=1,quality=3,out_exp=500}},
  {rune_id=23,level=1,equip_pos=0,template_info={type=1,quality=3,out_exp=500}}}
 for _,name in ipairs(A.DAILY_EVENTS)do network.event_listener[name]=function(msg)
  if name=='query_liveness_info_ret' then
   daily.liveness_list={}
   for id in pairs(daily_conf_for_test)do daily.liveness_list[#daily.liveness_list+1]={liveness_id=id,completion_count=server_counts[id],is_reward=server_claimed[id]} end
   daily.activity_receive_list=server_milestone and {} or {1}
  elseif name=='take_check_in_reward_ret' then checked_in=true;server_counts[1]=1
  elseif name=='take_daily_prayer_ret' then daily.prayer_info.mark=1
  elseif name=='take_daily_alchemy_ret' then daily.alchemy_info.mark=1
  elseif name=='get_liveness_reward_ret' then
   server_claimed[msg.liveness_id]=true
   for _,item in ipairs(daily.liveness_list)do if item.liveness_id==msg.liveness_id then item.is_reward=true end end
  elseif name=='get_activity_reward_ret' then server_milestone=true;daily.activity_receive_list={}
  elseif name=='send_gift_ret' then table.remove(social.unsend_gift_list,1);server_counts[3]=server_counts[3]+1
  elseif name=='recruit_mercenary_ret' and msg.result=='success' then
   server_counts[8]=server_counts[8]+1
   if msg.door_type=='recruiting_door' then resource_counts[3]=resource_counts[3]-gold_cost;gold_cost=gold_cost+100 end
  elseif name=='query_mail_info_ret' then
   for _,m in ipairs(msg.mail_list or {}) do
    if not m.mark_read then table.insert(mail_state.not_read_list,m);table.insert(mail_state.system_mail_not_read_list,m) end
   end
  elseif name=='open_mail_ret' and msg.result=='success' then
   for _,m in ipairs(mail_server) do if m.id==msg.id then m.mark_read=true end end
  elseif name=='alloc_mercenary_exp_ret' then
   troop.mercenary_list[1].level=troop.mercenary_list[1].level+1;server_counts[15]=server_counts[15]+1
  elseif name=='forge_mercenary_weapon_ret' then server_counts[4]=server_counts[4]+1
  elseif name=='get_mining_project_reward_ret' and msg.result=='success' then
   mining.project_list=msg.project_list or {}
  elseif name=='add_mining_project_ret' then
   server_counts[2]=server_counts[2]+1
   local conf=config_manager.mining_quarry_config[msg.id] or {}
   mining.dig_count=mining.dig_count-(tonumber(conf.dig_count) or 60)
   mining.project_list=msg.project_list or {}
  elseif name=='challenge_ladder_ret' then server_counts[6]=server_counts[6]+1;ladder.challenge_num=ladder.challenge_num-1;ladder.challenge_cd=clock+3
  elseif name=='buy_cave_challenge_ret' and msg.result=='success' then
   if mining.cave_buy_challenge_nums and mining.cave_buy_challenge_nums[msg.cave_type] then
    mining.cave_buy_challenge_nums[msg.cave_type]=mining.cave_buy_challenge_nums[msg.cave_type]-1
   end
   mining.cave_challenge_nums[msg.cave_type]=(mining.cave_challenge_nums[msg.cave_type] or 0)+1
  elseif name=='solve_cave_event_ret' then
   local id=msg.cave_type>=3 and 9 or 10;server_counts[id]=server_counts[id]+1
   local cost=tonumber((((config_manager.mining_event_config or {})[msg.cave_type] or {})[msg.cave_lv] or {}).pickaxe_count) or 1
   mining.dig_count=mining.dig_count-cost
   if mining.cave_challenge_nums and mining.cave_challenge_nums[msg.cave_type] then
    mining.cave_challenge_nums[msg.cave_type]=mining.cave_challenge_nums[msg.cave_type]-1
   end
  elseif name=='use_mining_tool_ret' then
   resource_counts[msg.resource_id or 7]=(resource_counts[msg.resource_id or 7] or 0)-(tonumber(msg.num) or 1)
   mining.dig_count=mining.dig_count+(tonumber(msg.num) or 1)*5
  elseif name=='challenge_cave_boss_ret' then server_counts[12]=server_counts[12]+1
  elseif name=='upgrade_rune_ret' then server_counts[13]=server_counts[13]+1;table.remove(rune.rune_list,2)
  elseif name=='escort_rob_target_ret' then server_counts[14]=server_counts[14]+1;escort.escort_times.remain_rob=escort.escort_times.remain_rob-1
  elseif name=='escort_start_ret' then server_counts[7]=server_counts[7]+1;escort.escort_times.remain_escort=escort.escort_times.remain_escort-1;escort.escort_info.status=2
  elseif name=='take_vip_reward_ret' and month_cards and month_cards.vip_list and month_cards.vip_list[msg.vip_type] then
   month_cards.vip_list[msg.vip_type].reward_mark=2
  end
 end end
 -- 活动阶段回执：按真实客户端回调更新次数/关卡/经验/赛点
 network.event_listener.campaign_overtime_ret=function(msg)
  if msg.result=='success' then
   campaign_state.challenge_num=campaign_state.challenge_num+1
   campaign_state.limit_count=math.max(0,campaign_state.limit_count-1)
  end
 end
 network.event_listener.battle_campaign_ret=function(msg)
  if msg.result~='success' then return end
  campaign_state.challenge_num=math.max(0,campaign_state.challenge_num-1)
  campaign_state.exp=campaign_state.exp+10;campaign_state.score=campaign_state.score+10
  local id=tonumber(msg.level_id) or 0
  local info=campaign_state.level_info_list[id]
  if info then info.status='clear';info.next_battle_time=clock+300 end
  local nxt=campaign_state.level_info_list[id+1]
  if nxt and nxt.status=='lock' then nxt.status='new' end
 end
 network.event_listener.convert_campaign_exp_ret=function(msg)
  if msg.result~='success' then return end
  local buff=campaign_state.buff_info_map[6]
  local req=tonumber(buff.req_exp) or 0
  campaign_state.exp=math.max(0,campaign_state.exp-req)
  campaign_state.score=campaign_state.score+5
  buff.req_exp=req+5;buff.level=(buff.level or 0)+1
 end
 network.event_listener.expedition_apend_times_ret=function(msg)
  if msg.result~='success' then return end
  local n=tonumber(msg.num) or 1
  expedition_state.figthing_count=expedition_state.figthing_count+n
  expedition_state.buy_figthing_count=expedition_state.buy_figthing_count+n
 end
 local function refresh_expedition_enemies()
  expedition_enemy_seq=expedition_enemy_seq+3
  expedition_state.players={{enemy_id=expedition_enemy_seq+1},{enemy_id=expedition_enemy_seq+2},
   {enemy_id=expedition_enemy_seq+3}}
 end
 network.event_listener.expedition_fight_ret=function(msg)
  if msg.result~='success' then return end
  expedition_state.figthing_count=math.max(0,expedition_state.figthing_count-1)
  expedition_state.integral_num=expedition_state.integral_num+10
  if not expedition_fixed_enemies then refresh_expedition_enemies() end
 end
 network.event_listener.refresh_expedition_search_ret=function(msg)
  if msg.result~='success' then return end
  expedition_state.search_count=math.max(0,expedition_state.search_count-1)
  refresh_expedition_enemies()
 end
 network.event_listener.query_arena_rival_ret=function(msg)
  arena.rival_list=msg.rival;arena.challenge_num=msg.challenge_num;arena.refresh_time=msg.refresh_time
 end
 network.event_listener.challenge_arena_rival_ret=function(msg)
  arena.rival_list[msg.rival_pos].state=true;arena.challenge_num=arena.challenge_num-1;server_counts[5]=server_counts[5]+1
 end
 function A:step()server_counts[11]=server_counts[11]+1 end
end
function service_daily_request()
 local p=A._task_pending;if not p or p.response then return end
 local message=sent[#sent];local name,payload=next(message)
 assert(A:daily_message_is_free(name,payload),'paid request escaped guard: '..name)
 local response={result='success'}
 for k,v in pairs(payload)do response[k]=v end
 if name=='query_arena_rival' then
  response.rival={};for i=1,9 do response.rival[i]={state=false,battle_point=i}end
  response.challenge_num=20;response.refresh_time=clock+10000
 end
 if name=='add_mining_project' then
  response.project_list={}
  for _,v in ipairs(mining.project_list)do response.project_list[#response.project_list+1]=v end
  response.project_list[#response.project_list+1]={id=payload.id,endtime=clock+7200}
 end
 if name=='get_mining_project_reward' then
  response.project_list={}
  for i=2,#mining.project_list do response.project_list[#response.project_list+1]=mining.project_list[i] end
 end
 if name=='query_campaign_info' then response.result=nil;response.cur_campaign_id=campaign_state.cur_campaign_id;response.status=campaign_state.status end
 if name=='query_campaign_level' then response.result=nil;response.level_info_list=campaign_state.level_info_list end
 if name=='query_campaign_buff' then response.result=nil;response.buff_info_list={campaign_state.buff_info_map[6]} end
 if name=='buy_campaign_overtime' then response.challenge_num=campaign_state.challenge_num+1 end
 if name=='battle_campaign' then response.challenge_num=math.max(0,campaign_state.challenge_num-1) end
 if name=='convert_campaign_exp' then
  local b=campaign_state.buff_info_map[6]
  response.exp=campaign_state.exp-b.req_exp;response.score=campaign_state.score+5
  response.buff_info={type=6,req_exp=b.req_exp+5,level=(b.level or 0)+1,next_value=5}
 end
 if name=='query_campaign_overtime' then
  response.result=nil
  response.req_value=campaign_price;response.reward_value=1;response.limit_count=campaign_state.limit_count
 end
 if name=='buy_campaign_overtime' and campaign_buy_fail then response.result='failure' end
 if name=='query_mining_info' then response.cave_challenge_nums=mining.cave_challenge_nums;response.cave_buy_challenge_nums=mining.cave_buy_challenge_nums end
 if name=='query_mail_info' then response.mail_list=mail_server;response.result=nil end
 if name=='open_mail' and mail_fail_ids[payload.id] then response.result=mail_fail_ids[payload.id] end
 if name=='recruit_mercenary' and gold_fail then response.result='not_enough_resource' end
 respond(p.event,response)
end
function setup_daily_events(budget)
 reset_daily_fixture()
 for id=1,15 do A.cfg['dailyTask'..id]=false end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
 A.cfg.dailyGiftAll=false;A.cfg.dailyCheckinAll=false;A.cfg.dailyVipReward=false
 A.cfg.dailyVoidExchange=false;A.cfg.dailyIncludeMine=false;A.cfg.dailyGuildBoss=false
 A.cfg.dailyEvents=true;A.cfg.spendBudget=budget or 2000;A.cfg.spendBase=0;A.cfg.spendDay=0
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
 daily.liveness_list={};daily.activity_receive_list={}
end
function run_daily_events(max_ticks)
 local result
 for tick=1,max_ticks or 400 do
  A.now=tick;clock=1000+tick
  result=A:daily_step();service_daily_request()
  if result.done then break end
 end
 return result
end
function count_event(key)
 local n=0
 for _,m in ipairs(sent)do if m[key] then n=n+1 end end
 return n
end
`);
test('daily executes all 15 task categories and verifies server progress',`
reset_daily_fixture();A.cfg.arenaAllowBloodRefresh=true;A:start_daily()
local result
for tick=1,500 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 if result.done then break end
end
assert(result and result.done and not result.partial,tostring(result and result.message))
for id,conf in pairs(daily_conf_for_test)do assert(server_counts[id]>=conf.condition_count,'missing real action '..id)end
assert(server_counts[5]==9 and server_counts[6]==3)
for _,message in ipairs(sent)do assert(not message.refresh_arena_rival)end
assert(A.cfg.arenaAllowBloodRefresh==true,'daily must preserve standalone arena preference')
`);
test('troll lair buys all extra attempts then fights the highest difficulty',`
reset_daily_fixture()
mining.cave_config_info={[7]={3,4}}
mining.cave_levels={[3]=3,[4]=2}
mining.cave_challenge_nums={[3]=1,[4]=1}
mining.cave_buy_challenge_nums={[3]=2,[4]=1}
config_manager.mining_event_config={
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔幼儿园'},
  {cave_type=3,level=2,bp_limit=0,pickaxe_count=5,name='巨魔幼儿园'},
  {cave_type=3,level=3,bp_limit=0,pickaxe_count=9,name='巨魔幼儿园'}},
 [4]={{cave_type=4,level=1,bp_limit=0,pickaxe_count=3,name='巨魔分校'},
  {cave_type=4,level=2,bp_limit=0,pickaxe_count=6,name='巨魔分校'}}}
network.event_listener.buy_cave_challenge_ret=function(msg)
 if msg.result=='success' then
  mining.cave_buy_challenge_nums[msg.cave_type]=mining.cave_buy_challenge_nums[msg.cave_type]-1
  mining.cave_challenge_nums[msg.cave_type]=mining.cave_challenge_nums[msg.cave_type]+1
 end
end
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask9=true
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,caves=true}
daily.liveness_list={{liveness_id=9,completion_count=0,is_reward=false}};daily.activity_receive_list={}
local result,buys,solves,first_solve,seen=nil,0,0,nil,0
for tick=1,300 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 for i=seen+1,#sent do
  local last=sent[i]
  if last.buy_cave_challenge then
   buys=buys+1
   assert(last.buy_cave_challenge.cave_type==3 or last.buy_cave_challenge.cave_type==4,'must buy troll lair attempts')
  end
  if last.solve_cave_event then
   solves=solves+1
   if not first_solve then first_solve=last.solve_cave_event end
  end
 end
 seen=#sent
 if result.done then break end
end
assert(result and result.done and not result.partial,tostring(result and result.message)..
 ' [sent='..(function() local t={} for i,m in ipairs(sent) do for k in pairs(m) do t[#t+1]=k end end return table.concat(t,',') end)()..']')
assert(buys==3,'must buy all 3 extra attempts, got '..tostring(buys)..
 ' [budget='..tostring(A.cfg.spendBudget)..' allow='..tostring(A.cfg.spendAllowCaveBuy)..
 ' stop='..tostring(A.daily_state and A.daily_state.cave_buy_stop)..
 ' msg='..tostring(A.task_message)..']')
assert(first_solve and first_solve.cave_type==3 and first_solve.cave_lv==3,
 'the first fight must be the highest difficulty, got '..tostring(first_solve and first_solve.cave_type)..' lv '..tostring(first_solve and first_solve.cave_lv))
-- 1.17：活跃度达成后仍需打完剩余次数（副本3共3场 + 副本4共2场）
assert(solves==5,'must spend every attempt (3 for cave 3, 2 for cave 4), got '..tostring(solves))
assert(mining.cave_challenge_nums[3]==0 and mining.cave_challenge_nums[4]==0,'all attempts must be spent')
mining.cave_config_info={[7]={1,3}};mining.cave_levels={[1]=3,[3]=3};mining.cave_challenge_nums={[1]=5,[3]=5};mining.cave_buy_challenge_nums=nil
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔巢穴'}}}
`);
test('troll lair skips buying when blood diamonds are insufficient',`
reset_daily_fixture()
mining.cave_config_info={[7]={3}}
mining.cave_levels={[3]=3}
mining.cave_challenge_nums={[3]=2}
mining.cave_buy_challenge_nums={[3]=3}
config_manager.mining_event_config={
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔幼儿园'},
  {cave_type=3,level=3,bp_limit=0,pickaxe_count=9,name='巨魔幼儿园'}}}
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask9=true
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,caves=true}
daily.liveness_list={{liveness_id=9,completion_count=0,is_reward=false}};daily.activity_receive_list={}
enough_blood=false
local result,buys,seen=nil,0,0
for tick=1,200 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 for i=seen+1,#sent do
  if sent[i].buy_cave_challenge then buys=buys+1 end
 end
 seen=#sent
 if result.done then break end
end
assert(result and result.done,tostring(result and result.message))
assert(buys==0,'must not buy without blood diamonds')
assert(server_counts[9]==2,'must still spend the 2 free attempts, got '..tostring(server_counts[9]))
mining.cave_config_info={[7]={1,3}};mining.cave_levels={[1]=3,[3]=3};mining.cave_challenge_nums={[1]=5,[3]=5};mining.cave_buy_challenge_nums=nil
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔巢穴'}}}
enough_blood=true
`);
test('daily spend guard blocks premium and nested automatic refreshes',`
reset_daily_fixture();A:start_daily();local n=#sent
assert(network:Send({refresh_arena_rival={}})==false)
assert(network:Send({buy_cave_challenge={cave_type=1}})==false)
assert(network:Send({forge_mercenary_weapon={mercenary_id=1,extra_chance=1}})==false)
assert(network:Send({recruit_mercenary={door_type='hero_door'}})==false)
assert(network:Send({take_daily_prayer={index=2}})==false)
assert(network:Send({escort_refresh_tramcar={refresh_type='auto'}})==false)
assert(#sent==n)
network:Send({take_daily_prayer={index=1}});assert(#sent==n+1)
A:cancel_task();network:Send({refresh_arena_rival={}});assert(#sent==n+2,'stop must release daily guard')
`);
test('daily reports incomplete tasks rather than treating rewards as completion',`
reset_daily_fixture();A.cfg.dailyCheckinAll=false;A.cfg.dailyGiftAll=false;A.cfg.dailyVipReward=false;A.cfg.dailyVoidExchange=false
A.cfg.dailyTask2=false;A.cfg.dailyTask9=false;A.cfg.dailyTask10=false
A:start_daily();A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
daily.liveness_list={{liveness_id=3,completion_count=0,is_reward=false}};daily.activity_receive_list={}
social.unsend_gift_list={};A.daily_state.loaded.social=true
local first=A:daily_step();assert(not first.done and A.daily_state.skipped[3])
local result=A:daily_step();local summary=result.message
for tick=1,200 do if result.done then break end;A.now=tick;result=A:daily_step();service_daily_request() end
assert(result.done and result.partial and server_counts[3]==0)
assert(result.message:find('0/1'))
`);
test('mining bag preparation does not count as a failed task attempt',`
reset_daily_fixture();mining.dig_count=0;A:start_daily();A.daily_state.loaded.mining=true
A:daily_add_tools(60,2);local p=A._task_pending;assert(p and p.task_id==nil)
respond('use_mining_tool_ret',{result='success',num=5})
respond('use_mining_tool_ret',{result='success',num=5})
respond('use_mining_tool_ret',{result='success',num=2})
assert(A._task_pending.event=='query_mining_info_ret' and not A.daily_state.stalls[2])
`);
test('daily rune upgrade consumes only dedicated experience runes',`
reset_daily_fixture();A:start_daily();A.daily_state.loaded.runes=true
local result=A:daily_action(13,daily_conf_for_test[13],{completion_count=0})
assert(sent[#sent].upgrade_rune.exp_rune_id_list[1]==21)
A:cancel_task();table.remove(rune.rune_list,4);table.remove(rune.rune_list,3);table.remove(rune.rune_list,2)
table.insert(rune.rune_list,{rune_id=99,level=1,equip_pos=0,template_info={type=2,quality=4,level_limit=100}})
A:start_daily();A.daily_state.loaded.runes=true;sent={}
A:daily_action(13,daily_conf_for_test[13],{completion_count=0})
assert(#sent==0 and A.daily_state.skipped[13])
`);
test('daily free arena never uses the paid standalone preference',`
reset_daily_fixture();A.cfg.arenaAllowBloodRefresh=true;A:start_daily()
A:daily_action(5,daily_conf_for_test[5],{completion_count=0})
A:daily_step()
respond('query_arena_rival_ret',{rival={{state=true}},challenge_num=10,refresh_time=clock+10000})
local result=A:daily_step()
assert(not A.daily_state.child_arena and A.daily_state.skipped[5])
for _,message in ipairs(sent)do assert(not message.refresh_arena_rival)end
`);
test('mining refill stops if an authoritative snapshot shows no increase',`
reset_daily_fixture();mining.dig_count=10;A:start_daily();A.daily_state.loaded.mining=true
A:daily_add_tools(60,2);respond('use_mining_tool_ret',{result='success',num=5})
respond('use_mining_tool_ret',{result='success',num=5})
mining.dig_count=10
respond('query_mining_info_ret',{dig_count=10})
A:daily_step();assert(A.daily_state.skipped[2] and A.daily_state.skipped[2]:find('未增加'))
`);
test('daily claims the current check-in period even when liveness was already complete',`
reset_daily_fixture();A:start_daily();A.daily_state.loaded={daily=true,liveness=true}
daily.liveness_list={{liveness_id=1,completion_count=1,is_reward=true}};checked_in=false
A:daily_step();assert(sent[#sent].take_check_in_reward)
`);
test('daily executes only selected tasks and excludes disabled items from completion totals',`
reset_daily_fixture()
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask3=true;A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
A.cfg.dailyGiftAll=false
A:start_daily();local result
for tick=1,100 do A.now=tick;clock=1000+tick;result=A:daily_step();service_daily_request();if result.done then break end end
assert(result.done and not result.partial and result.message:find('1/1'))
assert(server_counts[3]==daily_conf_for_test[3].condition_count)
for id,count in pairs(server_counts)do if id~=3 then assert(count==0,'disabled task executed '..id)end end
assert(not checked_in and daily.prayer_info.mark==0 and daily.alchemy_info.mark==0)
for _,request in ipairs(sent)do assert(not request.get_liveness_reward and not request.get_activity_reward)end
`);
test('daily freezes the selected checklist for the active run',`
reset_daily_fixture();A.cfg.dailyTask3=false;A:start_daily()
A.cfg.dailyTask3=true;assert(not A:daily_enabled(3))
A:cancel_task();A:start_daily();assert(A:daily_enabled(3))
`);
test('daily hands off to mine and retains the no-blood-diamond guard',`
reset_research_mine();reset_daily_fixture()
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false;A.cfg.dailyIncludeMine=true
A:start_daily();local parent=A.daily_state;local result;local saw_mine=false
for tick=1,80 do
 A.now=tick;result=A:daily_step()
 if result.phase=='mine' then saw_mine=true;assert(A.daily_state==parent)
  if not result.done then assert(parent.running and network:Send({refresh_arena_rival={}})==false)end
 end
 local p=A._task_pending
 if p and not p.response then
  if p.kind=='mine_read' then mine_query_response(next(sent[#sent])) else service_daily_request() end
 end
 if result.done then break end
end
assert(saw_mine and result.done and not result.error and not A.daily_state.running)
assert(A.mine_state and A.mine_state.started==0)
A:cancel_task();assert(not A.daily_state and not A.mine_state)
`);
test('daily mine phase ends after dispatch; a new run harvests the mature queue',`
reset_research_mine({{mine_index=1,status=2,mine_level=1,end_time=1010}});reset_daily_fixture()
local original_get=resource_logic.GetResourceNum
resource_logic.GetResourceNum=function(self,id)return resource_counts[id]or 0 end
fund_branch(1,1,0)
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false;A.cfg.dailyIncludeMine=true
A:start_daily();local result
for tick=1,80 do
 A.now=tick/100;result=A:daily_step()
 local p=A._task_pending
 if p and not p.response then
  if p.kind=='mine_read' then mine_query_response(next(sent[#sent])) else service_daily_request()end
 end
 if result.done then break end
end
assert(result.done and result.phase=='mine' and result.message:find('冷却'))
assert(A.daily_state and not A.daily_state.running)
clock=1011;A.now=11
local harvested,upgraded=false,false
A:start_daily();A:start_mine_collect(A.daily_state);A.daily_state.child_mine=true
for tick=1,80 do
 A.now=11+tick/10;result=A:daily_step()
 local p=A._task_pending
 if p and not p.response then
  local key,payload=next(sent[#sent])
  if p.kind=='mine_read' then mine_query_response(key)
  elseif key=='mine_receive_reward' then harvested=true;settle_mine(key,payload)
  elseif key=='cultivate' then upgraded=true;settle_mine(key,payload);break end
 end
end
resource_logic.GetResourceNum=original_get
assert(harvested and upgraded and server_research[1].coefficient1==1)
A:cancel_task();assert(not A.daily_state and not A.mine_state)
`);
test('daily extras gift every friend before stopping',`
reset_daily_fixture();A:start_daily();checked_in=true
A.daily_state.loaded={daily=true,liveness=true,vip=true}
social.unsend_gift_list={4,5,6}
local r=A:daily_extras();assert(sent[#sent].query_social_info)
respond('query_social_info_ret',{result='success'});A.daily_state.loaded.social=true
local gifts=0
for i=1,10 do
 r=A:daily_extras()
 if not r then break end
 if sent[#sent].send_gift then gifts=gifts+1;respond('send_gift_ret',{result='success'}) end
end
assert(gifts==3,'must gift all 3 friends, got '..gifts)
assert(#social.unsend_gift_list==0 and A.daily_state.gifts_done)
A:cancel_task()
`);
test('daily extras re-sign when a new check-in period opens',`
reset_daily_fixture();A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,social=true}
social.unsend_gift_list={};daily.liveness_list={}
checked_in=false
local r=A:daily_extras();assert(r and sent[#sent].take_check_in_reward)
respond('take_check_in_reward_ret',{result='success'})
checked_in=false
r=A:daily_extras();assert(r and sent[#sent].take_check_in_reward)
respond('take_check_in_reward_ret',{result='success'})
checked_in=true
assert(A:daily_extras()==nil,'signed periods must not be claimed again')
A:cancel_task()
`);
test('daily claims the adventure month card reward once',`
reset_daily_fixture();checked_in=true
month_cards={vip_list={[1]={end_time=clock+86400,reward_mark=1},[2]={end_time=clock+86400,reward_mark=2}}}
package.loaded['logic.vip']=month_cards
constants.VIP_STATE={unbuy=0,buy=1,daily_reward=2}
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,social=true}
social.unsend_gift_list={};daily.liveness_list={};daily.activity_receive_list={}
local r=A:daily_extras();assert(sent[#sent].query_vip)
respond('query_vip_ret',{result='success'});A.daily_state.loaded.vip=true
r=A:daily_extras();assert(sent[#sent].take_vip_reward and sent[#sent].take_vip_reward.vip_type==1)
respond('take_vip_reward_ret',{result='success',vip_type=1})
assert(A:daily_extras()==nil,'a taken month card must not be claimed again')
package.loaded['logic.vip']=nil
A:cancel_task()
`);
test('daily full run gifts all friends and runs the void exchange phase',`reset_daily_fixture()
social.unsend_gift_list={31,32}
A:start_daily()
local result,gifts,exchanges,seen=nil,0,0,0
for tick=1,400 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 for i=seen+1,#sent do
  if sent[i].send_gift then gifts=gifts+1 end
  if sent[i].vanity_exchange_goods then exchanges=exchanges+1 end
 end
 seen=#sent
 if result.done then break end
end
assert(result and result.done,tostring(result and result.message))
assert(gifts==2,'must gift both friends, got '..gifts)
assert(result.message:find('虚空兑换'),'summary must include the void exchange phase')
`);
test('daily underground adventure fights the highest unlocked level',`
reset_daily_fixture()
mining.cave_config_info={[7]={1}}
mining.cave_levels={[1]=3}
mining.cave_challenge_nums={[1]=5}
config_manager.mining_event_config={[1]={
 {cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'},
 {cave_type=1,level=2,bp_limit=0,pickaxe_count=2,name='地下探险'},
 {cave_type=1,level=3,bp_limit=0,pickaxe_count=3,name='地下探险'}}}
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask10=true
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,caves=true}
daily.liveness_list={{liveness_id=10,completion_count=0,is_reward=false}};daily.activity_receive_list={}
local result
for tick=1,60 do A.now=tick;clock=1000+tick;result=A:daily_step();service_daily_request();if result.done then break end end
local solved
for _,m in ipairs(sent)do if m.solve_cave_event then solved=m.solve_cave_event end end
assert(solved and solved.cave_lv==3,'must fight the highest level, got '..tostring(solved and solved.cave_lv))
mining.cave_config_info={[7]={1,3}};mining.cave_levels={[1]=3,[3]=3};mining.cave_challenge_nums={[1]=5,[3]=5}
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔巢穴'}}}
`);
test('daily workshop production uses the same tier as the workshop task',`
reset_daily_fixture()
mining.dig_depth=260;mining.dig_count=500
resource_counts[5]=30
config_manager.mining_quarry_config={
 {ID=1,name='浅层',need_layer=0,need_time=60,dig_count=60,tnt_count=0},
 {ID=2,name='中层',need_layer=35,need_time=120,dig_count=120,tnt_count=1},
 {ID=3,name='深层',need_layer=250,need_time=240,dig_count=240,tnt_count=6}}
A.cfg.workshopDepth=150;A.cfg.workshopMinutes=120
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask2=true
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,mining=true}
daily.liveness_list={{liveness_id=2,completion_count=0,is_reward=false}};daily.activity_receive_list={}
local result
for tick=1,60 do A.now=tick;clock=1000+tick;result=A:daily_step();service_daily_request();if result.done then break end end
local added
for _,m in ipairs(sent)do if m.add_mining_project then added=m.add_mining_project end end
assert(added and added.id==2,'must pick the 120-minute tier at the 150-layer cap, got '..tostring(added and added.id))
config_manager.mining_quarry_config={{ID=1,name='日常生产',need_layer=0,need_time=120,dig_count=60,tnt_count=0}}
mining.dig_depth=200;mining.dig_count=100
`);
test('daily collects all ready workshop jobs and fills their slots even after activity is complete',`
reset_daily_fixture()
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask2=true;A.cfg.dailyGiftAll=false;A.cfg.dailyVipReward=false;A.cfg.dailyVoidExchange=false
mining.dig_count=300;mining.max_project_count=3
mining.project_list={{id=1,endtime=900},{id=1,endtime=950},{id=1,endtime=9000}}
server_counts[2]=daily_conf_for_test[2].condition_count
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,mining=true};daily.liveness_list={}
for i=1,85 do
 A.now=i;local result=A:daily_step();service_daily_request()
 if result.done then break end
end
local operations={}
for _,request in ipairs(sent)do
 if request.get_mining_project_reward then operations[#operations+1]='collect' end
 if request.add_mining_project then operations[#operations+1]='produce' end
end
assert(table.concat(operations,',')=='collect,collect,produce,produce',table.concat(operations,','))
assert(#mining.project_list==3 and mining.project_list[1].endtime==9000)
`);
test('scheduled daily checks the time slot before activities and avoids a rune prompt',`
reset_daily_fixture()
A.cfg.dailyTask1=false;A.cfg.dailyCheckinAll=false;A.cfg.dailyTask13=true;A.cfg.dailyEvents=true
A:start_daily(true,true)
assert(A.daily_state.skipped[13] and not A.daily_state.events_ran)
local trace={}
for tick=1,10 do
 A.now=tick;local result=A:daily_step();trace[#trace+1]=result.message;service_daily_request()
 if A.daily_state.events_ran then break end
end
assert(sent[1] and sent[1].query_daily and sent[2] and sent[2].take_check_in_reward,table.concat(trace,' / '))
assert(checked_in and A.daily_state.events_ran and not A.daily_state.rune_selection)
A.cfg.dailyCheckinAll=true;A.cfg.dailyGiftAll=true;A.cfg.dailyVipReward=true;A.cfg.dailyVoidExchange=true
`);
test('daily workshop rejects a partially affordable batch and carries its camp-status reason',`
reset_daily_fixture()
local original=config_manager.mining_quarry_config
config_manager.mining_quarry_config={{ID=1,name='浅层',need_layer=0,need_time=120,dig_count=30,tnt_count=0},
 {ID=8,name='深层',need_layer=120,need_time=120,dig_count=60,tnt_count=6}}
mining.dig_depth=130;mining.dig_count=0;mining.max_project_count=2
resource_counts[5]=10;resource_counts[7]=100
A.cfg.workshopDepth=150;A.cfg.workshopMinutes=120
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask2=true
A:start_daily();A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,mining=true}
daily.liveness_list={{liveness_id=2,completion_count=0,is_reward=false}}
local blocked
for tick=1,60 do
 A.now=tick;clock=1000+tick
 local result=A:daily_step();service_daily_request();blocked=blocked or result.workshop_block
 if result.done then break end
end
assert(blocked=='工坊雷管不足')
for _,msg in ipairs(sent) do assert(not msg.add_mining_project and not msg.use_mining_tool and not msg.use_item) end
config_manager.mining_quarry_config=original
`);
test('daily guild boss phase buys all remaining tickets and challenges once',`
reset_daily_fixture()
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=true
A.cfg.spendBudget=2000;A.cfg.spendBase=0;A.cfg.spendDay=0
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
daily.liveness_list={};daily.activity_receive_list={}
local result,seen_buys,seen_challenges,counted=0,0,0,0
for tick=1,200 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 for i=counted+1,#sent do
  local m=sent[i]
  if m.guild_boss_buy_ticket then seen_buys=seen_buys+1;local c=m.guild_boss_buy_ticket.count;assert(c==10,'must buy all 10 tickets at once, got '..tostring(c)) end
  if m.guild_boss_challenge then seen_challenges=seen_challenges+1 end
 end
 counted=#sent
 if result.done then break end
end
assert(result and result.done and not result.error,tostring(result and result.message))
assert(seen_buys==1,'must send exactly one buy request, got '..seen_buys)
assert(seen_challenges==1,'must challenge the guild boss exactly once, got '..seen_challenges)
assert(guild.guild_boss_buy_count==10,'server must acknowledge all tickets')
assert(resource_counts[51]==9,'10 bought minus 1 challenge = 9 tickets left, got '..tostring(resource_counts[51]))
assert(result.message:find('工会首领'),'summary must mention the guild phase: '..tostring(result.message))
assert((A.spend_session or 0)==420,'must record 420 blood diamonds, got '..tostring(A.spend_session))
`);
test('daily guild boss phase skips tickets when actual blood is insufficient',`
reset_daily_fixture()
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=true
A.cfg.spendBudget=0;A.cfg.spendBase=0;A.cfg.spendDay=0
resource_counts[1]=0;function resource_logic:CheckResourceNum(id,n)return (resource_counts[id] or 0)>=n end
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
daily.liveness_list={};daily.activity_receive_list={}
local result
for tick=1,200 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 if result.done then break end
end
assert(result and result.done,tostring(result and result.message))
for _,m in ipairs(sent)do assert(not m.guild_boss_buy_ticket,'zero budget must forbid buying tickets') end
for _,m in ipairs(sent)do assert(not m.guild_boss_challenge,'without tickets there must be no challenge') end
`);
test('daily guild boss phase still challenges with pre-owned tickets when purchase fails',`
reset_daily_fixture()
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=true
A.cfg.spendBudget=2000
resource_counts[51]=2
network.event_listener.guild_boss_buy_ticket_ret=function(msg)
 guild.buy_fail=(guild.buy_fail or 0)+1
end
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
daily.liveness_list={};daily.activity_receive_list={}
local result,challenges,counted=0,0,0
for tick=1,200 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 for i=counted+1,#sent do
  if sent[i].guild_boss_challenge then challenges=challenges+1 end
 end
 counted=#sent
 if result.done then break end
end
assert(result and result.done,tostring(result and result.message))
assert(challenges==1,'must still challenge once with the 2 pre-owned tickets, got '..challenges)
`);
test('daily underground adventure buys and fights every attempt even after liveness is complete',`
reset_daily_fixture()
mining.cave_config_info={[7]={1,2}}
mining.cave_levels={[1]=3,[2]=3}
mining.cave_challenge_nums={[1]=3,[2]=3}
mining.cave_buy_challenge_nums={[1]=3,[2]=3}
config_manager.mining_event_config={
 [1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'},
  {cave_type=1,level=2,bp_limit=0,pickaxe_count=1,name='地下探险'},
  {cave_type=1,level=3,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [2]={{cave_type=2,level=1,bp_limit=0,pickaxe_count=1,name='废墟探险'},
  {cave_type=2,level=2,bp_limit=0,pickaxe_count=1,name='废墟探险'},
  {cave_type=2,level=3,bp_limit=0,pickaxe_count=1,name='废墟探险'}}}
network.event_listener.buy_cave_challenge_ret=function(msg)
 if msg.result=='success' then
  mining.cave_buy_challenge_nums[msg.cave_type]=mining.cave_buy_challenge_nums[msg.cave_type]-1
  mining.cave_challenge_nums[msg.cave_type]=mining.cave_challenge_nums[msg.cave_type]+1
 end
end
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask10=true
-- 活跃度任务10已经完成（比如玩家手动打过）：仍必须买满次数并打完
daily.liveness_list={{liveness_id=10,completion_count=99,is_reward=false}};daily.activity_receive_list={}
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,caves=true,mining=true}
local result,buys,solves,counted=0,0,0,0
for tick=1,400 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 for i=counted+1,#sent do
  local m=sent[i]
  if m.buy_cave_challenge then buys=buys+1 end
  if m.solve_cave_event then solves=solves+1 end
 end
 counted=#sent
 if result.done then break end
end
assert(result and result.done,tostring(result and result.message))
assert(buys==6,'underground adventure must buy all 6 attempts (2 kinds x 3), got '..buys)
assert(solves==12,'underground adventure must fight all 12 attempts (2 kinds x (3 free + 3 bought)), got '..solves)
assert(mining.cave_challenge_nums[1]==0 and mining.cave_challenge_nums[2]==0,'all challenge counts must be spent')
mining.cave_config_info={[7]={1,3}};mining.cave_levels={[1]=3,[3]=3};mining.cave_challenge_nums={[1]=5,[3]=5};mining.cave_buy_challenge_nums=nil
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔巢穴'}}}
`);
test('daily troll lair keeps fighting leftover attempts after liveness is complete',`
reset_daily_fixture()
mining.cave_config_info={[7]={3}}
mining.cave_levels={[3]=3}
mining.cave_challenge_nums={[3]=2}
mining.cave_buy_challenge_nums={[3]=0}
config_manager.mining_event_config={
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔幼儿园'},
  {cave_type=3,level=3,bp_limit=0,pickaxe_count=1,name='巨魔幼儿园'}}}
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask9=true
daily.liveness_list={{liveness_id=9,completion_count=99,is_reward=false}};daily.activity_receive_list={}
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,caves=true,mining=true}
local result,solves,counted=0,0,0
for tick=1,200 do
 A.now=tick;clock=1000+tick
 result=A:daily_step();service_daily_request()
 for i=counted+1,#sent do
  if sent[i].solve_cave_event then solves=solves+1 end
 end
 counted=#sent
 if result.done then break end
end
assert(result and result.done,tostring(result and result.message))
assert(solves==2,'must spend the 2 leftover attempts even with liveness complete, got '..solves)
assert(mining.cave_challenge_nums[3]==0,'troll lair count must be exhausted')
mining.cave_config_info={[7]={1,3}};mining.cave_levels={[1]=3,[3]=3};mining.cave_challenge_nums={[1]=5,[3]=5};mining.cave_buy_challenge_nums=nil
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔巢穴'}}}
`);
test('daily cave tasks stop cleanly when a kind cannot be fought',`
reset_daily_fixture()
mining.cave_config_info={[7]={1}}
mining.cave_levels={[1]=0}
mining.cave_challenge_nums={[1]=2}
mining.cave_buy_challenge_nums={[1]=0}
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}}}
for id=1,15 do A.cfg['dailyTask'..id]=false end
A.cfg.dailyTask10=true
A:start_daily()
A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,caves=true,mining=true}
daily.liveness_list={{liveness_id=10,completion_count=99,is_reward=false}};daily.activity_receive_list={}
local result
for tick=1,60 do A.now=tick;clock=1000+tick;result=A:daily_step();service_daily_request();if result.done then break end end
assert(result and result.done,'must finish instead of looping when the open cave level is not reached')
assert(A.daily_state.skipped[10],'unfightable cave must be skipped with a reason')
mining.cave_config_info={[7]={1,3}};mining.cave_levels={[1]=3,[3]=3};mining.cave_challenge_nums={[1]=5,[3]=5}
config_manager.mining_event_config={[1]={{cave_type=1,level=1,bp_limit=0,pickaxe_count=1,name='地下探险'}},
 [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔巢穴'}}}
`);
test('daily events buys every attempt, advances campaign and converts exp into score',`
setup_daily_events(2000)
local result=run_daily_events(400)
assert(result and result.done and not result.error,tostring(result and result.message))
assert(count_event('buy_campaign_overtime')==2,'must buy all 2 campaign overtime attempts, got '..count_event('buy_campaign_overtime'))
assert(count_event('battle_campaign')==4,'must spend all 4 campaign attempts (2 free + 2 bought), got '..count_event('battle_campaign'))
assert(count_event('convert_campaign_exp')>=2,'must convert campaign exp into score until exp runs out')
assert(count_event('expedition_apend_times')==1,'expedition attempts should be bought in one request')
local bought=0
for _,m in ipairs(sent)do if m.expedition_apend_times then bought=bought+(m.expedition_apend_times.num or 0) end end
assert(bought==3,'must buy all 3 expedition challenge attempts, got '..bought)
assert(count_event('expedition_fight')==5,'must fight all 5 expedition attempts, got '..count_event('expedition_fight'))
assert(campaign_state.score>=50,'campaign score must grow from battles and conversions, got '..campaign_state.score)
assert((A.spend_session or 0)==590,'must record 2x20 + (100+150+300) = 590 blood diamonds, got '..tostring(A.spend_session))
assert(result.message:find('合战与宿命远征'),'summary must mention the new phase: '..tostring(result.message))
`);
test('daily events keeps free attempts when the actual blood balance is zero',`
setup_daily_events(0)
resource_counts[1]=0;function resource_logic:CheckResourceNum(id,n)return (resource_counts[id] or 0)>=n end
local result=run_daily_events(400)
assert(result and result.done,tostring(result and result.message))
assert(count_event('buy_campaign_overtime')==0,'zero budget must forbid campaign overtime')
assert(count_event('expedition_apend_times')==0,'zero budget must forbid expedition attempts')
assert(count_event('battle_campaign')==2,'free campaign attempts must still be fought')
assert(count_event('expedition_fight')==2,'free expedition attempts must still be fought')
assert((A.spend_session or 0)==0,'no blood diamonds may be recorded')
`);
test('daily events skips closed activities without touching later daily phases',`
setup_daily_events(2000)
campaign_state.status=1
expedition_state.duration=clock-1
local result=run_daily_events(200)
assert(result and result.done,tostring(result and result.message))
for _,m in ipairs(sent)do
 assert(not m.battle_campaign and not m.expedition_fight and not m.buy_campaign_overtime and not m.expedition_apend_times,
  'closed activities must not buy or fight')
end
assert(result.message:find('合战与宿命远征阶段结束'),'summary must report the phase: '..tostring(result.message))
`);
test('daily events keeps free attempts and retries a rejected purchase after other activities',`
setup_daily_events(2000)
campaign_buy_fail=true
local result
for tick=1,900 do A.now=tick;clock=1000+tick;result=A:daily_step();service_daily_request();if count_event('expedition_apend_times')>0 then campaign_buy_fail=false end;if result.done then break end end
assert(result and result.done,tostring(result and result.message))
assert(count_event('buy_campaign_overtime')==3,'failed request must be retried after the queue pass')
assert(count_event('battle_campaign')==4,'free and subsequently purchased campaign attempts must be fought')
assert(count_event('expedition_apend_times')==1,'expedition phase must still run')
`);
test('daily events refreshes expedition rivals for free when opponents run out',`
setup_daily_events(2000)
expedition_fixed_enemies=true
expedition_state.players={{enemy_id=201}}
local result=run_daily_events(400)
assert(result and result.done,tostring(result and result.message))
assert(count_event('refresh_expedition_search')>=1,'must use a free search refresh when opponents are exhausted')
assert(count_event('expedition_fight')>=3,'must keep fighting after refreshing rivals')
for _,m in ipairs(sent)do
 if m.expedition_apend_times then assert(m.expedition_apend_times.type==1,'only challenge attempts may be bought') end
end
`);
test('daily events stops converting when the score exchange is sold out',`
setup_daily_events(2000)
campaign_state.buff_info_map[6].req_exp=0
local result=run_daily_events(400)
assert(result and result.done,tostring(result and result.message))
assert(count_event('convert_campaign_exp')==0,'sold out score exchange must not be requested')
assert(count_event('battle_campaign')==4,'campaign attempts must still be spent')
`);
};
