do
local stone_template=10000031
-- Match the native reset panel's options and the costs shown in settings.
local tier_costs={[1]={0,0},[100]={3,20},[200]={6,40},[300]={9,60},[350]={12,70}}

local function reset_eligible(self,target,threshold)
    local level=tonumber(mining.golem_lv)
    if not self.cfg.enabled or not self.cfg.autoMine or not self.cfg.autoMysticStoneReset
        or self.context~="mining_district_sub_scene" or self.daily_state and self.daily_state.running
        or self.inBattle or self.fight or self.pending_dig or bag_logic.__mw_use_pending
        or not level or not threshold or level<=target or level<threshold
        or not self:can_act() or not self:request_channel_available() then return false end
    if type(mining.CheckRefreshArea)~="function" then return false end
    local costs=tier_costs[target]
    if not costs then return false end
    local types=constants.RESOURCE_TYPE or {}
    for _,tier in pairs(config_manager.mining_refresh_config or {}) do
        if tonumber(tier.golem_lv)==target then
            local tnt,tools=tonumber(tier.tnt),tonumber(tier.ultimate_tool)
            return level>=(tonumber(tier.reset_lv) or math.huge)
                and tnt and tnt>=0 and tnt<=costs[1] and tools and tools>=0 and tools<=costs[2]
                and (tnt==0 or types.tnt and (tonumber(resource_logic:GetResourceNum(types.tnt)) or 0)>=tnt)
                and (tools==0 or types.ultimate_tool and (tonumber(resource_logic:GetResourceNum(types.ultimate_tool)) or 0)>=tools)
        end
    end
    return false
end

local function stone_instance(response)
    if type(response)~="table" or (response.result and response.result~="success" and response.result~=0)
        or type(response.item_list)~="table" then return nil end
    local id
    for _,item in ipairs(response.item_list) do
        if tonumber(item.template_id)==stone_template and (tonumber(item.count) or 1)>0 then
            local candidate=tonumber(item.item_id)
            if candidate and candidate>0 and candidate==math.floor(candidate)
                and (not id or candidate<id) then id=candidate end
        end
    end
    return id
end

local function finish(self,job,reason,uncertain)
    if self._task_pending==job.pending then self._task_pending=nil end
    self._mystic_reset=nil
    self._mystic_reset_next=job.phase=="map" and self.now or self.now+30
    if uncertain then self._mystic_reset_blocked=true end
    if reason then self:log(reason) end
    return false
end

local function clear_exploration(self)
    self.exploration_tip=nil;self.exploration_depth=nil;self.exploration_observation=nil
    self.exploration_vision=nil;self.horizontal_started=nil;self.horizontal_transition_logged=nil
    self.layer_target=nil;self.last_tunnel_y=nil;self.layer_dir=1
    self.mining_index=nil;self._route_job=nil
    self.failed={};self.failed_info={};self.seen_golems=nil
    self._golem_region_at=nil;self._shallow_query_at=nil;self._workshop_depth_query_at=nil
    self.mining_route_target=nil;self:set_mining_route(nil,nil)
end

local function map_replaced(job)
    return tonumber(mining.golem_lv)==job.target and (mining.block_list~=job.grid
        or job.generation~=nil and mining.stream_generation~=nil and mining.stream_generation~=job.generation)
end

local function request_map(self,job)
    self.status="等待重置后的矿区地图"
    self.detail="杂兵已重置到 "..job.target.." 级"
    if self.now>=(job.next_map_read or 0) and self:request_channel_available() then
        job.next_map_read=self.now+(self.now<job.wait_until and 2 or 5)
        pcall(function() mining:QueryBlockInfo() end)
    end
    return true
end

