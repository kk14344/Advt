module.exports=({run,test})=>{
run(`
function refine126_fixture(temperature,process)
 refine_fixture(temperature or 0,process or 0)
 A.cfg.refineReserve=1000
 config_manager.resource_config={[23]={temperature=100},[7]={temperature=10}}
 resource_counts[23]=11000;resource_counts[7]=10000
 package.loaded['logic.resource_recycle']={GetMaterialHeat=function(self,id,num)
  return ((config_manager.resource_config[id] or {}).temperature or 0)/5*(num or 1)
 end}
end
function refine126_reply(temperature,process)
 respond('add_material_ret',{result='success',temperature=temperature,process=process or 0})
 A:daily_refine_pending()
end
`);
test('current native sulfur heat is divided by five before calculating a full batch',`
 refine126_fixture();A:daily_refine();local list=sent[#sent].add_material.materials
 assert(#list==1 and list[1].type==23 and list[1].num==500)
`);
test('partial accepted heating continues with more sulfur until no more capacity remains',`
 refine126_fixture();A:daily_refine();refine126_reply(4000,50)
 assert(not A.daily_state.refine_done and A.daily_state.refine_temperature==4000)
 A:daily_refine();assert(sent[#sent].add_material.materials[1].num==300)
 refine126_reply(10000,100);assert(A.daily_state.refine_done and A.daily_state.refine_rounds==2)
`);
test('the most stocked material leads, and no material falls below the reserve',`
 refine126_fixture(9500,0);resource_counts[7]=1040;resource_counts[23]=1025
 A:daily_refine();local list=sent[#sent].add_material.materials
 assert(#list==2 and list[1].type==7 and list[1].num==40 and list[2].type==23 and list[2].num==21)
`);
test('native fractional heat uses the same non-overflow count as the game slider',`
 refine126_fixture(9999.4,0);config_manager.resource_config[23].temperature=1
 resource_counts[7]=0;A:daily_refine();assert(sent[#sent].add_material.materials[1].num==3)
 refine126_reply(10000,50);assert(A.daily_state.refine_done)
`);
test('an unfillable residue stops when no single available material fits',`
 refine126_fixture(9999,0);assert(A:daily_refine()==nil and A.daily_state.refine_done and #sent==0)
`);
test('a successful reply without any heating or process progress cannot consume a second batch',`
 refine126_fixture(3000,0);A:daily_refine();refine126_reply(3000,0)
 assert(A.daily_state.refine_done);A:daily_refine();assert(count_event('add_material')==1)
`);
test('a full refining process opens the normal click game, waits for reward and refills only after a fresh reset',`
 refine126_fixture();A:daily_refine();refine126_reply(3000,10000)
 local visible=false
 local panel={click_play=false,touche_numbers=0,touches_time=0,start_click=false,
  touch_layer={isTouchEnabled=function()return true end}}
 panel.root_node={isVisible=function()return visible end}
 function panel:TouchEventLayer()self.started=true end
 local old_context=A.read_context
 A.read_context=function()
  return {sub_name=visible and 'resource_recycle_sub_scene' or 'main_sub_scene',sub_scene={ui_root=panel}}
 end
 local old_graphic=graphic
 graphic={DispatchEvent=function(self,name)
  if name=='show_world_sub_scene' then visible=true
  elseif name=='hide_world_sub_scene' then visible=false end
 end}
 local old_account=A.harvest_account;A.harvest_account=function()return 'TEST' end
 network.event_listener.resource_recycle_click_finish_ret=function()end
 A:daily_refine();assert(visible and count_event('add_material')==1)
 local tapping=A:daily_refine()
 assert(tapping.refineTap and tapping.refineInput=='pointer' and tapping.refineTapBatch==8 and tapping.refineClickCount==0)
 panel.touche_numbers=8;A.now=1
 local more=A:daily_refine();assert(more.refineTap and more.refineClickCount==8)
 panel.touche_numbers=120;A.now=2
 local waiting=A:daily_refine();assert(not waiting.refineTap and count_event('resource_recycle_click_finish')==0)
 A.now=31
 -- The simulated game submits its own click result after its countdown.
 network:Send({resource_recycle_click_finish={click_times=panel.touche_numbers}})
 assert(A._task_pending.refine_reward)
 respond('resource_recycle_click_finish_ret',{result='success'})
 A:daily_refine_pending();A:daily_refine();assert(sent[#sent].query_resource_recycle and count_event('add_material')==1)
 respond('query_resource_recycle_ret',{result='success',temperature=0,process=0})
 A:daily_refine_pending();A:daily_refine();assert(A.daily_state.refine_reward_claimed and count_event('add_material')==2 and not visible)
 refine126_reply(10000,100)
 A:daily_refine_schedule(A.daily_state)
 assert(A.harvest_updates.refine and A.harvest_updates.refine.heat==10000 and A.harvest_updates.refine.seconds>0)
 A.harvest_updates=nil;A.read_context=old_context;graphic=old_graphic;A.harvest_account=old_account
`);
test('a cooling wake at exactly ten percent only reschedules; below ten starts the full daily queue',`
 refine126_fixture();A.cfg.dailyTask13=true;A:start_daily(true,false,true)
 local st=A.daily_state;local original=A.harvest_account;A.harvest_account=function()return 'TEST' end
 A:daily_step();assert(count_event('query_resource_recycle')==1 and #sent==1)
 respond('query_resource_recycle_ret',{result='success',temperature=1000,process=2000})
 local r=A:harvest_result(A:daily_step())
 assert(r.done and r.refineProbeOnly and r.harvest.refine.seconds==900 and #sent==1)
 sent={};A:start_daily(true,false,true);A:daily_step()
 respond('query_resource_recycle_ret',{result='success',temperature=999,process=2000})
 r=A:daily_step();assert(not r.done and not A.daily_state.refine_followup and A.daily_state.skipped[13])
 assert(sent[2].query_daily)
 A.harvest_account=original
`);
test('a successful add reply missing heat data forces a read before any more material',`
 refine126_fixture();A:daily_refine();respond('add_material_ret',{result='success'});A:daily_refine_pending()
 A:daily_refine();assert(sent[#sent].query_resource_recycle and count_event('add_material')==1)
`);
run(`package.loaded['logic.resource_recycle']=nil`);
};
