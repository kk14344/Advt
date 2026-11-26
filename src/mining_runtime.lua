-- Ordinary digging refills only to its current threshold. Daily and workshop
-- production retain their planned batch refill.
local batch_mining_refill=A.try_use_mining_bag
local previous_mining_response=A.mining_on_response
local previous_random_event=A.try_refuse_event

function A:automatic_mining_start_depth()
    return math.floor(math.max(1,math.min(9999,tonumber(self.cfg.horizontalStartDepth) or 150)))
end
function A:automatic_mining_depth_ready()
    return mining.cur_position~=nil and (tonumber(mining.cur_position.y) or 0)>=self:automatic_mining_start_depth()
end

local function in_daily(self)
    return self.daily_state and self.daily_state.running and not self.daily_state.encounter_clear
end

function A:try_cave_boss()
    if not self.cfg.autoCaveBoss or self.inBattle or self.fight or self._task_pending
        or (self.daily_state and self.daily_state.running)
        or self.now<(self.next_cave_boss or 0) then return false end
    if mining.cave_boss_lv==nil then
        if self.now>=(self.next_cave_query or 0) and self:request_channel_available() then
            self.next_cave_query=self.now+30
            pcall(function() mining:RefreshRandomEvents() end)
        end
        return false
    end
    local level=tonumber(mining.cave_boss_lv) or 0
    local bp=tonumber(mining.cave_boss_bp) or 0
    local expiry=tonumber(mining.cave_boss_end_time) or 0
    if level<=0 or bp<=0 or (expiry>0 and expiry<=time_logic:Now()) then return false end
    local medal=(constants.RESOURCE_TYPE or {}).demon_medal
    local cost=tonumber(constants.CAVE_BOSS_CHALLANGE_SUB)
    if not medal or not cost or cost<=0 or not self:can_act(2)
        or not self:request_channel_available() then return false end
    local known,eligible=pcall(function()
        return resource_logic:GetResourceNum(medal)>=cost
            and bag_logic:GetSpaceCount()>=(tonumber(constants.EVENT_MAX_BAG_SPACE_COUNT) or 1)
    end)
    if not known or not eligible then self.next_cave_boss=self.now+60;return false end
    self:install_task_hooks()
    if not self._task_hooks.challenge_cave_boss_ret then return false end
    self.next_cave_boss=self.now+(tonumber(self.cfg.caveBossInterval) or 120)
    local pending=self:bounded_request("challenge_cave_boss_ret","mine_cave_boss","challenge_cave_boss",{},
        "挑战矿区副本 BOSS")
    if pending._sent then self:mark_action() end
    return self:advance_auto_cave_boss()
end

function A:advance_auto_cave_boss()
    local p=self._task_pending
    if not p or (p.kind~="mine_cave_boss" and p.kind~="mine_cave_boss_check") then return false end
    local state=self:bounded_request_state(p,p.kind=="mine_cave_boss" and 3 or 1.5)
    if state=="waiting" and self.cfg.autoCaveBoss then
        self.status=p.kind=="mine_cave_boss_check" and "正在核对矿区副本 BOSS" or "等待矿区副本 BOSS 回执"
        self.detail=p._sent and "收到回执后继续挖掘" or "游戏通道暂未接受请求"
        return true
    end
    self._task_pending=nil
    if p.kind=="mine_cave_boss_check" then return false end
    if state=="response" then
        if p.response.result=="success" or p.response.result==0 then
            self.stats.caveBoss=self.stats.caveBoss+1
            self.close_reward_until=self.now+8
            self:log("矿区副本 BOSS 已确认，继续挖掘")
        else
            self:log("矿区副本 BOSS 请求未接受："..tostring(p.response.result))
        end
    elseif p._sent or state=="uncertain" then
        self:log("矿区副本 BOSS 回执未确认，跳过本轮挑战")
        if self:request_channel_available() and self._task_hooks.query_mining_info_ret then
            self:bounded_request("query_mining_info_ret","mine_cave_boss_check","query_mining_info",{},
                "核对矿区副本 BOSS 状态")
            return true
        end
    end
    return false
