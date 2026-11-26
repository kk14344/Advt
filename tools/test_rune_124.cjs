module.exports=({run,test})=>{
run(`
function setup_r124(crystals)
 setup_r123(crystals);RN.loadouts={{slots={0,0,0,0,0}},{slots={0,0,0,0,0}},{slots={0,0,0,0,0}}};RN.index=1
 rune.rune_loadouts={};rune.rune_bootstrap_ready=true;rune.rune_native_pending=0
 function rune:GetRuneLoadouts()return self.rune_loadouts end
 function rune:GetLoadoutIndex()return self.loadout_index or 1 end
 function rune:IsRuneEquippedAnyLoadout(info)
  for _,page in ipairs(self.rune_loadouts)do for _,id in ipairs(page.slots)do if id==info.rune_id then return true end end end
  return (info.equip_pos or 0)>0
 end
 function rune:IsRuneControlPending()return self.rune_query_pending or self.rune_query_needed or self.rune_control_pending or self.rune_native_pending>0 end
 function rune:QueryRuneLoadouts()
  self.rune_query_needed=true
  if not self.rune_bootstrap_ready or self.rune_query_pending then return false end
  self.rune_query_pending=true;self.rune_query_since=clock
  return network:Send({query_rune_loadouts={}})
 end
 function rune:UpdateRuneControl()
  if self.rune_query_pending and clock-self.rune_query_since>8 then self.rune_query_pending=false;self.rune_query_needed=true end
  if self.rune_query_needed and not self.rune_query_pending then self:QueryRuneLoadouts()end
 end
 function rune:SendRuneNative(msg)
  if msg.upgrade_rune then
   assert(not self:IsRuneControlPending(),'upgrade must wait for native controls')
   RN.native_upgrades=(RN.native_upgrades or 0)+1
   for _,id in ipairs(msg.upgrade_rune.exp_rune_id_list)do
    for _,r in ipairs(self.rune_list)do if r.rune_id==id then
     assert(not r.locked and not self:IsRuneEquippedAnyLoadout(r),'protected material')
    end end
   end
  end
  self.rune_native_pending=self.rune_native_pending+1
  return network:Send(msg)
 end
 local legacy=network.event_listener.query_rune_info_ret
 network.event_listener.query_rune_info_ret=function(msg)
  legacy(msg);for _,r in ipairs(rune.rune_list)do r.equip_pos=0 end;rune.rune_query_needed=true
 end
 network.event_listener.query_rune_loadouts_ret=function(msg)
  rune.rune_query_pending=false;rune.rune_query_needed=false;rune.rune_native_pending=0
  rune.rune_list=vcopy(RN.pool);rune.rune_temporary_list=vcopy(RN.temporary)
  rune.rune_loadouts=vcopy(RN.loadouts);rune.loadout_index=RN.index
 end
 for _,name in ipairs({'draw_rune_ret','rune_one_key_receive_ret','upgrade_rune_ret'})do
  local previous=network.event_listener[name]
  network.event_listener[name]=function(msg)
   rune.rune_native_pending=math.max(0,rune.rune_native_pending-1);previous(msg)
   if name=='upgrade_rune_ret' then rune:QueryRuneLoadouts()end
  end
 end
end
function service_r124()
 local p=A._task_pending;if not p or p.response then return end
 if p.kind=='growth_rune_loadouts' then
  RN.order[#RN.order+1]='query_rune_loadouts'
  respond(p.event,{result=RN.control_result or 0,rune_list=vcopy(RN.pool),rune_temporary_list=vcopy(RN.temporary)})
 else service_r123()end
end
function run_r124(steps)
 for i=1,steps do A.now=A.now+.1;clock=1000+A.now
  local result=A:rune_step();service_r124();if result.done then break end
 end
end
`);
test('current rune client requires the independent loadout snapshot before upgrading',`
setup_r124(0);RN.pool={rtarget(1,1,0,0),rfood(2)};RN.loadouts[1].slots[1]=1
A:start_rune();run_r124(8)
assert(RN.upgrades==1 and RN.native_upgrades==1)
assert(RN.order[1]=='query_rune_info' and RN.order[2]=='query_rune_loadouts' and RN.order[3]=='upgrade_rune')
`);
test('active rune pages two and three work even when legacy equip positions are zero',`
for page=2,3 do
 setup_r124(0);RN.index=page;RN.pool={rtarget(1,1,0,0),rfood(2)};RN.loadouts[page].slots[5]=1
 A:start_rune();run_r124(8);assert(RN.upgrades==1,'page '..page)
end
`);
test('the active loadout overrides stale equip positions belonging to a different page',`
setup_r124(0);RN.index=2;RN.pool={rtarget(1),rtarget(3,1,0,0),rfood(2)}
RN.loadouts[1].slots[1]=1;RN.loadouts[2].slots[1]=3
A:start_rune();run_r124(8)
local upgrade;for _,msg in ipairs(sent)do if msg.upgrade_rune then upgrade=msg.upgrade_rune end end
assert(upgrade and upgrade.rune_id==3)
`);
test('locked experience runes and runes equipped on inactive pages cannot be consumed',`
setup_r124(0);local locked=rfood(3);locked.locked=true
RN.pool={rtarget(1,1,0,0),rfood(2),locked,rfood(4),rfood(5)}
RN.loadouts[1].slots[1]=1;RN.loadouts[2].slots[2]=4;RN.loadouts[3].slots[5]=5
A:start_rune();run_r124(8)
local used;for _,msg in ipairs(sent)do if msg.upgrade_rune then used=msg.upgrade_rune.exp_rune_id_list end end
assert(used and #used==1 and used[1]==2 and RN.upgrades==1)
`);
test('missing template metadata is resolved from the current rune configuration',`
setup_r124(0);local target=rtarget(1,1,0,0);local food=rfood(2)
config_manager.rune_config={[1]=target.template_info,[2]=food.template_info}
target.template_info=nil;food.template_info=nil;RN.pool={target,food};RN.loadouts[1].slots[1]=1
A:start_rune();A:rune_step();service_r124();A:rune_step();service_r124();A:rune_step()
assert(sent[#sent].upgrade_rune and sent[#sent].upgrade_rune.rune_id==1,'phase='..tostring(A.rune_state.phase)..' pending='..tostring(A._task_pending and A._task_pending.kind)..' last='..tostring(next(sent[#sent]))..' targetmeta='..tostring(rune.rune_list[1].template_info))
`);
test('a loadout query started by the native upgrade handler is adopted without another request',`
setup_r124(0);RN.pool={rtarget(1,1,0,0),rfood(2)};RN.loadouts[1].slots[1]=1
A:start_rune();run_r124(3)
assert(RN.upgrades==1 and rune.rune_query_pending)
local count=#sent;A:rune_step();assert(#sent==count and A._task_pending.kind=='growth_rune_loadouts')
service_r124();A:rune_step();assert(#sent==count)
`);
test('rejected loadout snapshots never permit upgrades or draws before a successful sync',`
setup_r124();RN.pool={rtarget(1,1,0,0),rfood(2)};RN.loadouts[1].slots[1]=1;RN.control_result='failure'
A:start_rune();run_r124(12);assert(RN.draws==0 and RN.upgrades==0)
RN.control_result=nil;A.now=A.now+3;run_r124(8);assert(RN.upgrades>0)
`);
test('rune controls wait through login bootstrap without sending a write',`
setup_r124();RN.pool={rtarget(1,1,0,0),rfood(2)};RN.loadouts[1].slots[1]=1;rune.rune_bootstrap_ready=false
A:start_rune();run_r124(8);assert(#sent==1 and RN.upgrades==0 and RN.draws==0)
rune.rune_bootstrap_ready=true;run_r124(6);assert(RN.upgrades==1)
`);
test('a lost rune loadout reply retries only the read and does not consume material',`
setup_r124(0);RN.pool={rtarget(1,1,0,0),rfood(2)};RN.loadouts[1].slots[1]=1
A:start_rune();A:rune_step();service_r124();A:rune_step();assert(A._task_pending.kind=='growth_rune_loadouts')
local count=#sent;A.now=10;clock=1010;A:rune_step();A:rune_step()
assert(#sent==count+1 and (sent[#sent].query_rune_loadouts or sent[#sent].query_rune_info) and RN.upgrades==0)
service_r124();for i=1,5 do A.now=A.now+.1;clock=1000+A.now;A:rune_step();if sent[#sent].upgrade_rune then break end;service_r124()end
assert(sent[#sent].upgrade_rune)
`);
test('every continuous draw and receive is followed by loadout sync and equipped reinforcement',`
setup_r124();RN.pool={rtarget(1,1,0,0)};RN.loadouts[1].slots[1]=1
A:start_rune();run_r124(40)
local order=table.concat(RN.order,',')
assert(order:find('draw_rune,rune_one_key_receive,query_rune_info,query_rune_loadouts,upgrade_rune',1,true),order)
assert(RN.upgrades>2 and RN.draws>2 and A.rune_state)
`);
test('scheduled finite runes also synchronize active equipment before reinforcement',`
setup_r124(0);RN.pool={rtarget(1,1,0,0),rfood(2)};RN.index=3;RN.loadouts[3].slots[1]=1
A:start_rune(false)
for i=1,20 do local result=A:rune_step();service_r124();if result.done then break end end
assert(RN.upgrades==1)
`);
test('manual stop during loadout refresh cannot trigger a later rune upgrade',`
setup_r124(0);RN.pool={rtarget(1,1,0,0),rfood(2)};RN.loadouts[1].slots[1]=1
A:start_rune();A:rune_step();service_r124();A:rune_step();local count=#sent;A:cancel_task()
respond('query_rune_loadouts_ret',{result=0});assert(not A.rune_state and #sent==count)
`);
};
