module.exports=({run,test})=>{
run(`
 function fast135_fixture()
  mine126_fixture();A._route_job=nil;A._task_pending=nil;A.fight=nil;A.cfg.autoGolem=true
  package.loaded['logic.user'].query_queue={};RTNET.session=1;RTNET.internal_session=1
  RTNET.IsConnected=function()return true end;WIRE135={};RTNET.Send=function(net,msg)WIRE135[#WIRE135+1]=msg;return true end
  RTNET.event_listener.query_golem_info_ret=function(m)MINE.cur_event_id=m.event_id;MINE.cur_event_position={x=m.x,y=m.y}end
  MINE.DigOrCollectBlock=function(self,x,y)RTNET:Send({query_golem_info={x=x,y=y}})end
  MINE.SolveEvent=function(self,id)RTNET:Send({dig_block={position=self.cur_event_position}});return true end
  MINE.block_list[130][4]=3;MINE.cur_event_id=nil;MINE.cur_event_position=nil
  A:install_task_hooks();A:install_request_pipeline()
 end
`);
test('135 blocked golem read is queued until the next free tick, without a six-second wait',`
 fast135_fixture();RTNET.internal_session=2
 assert(A:start_target({x=4,y=130,golem=true,block_type=3,label='魔像'}));assert(#WIRE135==0 and A.fight.transport.queued)
 A.now=A.now+.05;A:update_fight();assert(#WIRE135==0)
 RTNET.session=2;A.now=A.now+.05;A:update_fight();assert(#WIRE135==1 and WIRE135[1].query_golem_info and not A.fight.transport.queued)
 RTNET.event_listener.query_golem_info_ret({x=4,y=130,event_id=6000001});A.now=A.now+.05;A:update_fight()
 assert(#WIRE135==2 and WIRE135[2].dig_block and A.fight.phase=='solve')
`);
test('135 rejected native query retries promptly and never queues a duplicate while busy',`
 fast135_fixture();local rejects=0;local raw=RTNET._mwa_pipeline_roots[RTNET.Send]
 RTNET.Send=function(net,msg)if rejects==0 then rejects=1;return false end;return raw(net,msg)end
 A:install_request_pipeline();A:start_target({x=4,y=130,golem=true,label='魔像'})
 assert(#WIRE135==0 and A.fight.transport.queued);A.now=A.now+.05;A:update_fight();assert(#WIRE135==1)
 A.now=A.now+.1;A:update_fight();assert(#WIRE135==1)
`);
test('135 unsent chest write waits for the channel and sends once without re-solving',`
 fast135_fixture();A:start_target({x=4,y=130,chest=true,label='宝箱'})
 RTNET.event_listener.query_golem_info_ret({x=4,y=130,event_id=6000001});RTNET.internal_session=2
 A.now=A.now+.05;A:update_fight();assert(A.fight.phase=='solve' and A.fight.transport.queued and #WIRE135==1)
 RTNET.session=2;A.now=A.now+.05;A:update_fight();assert(#WIRE135==2 and WIRE135[2].dig_block)
 for i=1,10 do A.now=A.now+.05;A:update_fight() end
 assert(#WIRE135==2,'accepted resource write replayed')
`);
test('135 lost read acknowledgement retries a read only and preserves target priority',`
 fast135_fixture();A:start_target({x=4,y=130,golem=true,label='魔像'});A.now=A.now+.8;A:update_fight()
 assert(#WIRE135==2 and WIRE135[2].query_golem_info and A.fight.x==4)
 A.now=A.now+.1;A:update_fight();assert(#WIRE135==2)
`);
test('135 highest-priority golem avoids evaluating a distant treasure route',`
 fast135_fixture();local calls=0;local blast=A.blast_path;A.blast_path=function(self,t)calls=calls+1;return blast(self,t)end
 local p=A:plan_mining_target({{x=4,y=130,golem=true,label='魔像',manhattan=1},{x=2000,y=400,treasure=true,label='藏宝箱',manhattan=2265}})
 assert(p.golem and #p.path==0 and calls==0);A.blast_path=blast
`);
test('135 distant ordinary target yields between bounded slices and caches unchanged planning',`
 fast135_fixture();MINE.block_list={[130]={[5]=0}};local target={{x=120,y=130,label='普通目标',manhattan=115}}
 local pending,slices,plan=true,0,nil
 while pending do plan,pending=A:plan_mining_target_budgeted(target);slices=slices+1;assert(slices<300) end
 assert(slices>1 and plan and plan.path and #plan.path>0)
 local job=A._route_job;local again,busy=A:plan_mining_target_budgeted(target);assert(again==plan and not busy and A._route_job==job)
 A:mining_on_response('query_mining_block_info_ret',{});A:plan_mining_target_budgeted(target);assert(A._route_job~=job)
`);
test('135 arriving golem preempts the treasure approach before digging',`
 fast135_fixture();MINE.block_list={[130]={[5]=0,[6]=1}};local treasure={{x=500,y=130,treasure=true,manhattan=495}}
 local first,waiting=A:plan_mining_target_budgeted(treasure);assert(not waiting and first.path[1].x==6)
 MINE.block_list[130][4]=3;local p,busy=A:plan_mining_target_budgeted({{x=4,y=130,golem=true,manhattan=1},{x=500,y=130,treasure=true,manhattan=495}})
 assert(not busy and p.golem and #p.path==0)
`);
// Same route and VM: compare one blocking search with cooperative frames.
const baseline=[],frames=[],slices=[];
for(let trial=0;trial<3;trial++){
 run(`fast135_fixture();MINE.block_list={[130]={[5]=0}};BENCH135={{x=800,y=130,label='普通目标',manhattan=795}};A._route_job=nil`);
 let start=performance.now();run(`A:plan_mining_target(BENCH135)`);baseline.push(performance.now()-start);
 run(`A._route_job=nil`);let pending=1,count=0;
 while(pending){start=performance.now();pending=run(`local p,b=A:plan_mining_target_budgeted(BENCH135);return b and 1 or 0`,'budget benchmark',true);frames.push(performance.now()-start);if(++count>300)throw Error('planner did not complete');}slices.push(count);
}
const fs=require('fs'),path=require('path');
fs.writeFileSync(path.join(__dirname,'../dist/mining-135-performance.json'),JSON.stringify({result:'passed',scenario:'same distant ordinary target route in Fengari; offline CPU only',monolithicMs:baseline,slices:slices,frameMaxMs:Math.max(...frames),frameMeanMs:frames.reduce((a,b)=>a+b,0)/frames.length,frameCount:frames.length,maxNodesPerSlice:256,realAccountTested:false},null,2));
run(`MINE.DigOrCollectBlock=ROUTE_OLD_DIG;MINE.UseTNT=ROUTE_OLD_TNT;MINE.golem_coordinates={};A.fight=nil;RTNET.session=nil;RTNET.internal_session=nil;RTNET.IsConnected=nil;A._route_job=nil;MINE.SolveEvent=function()return true end`);
};

