module.exports=({run,test})=>{
// ============================================================================
// 1.44 回归测试：
//   1. 炼化 100% 小游戏（过热不再拦截、process=0/temp>0 核验、禁用 listener 自动恢复、
//      模态遮罩清理、动画卡死强制武装、live logic.resource_recycle 结算）
//   2. 每日炼金档位（免费/30血钻/60血钻 + 血钻不足不回退）与巨魔/地下副本档位上限+自动降档
//   3. 工会首领日常每日一次（北京时间00:00重置、按账号独立、0次不锁、独立按钮不限）
//   4. 合并工坊按钮模式 start_workshop("both") 一键先收后产
// ============================================================================

test('1.44 refine gate enters 100% reward mini-game even when overheat is above 10%',`
 refine126_fixture()
 A.cfg.dailyRefine=true
 A:start_daily(true,false,true)
 local r1=A:daily_step()
 assert(r1.wait,'probe should query refine info first')
 respond('query_resource_recycle_ret',{result='success',temperature=3500,process=10000})
 local r2=A:daily_step()
 assert(not r2.done and A.daily_state.refine_followup==false,'process=100% must enter daily even when overheat >= 10%')
`);

test('1.44 refine verify reward succeeds when server resets process to 0 while temperature stays positive',`
 refine126_fixture()
 A:daily_refine();refine126_reply(3000,10000)
 -- 模拟小游戏结算后进入 verify 阶段：process 已清零，但过热度仍 > 0
 A.daily_state.refine_reward_active=nil
 A.daily_state.refine_verify_reward=true
 A.daily_state.refine_loaded=false
 local q=A:daily_refine()
 assert(q.wait and sent[#sent].query_resource_recycle)
 respond('query_resource_recycle_ret',{result='success',temperature=1800,process=0})
 A:daily_refine_pending()
 assert(A.daily_state.refine_reward_claimed==true and not A.daily_state.refine_verify_reward,'process==0 with temperature>0 must count as claimed')
`);

test('1.44 refine auto-enables disabled panel.listener before tapping',`
 local panel=refine142_setup(0,true)
 local listener_enabled=false
 panel.listener={
  isEnabled=function()return listener_enabled end,
  setEnabled=function(self,v)listener_enabled=v end,
 }
 local sims=0
 aandm={simTouch=function()sims=sims+1;panel.touche_numbers=math.min(100,panel.touche_numbers+1) end}
 local r1=A:daily_refine()
 assert(listener_enabled==true and r1.wait,'disabled listener must be re-enabled automatically')
 local r2=A:daily_refine()
 assert(r2.refineTap==nil and sims==8 and panel.touche_numbers==8,'must start tapping immediately once listener is enabled')
 refine142_restore()
`);

test('1.44 refine clears modal overlays on entry and on zero-growth stall',`
 local panel=refine142_setup(0,true)
 local events={}
 local hidden=0
 local orig_ctx=A.read_context
 A.read_context=function()
  return {
   scene={
    HideAllSubPanels=function()hidden=hidden+1 end,
    msgbox={Hide=function()hidden=hidden+1 end},
   },
   sub_name='resource_recycle_sub_scene',
   sub_scene={ui_root=panel},
  }
 end
 graphic={DispatchEvent=function(self,ev)events[#events+1]=ev end}
 aandm={simTouch=function()end}
 A:daily_refine()
 local had_hide=false
 for _,e in ipairs(events) do if e=='hide_all_sub_panel' then had_hide=true end end
 assert(had_hide and hidden>=2,'must dismiss modal overlays before tapping')
 local before_hidden=hidden
 A:daily_refine()
 A:daily_refine()
 assert(hidden>before_hidden,'stall must re-clear overlays before switching channel')
 A.read_context=orig_ctx
 refine142_restore()
`);

test('1.44 refine force-arms touch_layer and listener when TouchEventLayer animation stalls past 2.5s',`
 local panel=refine142_setup(0,false)
 local layer_on=false
 local listener_on=false
 panel.touch_layer={
  isTouchEnabled=function()return layer_on end,
  setTouchEnabled=function(self,v)layer_on=v end,
 }
 panel.listener={
  isEnabled=function()return listener_on end,
  setEnabled=function(self,v)listener_on=v end,
 }
 panel.click_play=false;panel.start_click=false
 A:daily_refine()
 A.now=2
 A:daily_refine()
 assert(panel.armed==1 and not layer_on,'first arm calls TouchEventLayer')
 A.now=5
 local r=A:daily_refine()
 assert(r.wait and layer_on==true and listener_on==true,'stalled animation past 2.5s must force-enable touch_layer and listener')
 refine142_restore()
`);

test('1.44 refine completes when live logic.resource_recycle process resets to zero after settlement',`
 local panel=refine142_setup(0,true)
 local rr={process=10000,temperature=2500,click_finish_pending=false}
 local prev_rr=package.loaded['logic.resource_recycle']
 package.loaded['logic.resource_recycle']=rr
 aandm={simTouch=function()panel.touche_numbers=math.min(100,panel.touche_numbers+1) end}
 A:daily_refine()
 assert(panel.touche_numbers==8)
 -- 游戏倒计时结束并完成结算，live module 的 process 归零
 rr.click_finish_pending=true
 local w=A:daily_refine()
 assert(w.wait and A.daily_state.refine_reward_active.seen_click_finish==true)
 rr.click_finish_pending=false;rr.process=0
 local done=A:daily_refine()
 assert(done.wait and A.daily_state.refine_verify_reward==true and A.daily_state.refine_reward_active==nil,'process reset to 0 must transition to verify')
 package.loaded['logic.resource_recycle']=prev_rr
 refine142_restore()
`);

test('1.44 alchemy tier 2 sends index 2 (30 blood) and records blood diamond spend',`
 reset_daily_fixture()
 A:spend_reset_day()
 local orig_cfg=constants.ALCHEMY_CONFIG
 constants.ALCHEMY_CONFIG={{lv=1,req_value=0},{lv=1,req_value=30},{lv=2,req_value=60}}
 for id=1,15 do A.cfg['dailyTask'..id]=false end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=true
 A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 A.cfg.dailyAlchemyTier=2;A.cfg.spendBudget=500
 resource_counts[1]=200
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true}
 daily.alchemy_info={mark=0}
 local sent_idx=nil
 for tick=1,40 do
  A.now=tick;clock=1000+tick
  local r=A:daily_step();service_daily_request()
  for _,m in ipairs(sent) do if m.take_daily_alchemy then sent_idx=m.take_daily_alchemy.index end end
  if r.done then break end
 end
 assert(sent_idx==2,'tier 2 must request take_daily_alchemy index 2, got '..tostring(sent_idx))
 assert(daily.alchemy_info.mark==1,'alchemy must complete')
 assert((A.spend_session or 0)==30,'tier 2 must record 30 blood spend, got '..tostring(A.spend_session))
 constants.ALCHEMY_CONFIG=orig_cfg
`);

test('1.44 alchemy tier 3 sends index 3 (60 blood) and records blood diamond spend',`
 reset_daily_fixture()
 A:spend_reset_day()
 local orig_cfg=constants.ALCHEMY_CONFIG
 constants.ALCHEMY_CONFIG={{lv=1,req_value=0},{lv=1,req_value=30},{lv=2,req_value=60}}
 for id=1,15 do A.cfg['dailyTask'..id]=false end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=true
 A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 A.cfg.dailyAlchemyTier=3;A.cfg.spendBudget=500
 resource_counts[1]=200
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true}
 daily.alchemy_info={mark=0}
 local sent_idx=nil
 for tick=1,40 do
  A.now=tick;clock=1000+tick
  local r=A:daily_step();service_daily_request()
  for _,m in ipairs(sent) do if m.take_daily_alchemy then sent_idx=m.take_daily_alchemy.index end end
  if r.done then break end
 end
 assert(sent_idx==3,'tier 3 must request take_daily_alchemy index 3, got '..tostring(sent_idx))
 assert((A.spend_session or 0)==60,'tier 3 must record 60 blood spend, got '..tostring(A.spend_session))
 constants.ALCHEMY_CONFIG=orig_cfg
`);

test('1.44 paid alchemy skips cleanly without falling back to free when blood diamonds insufficient',`
 reset_daily_fixture()
 A:spend_reset_day()
 local orig_cfg=constants.ALCHEMY_CONFIG
 constants.ALCHEMY_CONFIG={{lv=1,req_value=0},{lv=1,req_value=30},{lv=2,req_value=60}}
 for id=1,15 do A.cfg['dailyTask'..id]=false end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=true
 A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 A.cfg.dailyAlchemyTier=3;A.cfg.spendBudget=500
 resource_counts[1]=10
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true}
 daily.alchemy_info={mark=0}
 local res
 for tick=1,40 do
  A.now=tick;clock=1000+tick
  res=A:daily_step();service_daily_request()
  if res.done then break end
 end
 for _,m in ipairs(sent) do assert(not m.take_daily_alchemy,'must not send any alchemy request when blood insufficient') end
 assert(daily.alchemy_info.mark==0,'must preserve today alchemy mark for later')
 constants.ALCHEMY_CONFIG=orig_cfg
`);

test('1.44 dungeon tier cap limits troll and underground levels and auto-downgrades on lock or BP',`
 reset_daily_fixture()
 local orig_ev=config_manager.mining_event_config
 local orig_bp=troop.GetTroopBP
 local orig_cave_buy=A.cfg.spendAllowCaveBuy
 A.cfg.spendAllowCaveBuy=false
 config_manager.mining_event_config={[1]={},[3]={}}
 for lv=1,12 do
  config_manager.mining_event_config[1][lv]={cave_type=1,level=lv,bp_limit=lv*1000,pickaxe_count=1,name='地下'..lv}
  config_manager.mining_event_config[3][lv]={cave_type=3,level=lv,bp_limit=lv*1000,pickaxe_count=1,name='巨魔'..lv}
 end
 -- 1) 巨魔设定 Lv6、地下设定 Lv8，战力仅 5500 -> 两者都自动降档至 Lv5
 mining.cave_levels={[1]=10,[3]=10}
 mining.cave_challenge_nums={[1]=1,[3]=1}
 mining.cave_config_info={[7]={1,3}}
 for id=1,15 do A.cfg['dailyTask'..id]=(id==9 or id==10) end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
 A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 A.cfg.dailyTrollLevel=6
 A.cfg.dailyUndergroundLevel=8
 troop.GetTroopBP=function()return 5500 end
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
 local sent_troll,sent_ug=nil,nil
 for tick=1,60 do
  A.now=tick;clock=1000+tick
  local r=A:daily_step();service_daily_request()
  for _,m in ipairs(sent) do
   if m.solve_cave_event then
    if m.solve_cave_event.cave_type==3 and not sent_troll then sent_troll=m.solve_cave_event.cave_lv end
    if m.solve_cave_event.cave_type==1 and not sent_ug then sent_ug=m.solve_cave_event.cave_lv end
   end
  end
  if r.done then break end
 end
 assert(sent_troll==5,'troll target Lv6 with BP 5500 must auto-downgrade to Lv5, got '..tostring(sent_troll))
 assert(sent_ug==5,'underground target Lv8 with BP 5500 must auto-downgrade to Lv5, got '..tostring(sent_ug))

 -- 2) 战力充足但解锁仅 Lv4、目标 Lv9 -> 自动降档至 Lv4；解锁 Lv10、目标 Lv7 -> 精确打 Lv7
 reset_daily_fixture()
 A.cfg.spendAllowCaveBuy=false
 mining.cave_levels={[1]=4,[3]=10}
 mining.cave_challenge_nums={[1]=1,[3]=1}
 mining.cave_config_info={[7]={1,3}}
 troop.GetTroopBP=function()return 999999 end
 for id=1,15 do A.cfg['dailyTask'..id]=(id==9 or id==10) end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
 A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=false;A.cfg.dailyEvents=false
 A.cfg.dailyTrollLevel=7
 A.cfg.dailyUndergroundLevel=9
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
 sent_troll,sent_ug=nil,nil
 for tick=1,60 do
  A.now=tick;clock=1000+tick
  local r=A:daily_step();service_daily_request()
  for _,m in ipairs(sent) do
   if m.solve_cave_event then
    if m.solve_cave_event.cave_type==3 and not sent_troll then sent_troll=m.solve_cave_event.cave_lv end
    if m.solve_cave_event.cave_type==1 and not sent_ug then sent_ug=m.solve_cave_event.cave_lv end
   end
  end
  if r.done then break end
 end
 assert(sent_troll==7,'troll unlocked Lv10 with cap Lv7 must run Lv7, got '..tostring(sent_troll))
 assert(sent_ug==4,'underground unlocked Lv4 with cap Lv9 must auto-downgrade to Lv4, got '..tostring(sent_ug))
 config_manager.mining_event_config=orig_ev
 troop.GetTroopBP=orig_bp
 A.cfg.spendAllowCaveBuy=orig_cave_buy
`);

test('1.44 daily guild boss executes once per Beijing day per account, resets next day, and allows standalone fights',`
 reset_daily_fixture()
 A:spend_reset_day()
 local orig_acct=A.harvest_account
 local orig_guild=guild
 guild={guild_cur_boss_id=3,guild_boss_over=false,fright=0,guild_boss_buy_count=0}
 function guild:GetFrightBossState() return self.fright or 0 end
 function guild:IsOpenGuildBoss() return true end
 function guild:QueryGuildBossInfo() network:Send({query_guild_boss_info={}}) end
 function guild:GetBuyTicketMax() return math.max(0,10-(self.guild_boss_buy_count or 0)) end
 function guild:BuyTicketCost(num)
  local cost=0
  for i=(self.guild_boss_buy_count or 0)+1,(self.guild_boss_buy_count or 0)+num do
   cost=cost+(i<=3 and 20 or (i<=6 and 40 or 60))
  end
  return cost
 end
 package.loaded['logic.guild']=guild
 local cur_acct='ACCT_A'
 A.harvest_account=function()return cur_acct end
 local function run_guild_daily(start_clock)
  sent={}
  A:spend_reset_day()
  for id=1,15 do A.cfg['dailyTask'..id]=false end
  A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
  A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=true;A.cfg.dailyEvents=false
  A.cfg.spendBudget=2000;resource_counts[1]=2000;resource_counts[51]=0
  guild.guild_boss_buy_count=0;guild.fright=0;guild.guild_boss_over=false
  A:start_daily()
  A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
  local buys,fights,done_rec,last_res=0,0,nil,nil
  local counted=0
  for tick=1,160 do
   A.now=tick;clock=start_clock+tick
   last_res=A:daily_step();service_daily_request()
   if last_res and last_res.guildBossDone then done_rec=last_res.guildBossDone end
   for i=counted+1,#sent do
    if sent[i].guild_boss_buy_ticket then buys=buys+1 end
    if sent[i].guild_boss_challenge then fights=fights+1 end
   end
   counted=#sent
   if last_res and last_res.done then break end
  end
  return buys,fights,done_rec,last_res
 end

 -- 同一北京时间自然日：第 1 次执行买满+挑战并产出 guildBossDone
 local day1_clock=1700000000
 local b1,f1,rec1=run_guild_daily(day1_clock)
 assert(b1==1 and f1==1,'first daily run must buy and challenge guild boss, got '..b1..'/'..f1)
 assert(rec1 and rec1.account=='ACCT_A' and rec1.day==math.floor((day1_clock+28800)/86400),'must emit guildBossDone record')

 -- 同一北京时间自然日：第 2 次日常必须跳过买票与挑战
 local b2,f2,_,res2=run_guild_daily(day1_clock+3600)
 assert(b2==0 and f2==0,'second daily on same Beijing day must skip guild boss entirely')
 assert(res2 and res2.message:find('今日已执行过'),'summary must explain skip')

 -- 另一账号 ACCT_B 同一天不受 ACCT_A 影响
 cur_acct='ACCT_B'
 local bB,fB=run_guild_daily(day1_clock+4000)
 assert(bB==1 and fB==1,'different account must still run guild boss')

 -- 跨北京时间 00:00 后（次日），ACCT_A 重新执行
 cur_acct='ACCT_A'
 local b3,f3=run_guild_daily(day1_clock+86400)
 assert(b3==1 and f3==1,'next Beijing day must run guild boss again')

 -- 独立手动按钮 start_guild_boss 不受今日日常标记限制
 sent={};resource_counts[51]=2;guild.fright=0;guild.guild_boss_over=false
 A:start_guild_boss(1)
 local standalone_fights=0
 for tick=1,40 do
  A.now=tick;clock=day1_clock+86500+tick
  local r=A:guild_boss_step();service_daily_request()
  if r and r.done then break end
 end
 for _,m in ipairs(sent) do if m.guild_boss_challenge then standalone_fights=standalone_fights+1 end end
 assert(standalone_fights==1,'standalone guild boss button must never be blocked by daily dedup')

 guild=orig_guild;package.loaded['logic.guild']=guild
 A.harvest_account=orig_acct
`);

test('1.44 daily guild boss does NOT lock today when 0 fights succeeded',`
 reset_daily_fixture()
 A:spend_reset_day()
 local orig_acct=A.harvest_account
 local orig_guild=guild
 guild={guild_cur_boss_id=3,guild_boss_over=false,fright=0,guild_boss_buy_count=0}
 function guild:GetFrightBossState() return self.fright or 0 end
 function guild:IsOpenGuildBoss() return true end
 function guild:QueryGuildBossInfo() network:Send({query_guild_boss_info={}}) end
 function guild:GetBuyTicketMax() return 10 end
 function guild:BuyTicketCost(num) return num*20 end
 package.loaded['logic.guild']=guild
 A.harvest_account=function()return 'ACCT_RETRY' end
 for id=1,15 do A.cfg['dailyTask'..id]=false end
 A.cfg.dailyRewards=false;A.cfg.dailyFreePrayer=false;A.cfg.dailyFreeAlchemy=false
 A.cfg.dailyVoidExchange=false;A.cfg.dailyGuildBoss=true;A.cfg.dailyEvents=false
 A.cfg.spendBudget=0;resource_counts[1]=0;resource_counts[51]=0
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true}
 for tick=1,120 do
  A.now=tick;clock=1700000000+tick
  local r=A:daily_step();service_daily_request()
  if r and r.done then break end
 end
 assert(not A:guild_boss_done_today(),'0-fight guild boss run must NOT mark today done')
 guild=orig_guild;package.loaded['logic.guild']=guild
 A.harvest_account=orig_acct
`);

test('1.44 workshop merged mode both collects finished projects then produces to fill empty slots',`
 reset_daily_fixture()
 local orig_quarry=config_manager.mining_quarry_config
 config_manager.mining_quarry_config={
  {ID=8,name='深层L2',need_layer=120,need_time=120,dig_count=40,tnt_count=0}
 }
 A.cfg.workshopDepth=150;A.cfg.workshopMinutes=120
 mining.dig_depth=180;mining.dig_count=200;mining.max_project_count=2
 mining.project_list={{id=101,endtime=500},{id=102,endtime=9999}}
 clock=1000
 local orig_query=network.event_listener.query_mining_info_ret
 local orig_reward=network.event_listener.get_mining_project_reward_ret
 local orig_add=network.event_listener.add_mining_project_ret
 network.event_listener.query_mining_info_ret=function(msg)
  mining.dig_count=msg.dig_count or mining.dig_count
  mining.dig_depth=msg.dig_depth or mining.dig_depth
  mining.project_list=msg.project_list or mining.project_list
  mining.max_project_count=msg.project_max_count or mining.max_project_count
 end
 network.event_listener.get_mining_project_reward_ret=function(msg)
  if msg.result=='success' then mining.project_list={{id=102,endtime=9999}} end
 end
 network.event_listener.add_mining_project_ret=function(msg)
  if msg.result=='success' then table.insert(mining.project_list,{id=103,endtime=8200}) end
 end
 sent={}
 A:start_workshop('both')
 local collected,produced,final_res=0,0,nil
 for tick=1,40 do
  A.now=tick;clock=1000+tick
  final_res=A:workshop_step()
  local p=A._task_pending
  if p and not p.response then
   local msg=sent[#sent];local name=next(msg)
   if name=='query_mining_info' then
    respond('query_mining_info_ret',{result='success',dig_count=mining.dig_count,dig_depth=mining.dig_depth,
     project_list=mining.project_list,project_max_count=mining.max_project_count})
   elseif name=='get_mining_project_reward' then
    collected=collected+1;respond('get_mining_project_reward_ret',{result='success'})
   elseif name=='add_mining_project' then
    produced=produced+1;respond('add_mining_project_ret',{result='success'})
   end
  end
  if final_res and final_res.done then break end
 end
 assert(final_res and final_res.done and not final_res.error,tostring(final_res and final_res.message))
 assert(collected==1 and produced==1,'both mode must collect 1 finished then produce 1 empty slot, got '..collected..'/'..produced)
 assert(#mining.project_list==2,'workshop slots must be full after both mode')
 config_manager.mining_quarry_config=orig_quarry
 network.event_listener.query_mining_info_ret=orig_query
 network.event_listener.get_mining_project_reward_ret=orig_reward
 network.event_listener.add_mining_project_ret=orig_add
`);
};
