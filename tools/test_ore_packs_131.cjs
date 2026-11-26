module.exports=({run,test})=>{
run(`
 ORE_SAVED_HIDE=A.hide_panel
 function ore_fixture(a,b,c)
  A:cancel_task();A._cancelled_task_pending=nil;A._request_probe=nil;A.daily_state=nil;A.now=0
  network.session=nil;network.internal_session=nil;network.IsConnected=function()return true end
  package.loaded['logic.user']={query_queue={}}
  OP={[25]=a or 0,[26]=b or 0,[27]=c or 0,[29]=99};OP_INDEX=1;OP_HIDDEN=0;OP_SHOWN=0;OP_REWARD=0
  resource_logic.GetResourceNum=function(self,id)return OP[id] or 0 end
  sent={};network.Send=function(self,m)sent[#sent+1]=m;return true end
  local g=package.loaded['logic.graphic'];g.DispatchEvent=function(self,name,panel)if name=='show_world_sub_panel' then OP_SHOWN=OP_SHOWN+1 end end
  package.loaded['logic.reward']={ResetRewardInfoList=function()end}
  A.hide_panel=function(self,name)if name=='reward_panel' then OP_HIDDEN=OP_HIDDEN+1 end end
  network.event_listener.query_resource_list_ret=function()end
  network.event_listener.use_mining_tool_ret=function(msg)
   if msg.result=='success' then OP[msg.resource_id]=OP[msg.resource_id]-msg.num;OP_REWARD=OP_REWARD+msg.num
    g:DispatchEvent('show_world_sub_panel','reward_panel') end
  end
  A._task_hooks={};A:install_task_hooks()
 end
 function ore_query_reply()
  respond('query_resource_list_ret',{junior_tool=OP[25],intermediate_tool=OP[26],senior_tool=OP[27]})
 end
 function ore_service_all()
  local guard=0
  while OP_INDEX<=#sent do
   guard=guard+1;assert(guard<10000,'unexpected request loop')
   local m=sent[OP_INDEX];OP_INDEX=OP_INDEX+1
   if m.query_resource_list then ore_query_reply()
   elseif m.use_mining_tool then
    local v=m.use_mining_tool;assert(v.resource_id>=25 and v.resource_id<=27 and v.num>=1 and v.num<=5)
    assert(OP[v.resource_id]>=v.num)
    respond('use_mining_tool_ret',{result='success',resource_id=v.resource_id,num=v.num})
   else error('unrelated mutation') end
  end
 end
 function ore_uses()
  local n=0;for _,m in ipairs(sent)do if m.use_mining_tool then n=n+1 end end;return n
 end
`);
test('all three ore pack tiers use five-pack ACK chaining and close rewards without waiting for host steps',`
 ore_fixture(11,7,2);A:start_orepacks();A:orepacks_step();ore_service_all()
 assert(OP[25]==0 and OP[26]==0 and OP[27]==0 and OP[29]==99 and OP_REWARD==20)
 assert(ore_uses()==6 and #sent==8 and OP_SHOWN==0 and OP_HIDDEN>0)
 A.now=1;local r=A:orepacks_step();assert(r.done and r.used==20)
 package.loaded['logic.graphic']:DispatchEvent('show_world_sub_panel','reward_panel');assert(OP_SHOWN==1,'normal rewards must resume after completion')
`);
test('large ore inventory has no per-task item cutoff and only two normal inventory reads',`
 ore_fixture(1005,201,2);A:start_orepacks();A:orepacks_step();ore_service_all();A.now=1
 local r=A:orepacks_step();assert(r.done and r.used==1208 and ore_uses()==243 and #sent==245)
`);
test('empty ore stock finishes without using pickaxe bags or buying resources',`
 ore_fixture(0,0,0);A:start_orepacks();A:orepacks_step();ore_service_all();A.now=1
 assert(A:orepacks_step().done and ore_uses()==0 and #sent==1 and OP[29]==99)
`);
test('cancelling a pending ore batch preserves its confirmation barrier and stops chaining',`
 ore_fixture(20,5,5);A:start_orepacks();A:orepacks_step();ore_query_reply()
 assert(ore_uses()==1);A:cancel_task();assert(not A.orepacks_state and A._cancelled_task_pending)
 respond('use_mining_tool_ret',{result='success',resource_id=25,num=5})
 assert(ore_uses()==1 and OP[25]==15 and not A._cancelled_task_pending)
`);
test('an unrelated tool acknowledgement cannot advance the ore task',`
 ore_fixture(8,0,0);A:start_orepacks();A:orepacks_step();ore_query_reply()
 respond('use_mining_tool_ret',{result='success',resource_id=29,num=1})
 assert(A._task_pending and not A._task_pending.response and A.orepacks_state.used==0 and ore_uses()==1)
`);
test('a lost ore ACK is reconciled through actual ore inventory rather than the pickaxe counter',`
 ore_fixture(8,0,0);A:start_orepacks();A:orepacks_step();ore_query_reply();OP_INDEX=#sent+1
 OP[25]=3;OP_REWARD=5;A.now=21;A:orepacks_step();assert(sent[#sent].query_resource_list)
 OP_INDEX=#sent+1;ore_query_reply();A.now=22;A:orepacks_step()
 assert(sent[#sent].use_mining_tool and sent[#sent].use_mining_tool.num==3)
 ore_service_all();A.now=23;local r=A:orepacks_step();assert(r.done and r.used==8 and ore_uses()==2)
`);
test('an incomplete inventory response causes a read retry and never a guessed open request',`
 ore_fixture(5,5,5);A:start_orepacks();A:orepacks_step()
 respond('query_resource_list_ret',{junior_tool=5});assert(ore_uses()==0)
 A.now=0.5;A:orepacks_step();assert(#sent==1)
 A.now=1.1;A:orepacks_step();assert(#sent==2 and sent[2].query_resource_list)
`);
test('explicit ore rejection retries the same batch without crediting any unopened packs',`
 ore_fixture(5,0,0);A:start_orepacks();A:orepacks_step();ore_query_reply()
 respond('use_mining_tool_ret',{result='failure',resource_id=25,num=5});A:orepacks_step()
 assert(ore_uses()==2 and A.orepacks_state.used==0 and OP[25]==5)
 OP_INDEX=#sent;ore_service_all();A.now=1;assert(A:orepacks_step().done and OP[25]==0 and OP_REWARD==5)
`);
run(`A:cancel_task();A._cancelled_task_pending=nil;A.hide_panel=ORE_SAVED_HIDE`);
};
