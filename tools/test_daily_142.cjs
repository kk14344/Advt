module.exports=({run,test})=>{
run(`
local adventure142_cc,adventure142_graphic,adventure142_context,adventure142_hide,adventure142_modules

function adventure142_setup(boxes,options)
 options=options or {}
 adventure142_modules={adventure=package.loaded['logic.adventure'],bag=package.loaded['logic.bag']}
 local world={visible=false,loot=false,open_btn=options.open_btn~=false,bag_free=options.bag_free~=false,hides=0}
 package.loaded['logic.adventure']={
  explored_box_num=boxes,OpenBox=function(self)world.opened=(world.opened or 0)+1;network:Send({open_box={}}) end}
 package.loaded['logic.bag']={GetSpaceCount=function()return world.bag_free and 20 or 0 end}
 network.event_listener.open_box_ret=function()end
 adventure142_cc=cc;adventure142_graphic=graphic;adventure142_context=A.read_context;adventure142_hide=A.hide_panel
 local scene={sub_panels={loot_result_panel={IsVisible=function()return world.loot end}}}
 cc={Director={getInstance=function() return {getRunningScene=function() return scene end} end}}
 local old_hide=A.hide_panel
 A.hide_panel=function(self,name) if name=='loot_result_panel' then world.loot=false end end
 graphic={DispatchEvent=function(self,name)
  if name=='show_world_sub_scene' then world.visible=true
  elseif name=='hide_world_sub_scene' then world.visible=false end
 end}
 A.read_context=function()
  return {sub_name=world.visible and 'exploring_sub_scene' or 'main_sub_scene',
   sub_scene=world.visible and {ui_root={open_box_btn=world.open_btn and {}}} or nil,
   scene={sub_panels={loot_result_panel={IsVisible=function()return world.loot end}}}}
 end
 local st={running=true,started=A.now or 0}
 return st,world
end

function adventure142_restore()
 cc=adventure142_cc;graphic=adventure142_graphic;A.read_context=adventure142_context;A.hide_panel=adventure142_hide
 package.loaded['logic.adventure']=adventure142_modules.adventure
 package.loaded['logic.bag']=adventure142_modules.bag
end
`);

test('adventure 1.42 skips quietly when the adventure page has no claimable result',`
 local st,world=adventure142_setup(0)
 local r=A:daily_adventure_claim(st)
 assert(r.done and st.adventure_done and not world.visible and count_event('open_box')==0)
 adventure142_restore()
`);

test('adventure 1.42 opens the page, uses the magnifier binding once and closes the loot window',`
 local st,world=adventure142_setup(3)
 local r=A:daily_adventure_claim(st)
 assert(not r.done and world.visible and count_event('open_box')==0,tostring(r.message)..'|'..tostring(world.visible)..'|'..count_event('open_box'))
 r=A:daily_adventure_claim(st)
 assert(count_event('open_box')==1 and world.opened==1 and not r.done,tostring(r.message)..'|'..count_event('open_box')..'|'..tostring(world.opened))
 respond('open_box_ret',{result='success',open_box_num=3})
 world.loot=true
 A:daily_adventure_claim(st)
 r=A:daily_adventure_claim(st)
 assert(r.done and st.adventure_done and not world.visible and not world.loot)
 assert(st.adventure_summary:find('已领取 3 个宝箱',1,true)~=nil)
 adventure142_restore()
`);

test('adventure 1.42 waits for the result window before closing the page',`
 local st,world=adventure142_setup(2)
 A:daily_adventure_claim(st);A:daily_adventure_claim(st)
 respond('open_box_ret',{result='success',open_box_num=2})
 local r
 for i=1,2 do r=A:daily_adventure_claim(st);if world.loot then break end end
 assert(not r.done and world.visible and not world.loot)
 world.loot=true
 r=A:daily_adventure_claim(st)
 assert(not r.done and not world.loot)
 r=A:daily_adventure_claim(st)
 assert(r.done and not world.visible)
 adventure142_restore()
`);

test('adventure accepts a partial native reward when the bag fills and does not open again',`
 local st,world=adventure142_setup(3)
 A:daily_adventure_claim(st);A:daily_adventure_claim(st)
 respond('open_box_ret',{result='bag_full',open_box_num=1})
 world.loot=true
 A:daily_adventure_claim(st)
 local r=A:daily_adventure_claim(st)
 assert(r.done and r.partial and not world.loot and not world.visible)
 assert(count_event('open_box')==1 and st.adventure_summary:find('已领取 1 个宝箱',1,true))
 adventure142_restore()
`);

test('adventure 1.42 keeps a full bag untouched',`
 local st,world=adventure142_setup(5,{bag_free=false})
 A:daily_adventure_claim(st)
 local r=A:daily_adventure_claim(st)
 assert(r.done and st.adventure_done and count_event('open_box')==0 and not world.visible)
 assert(st.adventure_summary:find('背包已满',1,true)~=nil)
 adventure142_restore()
`);

test('adventure 1.42 a rejected claim stops after one attempt',`
 local st,world=adventure142_setup(4)
 A:daily_adventure_claim(st);A:daily_adventure_claim(st)
 respond('open_box_ret',{result='failure'})
 local r=A:daily_adventure_claim(st)
 assert(r.done and r.partial and st.adventure_done and count_event('open_box')==1)
 adventure142_restore()
`);

test('adventure 1.42 a missing acknowledgement does not repeat the claim',`
 local st,world=adventure142_setup(1)
 A:daily_adventure_claim(st);A:daily_adventure_claim(st)
 A.now=16
 local r=A:daily_adventure_claim(st)
 assert(r.done and r.partial and st.adventure_done and count_event('open_box')==1 and not world.visible)
 adventure142_restore()
`);

test('adventure 1.42 a client without the magnifier binding is skipped',`
 local st,world=adventure142_setup(2,{open_btn=false})
 A:daily_adventure_claim(st)
 local r=A:daily_adventure_claim(st)
 assert(r.done and st.adventure_done and count_event('open_box')==0)
 assert(st.adventure_summary:find('放大镜',1,true)~=nil)
 adventure142_restore()
`);

test('adventure 1.42 the completion chain starts with the adventure claim',`
 setup_120();A.cfg.dailyRefine=false;A.cfg.dailyIncludeMine=false;A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 package.loaded['logic.adventure']={explored_box_num=0,OpenBox=function() end}
 local called=0
 local original=A.daily_adventure_claim
 A.daily_adventure_claim=function(self,st) called=called+1; return original(self,st) end
 start_119()
 local r=run_119()
 A.daily_adventure_claim=original
 assert(r and r.done and called==1,tostring(r and r.message))
 assert(A.daily_state and A.daily_state.adventure_done)
 package.loaded['logic.adventure']=nil
`);

test('adventure 1.42 the daily state enables the claim and reports it as its own row',`
 A:start_daily(true)
 local st=A.daily_state
 assert(st.adventure_claim==true and st.adventure_done==false)
 st.adventure_done=true;st.adventure_summary='冒险领取：已领取 3 个宝箱'
 local report=A:daily_report(false)
 local row
 for _,item in ipairs(report.rows) do if item.key=='adventure_claim' then row=item end end
 assert(row and row.name=='冒险探险结果' and row.status=='已处理' and row.detail=='冒险领取：已领取 3 个宝箱')
 A:cancel_task();A.daily_state=nil
`);
};
