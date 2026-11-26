module.exports=({run,test})=>{
run('D129_LIMITS=constants.CAVE_DAILY_BUY_CHALLENGE_NUM;D129_PRICES=constants.CAVE_DAILY_BUY_PRICE');
test('cached unread mail is claimed even when the server query contains no new mail',`
 setup_119();A.cfg.dailyMail=true
 mail_state.not_read_list={{id=501,mark_read=false},{id=502,mark_read=false}}
 mail_state.system_mail_not_read_list={{id=501,mark_read=false}}
 start_119();local result=run_119();assert(result.done and not result.error)
 assert(count_event('open_mail')==2 and A.daily_state.mail_claimed==2)
`);
test('completed rewards are collected even when their task execution switches are off',`
 setup_119();A.cfg.dailyRewards=true;start_119()
 server_counts[3]=daily_conf_for_test[3].condition_count
 daily.liveness_list={{liveness_id=3,completion_count=server_counts[3],is_reward=false}}
 local result=run_119();assert(result.done and not result.error)
 assert(server_claimed[3] and server_milestone and count_event('get_liveness_reward')==1)
 assert(count_event('send_gift')==0)
`);
test('mail and liveness claims follow all gold summons and occur before window cleanup',`
 setup_119();A.cfg.dailyMail=true;A.cfg.dailyRewards=true;A.cfg.dailyGoldSummon=true
 mail_server={{id=601}};start_119();local result=run_119();assert(result.done)
 local lastGold,firstMail,firstReward=0,10000,10000
 for i,m in ipairs(sent)do
  if m.recruit_mercenary then lastGold=i end
  if m.open_mail then firstMail=math.min(firstMail,i)end
  if m.get_activity_reward or m.get_liveness_reward then firstReward=math.min(firstReward,i)end
 end
 assert(lastGold>0 and firstMail>lastGold and firstReward>firstMail)
`);
test('a cave command that acknowledges without executing is recovered in the final sweep',`
 caves127({3,4,5},1,0)
 local old=network.event_listener.solve_cave_event_ret;local missed=false
 network.event_listener.solve_cave_event_ret=function(msg)
  if msg.cave_type==3 and not missed then missed=true else old(msg)end
 end
 A:install_task_hooks();local result=run_119(800);assert(result.done and not result.error)
 local buys,fights=cave127_counts();assert(fights[3]==2 and fights[4]==1 and fights[5]==1)
 assert(mining.cave_challenge_nums[3]==0)
`);
test('a dropped cave response does not stop other projects and is reconciled before resending',`
 caves127({3,4,5},1,0);local dropped=false;local result
 for tick=1,800 do
  A.now=tick;clock=1000+tick;result=A:daily_step()
  local p=A._task_pending
  if p and p.cave_kind==3 and not dropped then dropped=p end
  if p~=dropped then service_daily_request()end
  if result.done then break end
 end
 assert(result.done and not result.error)
 local buys,fights=cave127_counts();assert(fights[3]==2 and fights[4]==1 and fights[5]==1)
 assert(mining.cave_challenge_nums[3]==0)
`);
test('an applied cave command with a missing response is not challenged twice',`
 caves127({3,4,5},1,0);local dropped=false;local result
 for tick=1,800 do
  A.now=tick;clock=1000+tick;result=A:daily_step();local p=A._task_pending
  if p and p.cave_kind==3 and not dropped then
   dropped=p;mining.cave_challenge_nums[3]=0
  end
  if p~=dropped then service_daily_request()end
  if result.done then break end
 end
 local buys,fights=cave127_counts();assert(result.done and fights[3]==1 and fights[4]==1 and fights[5]==1)
`);
test('an applied cave purchase with a missing response cannot purchase the same attempt again',`
 caves127({3,4,5},0,1);local dropped=false;local result
 for tick=1,800 do
  A.now=tick;clock=1000+tick;result=A:daily_step();local p=A._task_pending
  if p and p.cave_buy_kind==3 and not dropped then
   dropped=p;mining.cave_challenge_nums[3]=1;mining.cave_buy_challenge_nums[3]=0
  end
  if p~=dropped then service_daily_request()end
  if result.done then break end
 end
 local buys,fights=cave127_counts();assert(result.done and buys[3]==1 and fights[3]==1)
`);
test('a dropped cave purchase is bought in the sweep after a fresh remaining-quota query',`
 caves127({3},0,1);local dropped=false;local result
 for tick=1,800 do
  A.now=tick;clock=1000+tick;result=A:daily_step();local p=A._task_pending
  if p and p.cave_buy_kind==3 and not dropped then dropped=p end
  if p~=dropped then service_daily_request()end
  if result.done then break end
 end
 local buys,fights=cave127_counts();assert(result.done and buys[3]==2 and fights[3]==1)
 assert(mining.cave_buy_challenge_nums[3]==0)
`);
test('wrong-project cave responses cannot settle an outstanding challenge',`
 caves127({3},1,0);A:daily_action(9,nil,nil);assert(A._task_pending.cave_kind==3)
 respond('solve_cave_event_ret',{result='success',cave_type=4,cave_lv=2})
 assert(not A._task_pending.response)
`);
test('missing authoritative cave quotas cannot authorize replay of an uncertain purchase',`
 caves127({3},0,1);A.daily_state.cave_uncertain={[3]={cave_buy_kind=3}}
 A.daily_state.completion_phase='caves';A.daily_state.completion_result={done=true,message='test'}
 A:daily_tail_caves(A.daily_state);respond('query_mining_info_ret',{result='success'})
 A:daily_tail_caves(A.daily_state)
 assert(A.daily_state.cave_skips[3] and A.daily_state.cave_buy_stops[3])
 assert(count_event('buy_cave_challenge')==0)
`);
test('a locally rejected cave command cannot block the other projects in the final sweep',`
 caves127({3,4,5},1,0);local send=network.Send
 network.Send=function(self,msg)if msg.solve_cave_event and msg.solve_cave_event.cave_type==3 then return false end;return send(self,msg)end
 A.daily_state.completion_phase='caves';A.daily_state.completion_result={done=true,message='test'}
 local result=run_119(500);network.Send=send
 assert(result.done and result.partial and A.daily_state.retry_round==8,'unaccepted commands must remain unconfirmed after bounded retries')
 local buys,fights=cave127_counts();assert(not fights[3] and fights[4]==1 and fights[5]==1)
`);
test('completion cave work waits for a visible battle when skipping is disabled',`
 caves127({3},1,0);A.cfg.autoSkipBattle=false;A.inBattle=true
 A.daily_state.completion_phase='caves';A.daily_state.completion_result={done=true,message='test'}
 local result=A:daily_step();assert(result.wait and #sent==0)
 A.cfg.autoSkipBattle=true;A.inBattle=false
`);
run('constants.CAVE_DAILY_BUY_CHALLENGE_NUM=D129_LIMITS;constants.CAVE_DAILY_BUY_PRICE=D129_PRICES');
};
