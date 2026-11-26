module.exports=({run,test,toLua})=>{
run(`
bag_logic={item_list={},capacity=20}
function bag_logic:UseItem(item_id) network:Send({use_item={item_id=item_id}}) end
package.loaded['logic.bag']=bag_logic
mining.project_list={}
mining.max_project_count=1
mining.dig_depth=180
mining.dig_count=100
resource_counts={}
function resource_logic:GetResourceNum(id) return resource_counts[id] or 0 end
function resource_logic:CheckResourceNum(id,num) return (resource_counts[id] or 0)>=num end
config_manager.mining_quarry_config={
 {ID=2,name='浅表L2',need_layer=0,need_time=120,dig_count=25,tnt_count=0},
 {ID=5,name='中层L2',need_layer=35,need_time=120,dig_count=40,tnt_count=2},
 {ID=8,name='深层L2',need_layer=120,need_time=120,dig_count=40,tnt_count=10},
 {ID=9,name='深层L3',need_layer=120,need_time=240,dig_count=80,tnt_count=15},
 {ID=11,name='地心L2',need_layer=250,need_time=120,dig_count=40,tnt_count=40}}
guild={guild_cur_boss_id=3,guild_boss_over=false,fright=0}
function guild:GetFrightBossState() return self.fright or 0 end
function guild:IsOpenGuildBoss() return true end
function guild:QueryGuildBossInfo() network:Send({query_guild_boss_info={}}) end
package.loaded['logic.guild']=guild
network.event_listener.query_mining_info_ret=function(msg)
 if msg.dig_count~=nil then mining.dig_count=msg.dig_count end
 if msg.dig_depth~=nil then mining.dig_depth=msg.dig_depth end
 if msg.project_list~=nil then mining.project_list=msg.project_list end
 if msg.project_max_count~=nil then mining.max_project_count=msg.project_max_count end
end
network.event_listener.query_bag_info_ret=function(msg)
 if msg.item_list then bag_logic.item_list=msg.item_list end
 if msg.max_count then bag_logic.capacity=msg.max_count end
end
network.event_listener.use_item_ret=function(msg)
 if msg.result=='success' then
  for i,item in ipairs(bag_logic.item_list) do
   if item.item_id==msg.item_id then table.remove(bag_logic.item_list,i);break end
  end
  mining.dig_count=mining.dig_count+(tonumber(msg.add_dig) or 0)
 end
end
network.event_listener.use_mining_tool_ret=function(msg)
 if msg.result=='success' then
  resource_counts[msg.resource_id]=(resource_counts[msg.resource_id] or 0)-(tonumber(msg.num) or 1)
  mining.dig_count=mining.dig_count+(tonumber(msg.add_dig) or 5)
 end
end
network.event_listener.add_mining_project_ret=function(msg)
 if msg.result=='success' then
  mining.project_list=msg.project_list or mining.project_list
 end
end
network.event_listener.get_mining_project_reward_ret=function(msg)
 if msg.result=='success' then mining.project_list=msg.project_list or {} end
end
network.event_listener.query_guild_boss_info_ret=function(msg)
 if msg.result=='success' then
  guild.guild_cur_boss_id=msg.boss_id or guild.guild_cur_boss_id
  guild.guild_boss_over=false
 end
end
network.event_listener.guild_boss_challenge_ret=function(msg)
 if msg.result=='success' then
  guild.fright=2
  resource_counts[51]=math.max(0,(resource_counts[51] or 0)-1)
 end
end
network.event_listener.guild_boss_update_ret=function(msg) end
function reset_workshop()
 constants.RESOURCE_TYPE.ultimate_tool=29;A.cfg.earthUseAll=false;A.cfg.reserveTnt=0
 A:cancel_task();A._cancelled_task_pending=nil;A.now=0;clock=1000;A.inBattle=false;sent={}
 mining.project_list={};mining.max_project_count=1;mining.dig_depth=180;mining.dig_count=100
 bag_logic.item_list={};bag_logic.capacity=20
 resource_counts={[5]=100,[29]=0,[51]=2}
constants.RESOURCE_TYPE.guild_boss_ticket=51
 A.cfg.workshopDepth=150;A.cfg.workshopMinutes=120;A.cfg.guildBossCount=3;A.cfg.autoUseEarthHeart=true
 guild.guild_cur_boss_id=3;guild.guild_boss_over=false;guild.fright=0
end
function last() return sent[#sent] end
function begin_workshop(mode,dig,depth)
 reset_workshop();mining.dig_count=dig or 100;mining.dig_depth=depth or 180
 A:start_workshop(mode)
 A:workshop_step()
 respond('query_mining_info_ret',{})
end
`);
test('workshop reports the selected tier depth and aborts before spending any materials',`
begin_workshop('produce',0,50)
resource_counts[29]=100;bag_logic.item_list={{item_id=77,template_id=6000005}}
local result=A:workshop_step()
assert(result.done and result.workshop_block=='工坊深度不足' and result.workshop_detail:find('120') and result.workshop_detail:find('50'))
assert(#sent==1 and last().query_mining_info and not A.fast_refill)
`);
test('workshop checks dynamite for all empty slots and reserves before refilling picks',`
begin_workshop('produce',0,180);mining.max_project_count=3
resource_counts[5]=24;resource_counts[29]=100;A.cfg.reserveTnt=4
local result=A:workshop_step()
assert(result.done and result.workshop_block=='工坊雷管不足' and result.workshop_detail:find('30'))
assert(#sent==1 and not A.fast_refill)
`);
test('workshop fills each slot at the same tier with exactly enough dynamite and reserve',`
begin_workshop('produce',100,180);mining.max_project_count=2
resource_counts[5]=24;A.cfg.reserveTnt=4
for i=1,2 do
 A:workshop_step();assert(last().add_mining_project and last().add_mining_project.id==8)
 table.insert(mining.project_list,{id=8,endtime=5000})
 resource_counts[5]=resource_counts[5]-10;mining.dig_count=mining.dig_count-40
 respond('add_mining_project_ret',{result='success',id=8})
end
assert(A:workshop_step().done and A.workshop_state.produced==2 and resource_counts[5]==4)
`);
test('workshop production prefers the configured deep 120-minute project',`
begin_workshop('produce',100,180)
local result=A:workshop_step()
assert(last().add_mining_project and last().add_mining_project.id==8,'wrong project: '..tostring(last().add_mining_project and last().add_mining_project.id))
respond('add_mining_project_ret',{result='success',id=8,project_list={{id=8,endtime=clock+7200}}})
result=A:workshop_step()
assert(result.done and not result.error and A.workshop_state.produced==1,tostring(result.message))
`);
test('workshop depth cap of 150 prevents earth-core projects',`
begin_workshop('produce',100,300)
A:workshop_step()
assert(last().add_mining_project.id==8,'depth cap must select 120-layer project')
`);
test('workshop collect drains every completed project',`
reset_workshop();mining.project_list={{id=1,endtime=clock-1},{id=2,endtime=clock+500}}
A:start_workshop('collect');A:workshop_step();respond('query_mining_info_ret',{})
local result=A:workshop_step();assert(last().get_mining_project_reward)
respond('get_mining_project_reward_ret',{result='success',project_list={{id=2,endtime=clock+500}}})
result=A:workshop_step()
assert(result.done and A.workshop_state.claimed==1,tostring(result.message))
`);
test('workshop refill uses the adventure earth heart before a mining bag',`
reset_workshop();mining.dig_count=10;resource_counts[29]=1
bag_logic.item_list={{item_id=77,template_id=6000005}}
A:start_workshop('produce');A:workshop_step();respond('query_mining_info_ret',{})
A:workshop_step();assert(last().use_item and last().use_item.item_id==77)
respond('use_item_ret',{result='success',item_id=77,add_dig=30})
A:workshop_step();assert(last().query_mining_info)
respond('query_mining_info_ret',{})
A:workshop_step();assert(last().add_mining_project and last().add_mining_project.id==8)
assert(resource_counts[29]==1,'must preserve the mining bag')
`);
test('workshop uses up to five mining bags at once',`
reset_workshop();mining.dig_count=10;resource_counts[29]=5
A:start_workshop('produce');A:workshop_step();respond('query_mining_info_ret',{})
A:workshop_step();assert(last().use_mining_tool and last().use_mining_tool.num==5)
`);
test('workshop refill stops if the authoritative pickaxe count does not rise',`
reset_workshop();mining.dig_count=10;resource_counts[29]=1
A:start_workshop('produce');A:workshop_step();respond('query_mining_info_ret',{})
A:workshop_step();respond('use_mining_tool_ret',{result='success',resource_id=29,num=1,add_dig=0})
A:workshop_step();respond('query_mining_info_ret',{})
local result=A:workshop_step()
assert(result.done and result.error and result.message:find('未增加'),tostring(result.message))
`);
test('guild boss challenge counts only acknowledged attacks and stops at the target',`
reset_workshop();A.cfg.guildBossCount=2
A:start_guild_boss(2);A:guild_boss_step();respond('query_guild_boss_info_ret',{result='success',boss_id=3})
A:guild_boss_step();assert(last().guild_boss_challenge and last().guild_boss_challenge.boss_id==3)
respond('guild_boss_challenge_ret',{result='success',boss_id=3,boss_bp=100,attack_count=1})
local result=A:guild_boss_step();assert(result.wait and A.guild_state.count==1)
guild.fright=0;A.inBattle=false
result=A:guild_boss_step();assert(last().query_guild_boss_info)
respond('query_guild_boss_info_ret',{result='success',boss_id=3})
A:guild_boss_step();assert(last().guild_boss_challenge)
respond('guild_boss_challenge_ret',{result='success',boss_id=3,boss_bp=0,attack_count=2})
A:guild_boss_step();guild.fright=0;A.inBattle=false
result=A:guild_boss_step();assert(result.done and A.guild_state.count==2,tostring(result.message))
`);
test('guild closes the reward panel so the fight state resets',`
reset_workshop();resource_counts[51]=1
local closed=false
local panel_visible=false
local mock_scene={sub_panels={reward_panel={
 IsVisible=function(self) return panel_visible end,
 PopRewardInfoFromQueue=function(self) panel_visible=false;closed=true;guild.fright=0 end}}}
cc={Director={getInstance=function() return {getRunningScene=function() return mock_scene end} end}}
A:start_guild_boss(2);A:guild_boss_step();respond('query_guild_boss_info_ret',{result='success',boss_id=3})
A:guild_boss_step();respond('guild_boss_challenge_ret',{result='success',boss_id=3,boss_bp=10,attack_count=1})
A:guild_boss_step()
assert(not closed,'panel is not visible during the battle')
panel_visible=true
A:guild_boss_step()
assert(closed,'reward panel must be closed through its own handler')
assert(last().query_guild_boss_info)
cc=nil
`);
test('guild boss stops when the feature is closed',`
reset_workshop()
guild.IsOpenGuildBoss=function(self) return false end
A:start_guild_boss(1)
local result=A:guild_boss_step()
assert(result.done and result.message:find('功能尚未开启'),tostring(result.message))
guild.IsOpenGuildBoss=function(self) return true end
`);
test('guild boss stops without buying a ticket',`
reset_workshop();resource_counts[51]=0
A:start_guild_boss(1);A:guild_boss_step();respond('query_guild_boss_info_ret',{result='success',boss_id=3})
local result=A:guild_boss_step()
assert(result.done and result.message:find('许可证'),tostring(result.message))
for _,message in ipairs(sent)do assert(not message.guild_boss_buy_ticket)end
`);
test('guild boss rejects a missing boss without spending',`
reset_workshop();guild.guild_cur_boss_id=0
A:start_guild_boss(1);A:guild_boss_step();respond('query_guild_boss_info_ret',{result='success',boss_id=0})
local result=A:guild_boss_step()
assert(result.done and not result.error and #sent==1,tostring(result.message))
`);
test('daily guild ticket phase ignores old budgets and buys the full allowance',`
reset_workshop()
guild.guild_boss_buy_count=0
guild.GetBuyTicketMax=function(self) return math.max(0,10-(self.guild_boss_buy_count or 0)) end
guild.BuyTicketCost=function(self,num)
 local cost=0
 for i=(self.guild_boss_buy_count or 0)+1,(self.guild_boss_buy_count or 0)+num do
  cost=cost+(i<=3 and 20 or (i<=6 and 40 or 60))
 end
 return cost
end
network.event_listener.guild_boss_buy_ticket_ret=function(msg)
 if msg.result=='success' then
  guild.guild_boss_buy_count=(guild.guild_boss_buy_count or 0)+(tonumber(msg.count) or 0)
  resource_counts[51]=(resource_counts[51] or 0)+(tonumber(msg.count) or 0)
 end
end
A.cfg.spendBudget=200;A.cfg.spendBase=0;A.cfg.spendDay=0;A.spend_session=0
resource_counts[1]=10000
A:start_daily_guild_boss(6,1)
local result
for tick=1,20 do
 A.now=tick;clock=1000+tick
 result=A:daily_guild_ticket_step()
 local p=A._task_pending
 if p and not p.response then respond(p.event,{result='success',count=p.buy_count}) end
 if result.done then break end
end
assert(result.done,tostring(result.message))
local bought
for _,m in ipairs(sent)do if m.guild_boss_buy_ticket then bought=m.guild_boss_buy_ticket.count end end
-- 6张需180+? 阶梯20x3+40x3=180 ≤ 200 → 全买；把预算压到100 → 只买前3张(60)再买第4张(40)共100
assert(bought==6,'200 budget must cover all 6 tickets (cost 180), got '..tostring(bought))
-- 预算100：只能买3张(20x3=60)，第4张需40 → 总100恰好；用90再测
A:start_daily_guild_boss(6,1)
A.cfg.spendBudget=90;A.spend_session=0
for tick=1,20 do
 A.now=tick;clock=1000+tick
 result=A:daily_guild_ticket_step()
 local p=A._task_pending
 if p and not p.response then respond(p.event,{result='success',count=p.buy_count}) end
 if result.done then break end
end
local bought2
for _,m in ipairs(sent)do if m.guild_boss_buy_ticket then bought2=m.guild_boss_buy_ticket.count end end
-- 已购6张后阶梯从第7张起(60/张)：90预算只能买1张
assert(bought2==4,'remaining 4 tickets must be bought despite the old 90 budget, got '..tostring(bought2))
guild.GetBuyTicketMax=nil;guild.BuyTicketCost=nil
guild.guild_boss_buy_count=0
`);

run(`
function begin_gb124()
 reset_workshop();A:start_guild_boss(3);A:guild_boss_step()
 respond('query_guild_boss_info_ret',{result='success',boss_id=3});A:guild_boss_step()
 assert(last().guild_boss_challenge)
end
function gb124_failure()
 respond('guild_boss_challenge_ret',{result='failure'});local result=A:guild_boss_step()
 assert(not result.done and A.guild_state.count==0)
 A.now=A.now+.3;A:guild_boss_step();assert(last().query_guild_boss_info)
end
`);
test('guild boss literal failure refreshes authoritative state before retrying',`
 begin_gb124();local count=#sent;gb124_failure();assert(#sent==count+1)
 respond('query_guild_boss_info_ret',{result='success',boss_id=4});A:guild_boss_step()
 assert(last().guild_boss_challenge.boss_id==4 and A.guild_state.count==0)
`);
test('guild boss known failure continues beyond the old fixed retry limits',`
 begin_gb124()
 for i=1,30 do
  gb124_failure();respond('query_guild_boss_info_ret',{result='success',boss_id=3});local r=A:guild_boss_step()
  assert(not r.done and last().guild_boss_challenge)
 end
 assert(A.guild_state.failures==30 and A.guild_state.count==0)
`);
test('guild boss stops after failure when refreshed tickets are exhausted',`
 begin_gb124();gb124_failure();resource_counts[51]=0
 respond('query_guild_boss_info_ret',{result='success',boss_id=3});local count=#sent;local r=A:guild_boss_step()
 assert(r.done and r.message:find('许可证') and #sent==count)
`);
test('guild boss stops after failure when the refreshed trial has ended',`
 begin_gb124();gb124_failure();respond('query_guild_boss_info_ret',{result='success',boss_id=3});guild.guild_boss_over=true
 local count=#sent;local r=A:guild_boss_step();assert(r.done and #sent==count)
`);
test('guild boss real errors still stop instead of being treated as the known failure',`
 begin_gb124();respond('guild_boss_challenge_ret',{result='not_enough_resource'})
 local count=#sent;local r=A:guild_boss_step();assert(r.done and r.error and #sent==count)
`);
test('guild boss uncertain timeouts never replay a possibly accepted challenge',`
 begin_gb124();A.now=30;local count=#sent;local r=A:guild_boss_step()
 assert(r.wait and not r.done and #sent==count+1 and sent[#sent].query_resource_list)
`);
test('manual cancellation after a guild failure cannot continue challenging',`
 begin_gb124();respond('guild_boss_challenge_ret',{result='failure'});A:guild_boss_step();local count=#sent
 A:cancel_task();A.now=10;local r=A:guild_boss_step();assert(r.done and #sent==count)
`);
};
