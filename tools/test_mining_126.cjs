module.exports=({run,test})=>{
run(`
function mine126_fixture()
 route_fixture(130);A.fight=nil;A._mine_blast=nil;A._task_pending=nil;A.failed={};A.failed_info={}
 MINE.golem_coordinates={};MINE.stream_generation=101;MINE.SolveEvent=function()return true end
 config_manager.event_config[6000001]={event_type=4,need_item_id=0,need_num=''}
end
`);
test('a free open chest is a traversable action on the path to a higher-priority golem',`
 mine126_fixture();MINE.block_list[130][6]=40;MINE.block_list[130][7]=3
 local plan=A:plan_mining_target(A:scan_targets())
 assert(plan.golem and plan.path[1].free_chest and plan.path[1].x==6 and plan.route_tnt==0)
 A:step();assert(A.fight and A.fight.chest and A.fight.x==6 and #ROUTE_HITS==1 and RTTNT==20)
`);
test('opening a route chest costs no picks and immediately exposes the golem route',`
 mine126_fixture();MINE.block_list[130][6]=40;MINE.block_list[130][7]=3
 local picks=MINE.dig_count;A:step();MINE.cur_event_id=6000001;MINE.cur_event_position={x=6,y=130}
 A:update_fight();rt_reply('dig_block_ret',{result='success',cur_position={x=6,y=130}});A:update_fight()
 A.now=A.now+.1;A:step()
 assert(A.fight.golem and A.fight.x==7 and MINE.dig_count==picks and RTTNT==20)
`);
test('free passage chests may be cleared even when optional paid chest opening is disabled',`
 mine126_fixture();A.cfg.autoChest=false;A.cfg.autoChest40=false
 MINE.block_list[130][6]=40;MINE.block_list[130][7]=3
 A:step();assert(A.fight and A.fight.chest and A.fight.x==6)
`);
test('a free chest is never assigned a TNT cost or treated like an ordinary terrain dig',`
 mine126_fixture();MINE.block_list[130][6]=40;MINE.block_list[130][7]=3;RTTNT=0
 local path,cost=A:blast_path({x=7,y=130});assert(cost==0 and #path==1 and path[1].free_chest and not path[1].blast)
 A:step();assert(A.fight.chest and not A._mine_blast and A.stats.digs>=0)
`);
test('a free passage chest can open before a later TNT barrier even when TNT stock is zero',`
 route_ring();A.failed_info={};MINE.block_list[120][6]=40;RTTNT=0
 A:step();assert(A.fight and A.fight.chest and A.fight.x==6 and RTTNT==0 and #RTSENT==0)
`);
test('a paid locked chest is not silently treated as a free pass-through cell',`
 mine126_fixture();A.cfg.autoChest=false;MINE.block_list[130][6]=41;MINE.block_list[130][7]=3
 local plan=A:plan_mining_target(A:scan_targets());assert(plan.path)
 for _,step in ipairs(plan.path)do assert(not (step.x==6 and step.y==130),'paid box cannot be a free passage')end
 A:step();assert(#ROUTE_HITS==0 and #RTSENT==0)
`);
test('a free passage whose query unexpectedly reports a cost cannot consume a key',`
 mine126_fixture();MINE.block_list[130][6]=40;MINE.block_list[130][7]=3;A:step()
 config_manager.event_config[6000001]={need_resource_id='38',need_num='1'}
 MINE.cur_event_id=6000001;MINE.cur_event_position={x=6,y=130}
 local solves=0;MINE.SolveEvent=function()solves=solves+1;return true end
 A:update_fight();assert(solves==0 and not A.fight and A:is_failed(6,130))
`);
test('a visible temporarily rejected golem prevents ordinary excavation until retry',`
 mine126_fixture();MINE.block_list[130][4]=3;MINE.block_list[131]={[5]=1}
 A:blacklist(4,130,2,'资源尚未就绪');A:step();assert(#ROUTE_HITS==0 and A.status:find('重试'))
 A.now=A.now+2.1;A:step();assert(A.fight and A.fight.golem and ROUTE_HITS[1].x==4)
`);
test('an available golem is selected before another golem that is cooling down',`
 mine126_fixture();MINE.block_list[130][4]=3;MINE.block_list[130][6]=3
 A:blacklist(4,130,30,'冷却');A:step();assert(A.fight and A.fight.x==6)
`);
test('a real visible defeated golem does not cause silent spending on ordinary excavation',`
 mine126_fixture();MINE.block_list[130][4]=3;MINE.block_list[131]={[5]=1}
 A:blacklist(4,130,120,'战斗失败');A:step();assert(#ROUTE_HITS==0 and A.status:find('重试'))
`);
test('a delayed golem query retries only the read and keeps the visible target',`
 mine126_fixture();MINE.block_list[130][6]=3;A:step();local before=#ROUTE_HITS
 A.now=A.now+7;A:step();assert(A.fight and A.fight.phase=='query' and RTSENT[1].query_golem_info)
 A.now=A.now+1;A:step();assert(#ROUTE_HITS==before and #RTSENT==1)
`);
test('a changed tile or new mine generation invalidates a stale blacklist',`
 mine126_fixture();MINE.block_list[130][6]=3;A:blacklist(6,130,120,'旧目标')
 MINE.block_list[130][6]=0;assert(not A:is_failed(6,130))
 MINE.block_list[130][6]=3;A:blacklist(6,130,120,'旧矿区');MINE.stream_generation=102
 assert(not A:is_failed(6,130))
`);
test('a nearby golem marker with an unloaded row triggers a read instead of ordinary digging',`
 mine126_fixture();MINE.golem_coordinates={{x=6,y=130}};MINE.block_list[131]={[5]=1}
 local queries=0;MINE.RequestMiningRegion=function(self,pos)queries=queries+1;assert(pos.x==6 and pos.y==130)end
 A:step();assert(queries==1 and #ROUTE_HITS==0 and A.status:find('同步'))
 MINE.block_list[130][6]=3;A.now=A.now+.1;A:step();assert(A.fight.golem)
`);
test('a stale golem marker over a known empty tile does not block descent',`
 mine126_fixture();MINE.cur_position={x=5,y=120};MINE.block_list={[120]={[5]=0,[6]=0},[121]={[5]=1}}
 MINE.golem_coordinates={{x=6,y=120}};A:step();assert(ROUTE_HITS[1].y==121)
`);
run(`MINE.DigOrCollectBlock=ROUTE_OLD_DIG;MINE.UseTNT=ROUTE_OLD_TNT;MINE.golem_coordinates={};MINE.RequestMiningRegion=nil;MINE.stream_generation=nil;A.fight=nil;A._mine_blast=nil;A._task_pending=nil;A.pending_dig=nil`);
};
