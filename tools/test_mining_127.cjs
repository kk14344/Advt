module.exports=({run,test})=>{
run(`
 function treasure127(x,y)
  mine126_fixture();A.cfg.autoTreasureEvent=true;constants.RANDOM_EVENT_TYPE.treasure_hunt=71
  MINE.random_event_list={{event_type=71,position={x=x,y=y},end_time=5000}}
  MINE.RequestMiningRegion=nil
 end
`);
test('a distant treasure with one blocked direction continues along an ordinary visible route',`
 treasure127(38,136);MINE.block_list[130][6]=60;MINE.block_list[131]={[5]=1,[6]=1}
 A:step();assert(#ROUTE_HITS==1 and ROUTE_HITS[1].y==131 and ROUTE_HITS[1].kind==1)
 assert(RTTNT==20 and not A._mine_blast and not A.status:find('不可达'))
`);
test('an unknown treasure approach requests the missing region without spending picks or TNT',`
 treasure127(38,136);local reads={}
 MINE.RequestMiningRegion=function(self,pos,w,h,force)reads[#reads+1]={x=pos.x,y=pos.y};return true end
 A:step();assert(#reads>=1 and #ROUTE_HITS==0 and #RTSENT==0 and not A._mine_blast)
 local target_read=false;for _,r in ipairs(reads)do if r.x==38 and r.y==136 then target_read=true end end
 assert(target_read and A.status:find('加载'))
 local count=#reads;route_tick(.1);assert(#reads==count,'region reads must be throttled')
`);
test('a streamed treasure corridor resumes excavation as soon as its tiles arrive',`
 treasure127(12,130);MINE.RequestMiningRegion=function()return true end
 A:step();assert(#ROUTE_HITS==0)
 for x=6,12 do MINE.block_list[130][x]=1 end
 route_tick(1);assert(#ROUTE_HITS==1 and ROUTE_HITS[1].x==6 and RTTNT==20)
`);
test('a far treasure beyond the normal route horizon advances toward the target',`
 treasure127(305,130);MINE.block_list[130][6]=1
 A:step();assert(#ROUTE_HITS==1 and ROUTE_HITS[1].x==6 and not A._mine_blast)
`);
test('a far treasure uses the nearest explored edge rather than stalling in an empty waypoint',`
 treasure127(305,130);for x=6,100 do MINE.block_list[130][x]=0 end
 MINE.block_list[130][101]=1
 A:step();assert(#ROUTE_HITS==1 and ROUTE_HITS[1].x==101)
`);
test('a shallow treasure advances on the first tick despite incoming map updates',`
 treasure127(-27,73)
 MINE.cur_position={x=-17,y=23};MINE.block_list={[23]={[-17]=0}}
 for y=24,74 do MINE.block_list[y]={[-17]=1} end
 for y=60,78 do
  local row=MINE.block_list[y] or {};MINE.block_list[y]=row
  for x=-33,-21 do row[x]=1 end
 end
 for step=1,3 do
  local y=23+step
  A:mining_on_response('query_mining_block_info_ret',{})
  if step==1 then A:step() else route_tick(.1) end
  assert(#ROUTE_HITS==step and ROUTE_HITS[step].x==-17 and ROUTE_HITS[step].y==y,A.status)
  assert(not A._route_job and not A._mine_blast)
 end
`);
test('treasure opens when reachable without waiting on the distant planner',`
 treasure127(6,130)
 local opened=0;local old=MINE.CheckTreasureHuntEvent
 MINE.CheckTreasureHuntEvent=function(self,x,y)assert(x==6 and y==130);opened=opened+1;return true end
 A:step();assert(opened==1 and #ROUTE_HITS==0 and A.status:find('开启藏宝箱'))
 route_tick(.1);assert(opened==1)
 MINE.CheckTreasureHuntEvent=old
`);
test('one gem on a treasure route does not force TNT when another ordinary direction is open',`
 treasure127(8,130);MINE.block_list={
  [130]={[5]=0,[6]=8,[7]=1,[8]=1},[131]={[5]=1,[6]=1,[7]=1}}
 A:step();assert(#ROUTE_HITS==1 and ROUTE_HITS[1].y==131 and RTTNT==20 and not A._mine_blast)
`);
test('a treasure genuinely enclosed on all sides uses the necessary TNT opening',`
 treasure127(8,130);MINE.block_list={
  [130]={[5]=0,[6]=1,[7]=60,[8]=1,[9]=60},[129]={[8]=60},[131]={[8]=60}}
 A:step();assert(ROUTE_HITS[1].x==6)
 route_tick();assert(ROUTE_HITS[2].kind==60 and A._mine_blast)
`);
test('a fully enclosed paid-chest barrier cannot be treated as a free or unknown route',`
 mine126_fixture();A.cfg.autoChest=false;MINE.block_list={
  [130]={[5]=0,[6]=41,[7]=3,[8]=41},[129]={[7]=41},[131]={[7]=41}}
 local plan=A:plan_mining_target(A:scan_targets());assert(not plan.path)
 A:step();assert(#ROUTE_HITS==0 and #RTSENT==0)
`);
run(`MINE.random_event_list={};MINE.RequestMiningRegion=nil`);
};
