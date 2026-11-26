module.exports=({run,test})=>{
run(`
function clone(value)
 if type(value)~='table' then return value end
 local out={} for k,v in pairs(value)do out[k]=clone(v)end return out
end
function reset_research_mine(slots)
 A.cfg.mineResearchMask=-1;A.cfg.mineResearch=0
 A:cancel_task();A._cancelled_task_pending=nil;A.now=0;clock=1000;sent={};originals={}
 bp_ok=true;formation_ok=true;A.inBattle=false;server_stock={};resource_counts={}
 server_mines=slots or {{mine_index=1,status=1}}
 server_research={};for i=1,6 do server_research[i]={coefficient1=25,coefficient2=25}end
 server_rewards={}
 for index=1,3 do for level=1,5 do
  local rewards={}
  for id in config_manager.mine_config[level].resource_ids:gmatch('%d+') do rewards[#rewards+1]={resource_id=tonumber(id),resource_num=80}end
  server_rewards[#server_rewards+1]={mine_index=index,mine_level=level,all_resource_list={{part_resource_list=rewards}}}
 end end
 mine_logic.mine_info_list=clone(server_mines);mine_logic.all_mine_rewards=nil
 cultivation_logic.cultivation_info=nil
 mine_processed=0
end
function fund_branch(kind,branch,current)
 server_research[kind]['coefficient'..branch]=current
end
function mine_query_response(key)
 if key=='query_mine_info' then respond(key..'_ret',{mine_info_list=clone(server_mines)})
 elseif key=='query_cultivation_info' then respond(key..'_ret',{cultivation_states=clone(server_research)})
 elseif key=='query_resource_list' then respond(key..'_ret',{stocks=clone(server_stock)})
 elseif key=='query_all_mine_rewards' then respond(key..'_ret',{all_mine_rewards=clone(server_rewards)})
 else error('unexpected query '..key)end
end
function next_mine_action()
 for i=1,24 do
  local r=A:mine_step()
  if #sent>mine_processed then
   mine_processed=#sent
   local request=sent[#sent];local key=next(request)
   if key:match('^query_') then mine_query_response(key)
   else return key,request[key],r end
  else if r.done or r.delaySeconds then return nil,nil,r end end
 end
 error('mine step failed to settle')
end
function settle_mine(key,payload)
 if key=='mine_start' then
  for _,info in pairs(server_mines)do if info.mine_index==payload.mine_index then
   info.status=2;info.mine_level=payload.mine_level;info.beg_time=clock;info.end_time=clock+28800
   respond('mine_start_ret',{result='success',mine_info=clone(info)})
  end end
 elseif key=='mine_receive_reward' then
  for _,info in pairs(server_mines)do if info.mine_index==payload.mine_index then
   for _,row in pairs(server_rewards)do if row.mine_index==payload.mine_index and row.mine_level==info.mine_level then
    local last=row.all_resource_list[#row.all_resource_list]
    for _,r in pairs(last.part_resource_list)do server_stock[r.resource_id]=(server_stock[r.resource_id]or 0)+r.resource_num end
   end end
   info.status=1;info.mine_level=0;info.end_time=0
  end end
  respond('mine_receive_reward_ret',{result='success',mine_index=payload.mine_index})
 elseif key=='cultivate' then
  local current=server_research[payload.cultivation_type][payload.coefficient_type]
  local conf=config_manager.cultivation_config[payload.cultivation_type][current+1]
  local costs={}for n in conf.cost_nums:gmatch('%d+')do costs[#costs+1]=tonumber(n)end
  local i=0;for id in conf.resource_ids:gmatch('%d+')do i=i+1;id=tonumber(id)
   assert((server_stock[id]or 0)>=costs[i],'server prevents overspending')
   server_stock[id]=server_stock[id]-costs[i]
  end
  server_research[payload.cultivation_type][payload.coefficient_type]=current+1
  respond('cultivate_ret',{result='success',cur_state=current+1})
 else error('unexpected mutation '..key)end
end
function begin_research_mine() A:start_mine_collect();return next_mine_action()end
`);
test('mine continuation waits for the last active team and rechecks maturity',`
reset_research_mine({{mine_index=1,status=2,mine_level=3,end_time=1100},
    {mine_index=2,status=2,mine_level=3,end_time=1200}})
package.loaded['logic.user']={user_id='MINE',query_queue={}};A.harvest_updates=nil;fund_branch(1,1,11)
local key,p,r=begin_research_mine();r=A:harvest_result(r)
assert(not key and r.done and r.harvest.mine.seconds==200 and r.harvest.mine.account=='MINE')
clock=1150;A:start_mine_collect(nil,true);key,p,r=next_mine_action();r=A:harvest_result(r)
assert(not key and r.done and r.harvest.mine.seconds==50)
for _,request in ipairs(sent)do assert(not request.mine_receive_reward and not request.cultivate)end
clock=1201;A:start_mine_collect(nil,true);key,p=next_mine_action()
assert(key=='mine_receive_reward' and p.mine_index==1);settle_mine(key,p)
key,p=next_mine_action();assert(key=='mine_receive_reward' and p.mine_index==2);settle_mine(key,p)
key,p=next_mine_action();assert(key=='mine_start' or key=='cultivate')
`);
test('mine guardian ranks research material matches before battle power and stops at the live quota',`
reset_research_mine();fund_branch(1,1,11)
local client=package.loaded['util.client_constants'];client.ROB_TYPE={rob=0}
constants.MINE_BE_ROBBED_MAX_TIMES=2
package.loaded['logic.user']={user_id='MINE',query_queue={}}
function mine_logic:MineRobTarget(rob,user,index)
 return network:Send({mine_rob_target={rob_type=rob,target_user_id=user,mine_index=index}})
end
local g={running=true,guard_active=true}
A.daily_state=g
for _,event in ipairs({'query_mine_times_ret','query_mine_rob_target_list_ret','mine_rob_target_ret'})do
 network.event_listener[event]=function()end
end
A:install_task_hooks()
local candidates={{user_id='wrong',mine_index=2,mine_level=1,be_robbed_times=0,battle_point=10},
 {user_id='good',mine_index=1,mine_level=3,be_robbed_times=0,battle_point=100}}
local remaining=1
for tick=1,25 do
 A:daily_guard_step(g)
 if not g.guard_active then break end
 local p=A._task_pending
 if p and not p.response then
  local key,body=next(sent[#sent])
  if key=='query_mine_times' then p.response={mine_times={remain_rob=remaining}}
  elseif key=='query_mine_rob_target_list' then p.response={rob_target_list=candidates}
  elseif key=='mine_rob_target' then
   assert(body.target_user_id=='good' and remaining==1)
   remaining=0;p.response={result='success',is_winner=true}
  else mine_query_response(key) end
 end
end
assert(not g.guard_active and remaining==0 and g.guard_state.fought==1 and g.guard_state.won==1)
for _,request in ipairs(sent)do assert(not request.mine_buy_times and not request.refresh_mine_rob_target_list)end
`);
test('daily follow-up obeys account and date and selects only unfinished items',`
package.loaded['logic.user']={user_id='MINE',query_queue={}}
package.loaded['logic.daily']={liveness_list={},activity_receive_list={}}
A.cfg.dailyTask1=true;A.cfg.dailyTask9=true;A.cfg.dailyMarketExchange=true;A.cfg.dailyMineGuards=true
assert(not A:start_daily_unfinished('OTHER',0,{['9']=true}))
assert(not A:start_daily_unfinished('MINE',1,{['9']=true}))
assert(A:start_daily_unfinished('MINE',0,{['9']=true,market_exchange=true}))
local st=A.daily_state
assert(not st.selected[1] and st.selected[9] and not st.mine_guards and st.market_exchange)
assert(not st.gold_enabled and not st.include_mine and not st.rewards)
local previous=mining;mining={cave_config_info={}}
local report=A:daily_report(true)
mining=previous
assert(report.account=='MINE' and #report.rows==2 and report.rows[2].key=='market_exchange')
`);
test('mining outline paints a target and next cell only when the route changes',`
local store={draws=0,removes=0};local before=cc
cc={DrawNode={create=function()return {
 clear=function()end,drawRect=function()store.draws=store.draws+1 end,
 removeFromParent=function()store.removes=store.removes+1 end
 }end},p=function(x,y)return {x=x,y=y}end,c4f=function(...)return {...}end}
local scene={mini_scene_node={addChild=function()end},block_scale=1,
 ConvertToUISpace=function(self,x,y)return x*50,y*50 end}
A.cfg.showMiningRoute=true;A.cfg.enabled=true
A:set_mining_route({x=2,y=4},{x=1,y=4},'test');A:update_mining_route_overlay(scene)
assert(store.draws==2)
A:set_mining_route({x=2,y=4},{x=1,y=4},'new label');A:update_mining_route_overlay(scene)
assert(store.draws==2)
A:set_mining_route({x=2,y=4},{x=1,y=4,blast=true},'test');A:update_mining_route_overlay(scene)
assert(store.draws==4)
A.cfg.showMiningRoute=false;A:update_mining_route_overlay(scene)
assert(store.removes==1);cc=before;A.cfg.enabled=false
`);
test('mine clears the continuation once there are no active teams or research',`
reset_research_mine({{mine_index=1,status=2,mine_level=3,end_time=999}})
package.loaded['logic.user'].user_id='MINE';A.cfg.mineResearchMask=0;A.harvest_updates=nil
A:start_mine_collect(nil,true);local key,p,r=next_mine_action()
assert(key=='mine_receive_reward');settle_mine(key,p)
key,p,r=next_mine_action();assert(not key and r.done and A:harvest_result(r).harvest.mine.seconds==0)
`);
test('mine chooses actual cached reward level for research shortages',`
reset_research_mine();fund_branch(1,1,11)
local key,p=begin_research_mine()
assert(key=='mine_start' and p.mine_level==3)
assert(sent[1].query_mine_info and sent[2].query_cultivation_info and sent[3].query_resource_list and sent[4].query_all_mine_rewards)
`);
test('mine shares inventory once across research branches',`
reset_research_mine();fund_branch(1,1,11);fund_branch(1,2,11);server_stock[72]=150
cultivation_logic.cultivation_info=clone(server_research);resource_counts=clone(server_stock)
local a=A:mine_analysis();assert(a.gaps[72].missing==50)
`);
test('mine harvests, refreshes stock, upgrades research, then selects a new layer',`
reset_research_mine({{mine_index=1,status=2,mine_level=3,end_time=999}})
fund_branch(1,1,11)
for _,row in pairs(server_rewards)do if row.mine_index==1 and row.mine_level==3 then row.all_resource_list[1].part_resource_list[1].resource_num=120 end end
local key,p=begin_research_mine();assert(key=='mine_receive_reward')
settle_mine(key,p)
key,p=next_mine_action();assert(key=='cultivate' and p.coefficient_type=='coefficient1')
assert(A.mine_state.collected==1 and resource_counts[72]==120)
settle_mine(key,p)
key,p=next_mine_action();assert(key=='mine_start' and p.mine_level==4,'new research tier should request ore IV')
assert(A.mine_state.upgraded==1 and server_stock[72]==20)
assert(cultivation_logic.cultivation_info[1].coefficient1==12)
`);
test('mine never claims a partly elapsed active queue',`
reset_research_mine({{mine_index=1,status=2,mine_level=3,beg_time=clock-14400,end_time=clock+14400}})
fund_branch(1,1,11)
local key,p,r=begin_research_mine();assert(not key and r.done and r.message:find('最后一队'))
for _,req in ipairs(sent)do assert(not req.mine_receive_reward and not req.mine_start)end
`);
test('mine reserves active yields before dispatching another queue',`
reset_research_mine({{mine_index=1,status=2,mine_level=1,end_time=3000},{mine_index=2,status=1}})
fund_branch(1,1,0)
local key,p,r=begin_research_mine();assert(not key and r.done)
assert(A:mine_analysis().gaps[60].unreserved==0)
`);
test('mine spreads different material levels across idle queues',`
reset_research_mine({{mine_index=1,status=1},{mine_index=2,status=1}})
fund_branch(1,1,11);fund_branch(2,1,7)
local key,p=begin_research_mine();assert(key=='mine_start')
local first=p.mine_level;settle_mine(key,p)
key,p=next_mine_action();assert(key=='mine_start' and p.mine_level~=first)
`);
test('mine uses actual rewards instead of assuming every queue drops every material',`
reset_research_mine();fund_branch(1,1,11)
for _,row in ipairs(server_rewards)do row.all_resource_list[1].part_resource_list={{resource_id=61,resource_num=100}}end
local key,p,r=begin_research_mine();assert(not key and r.done and r.partial)
`);
test('mine refuses absent formations and insufficient power without spending',`
reset_research_mine();fund_branch(1,1,11);formation_ok=false
local key,p,r=begin_research_mine();assert(not key and r.partial)
assert(r.message:find('配置'))
reset_research_mine();fund_branch(1,1,11);bp_ok=false
key,p,r=begin_research_mine();assert(not key and r.partial)
`);
test('mine never invents a fallback layer for missing reward data',`
reset_research_mine();fund_branch(1,1,11);server_rewards={}
local key,p,r=begin_research_mine();assert(not key and r.done and r.message:find('收益数据'))
`);
test('mine upgrades both research branches with fresh inventory between costs',`
reset_research_mine();fund_branch(1,1,0);fund_branch(1,2,0);server_stock[60]=20
local key,p=begin_research_mine();assert(key=='cultivate' and p.coefficient_type=='coefficient1')
settle_mine(key,p);key,p=next_mine_action();assert(key=='cultivate' and p.coefficient_type=='coefficient2')
settle_mine(key,p);key,p=next_mine_action();assert(key=='mine_start')
assert(A.mine_state.upgraded==2 and server_stock[60]==0)
`);
test('mine retries a rejected unaccepted upgrade without counting success early',`
reset_research_mine();fund_branch(1,1,0);server_stock[60]=10
local key,p=begin_research_mine();assert(key=='cultivate')
respond('cultivate_ret',{result='failure'});local r=A:mine_step();assert(r.wait and not r.done)
assert(A.mine_state.upgraded==0)
`);
test('mine validates authoritative research level after an upgrade',`
reset_research_mine();fund_branch(1,1,0);server_stock[60]=10
local key,p=begin_research_mine();assert(key=='cultivate')
respond('cultivate_ret',{result='success',cur_state=1})
key,p,r=next_mine_action();assert(not key and r.done and r.error and r.message:find('等级'))
`);
test('mine ignores acknowledgements for a different queue',`
reset_research_mine();fund_branch(1,1,11)
local key,p=begin_research_mine();assert(key=='mine_start')
respond('mine_start_ret',{result='success',mine_info={mine_index=2,mine_level=3,status=2,end_time=5000}})
assert(not A._task_pending.response)
local count=#sent;A:mine_step();assert(#sent==count)
settle_mine(key,p);next_mine_action();assert(A.mine_state.started==1)
`);
test('mine timeout requests an authoritative read before repeating any mutation',`
reset_research_mine();fund_branch(1,1,11)
local key,p=begin_research_mine();assert(key=='mine_start')
local count=#sent;A.now=21;local r=A:mine_step();assert(r.wait and not r.done and #sent==count+1 and sent[#sent].query_mine_info)
`);
test('mine ends after dispatch and a new run collects the mature queue',`
reset_research_mine({{mine_index=1,status=2,mine_level=1,end_time=1010}});fund_branch(1,1,0)
local key,p,r=begin_research_mine();assert(not key and r.done and r.message:find('冷却'))
clock=1011;A.now=11;mine_processed=#sent;A:start_mine_collect()
key,p=next_mine_action();assert(key=='mine_receive_reward')
settle_mine(key,p);key,p=next_mine_action();assert(key=='cultivate')
settle_mine(key,p);assert(server_research[1].coefficient1==1)
`);
test('mine completed research still receives mature rewards and does not dispatch',`
reset_research_mine({{mine_index=1,status=3,mine_level=1}})
local key,p=begin_research_mine();assert(key=='mine_receive_reward');settle_mine(key,p)
key,p,r=next_mine_action();assert(not key and r.done and not r.error)
assert(A.mine_state.started==0 and A.mine_state.collected==1)
`);
test('mine blocks blood diamond research costs before sending an upgrade',`
reset_research_mine();fund_branch(1,1,0)
local conf=config_manager.cultivation_config[1][1];local old=conf.resource_ids;conf.resource_ids='1'
local key,p,r=begin_research_mine();conf.resource_ids=old
assert(not key and r.done and r.error and r.message:find('血钻'))
`);
test('mine view tolerates unopened research data',`
reset_research_mine();local view=A:mine_view();assert(type(view.plan)=='string' and type(view.slots)=='string')
`);
test('cancelled mutation blocks a new task until the old response is settled',`
reset_research_mine();fund_branch(1,1,11)
local key,p=begin_research_mine();local count=#sent
A:cancel_task();A:start_arena(1);local r=A:arena_step();assert(r.wait and #sent==count)
settle_mine(key,p);A:arena_step();assert(sent[#sent].query_arena_rival)
`);
test('research branch listing exposes levels for the selection page',`
reset_research_mine()
cultivation_logic.cultivation_info=clone(server_research)
local list=A:research_branches()
assert(list.count==12 and list.sel==0,'12 branches with auto selection default')
assert(list.list['1'].key==11 and list.list['1'].level==25)
assert(list.list['2'].key==12 and list.list['2'].title:find('抵抗')~=nil)
assert(list.list['3'].key==21 and list.list['3'].title:find('加成')~=nil)
`);
test('mine research selection restricts upgrades to the chosen branch',`
reset_research_mine();server_stock[60]=20
fund_branch(1,2,0)
cultivation_logic.cultivation_info=clone(server_research);resource_counts=clone(server_stock)
A.cfg.mineResearch=12
local a=A:mine_analysis()
assert(#a.targets==1 and a.targets[1].kind==1 and a.targets[1].branch==2,'only the selected branch remains')
local key,p=begin_research_mine()
assert(key=='cultivate' and p.cultivation_type==1 and p.coefficient_type=='coefficient2','must upgrade only the selected branch')
settle_mine(key,p)
A.cfg.mineResearch=0
`);
test('all twelve choices remain available when research data is partially loaded',`
reset_research_mine();cultivation_logic.cultivation_info={[1]={coefficient1=1}}
local list=A:research_branches();assert(list.count==12 and list.mask==4095)
assert(list.list['1'].available and not list.list['2'].available and list.list['12'].key==62)
`);
test('all twelve choices remain available before research configuration is ready',`
reset_research_mine();local old=config_manager.cultivation_config;config_manager.cultivation_config=nil
local list=A:research_branches();assert(list.count==12 and not list.list['12'].available)
config_manager.cultivation_config=old
`);
test('multiple selected research branches exclude every unselected branch',`
reset_research_mine();fund_branch(1,1,0);fund_branch(2,2,0);fund_branch(6,2,0)
cultivation_logic.cultivation_info=clone(server_research);A.cfg.mineResearchMask=1+8
local a=A:mine_analysis();assert(#a.targets==2)
for _,t in ipairs(a.targets)do assert(t.kind==1 and t.branch==1 or t.kind==2 and t.branch==2)end
`);
test('all twelve research branches can be selected together',`
reset_research_mine();for k=1,6 do fund_branch(k,1,0);fund_branch(k,2,0)end
cultivation_logic.cultivation_info=clone(server_research);A.cfg.mineResearchMask=4095
assert(#A:mine_analysis().targets==12)
`);
test('clearing all research choices leaves only mature reward collection',`
reset_research_mine();fund_branch(1,1,0);cultivation_logic.cultivation_info=clone(server_research)
A.cfg.mineResearchMask=0;local a=A:mine_analysis();assert(a.complete and #a.targets==0 and not a.hasGap)
local key,p,r=begin_research_mine();assert(r.done and key==nil)
for _,m in ipairs(sent)do assert(not m.cultivate and not m.mine_start)end
`);
test('legacy single-branch choices migrate to the corresponding one of twelve bits',`
reset_research_mine();A.cfg.mineResearch=62;A.cfg.mineResearchMask=-1
local list=A:research_branches();assert(list.mask==2048 and list.list['12'].selected and not list.list['11'].selected)
A.cfg.mineResearch=0
`);
};