end
local function event_key(event)
    local pos=event.position or {}
    return table.concat({event.event_type or 0,event.occupy_type or 0,pos.x or 0,pos.y or 0,event.end_time or 0},":")
end
local function same_event(a,b)
    if tonumber(a.event_type)~=tonumber(b.event_type) or (tonumber(a.occupy_type) or 0)~=(tonumber(b.occupy_type) or 0) then return false end
    local pa,pb=a.position,b.position
    return not pa or not pb or (tonumber(pa.x)==tonumber(pb.x) and tonumber(pa.y)==tonumber(pb.y))
end
function A:mine_tool_target(extra)
    local target=math.max(1,(tonumber(mining.golem_num) or 0)+1,tonumber(extra) or 0)
    if self.cfg.refuseEvents==false then target=math.max(41,target) end
    return target
end
function A:mine_refill_unavailable(st,message)
    st.error=message
    st.unavailable=true
    if not (self.daily_state and self.daily_state.encounter_clear) then
        self:set("enabled",false)
        self.status=message
        self:log(message)
    end
end
function A:mine_single_bag_send()
    local st=self.mine_refill
    if not st or self._task_pending then return end
    st.target=self:mine_tool_target(st.extra)
    local have=tonumber(mining.dig_count) or 0
    if have>=st.target then self.mine_refill=nil;return end
    local cap=tonumber(mining.dig_max_count) or 0
    if cap>0 and st.target>cap then
        self:mine_refill_unavailable(st,"矿镐上限不足以维持所需次数")
        return
    end
    if self.cfg.autoUseEarthHeart~=false then
        local ok,bag=pcall(require,"logic.bag")
        if ok and bag then
            local items=type(bag.item_list)=="table" and bag.item_list or nil
            if not st.inventory_read and (not items or self:earth_inventory_unknown()) then
                st.inventory_read=true
                st.phase="inventory"
                self:task_request("query_bag_info_ret","mine_heart_inventory",function()
                    return network:Send({query_bag_info={}})
                end)
                return
            end
            for _,item in pairs(items or {})do
                local id=tonumber(item.item_id)
                if id and tonumber(item.template_id)==6000005 and (tonumber(item.count) or 1)>0
                    then
                    st.error=nil;st.phase="use";st.before=have;st.reads=0;st.unavailable=nil
                    bag.quick_use_item=false
                    local p=self:task_request("use_item_ret","mine_single_heart",function()
                        return network:Send({use_item={item_id=id}})
                    end)
                    p.accept_response=function(msg)return tonumber(msg.item_id)==id end
                    self.status="使用大地之心";self.detail="矿镐 "..have.." / "..st.target
                    return
                end
            end
        end
    end
    local tool=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.ultimate_tool
    local stock=tool and tonumber(resource_logic:GetResourceNum(tool)) or 0
    if not self.cfg.autoUseMiningBag or not tool or stock<1 then
        self:mine_refill_unavailable(st,"矿镐不足，且没有可用的大地之心或矿工包")
        return
    end
    st.error=nil;st.unavailable=nil;st.before=have;st.phase="use";st.reads=0
    local p=self:task_request("use_mining_tool_ret","mine_single_bag",function()
        network:Send({use_mining_tool={resource_id=tool,num=1}})
    end)
    p.accept_response=function(msg)
        return tonumber(msg.resource_id)==tool and tonumber(msg.num)==1
    end
    self.status="使用一个矿工包";self.detail="矿镐 "..have.." / "..st.target
end
function A:mine_bag_query()
    local st=self.mine_refill
    if not st or self._task_pending then return end
    st.phase="verify";st.reads=(st.reads or 0)+1
    self:task_request("query_mining_info_ret","mine_bag_snapshot",function()network:Send({query_mining_info={}})end)
