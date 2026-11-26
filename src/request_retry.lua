-- One transport owner: the native network permits only one unacknowledged
-- session. A false return is not an accepted operation. Keep unsent commands
-- until the native session is free, without wrapping Send on every daily run.
A.REQUEST_EVENTS={"query_troop_info_ret"}
local function request_copy(value)
    if type(value)~="table" then return value end
    local out={};for k,v in pairs(value)do if k~="session" then out[k]=request_copy(v) end end;return out
end
local function request_module(name)
    local ok,m=pcall(require,name);return ok and type(m)=="table" and m or {}
end
local function request_signature(value)
    if type(value)~="table" then return tostring(value) end
    local keys={};for k in pairs(value)do keys[#keys+1]=k end
    table.sort(keys,function(a,b)return tostring(a)<tostring(b)end)
    local out={};for _,k in ipairs(keys)do out[#out+1]=tostring(k).."="..request_signature(value[k]) end
    return "{"..table.concat(out,";").."}"
end
local function request_query(key,read)
    return {key=key,event=key.."_ret",read=read}
end
-- A timeout is reconciled with a fresh domain read before a mutation is sent
-- again. A synthetic success below only resumes the task; it never invokes a
-- native resource/battle callback a second time.
function A:request_proof(key,v)
    v=v or {}
    if self.push_state and (key=="solve_adventure_event" or key=="enter_adventure_maze" or key=="open_box") then
        local st=self.push_state;local target=v.maze_id or st.claim
        if key=="solve_adventure_event" then for id,conf in pairs(config_manager.adventure_maze_config or {})do if conf.event_id==v.event_id then target=id;break end end end
        return request_query("query_adventure_maze_list",function(msg)
            local adv=request_module("logic.adventure");local row=(st.mazes or {})[target] or (adv.maze_list or {})[target]
            local current=st.cur or adv.cur_maze_id;local fails=(st.fails or {})[v.event_id] or 0
            if msg then
                if type(msg.maze_list)~="table" then return nil end
                row=nil;for _,r in pairs(msg.maze_list)do if tonumber(r.id)==tonumber(target)then row=r;break end end
                current=msg.cur_maze_id;fails=0;for _,r in pairs(msg.event_fail_time_list or {})do if r.id==v.event_id then fails=r.fail_time end end
            end
            if not row then return nil end
            return {done=row.event_is_finish==true,current=current,fail=fails,boxes=tonumber(row.box_num) or 0,target=target}
        end)
    elseif key=="use_mining_tool" and tonumber(v.resource_id) and v.resource_id>=25 and v.resource_id<=27 then
        return request_query("query_resource_list",function(msg)
            local count=self:ore_pack_count(v.resource_id,msg)
            return count and {count=count} or nil
        end)
    elseif key=="buy_cave_challenge" or key=="solve_cave_event" then
        local kind=v.cave_type
        return request_query("query_mining_info",function(msg)
            local counts=msg and msg.cave_challenge_nums or mining.cave_challenge_nums
            local buys=msg and msg.cave_buy_challenge_nums or mining.cave_buy_challenge_nums
            if type(counts)~="table" or tonumber(counts[kind])==nil then return nil end
            if key=="buy_cave_challenge" and (type(buys)~="table" or tonumber(buys[kind])==nil) then return nil end
            return {count=counts[kind],buy=(buys or {})[kind],dig=msg and msg.dig_count or mining.dig_count}
        end)
    elseif key=="merchant_exchange" or key=="merchant_soul_chip" then
        return request_query("query_merchant",function(msg)
            if not msg then
                local m=request_module("logic.merchant")
                return {done=key=="merchant_soul_chip" and m.has_collected_soul_chip or false}
            end
            if type(msg.order_list)~="table" then return nil end
            if key=="merchant_soul_chip" then return {done=msg.ext_soul_chip_flag==true} end
            for _,o in ipairs(msg.order_list)do if tonumber(o.order_id)==tonumber(v.order_id) then return {done=o.is_done==true} end end
            return {stale=true}
        end)
    elseif key=="use_mining_tool" or key=="add_mining_project" or key=="get_mining_project_reward" or key=="unlock_cave_boss" or key=="challenge_cave_boss" then
        return request_query("query_mining_info",function(msg)
            local m=msg or mining
            if key=="use_mining_tool" then return m.dig_count and {dig=m.dig_count} end
            if key=="unlock_cave_boss" then return m.cave_boss_lv and {level=m.cave_boss_lv} end
            if key=="challenge_cave_boss" then return m.cave_boss_lv and {level=m.cave_boss_lv,dig=m.dig_count} end
            if type(m.project_list)~="table" then return nil end
            return {projects=request_signature(m.project_list)}
        end)
    elseif key=="get_liveness_reward" or key=="get_activity_reward" then
        return request_query("query_liveness_info",function(msg)
            local d=msg or request_module("logic.daily")
            if key=="get_liveness_reward" then
                if type(d.liveness_list)~="table" then return nil end
                for _,row in pairs(d.liveness_list)do if tonumber(row.liveness_id)==tonumber(v.liveness_id)then return {done=row.is_reward==true} end end
                return nil
            end
            local list=msg and msg.receive_list or d.activity_receive_list
            if type(list)~="table" then return nil end
            for _,index in pairs(list)do if tonumber(index)==tonumber(v.index)then return {done=false} end end
            return {done=true}
        end)
    elseif key=="open_mail" then
        return request_query("query_mail_info",function(msg)
            local m=request_module("logic.mail")
            for _,list in ipairs({msg and msg.mail_list or {},m.not_read_list or {},m.already_read_list or {}})do
                for _,row in pairs(list)do if tostring(row.id)==tostring(v.id)then return {done=row.mark_read==true}end end
            end
            -- Mail claims are idempotent on the server even if a delta-only
            -- mailbox response no longer includes a claimed message.
            return msg and {done=false} or nil
        end)
    elseif key=="vanity_buy_recruit" or key=="vanity_sync_troop" then
        return request_query("query_vanity_troop",function()
            local t=request_module("logic.troop")
            if key=="vanity_buy_recruit" then return tonumber(t.reduce_search_times) and {left=t.reduce_search_times} end
            return type(t.vanity_troop)=="table" and {team=request_signature(t.vanity_troop)} or nil
        end)
    elseif key=="vanity_challenge" or key=="get_vanity_mercenary" then
        return request_query("query_vanity_maze_states",function(msg)
            local list=msg and (msg.states or msg.maze_states) or request_module("logic.troop").vanity_maze_state_list
            local state=list and list[v.maze_id];return state and {state=state} or nil
        end)
    elseif key=="vanity_exchange_goods" then
        return request_query("query_vanity_goods",function(msg)
            local list=msg and msg.good_list or request_module("logic.troop").vanity_good_list
            for _,row in pairs(list or {})do if tonumber(row.good_id)==tonumber(v.good_id)then return {good=request_signature(row)}end end
            return nil
        end)
    elseif key=="add_material" then
        local state=self.daily_state
        return request_query("query_resource_recycle",function(msg)
            local temperature=msg and tonumber(msg.temperature) or state and tonumber(state.refine_temperature)
            local process=msg and tonumber(msg.process) or state and tonumber(state.refine_process)
            return temperature and process and {temperature=temperature,process=process} or nil
        end)
    elseif key=="mine_start" or key=="mine_receive_reward" then
        return request_query("query_mine_info",function(msg)
            local list=msg and msg.mine_info_list or request_module("logic.mine").mine_info_list
            for _,row in pairs(list or {})do if tonumber(row.mine_index)==tonumber(v.mine_index) then
                return {state=row.status,level=row.mine_level,end_time=row.end_time}
            end end
            return nil
        end)
    elseif key=="cultivate" then
        return request_query("query_cultivation_info",function()
            local list=request_module("logic.cultivation").cultivation_info
            local row=list and list[v.cultivation_type]
            local level=row and tonumber(row[v.coefficient_type]);return level and {level=level} or nil
        end)
    elseif key=="challenge_ladder" then
        return request_query("query_ladder_info",function()
            local l=request_module("logic.ladder");return tonumber(l.challenge_num) and {count=l.challenge_num} or nil
        end)
    elseif key=="escort_refresh_rob_target_list" then
        return request_query("query_rob_target_list",function(msg)
            if not msg then return {done=false} end
            if not msg or type(msg.rob_target_list)~="table" then return nil end
            -- Do not blindly replay an uncertain paid refresh. Re-select from a
            -- fresh authoritative list; only refresh again if it has no candidates.
            return {done=true}
        end)
    elseif key=="escort_buy_escort_times" then
        return request_query("query_escort_times",function(msg)
            local t=msg and msg.escort_times or request_module("logic.escort").escort_times
            return t and tonumber(t.buy_escort) and tonumber(t.remain_escort) and {buy=t.buy_escort,count=t.remain_escort} or nil
        end)
    elseif key=="escort_refresh_tramcar" and v.refresh_type~="random" then
        return request_query("query_escort_info",function(msg)
            local e=msg and msg.escort_info or request_module("logic.escort").escort_info
            if not e or not tonumber(e.tramcar_id) or v.refresh_type=="auto" and not tonumber(e.auto_refresh_tramcar) then return nil end
            return {level=e.tramcar_id,auto=e.auto_refresh_tramcar,
                done=v.refresh_type=="specify" and tonumber(e.tramcar_id)==(tonumber(constants.SPECITY_TRAMCAR_ID) or 4) or
                    v.refresh_type=="auto" and tonumber(e.tramcar_id)>0 and tonumber(e.auto_refresh_tramcar)~=(tonumber((constants.ESCORT_AUTO_REFRESH_TRAMCAR or {}).TRUE) or 1)}
        end)
    elseif key=="escort_start" or key=="escort_receive_reward" then
        return request_query("query_escort_info",function()
            local e=request_module("logic.escort").escort_info
            return e and tonumber(e.status) and {state=e.status,beg=e.escort_beg_time,finish=e.escort_end_time,auto=e.auto_refresh_tramcar} or nil
        end)
    elseif key=="escort_rob_target" or key=="escort_refresh_tramcar" then
        return request_query("query_escort_times",function()
            local t=request_module("logic.escort").escort_times
            if not t then return nil end
            return key=="escort_rob_target" and tonumber(t.remain_rob) and {count=t.remain_rob} or
                key=="escort_refresh_tramcar" and tonumber(t.refresh_tramcar) and {refresh=t.refresh_tramcar} or nil
        end)
    elseif key=="upgrade_rune" or key=="draw_rune" or key=="rune_one_key_receive" then
        return request_query("query_rune_info",function()
            local r=request_module("logic.rune")
            if key=="upgrade_rune" then
                for _,row in ipairs(r.rune_list or {})do if tonumber(row.rune_id)==tonumber(v.rune_id)then return {level=row.level,exp=row.exp}end end
                return nil
            end
            return type(r.rune_temporary_list)=="table" and {temporary=request_signature(r.rune_temporary_list)} or nil
        end)
    elseif key=="weapon_star_upgrade" then
        return request_query("query_weapon_star_info",function()
            local list=request_module("logic.destiny_weapon").weapon_star_info_list
            local row=list and list[v.weapon_id];return row and {star=request_signature(row)} or nil
        end)
    elseif key=="refresh_arena_rival" or key=="challenge_arena_rival" then
        return request_query("query_arena_rival",function(msg)
            local a=msg or request_module("logic.arena");local list=a.rival or a.rival_list
            if type(list)~="table" then return nil end
            local row=list[tonumber(v.rival_pos) or 0]
            return {rivals=request_signature(list),count=a.challenge_num,refresh=a.refresh_time,done=row and row.state==true}
        end)
    elseif key=="guild_boss_buy_ticket" or key=="guild_boss_challenge" then
        return request_query("query_resource_list",function()
            local id=(constants.RESOURCE_TYPE or {}).guild_boss_ticket or 51
            return {tickets=resource_logic:GetResourceNum(id)}
        end)
    elseif key=="recruit_mercenary" and v.door_type=="friendship_door" then
        return request_query("query_resource_list",function()
            local id=(constants.RESOURCE_TYPE or {}).friendship_pt
            return id and {points=resource_logic:GetResourceNum(id)} or nil
        end)
    elseif key=="take_daily_prayer" or key=="take_daily_alchemy" or key=="take_check_in_reward" or key=="recruit_mercenary" then
        return request_query("query_daily",function()
            local d=request_module("logic.daily")
            if key=="take_daily_prayer" then return d.prayer_info and {mark=request_signature(d.prayer_info)} end
            if key=="take_daily_alchemy" then return d.alchemy_info and {mark=request_signature(d.alchemy_info)} end
            if key=="recruit_mercenary" then
                local ok,cost=pcall(function()return d:GetRecruitCost()end);return ok and {cost=cost} or nil
            end
            local ok,done=pcall(function()return d:AlreadyCheckin()end);return ok and {done=done==true} or nil
        end)
    elseif key=="send_gift" then
        return request_query("query_social_info",function()
            local s=request_module("logic.social");local ok,list=pcall(function()return s:GetUnsendGiftList()end)
            if not ok then return nil end
            for _,id in pairs(list or {})do if tonumber(id)==tonumber(v.user_id)then return {done=false}end end
            return {done=true}
        end)
    elseif key=="alloc_mercenary_exp" or key=="forge_mercenary_weapon" then
        return request_query("query_troop_info",function()
            local t=request_module("logic.troop");local id=v.instance_id or v.mercenary_id
            local row=(t.mercenary_list or {})[id]
            return row and {level=row.level,exp=row.exp,weapon=row.weapon_lv} or nil
        end)
    end
end

local function request_channel_free()
    local user=package.loaded["logic.user"]
    if type(user)=="table" and type(user.query_queue)=="table" and #user.query_queue>0 then return false end
    if type(network.IsConnected)=="function" and not network:IsConnected() then return false end
    return not (tonumber(network.session) and tonumber(network.internal_session) and network.session<network.internal_session)
end
function A:request_channel_available()return request_channel_free()end
function A:install_request_pipeline()
    if network.Send==network._mwa_pipeline then return end
    network._mwa_pipeline_roots=network._mwa_pipeline_roots or {}
    local previous=network.Send
    while network._mwa_pipeline_roots[previous]do previous=network._mwa_pipeline_roots[previous]end
    local wrapped=function(net,message,...)
        if net._mwa_dispatch_message==message then return previous(net,message,...) end
        local a=__MWA;local p=a and a._task_pending
        local key,payload=next(message or {})
        local aliases={buy_campaign_overtime="campaign_overtime_ret",battle_pass_reward_receive="ret_battle_pass_reward_receive"}
        local daily=a and a.daily_state
        if key=="resource_recycle_click_finish" and daily and daily.refine_reward_active and (not p or p.refine_query) then
            p={kind="daily_refine",event="resource_recycle_click_finish_ret",sent_at=task_now(a),refine_reward=true}
            a._task_pending=p
        end
        local owned=p and p.kind~="mine_target_blast" and p.kind~="mine_target_blast_read" and (p.event==(aliases[key] or tostring(key).."_ret"))
        local fight=a and a.fight
        -- Native DigOrCollectBlock/SolveEvent discard Send's return value. Give
        -- their requests an owner before entering the shared single channel.
        if not owned and fight then
            local pos=payload and payload.position
            if key=="dig_block" and pos and pos.x==fight.x and pos.y==fight.y and fight.phase=="query" then
                fight.phase="solve";fight.event_id=mining.cur_event_id;fight.solved_at=a.now
                fight.transport={event="dig_block_ret",kind="mining_target",sent_at=a.now}
            end
            local fp=fight.transport
            local same=(key=="query_golem_info" and payload.x==fight.x and payload.y==fight.y)
                or (key=="dig_block" and pos and pos.x==fight.x and pos.y==fight.y)
            if same and fp and fp.event==tostring(key).."_ret" then p=fp;owned=true end
        end
        if owned and not p.message then
            p.message={[key]=request_copy(payload or {})};p.command=key
            p.proof=a:request_proof(key,payload);if p.proof then local ok,before=pcall(p.proof.read);if ok then p.proof_before=before end end
        end
        if a and a.audit_record then
            local private=tostring(key):find("login") or tostring(key):find("password") or tostring(key):find("token")
            a:audit_record({dir="send",name=tostring(key),data=private and "[redacted]" or audit_sprint(payload)})
        end
        if a and a.daily_state and a.daily_state.running then
            for name,value in pairs(message or {})do
                if name~="session" and not a:daily_message_is_free(name,value) then
                    if owned then p.blocked=true end
                    return false
                end
            end
        end
        if owned and not request_channel_free() then p._queued_at=p._queued_at or task_now(a);p.queued=true;p._transport_message=request_copy(message);return true end
        local previous_message=net._mwa_dispatch_message;net._mwa_dispatch_message=message
        local called,ok=pcall(previous,net,message,...);net._mwa_dispatch_message=previous_message
        if not called then error(ok) end
        if owned and ok==false then
            if type(network.IsConnected)=="function" and not network:IsConnected() then
                p.queued=false;p._sent=true;p.sent_at=task_now(a)-21;p.transport_uncertain=true
            else p._queued_at=p._queued_at or task_now(a);p.queued=true;p._transport_message=request_copy(message);p.locally_rejected=true end
            return true
        end
        if owned then p.queued=false;p.locally_rejected=nil;p.sent_at=task_now(a);p._sent=true end
        return ok
    end
    network._mwa_pipeline_roots[wrapped]=previous
    network.Send=wrapped;network._mwa_pipeline=wrapped
end
function A:install_audit_hook()return self:install_request_pipeline()end
function A:install_daily_spend_guard()return self:install_request_pipeline()end
local request_task_original=A.task_request
function A:task_request(event,kind,action)
    self:install_request_pipeline()
    return request_task_original(self,event,kind,function()
        local p=self._task_pending;p._capture_send=true
        local ok,value=pcall(action);p._capture_send=nil
        if not ok then error(value) end
        return value
    end)
end
local function request_backoff(p)
    p.retries=(p.retries or 0)+1
    return math.min(10,0.5*2^math.min(5,p.retries-1))
end
function A:request_retry_gate(owner)
    local p=self._task_pending;if not p or p._no_retry then return nil end
    local st=self.daily_state
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待战斗结算后继续请求队列",{wait=true}) end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待连接恢复后继续请求队列",{wait=true,delaySeconds=1}) end
    local now=task_now(self)
    if p.kind=="request_probe" and p.queued then
        if request_channel_free() then network:Send(request_copy(p._transport_message or p.message)) end
        return reply(self,"状态查询已排队，等待游戏通道空闲",{wait=true})
    end
    if p.kind=="request_probe" then
        local job=self._request_probe
        if not job then return nil end
        if p.response and (not p.response.result or p.response.result=="success" or p.response.result==0) then
            local after=job.source.proof.read(p.response)
            self._task_pending=job.source;self._request_probe=nil
            if after and job.source.proof_before then
                if after.stale then job.source.response={result="stale"};return nil end
                local before=job.source.proof_before;local command=job.source.command
                local applied=after.done==true or request_signature(after)~=request_signature(before)
                if command=="buy_cave_challenge" then applied=(tonumber(after.buy) or 0)<(tonumber(before.buy) or 0) or after.count>before.count
                elseif command=="solve_adventure_event" and self.push_state then applied=after.done or after.fail>before.fail
                elseif command=="enter_adventure_maze" then applied=after.current==after.target
                elseif command=="open_box" then applied=after.boxes<before.boxes
                elseif command=="solve_cave_event" then applied=after.count<before.count or (tonumber(after.dig) or 0)<(tonumber(before.dig) or 0)
                elseif command=="use_mining_tool" then
                    local amount=tonumber(job.source.message[command].num) or 1
                    if before.count~=nil then
                        applied=after.count<before.count
                    else
                        local expected=math.min(amount*5,math.max(0,(tonumber(mining.dig_max_count) or math.huge)-before.dig))
                        applied=after.dig-before.dig>=expected
                    end
                end
                if applied then
                    local msg=request_copy(job.source.message[job.source.command]);msg.result="success"
                    if command=="solve_adventure_event" and self.push_state and not after.done then msg.result="battle_failure" end
                    if command=="use_mining_tool" and before.count~=nil then msg.num=math.min(msg.num,before.count-after.count) end
                    if job.source.command=="solve_cave_event" then msg.is_winner=after.count<job.source.proof_before.count end
                    if command=="add_material" then msg.temperature=after.temperature;msg.process=after.process end
                    job.source.response=msg;job.source.reconciled=true
                    return nil
                end
                job.source.response=nil;job.source.sent_at=now;job.source.retry_at=now+request_backoff(job.source)
                job.source._resend_safe=true
            else
                job.source.retry_at=now+2;job.source._verify_again=true
            end
        elseif now-p.sent_at>8 then
            self._task_pending=job.source;self._request_probe=nil
            job.source.retry_at=now+2;job.source._verify_again=true
        else return reply(self,"同步游戏状态，确认上次请求是否已生效",{wait=true}) end
        p=self._task_pending
    end
    if p.blocked then
        if p.kind=="daily_cart" then p.queued=false;p.response={result=p.cart_blocked_result or "not_enough_resource"};p.blocked=nil end
        return nil
    end
    if p.queued and st and not tostring(p.command):match("^query_") and not p.transport_uncertain
        and (p.locally_rejected or now-(p._queued_at or now)>1)
        and (p.task_id or p.cave_kind or p.cave_buy_kind or st.child_events or st.child_mine or st.child_void_challenge or st.child_void or st.child_arena or p.extra) then
        p.queued=false;p.response={result="deferred"}
        if p.task_id then st.retry_ids=st.retry_ids or {};st.retry_ids[p.task_id]=true end
        if p.cave_kind or p.cave_buy_kind then self:log("游戏未接收副本操作，已排到其他事项之后补办") end
    end
    if p.queued then
        if p.refine_reward and now-(p._queued_at or now)>20 then
            p.queued=false;p.response={result="refine_timeout"};return nil
        end
        if now>=(p._transport_retry_at or 0) and request_channel_free() then
            p._transport_retry_at=now+0.1
            network:Send(request_copy(p._transport_message or p.message))
        end
        return reply(self,p.queued and "请求已排队，等待游戏接受" or "游戏已接收排队请求，等待确认",{wait=true})
    end
    if p.refine_reward then return nil end
    if (p.kind=="daily_cart" or p.kind=="daily_guard") and p.response then return nil end
    if p.kind=="daily_guard" and p.guard_target and not p.queued then return nil end
    if st and (p.cave_kind or p.cave_buy_kind or p.kind=="daily_market") then return nil end
    if st and st.completion_phase=="rewards" and not tostring(p.command):match("^query_") then return nil end
    local result=p.response and p.response.result
    if result==0 then result="success" elseif result~=nil then result=tostring(result) end
    if owner=="rune_step" and p.response then
        if p.kind=="growth_rune_upgrade" then return nil end
        if p.kind=="growth_rune_receive" and result and result:find("bag_full",1,true) then return nil end
        if p.kind=="growth_rune_draw" and result and result:find("bag_full",1,true) then return nil end
    end
    if not p.message and p.response then return nil end
    local accepted_battle=(result=="battle_failure" or p.response and p.response.is_winner~=nil)
    if p.response and (p.kind=="push_solve" or p.kind=="push_enter" or p.kind=="push_box") then return nil end
    if result and result~="success" and p.kind=="growth_void_buy" and (tonumber(p.num) or 1)>1 and self.void_exchange_state and not self.void_exchange_state.bulk_rejected then return nil end
    local terminal=result and (result:find("not_enough",1,true) or result=="bag_full" or result=="full_friendship_pt" or result=="already_received" or result=="already_done" or result=="error_maze_state")
    local rejected=result and result~="success" and not accepted_battle and result~="stale" and not terminal
    local timeout=not p.response and now-p.sent_at>math.max(8,tonumber(p.timeout) or 20)
    if timeout and (tostring(p.command):match("^query_") or tostring(p.command):match("^check_")) then p._resend_safe=true;timeout=false end
    if st and rejected and not tostring(p.command):match("^query_") then
        if st.child_arena then st.retry_ids=st.retry_ids or {};st.retry_ids[5]=true;st.skipped[5]="请求未被接收，队尾补办";return nil end
        local preparatory={get_mining_project_reward=2,escort_refresh_tramcar=7,escort_receive_reward=7,unlock_cave_boss=12}
        if preparatory[p.command] then local id=preparatory[p.command];st.retry_ids=st.retry_ids or {};st.retry_ids[id]=true;return nil end
        if p.command=="take_daily_prayer" or p.command=="take_daily_alchemy" then
            st.retry_flags=st.retry_flags or {};st.retry_flags[p.command]=true;return nil
        end
        local child=st.child_events and "events" or st.child_mine and "mine" or st.child_void_challenge and "void" or st.child_void and "exchange"
        if child then st.retry_children=st.retry_children or {};st.retry_children[child]=true;return nil end
        if st.completion_phase=="rewards" then return nil end
        if not st.completion_phase or st.completion_phase=="retry_items" then
            local extra=p.extra or (p.kind=="daily_refine" and "refine")
            if extra then st.retry_flags=st.retry_flags or {};st.retry_flags[extra]=true;return nil end
        end
    end
    if p.command=="guild_boss_challenge" and result=="failure" then return nil end
    if not rejected and not timeout and not p._resend_safe and not p._verify_again then return nil end
    if st and p.kind~="daily_cart" and (not st.completion_phase or st.completion_phase=="retry_items") and p.task_id and (rejected or timeout) then
        st.retry_ids=st.retry_ids or {};st.retry_ids[p.task_id]=true
        if timeout then p.response={result="deferred"} end
        return nil -- The regular pass skips it; the completion pass restores it.
    end
    if p.retry_at and now<p.retry_at then return reply(self,"未确认请求等待补发："..tostring(p.description or p.command),{wait=true,delaySeconds=math.min(1,p.retry_at-now)}) end
    if not p.message then return reply(self,"等待游戏补齐请求信息后继续",{wait=true,delaySeconds=1}) end
    if timeout or p._verify_again then
        p._verify_again=nil
        if not p.proof then
            -- Keep waiting for the original reply when a protocol has no
            -- authoritative read adapter; never guess a paid mutation failed.
            p.retry_at=now+2
            return reply(self,"正在等待原请求确认，保留队列（"..tostring(p.command).."）",{wait=true,delaySeconds=1})
        end
        self._request_probe={source=p}
        local probe=request_task_original(self,p.proof.event,"request_probe",function()return network:Send({[p.proof.key]={}})end)
        probe._no_retry=false
        return reply(self,"重新读取游戏状态后补办未确认操作",{wait=true})
    end
    p.response=nil;p._resend_safe=nil;p.retry_at=nil;p.sent_at=now
    network:Send(request_copy(p.message))
    p.retry_at=now+request_backoff(p)
    if now>=(p.retry_log_at or 0) then
        p.retry_log_at=now+5;self:log("请求暂未被接受，自动补发："..tostring(p.description or p.command))
    end
    return reply(self,"正在补发未被接受的请求",{wait=true})
end
-- Every public task controller shares this gate, including child tasks. Only
-- the outermost controller handles a response, so nesting cannot replay it.
for _,name in ipairs({"daily_step","escort_followup_step","arena_step","void_step","mine_step","workshop_step","guild_boss_step",
    "daily_guild_ticket_step","rune_step","destiny_step","void_exchange_step","mining_boss_step","daily_events_step","earth_heart_step","orepacks_step","push_step"})do
    local previous=A[name]
    if previous then A[name]=function(self,...)
        if self._request_gate_depth then return previous(self,...) end
        local wait=self:request_retry_gate(name);if wait then return wait end
        self._request_gate_depth=true
        local ok,result=pcall(previous,self,...);self._request_gate_depth=nil
        if not ok then error(result) end
        return result
    end end
end
local request_cancel=A.cancel_task
function A:cancel_task()
    local p=self._task_pending
    local market=self.daily_state and self.daily_state.market
    if market and market.uncertain and (not p or tostring(p.command):match("^query_")) then p=market.uncertain;self._task_pending=p;p.sent_at=task_now(self) end
    if self._request_probe and self._request_probe.source then
        p=self._request_probe.source;self._task_pending=p;p.sent_at=task_now(self)
    end
    if p and p.queued and not p._sent then p.response={result="cancelled"} end
    self._request_probe=nil
    if self.daily_state and self.daily_state.refine_reward_active then
        self:close_refine_game()
    end
    return request_cancel(self)
end

local request_tick=A.tick
if request_tick then
    function A:tick(now,...)
        local p=self._task_pending
        if p and p.queued and not (self.daily_state and self.daily_state.running) then
            self.now=tonumber(now) or self.now;p.sent_at=task_now(self)
            if request_channel_free() then network:Send(request_copy(p._transport_message or p.message)) end
        end
        return request_tick(self,now,...)
    end
end
