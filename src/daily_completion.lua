-- Daily-only trade authorization and a single completion path for every child
-- flow, including research and void exchange. Never replay an uncertain trade.
A.cfg.dailyVoidChallenge=true
A.cfg.dailyMarketExchange=true
for _,name in ipairs({"query_merchant_ret","merchant_exchange_ret","merchant_soul_chip_ret"})do
    A.DAILY_EVENTS[#A.DAILY_EVENTS+1]=name
end
local mineral_names={"copper","iron","silver","tin","gold","diamond","titan_iron","ruby",
    "purple_gem","emerald","topaz","golem","red_soul_crystal","green_soul_crystal",
    "light_soul_crystal","dark_soul_crystal","inferno_brimstone","time_sand","golem2","golem3","golem4"}
local function trade_key(order)return tostring(order.merchant_type)..":"..tostring(order.order_id)end
local function trade_group(kind)
    local t=constants.MERCHANT_TYPE or {}
    return kind==(t.DARK or 1) or kind==(t.dark1 or 3) or kind==(t.dark2 or 4) or kind==(t.dark3 or 5)
end
local function exchange_kind(order)
    local kind=tonumber(order.merchant_type)
    return trade_group(kind) and ((constants.MERCHANT_TYPE or {}).DARK or 1) or kind
end
function A:daily_market_order_allowed(order)
    if type(order)~="table" or order.is_done==true or (tonumber(order.order_id) or 0)<=0
        or (tonumber(order.merchant_type) or 0)<=0 then return false end
    local kind=tonumber(order.merchant_type)
    if not trade_group(kind) and kind~=((constants.MERCHANT_TYPE or {}).WHITE or 2) then return false,"此订单不在矿产兑换范围" end
    local allowed={};local types=constants.RESOURCE_TYPE or {}
    for _,name in ipairs(mineral_names)do if types[name] then allowed[types[name]]=true end end
    local costs={};local count=0
    for i=1,2 do
        local id=tonumber(order["need_type"..i]) or 0
        local num=tonumber(order["need_num"..i]) or 0
        if id~=0 or num~=0 then
            if not allowed[id] or num<=0 or num~=math.floor(num) then return false,"此订单不在矿产兑换范围" end
            costs[id]=(costs[id] or 0)+num;count=count+1
        end
    end
    if count==0 then return false end
    for id,amount in pairs(costs)do
        local have=tonumber(resource_logic:GetResourceNum(id)) or 0
        if have<amount then return false,"缺"..resource_name(id).." ×"..(amount-have) end
    end
    return true
end
local function bonus_ready(snapshot)
    if not snapshot or snapshot.ext_soul_chip_flag==true then return false end
    local count=0
    for _,order in ipairs(snapshot.order_list or {})do
        if trade_group(tonumber(order.merchant_type)) then
            count=count+1;if order.is_done~=true then return false end
        end
    end
    return count>0
end
local old_daily_guard=A.daily_message_is_free
function A:daily_message_is_free(name,payload)
    local st=self.daily_state;local m=st and st.market
    if name=="vanity_buy_recruit" then
        return st and st.child_void_challenge==true and self.void_state~=nil
            and self._task_pending and self._task_pending.kind=="void_recruit" or false
    elseif name=="merchant_exchange" then
        local order=m and m.sending
        return st and st.completion_phase=="market" and order~=nil
            and tonumber(payload.order_id)==tonumber(order.order_id)
            and tonumber(payload.merchant_type)==exchange_kind(order)
            and self:daily_market_order_allowed(order) or false
    elseif name=="merchant_soul_chip" then
        return st and st.completion_phase=="market" and m and m.sending_bonus and bonus_ready(m.snapshot) or false
    end
    return old_daily_guard(self,name,payload)
end
local function market_request(self,event,key,payload,description)
    local p=self:task_request(event,"daily_market",function()return network:Send({[key]=payload or {}})end)
    p.description=description
    return reply(self,description,{wait=true,phase="market"})
end
function A:daily_market_step(st)
    local m=st.market
    if not m then m={tried={},requested={},confirmed={},exchanged=0,bonus=false};st.market=m end
    local p=self._task_pending
    if p then
        if not p.response then
            if task_now(self)-p.sent_at<=20 then return reply(self,"等待集市确认",{wait=true,phase="market"}) end
            self._task_pending=nil;m.retry=true;m.snapshot=nil
            if p.event~="query_merchant_ret" then m.must_query=true;m.uncertain=p end
            return {done=true,partial=true,retry=true,message="集市未确认操作进入队尾，重新查询后补办"}
        end
        self._task_pending=nil
        local response=p.response
        if p.event=="query_merchant_ret" then
            if response.result and response.result~="success" then return {done=true,partial=true,retry=true,message="集市查询暂未接受，进入队尾"} end
            local orders=response.order_list or {}
            if type(orders)~="table" then return {done=true,partial=true,retry=true,message="集市订单尚未同步，进入队尾"} end
            local ids={};for _,o in ipairs(orders)do ids[#ids+1]=trade_key(o)end;table.sort(ids)
            local signature=table.concat(ids,"|")..":"..tostring(response.reset_time or 0)
            if m.signature and m.signature~=signature then return {done=true,partial=true,message="集市已换批，本轮不重复旧订单"} end
            m.signature=signature;m.uncertain=nil
            response.order_list=orders;m.snapshot=response;m.snapshot_at=self.now;m.must_query=false
            for _,o in ipairs(orders)do
                local key=trade_key(o)
                if o.is_done==true and m.requested[key] and not m.confirmed[key] then
                    m.confirmed[key]=true;m.exchanged=m.exchanged+1
                end
            end
            m.bonus=response.ext_soul_chip_flag==true
        else
            if response.result~="success" then
                self:log((p.description or "集市操作").."尚未接受："..tostring(response.result).."，本轮其他事项后补办")
            end
            m.sending=nil;m.sending_bonus=nil;m.snapshot=nil
        end
    end
    if not m.snapshot then
        local ok,merchant=pcall(require,"logic.merchant");self:install_task_hooks()
        if not ok or type(merchant)~="table" or not (self._task_hooks or {}).query_merchant_ret
            or not self._task_hooks.merchant_exchange_ret or not self._task_hooks.merchant_soul_chip_ret then
            return {done=true,partial=true,retry=true,message="等待集市接口初始化后补办"}
        end
        return market_request(self,"query_merchant_ret","query_merchant",{},"读取集市当前订单与Bonus状态")
    end
    local needs_retry=false
    for _,order in ipairs(m.snapshot.order_list)do
        local key=trade_key(order)
        if self:daily_market_order_allowed(order) then
            if m.tried[key] then needs_retry=true
            else
                m.tried[key]=true;m.requested[key]=true;m.sending=order
                local kind,id=exchange_kind(order),tonumber(order.order_id);local row_kind=tonumber(order.merchant_type)
                local r=market_request(self,"merchant_exchange_ret","merchant_exchange",{merchant_type=kind,order_id=id},"兑换集市矿产订单"..id)
                self._task_pending.accept_response=function(msg)
                    return (msg.order_id==nil and msg.result~="success" or tonumber(msg.order_id)==id)
                        and (msg.merchant_type==nil or tonumber(msg.merchant_type)==kind or tonumber(msg.merchant_type)==row_kind)
                end
                return r
            end
        end
    end
    if bonus_ready(m.snapshot) and not m.bonus then
        if m.bonus_attempted then needs_retry=true
        else
            m.bonus_attempted=true;m.sending_bonus=true
            return market_request(self,"merchant_soul_chip_ret","merchant_soul_chip",{},"领取集市Bonus奖励")
        end
    end
    return {done=true,partial=needs_retry,retry=needs_retry,message=needs_retry and "集市尚未确认项进入下一轮补办" or
        ("集市兑换已核对，Bonus"..(m.bonus and "已领取" or "当前条件未满足"))}
end
function A:close_daily_market()
    local ok,context=pcall(function()return self:read_context()end)
    if ok and context and context.sub_name=="merchant_sub_scene" then
        pcall(function()graphic:DispatchEvent("hide_world_sub_scene")end)
    end
    pcall(function()self:hide_panel("merchant_panel")end)
end

function A:close_daily_windows()
    if scene_manager:GetCurrentSceneName()~="world" then return end
    pcall(function()self:drain_fast_settlements()end)
    self:close_daily_market()
    self:close_refine_game()
    pcall(function()graphic:DispatchEvent("hide_all_sub_panel")end)
    local ok,context=pcall(function()return self:read_context()end)
    local scene=ok and context and context.scene
    if scene then
        if type(scene.HideAllSubPanels)=="function" then pcall(function()scene:HideAllSubPanels()end) end
        for _,key in ipairs({"msgbox","jump_panel","floating_panel","alert_panel"})do
            local panel=scene[key]
            if panel and type(panel.Hide)=="function" then pcall(function()panel:Hide()end) end
        end
    end
    for _,name in ipairs({"reward_panel","vanity_adventure_result","confirm_msgbox"})do pcall(function()self:hide_panel(name)end) end
    if scene and type(scene.ShowMainSubScene)=="function" then pcall(function()scene:ShowMainSubScene()end)
    else pcall(function()graphic:DispatchEvent("show_world_sub_scene","main_sub_scene")end) end
    -- Returning to the main scene can itself dispatch a queued reward or prompt.
    -- Close those synchronously as well; never confirm or purchase anything.
    pcall(function()graphic:DispatchEvent("hide_all_sub_panel")end)
    if scene then
        if type(scene.HideAllSubPanels)=="function" then pcall(function()scene:HideAllSubPanels()end) end
        for _,key in ipairs({"msgbox","jump_panel","floating_panel","alert_panel"})do
            local panel=scene[key]
            if panel and type(panel.Hide)=="function" then pcall(function()panel:Hide()end) end
        end
    end
end
local base_daily_step=A.daily_step

local daily_extra_rows={
    {"adventure_claim","冒险探险结果"},{"market_exchange","集市兑换"},{"mystery_shop_enabled","神秘商店"},{"mail_enabled","邮件"},{"rewards","活跃度奖励"},
    {"activity_rewards","活动奖励"},{"gold_enabled","金币召唤"},{"refine_enabled","炼化"},
    {"gift_all","好友点数"},{"checkin_all","全时段签到"},{"vip_reward","月卡奖励"},
    {"prayer","免费祈祷"},{"alchemy","每日炼金"},{"guild_boss","工会首领"},
    {"events_enabled","合战与远征"},{"void_challenge","虚空挑战"},{"void_exchange","虚空兑换"},
    {"mine_guards","矿山护卫"},{"include_mine","矿山采集与研究"},
}
local function daily_report_day()
    return math.floor((time_logic:Now()+(tonumber(time_logic.time_zone_offset) or 28800))/86400)
end

function A:game_day_start()
    local now=tonumber(time_logic:Now())
    if not now or now<=0 then return end
    local offset=tonumber(time_logic.time_zone_offset) or 28800
    local day=math.floor((now+offset)/86400)*86400-offset
    if type(time_logic.GetNowDayTime)=="function" then
        local ok,legacy_day=pcall(time_logic.GetNowDayTime,time_logic)
        if ok and tonumber(legacy_day) and tonumber(legacy_day)>0 then day=tonumber(legacy_day) end
    end
    return day
end

function A:encounter_identity()
    local user=self:harvest_account()
    local login=package.loaded["logic.login"]
    local server=login and tonumber(login.server_id)
    if not server or server<=0 or user=="" or user=="0" then return end
    local day=self:game_day_start()
    if day then return tostring(server)..":"..user,day end
end

function A:set_encounter_clear_days(days)
    self.encounter_clear_days=days
    return true
end

function A:start_daily_encounter_resume(role)
    self:cancel_task()
    self.daily_state={running=true,selected={[11]=true},skipped={},claims=0,
        completion_phase="close",completion_result={done=true,message="日常执行结束"},
        encounter_clear={role=role}}
    self:install_daily_spend_guard()
    return true
end

local function treasure_active()
    local function active(event)
        return type(event)=="table" and event.event_type==RANDOM_TYPE.treasure_hunt
            and (event.end_time==nil or (tonumber(event.end_time) or 0)>time_logic:Now())
    end
    if active(mining.pending_random_event) or active(mining.show_random_event) then return true end
    for _,event in pairs(mining.random_event_list or {}) do
        if active(event) then return true end
    end
    return false
end

local function encounter_wait(self,message,delay)
    local clear=self.daily_state.encounter_clear
    return reply(self,message,{wait=true,phase="encounter",encounterRole=clear.role,delaySeconds=delay})
end

function A:daily_encounter_step(st)
    if not st.selected or not st.selected[11] or self.cfg.mobileNoMining or st.encounter_done then return end
    local clear=st.encounter_clear
    if not clear then
        if self.cfg.enabled then self:set("enabled",false) end
        clear={}
        st.encounter_clear=clear
    end
    local context=self:read_context()
    if not context then
        if not self.inBattle then
            clear.entered=nil;clear.confirmed=nil;clear.zero_confirmed=nil
            local pending=(self.mine_refill and self.mine_refill.uncertain) or self._task_pending
            if pending and not pending.response and not pending.event:match("^query_") then
                self._cancelled_task_pending=pending
            end
            self._task_pending=nil;self._mine_event=nil;self.mine_refill=nil
        end
        return encounter_wait(self,"奇遇清空 · 等待游戏连接或战斗结束",0.5)
    end
    local role,day=self:encounter_identity()
    if not role then return encounter_wait(self,"奇遇清空 · 等待区服、角色与游戏日同步",0.5) end
    if clear.role and clear.role~=role then
        st.skipped[11]="角色已切换，奇遇清空未完成"
        st.encounter_done=true
        return
    end
    clear.role=role
    if day~=clear.day then
        clear.day=day;clear.confirmed=nil;clear.zero_confirmed=nil
        if self._task_pending and self._task_pending.kind=="clear_encounter_read" then self._task_pending=nil end
    end
    if (self.encounter_clear_days or {})[role]==day then
        st.encounter_done=true
        st.encounter_success=true
        st.encounter_message="今日已完成，直接跳过"
        return
    end
    if self.inBattle or self.fight then
        if self.fight and not self.inBattle then self:update_fight() end
        return encounter_wait(self,"奇遇清空 · 等待战斗结算")
    end
    if context.sub_name~="mining_district_sub_scene" then
        clear.entered=nil;clear.confirmed=nil;clear.zero_confirmed=nil
        if task_now(self)>=(clear.enter_at or 0) then
            clear.enter_at=task_now(self)+2
            mining:QueryBlockInfo()
        end
        return encounter_wait(self,"奇遇清空 · 正在进入矿区",0.15)
    end
    if not clear.entered then clear.entered=true;clear.confirmed=nil;clear.zero_confirmed=nil end

    local pending=self._task_pending
    if pending and pending.kind=="clear_encounter_read" then
        if not pending.response and task_now(self)-pending.sent_at<5 then
            return encounter_wait(self,"奇遇清空 · 等待矿区状态确认",0.1)
        end
        self._task_pending=nil
        local message=pending.response
        if not message or message.result and message.result~="success" or tonumber(message.event_count)==nil then
            return encounter_wait(self,"奇遇清空 · 重新读取矿区状态",0.5)
        end
        clear.confirmed=true
        clear.zero_confirmed=tonumber(message.event_count)==0
    end

    local remaining=tonumber(mining.event_count)
    local has_treasure=treasure_active()
    local settled=not self._task_pending and not self._mine_event and not self._mine_blast and not self.mine_refill
        and not self.pending_dig
    if settled and (not clear.confirmed or remaining==nil
        or remaining==0 and not has_treasure and not clear.zero_confirmed) then
        local barrier=self:task_barrier()
        if barrier then return encounter_wait(self,barrier.message,0.1) end
        self:task_request("query_mining_info_ret","clear_encounter_read",function()
            return network:Send({query_mining_info={}})
        end)
        return encounter_wait(self,"奇遇清空 · 正在刷新矿区状态",0.1)
    end
    if clear.confirmed and clear.zero_confirmed and remaining==0 and not has_treasure
        and type(mining.random_event_list)=="table" and settled then
        st.encounter_done=true
        st.encounter_success=true
        st.encounter_message="今日奇遇剩余 0 次，藏宝图已结束"
        self.encounter_clear_days=self.encounter_clear_days or {}
        self.encounter_clear_days[role]=day
        st.completion_result.encounterClearDone={role=role,day=day}
        return
    end
    if not clear.confirmed then
        return encounter_wait(self,"奇遇清空 · 等待上一次矿区操作结算",0.1)
    end
    if remaining==0 and not has_treasure then
        if self.pending_dig then self:pending_dig_resolved() end
        if self._mine_blast then self:mine_blast_step() end
        if self.mine_refill then self:mine_bag_step() end
        if self._mine_event and self._mine_event.error then
            st.skipped[11]=self._mine_event.error;st.encounter_done=true
            return
        end
        if settled then clear.zero_confirmed=nil end
        return encounter_wait(self,"奇遇清空 · 等待当前矿区操作和藏宝图状态同步",0.15)
    end
    if remaining~=0 or has_treasure then clear.zero_confirmed=nil end
    local enabled,auto_mine=self.cfg.enabled,self.cfg.autoMine
    self.cfg.enabled=true;self.cfg.autoMine=true
    local ok,err=pcall(function()self:step()end)
    self.cfg.enabled=enabled;self.cfg.autoMine=auto_mine
    if not ok or (self.mine_refill and self.mine_refill.unavailable) or (self._mine_event and self._mine_event.error) then
        st.skipped[11]=not ok and tostring(err) or self.mine_refill and self.mine_refill.error or self._mine_event.error
        st.encounter_done=true
        return
    end
    pcall(function()self:follow_mining_camera()end)
    return encounter_wait(self,"奇遇清空 · 剩余 "..tostring(remaining or "待确认").." 次 · "..tostring(self.status or "自动挖掘"))
end

function A:start_daily_unfinished(account,day,scope)
    if self:harvest_account()~=account or daily_report_day()~=day then
        return false
    end
    self:start_daily(true)
    local st=self.daily_state
    for id=1,15 do st.selected[id]=st.selected[id] and scope[tostring(id)]==true end
    for _,row in ipairs(daily_extra_rows) do st[row[1]]=st[row[1]] and scope[row[1]]==true end
    return true
end

function A:daily_report(stopped)
    local st=self.daily_state
    if not st or st.cart_followup or not st.selected then return nil end
    local rows={}
    local function row(key,name,status,detail,retry)
        rows[#rows+1]={key=tostring(key),name=name,status=status,detail=detail or "",retry=retry==true}
    end
    local daily=require("logic.daily")
    local chosen=st.retry_active and st.retry_active.selected or st.selected
    for id=1,15 do
        if chosen[id] then
            local conf=daily_conf(id)
            local item=daily_item(daily,id)
            local name=conf and conf.active_title or "任务 "..id
            local done=item and daily:CheckActivityComplet(id,item.completion_count or 0)
            local detail=st.skipped[id]
            local retry=id~=7 and id~=14 and id~=13
            if id==11 then
                name="奇遇清空";done=st.encounter_success;detail=st.encounter_message or detail or "尚未确认清空"
                retry=not self.cfg.mobileNoMining
            end
            if id==2 then
                name="工坊收取与生产"
                done=st.workshop_done and not detail
                detail="收取 "..(st.workshop_collected or 0).." 项，开工 "..(st.workshop_produced or 0).." 项"..
                    (detail and " · "..detail or "")
            end
            if id==9 or id==10 then
                local kinds=self:daily_cave_kinds(id)
                local complete=0
                for _,kind in ipairs(kinds) do
                    local left=tonumber((mining.cave_challenge_nums or {})[kind])
                    local buy=tonumber((mining.cave_buy_challenge_nums or {})[kind])
                    if left==0 and buy==0 then complete=complete+1 end
                    detail=(st.cave_skips or {})[kind] or detail
                end
                done=#kinds>0 and complete==#kinds
                detail=#kinds==0 and "今日无开放项目或未同步" or
                    (complete.."/"..#kinds.." 个开放项目次数用完"..(detail and " · "..detail or ""))
                retry=#kinds>0 or stopped
            end
            if (id==7 or id==14) and st.carts and st.carts[id] then done=st.carts[id].done end
            local status=done and "已处理" or retry and "待补办" or "已跳过"
            row(id,name,status,detail or (item and "当前次数 "..(item.completion_count or 0) or "等待同步"),not done and retry)
        end
    end
    for _,spec in ipairs(daily_extra_rows) do
        local key,name=spec[1],spec[2]
        local enabled=key=="include_mine" and st.retry_active and st.retry_active.include_mine or st[key]
        if enabled then
            local status,detail,retry="已核对",nil,false
            local tail=st.reward_tail or {}
            if key=="market_exchange" then
                local m=st.market or {}
                local list=m.snapshot and m.snapshot.order_list
                local done,remaining=0,0
                if list then
                    for _,order in ipairs(list) do
                        if order.is_done==true then done=done+1
                        else
                            local allowed,why=self:daily_market_order_allowed(order)
                            if allowed or (why and why:sub(1,#"缺")=="缺") then remaining=remaining+1 end
                            detail=detail or why
                        end
                    end
                end
                retry=not list or remaining>0 or bonus_ready(m.snapshot)
                status=retry and "待补办" or "已处理"
                detail=(list and ("兑换 "..done.."/"..#list.." · Bonus "..(m.bonus and "已领取" or "未达成")) or "订单待同步")
                    ..(detail and " · "..detail or "")
            elseif key=="mystery_shop_enabled" then
                local result=st.mystery_shop and st.mystery_shop.result
                detail=result and result.message or (st.completion_notes or {}).mystery_shop
                status=result and result.partial and "部分完成" or (detail and "已核对" or "待核对")
            elseif key=="mail_enabled" then
                retry=not tail.mail_checked or tail.mail_retry==true
                detail="领取 "..st.mail_claimed.." 封"
            elseif key=="adventure_claim" then
                status=st.adventure_done and "已处理" or "待核对"
                detail=st.adventure_summary
                retry=not st.adventure_done
            elseif key=="rewards" then
                retry=not tail.live_loaded or tail.liveness_retry==true
                detail="本轮领取 "..st.claims.." 份任务或活跃度奖励"
            elseif key=="activity_rewards" then
                retry=not st.activity_done or tail.activity_retry==true
                detail="领取 "..st.activity_claims.." 份"
            elseif key=="gold_enabled" then
                status=st.gold_summons>=4 and "已处理" or "条件不足"
                detail="金币召唤 "..st.gold_summons.."/4"
            elseif key=="gift_all" then
                status=st.gifts_done and "已核对" or "待核对"
                detail="已赠送 "..st.gifts_sent.." 次"
            elseif key=="checkin_all" then
                status=st.checkin_stop and "已核对" or "待核对"
                detail="完成签到 "..(st.checkin_claims or 0).." 次"
            elseif key=="refine_enabled" then
                status=st.refine_done and "已核对" or "待核对"
                detail=st.refine_summary or "按库存从多到少加料"
            elseif key=="vip_reward" then
                status=st.vip_done and "已核对" or "待核对"
            elseif key=="prayer" or key=="alchemy" then
                local info=daily[key.."_info"]
                status=info and tonumber(info.mark)~=0 and "已处理" or "见运行记录"
            elseif key=="mine_guards" then
                status=st.guard_summary and "已处理" or "待核对"
                detail=st.guard_summary
            else
                local notes=st.report_notes or {}
                status=notes[key] and "已核对" or "见运行记录"
                detail=notes[key]
            end
            if retry then status="待补办" end
            row(key,name,status,detail,retry)
        end
    end
    return {account=self:harvest_account(),day=st.day or daily_report_day(),rows=rows}
end

function A:daily_retry_items(st)
    if not st.retry_active then
        if (not st.retry_ids or next(st.retry_ids)==nil) and (not st.retry_flags or next(st.retry_flags)==nil) then return {done=true,message=""} end
        st.retry_active={selected=st.selected,include_mine=st.include_mine}
        st.selected={};for id=1,15 do st.selected[id]=(st.retry_ids or {})[id]==true end
        st.retry_ids={};st.skipped={};st.stalls={};st.attempts={}
        for flag in pairs(st.retry_flags or {})do
            if flag=="gold" then st.gold_done=false
            elseif flag=="refine" then st.refine_done=false;st.refine_loaded=false
            elseif flag=="gift" then st.gifts_done=false;st.gifts_sent=0
            elseif flag=="checkin" then st.checkin_stop=false
            elseif flag=="vip" or flag=="vip_query" then st.vip_done=false end
        end
        st.retry_flags={}
        st.loaded={};st.needs_sync=true;st.include_mine=false
        st.child_mine=false;st.child_guild=false;st.child_void=false;st.child_void_challenge=false;st.child_events=false;st.child_arena=false
    end
    st.running=true
    local r=base_daily_step(self)
    if r and r.done then
        local saved=st.retry_active;st.selected=saved.selected;st.include_mine=saved.include_mine;st.retry_active=nil;st.running=true
        return {done=true,retry=(st.retry_ids and next(st.retry_ids)~=nil) or (st.retry_flags and next(st.retry_flags)~=nil),message="失败日常项目已重新核对"}
    end
    return r
end
function A:daily_retry_children(st)
    local run=st.retry_children_run
    if not run then
        run={queue={},index=1};st.retry_children_run=run
        for _,key in ipairs({"events","void","exchange","mine"})do if (st.retry_children or {})[key] then run.queue[#run.queue+1]=key end end
    end
    local key=run.queue[run.index]
    if not key then return {done=true,retry=st.retry_children and next(st.retry_children)~=nil,message=""} end
    if not run.active then
        st.retry_children[key]=nil
        self.daily_state=nil
        if key=="events" then self:start_daily_events()
        elseif key=="void" then self:start_void()
        elseif key=="exchange" then self:start_void_exchange()
        else self:start_mine_collect() end
        self.daily_state=st;run.active=true
        if key=="events" then st.child_events=true;st.allow_event_purchase=true
        elseif key=="void" then st.child_void_challenge=true
        elseif key=="exchange" then st.child_void=true
        else st.child_mine=true end
    end
    local r
    if key=="events" then r=self:daily_events_step()
    elseif key=="void" then r=self:void_step()
    elseif key=="exchange" then r=self:void_exchange_step()
    else r=self:mine_step() end
    if r and r.done then
        if r.error then st.retry_children[key]=true end
        if key=="events" then st.child_events=false;st.allow_event_purchase=false;self.event_activity_state=nil
        elseif key=="void" then st.child_void_challenge=false;self.void_state=nil
        elseif key=="exchange" then st.child_void=false;self.void_exchange_state=nil
        else st.child_mine=false;self.mine_state=nil end
        run.active=false;run.index=run.index+1
        return reply(self,"已补查日常子队列："..key,{action="队尾补办"})
    end
    return r
end
function A:daily_step()
    local DAILY_COMPLETION_MAX_RETRIES=8
    local st=self.daily_state
    if not st or not st.running then return base_daily_step(self) end
    local refine_gate=self:daily_refine_gate(st)
    if refine_gate then return refine_gate end
    if not st.completion_phase then
        local p=self._task_pending
        if p and not p.response and (p.cave_kind or p.cave_buy_kind)
            and task_now(self)-p.sent_at>20 then
            defer_cave(self,st,p)
        end
        local ok,result=pcall(base_daily_step,self)
        if not ok then result={done=true,error=true,partial=true,message="日常阶段异常："..tostring(result)} end
        if not result or not result.done then return result end
        st.completion_result=result;st.running=true;self.daily_state=st
        st.completion_phase="adventure"
    end
    if self.inBattle then return reply(self,"等待战斗结算后继续日常收尾",{wait=true}) end
    if st.round_at and task_now(self)<st.round_at then return reply(self,"队列已执行一轮，等待补办未确认请求",{wait=true,delaySeconds=math.min(1,st.round_at-task_now(self))}) end
    local phases={"adventure","caves","market","mystery_shop","rewards","retry_items","retry_children","close"}
    for index=1,#phases-1 do
        local phase=phases[index]
        if st.completion_phase==phase then
            local ok,result=pcall(function()
                if phase=="adventure" then
                    if not st.adventure_claim then return {done=true,message=""} end
                    return self:daily_adventure_claim(st)
                elseif phase=="caves" then
                    if not self:daily_enabled(9) and not self:daily_enabled(10) then return {done=true,message=""} end
                    return self:daily_tail_caves(st)
                elseif phase=="market" then
                    if not st.market_exchange then return {done=true,message=""} end
                    return self:daily_market_step(st)
                elseif phase=="mystery_shop" then
                    if not st.mystery_shop_enabled then return {done=true,message=""} end
                    return self:daily_mystery_step(st)
                elseif phase=="rewards" then return self:daily_tail_rewards(st)
                elseif phase=="retry_items" then return self:daily_retry_items(st)
                else return self:daily_retry_children(st) end
            end)
            if not ok then
                self._task_pending=nil
                if phase=="mystery_shop" then
                    result={done=true,partial=true,message="神秘商店已暂停，未确认的额度仍保留："..tostring(result)}
                    st.mystery_shop_enabled=false
                else
                    result={done=true,retry=true,partial=true,message="收尾阶段暂不可用，将自动补办："..tostring(result)}
                end
            end
            if not result.done then return reply(self,result.message,result) end
            if phase=="market" and st.market_exchange and not st.mystery_shop_enabled then self:close_daily_market() end
            if phase=="mystery_shop" and (st.mystery_shop_enabled or st.mystery_shop) then self:close_daily_market() end
            if result.retry then
                st.completion_retry=true
                st.completion_retry_notes=st.completion_retry_notes or {}
                st.completion_retry_notes[phase]=result.message~="" and result.message or "状态未确认"
            end
            st.completion_notes=st.completion_notes or {}
            if result.message and result.message~="" and not result.retry and st.completion_notes[phase]~=result.message then
                st.completion_notes[phase]=result.message
                st.completion_result.message=(st.completion_result.message or "日常结束").."；"..result.message
            end
            st.completion_result.partial=st.completion_result.partial or result.partial or result.error
            if self._task_pending and not self._task_pending.response then
                local pending=self._task_pending
                if not pending.event:match("^query_") then self._cancelled_task_pending=pending end
                st.completion_retry=true;self._task_pending=nil
            end
            st.completion_phase=phases[index+1]
        end
    end
    if st.completion_retry and (st.retry_round or 0)<DAILY_COMPLETION_MAX_RETRIES then
        st.completion_retry_notes=nil
        st.retry_round=(st.retry_round or 0)+1;st.round_at=task_now(self)+math.min(10,st.retry_round)
        st.completion_retry=false;st.completion_phase="adventure";st.cave_tail=nil;st.reward_tail=nil;st.retry_children_run=nil
        st.adventure=nil
        if st.market then st.market.snapshot=nil;st.market.tried={};st.market.bonus_attempted=nil end
        return reply(self,"本轮已结束，自动补办未确认请求（第"..st.retry_round.."轮，可手动停止）",{wait=true})
    end
    if st.completion_retry then
        st.completion_retry=false
        st.completion_result.partial=true
        local notes={}
        for _,phase in ipairs(phases) do
            local note=(st.completion_retry_notes or {})[phase]
            if note then notes[#notes+1]=note end
        end
        local detail=#notes>0 and "（"..table.concat(notes,"；").."）" or ""
        st.completion_result.message=(st.completion_result.message or "日常结束").."；部分收尾仍待核对，已继续后续任务"..detail
    end
    if not st.encounter_clear then
        if self.inBattle then return reply(self,"等待战斗结算后关闭日常窗口",{wait=true}) end
        local refine_schedule=self:daily_refine_schedule(st)
        if refine_schedule then return refine_schedule end
        self:close_daily_windows()
    end
    local ok_clear,clear_result=pcall(function()return self:daily_encounter_step(st)end)
    if ok_clear and clear_result then return clear_result end
    if not ok_clear then st.skipped[11]="矿区状态读取失败："..tostring(clear_result) end
    if st.skipped[11] then
        st.completion_result.partial=true
        st.completion_result.message=(st.completion_result.message or "日常结束").."；奇遇清空未完成："..st.skipped[11]
    elseif st.encounter_success then
        st.completion_result.message=(st.completion_result.message or "日常结束").."；奇遇清空："..st.encounter_message
    end
    if st.encounter_clear then self:close_daily_windows() end
    st.running=false
    local ok,report=pcall(function()return self:daily_report(false)end)
    if ok then st.completion_result.dailyReport=report
    else self:log("日常结果单汇总失败："..tostring(report)) end
    st.completion_result.cartReview=st.carts and st.carts[7] and st.carts[7].review
    if st.guild_boss_done_record then st.completion_result.guildBossDone=st.guild_boss_done_record end
    st.completion_result.message=(st.completion_result.message or "日常结束").."；已关闭残留窗口并返回主界面"
    return reply(self,st.completion_result.message,st.completion_result)
end
