// v1.8 消费策略中心 + 审计 + 兼容性自检（PRD P0-03 / P0-04 / P1-02）
module.exports=({run,test,toLua})=>{
run(`
function reset_spend()
 A:cancel_task();A._cancelled_task_pending=nil;sent={};A.now=0;clock=1000;A.inBattle=false;enough_blood=true
 A.spend_session=0;A.cfg.spendBase=0;A.cfg.spendDay=0
 A.cfg.spendBudget=360;A.cfg.spendAllowCaveBuy=true;A.cfg.reserveTnt=0
 A.audit_buf={};A.audit_seq=0
 A.failed={};A.fight_unconfirmed={}
end
function audit_names()
 local t={}
 for _,e in ipairs(A:audit_tail(0)) do t[#t+1]=e.dir..':'..e.name..(e.result and ('@'..e.result) or '') end
 return table.concat(t,' | ')
end
`)

test('spend accounting records cave buy price ladder via the ret hook',`
 reset_spend()
 mining.cave_buy_challenge_nums={[3]=3}
 local cost_seen={}
 network.event_listener.buy_cave_challenge_ret=function(msg)
  if msg.result=='success' then
   mining.cave_buy_challenge_nums[msg.cave_type]=mining.cave_buy_challenge_nums[msg.cave_type]-1
  end
 end
 A:install_task_hooks()
 respond('buy_cave_challenge_ret',{result='success',cave_type=3})
 respond('buy_cave_challenge_ret',{result='success',cave_type=3})
 respond('buy_cave_challenge_ret',{result='success',cave_type=3})
 assert(A.spend_session==20+40+60,'must record 20+40+60 from the price ladder, got '..tostring(A.spend_session))
 assert(A:spend_total()==120,'total must include the base(0) + session')
`)
test('spend accounting records arena refresh cost',`
 reset_spend()
 A:install_task_hooks()
 respond('refresh_arena_rival_ret',{result='success'})
 assert(A.spend_session==60,'arena refresh must record the game cost, got '..tostring(A.spend_session))
 respond('refresh_arena_rival_ret',{result='failure'})
 assert(A.spend_session==60,'failed refresh must not be charged')
`)
test('legacy budgets never cap spending',`
 reset_spend();A.cfg.spendBudget=0;A.spend_session=99999
 assert(A:spend_can_afford(1000000))
 A.cfg.spendBudget=1;assert(A:spend_can_afford(1000000))
`)
test('arena refresh ignores a legacy zero budget when actual diamonds are sufficient',`
 reset_spend();A.cfg.spendBudget=0;resource_counts[1]=10000
 A.cfg.arenaAllowBloodRefresh=true
 A:start_arena(2);A:arena_step()
 respond('query_arena_rival_ret',{rival={{state=true}},challenge_num=10,refresh_time=clock+100})
 A:arena_step();assert(sent[#sent].refresh_arena_rival)
`)
test('daily troll ignores the removed purchase policy and buys every available attempt',`
 reset_daily_fixture()
 function troop:GetTroopBP()return 10000000 end
 function troop:CheckMercenaryNum()return true end
 function troop:CheckForgeWeaponResource()return true end
 mining.cave_config_info={[7]={3}}
 mining.cave_levels={[3]=3}
 mining.cave_challenge_nums={[3]=3}
 mining.cave_buy_challenge_nums={[3]=3}
 config_manager.mining_event_config={
  [3]={{cave_type=3,level=1,bp_limit=0,pickaxe_count=1,name='巨魔幼儿园'},
   {cave_type=3,level=3,bp_limit=0,pickaxe_count=9,name='巨魔幼儿园'}}}
 for id=1,15 do A.cfg['dailyTask'..id]=false end
 A.cfg.dailyTask9=true
 A.cfg.spendAllowCaveBuy=false
 A:start_daily()
 A.daily_state.loaded={daily=true,liveness=true,checkin=true,prayer=true,alchemy=true,caves=true}
 daily.liveness_list={{liveness_id=9,completion_count=0,is_reward=false}};daily.activity_receive_list={}
 local result,buys,seen=nil,0,0
 for tick=1,200 do
  A.now=tick;clock=1000+tick
  result=A:daily_step();service_daily_request()
  for i=seen+1,#sent do if sent[i].buy_cave_challenge then buys=buys+1 end end
  seen=#sent
  if result.done then break end
 end
 assert(result and result.done,tostring(result and result.message)..' [solves='..tostring(server_counts[9])..']')
 assert(buys==3,'removed policy must not block buying, got '..tostring(buys))
 assert(server_counts[9]==6,'must finish free and bought attempts, solves='..tostring(server_counts[9])..
  ' skip='..tostring(A.daily_state and A.daily_state.skipped and A.daily_state.skipped[9])..
  ' stalls='..tostring(A.daily_state and A.daily_state.stalls and A.daily_state.stalls[9])..
  ' msg='..tostring(result and result.message))
`)
test('audit records sends, rets and their order',`
 reset_spend()
 network.event_listener.query_arena_rival_ret=function(msg) end
 A:install_task_hooks()
 A:install_audit_hook()
 network:Send({query_arena_rival={}})
 respond('query_arena_rival_ret',{result='success'})
 local tail=A:audit_tail(0)
 assert(#tail==2,'must have one send + one ret, got '..#tail)
 assert(tail[1].dir=='send' and tail[1].name=='query_arena_rival','first entry must be the send')
 assert(tail[2].dir=='ret' and tail[2].result=='success','second entry must be the ret')
 local incremental=A:audit_tail(tail[2].seq)
 assert(#incremental==0,'audit_tail(since) must return only newer entries')
`)
test('compat report passes on a healthy env and warns on missing fields',`
 reset_spend()
 -- 测试环境补齐真实客户端已有、但桩环境缺失的接口（对照游戏源码）。
 function mining:SolveCaveEvent(event_data) return true end
 function mining:BuyCaveChallengeCounts(cave_type) end
 function troop:VanityExchangeReward(good_id,num) end
 local report=A:compat_report()
 assert(report.status=='ok','healthy env must pass: '..tostring(report.failures and report.failures[1] and report.failures[1].reason))
 local saved=constants.CAVE_DAILY_BUY_PRICE
 constants.CAVE_DAILY_BUY_PRICE=nil
 report=A:compat_report()
 assert(report.status=='warning','missing price table must warn')
 assert(report.failures[1] and report.failures[1].name=='constants.CAVE_DAILY_BUY_PRICE',
  'must name the failed check: '..tostring(report.failures[1] and report.failures[1].name))
 constants.CAVE_DAILY_BUY_PRICE=saved
 report=A:compat_report()
 assert(report.status=='ok','restoring the field must pass again')
`)
test('workshop production respects the tnt reserve line',`
 reset_workshop()
 A.cfg.reserveTnt=150
 A:start_workshop('produce')
 A:workshop_step();respond('query_mining_info_ret',{})
 local result=A:workshop_step()
 assert(result.done,'must stop with the reserve line: '..tostring(result.message))
 assert(tostring(result.message):find('雷管不足')~=nil,'must explain the tnt reserve: '..tostring(result.message))
`)
test('spend_reset_session keeps in-flight spends across the push round-trip',`
 reset_spend()
 -- 1. 消耗 40 → 快照 total=40 → C# 回推基数 40。
 A:spend_record(40,'测试')
 assert(A:spend_total()==40,'spend must register, got '..tostring(A:spend_total()))
 A:spend_reset_session(40)
 assert(A:spend_total()==40,'confirmed: base 40 + session 0, got '..tostring(A:spend_total()))
 -- 2. 再消耗 20 → 快照 total=60 → 回推基数 60。
 A:spend_record(20,'测试')
 assert(A:spend_total()==60,'total must be 60, got '..tostring(A:spend_total()))
 A:spend_reset_session(60)
 assert(A:spend_total()==60,'confirmed: base 60 + session 0, got '..tostring(A:spend_total()))
 -- 3. 回推间隙又消耗 20（C# 快照读到的还是 60）→ 回推 60 不得丢掉这 20。
 A:spend_record(20,'测试')
 assert(A:spend_total()==80,'total with in-flight must be 80, got '..tostring(A:spend_total()))
 A:spend_reset_session(60)
 assert(A:spend_total()==80,'in-flight 20 must survive, got '..tostring(A:spend_total()))
`)
};
