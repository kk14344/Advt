module.exports=({run,test})=>{
run(`
  ROUTE_OLD_DIG=MINE.DigOrCollectBlock;ROUTE_OLD_TNT=MINE.UseTNT
  function route_fixture(depth)
    runtime_fixture(45,true);A.cfg.autoMine=true;A.cfg.autoGolem=true;A.cfg.autoChest=true;A.cfg.autoChest40=true
    A.cfg.autoBoss=true;A.cfg.autoCaveBoss=false;A.cfg.horizontalMining=true;A.cfg.useTntOnLong=false
    A.cfg.allowLongOre=false;A.cfg.maxDigTime=8;A.cfg.maxTargetPath=90;A.last_action_at=99;A._route_query_at=nil
    MINE.cur_position={x=5,y=depth or 120};MINE.cur_block_type=0;MINE.block_list={[depth or 120]={[5]=0}}
    config_manager.mining_dig_info_config[60]={ID=60,collect_type=0,destroy_level=1,need_time1=1,tnt_count=2}
    config_manager.mining_dig_info_config[61]={ID=61,collect_type=1,destroy_level=1,need_time1=1200,tnt_count=1}
    config_manager.mining_dig_info_config[8]={ID=8,collect_type=1,destroy_level=1,need_time1=1200,tnt_count=1}
    constants.RESOURCE_TYPE.tnt=9;RTTNT=20;ROUTE_HITS={}
    package.loaded['logic.resource'].GetResourceNum=function(self,id)return id==9 and RTTNT or (id==29 and RTSTOCK or 0)end
    MINE.DigOrCollectBlock=function(self,x,y)
      local kind=self:GetBlockType(x,y);ROUTE_HITS[#ROUTE_HITS+1]={x=x,y=y,kind=kind}
      if kind==60 or kind==8 or kind==61 then RTNET:Send({dig_block={position={x=x,y=y}}});return 'success' end
      if kind~=3 and kind~=40 and kind~=50 then self.block_list[y][x]=0;self.dig_count=self.dig_count-1 end
      self.cur_position={x=x,y=y};self.cur_block_type=0;self.dig_endtime=0
      return 'success'
    end
    MINE.UseTNT=function()error('TNT-only tile must not be followed by a second use_tnt request')end
    RTNET.event_listener.dig_block_ret=function(msg)
      if msg.result=='success' and msg.cur_position then
        local pos=msg.cur_position;local kind=MINE:GetBlockType(pos.x,pos.y)
        if kind==60 then RTTNT=RTTNT-2 end
        MINE.cur_position=pos
        if kind==8 or kind==61 then MINE.cur_block_type=kind;MINE.dig_endtime=2200
        else MINE.block_list[pos.y][pos.x]=0;MINE.cur_block_type=0;MINE.dig_endtime=0 end
      end
    end
    RTNET.event_listener.use_tnt_ret=function(msg)if msg.result=='success' then RTTNT=RTTNT-1;MINE.dig_endtime=0 end end
    RTNET.event_listener.collect_mine_ret=function(msg)if msg.result=='success' then
      MINE.block_list[MINE.cur_position.y][MINE.cur_position.x]=0;MINE.cur_block_type=0;MINE.dig_endtime=0
    end end
    A:install_task_hooks()
  end
  function route_ring()
    route_fixture(120);MINE.block_list={
      [120]={[5]=0,[6]=1,[7]=60,[8]=3,[9]=60},
      [119]={[8]=60},[121]={[5]=1,[6]=1,[8]=60}}
  end
  function route_tick(delta)A.now=A.now+(delta or .1);MINE.dig_endtime=0;A:step()end
  function route_ack(x,y)rt_reply('dig_block_ret',{result='success',cur_position={x=x or 7,y=y or 120}})end
`,'target route fixtures');
test('golems take priority over adjacent chests and downward excavation at every depth',`
 for _,depth in ipairs({0,50,119,120,130,150})do
  route_fixture(depth);MINE.block_list[depth][4]=3;MINE.block_list[depth][6]=40;MINE.block_list[depth+1]={[5]=1}
  A:step();assert(A.fight and A.fight.golem and ROUTE_HITS[1].x==4 and #ROUTE_HITS==1)
 end
`);
test('after the golem the chest is handled before resuming shallow descent',`
 route_fixture(120);MINE.block_list[120][4]=3;MINE.block_list[120][6]=40;MINE.block_list[121]={[5]=1}
 A:step();assert(A.fight.golem);MINE.block_list[120][4]=0;A.fight=nil;route_tick(1)
 assert(A.fight.chest and ROUTE_HITS[2].x==6)
 MINE.block_list[120][6]=0;A.fight=nil;route_tick(1)
 assert(ROUTE_HITS[3].y==121 and not A.fight)
`);
test('a chest requiring an approach dig still outranks an immediately available downward tile',`
 route_fixture(120);MINE.block_list[120][6]=1;MINE.block_list[120][7]=40;MINE.block_list[121]={[5]=1}
 A:step();assert(ROUTE_HITS[1].x==6 and ROUTE_HITS[1].y==120)
`);
test('chests precede a normal mining boss while ordinary golems remain first',`
 route_fixture(120);MINE.block_list[120][4]=50;MINE.block_list[120][6]=40
 A:step();assert(A.fight and A.fight.chest)
`);
test('disabled target categories are still respected during automatic descent',`
 route_fixture(120);A.cfg.autoGolem=false;A.cfg.autoChest=false
 MINE.block_list[120][4]=3;MINE.block_list[120][6]=40;MINE.block_list[121]={[5]=1}
 A:step();assert(not A.fight and ROUTE_HITS[1].y==121)
`);
test('an enclosed golem gets one necessary TNT opening even with timed-ore TNT disabled',`
 route_ring();A:step();assert(ROUTE_HITS[1].x==6 and #RTSENT==0)
 route_tick();assert(#ROUTE_HITS==2 and ROUTE_HITS[2].x==7 and ROUTE_HITS[2].kind==60)
 assert(#RTSENT==1 and RTSENT[1].dig_block and A._mine_blast and A.cfg.useTntOnLong==false)
 route_ack();route_tick(1);assert(A.fight and A.fight.x==8 and RTTNT==18 and #ROUTE_HITS==3)
`);
test('the same necessary TNT path works for a chest',`
 route_ring();MINE.block_list[120][8]=40;A:step();route_tick();route_ack();route_tick(1)
 assert(A.fight and A.fight.chest and RTTNT==18)
`);
test('a normal path avoids forced TNT even when a TNT opening would be closer',`
 route_ring();MINE.block_list[120][9]=1;MINE.block_list[120][10]=0
 A:step();assert(ROUTE_HITS[1].x==9 and ROUTE_HITS[1].kind==1 and #RTSENT==0 and RTTNT==20)
`);
test('TNT route planning crosses a thick barrier with the fewest explosive tiles',`
 route_fixture(120);MINE.block_list={}
 for y=118,122 do MINE.block_list[y]={};for x=6,10 do MINE.block_list[y][x]=60 end end
 MINE.block_list[120][5]=0;MINE.block_list[120][8]=3
 local path,cost=A:blast_path({x=8,y=120});assert(#path==2 and cost==4 and path[1].x==6 and path[2].x==7)
`);
test('insufficient TNT waits before wasting even the ordinary approach pickaxes',`
 route_ring();RTTNT=1;A:step();assert(#ROUTE_HITS==0 and #RTSENT==0 and A.status:find('雷管'))
 RTTNT=2;route_tick(20);assert(#ROUTE_HITS==1 and ROUTE_HITS[1].x==6)
`);
test('ruby and other long ores get one confirmed selection explosion and harvest before reaching the target',`
 for _,kind in ipairs({8,61})do
  route_ring();MINE.block_list[120][7]=kind;A:step();route_tick()
  assert(RTSENT[1].dig_block and RTTNT==20 and A._mine_blast.phase=='select')
  route_ack();route_tick();assert(RTSENT[2].use_tnt and RTTNT==20)
  rt_reply('use_tnt_ret',{result='success'});route_tick();assert(RTSENT[3].collect_mine and RTTNT==19)
  rt_reply('collect_mine_ret',{result='success'});route_tick(1)
  assert(A.fight and A.fight.x==8 and RTTNT==19 and #RTSENT==3 and not A._mine_blast)
 end
`);
test('a normal fast gem route does not spend TNT just because the tile is a gem',`
 route_ring();MINE.block_list[120][7]=8;config_manager.mining_dig_info_config[8].need_time1=1
 local plan=A:plan_mining_target(A:scan_targets());assert(plan.route_tnt==0 and not plan.path[1].blast)
`);
test('a delayed ruby selection cannot trigger TNT before its matching acknowledgement',`
 route_ring();MINE.block_list[120][7]=8;A:step();route_tick()
 for i=1,12 do route_tick(.1)end
 assert(#RTSENT==1 and RTTNT==20 and A._mine_blast.phase=='select')
`);
test('lost ruby TNT acknowledgements only refresh reads and never spend another TNT',`
 route_ring();MINE.block_list[120][7]=8;A:step();route_tick();route_ack();route_tick()
 assert(RTSENT[2].use_tnt);route_tick(4);route_tick(4)
 local uses=0;for _,msg in ipairs(RTSENT)do if msg.use_tnt then uses=uses+1 end end
 assert(uses==1 and A._mine_blast.phase=='tnt')
 rt_reply('use_tnt_ret',{result='success'});route_tick();assert(RTSENT[#RTSENT].collect_mine)
`);
test('a rejected mining read is retried later without an exception or duplicate mutation',`
 route_ring();A:step();route_tick();RTNET.Send=function()return false end
 route_tick(4);assert(A._mine_blast and not A._task_pending and #ROUTE_HITS==2)
`);
test('a rejected unsent TNT request does not create a twenty-second cancellation barrier',`
 route_ring();MINE.block_list[120][7]=8;A:step();route_tick();route_ack()
 RTNET.Send=function()return false end;route_tick();assert(A._mine_blast.phase=='ready_tnt' and not A._task_pending)
 A:cancel_task();assert(not A._cancelled_task_pending)
`);
test('an impossible target never causes perimeter excavation',`
 route_ring();MINE.block_list[120][7]=2;MINE.block_list[120][9]=2
 MINE.block_list[119][8]=2;MINE.block_list[121][8]=2
 for i=1,40 do route_tick(1)end
 assert(#ROUTE_HITS==0 and #RTSENT==0 and A.cfg.enabled)
`);
test('waiting for a TNT reply cannot send a duplicate blast or dig the perimeter',`
 route_ring();A:step();route_tick();local before=#ROUTE_HITS
 for i=1,15 do route_tick(.1)end
 assert(#ROUTE_HITS==before and #RTSENT==1)
`);
test('a lost TNT reply only refreshes reads and a late matching reply resumes the target',`
 route_ring();A:step();route_tick()
 route_tick(4);assert(RTSENT[2].query_mining_info and #ROUTE_HITS==2)
 rt_reply('query_mining_info_ret',{cur_position={x=6,y=120},dig_endtime=0,dig_count=44})
 for i=1,3 do route_tick(4)end
 for _,message in ipairs(RTSENT)do assert(not message.use_tnt)end
 assert(#ROUTE_HITS==2 and A._mine_blast)
 route_ack();route_tick(1);assert(A.fight and not A._mine_blast and RTTNT==18)
`);
test('a cleared authoritative tile releases the TNT barrier after a missing dig callback',`
 route_ring();A:step();route_tick();route_tick(4)
 MINE.block_list[120][7]=0
 rt_reply('query_mining_info_ret',{cur_position={x=7,y=120},dig_endtime=0,dig_count=44})
 route_tick(1);assert(A.fight and not A._mine_blast and #ROUTE_HITS==3)
`);
test('a rejected TNT action waits for resource or terrain changes instead of repeatedly spending',`
 route_ring();A:step();route_tick();rt_reply('dig_block_ret',{result='not_enough_resource'})
 for i=1,10 do route_tick(1)end
 assert(#ROUTE_HITS==2 and #RTSENT==1 and A._mine_blast.error=='not_enough_resource')
`);
test('stopping during TNT readback preserves the uncertain mutation barrier',`
 route_ring();A:step();route_tick();route_tick(4);local count=#RTSENT;A:cancel_task()
 assert(A._cancelled_task_pending and A._cancelled_task_pending.kind=='mine_target_blast')
 route_ack();assert(not A._mine_blast and not A._cancelled_task_pending and #RTSENT==count)
`);
test('a nil native TNT send does not create an uncertain blast or consume resources',`
 route_ring();MINE.block_list[120][6]=0
 MINE.DigOrCollectBlock=function()return nil end;A:step()
 assert(not A._mine_blast and not A._task_pending and not A.pending_dig and RTTNT==20)
`);
test('the camera shifts the on-screen anchor down one tile and keeps the real coordinate label',`
 camera_fixture();local label
 CAM.ui_root={coordinate_text={setString=function(self,text)label=text end}}
 assert(A:follow_mining_camera() and CAM.target.y==129 and label=='X:5 Y:130')
 assert(MINE.cur_position.y==130 and #RTSENT==0)
 A.read_context=CAMERA_ORIGINAL_READ
`);
run(`MINE.DigOrCollectBlock=ROUTE_OLD_DIG;MINE.UseTNT=ROUTE_OLD_TNT;A._mine_blast=nil;A._task_pending=nil;A.pending_dig=nil`,'restore route fixtures');
run(`
 function speed_fixture(chest)
  route_fixture(130);MINE.block_list[130][6]=chest and 40 or 3
  A.last_action_at=A.now-.05;SPEED_SOLVES=0
  MINE.SolveEvent=function(self,id)SPEED_SOLVES=SPEED_SOLVES+1;return true end
  RTNET.event_listener.solve_adventure_event_ret=function(msg)
   if msg.event_id==99 then
    MINE.__mw_pending_monster=nil
    if msg.win then MINE.block_list[130][6]=0 end
   end
  end
  A:install_task_hooks()
  local target=A:scan_targets()[1];assert(A:start_target(target),'start must not wait an extra .9 seconds')
  MINE.cur_event_id=99;MINE.cur_event_position={x=6,y=130}
  A.now=A.now+.05;A:update_fight();assert(A.fight.phase=='solve' and SPEED_SOLVES==1)
 end
`,'rapid mining target fixtures');
test('an acknowledged opened chest finishes on the next update without a fixed 1.2-second delay',`
 speed_fixture(true);local before=A.stats.chests;A.now=A.now+.05
 rt_reply('dig_block_ret',{result='success',cur_position={x=6,y=130}})
 A:update_fight();assert(not A.fight and A.stats.chests==before+1 and A.last_action_at==A.now)
`);
test('a settled golem victory finishes immediately and the next target may start fifty milliseconds later',`
 speed_fixture(false);MINE.__mw_pending_monster={event_id=99};local before=A.stats.golems
 rt_reply('solve_adventure_event_ret',{event_id=99,result='success',win=true});A.now=A.now+.05;A:update_fight()
 assert(not A.fight and A.stats.golems==before+1)
 MINE.block_list[130][7]=40;A.now=A.now+.05
 local target=A:scan_targets()[1];assert(A:start_target(target) and A.fight.chest)
`);
test('delayed golem settlement does not become a false defeat or a second challenge',`
 speed_fixture(false);local before=A.stats.losses
 for i=1,50 do A.now=A.now+.05;A:update_fight()end
 assert(A.fight and not A.fight.settlement_received and A.stats.losses==before and SPEED_SOLVES==1 and not A.failed['6:130'])
`);
test('a native pending battle still blocks fast completion even if terrain was already cleared',`
 speed_fixture(false);MINE.block_list[130][6]=0;MINE.__mw_pending_monster={event_id=99}
 A.now=A.now+.05;A:update_fight();assert(A.fight)
 MINE.__mw_pending_monster=nil;A:update_fight();assert(not A.fight)
`);
test('a real acknowledged golem defeat retains the blacklist and finishes without an added global delay',`
 speed_fixture(false);local before=A.stats.losses
 rt_reply('solve_adventure_event_ret',{event_id=99,result='success',win=false});A.now=A.now+.05;A:update_fight()
 assert(not A.fight and A.stats.losses==before+1 and A.failed['6:130'])
`);
test('a settlement for another event cannot release the current golem',`
 speed_fixture(false);rt_reply('solve_adventure_event_ret',{event_id=123,result='success',win=true})
 A.now=A.now+2;A:update_fight();assert(A.fight and not A.fight.settlement_received)
`);
test('a legacy zero event identifier is accepted only at the current golem position',`
 speed_fixture(false);rt_reply('solve_adventure_event_ret',{event_id=0,result='success',win=false})
 A:update_fight();assert(not A.fight and A.failed['6:130'])
`);
test('a cache eviction is not mistaken for an opened chest',`
 speed_fixture(true);MINE.block_list[130][6]=nil;local before=A.stats.chests;A.now=A.now+2
 A:update_fight();assert(A.fight and A.stats.chests==before)
`);
test('a chest rejected for missing resources does not become a false win or combat loss',`
 speed_fixture(true);local wins=A.stats.chests;local losses=A.stats.losses
 rt_reply('dig_block_ret',{result='not_enough_resource'});A:update_fight()
 assert(not A.fight and A.stats.chests==wins and A.stats.losses==losses and A.failed['6:130'])
`);
test('even an acknowledged settlement cannot turn an evicted map tile into a defeat',`
 speed_fixture(false);MINE.block_list[130][6]=nil;A.fight.settlement_received=true;local losses=A.stats.losses
 A.now=A.now+2;A:update_fight();assert(A.fight and A.stats.losses==losses)
`);
run(`MINE.DigOrCollectBlock=ROUTE_OLD_DIG;MINE.UseTNT=ROUTE_OLD_TNT;A._mine_blast=nil;A._task_pending=nil;A.pending_dig=nil;A.fight=nil`,'restore fast target fixtures');
};
