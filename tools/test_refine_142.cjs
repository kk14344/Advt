module.exports=({run,test})=>{
run(`
local refine142_context,refine142_graphic

function refine142_setup(count,enabled)
 refine126_fixture()
 A:daily_refine();refine126_reply(3000,10000)
 local panel={click_play=true,touche_numbers=count or 0,touches_time=30,start_click=(count or 0)>0,
  touch_layer={isTouchEnabled=function()return enabled~=false end}}
 panel.root_node={isVisible=function()return true end}
 function panel:TouchEventLayer()self.armed=(self.armed or 0)+1;self.click_play=true end
 refine142_context=A.read_context;refine142_graphic=graphic
 A.read_context=function()
  return {sub_name='resource_recycle_sub_scene',sub_scene={ui_root=panel}}
 end
 graphic={DispatchEvent=function()end}
 return panel
end

function refine142_restore()
 A.read_context=refine142_context;graphic=refine142_graphic
 aandm=nil
end
`);

test('refine 1.42 native simulated touch fills the game counter from zero and stops at the client cap',`
 local panel=refine142_setup(0,true)
 local sims=0
 aandm={simTouch=function(x,y)sims=sims+1;panel.touche_numbers=math.min(100,panel.touche_numbers+1) end}
 local first=A:daily_refine()
 assert(first.refineTap==nil and sims==8 and panel.touche_numbers==8)
 for i=1,20 do A:daily_refine() end
 assert(panel.touche_numbers==100)
 assert(A.daily_state.refine_reward_active.cap==100)
 local settled=A:daily_refine()
 assert(settled.refineTap==nil and panel.touche_numbers==100)
 refine142_restore()
`);

test('refine 1.42 a manual first click is kept and topped up instead of re-arming the layer',`
 local panel=refine142_setup(1,true)
 local sims=0
 aandm={simTouch=function(x,y)sims=sims+1;panel.touche_numbers=math.min(100,panel.touche_numbers+1) end}
 local first=A:daily_refine()
 assert(first.refineTap==nil and sims==8 and panel.touche_numbers==9)
 assert(panel.armed==nil)
 for i=1,20 do A:daily_refine() end
 assert(panel.touche_numbers==100)
 refine142_restore()
`);

test('refine 1.42 zero counter growth switches the input channel after two batches',`
 local panel=refine142_setup(0,true)
 local sims=0
 aandm={simTouch=function()sims=sims+1 end}
 local modes={}
 for i=1,6 do
  local r=A:daily_refine()
  if r.refineInput then modes[#modes+1]=r.refineInput end
 end
 assert(sims==16 and modes[1]=='pointer' and modes[2]=='pointer' and modes[3]=='touch' and modes[4]=='touch')
 assert(A.daily_state.refine_reward_active.mode=='touch')
 refine142_restore()
`);

test('refine a stall below the known 100 click cap changes input mode',`
 local panel=refine142_setup(95,true)
 local sims=0
 aandm={simTouch=function()sims=sims+1 end}
 local r
 for i=1,6 do r=A:daily_refine() end
 assert(A.daily_state.refine_reward_active.cap==nil)
 assert(A.daily_state.refine_reward_active.mode=='touch')
 assert(r.refineTap and sims==16)
 refine142_restore()
`);

test('refine 1.42 a disabled click layer is re-armed once and then tapped',`
 local panel=refine142_setup(0,false)
 local sims=0
 aandm={simTouch=function(x,y)sims=sims+1;panel.touche_numbers=math.min(100,panel.touche_numbers+1) end}
 A:daily_refine()
 A.now=2
 local arm=A:daily_refine()
 assert(panel.armed==1 and arm.wait)
 panel.touch_layer.isTouchEnabled=function()return true end
 local r=A:daily_refine()
 assert(r.refineTap==nil and sims==8 and panel.armed==1)
 refine142_restore()
`);

test('refine 1.42 the last batch never exceeds the remaining redundant target',`
 local panel=refine142_setup(116,true)
 local r=A:daily_refine()
 assert(r.refineTap and r.refineTapBatch==4 and r.refineClickCount==116)
 refine142_restore()
`);

test('refine 1.42 reaches the one hundred twenty ceiling when the client allows it',`
 local panel=refine142_setup(0,true)
 aandm={simTouch=function(x,y)panel.touche_numbers=math.min(120,panel.touche_numbers+1) end}
 for i=1,20 do A:daily_refine() end
 assert(panel.touche_numbers==120)
 local r=A:daily_refine()
 assert(r.refineTap==nil)
 refine142_restore()
`);

test('refine 1.42 pointer mode is requested when the client has no native touch simulation',`
 local panel=refine142_setup(0,true)
 aandm=nil
 local r=A:daily_refine()
 assert(r.refineTap and r.refineInput=='pointer' and r.refineTapBatch==8 and r.refineClickCount==0)
 refine142_restore()
`);
};
