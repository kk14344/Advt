const fs=require('fs'),path=require('path'),assert=require('assert');
module.exports=({run,test,toLua})=>{
const primary=JSON.parse(fs.readFileSync(path.join(__dirname,'../tests/fixtures/adventure-primary-132.json'),'utf8'));
assert.equal(primary.tables.adventure_maze.length,1050);
assert.deepEqual([...new Set(primary.tables.adventure_income.map(r=>r.difficulty))].sort(),[1,2,3]);
run(`
 function push_fixture()
  A:cancel_task();A._cancelled_task_pending=nil;A._request_probe=nil;A.now=0;clock=1000;A.inBattle=false
  network.session=nil;network.internal_session=nil;network.IsConnected=function()return true end;package.loaded['logic.user']={query_queue={}}
  sent={};network.Send=function(self,msg)sent[#sent+1]=msg;return true end
  constants.RESOURCE_TYPE.gold_coin=1;constants.EVENT_MAX_BAG_SPACE_COUNT=5
  PX_GOLD=1000000000;PX_SPACE=50;resource_logic.GetResourceNum=function(self,id)return id==1 and PX_GOLD or 0 end
  package.loaded['logic.bag'].GetSpaceCount=function()return PX_SPACE end
  config_manager.adventure_maze_config={
   [101]={ID=101,name='1-1',income_id=1,type=1,event_id=1001,event_time=1},
   [102]={ID=102,name='1-2',income_id=1,type=2,event_id=1002,event_time=1},
   [201]={ID=201,name='1-1 normal',income_id=2,type=1,event_id=2001,event_time=1},
   [301]={ID=301,name='1-1 hard',income_id=3,type=1,event_id=3001,event_time=1}}
  config_manager.adventure_income_config={[1]={area_id=1,difficulty=1},[2]={area_id=1,difficulty=2},[3]={area_id=1,difficulty=3}}
  config_manager.area_info_config={[1]={bp_limit=0}}
  config_manager.event_config={};for _,c in pairs(config_manager.adventure_maze_config)do config_manager.event_config[c.event_id]={ID=c.event_id,need_resource_id='1',need_num='8',fail_cd=2}end
  local t=package.loaded['logic.troop'];t.GetTroopBP=function()return 1000000 end
  AD={cur_maze_id=101,maze_list={[101]={id=101,event_is_finish=false,event_time=10,box_num=0}},event_cd_list={},event_fail_time_list={}}
  package.loaded['logic.adventure']=AD
  network.event_listener.query_adventure_maze_list_ret=function(msg)
   AD.maze_list={};for _,r in pairs(msg.maze_list or {})do AD.maze_list[r.id]=r end;AD.cur_maze_id=msg.cur_maze_id
   AD.event_fail_time_list={};for _,r in pairs(msg.event_fail_time_list or {})do AD.event_fail_time_list[r.id]=r.fail_time end
  end
  network.event_listener.enter_adventure_maze_ret=function(msg)if msg.result=='success' then AD.cur_maze_id=msg.maze_id end end
  network.event_listener.solve_adventure_event_ret=function(msg)if msg.result=='success' then AD.maze_list[AD.cur_maze_id].event_is_finish=true end end
  network.event_listener.open_box_ret=function(msg)if msg.maze_info then AD.maze_list[AD.cur_maze_id]=msg.maze_info end end
  A._task_hooks={};A:install_task_hooks();A:start_push()
 end
 function push_reply(extra)
  local rows={};for _,r in pairs(AD.maze_list)do rows[#rows+1]=r end
  local msg={maze_list=rows,cur_maze_id=AD.cur_maze_id,event_cd_list={},event_fail_time_list={}}
  for k,v in pairs(extra or {})do msg[k]=v end
  respond('query_adventure_maze_list_ret',msg)
 end
 function push_load()A:push_step();push_reply();A:push_step()end
 function push_sent(key)local n=0;for _,m in ipairs(sent)do if m[key] then n=n+1 end end;return n end
`);
test('push chooses an unlocked unfinished mainline event and never purchases or upgrades',`
 push_fixture();push_load();assert(sent[#sent].solve_adventure_event.event_id==1001)
 for _,m in ipairs(sent)do assert(m.query_adventure_maze_list or m.solve_adventure_event)end
`);
test('push includes all three difficulties and excludes cleared and still-locked stages',`
 push_fixture();AD.maze_list[101].event_is_finish=true
 AD.maze_list[201]={id=201,event_time=10,event_is_finish=false,box_num=0}
 AD.maze_list[301]={id=301,event_time=10,event_is_finish=false,box_num=0}
 A:push_step();push_reply();A:push_step();assert(sent[#sent].enter_adventure_maze.maze_id==201)
 local choices=A:push_candidates();assert(#choices==2 and choices[1].id==201 and choices[2].id==301)
`);
test('native exploration time is waited for without forging completion or sending a battle early',`
 push_fixture();AD.maze_list[101].event_time=0;push_load();assert(push_sent('solve_adventure_event')==0)
 AD.maze_list[101].event_time=1;A:push_step();if sent[#sent].query_adventure_maze_list then push_reply();A:push_step()end
 assert(push_sent('solve_adventure_event')==1)
`);
test('twenty consecutive losses keep retrying after cooldown until manually stopped',`
 push_fixture();push_load()
 for i=1,20 do
  respond('solve_adventure_event_ret',{event_id=1001,result='battle_failure'})
  A:push_step();push_reply();A:push_step();local n=push_sent('solve_adventure_event')
  A.now=A.now+1;clock=1000+A.now;A:push_step();assert(push_sent('solve_adventure_event')==n,'must respect failure cooldown')
  A.now=A.now+2;clock=1000+A.now
  for j=1,4 do A:push_step();if A._task_pending and A._task_pending.kind=='push_query' then push_reply()end end
  assert(A.push_state and A.push_state.lost==i and not A:push_step().done)
 end
 assert(push_sent('solve_adventure_event')==21)
 A:cancel_task();assert(not A.push_state)
`);
test('a failed stage yields to other unlocked difficulties instead of blocking the whole mainline',`
 push_fixture();AD.maze_list[201]={id=201,event_time=10,event_is_finish=false,box_num=0}
 push_load();respond('solve_adventure_event_ret',{event_id=1001,result='battle_failure'})
 A:push_step();push_reply();A:push_step();assert(sent[#sent].enter_adventure_maze.maze_id==201)
`);
test('gold shortage waits and resumes when balance recovers without buying gold',`
 push_fixture();PX_GOLD=0;push_load();assert(push_sent('solve_adventure_event')==0)
 PX_GOLD=1000000;A:push_step();if A._task_pending.kind=='push_query' then push_reply();A:push_step()end
 assert(push_sent('solve_adventure_event')==1)
`);
test('gold cost accounts for server failure multipliers and has no assistant spending budget',`
 push_fixture();A:push_step();push_reply({event_fail_time_list={{id=1001,fail_time=10}}});PX_GOLD=8000;A:push_step();assert(push_sent('solve_adventure_event')==0)
 PX_GOLD=8192;A:push_step();if A._task_pending and A._task_pending.kind=='push_query' then push_reply({event_fail_time_list={{id=1001,fail_time=10}}});A:push_step()end
 assert(push_sent('solve_adventure_event')==1)
`);
test('full bags pause progression without expanding storage or consuming unrelated resources',`
 push_fixture();PX_SPACE=1;push_load();assert(push_sent('solve_adventure_event')==0)
 PX_SPACE=50;A:push_step();assert(push_sent('solve_adventure_event')==1)
`);
test('a successful clear refreshes progress and collects available free stage boxes',`
 push_fixture();push_load();respond('solve_adventure_event_ret',{event_id=1001,result='success'})
 AD.maze_list[101].box_num=1;A:push_step();push_reply();A:push_step();assert(sent[#sent].open_box)
 respond('open_box_ret',{result='success',open_box_num=1,maze_info={id=101,event_is_finish=true,event_time=10,box_num=0}})
 A:push_step();push_reply();local r=A:push_step();assert(r.wait and A.push_state.won==1 and A.push_state.boxes==1)
`);
test('a missing battle ACK reads authoritative progress and never repeats an already won fight',`
 push_fixture();push_load();AD.maze_list[101].event_is_finish=true
 A.now=21;clock=1021;A:push_step();assert(sent[#sent].query_adventure_maze_list)
 push_reply();A.now=22;A:push_step();if A._task_pending and A._task_pending.kind=='push_query' then push_reply();A:push_step()end
 assert(push_sent('solve_adventure_event')==1 and A.push_state.won==1)
`);
test('stopping a pending mainline challenge retains its barrier and prevents further progression',`
 push_fixture();push_load();local n=#sent;A:cancel_task();assert(A._cancelled_task_pending and not A.push_state)
 respond('solve_adventure_event_ret',{event_id=1001,result='success'});assert(#sent==n and not A._cancelled_task_pending)
`);
test('an incomplete mainline query never becomes a guessed challenge or false completion',`
 push_fixture();A:push_step();respond('query_adventure_maze_list_ret',{maze_list={},cur_maze_id=101})
 local r=A:push_step();assert(r.wait and not r.done and push_sent('solve_adventure_event')==0)
`);
};
