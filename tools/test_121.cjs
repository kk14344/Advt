module.exports=({run,test})=>{
run(`
function refill_fixture()
 setup_120();A.cfg.autoUseEarthHeart=true;A.cfg.earthUseAll=false
 constants.RESOURCE_TYPE.ultimate_tool=7;mining.dig_count=0;mining.dig_max_count=1000
 resource_counts[7]=30
 bag_logic.item_list={};bag_logic.capacity=30;package.loaded['logic.bag']=bag_logic
 network.event_listener.query_bag_info_ret=function(msg)
  if msg.item_list then bag_logic.item_list=msg.item_list end;bag_logic.capacity=msg.max_count or 30
 end
 network.event_listener.use_item_ret=function(msg)
  if msg.result=='success' then
   for i,item in ipairs(bag_logic.item_list)do if item.item_id==msg.item_id then table.remove(bag_logic.item_list,i);break end end
   mining.dig_count=mining.dig_count+(msg.add_dig or 30)
  end
 end
end
function refine_fixture(temp,process)
 setup_120();A.cfg.dailyRefine=true
 A.cfg.refineReserve=0
 network.event_listener.query_resource_recycle_ret=function()end
 network.event_listener.add_material_ret=function()end
 start_119();A.daily_state.refine_loaded=true
 A.daily_state.refine_temperature=temp or 0;A.daily_state.refine_process=process or 0
 config_manager.resource_config={[23]={temperature=90},[7]={temperature=10},[8]={temperature=10},[12]={temperature=60}}
 resource_counts[23]=2000;resource_counts[7]=1000;resource_counts[8]=1000;resource_counts[12]=1000
end
`);
test('gold summon skips a price above twenty million',`
setup_120();A.cfg.dailyGoldSummon=true;gold_cost=20000001;resource_counts[3]=1000000000
start_119();local r=run_119();assert(r.done and count_event('recruit_mercenary')==0)
`);
test('gold summon allows exactly twenty million then checks the new price',`
setup_120();A.cfg.dailyGoldSummon=true;gold_cost=20000000;resource_counts[3]=1000000000
start_119();local r=run_119();assert(r.done and count_event('recruit_mercenary')==1 and A.daily_state.gold_summons==1)
`);
test('ordinary liveness fallback also refuses gold summons above twenty million',`
setup_120();A.cfg.dailyGoldSummon=false;gold_cost=20000001;resource_counts[3]=1000000000;resource_counts[2]=0
start_119();A:daily_action(8,daily_conf_for_test[8],{completion_count=0})
assert(count_event('recruit_mercenary')==0 and A.daily_state.skipped[8])
`);
test('earth hearts are used before bags and unrelated items are preserved',`
refill_fixture();bag_logic.item_list={{item_id=11,template_id=6000005},{item_id=12,template_id=123}}
A:start_refill('workshop',20)
assert(sent[1].use_item.item_id==11 and count_event('use_mining_tool')==0)
respond('use_item_ret',{result='success',item_id=11,add_dig=30})
assert(sent[2].query_mining_info)
respond('query_mining_info_ret',{dig_count=30})
assert(A:refill_step().refill_done and #bag_logic.item_list==1 and bag_logic.item_list[1].item_id==12)
`);
test('use-all hearts sends the next item immediately after each acknowledgement and queries only once',`
refill_fixture();A.cfg.earthUseAll=true
bag_logic.item_list={{item_id=11,template_id=6000005},{item_id=12,template_id=123},{item_id=13,template_id=6000005}}
A:start_refill('daily',10,2)
local id=sent[#sent].use_item.item_id;respond('use_item_ret',{result='success',item_id=id,add_dig=30})
assert(count_event('use_item')==2 and count_event('query_mining_info')==0,'no host step or query between hearts')
id=sent[#sent].use_item.item_id;respond('use_item_ret',{result='success',item_id=id,add_dig=30})
assert(count_event('query_mining_info')==1 and count_event('use_mining_tool')==0)
respond('query_mining_info_ret',{dig_count=60});assert(A:refill_step().refill_done)
assert(#bag_logic.item_list==1 and bag_logic.item_list[1].template_id==123)
`);
test('as-needed hearts stop at the required count',`
refill_fixture();bag_logic.item_list={{item_id=11,template_id=6000005},{item_id=13,template_id=6000005}}
A:start_refill('daily',20,2);local id=sent[#sent].use_item.item_id
respond('use_item_ret',{result='success',item_id=id,add_dig=30});respond('query_mining_info_ret',{dig_count=30})
assert(A:refill_step().refill_done and count_event('use_item')==1 and #bag_logic.item_list==1)
`);
test('bag plan chains five five two and verifies the entire sixty-pickaxe refill once',`
refill_fixture();A:start_refill('daily',60,2)
assert(sent[#sent].use_mining_tool.num==5)
respond('use_mining_tool_ret',{result='success',resource_id=7,num=5})
assert(sent[#sent].use_mining_tool.num==5)
respond('use_mining_tool_ret',{result='success',resource_id=7,num=5})
assert(sent[#sent].use_mining_tool.num==2)
respond('use_mining_tool_ret',{result='success',resource_id=7,num=2})
assert(count_event('query_mining_info')==1 and resource_counts[7]==18)
respond('query_mining_info_ret',{dig_count=60});assert(A:refill_step().refill_done)
`);
test('an acknowledgement for another item cannot advance the refill queue',`
refill_fixture();A.cfg.earthUseAll=true;bag_logic.item_list={{item_id=11,template_id=6000005},{item_id=13,template_id=6000005}}
A:start_refill('daily',10,2);respond('use_item_ret',{result='success',item_id=99})
assert(#sent==1 and not A._task_pending.response)
`);
test('a rejected heart stops the queue without consuming bags',`
refill_fixture();bag_logic.item_list={{item_id=11,template_id=6000005}}
A:start_refill('daily',40,2);respond('use_item_ret',{result='not_enough_item',item_id=11})
local r=A:refill_step();assert(r.error and count_event('use_item')==1 and count_event('use_mining_tool')==0)
`);
test('an uncertain refill is not retried after timeout',`
refill_fixture();A:start_refill('daily',60,2);A.now=21
local r=A:refill_step();assert(r.error and #sent==1 and A._cancelled_task_pending)
respond('use_mining_tool_ret',{result='success',resource_id=7,num=5});assert(#sent==1)
`);
test('cancelling a use-all refill prevents the next heart from being sent',`
refill_fixture();A.cfg.earthUseAll=true;bag_logic.item_list={{item_id=11,template_id=6000005},{item_id=13,template_id=6000005}}
A:start_refill('daily',40,2);local id=sent[#sent].use_item.item_id
A:cancel_task();respond('use_item_ret',{result='success',item_id=id})
assert(#sent==1 and not A.fast_refill)
`);
test('an unopened bag is queried before falling back to mining bags',`
refill_fixture();bag_logic.capacity=1;bag_logic.item_list={}
A:start_refill('workshop',20);assert(sent[#sent].query_bag_info)
respond('query_bag_info_ret',{max_count=30,item_list={{item_id=11,template_id=6000005}}})
assert(sent[#sent].use_item.item_id==11 and count_event('use_mining_tool')==0)
`);
test('automatic refining computes the most stocked material first and fills the remainder in one batch',`
refine_fixture(0,0);A:daily_refine()
local m=sent[#sent].add_material.materials
assert(#m==2 and m[1].type==23 and m[1].num==111 and m[2].type==7 and m[2].num==1)
local heat=0;for _,v in ipairs(m)do heat=heat+v.num*config_manager.resource_config[v.type].temperature end
assert(heat==10000 and count_event('add_material')==1)
respond('add_material_ret',{result='success',temperature=10000,process=50});A:daily_step()
assert(A.daily_state.refine_done and count_event('add_material')==1)
`);
test('refining never exceeds the remaining temperature capacity',`
refine_fixture(9950,0);A:daily_refine();local m=sent[#sent].add_material.materials
assert(#m==1 and m[1].type==7 and m[1].num==5)
`);
test('refining at full process waits for the native finish game without invented clicks',`
refine_fixture(0,10000);assert(A:daily_refine()==nil and A.daily_state.refine_done and #sent==0)
`);
test('refining at full temperature consumes no material',`
refine_fixture(10000,0);assert(A:daily_refine()==nil and A.daily_state.refine_done and #sent==0)
`);
test('refining uses actual stock and does not consume an unfillable final remainder',`
refine_fixture(0,0);resource_counts[23]=1;resource_counts[7]=0;resource_counts[8]=0;resource_counts[12]=0
A:daily_refine();local m=sent[#sent].add_material.materials
assert(#m==1 and m[1].type==23 and m[1].num==1)
`);
test('refining queries the server before calculating a new run',`
refine_fixture();A.daily_state.refine_loaded=false;A:daily_refine();assert(sent[#sent].query_resource_recycle)
respond('query_resource_recycle_ret',{result='success',temperature=9100,process=0});A:daily_step()
assert(sent[#sent].add_material.materials[1].num==10)
`);
test('a refining rejection is not repeated',`
refine_fixture();A:daily_refine();respond('add_material_ret',{result='not_enough_resource'})
A:daily_step();A:daily_step();assert(A.daily_state.refine_done and count_event('add_material')==1)
`);
test('an uncertain refining mutation is never repeated',`
refine_fixture();A:daily_refine();A.now=21;A:daily_step();A.now=22;A:daily_step()
assert(not A.daily_state.refine_done and count_event('add_material')==1 and count_event('query_resource_recycle')==1)
`);
test('refining respects its daily checklist switch',`
refine_fixture();A.daily_state.refine_enabled=false;assert(A:daily_refine()==nil and #sent==0)
`);
};
