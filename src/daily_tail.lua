-- Fresh reads at the end of the run reconcile dropped cave commands and collect
-- rewards earned by the final child tasks. Writes always wait for their reply.
local function tail_send(self,event,key,payload,description)
    local p=self:task_request(event,"daily_action",function()return network:Send({[key]=payload or {}})end)
    p.description=description;p.tail=true
    return reply(self,description,{wait=true})
end
local function defer_cave(self,st,p)
    local kind=p.cave_kind or p.cave_buy_kind
    st.cave_skips=st.cave_skips or {};st.cave_buy_stops=st.cave_buy_stops or {}
    st.cave_skips[kind]="等待收尾重新核对";st.cave_buy_stops[kind]=true
    st.cave_uncertain=st.cave_uncertain or {};st.cave_uncertain[kind]=p
    self._task_pending=nil;st.loaded.mining=false
    self:log("副本"..kind.."未收到操作确认，先执行其他日常，收尾查询剩余次数后补办")
end
function A:daily_tail_caves(st)
    local t=st.cave_tail
    if not t then
        t={stalls={},checked=false};st.cave_tail=t
        st.loaded.mining=false;st.loaded.caves=false
        -- A new server read determines remaining purchases, not sent commands.
        st.cave_bought=st.cave_bought or {};st.cave_skips=st.cave_skips or {};st.cave_buy_stops={};st.cave_fights={}
        st.cave_skips={}
        st.skipped[9]=nil;st.skipped[10]=nil
    end
    if self.fast_refill then
        local r=self:refill_step();if not r.refill_done then return r end
        if r.error then return {done=true,partial=true,message="副本补查：矿镐不足"} end
    end
    local p=self._task_pending
    if p then
        if not p.response then
            if task_now(self)-p.sent_at<=20 then return reply(self,"等待副本补查响应",{wait=true}) end
            local kind=p.cave_kind or p.cave_buy_kind
            if not kind then return {done=true,error=true,partial=true,message="副本状态查询超时，停止补发"} end
            t.check=p;t.expired=true;self._task_pending=nil;st.loaded.mining=false
        else
            self._task_pending=nil
            local result=p.response.result
            if p.flag then
                if result and result~="success" then return {done=true,partial=true,message="副本状态读取失败"} end
                st.loaded[p.flag]=true
                if p.event=="query_mining_info_ret" then
                    for kind,c in pairs(st.cave_uncertain or {})do
                        local counts=p.response.cave_challenge_nums
                        local buys=p.response.cave_buy_challenge_nums
                        if type(counts)~="table" or tonumber(counts[kind])==nil
                            or (c.cave_buy_kind and (type(buys)~="table" or tonumber(buys[kind])==nil)) then
                            st.cave_skips[kind]="服务端未返回剩余次数，不补发未确认操作"
                            st.cave_buy_stops[kind]=true;t.partial=true;t.needs_retry=true
                        end
                    end
                    st.cave_uncertain={}
                end
            end
            if p.cave_kind or p.cave_buy_kind then
                t.check=p;st.loaded.mining=false
                if p.cave_kind and (p.response.is_winner==false or result=="battle_failure") then
                    st.cave_level_ceiling=st.cave_level_ceiling or {}
                    st.cave_level_ceiling[p.cave_kind]=math.max(0,(p.cave_level or 1)-1)
                end
            end
            if p.event=="query_mining_info_ret" and t.check then
                local c=t.check;local kind=c.cave_kind or c.cave_buy_kind
                local now=tonumber((mining.cave_challenge_nums or {})[kind]) or 0
                local left=tonumber((mining.cave_buy_challenge_nums or {})[kind])
                local fresh=type(p.response.cave_challenge_nums)=="table" and tonumber(p.response.cave_challenge_nums[kind])~=nil
                if c.cave_buy_kind then fresh=fresh and type(p.response.cave_buy_challenge_nums)=="table" and tonumber(p.response.cave_buy_challenge_nums[kind])~=nil end
                if t.expired and not fresh then
                    st.cave_skips[kind]="次数读取不完整，不补发未确认操作";st.cave_buy_stops[kind]=true;t.partial=true;t.needs_retry=true
                end
                local progressed=c.cave_kind and now<(c.cave_before or 0)
                    or c.cave_buy_kind and ((left and left<(c.cave_buy_left or left)) or now>(c.cave_buy_before or now))
                if not progressed then
                    t.stalls[kind]=(t.stalls[kind] or 0)+1
                    st.cave_skips[kind]="本轮尚未执行，排到下一轮补办";st.cave_buy_stops[kind]=true
                    t.partial=true;t.needs_retry=true
                else t.stalls[kind]=0 end
                t.check=nil;t.expired=nil
            end
            return reply(self,"副本补查响应已处理",{action="副本补查"})
        end
    end
    if not st.loaded.mining then return self:daily_query("query_mining_info_ret","query_mining_info","mining") end
    if not t.reset_bought then
        for kind,left in pairs(mining.cave_buy_challenge_nums or {})do
            if tonumber(left) then st.cave_bought[kind]=0 end
        end
        t.reset_bought=true
    end
    if not st.loaded.caves then return self:daily_query("query_cave_event_config_info_ret","query_cave_event_config_info","caves") end
    for _,id in ipairs({9,10})do
        if self:daily_enabled(id) and not st.skipped[id] and self:daily_cave_pending(id) then
            st.cave_last_send_kind=nil
            local ok,r=pcall(function()return self:daily_action(id,nil,nil)end)
            if not ok then
                local kind=st.cave_last_send_kind
                if kind then st.cave_skips[kind]="游戏未接收操作";st.cave_buy_stops[kind]=true else st.skipped[id]="副本接口暂不可用" end
                t.partial=true;t.needs_retry=true;self:log("副本补办未被接收，继续其他项目："..tostring(r))
                return reply(self,"副本补办暂未被接收，继续其他项目",{action="副本补查"})
            end
            if r then return r end
        end
    end
    return {done=true,partial=t.partial,retry=t.needs_retry,message=t.needs_retry and "未接收的副本操作进入下一轮补办" or "已核对并补办巨魔巢穴与地下探险剩余次数"}
