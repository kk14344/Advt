-- Mine carts have their own completion rules, independent of liveness quotas.
-- Only the current, acknowledged operation may spend. Transport starts once per
-- daily run; interception exhausts existing attempts without buying more.
A.cfg.cartUpgradeMax=true
A.cfg.cartBuyEscort=true
A.cfg.cartPaidTargetRefresh=true
for _,event in ipairs({"escort_buy_escort_times_ret","escort_refresh_rob_target_list_ret"})do
    A.DAILY_EVENTS[#A.DAILY_EVENTS+1]=event
end
local function cart_state(self,id)
    local st=self.daily_state
    st.carts=st.carts or {}
    if not st.carts[id] then st.carts[id]={loaded={},tried={},started=task_now(self)} end
    return st.carts[id]
end
local function cart_key(target)
    -- A player may send a new cart later; the same trip must not be fought twice.
    return tostring(target.user_id)..":"..tostring(target.escort_beg_time or 0)
end
local function cart_blood(cost)
    local id=(constants.RESOURCE_TYPE or {}).blood_diamond
    cost=tonumber(cost)
    return cost and cost>=0 and (cost==0 or id and (tonumber(resource_logic:GetResourceNum(id)) or 0)>=cost)
end
local function cart_highest()
    return tonumber(constants.SPECITY_TRAMCAR_ID) or 4
end
local function cart_finish(self,id,message,seconds)
    local f=cart_state(self,id);f.done=true;f.deferred=nil
    self.daily_state.skipped[id]=message
    if id==7 then self:record_harvest("escort",seconds) end
    if id==7 then
        message=message.." · 购次 "..(f.buy_cost or 0).." 血钻 · 升满 "..(f.upgrade_cost or 0).." 血钻 · "
            ..(f.reward_text or "本轮无到站领奖")
        self.daily_state.skipped[id]=message
        f.review=message
    end
    self:log((id==7 and "矿车运送：" or "矿车拦截：")..message)
    return reply(self,message,{action="矿车阶段完成"})
end
local function cart_remaining(info)
    local ending=tonumber(info and info.escort_end_time)
    return ending and ending>0 and math.max(5,ending-time_logic:Now()) or 10
end
local function cart_defer(self,id,message)
    local st=self.daily_state;local f=cart_state(self,id)
    f.deferred=true;f.loaded={}
    st.retry_ids=st.retry_ids or {};st.retry_ids[id]=true;st.skipped[id]=message
    self:log(message.."；其他日常完成后重新查询补办")
    return reply(self,message.."，已进入队尾补办",{action="矿车队尾补办"})
end
local function cart_request(self,id,key,payload,description,cost,extra)
    -- Set authorization before Send; task_request calls the closure synchronously.
    local p=self:task_request(key.."_ret","daily_cart",function()
        local current=self._task_pending
        current.cart_id=id;current.task_id=id;current.cart_cost=cost or 0
        current.description=description
        for k,v in pairs(extra or {})do current[k]=v end
        return network:Send({[key]=payload or {}})
    end)
    return reply(self,description,{wait=true,phase="cart"})
end
local function cart_query(self,id,key,flag,description)
    return cart_request(self,id,key,{},description,0,{cart_flag=flag})
end
function A:daily_cart_pending(id)
    if id~=7 and id~=14 then return false end
    local f=cart_state(self,id)
    return not f.done and not f.deferred
end
local cart_guard=A.daily_message_is_free
function A:daily_message_is_free(name,payload)
    if name=="escort_buy_rob_times" then return false end
    if name=="escort_buy_escort_times" or name=="escort_refresh_tramcar" or name=="escort_refresh_rob_target_list" then
        local st=self.daily_state;local p=self._task_pending
        local authorized=st and st.running and p and p.kind=="daily_cart" and p.command==name
        if not authorized then
            -- Preserve the old guard for manual free refreshes, never authorize a
            -- paid or automatic nested operation merely because daily is running.
            return cart_guard(self,name,payload)
        end
        if not cart_blood(p.cart_cost) then return false end
        if name=="escort_buy_escort_times" then
            return self.cfg.cartBuyEscort~=false and p.cart_id==7 and tonumber(payload.times)==p.cart_buy_count
        elseif name=="escort_refresh_tramcar" then
            if payload.refresh_type=="random" then
                local t=require("logic.escort").escort_times or {}
                if (tonumber(t.refresh_tramcar) or math.huge)>=(tonumber(constants.FREE_REFRESH_TRAMCAR_TIMES) or 0) then
                    p.cart_blocked_result="refresh_quota_changed";return false
                end
            end
            return p.cart_id==7 and (payload.refresh_type=="auto" or
                payload.refresh_type=="random" and p.cart_cost==0 or
                payload.refresh_type=="specify" and self.cfg.cartUpgradeMax~=false)
        else
            return p.cart_id==14 and (payload.refresh_type=="normal" or
                payload.refresh_type=="immediately" and self.cfg.cartPaidTargetRefresh~=false)
        end
    end
    return cart_guard(self,name,payload)
end
function A:daily_cart_step(id)
    local st=self.daily_state;local f=cart_state(self,id)
    local escort=require("logic.escort")
    if f.done or f.deferred then return reply(self,"矿车事项本轮已处理",{action="矿车阶段"}) end
    if self._cart_visual_battle and self.cfg.autoSkipBattle==false then return reply(self,"等待矿车拦截动画与战斗播放完毕",{wait=true}) end
    local p=self._task_pending
    if p then
        if p.kind~="daily_cart" or p.cart_id~=id then return reply(self,"等待当前请求结算后处理矿车",{wait=true}) end
        if not p.response then return reply(self,"等待矿车服务器确认",{wait=true}) end
        self._task_pending=nil
        local msg=p.response;local result=msg.result
        if result==0 or result==nil then result="success" end
        local accepted=result=="success" or p.command=="escort_rob_target" and result=="battle_failure"
        if accepted then
            if p.command=="escort_buy_escort_times" then f.buy_cost=(f.buy_cost or 0)+p.cart_cost end
            if p.command=="escort_refresh_tramcar" and p.cart_cost>0 then f.upgrade_cost=(f.upgrade_cost or 0)+p.cart_cost end
            if p.cart_cost>0 and not (p.reconciled and p.command=="escort_refresh_rob_target_list") then self:spend_record(p.cart_cost,p.description) end
            if p.cart_flag then f.loaded[p.cart_flag]=true
            elseif p.command=="escort_start" then
                st.needs_sync=true;st.actions=st.actions+1
                return cart_finish(self,id,"本次已发出1趟矿车",cart_remaining(msg.escort_info or escort.escort_info))
            elseif p.command=="escort_rob_target" then
                f.tried[p.cart_target_key]=true;f.fights=(f.fights or 0)+1
                st.actions=st.actions+1;st.needs_sync=true
                if p.reconciled then f.loaded.times=false end
                self:hide_panel("reward_panel")
            elseif p.command=="escort_refresh_rob_target_list" then
                -- Reconciliation queries the target list, so even a lost reply
                -- returns to selection before another paid refresh is considered.
                f.loaded.targets=type(msg.rob_target_list)=="table" or p.reconciled==true
                if not p.reconciled and type(msg.rob_target_list)~="table" then f.loaded.targets=false end
                f.refreshes=(f.refreshes or 0)+1
            elseif p.command=="escort_refresh_tramcar" then
                if p.reconciled or not tonumber(msg.tramcar_id) then f.loaded.info=false end
                if p.reconciled then f.loaded.times=false end
            elseif p.command=="escort_receive_reward" then
                f.reward_text="到站奖励已领取"..(p.cart_reward and (" · "..p.cart_reward) or "")
                self:hide_panel("reward_panel");f.loaded.info=false
            elseif p.command=="escort_buy_escort_times" then
                f.loaded.times=false
            end
        elseif result=="refresh_quota_changed" then f.loaded.info=false;f.loaded.times=false
        elseif result=="already_specify_tramcar" then f.loaded.info=false
        elseif p.command=="escort_rob_target" and (result=="has_robbed_this_target" or result=="target_be_robbed_to_much_times"
            or result=="target_is_not_escorting" or result=="target_has_finished" or result=="no_target_troop_info") then
            f.tried[p.cart_target_key]=true;f.loaded.targets=false
        elseif result=="need_refresh_tramcar" or result=="status_is_not_ready" or result=="status_is_not_finish" then
            f.loaded.info=false;f.loaded.times=false
        elseif result=="no_rob_times" then f.loaded.times=false
        elseif result=="in_refresh_target_cd" then f.loaded.info=false
        elseif p.command=="escort_refresh_tramcar" and result=="not_enough_resource" then
            f.upgrade_unavailable=true;f.loaded.info=false
        elseif p.command=="escort_buy_escort_times" and (result=="has_buy_too_much_escort_times" or result=="not_enough_resource") then
            f.buy_unavailable=true;f.loaded.times=false
        elseif result=="not_enough_resource" or result=="no_troop_info" or result=="feature_not_open" then
            return cart_finish(self,id,"矿车条件不足："..tostring(result))
        else return cart_defer(self,id,"矿车操作未被接受："..tostring(result)) end
        return reply(self,(p.description or p.command).."："..tostring(result),{action="矿车响应"})
    end
    if not f.loaded.info then return cart_query(self,id,"query_escort_info","info","读取矿车运送状态") end
    if not f.loaded.times then return cart_query(self,id,"query_escort_times","times","读取矿车剩余次数与购买额度") end
    local info=escort.escort_info or {};local times=escort.escort_times or {}
    if id==14 then
        local remain=tonumber(times.remain_rob)
        if not remain then return cart_defer(self,id,"矿车拦截次数尚未同步") end
        if remain<=0 then return cart_finish(self,id,"现有拦截次数已用完，不购买额外次数") end
        if not f.loaded.targets then return cart_query(self,id,"query_rob_target_list","targets","读取可拦截矿车") end
        local choices={};local now=time_logic:Now();local me=tostring((package.loaded["logic.user"] or {}).user_id or "")
        for _,target in pairs(escort.rob_target_list or {})do
            local level=tonumber(target.tramcar_id);local uid=target.user_id
            local until_time=tonumber(target.escort_end_time)
            local key=cart_key(target)
            local history=false;local max_rob=tonumber(constants.MAX_BE_ROBBED_TIMES)
            local records=target.be_robbed_list or {};local count=0
            for _,row in pairs(records)do
                if (tonumber(row.be_robbed_time) or 0)>=(tonumber(target.escort_beg_time) or 0) then
                    count=count+1
                    if tostring(row.user_id or "")==me and me~="" then history=true end
                end
            end
            if uid and tostring(uid)~=me and (level==3 or level==4) and not f.tried[key] and not history
                and (not max_rob or count<max_rob) and (not until_time or until_time>now) then choices[#choices+1]=target end
        end
        table.sort(choices,function(a,b)
            if tonumber(a.tramcar_id)~=tonumber(b.tramcar_id) then return tonumber(a.tramcar_id)>tonumber(b.tramcar_id) end
            return tostring(a.user_id)<tostring(b.user_id)
        end)
        local target=choices[1]
        if target then
            local uid=target.user_id
            return cart_request(self,id,"escort_rob_target",{target_user_id=uid},"拦截"..target.tramcar_id.."级矿车（本趟只尝试一次）",0,
                {cart_target_key=cart_key(target),accept_response=function(msg)return msg.target_user_id==nil or tostring(msg.target_user_id)==tostring(uid)end})
        end
        local cooldown=tonumber(info.refresh_rob_target_time)
        if not cooldown then return cart_defer(self,id,"矿车目标刷新冷却尚未同步") end
        local paid=cooldown>now
        if paid and self.cfg.cartPaidTargetRefresh==false then return cart_finish(self,id,"没有未尝试的3、4级矿车，血钻刷新已关闭") end
        local cost=paid and tonumber(constants.ESCORT_REFRESH_ROB_TARGET_LIST_IMMEDIATELY_COST) or 0
        if not cart_blood(cost) then return cart_finish(self,id,"没有合适目标，血钻不足以刷新") end
        if task_now(self)<(f.next_refresh_at or 0) then return reply(self,"等待下一次矿车目标刷新",{wait=true,delaySeconds=0.25}) end
        f.next_refresh_at=task_now(self)+1
        return cart_request(self,id,"escort_refresh_rob_target_list",{refresh_type=paid and "immediately" or "normal"},
            paid and "使用血钻刷新，寻找3、4级矿车" or "免费刷新，寻找3、4级矿车",cost)
    end
    local status=constants.ESCORT_STATUS or {READY=1,ESCORTING=2,FINISH=3}
    -- Rewards must be claimed even when no transport attempts remain.
    if tonumber(info.status)==tonumber(status.FINISH) then
        return cart_request(self,id,"escort_receive_reward",{},"收取已完成矿车的运送奖励",0)
    end
    if tonumber(info.status)==tonumber(status.ESCORTING) then
        local finish=tonumber(info.escort_end_time)
        if finish and finish<=time_logic:Now() and not f.end_queried then
            f.end_queried=true;f.loaded.info=false
            return reply(self,"矿车倒计时已到，重新确认服务器结算状态",{action="矿车结算查询"})
        end
        return cart_finish(self,id,"已有矿车运送中",cart_remaining(info))
    end
    if tonumber(info.status)~=tonumber(status.READY) then return cart_defer(self,id,"矿车运送状态尚未同步") end
    local remain=tonumber(times.remain_escort)
    if not remain then return cart_defer(self,id,"矿车运送次数尚未同步") end
    if remain<=0 then
        if f.buy_unavailable or self.cfg.cartBuyEscort==false then return cart_finish(self,id,"运送次数不足，购买已关闭、余额不足或已达上限") end
        local bought=tonumber(times.buy_escort)
        local max=tonumber(constants.MAX_BUY_ESCORT_TIMES)
        if not bought or not max then return cart_defer(self,id,"矿车购买额度尚未同步") end
        local prices=config_manager.tramcar_buy_escort_config or {};local count,cost=0,0
        for i=bought+1,max do
            local price=tonumber((prices[i] or {}).cost)
            if not price or price<0 or not cart_blood(cost+price) then break end
            cost=cost+price;count=count+1
        end
        if count==0 then return cart_finish(self,id,"矿车运送已达当日购买上限或血钻不足") end
        return cart_request(self,id,"escort_buy_escort_times",{times=count},"一次购买剩余可购运送次数 ×"..count,cost,{cart_buy_count=count})
    end
    local auto=tonumber(info.auto_refresh_tramcar)==tonumber((constants.ESCORT_AUTO_REFRESH_TRAMCAR or {}).TRUE or 1)
    if auto or not tonumber(info.tramcar_id) or tonumber(info.tramcar_id)<=0 then
        return cart_request(self,id,"escort_refresh_tramcar",{refresh_type="auto"},"同步本趟初始矿车等级",0)
    end
    if tonumber(info.tramcar_id)<cart_highest() then
        local used=tonumber(times.refresh_tramcar);local free=tonumber(constants.FREE_REFRESH_TRAMCAR_TIMES)
        if not used or not free then return cart_defer(self,id,"矿车免费刷新次数尚未同步") end
        if used<free then return cart_request(self,id,"escort_refresh_tramcar",{refresh_type="random"},"免费刷新矿车，目标最高等级",0) end
        local cost=tonumber(constants.ESCORT_REFRESH_TRAMCAR_SPECIFY_COST)
        if self.cfg.cartUpgradeMax~=false and not f.upgrade_unavailable and cart_blood(cost) then
            return cart_request(self,id,"escort_refresh_tramcar",{refresh_type="specify"},"一键升至最高等级矿车",cost)
        end
        if not f.fallback_logged then self:log("免费刷新已用完，无法一键升满；按设置使用当前等级运送");f.fallback_logged=true end
    end
    return cart_request(self,id,"escort_start",{},"开始运送"..tostring(info.tramcar_id).."级矿车",0)
end

function A:cart_reward_receipt(message)
    local p=self._task_pending
    if not p or p.command~="escort_receive_reward" or type(message)~="table" then return end
    local parts={}
    for _,info in ipairs(message.reward_info_list or {}) do
        local id=tonumber(info.resource_id or info.param1)
        local count=tonumber(info.resource_num or info.param2)
        if id and count and count>0 and (config_manager.resource_config or {})[id] then
            parts[#parts+1]=resource_name(id).." ×"..count
        end
    end
    if #parts>0 then p.cart_reward=table.concat(parts,"、") end
end

function A:start_escort_followup()
    self:cancel_task()
    self.daily_state={running=true,cart_followup=true,skipped={},actions=0}
    self:install_daily_spend_guard()
end

function A:escort_followup_step()
    local state=self.daily_state
    if not state or not state.cart_followup then return reply(self,"矿车续跑已停止",{done=true}) end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    local result=self:daily_cart_step(7)
    local cart=cart_state(self,7)
    if cart.done or cart.deferred then
        self:hide_panel("reward_panel")
        state.running=false
        result.done=true
        result.partial=cart.deferred==true
        if cart.deferred then self:record_harvest("escort",60) end
    end
    return result
end
local cart_retry_items=A.daily_retry_items
function A:daily_retry_items(st)
    if not st.retry_active then
        for _,id in ipairs({7,14})do
            local f=st.carts and st.carts[id]
            if f and (st.retry_ids or {})[id] then f.deferred=nil;f.loaded={} end
        end
    end
    return cart_retry_items(self,st)
end