end
function A:mine_bag_step()
    local st=self.mine_refill
    if not st then return false end
    local p=self._task_pending
    if p and not p.response then
        if self.now-p.sent_at>3 and (p.kind=="mine_single_bag" or p.kind=="mine_single_heart") then
            -- Read back state instead of repeating an uncertain use.
            self._task_pending=nil;st.uncertain=p;self:mine_bag_query()
        elseif self.now-p.sent_at>5 and p.kind=="mine_heart_inventory" then
            self._task_pending=nil
            st.phase="failed";st.error="大地之心库存读取超时";st.unavailable=true
        elseif self.now-p.sent_at>5 and p.kind=="mine_bag_snapshot" then
            self._task_pending=nil;st.next_at=self.now+1;st.phase="read_wait"
        end
    elseif not p and self.now>=(st.next_at or 0) then
        if st.phase=="read_wait" then self:mine_bag_query()
        elseif not st.phase then self:mine_single_bag_send() end
    end
    self.status=st.error or "优先使用大地之心补充矿镐，等待次数确认"
    self.detail="当前 "..tostring(mining.dig_count).."，目标 "..self:mine_tool_target(st.extra)
    return true
end
function A:try_use_mining_bag(extra)
    if in_daily(self) then return batch_mining_refill and batch_mining_refill(self) or false end
    local barrier=self:task_barrier()
    if barrier then self.status=barrier.message;return true end
    if self.mine_refill then return self:mine_bag_step() end
    local target=self:mine_tool_target(extra)
    if (tonumber(mining.dig_count) or 0)>=target then return false end
    self.mine_refill={target=target,extra=extra}
    self:mine_single_bag_send()
    return true
end

-- A successful move is also confirmed by the native client's current position.
-- Missing callbacks trigger read-only recovery, never a blind repeated dig.
function A:pending_dig_resolved()
    local p=self.pending_dig
    if not p then return true end
    local pos=mining.cur_position
    if (pos and tonumber(pos.x)==p.x and tonumber(pos.y)==p.y)
        or self.inBattle or self.fight or mining.__mw_pending_monster
        or mining:GetBlockType(p.x,p.y)~=p.block_type
        or (tonumber(mining.dig_endtime) or 0)>(p.game_at or 0)+0.001 then
        self.pending_dig=nil;self._mine_sync=nil;return true
    end
    local pending_wait = math.max(0.8, (tonumber(self.cfg.actionInterval) or 0.03) * 2)
    if self.now-(p.at or self.now)>=pending_wait and not self._task_pending then
        local sync=self._mine_sync or {};self._mine_sync=sync
        if sync.waiting and self.now-(sync.sent_at or 0)>3 then sync.waiting=false;sync.next_at=self.now+0.5 end
        if not sync.waiting and self.now>=(sync.next_at or 0) then
            self:install_task_hooks()
            sync.sent_at=self.now;sync.waiting=true
            network:Send({query_mining_info={}})
        end
    end
    return false
end
function A:mine_sync_response(msg)
    local sync=self._mine_sync;local p=self.pending_dig
    if not sync or not sync.waiting or not p then return end
    local sync_gap = math.max(0.15, (tonumber(self.cfg.actionInterval) or 0.03) * 0.75)
    sync.waiting=false;sync.next_at=self.now+sync_gap
    local pos=msg.cur_position;local ends=tonumber(msg.dig_endtime);local count=tonumber(msg.dig_count)
    if (msg.result and msg.result~="success") or not pos or tonumber(pos.x)==nil
        or tonumber(pos.y)==nil or not ends or not count then sync.idle=nil;return end
    if tonumber(pos.x)==p.x and tonumber(pos.y)==p.y then
        self.pending_dig=nil;self._mine_sync=nil;return
    end
    if ends>time_logic:Now()+0.01 then sync.idle=nil;return end
    local signature=tostring(pos.x)..":"..tostring(pos.y)..":"..count
    if sync.idle==signature then
        -- Two consecutive fresh idle snapshots resolve a rejected/no-op move.
        self.pending_dig=nil;self._mine_sync=nil
        self:log("矿区空闲状态已连续确认，恢复自动挖掘")
    else sync.idle=signature end
end