end
-- ============================================================
-- 冒险页探险结果（1.42）
--
-- 冒险页右侧、剑图标旁的放大镜是 ui/exploring_panel.lua 的 open_box_btn
-- （item_btn）；它的原生绑定是 adventure_logic:OpenBox()，成功后服务器回
-- open_box_ret，游戏再弹出 loot_result_panel。这里只做一次同样的调用，
-- 等领奖窗口出现后关闭，不切换关卡、不开启新的探索。
-- ============================================================
local ADVENTURE_TIMEOUT=40
local ADVENTURE_PANEL_TRIES=4

local function adventure_context(self)
    local ok,context=pcall(function() return self:read_context() end)
    if not ok or not context then return nil end
    if context.sub_name~="exploring_sub_scene" then return nil,context end
    return context.sub_scene or true,context
end

function A:close_adventure_panel()
    pcall(function() self:hide_panel("loot_result_panel") end)
    local sub,context=adventure_context(self)
    if sub and context and context.sub_name=="exploring_sub_scene" then
        pcall(function() graphic:DispatchEvent("hide_world_sub_scene") end)
    end
end

function A:daily_adventure_claim(st)
    if st.adventure_done then return {done=true,message=""} end
    local ok,adv=pcall(require,"logic.adventure")
    if not ok or type(adv)~="table" or type(adv.OpenBox)~="function" then
        st.adventure_done=true
        st.adventure_summary="冒险领取：当前客户端缺少开箱接口"
        self:log("冒险探险结果：当前客户端缺少 OpenBox 接口，跳过领取")
        return {done=true,message="冒险领取：接口不可用"}
    end
    local a=st.adventure
    if not a then a={phase="open",started=task_now(self)};st.adventure=a end
    if task_now(self)-a.started>ADVENTURE_TIMEOUT then
        self:close_adventure_panel()
        st.adventure_done=true
        st.adventure_summary="冒险领取：等待超时，下一轮再试"
        self:log("冒险探险结果：领取超时，已关闭冒险页并继续日常")
        return {done=true,partial=true,message="冒险领取：等待超时"}
    end
    local boxes=tonumber(adv.explored_box_num) or 0
    if a.phase=="open" then
        if boxes<=0 then
            st.adventure_done=true
            st.adventure_summary="冒险领取：暂无可领取的探险结果"
            self:log("冒险探险结果：没有待领取的宝箱，继续日常")
            return {done=true,message=st.adventure_summary}
        end
        local sub=adventure_context(self)
        if not sub then
            if (a.open_tries or 0)>=6 then
                st.adventure_done=true
                st.adventure_summary="冒险领取：冒险页未能打开"
                self:log("冒险探险结果：冒险页未能打开，跳过领取")
                return {done=true,message=st.adventure_summary}
            end
            a.open_tries=(a.open_tries or 0)+1
            pcall(function() graphic:DispatchEvent("show_world_sub_scene","exploring_sub_scene") end)
            return reply(self,"打开冒险页面领取探险结果",{wait=true,delaySeconds=0.5,phase="adventure"})
        end
        local panel=type(sub)=="table" and sub.ui_root or nil
        if not panel or not panel.open_box_btn then
            st.adventure_done=true
            st.adventure_summary="冒险领取：未找到放大镜开箱按钮"
            self:log("冒险探险结果：当前客户端未绑定 open_box_btn，跳过领取")
            self:close_adventure_panel()
            return {done=true,message=st.adventure_summary}
        end
        a.phase="click"
    end
    local pending=self._task_pending
    if pending and pending.kind=="daily_adventure" then
        if not pending.response then
            if task_now(self)-pending.sent_at<=15 then
                return reply(self,"等待探险结果领取确认",{wait=true,phase="adventure"})
            end
            self._task_pending=nil
            st.adventure_done=true
            st.adventure_summary="冒险领取：未收到领取回执，下一轮再试"
            self:log("冒险探险结果：领取回执超时，保留游戏状态并继续日常")
            self:close_adventure_panel()
            return {done=true,partial=true,message=st.adventure_summary}
        end
        local msg=pending.response
        self._task_pending=nil
        local result=msg.result
        local opened=tonumber(msg.open_box_num) or 0
        if opened<=0 or (result~="success" and result~="bag_full") then
            st.adventure_done=true
            st.adventure_summary="冒险领取：服务器未接受（"..tostring(result).."）"
            self:log("冒险探险结果："..tostring(result).."，继续日常")
            self:close_adventure_panel()
            return {done=true,partial=true,message=st.adventure_summary}
        end
        a.claimed=opened
        a.partial=result=="bag_full"
        a.phase="close";a.panel_tries=0
    end
    if a.phase=="click" then
        local sub=adventure_context(self)
        if not sub then a.phase="open";return reply(self,"重新打开冒险页面",{wait=true,delaySeconds=0.4,phase="adventure"}) end
        if boxes<=0 then
            a.phase="close";a.panel_tries=ADVENTURE_PANEL_TRIES
        else
            local bag_ok,space=pcall(function() return require("logic.bag"):GetSpaceCount() end)
            if bag_ok and tonumber(space)==0 then
                st.adventure_done=true
                st.adventure_summary="冒险领取：背包已满，暂不开箱"
                self:log("冒险探险结果：背包已满，跳过领取")
                self:close_adventure_panel()
                return {done=true,message=st.adventure_summary}
            end
            self:install_task_hooks()
            if not (self._task_hooks or {}).open_box_ret then
                st.adventure_done=true
                st.adventure_summary="冒险领取：开箱回包未注册"
                self:log("冒险探险结果：未拦截到 open_box_ret，跳过领取")
                self:close_adventure_panel()
                return {done=true,message=st.adventure_summary}
            end
            local requested,request=pcall(function()
                return self:task_request("open_box_ret","daily_adventure",function()
                    return adv:OpenBox()
                end)
            end)
            if not requested then
                self._task_pending=nil
                st.adventure_done=true
                st.adventure_summary="冒险领取：开箱请求未能发出"
                self:log("冒险探险结果：开箱请求未能发出（"..tostring(request).."），继续日常")
                self:close_adventure_panel()
                return {done=true,partial=true,message=st.adventure_summary}
            end
            request.description="领取冒险探险结果"
            return reply(self,"按放大镜按钮的原生流程领取探险结果",{wait=true,phase="adventure",delaySeconds=0.2})
        end
    end
    if a.phase=="close" then
        local scene=nil
        local ok_context,context=pcall(function() return self:read_context() end)
        if ok_context and context then scene=context.scene end
        local panel=scene and scene.sub_panels and scene.sub_panels["loot_result_panel"]
        if panel and panel:IsVisible() then
            pcall(function() self:hide_panel("loot_result_panel") end)
            a.closed=true
            return reply(self,"关闭探险结果窗口",{wait=true,delaySeconds=0.2,phase="adventure"})
        end
        if not a.closed then a.panel_tries=(a.panel_tries or 0)+1 end
        if not a.closed and a.panel_tries<ADVENTURE_PANEL_TRIES then
            return reply(self,"等待探险结果窗口",{wait=true,delaySeconds=0.3,phase="adventure"})
        end
        self:close_adventure_panel()
        st.adventure_done=true
        st.adventure_summary=(a.claimed or 0)>0 and ("冒险领取：已领取 "..a.claimed.." 个宝箱") or "冒险领取：暂无可领取的探险结果"
        if a.partial then st.adventure_summary=st.adventure_summary.."，背包已满，剩余留待下次" end
        self:log(st.adventure_summary.."，继续日常")
        return {done=true,partial=a.partial,message=st.adventure_summary}
    end
    return {done=true,message=""}
