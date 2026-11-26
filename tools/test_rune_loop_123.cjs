module.exports=({run,test})=>{
run(`
function setup_r123(crystals)
 A:cancel_task();A._cancelled_task_pending=nil;A.now=0;clock=1000;A.inBattle=false;sent={};A.cfg.runeUseFlashGold=true
 constants.RESOURCE_TYPE.crystal=54;resource_counts={[54]=crystals or 10000}
 config_manager.rune_draw_config={[1]={cost_num=1}};constants.MAX_RUNE_BAG_MUILT_SELECT_NUM=10
 RN={pool={},temporary={},next_id=100,order={},capacity=100,draws=0,upgrades=0}
 rune={rune_list={},rune_temporary_list={},rune_bag_capacity=100,cur_draw_platform_id=1}
 function rune:SetSelectedAutoCompose(value)self.auto_compose=value end
 function rune:SetSelectedAutoGo(value)self.auto_go=value end
 function rune:SetDrawState()end
 function rune:SetIsMovingState()end
 function rune:GetRunePreviewLevelAndExp(target,list)return (target.level or 0)+#list,0 end
 package.loaded['logic.rune']=rune
 network.event_listener.query_rune_info_ret=function(msg)rune.rune_list=vcopy(msg.rune_list or {});rune.rune_temporary_list=vcopy(msg.rune_temporary_list or {});rune.rune_bag_capacity=msg.capacity or 100 end
 network.event_listener.draw_rune_ret=function(msg)if msg.result=='success'then rune.rune_temporary_list=vcopy(RN.temporary)end end
 network.event_listener.rune_one_key_receive_ret=function(msg)rune.rune_list=vcopy(RN.pool);rune.rune_temporary_list=vcopy(RN.temporary)end
 network.event_listener.upgrade_rune_ret=function(msg)if msg.result=='success' then rune.rune_list=vcopy(RN.pool)end end
end
function rtarget(id,level,exp,equipped)
 return {rune_id=id,template_id=1,level=level or 1,exp=exp or 0,equip_pos=equipped or 1,template_info={type=2,quality=5,level_limit=10}}
end
function rfood(id,quality)return {rune_id=id,template_id=2,level=1,exp=0,equip_pos=0,template_info={type=1,quality=quality or 4,level_limit=1,out_exp=100}}end
function service_r123()
 local p=A._task_pending;if not p or p.response then return end
 local key,payload=next(sent[#sent]);RN.order[#RN.order+1]=key
 if key=='query_rune_info' then respond(p.event,{result='success',rune_list=vcopy(RN.pool),rune_temporary_list=vcopy(RN.temporary),capacity=RN.capacity})
 elseif key=='draw_rune' then
  RN.draws=RN.draws+1
  if RN.draw_failure then respond(p.event,{result='not_enough_resource'});return end
  local amount=payload.draw_type=='ten_times' and 10 or 1
  local cost=config_manager.rune_draw_config[rune.cur_draw_platform_id].cost_num*amount
  assert(payload.go_to_area_4==false and resource_counts[54]>=cost)
  resource_counts[54]=resource_counts[54]-cost
  RN.next_id=RN.next_id+1;RN.temporary={rfood(RN.next_id)}
  respond(p.event,{result='success'})
 elseif key=='rune_one_key_receive' then
  if not RN.bag_blocked then for _,r in ipairs(RN.temporary)do RN.pool[#RN.pool+1]=r end;RN.temporary={}end
  respond(p.event,{})
 elseif key=='upgrade_rune' then
  RN.upgrades=RN.upgrades+1
  if not RN.no_progress then
   for _,id in ipairs(payload.exp_rune_id_list)do
    for i,r in ipairs(RN.pool)do if r.rune_id==id then assert(r.equip_pos==0 and not r.locked);table.remove(RN.pool,i);break end end
   end
   for _,r in ipairs(RN.pool)do if r.rune_id==payload.rune_id then r.level=math.min(10,r.level+1);r.exp=0 end end
  end
  respond(p.event,{result='success'})
 else error('unexpected rune-loop request '..key)end
end
function run_r123(steps,delta)
 local result
 for i=1,steps do
  A.now=A.now+(delta or .1);result=A:rune_step();service_r123()
  if result.done then break end
 end
 return result
end
`);
test('manual rune target waits, rejects other IDs and upgrades only the chosen equipped rune',`
setup_r123(0);RN.pool={rtarget(1),rtarget(2,3,0,2),rfood(3)}
A:start_rune(true,true)
local result=run_r123(5)
assert(result.runeTargets and #result.runeTargets==2 and result.runeTargets[2].id=='2')
assert(RN.upgrades==0 and not A:choose_rune_target('3'))
assert(A:choose_rune_target('2'))
result=run_r123(10)
assert(result.done and RN.upgrades==1 and RN.pool[1].level==1 and RN.pool[2].level==4)
assert(result.message:find('材料 1 枚'))
`);
test('canceling rune selection consumes no reinforcement material',`
setup_r123(0);RN.pool={rtarget(1),rfood(2)}
A:start_rune(true,true);assert(run_r123(5).runeTargets)
assert(A:choose_rune_target(''))
assert(run_r123(10).done and RN.upgrades==0 and #RN.pool==2)
`);
test('daily rune action asks for an equipped target and uses one dedicated rune after selection',`
setup_r123(0)
rune.rune_list={rtarget(1),rtarget(2,2,0,2),rfood(3)}
A:start_daily(true)
local step=A:daily_manual_rune(rune)
assert(step.runeTargets and #sent==0 and A:choose_rune_target('2'))
A:daily_manual_rune(rune)
assert(#sent==1 and sent[1].upgrade_rune.rune_id==2 and sent[1].upgrade_rune.exp_rune_id_list[1]==3)
`);
test('continuous runes upgrade existing equipped runes before the next draw',`
setup_r123();RN.pool={rtarget(1),rfood(2)};A:start_rune();run_r123(15)
assert(RN.order[1]=='query_rune_info' and RN.order[2]=='upgrade_rune' and RN.draws>=1 and RN.upgrades>=2)
assert(A.rune_state.continuous and rune.auto_go==false)
`);
test('each rune draw is followed by receive authoritative refresh and equipped upgrade',`
setup_r123();RN.pool={rtarget(1)};A:start_rune();run_r123(12)
local order=table.concat(RN.order,',')
assert(order:find('draw_rune,rune_one_key_receive,query_rune_info,upgrade_rune',1,true),order)
`);
test('continuous rune drawing does not finish when all equipped runes are maxed',`
setup_r123();RN.pool={rtarget(1,10)};A:start_rune();run_r123(30)
assert(RN.draws>=3 and RN.upgrades==0 and A.rune_state)
`);
test('continuous rune drawing does not require any rune to be equipped',`
setup_r123();A:start_rune();run_r123(25);assert(RN.draws>=3 and A.rune_state)
`);
test('rune drawing ends after the balance cannot afford a single draw',`
setup_r123(0);RN.pool={rtarget(1)};A:start_rune();local result=run_r123(10,1)
assert(RN.draws==0 and result.done and result.message:find('水晶不足'))
`);
test('equipped rune upgrades continue even when no drawing crystals remain',`
setup_r123(0);RN.pool={rtarget(1),rfood(2)};A:start_rune();run_r123(10,1)
assert(RN.upgrades==1 and RN.draws==0 and A.rune_state.upgrades==1)
`);
test('rune equipment is refreshed before choosing an upgrade target',`
setup_r123();RN.pool={rtarget(1),rfood(2)};rune.rune_list={rtarget(1,1,0,0)}
A:start_rune();run_r123(4);assert(RN.upgrades==1)
`);
test('rune feeding includes unlocked spare property runes and honors the flash-gold switch',`
setup_r123(0);A.cfg.runeUseFlashGold=false
local spare=rtarget(3,1,0,0);spare.template_info.out_exp=100
RN.pool={rtarget(1),rfood(2,6),spare,rfood(4,4)}
A:start_rune();run_r123(10,1)
local ids={};for _,r in ipairs(RN.pool)do ids[r.rune_id]=true end
assert(ids[2] and not ids[3] and not ids[4] and RN.upgrades==1)
`);
test('a rune upgrade acknowledgement without progress cannot repeat material consumption',`
setup_r123();RN.pool={rtarget(1),rfood(2)};RN.no_progress=true
A:start_rune();local result=run_r123(35,1);assert(RN.upgrades==1 and result.done and result.error)
`);
test('a failed rune draw is not counted as ten successful draws',`
setup_r123();RN.pool={rtarget(1,10)};RN.draw_failure=true
A:start_rune();local result=run_r123(12,.1)
assert(RN.draws==2 and A.rune_state.drawn==0 and A.rune_state.ten_pulls==0 and result.done)
`);
test('rune receive with unreleased temporary runes ends cleanly instead of spamming',`
setup_r123();RN.pool={rtarget(1,10)};RN.bag_blocked=true
A:start_rune();local result=run_r123(30,1)
local receives=0;for _,msg in ipairs(sent)do if msg.rune_one_key_receive then receives=receives+1 end end
assert(receives==1 and RN.draws==1 and result.done and result.message:find('背包'))
`);
test('rune looping continues beyond the old thousand-request draw cap',`
setup_r123(100000);RN.capacity=1200;RN.pool={rtarget(1,10)};A:start_rune();run_r123(4200,.05)
assert(RN.draws>1000 and A.rune_state and A.rune_state.drawn>=10000)
`);
test('manual cancellation prevents any later rune continuation',`
setup_r123();RN.pool={rtarget(1,10)};A:start_rune()
for i=1,10 do A:rune_step();if A._task_pending and A._task_pending.kind=='growth_rune_draw'then break end;service_r123()end
assert(A._task_pending.kind=='growth_rune_draw');local count=#sent;A:cancel_task()
respond('draw_rune_ret',{result='success'});assert(not A.rune_state and #sent==count)
`);
test('the scheduled single-run rune mode still terminates rather than blocking later tasks',`
setup_r123(0);rune.rune_list={rtarget(1,10)};A:start_rune(false)
local r=A:rune_step();assert(r.done and not A.rune_state.continuous)
`);
test('a full rune bag without eligible food finishes with the capacity reason',`
setup_r123();RN.pool={rtarget(1)};RN.bag_blocked=true;A:start_rune();run_r123(25,1)
local receives=0;for _,msg in ipairs(sent)do if msg.rune_one_key_receive then receives=receives+1 end end
assert(receives==1 and RN.draws==1 and RN.upgrades==0)
`);
test('21490 crystals at the 3000-cost platform single-draws seven times and drains upgrade materials before ending',`
setup_r123(21490);config_manager.rune_draw_config={[5]={cost_num=3000}};rune.cur_draw_platform_id=5
RN.pool={rtarget(1)};A:start_rune();local result=run_r123(120,.1)
assert(result.done and result.message:find('水晶不足') and RN.draws==7 and RN.upgrades==1)
assert(resource_counts[54]==490 and A.rune_state.drawn==7 and A.rune_state.ten_pulls==0 and #RN.temporary==0)
local order=table.concat(RN.order,',')
assert(order:find('upgrade_rune',1,true)>order:match('.*()draw_rune'))
assert(#RN.pool==1 and RN.pool[1].level>1)
`);
test('a full rune bag uses unlocked flash-gold spares before taking another draw',`
setup_r123(50);RN.capacity=3
local flash=rtarget(2,1,0,0);flash.template_info.quality=6;flash.template_info.out_exp=1200
local locked=rfood(3,6);locked.locked=true
RN.pool={rtarget(1),flash,locked};A:start_rune();run_r123(3)
assert(RN.order[2]=='upgrade_rune' and RN.order[3]=='draw_rune')
local feed;for _,m in ipairs(sent)do if m.upgrade_rune then feed=m.upgrade_rune.exp_rune_id_list end end
assert(#feed==1 and feed[1]==2)
`);
};