function A:try_refuse_event()
    if in_daily(self) then return previous_random_event and previous_random_event(self) or false end
    if self._mine_event then
        self.status=self._mine_event.error and ("奇遇处理失败："..self._mine_event.error) or "等待奇遇处理确认"
        return true
    end
    local event=mining.pending_random_event or mining.show_random_event
    if type(event)~="table" or event.event_type==nil then return false end
    if type(mining.IsPendingRandomEvent)=="function" and not mining:IsPendingRandomEvent(event) then return false end
    local key=event_key(event)
    if key==self._mine_event_done then return false end
    local costly=event.event_type==RANDOM_TYPE.occupancy or (tonumber(event.pick_cost) or 0)>0
    local operation=costly and self.cfg.refuseEvents~=false and "refuse" or "accept"
    if operation=="accept" and (costly or self.cfg.refuseEvents==false)
        and self:try_use_mining_bag(tonumber(event.pick_cost) or 0) then return true end
    if not self:can_act() or self._task_pending then return true end
    mining.show_random_event=event
    self._mine_event={key=key,event=event,operation=operation}
    local p=self:task_request("accept_random_event_ret","mine_random_event",function()
        return mining:AcceptRandomEvent(operation)
    end)
    p.accept_response=function(msg)
        local op=msg.accept_opeation
        local matched=op==operation or tonumber(op)==(operation=="accept" and 0 or 1)
        return matched and (not msg.random_event or same_event(msg.random_event,event))
    end
    self:mark_action()
    self.status=operation=="refuse" and "自动拒绝耗时奇遇" or "自动接受奇遇"
    self.detail=operation=="accept" and "已确认所需矿镐次数" or "跳过耗时与矿镐消耗"
    return true
end

function A:mining_on_response(name,msg)
    if previous_mining_response then previous_mining_response(self,name,msg) end
    if name=="query_mining_info_ret" then self:mine_sync_response(msg) end
    if not self.pending_dig then self._mine_sync=nil end
    local st=self.mine_refill;local p=self._task_pending
    if st and p and p.response and p.kind=="mine_heart_inventory" then
        self._task_pending=nil
        local bag=require("logic.bag")
        if (not msg.result or msg.result=="success") and type(msg.item_list)=="table" then
            bag.item_list=msg.item_list
            self:mine_single_bag_send()
        else
            st.error="未能确认大地之心库存";st.phase="failed";st.unavailable=true
        end
    elseif st and p and p.response and (p.kind=="mine_single_bag" or p.kind=="mine_single_heart" or p.kind=="mine_bag_snapshot") then
        self._task_pending=nil
        if msg.result and msg.result~="success" then
            st.error="补充矿镐失败："..tostring(msg.result);st.phase="failed";st.unavailable=true;self:log(st.error)
        else
            if msg.dig_count~=nil then mining.dig_count=msg.dig_count end
            if (tonumber(mining.dig_count) or 0)>st.before then
                st.uncertain=nil
                local gap=tonumber(self.cfg.refillInterval) or 0
                if gap>0 then st.phase=nil;st.next_at=self.now+gap
                else self:mine_single_bag_send() end
            elseif p.kind=="mine_single_bag" or p.kind=="mine_single_heart" then self:mine_bag_query()
            elseif (st.reads or 0)<3 then st.phase="read_wait";st.next_at=self.now+0.5
            else st.error="补充后矿镐尚未增加，停止重复消耗并等待同步";st.phase="read_wait";st.next_at=self.now+3;st.unavailable=true end
        end
    end
    p=self._task_pending
    if self._mine_event and p and p.kind=="mine_random_event" and p.response then
        self._task_pending=nil
        if msg.result=="success" then
            local ev=self._mine_event;self._mine_event_done=ev.key;self._mine_event=nil
            if type(mining.pending_random_event)=="table" and same_event(mining.pending_random_event,ev.event) then mining.pending_random_event=nil end
            if type(mining.show_random_event)=="table" and same_event(mining.show_random_event,ev.event) then mining.show_random_event=nil end
            self:hide_panel("event_panel")
            self:log(ev.operation=="refuse" and "已拒绝耗时奇遇" or "已自动接受奇遇，继续挖矿")
        else
            self._mine_event.error=tostring(msg.result)
            self:log("奇遇处理失败："..tostring(msg.result).."，未重复提交")
        end
    end
end
