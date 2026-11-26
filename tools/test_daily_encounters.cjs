module.exports = ({run, test}) => {
  run(`
    function encounter_fixture()
      A:cancel_task()
      A._cancelled_task_pending=nil;A.pending_dig=nil;A._mine_blast=nil;A.inBattle=false;A.fight=nil
      A.now=100;A.cfg.mobileNoMining=false;A.cfg.enabled=false
      local day=2000
      local saved={context=A.read_context,step=A.step,identity=A.encounter_identity,
        camera=A.follow_mining_camera,graphic=graphic,query=mining.QueryBlockInfo,send=network.Send,random=RANDOM_TYPE}
      RANDOM_TYPE={treasure_hunt=71}
      local sim={scene='main_sub_scene',digs=0,queries=0,enters=0}
      A.read_context=function()if sim.scene then return {sub_name=sim.scene} end end
      A.encounter_identity=function()return sim.role or '1:role',sim.day or day end
      A.step=function(self) assert(self.cfg.enabled and self.cfg.autoMine);sim.digs=sim.digs+1 end
      A.follow_mining_camera=function()end
      graphic={DispatchEvent=function(_,name,scene)
        assert(name~='show_world_sub_scene','use the native mining entry while the map streams')
      end}
      mining.QueryBlockInfo=function()
        sim.enters=sim.enters+1
        if not sim.wait_for_region then sim.scene='mining_district_sub_scene' end
      end
      mining.random_event_list={};mining.pending_random_event=nil;mining.show_random_event=nil
      local st={selected={[11]=true},skipped={},completion_result={}}
      A.daily_state=st
      A.encounter_clear_days={}
      local req=A.task_request
      A.task_request=function(self,event,kind,action)
        assert(event=='query_mining_info_ret' and kind=='clear_encounter_read')
        sim.queries=sim.queries+1
        self._task_pending={kind=kind,event=event,sent_at=self.now}
      end
      sim.reply=function(n)
        mining.event_count=n
        A._task_pending.response={event_count=n}
      end
      sim.restore=function()
        A:cancel_task()
        A.read_context=saved.context;A.step=saved.step;A.encounter_identity=saved.identity
        A.follow_mining_camera=saved.camera;graphic=saved.graphic;mining.QueryBlockInfo=saved.query
        A.task_request=req;network.Send=saved.send;RANDOM_TYPE=saved.random
      end
      function sim.tick()return A:daily_encounter_step(st)end
      return st,sim
    end
  `);
  test('repeated market errors cannot block a later encounter forever', `
    local st,sim=encounter_fixture()
    st.running=true;st.completion_phase='market';st.market_exchange=true
    local market,close,tail,refine,items,children=A.daily_market_step,A.close_daily_market,A.daily_tail_rewards,A.daily_refine_gate,A.daily_retry_items,A.daily_retry_children
    A.daily_market_step=function()return {done=true,partial=true,retry=true,message="集市读取失败"} end
    A.close_daily_market=function()end
    A.daily_tail_rewards=function()return {done=true} end
    A.daily_retry_items=function()return {done=true} end
    A.daily_retry_children=function()return {done=true} end
    A.daily_refine_gate=function()end
    for _=1,8 do A:daily_step();A.now=A.now+20 end
    local result=A:daily_step()
    assert(result.wait and result.phase=='encounter' and sim.enters==1)
    assert(st.retry_round==8 and st.completion_result.partial and st.completion_result.message:find("集市读取失败"))
    A.daily_market_step,A.close_daily_market,A.daily_tail_rewards,A.daily_refine_gate,A.daily_retry_items,A.daily_retry_children=market,close,tail,refine,items,children
    sim.restore()
  `);
  test('encounter uses the current native clock and keeps old day identifiers when provided', `
    local login=package.loaded['logic.login']
    local account,now,zone,legacy=A.harvest_account,time_logic.Now,time_logic.time_zone_offset,time_logic.GetNowDayTime
    package.loaded['logic.login']={server_id=2}
    A.harvest_account=function()return '777' end
    local stamp=20000*86400-28801
    time_logic.Now=function()return stamp end
    time_logic.time_zone_offset=28800
    time_logic.GetNowDayTime=nil
    local role,before=A:encounter_identity()
    assert(role=='2:777' and before==19999*86400-28800)
    stamp=stamp+1
    local _,after=A:encounter_identity()
    assert(after==20000*86400-28800 and after-before==86400)
    time_logic.GetNowDayTime=function()return 12345 end
    local _,compatible=A:encounter_identity()
    assert(compatible==12345)
    A.harvest_account,time_logic.Now,time_logic.time_zone_offset,time_logic.GetNowDayTime=account,now,zone,legacy
    package.loaded['logic.login']=login
  `);
  test('encounter leaves the scene transition to the native streamed map and resumes after it loads', `
    local st,sim=encounter_fixture()
    sim.wait_for_region=true
    sim.tick();sim.tick()
    assert(sim.scene=='main_sub_scene' and sim.enters==1 and sim.queries==0 and sim.digs==0)
    sim.scene='mining_district_sub_scene'
    sim.tick();sim.reply(1);sim.tick()
    assert(sim.enters==1 and sim.queries==1 and sim.digs==1)
    sim.restore()
  `);
  test('encounter tail enters and verifies a zero before recording completion and skips the same role and game day', `
    local st,sim=encounter_fixture()
    sim.tick();assert(sim.enters==1 and not st.encounter_success)
    sim.tick();assert(sim.queries==1 and sim.digs==0)
    sim.reply(0);sim.tick()
    assert(st.encounter_success and st.completion_result.encounterClearDone.day==2000 and sim.digs==0)
    st.encounter_done=nil;st.encounter_success=nil;sim.scene='main_sub_scene'
    sim.tick();assert(st.encounter_message:find("跳过") and sim.enters==1)
    st.encounter_done=nil;st.encounter_success=nil;sim.day=3000;A.now=105
    sim.tick();assert(not st.encounter_success and sim.enters==2)
    sim.restore()
  `);
  test('the last treasure uses ordinary digging and completion waits for a fresh zero after its settlement', `
    local st,sim=encounter_fixture()
    sim.tick();sim.tick();sim.reply(1);sim.tick()
    assert(sim.digs==1 and not A.cfg.enabled)
    mining.event_count=0
    mining.random_event_list={{event_type=RANDOM_TYPE.treasure_hunt, end_time=time_logic:Now()+100}}
    A.now=1000;sim.tick()
    assert(sim.digs==2 and not st.encounter_success)
    mining.random_event_list={}
    sim.tick();assert(sim.queries==2 and not st.encounter_success)
    sim.reply(0);sim.tick()
    assert(st.encounter_success and sim.digs==2)
    sim.restore()
  `);
  test('encounter reconnect discards stale reads and stops when a different role logs in', `
    local st,sim=encounter_fixture()
    sim.tick();sim.tick()
    sim.scene=nil;sim.tick();assert(not A._task_pending)
    sim.scene='mining_district_sub_scene';sim.tick();assert(sim.queries==2)
    sim.role='1:different';sim.tick();assert(st.encounter_done and not st.encounter_success)
    sim.restore()
  `);
};
