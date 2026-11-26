do
local function white()
    return (constants.MERCHANT_TYPE or {}).WHITE or 2
end

local function positive(value)
    local n=tonumber(value)
    return n and n>0 and n<=2147483647 and n==math.floor(n) and n or nil
end

local function product(reward)
    if type(reward)~="table" then return end
    local kind=positive(reward.reward_type)
    local item=positive(reward.param1)
    local quantity=positive(reward.param2)
    if not kind then return end
    local types=constants.REWARD_TYPE or {}
    if not item and (kind==(types.resource or 2) or kind==(types.item or 1)
        or kind==(types.mercenary or 8)) then return end
    return kind..":"..(item or 0),quantity or 1
end

local function price_of_order(order)
    local blood=(constants.RESOURCE_TYPE or {}).blood_diamond
    if not blood then return end
    local cost=0
    for i=1,2 do
        local id=tonumber(order["need_type"..i]) or 0
        local count=tonumber(order["need_num"..i]) or 0
        if id~=0 or count~=0 then
            if id~=blood or not positive(count) then return end
            cost=cost+count
        end
    end
    return positive(cost)
end

local function merchant_module()
    local ok,merchant=pcall(require,"logic.merchant")
    return ok and type(merchant)=="table" and merchant or nil
end

local function white_orders(snapshot)
    local orders={}
    for _,order in ipairs(snapshot.order_list) do
        if type(order)=="table" and tonumber(order.merchant_type)==white() then orders[#orders+1]=order end
    end
    table.sort(orders,function(a,b) return (tonumber(a.order_id) or 0)<(tonumber(b.order_id) or 0) end)
    return orders
end

local function complete_snapshot(msg)
    return type(msg)=="table" and (not msg.result or msg.result=="success" or msg.result==0)
        and type(msg.order_list)=="table" and (tonumber(msg.reset_time) or 0)>0
        and tonumber(msg.buy_refresh_times) and tonumber(msg.buy_refresh_times)>=0
end

local function exchange_success(msg)
    return msg and (msg.result=="success" or msg.result==0)
end

local function current_account(self)
    local id=self:harvest_account()
    return type(id)=="string" and id:match("^%d+$") and id:find("[1-9]") and id or nil
end

function A:set_mystery_products(keys)
    local selected={}
    for _,key in ipairs(keys or {}) do
        if type(key)=="string" and key:match("^%d+:%d+$") then selected[key]=true end
    end
    self.mystery_products=selected
end

function A:mystery_budget_ack(token,operation,status,remaining)
    local daily=self.daily_state
    local work=daily and daily.mystery_shop
    local charge=work and work.charge
    if not charge or charge.token~=token or charge.operation~=operation then return false end
    charge.ack=status
    remaining=tonumber(remaining)
    if remaining and remaining>=0 then work.remaining[charge.kind]=remaining end
    return true
end

local function budget_wait(self,charge)
    local message=charge.operation=="reserve" and "核对神秘商店今日预算" or "归还未使用的神秘商店额度"
    return reply(self,message,{wait=true,mysteryBudget={
        token=charge.token,operation=charge.operation,kind=charge.kind,
        amount=charge.amount,account=charge.account,day=charge.day,
    }})
end

local function selected_order(self,order)
    if not self.mystery_products or order.is_done==true or not positive(order.order_id)
        or type(order.reward_info)~="table" then return false end
    local rewards=order.reward_info
    if #rewards==0 then return false end
    for _,reward in ipairs(rewards) do
        local key=product(reward)
        if not key or not self.mystery_products[key] then return false end
    end
    return true
end

local function configured_budget(self,kind)
    return tonumber(self.cfg[kind=="buy" and "mysteryBuyBudget" or "mysteryRefreshBudget"]) or 0
end

local function current_price()
    local merchant=merchant_module()
    if not merchant or type(merchant.GetResetPrice)~="function" then return end
    local ok,value=pcall(merchant.GetResetPrice,merchant,white())
    return ok and positive(value) or nil
end

local function authorized(self,work,charge)
    if not self.cfg.dailyMysteryShop or self:guild_boss_today()~=charge.day
        or current_account(self)~=charge.account or configured_budget(self,charge.kind)<charge.cap then return false end
    if charge.kind=="buy" then
        if not selected_order(self,charge.order) or price_of_order(charge.order)~=charge.amount then return false end
    elseif current_price()~=charge.amount then return false end
    local merchant=merchant_module()
    if merchant and merchant.buy_refresh_times~=nil
        and tonumber(merchant.buy_refresh_times)~=tonumber(work.snapshot.buy_refresh_times) then return false end
    if merchant and merchant.reset_time and tonumber(merchant.reset_time)>0
        and tonumber(merchant.reset_time)~=tonumber(work.snapshot.reset_time) then return false end
    if charge.kind=="buy" and merchant and type(merchant.GetOrderInfo)=="function" then
        local ok,current=pcall(function()
            for i=1,merchant:GetOrderNum(white()) do
                local order=merchant:GetOrderInfo(i,white())
                if tonumber(order.order_id)==tonumber(charge.order.order_id) then return order end
            end
        end)
        if not ok or not current or not selected_order(self,current) or price_of_order(current)~=charge.amount then return false end
        local original=charge.order.reward_info
        if #current.reward_info~=#original then return false end
        for i,reward in ipairs(current.reward_info) do
            local current_key,current_count=product(reward)
            local expected_key,expected_count=product(original[i])
            if current_key~=expected_key or current_count~=expected_count then return false end
        end
    end
    local blood=constants.RESOURCE_TYPE.blood_diamond
    return (tonumber(resource_logic:GetResourceNum(blood)) or 0)>=charge.amount
end

local function finish(self,work,message,partial)
    work.finished=true
    local text=string.format("神秘商店购买 %d 笔（%d 血钻），刷新 %d 次（%d 血钻）",
        work.purchases,work.buy_spend,work.refreshes,work.refresh_spend)
    if message then text=text.." · "..message end
    work.result={done=true,partial=partial,message=text}
    return work.result
end

local function refund(self,charge,stop)
    charge.operation="refund"
    charge.ack=nil;charge.at=self.now;charge.stop=stop
    return budget_wait(self,charge)
end

local function record(self,work,charge)
    if charge.kind=="buy" then
        work.purchases=work.purchases+1;work.buy_spend=work.buy_spend+charge.amount
        charge.order.is_done=true
    else
        work.refreshes=work.refreshes+1;work.refresh_spend=work.refresh_spend+charge.amount
        work.tried={}
    end
    self:spend_record(charge.amount,charge.kind=="buy" and "神秘商店购买" or "神秘商店刷新")
end

local function reserve(self,work,kind,amount,order)
    self._mystery_seq=(self._mystery_seq or 0)+1
    local charge={kind=kind,amount=amount,token=self._mystery_seq,order=order,
        account=work.account,day=work.day,operation="reserve",at=self.now,cap=configured_budget(self,kind)}
    work.charge=charge
    return budget_wait(self,charge)
end

local function send_paid(self,work,charge)
    charge.operation="sent";charge.at=self.now
    if charge.kind=="buy" then
        local id=tonumber(charge.order.order_id)
        self:bounded_request("merchant_exchange_ret","daily_mystery","merchant_exchange",{merchant_type=white(),order_id=id},
            "购买神秘商店第 "..id.." 笔订单",function(msg)
                return (tonumber(msg.order_id)==id or msg.order_id==nil and not exchange_success(msg))
                    and (msg.merchant_type==nil or tonumber(msg.merchant_type)==white())
            end)
    else
        self:bounded_request("merchant_reset_ret","daily_mystery","merchant_reset",{merchant_type=white()},"刷新神秘商店")
    end
    return reply(self,"等待神秘商店回执",{wait=true})
end

local function handle_reply(self,work)
    local pending=self._task_pending
    if not pending then return end
    if pending.kind~="daily_mystery" then return reply(self,"等待游戏通道空闲",{wait=true}) end
    local state=self:bounded_request_state(pending,4)
    if state=="waiting" then return reply(self,"等待神秘商店回执",{wait=true}) end
    self._task_pending=nil
    local charge=work.charge
    if pending.command=="query_merchant" then
        if state~="response" or not complete_snapshot(pending.response) then
            return finish(self,work,"未能确认最新订单",true)
        end
        local snapshot=pending.response
        if charge then
            local same_day=tonumber(snapshot.reset_time)==tonumber(work.snapshot.reset_time)
            local change=tonumber(snapshot.buy_refresh_times)-tonumber(work.snapshot.buy_refresh_times)
            local confirmed=charge.kind=="refresh" and same_day and change==1
            if charge.kind=="buy" and same_day and change==0 then
                for _,order in ipairs(white_orders(snapshot)) do
                    if tonumber(order.order_id)==tonumber(charge.order.order_id) and order.is_done==true then confirmed=true end
                end
            end
            if not confirmed then return finish(self,work,"上次操作未确认，所需额度保留",true) end
            record(self,work,charge);work.charge=nil
        end
        work.snapshot=snapshot
    elseif state=="unsent" then return refund(self,charge,"游戏通道未接受操作")
    elseif state=="response" then
        local msg=pending.response
        if msg.result~=nil and not exchange_success(msg) then
            return refund(self,charge,"游戏拒绝操作："..tostring(msg.result))
        end
        if exchange_success(msg) and charge.kind=="buy" then
            record(self,work,charge);work.charge=nil
        elseif exchange_success(msg) and complete_snapshot(msg)
            and tonumber(msg.buy_refresh_times)==tonumber(work.snapshot.buy_refresh_times)+1
            and tonumber(msg.reset_time)==tonumber(work.snapshot.reset_time) then
            record(self,work,charge);work.charge=nil;work.snapshot=msg
        else charge.operation="verify";charge.at=self.now end
    else charge.operation="verify";charge.at=self.now end
end

function A:daily_mystery_step(st)
    local work=st.mystery_shop
    if not work then
        local account=current_account(self)
        if not account then return {done=true,partial=true,message="神秘商店等待账号就绪"} end
        if configured_budget(self,"buy")<=0 and configured_budget(self,"refresh")<=0 then
            return {done=true,message="神秘商店买货及刷新额度均为 0"}
        end
        if (not self.mystery_products or not next(self.mystery_products)) and configured_budget(self,"refresh")<=0 then
            return {done=true,message="神秘商店尚未勾选商品"}
        end
        work={account=account,day=self:guild_boss_today(),purchases=0,refreshes=0,
            buy_spend=0,refresh_spend=0,remaining={},tried={},at=self.now}
        local market=st.market
        if market and market.snapshot_at and self.now-market.snapshot_at<=2
            and complete_snapshot(market.snapshot) then
            work.snapshot=market.snapshot
        end
        st.mystery_shop=work
    end
    if work.finished then return work.result end
    local handled=handle_reply(self,work)
    if handled then return handled end
    local charge=work.charge
    if charge and charge.operation=="refund" then
        if not charge.ack and self.now-charge.at<=5 then return budget_wait(self,charge) end
        return finish(self,work,charge.ack=="ok" and charge.stop or "额度存档暂未确认",true)
    end
    if current_account(self)~=work.account or self:guild_boss_today()~=work.day then
        if charge and charge.operation=="reserve" and charge.ack=="ok" then return refund(self,charge,"账号或日期已切换") end
        return finish(self,work,"账号或日期已切换",true)
    end
    if charge and charge.operation=="reserve" then
        if self.now-charge.at>5 then return finish(self,work,"无法确认本地预算，暂停采购",true) end
        if not charge.ack then return budget_wait(self,charge) end
        if charge.ack=="cap" then
            if charge.kind=="refresh" then return finish(self,work,"下一次刷新超出剩余额度") end
            work.tried[charge.order.order_id]=true;work.charge=nil
        elseif charge.ack~="ok" then return finish(self,work,"本地预算存档不可用",true)
        elseif not authorized(self,work,charge) then return refund(self,charge,"商品、报价或余额已变化")
        elseif not self:request_channel_available() then return reply(self,"等待游戏通道空闲",{wait=true})
        else return send_paid(self,work,charge) end
    end
    if not work.snapshot or (work.charge and work.charge.operation=="verify") then
        local since=work.charge and work.charge.at or work.at
        if self.now-since>5 then return finish(self,work,"未能读取神秘商店当前状态",true) end
        if not merchant_module() or not self:request_channel_available() then return reply(self,"等待神秘商店数据",{wait=true}) end
        self:install_task_hooks()
        if not self._task_hooks.query_merchant_ret or not self._task_hooks.merchant_reset_ret
            or not self._task_hooks.merchant_exchange_ret then return reply(self,"等待神秘商店接口就绪",{wait=true}) end
        self:bounded_request("query_merchant_ret","daily_mystery","query_merchant",{},"读取神秘商店")
        return reply(self,"读取神秘商店订单",{wait=true})
    end
    if tonumber(work.snapshot.reset_time)<=time_logic:Now() then
        return finish(self,work,"商店已自然换批",true)
    end
    local balance=tonumber(resource_logic:GetResourceNum(constants.RESOURCE_TYPE.blood_diamond)) or 0
    local buying=math.min(work.remaining.buy or math.huge,configured_budget(self,"buy"))
    for _,order in ipairs(white_orders(work.snapshot)) do
        local price=price_of_order(order)
        if price and price<=buying and price<=balance and not work.tried[order.order_id]
            and selected_order(self,order) then return reserve(self,work,"buy",price,order) end
    end
    local allowance=math.min(work.remaining.refresh or math.huge,configured_budget(self,"refresh"))
    if allowance<=0 then return finish(self,work,"刷新额度已用完或设为 0") end
    local price=current_price()
    if not price then return finish(self,work,"未读取到刷新报价",true) end
    if price>balance then return finish(self,work,"血钻余额不足以继续刷新") end
    if price>allowance then return finish(self,work,"下一次刷新超出剩余额度") end
    return reserve(self,work,"refresh",price)
end

local previous_guard=A.daily_message_is_free
function A:daily_message_is_free(key,payload)
    local st=self.daily_state
    local work=st and st.mystery_shop
    local charge=work and work.charge
    if st and st.completion_phase=="mystery_shop" and (key=="merchant_exchange" or key=="merchant_reset") then
        local pending=self._task_pending
        if not pending or pending.kind~="daily_mystery" or not charge or charge.operation~="sent"
            or charge.ack~="ok" or tonumber(payload.merchant_type)~=white() then return false end
        if key=="merchant_exchange" then return charge.kind=="buy" and tonumber(payload.order_id)==tonumber(charge.order.order_id) end
        return charge.kind=="refresh"
    end
    if key=="merchant_reset" then return false end
    return previous_guard(self,key,payload)
end

A.DAILY_EVENTS[#A.DAILY_EVENTS+1]="merchant_reset_ret"
end
