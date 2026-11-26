module.exports=({run,test})=>{
run(`
function transport130()
 setup_119();A:cancel_task();A._cancelled_task_pending=nil
 T130={send=network.Send,connected=network.IsConnected,session=network.session,internal=network.internal_session,user=package.loaded['logic.user'],trace={},fail=0,attempts=0}
 package.loaded['logic.user']={query_queue={}}
 network.session=0;network.internal_session=0;network.IsConnected=function()return true end
 network.Send=function(self,msg)
  T130.attempts=T130.attempts+1
  if T130.fail>0 then T130.fail=T130.fail-1;return false end
  assert(self.session>=self.internal_session,'transport sent while native session was busy')
  T130.trace[#T130.trace+1]=clone(msg);self.internal_session=self.internal_session+1
 end
 network.event_listener.query_arena_rival_ret=function()end
end
function restore130()
 A:cancel_task();A._cancelled_task_pending=nil
 network.Send=T130.send;network.IsConnected=T130.connected;network.session=T130.session;network.internal_session=T130.internal
 package.loaded['logic.user']=T130.user
end
function begin130(key,event,payload)
 return A:task_request(event or key..'_ret','test130',function()return network:Send({[key]=payload or {}})end)
end
`);
test('native busy sessions queue a request and reset its timeout only after the real send',`
 transport130();network.internal_session=1
 local p=begin130('query_arena_rival');assert(p.queued and T130.attempts==0)
 A.now=50;assert(A:request_retry_gate('test').wait and T130.attempts==0)
 network.session=1;A.now=51;A:request_retry_gate('test')
 assert(not p.queued and T130.attempts==1 and p.sent_at==51)
 A.now=52;A:request_retry_gate('test');assert(T130.attempts==1)
 restore130()
`);
test('assistant requests never overtake the native serial login query queue',`
 transport130();package.loaded['logic.user'].query_queue={{query_guild={}}}
 local p=begin130('query_arena_rival');A.now=5;A:request_retry_gate('test')
 assert(p.queued and T130.attempts==0 and #package.loaded['logic.user'].query_queue==1)
 package.loaded['logic.user'].query_queue={};A.now=6;A:request_retry_gate('test')
 assert(not p.queued and T130.attempts==1)
 restore130()
`);
test('more than two local rejections are retained until the native sender accepts',`
 transport130();T130.fail=7;local p=begin130('query_arena_rival')
 for i=1,20 do A.now=i;A:request_retry_gate('test');if not p.queued then break end end
 assert(not p.queued and T130.attempts==8 and #T130.trace==1)
 restore130()
`);
test('cancelling an unsent queued request cannot send it later or create a false mutation barrier',`
 transport130();network.internal_session=1
 local p=begin130('send_gift',nil,{user_id=1});assert(p.queued)
 A:cancel_task();network.session=1;A.now=100
 assert(not A._cancelled_task_pending and not A:request_retry_gate('test') and T130.attempts==0)
 restore130()
`);
test('reinstalling audit and spend checks a thousand times keeps one transport wrapper',`
 transport130();A:install_audit_hook();local fn=network.Send
 for i=1,1000 do A:install_audit_hook();A:install_daily_spend_guard()end
 assert(network.Send==fn)
 local seq=A.audit_seq or 0;network:Send({query_arena_rival={}})
 assert(T130.attempts==1 and (A.audit_seq or 0)==seq+1)
 restore130()
`);
test('eight explicit rejected replies remain active and are not counted as success',`
 transport130();local p=begin130('query_arena_rival')
 for i=1,8 do network.session=network.internal_session;p.response={result='failure'};A.now=i*20;local r=A:request_retry_gate('test');assert(r.wait and not r.done)end
 assert(#T130.trace==9)
 p.response={result='success'};network.session=network.internal_session
 assert(A:request_retry_gate('test')==nil)
 restore130()
`);
test('an uncertain guild attack is resolved by fresh tickets without repeating the attack',`
 transport130();resource_counts[51]=10
 local p=begin130('guild_boss_challenge',nil,{boss_id=3});network.session=network.internal_session
 A.now=25;assert(A:request_retry_gate('test').wait and A._task_pending.command=='query_resource_list')
 resource_counts[51]=9;A._task_pending.response={result='success'}
 A:request_retry_gate('test');assert(A._task_pending==p and p.response.result=='success' and p.reconciled)
 local attacks=0;for _,m in ipairs(T130.trace)do if m.guild_boss_challenge then attacks=attacks+1 end end
 assert(attacks==1);restore130()
`);
test('an unapplied uncertain guild attack is retried only after an authoritative unchanged read',`
 transport130();resource_counts[51]=10
 local p=begin130('guild_boss_challenge',nil,{boss_id=3});network.session=network.internal_session
 A.now=25;A:request_retry_gate('test');network.session=network.internal_session;A._task_pending.response={result='success'}
 A:request_retry_gate('test');assert(A._task_pending==p and not p.response)
 A.now=28;A:request_retry_gate('test')
 local attacks=0;for _,m in ipairs(T130.trace)do if m.guild_boss_challenge then attacks=attacks+1 end end
 assert(attacks==2);restore130()
`);
test('manual cancellation during reconciliation retains the original uncertain mutation',`
 transport130();resource_counts[51]=10
 local p=begin130('guild_boss_challenge',nil,{boss_id=3});network.session=network.internal_session
 A.now=25;A:request_retry_gate('test');assert(A._request_probe)
 A:cancel_task();assert(A._cancelled_task_pending==p and not A._request_probe)
 restore130()
`);
test('literal cave failure is retried more than six times and other projects still finish once',`
 caves127({3,4,5},1,0);troop.GetTroopBP=function()return 10000000 end;local old=network.event_listener.solve_cave_event_ret;local failed=0
 network.event_listener.solve_cave_event_ret=function(msg)
  if msg.cave_type==3 and failed<6 then failed=failed+1;msg.result='failure' else old(msg)end
 end
 A:install_task_hooks();local r=run_119(1500);local buys,fights=cave127_counts()
 assert(r.done and failed==6 and fights[3]==7 and fights[4]==1 and fights[5]==1, 'done='..tostring(r.done)..' failures='..failed..' fights3='..tostring(fights[3])..' fights4='..tostring(fights[4])..' msg='..tostring(r.message))
`);
test('game-unaccepted cave sends run after other projects and continue until the seventh acceptance',`
 caves127({3,4,5},1,0);troop.GetTroopBP=function()return 10000000 end;local send=network.Send;local failed=0
 network.Send=function(self,msg)
  if msg.solve_cave_event and msg.solve_cave_event.cave_type==3 and failed<6 then failed=failed+1;return false end
  return send(self,msg)
 end
 local r=run_119(1800);network.Send=send;local buys,fights=cave127_counts()
 assert(r.done and failed==6 and fights[3]==1 and fights[4]==1 and fights[5]==1)
`);
test('a market native callback exception cannot erase a query response or skip selected exchanges',`
 daily128_fixture({order128(1,3,7,1),order128(2,4,8,1),order128(3,5,9,1)})
 network.event_listener.query_merchant_ret=function()error('native panel not initialized')end
 A:install_task_hooks();local r=run128()
 assert(r.done and count_event('merchant_exchange')==3 and M128_BONUS)
`);
test('Bonus retries after five rejections without exchanging completed mineral orders again',`
 daily128_fixture({order128(1,3,7,1),order128(2,4,8,1),order128(3,5,9,1)})
 local failures=0;local r
 for tick=1,1500 do
  A.now=tick;clock=1000+tick;r=A:daily_step();local p=A._task_pending
  if p and p.event=='merchant_soul_chip_ret' and not p.response and failures<5 then
   failures=failures+1;respond(p.event,{result='failure'})
  else service128()end
  if r.done then break end
 end
 assert(r.done and failures==5 and count_event('merchant_exchange')==3 and count_event('merchant_soul_chip')==6 and M128_BONUS)
`);
test('authentication messages are redacted before the audit buffer is written',`
 transport130();A:install_audit_hook();local seq=A.audit_seq or 0
 network:Send({login={user='fixture',password='fixture-secret',reconnect_token='fixture-token'}})
 local rows=A:audit_tail(seq);assert(#rows==1 and rows[1].data=='[redacted]')
 restore130()
`);test('stale native rune animation flags recover through a fresh bag and complete loadout snapshot',`
 setup_r124(0);RN.pool={rtarget(1,1,0,0),rfood(2),rfood(3)};RN.pool[3].locked=true;RN.loadouts[1].slots[1]=1
 local native=rune.QueryRuneLoadouts
 rune.QueryRuneLoadouts=function(self)if self.is_drawing or self.is_moving then return false end;return native(self)end
 rune.is_drawing=true;rune.is_moving=true;rune.rune_native_pending=1
 A:start_rune();local recovery=false
 for i=1,180 do
  A.now=i*.1;clock=1000+A.now;local r=A:rune_step()
  local p=A._task_pending;if p and p.kind=='rune_recovery_bag' then recovery=true end
  if A.now<10 then assert(RN.native_upgrades==nil or RN.native_upgrades==0)end
  service_r124()
  if r.done then break end
 end
 assert(recovery and RN.native_upgrades==1 and RN.upgrades==1 and not rune.is_drawing and not rune.is_moving)
 local protected=false;for _,row in ipairs(RN.pool)do if row.rune_id==3 then protected=true end end
 assert(protected and RN.draws==0)
`);

};