function A:try_mystic_stone_reset()
    if not self.cfg.autoMysticStoneReset then return false end
    local level=tonumber(mining.golem_lv)
    local target=tonumber(self.cfg.mysticStoneResetTier) or 1
    local threshold=tonumber(self.cfg.mysticStoneThreshold) or 150
    if level and (level<threshold or level<=target) then self._mystic_reset_blocked=nil end
    if self._mystic_reset_blocked or self._task_pending or self.now<(self._mystic_reset_next or 0)
        or not reset_eligible(self,target,threshold) then return false end
    self:install_task_hooks()
    if not self._task_hooks.query_bag_info_ret or not self._task_hooks.use_item_ret
        or not self._task_hooks.refresh_mining_area_ret or not self._task_hooks.query_mining_info_ret then
        self._mystic_reset_next=self.now+30
        return false
    end
    self._mystic_reset={phase="inventory",target=target,threshold=threshold,
        pending=self:bounded_request("query_bag_info_ret","mystic_stone_bag","query_bag_info",{},
            "核对神秘之石库存")}
    self.status="核对神秘之石库存"
    self.detail="当前杂兵 "..level.." 级，触发等级 "..threshold
    return true
end

function A:advance_mystic_stone_reset()
    local job=self._mystic_reset
    if not job then return false end
    if self._task_pending and self._task_pending~=job.pending then return false end
    if (job.phase=="use" or job.phase=="verify") and map_replaced(job) then
        if self._task_pending==job.pending then self._task_pending=nil end
        clear_exploration(self)
        job.phase="map";job.wait_until=self.now+12
    end
    if job.phase=="map" then
        if type(mining.block_list)=="table" and next(mining.block_list)~=nil then
            pcall(function() mining:GetDepth() end)
            return finish(self,job,"神秘之石已重置矿区到 "..job.target.." 级，继续自动挖掘")
        end
        return request_map(self,job)
    end

    local state=job.pending and self:bounded_request_state(job.pending,5) or "uncertain"
    if job.phase=="inventory" then
        if state=="waiting" then return true end
        local id=state=="response" and stone_instance(job.pending.response)
        self._task_pending=nil
        if not id or self.cfg.mysticStoneResetTier~=job.target or self.cfg.mysticStoneThreshold~=job.threshold
            or not reset_eligible(self,job.target,job.threshold) then return finish(self,job) end
        if mining:CheckRefreshArea(job.target,true)~=true then return finish(self,job) end
        job.grid=mining.block_list;job.generation=mining.stream_generation
        job.phase="use";job.wait_until=self.now+8
        job.pending=self:bounded_request("use_item_ret","mystic_stone_use","use_item",
            {item_id=id,golem_lv=job.target},"使用神秘之石重置矿区",
            function(msg) return tonumber(msg.item_id)==id end)
        self.status="等待神秘之石重置确认"
        self.detail="重置目标 "..job.target.." 级"
        return true
    end

    if job.phase=="use" then
        if state=="unsent" then return finish(self,job,"神秘之石请求未发出，继续挖掘") end
        if state=="response" then
            local result=job.pending.response.result
            if result~="success" and result~=0 then
                return finish(self,job,"神秘之石未使用："..tostring(result))
            end
            -- Some clients apply the item and the map in separate callbacks.
            if self.now<job.pending.received_at+0.5 then return true end
        elseif state=="waiting" then return true end
        if self:request_channel_available() then
            job.phase="verify"
            job.pending=self:bounded_request("query_mining_info_ret","mystic_stone_verify","query_mining_info",{},
                "核对矿区是否已经重置")
            self.status="正在核对矿区重置结果"
            return true
        end
        if self.now<job.wait_until then return true end
    elseif state=="waiting" then return true
    elseif state=="response" and (job.pending.response.result==nil or job.pending.response.result=="success"
        or job.pending.response.result==0) and tonumber(job.pending.response.golem_lv)==job.target
        and type(mining.ResetArea)=="function" then
        -- The level was confirmed by the server. Discard the old native map
        -- when its separate reset notification was lost, and fetch new tiles.
        local refresh=job.pending.response.refresh_time or mining.refresh_time
        mining:ResetArea(job.target)
        mining.refresh_time=refresh
        clear_exploration(self)
        self._task_pending=nil
        job.phase="map";job.wait_until=self.now+12
        return request_map(self,job)
    end

    return finish(self,job,"神秘之石结果未确认，暂缓再次自动使用",true)
end
end