end

function A:daily_tail_rewards(st)
    local t=st.reward_tail
    if not t then
        t={phase="activity",mail={},attempts={},reward_attempts={}};st.reward_tail=t
        st.activity_done=false;st.activity_queries={};st.activity_seen={};st.activity_tries=0
    end
    local p=self._task_pending
    if p then
        if not p.response then
            if task_now(self)-p.sent_at<=20 then return reply(self,"等待收尾领奖确认",{wait=true}) end
            -- These claims have server-side claimed flags. Do not retry a
            -- missing acknowledgement within this run; continue other rewards.
            self._task_pending=nil;t.partial=true;t.needs_retry=true
            if p.mail_id or p.tail_query=="mail" then t.mail_retry=true
            elseif p.tail_reward or p.tail_query=="liveness" then t.liveness_retry=true
            elseif p.extra and p.extra:sub(1,9)=="activity_" then t.activity_retry=true end
            if p.tail_query=="mail" then t.phase="liveness";t.needs_retry=true
            elseif p.tail_query=="liveness" then return {done=true,partial=true,message="活跃度查询超时"}
            elseif p.extra and p.extra:sub(1,9)=="activity_" then st.activity_done=true end
            self:log((p.description or "收尾领奖").."超时，保留日志并继续其他奖励")
        else
            self._task_pending=nil
            local msg=p.response;local result=msg.result
            if p.extra and p.extra:sub(1,9)=="activity_" then
                if result and result~="success" then t.needs_retry=true;t.activity_retry=true end
                self:daily_activity_response(p,result)
            elseif p.tail_query=="mail" then
                if result and result~="success" then t.partial=true;t.phase="liveness";t.mail_retry=true
                else
                    t.mail_checked=true
                    -- Some servers return only new mail; merge existing unread
                    -- client caches with the response and deduplicate by ID.
                    local mail=require("logic.mail");local seen={}
                    local function add(list)
                        for _,row in pairs(list or {})do
                            local key=row.id and tostring(row.id)
                            if key and row.mark_read~=true and not seen[key] then
                                seen[key]=true;t.mail[#t.mail+1]=row
                            end
                        end
                    end
                    add(msg.mail_list)
                    for _,key in ipairs({"not_read_list","system_mail_not_read_list","friend_mail_not_read_list"})do add(mail[key]) end
                    for _,key in ipairs({"not_read_list","system_mail_not_read_list","friend_mail_not_read_list"})do
                        local list=mail[key];local ids={}
                        if type(list)=="table" then for i=#list,1,-1 do
                            local id=tostring(list[i].id)
                            if ids[id] then table.remove(list,i) else ids[id]=true end
                        end end
                    end
                    t.phase="mail_claim"
                end
            elseif p.mail_id then
                if result~="success" then t.needs_retry=true;t.mail_retry=true end
                if result=="success" then st.mail_claimed=st.mail_claimed+1
                else st.mail_failures=st.mail_failures+1;t.partial=true end
            elseif p.tail_query=="liveness" then
                if result and result~="success" then return {done=true,partial=true,message="活跃度读取失败"} end
                t.live_loaded=true
            elseif p.tail_reward then
                if result=="success" then st.claims=st.claims+1 else t.partial=true;t.needs_retry=true;t.liveness_retry=true end
                t.live_loaded=false -- Refresh to include new boxes earned by task claims.
            end
        end
    end
    self:install_task_hooks();local hooks=self._task_hooks or {}
    if t.phase=="activity" then
        if st.activity_rewards and not st.activity_done then
            local r=self:daily_activity_rewards();if r then return r end
        end
        t.phase="mail"
    end
    if t.phase=="mail" then
        local ok=pcall(require,"logic.mail")
        if st.mail_enabled and ok and hooks.query_mail_info_ret and hooks.open_mail_ret then
            local r=tail_send(self,"query_mail_info_ret","query_mail_info",{},"收尾读取全部待领取邮件")
            self._task_pending.tail_query="mail";return r
        end
        st.mail_done=true;t.phase="liveness"
    end
    if t.phase=="mail_claim" then
        for _,row in ipairs(t.mail)do
            local key=tostring(row.id)
            if not t.attempts[key] then
                t.attempts[key]=true;st.mail_tries=(st.mail_tries or 0)+1
                if st.mail_tries>300 then break end
                local r=tail_send(self,"open_mail_ret","open_mail",{id=row.id},"收尾领取邮件"..key)
                self._task_pending.mail_id=row.id
                self._task_pending.accept_response=function(msg)return tostring(msg.id)==key end
                return r
            end
        end
        st.mail_done=true;t.phase="liveness"
    end
    if t.phase=="liveness" and st.rewards then
        if not t.live_loaded then
            local r=tail_send(self,"query_liveness_info_ret","query_liveness_info",{},"收尾刷新任务与活跃度奖励")
            self._task_pending.tail_query="liveness";return r
        end
        local daily=require("logic.daily")
        for _,row in pairs(daily.liveness_list or {})do
            local id=tonumber(row.liveness_id);local key="task:"..tostring(id)
            if id and not row.is_reward and daily:CheckActivityComplet(id,row.completion_count or 0) and not t.reward_attempts[key] then
                t.reward_attempts[key]=true
                local r=tail_send(self,"get_liveness_reward_ret","get_liveness_reward",{liveness_id=id},"收尾领取已完成任务奖励")
                self._task_pending.tail_reward=true
                self._task_pending.accept_response=function(msg)return tonumber(msg.liveness_id)==id end
                return r
            end
        end
        for _,index in pairs(daily.activity_receive_list or {})do
            local key="box:"..tostring(index)
            if index and not t.reward_attempts[key] then
                t.reward_attempts[key]=true
                local r=tail_send(self,"get_activity_reward_ret","get_activity_reward",{index=index},"收尾领取活跃度宝箱")
                self._task_pending.tail_reward=true
                self._task_pending.accept_response=function(msg)return tonumber(msg.index)==tonumber(index) end
                return r
            end
        end
    end
    return {done=true,partial=t.partial,retry=t.needs_retry,message="收尾邮件、活动与活跃度奖励领取结束"}
end
