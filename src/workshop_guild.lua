-- ============================================================
-- 工坊工程与公会首领
-- 只使用游戏已有的工程、道具和战斗接口；不购买资源，不花血钻。
-- ============================================================
A.WORKSHOP_EVENTS={
    "query_bag_info_ret","use_item_ret",
    "query_guild_boss_info_ret","guild_boss_challenge_ret","guild_boss_update_ret",
    "guild_boss_buy_ticket_ret",
}

local EARTH_HEART_ITEM = 6000005
local ULTIMATE_TOOL = 29

local function ws_mining_depth()
    local depth=tonumber(mining.dig_depth) or 0
    if type(mining.GetDepth)=="function" then
        local ok,value=pcall(mining.GetDepth,mining)
        if ok then depth=math.max(depth,tonumber(value) or 0) end
    end
    return depth
end

local function ws_now(self)
    local ok,value=pcall(function() return time_logic:Now() end)
    if ok and tonumber(value) then return tonumber(value) end
    return nil
end

local function ws_bag()
    local ok,bag=pcall(require,"logic.bag")
    if not ok or not bag then return nil end
    return bag
end

local function ws_bag_loaded(bag)
    if not bag then return true end
    return ((tonumber(bag.capacity) or 1)>1) or (#(bag.item_list or {})>0)
end

local function ws_earth_heart()
    local bag=ws_bag()
    if not bag then return nil end
    local items=bag.item_list or {}
    for _,item in ipairs(items) do
        local template=tonumber(item.template_id)
        if template==EARTH_HEART_ITEM then return item end
    end
    return nil
end

local function ws_ready_time(project)
    return tonumber(project and project.endtime) or 0
end

-- Resolve the configured tier before reading runtime depth or materials.
-- The same preflight is used by manual production and one-key daily.
function A:workshop_production_project(empty_slots)
    local configured=tonumber(self.cfg.workshopDepth) or 150
    local minutes=tonumber(self.cfg.workshopMinutes) or 120
    local project,layer
    for _,conf in pairs(config_manager.mining_quarry_config or {}) do
        local need=tonumber(conf.need_layer)
        if need and need<=configured and tonumber(conf.need_time)==minutes then
            if not project or need>layer then
                project,layer=conf,need
            end
        end
    end
    if not project then
        return nil,"config",string.format("找不到 %d 层内的 %d 分钟工坊档位",configured,minutes)
    end
    local title=tostring(project.name or "设定档位")
    local actual=ws_mining_depth()
    if actual<layer then
        return nil,"depth",string.format("%s需 %d 层，当前 %d 层",title,layer,actual)
    end
    local cost=(tonumber(project.tnt_count) or 0)*empty_slots
    if cost>0 then
        local id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.tnt
        local stock=id and tonumber(resource_logic:GetResourceNum(id)) or 0
        local reserve=math.max(0,tonumber(self.cfg.reserveTnt) or 0)
        if stock<cost+reserve then
            return nil,"tnt",string.format("%s填 %d 格需 %d 支，保留 %d 支，现有 %d 支",
                title,empty_slots,cost,reserve,stock)
        end
    end
    return project
end

function A:workshop_block_reply(code,detail,daily_id)
    local title=code=="depth" and "工坊深度不足" or code=="tnt" and "工坊雷管不足" or "工坊档位不可用"
    local message=title.." · "..detail
    local result
    if daily_id then result=self:daily_skip(daily_id,message)
    else result=reply(self,message,{done=true,partial=true}) end
    result.workshop_block=title
    result.workshop_detail=detail
    return result
end

function A:start_workshop(mode)
    self:cancel_task()
    if mode~="produce" and mode~="both" then mode="collect" end
    self.workshop_state={mode=mode,started=task_now(self),claimed=0,produced=0,
        loaded=false,bag_queried=false,refill_attempts=0,refill_check=nil}
    self:log(mode=="both" and "开始工坊（先收取已完成工程，再补满空槽位）" or (mode=="collect" and "开始收取工坊工程" or "开始工坊生产"))
    return true
end

-- ------------------------------------------------------------
-- 一键日常的工会首领阶段（1.17）：先按预算购买试炼许可证
-- （游戏接口 guild_boss_buy_ticket，价格阶梯取自
--   guild:BuyTicketCost，上限 guild:GetBuyTicketMax），
-- 再复用 start_guild_boss / guild_boss_step 执行挑战。
-- ------------------------------------------------------------
function A:start_daily_guild_boss(ticket_target, fight_target)
    -- 不调用 cancel_task：由一键日常的 child_guild 阶段托管状态。
    self.guild_ticket_state={
        target=math.max(0,math.floor(tonumber(ticket_target) or 0)),
        fight=math.max(1,math.floor(tonumber(fight_target) or 1)),
        queried=false,started=task_now(self)}
    return true
end

function A:daily_guild_ticket_step()
    local state=self.guild_ticket_state
    if not state then return reply(self,"工会首领许可证阶段已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待战斗结束",{wait=true}) end
    local guild=require("logic.guild")
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>25 then
                self.guild_ticket_state=nil
                return reply(self,"工会首领许可证请求超时，跳过购买",{done=true,partial=true})
            end
            return reply(self,"等待工会首领服务器响应",{wait=true})
        end
        self._task_pending=nil
        local result=tostring(pending.response.result or "")
        if pending.kind=="gt_query" then
            if result~="success" then
                self.guild_ticket_state=nil
                return reply(self,"读取公会首领信息失败：" .. result .. "，跳过许可证购买",{done=true,partial=true})
            end
            state.queried=true
        elseif pending.kind=="gt_buy" then
            if result=="success" then
                if pending.cost and pending.cost>0 then
                    self:spend_record(pending.cost,"工会首领许可证×" .. tostring(pending.buy_count or 0))
                end
                self:log("工会首领许可证已购买" .. tostring(pending.buy_count or 0) ..
                    "张（" .. tostring(pending.cost or 0) .. "血钻）")
                self.guild_ticket_state=nil
                return reply(self,"工会首领许可证购买完成（" .. tostring(pending.buy_count or 0) .. "张）",{done=true})
            end
            self:log("购买工会首领许可证失败：" .. result .. "，继续使用已有许可证")
            self.guild_ticket_state=nil
            return reply(self,"购买工会首领许可证失败：" .. result,{done=true,partial=true})
        end
    end
    if not state.queried then
        -- 先读取服务器侧首领状态与许可证购买计数，再决定购买数量
        self:task_request("query_guild_boss_info_ret","gt_query",function()
            network:Send({query_guild_boss_info={}})
        end)
        return reply(self,"读取公会首领与试炼许可证",{wait=true})
    end
    if type(guild.IsOpenGuildBoss)=="function" then
        local open_ok,open=pcall(guild.IsOpenGuildBoss,guild)
        if open_ok and open==false then
            self.guild_ticket_state=nil
            return reply(self,"公会首领功能尚未开启，跳过许可证购买",{done=true,partial=true})
        end
    end
    if guild.guild_boss_over then
        self.guild_ticket_state=nil
        return reply(self,"本次公会首领试炼已结束，跳过许可证购买",{done=true,partial=true})
    end
    -- 今日可购买上限：价格阶梯长度减去已购买数。
    local max_buy
    if type(guild.GetBuyTicketMax)=="function" then
        local ok,value=pcall(guild.GetBuyTicketMax,guild)
        if ok and tonumber(value) then max_buy=math.max(0,math.floor(tonumber(value))) end
    end
    if max_buy==nil then
        self.guild_ticket_state=nil
        return reply(self,"无法确认许可证剩余购买额度，跳过购买",{done=true,partial=true})
    end
    if state.target>0 then max_buy=math.min(max_buy,state.target) end
    if max_buy<=0 then
        self.guild_ticket_state=nil
        return reply(self,"工会首领许可证今日购买额度已用完",{done=true,partial=true})
    end
    -- 逐张缩减直到预算与血钻都允许；一张都买不起则直接跳过。
    local buy=max_buy
    local function ticket_cost(n)
        if type(guild.BuyTicketCost)~="function" then return nil end
        local ok,value=pcall(guild.BuyTicketCost,guild,n)
        if ok and tonumber(value) and tonumber(value)>0 then return tonumber(value) end
        return nil
    end
    while buy>0 do
        local cost=ticket_cost(buy)
        if not cost then
            self.guild_ticket_state=nil
            return reply(self,"许可证价格未加载，跳过购买",{done=true,partial=true})
        end
        local budget_ok,budget_reason=self:spend_can_afford(cost)
        if not budget_ok then
            if buy==1 then
                self:log("消费策略拦截：跳过购买工会首领许可证（" .. budget_reason .. "）")
                self.guild_ticket_state=nil
                return reply(self,"工会首领许可证未购买：" .. budget_reason,{done=true,partial=true})
            end
            buy=buy-1
        elseif not resource_logic:CheckResourceNum(constants.RESOURCE_TYPE.blood_diamond,cost,false) then
            if buy==1 then
                self:log("血钻不足，跳过购买工会首领许可证（需" .. cost .. "血钻）")
                self.guild_ticket_state=nil
                return reply(self,"血钻不足（需" .. cost .. "血钻），跳过购买工会首领许可证",{done=true,partial=true})
            end
            buy=buy-1
        else
            local cost_final=cost
            if buy<max_buy then
                self:log("工会首领许可证剩余额度" .. max_buy .. "张，预算或血钻余额仅允许购买" .. buy .. "张")
            end
            local daily=self.daily_state
            if daily then daily.buy_guild_ticket=true end
            local p=self:task_request("guild_boss_buy_ticket_ret","gt_buy",function()
                network:Send({guild_boss_buy_ticket={count=buy}})
            end)
            p.buy_count=buy;p.cost=cost_final
            return reply(self,"购买工会首领许可证" .. buy .. "张（" .. cost_final .. "血钻）",{wait=true,action="购买许可证"})
        end
    end
    self.guild_ticket_state=nil
    return reply(self,"工会首领许可证未购买",{done=true,partial=true})
end

function A:start_guild_boss(count)
    self:cancel_task()
    local target=math.floor(tonumber(count) or tonumber(self.cfg.guildBossCount) or 3)
    target=math.max(1,math.min(99,target))
    self.guild_state={target=target,count=0,queried=false,waiting=false,
        wait_started=0,started=task_now(self)}
    self:log("开始公会首领战斗，计划" .. target .. "次；不购买许可证")
    return true
end

function A:workshop_refill(needed)
    self:start_refill("workshop",needed)
    return reply(self,"按全部空闲栏位计算缺口，大地之心优先补齐",{wait=true,action="补充矿镐"})
end

function A:workshop_step()
    local state=self.workshop_state
    if not state then return reply(self,"工坊任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待战斗结束",{wait=true}) end
    self:hide_panel("reward_panel")
    if self.fast_refill then
        local r=self:refill_step()
        if not r.refill_done then return r end
        if r.error then return reply(self,r.error,{done=true,error=true,partial=true}) end
    end
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>20 then
                state.loaded=false
                return reply(self,"工坊请求超时，已停止，避免重复消耗资源",{done=true,error=true})
            end
            return reply(self,"等待工坊服务器响应",{wait=true})
        end
        local result=tostring(pending.response.result or "success")
        self._task_pending=nil
        if pending.kind=="ws_query" then
            if result~="success" then
                return reply(self,"读取工坊信息失败：" .. result,{done=true,error=true})
            end
            state.loaded=true
            if state.refill_check then
                local before=state.refill_check
                state.refill_check=nil
                if (tonumber(mining.dig_count) or 0)<=before then
                    return reply(self,"补充后矿镐次数没有增加，已停止重复使用",{done=true,error=true,partial=true})
                end
            end
        elseif pending.kind=="ws_collect" then
            if result~="success" then
                return reply(self,"收取工坊工程失败：" .. result,{done=true,error=true,partial=state.claimed>0})
            end
            state.claimed=state.claimed+1
            state.loaded=true
        elseif pending.kind=="ws_add" then
            if result~="success" then
                return reply(self,"开始工坊生产失败：" .. result,{done=true,error=true,partial=state.produced>0})
            end
            state.produced=state.produced+1
            state.loaded=true
            state.refill_attempts=0
            self:log("工坊生产已开始（" .. state.produced .. "）：" .. tostring(pending.project_name or ""))
        elseif pending.kind=="ws_tool" then
            if result~="success" then
                return reply(self,"矿工包使用失败：" .. result,{done=true,error=true,partial=true})
            end
            state.loaded=false
        elseif pending.kind=="ws_bag" then
            state.loaded=true
        elseif pending.kind=="ws_item" then
            if result~="success" then
                return reply(self,"大地之心使用失败：" .. result,{done=true,error=true,partial=true})
            end
            state.loaded=false
        end
    end
    if not state.loaded then
        self:task_request("query_mining_info_ret","ws_query",function()
            network:Send({query_mining_info={}})
        end)
        return reply(self,"读取工坊工程、矿镐与深度",{wait=true})
    end
    local list=mining.project_list or {}
    local max_count=tonumber(mining.max_project_count) or 0
    if state.mode=="collect" or state.mode=="both" then
        local first=list[1]
        local now=ws_now(self) or 0
        if first and ws_ready_time(first)<=now then
            self:task_request("get_mining_project_reward_ret","ws_collect",function()
                network:Send({get_mining_project_reward={}})
            end)
            return reply(self,"收取已完成工坊工程",{wait=true,action="收取工坊"})
        end
        if state.mode=="collect" then
            if not first then
                return reply(self,"工坊没有待收取工程（本次已收取" .. state.claimed .. "个）",{done=true,partial=state.claimed==0})
            end
            local remain=math.max(1,math.ceil(ws_ready_time(first)-now))
            return reply(self,"工坊暂无完成工程，下一个还需" .. remain .. "秒；本次已收取" .. state.claimed .. "个",{done=true,partial=state.claimed==0})
        end
    end
    if max_count<=0 then
        return reply(self,"无法读取工坊槽位，请确认工坊功能已开启",{done=true,error=true})
    end
    if #list>=max_count then
        if state.mode=="both" then
            return reply(self,"工坊完成：已收取" .. state.claimed .. "个工程，新开工" .. state.produced .. "个工程（槽位" .. #list .. "/" .. max_count .. "）",{done=true,partial=state.claimed==0 and state.produced==0})
        end
        return reply(self,"工坊槽位已满（" .. #list .. "/" .. max_count .. "），本次已开始" .. state.produced .. "个工程",{done=true,partial=state.produced==0})
    end
    local empty_slots=max_count-#list
    local project,code,detail=self:workshop_production_project(empty_slots)
    if not project then return self:workshop_block_reply(code,detail) end
    local needed=tonumber(project.dig_count) or 0
    local have=tonumber(mining.dig_count) or 0
    local target=needed*empty_slots
    local cap=tonumber(mining.dig_max_count) or 0
    if cap>0 then target=math.min(target,cap) end
    local tools=tonumber(resource_logic:GetResourceNum(ULTIMATE_TOOL)) or 0
    if not self:has_earth_heart() then
        if tools>0 then target=math.max(needed,math.min(target,have+tools*5)) else target=needed end
    end
    if have<target then
        return self:workshop_refill(target)
    end
    local project_id=project.ID or project.id
    if not project_id then
        return reply(self,"工坊工程配置缺少ID",{done=true,error=true})
    end
    local p=self:task_request("add_mining_project_ret","ws_add",function()
        network:Send({add_mining_project={id=project_id}})
    end)
    p.project_name=project.name
    self:log("安排工坊工程：" .. tostring(project.name) .. "（" .. tostring(project.need_layer) .. "层，" ..
        tostring(project.need_time) .. "分钟）")
    return reply(self,"开始工坊生产：" .. tostring(project.name) .. "（" .. tostring(project.need_layer) .. "层/" .. tostring(project.need_time) .. "分钟）",
        {wait=true,action="工坊生产"})
end

function A:close_reward_panel()
    local ok,scene=pcall(function()
        return cc.Director:getInstance():getRunningScene()
    end)
    if not ok or not scene or not scene.sub_panels then return false end
    local panel=scene.sub_panels["reward_panel"]
    if not panel or not panel:IsVisible() then return false end
    -- Closing through the panel itself runs reward_panel.close_call.  The
    -- guild boss handler uses that callback to reset its battle state, so a
    -- plain hide would leave the next challenge waiting forever.
    for i=1,30 do
        if not panel:IsVisible() then break end
        local closed=false
        if type(panel.PopRewardInfoFromQueue)=="function" then
            local good=pcall(panel.PopRewardInfoFromQueue,panel)
            closed=good
        end
        if not closed then
            pcall(function() graphic:DispatchEvent("hide_world_sub_panel","reward_panel") end)
            break
        end
    end
    return true
end

function A:guild_boss_step()
    local state=self.guild_state
    if not state then return reply(self,"公会首领任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if not state.waiting then self:close_reward_panel() end
    local guild=require("logic.guild")
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>25 then
                return reply(self,"公会首领请求超时，已停止",{done=true,error=true,partial=state.count>0})
            end
            return reply(self,"等待公会首领服务器响应",{wait=true})
        end
        local result=tostring(pending.response.result or "")
        self._task_pending=nil
        if pending.kind=="gb_query" then
            if result~="success" then
                return reply(self,"读取公会首领失败：" .. result,{done=true,error=true,partial=state.count>0})
            end
            state.queried=true
        elseif pending.kind=="gb_challenge" then
            if result=="failure" then
                -- This server can return failure for a valid boss attempt.
                -- Re-read boss/tickets before continuing; never replay a timeout.
                state.queried=false;state.waiting=false
                state.failures=(state.failures or 0)+1
                state.retry_at=task_now(self)+0.25
                if state.failures==1 or task_now(self)>=(state.failure_log_at or 0)+5 then
                    self:log("公会首领返回failure，刷新首领和许可证后继续挑战")
                    state.failure_log_at=task_now(self)
                end
            elseif result~="success" then
                return reply(self,"公会首领挑战未被接受：" .. result,{done=true,error=true,partial=state.count>0})
            else
                state.count=state.count+1
                state.waiting=true
                state.wait_started=task_now(self)
                state.queried=false
                self:log("公会首领第" .. state.count .. "次攻击已发起")
            end
        end
    end
    if state.waiting then
        if self.inBattle and self.cfg.autoSkipBattle==false then
            if task_now(self)-state.wait_started>120 then
                return reply(self,"公会首领战斗超过120秒未结算，请检查游戏画面",{done=true,error=true,partial=state.count>0})
            end
            return reply(self,"等待公会首领战斗结算（" .. state.count .. "/" .. state.target .. "）",{wait=true})
        end
        -- The battle animation may be skipped before the guild handler
        -- receives its close callback.  Closing the reward panel runs that
        -- callback, which resets fight_boss_state for the next attack.
        self:close_reward_panel()
        local fighting=false
        if type(guild.GetFrightBossState)=="function" then
            local ok,mode=pcall(guild.GetFrightBossState,guild)
            if ok and tonumber(mode) and tonumber(mode)~=0 then fighting=true end
        end
        if fighting then
            if task_now(self)-state.wait_started>20 then
                return reply(self,"公会首领战斗已结束但状态未复位，请重新加载游戏",{done=true,error=true,partial=state.count>0})
            end
            return reply(self,"等待公会首领状态复位",{wait=true})
        end
        state.waiting=false
        state.queried=false
    end
    if state.count>=state.target then
        return reply(self,"公会首领已完成 " .. state.count .. " 次攻击",{done=true})
    end
    if task_now(self)<(state.retry_at or 0) then
        return reply(self,"公会首领返回failure，稍后刷新状态继续",{wait=true,delaySeconds=0.25})
    end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待当前战斗结束",{wait=true}) end
    if type(guild.IsOpenGuildBoss)=="function" then
        local open_ok,open=pcall(guild.IsOpenGuildBoss,guild)
        if open_ok and open==false then
            return reply(self,"公会首领功能尚未开启",{done=true,partial=state.count>0})
        end
    end
    if not state.queried then
        self:task_request("query_guild_boss_info_ret","gb_query",function()
            network:Send({query_guild_boss_info={}})
        end)
        return reply(self,"读取公会首领与试炼许可证",{wait=true})
    end
    local open=true
    if type(guild.IsOpenGuildBoss)=="function" then
        local ok,value=pcall(guild.IsOpenGuildBoss,guild)
        if ok and value==false then open=false end
    end
    if not open then
        return reply(self,"公会首领功能尚未开启",{done=true,partial=state.count>0})
    end
    if guild.guild_boss_over then
        return reply(self,"本次公会首领试炼已结束，已完成 " .. state.count .. " 次",{done=true,partial=state.count>0})
    end
    local boss_id=tonumber(guild.guild_cur_boss_id) or 0
    if boss_id<=0 then
        return reply(self,"当前没有可挑战的公会首领",{done=true,partial=state.count>0})
    end
    if type(guild.GetFrightBossState)=="function" then
        local ok,mode=pcall(guild.GetFrightBossState,guild)
        if ok and tonumber(mode) and tonumber(mode)~=0 then
            state.waiting=true;state.wait_started=task_now(self)
            return reply(self,"等待已有公会首领战斗结束",{wait=true})
        end
    end
    local ticket=(constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.guild_boss_ticket) or 51
    local tickets=0
    if ticket then
        local ok,value=pcall(function() return resource_logic:GetResourceNum(ticket) end)
        if ok then tickets=tonumber(value) or 0 end
    end
    local ticket_cost=tonumber(constants.GUILD_BOSS_FRIGHT_CONSUME) or 1
    if tickets<ticket_cost then
        return reply(self,"试炼许可证不足（每次消耗" .. ticket_cost .. "张，不自动购买），已完成 " .. state.count .. " 次",{done=true,partial=state.count>0})
    end
    local first_boss_id=boss_id
    self:task_request("guild_boss_challenge_ret","gb_challenge",function()
        network:Send({guild_boss_challenge={boss_id=first_boss_id}})
    end)
    return reply(self,"挑战公会首领第" .. boss_id .. "战（" .. (state.count+1) .. "/" .. state.target .. "）",{wait=true,action="公会首领"})
end

function A:workshop_view()
    local list=mining.project_list or {}
    local ready=0
    local now=ws_now(self) or 0
    for _,project in ipairs(list) do
        if ws_ready_time(project)<=now then ready=ready+1 end
    end
    local max_count=tonumber(mining.max_project_count) or 0
    local tool_count=0
    local ok,value=pcall(function() return resource_logic:GetResourceNum(tonumber(ULTIMATE_TOOL)) end)
    if ok then tool_count=tonumber(value) or 0 end
    local heart=ws_earth_heart()
    local depth=ws_mining_depth()
    return {slots=#list,max=max_count,ready=ready,depth=depth,
        minutes=tonumber(self.cfg.workshopMinutes) or 120,
        picks=tonumber(mining.dig_count) or 0,tools=tool_count,
        earthHeart=heart~=nil,
        label=string.format("工坊 %d/%d · 可收 %d · 矿镐 %d",
            #list,max_count,ready,tonumber(mining.dig_count) or 0)}
end

function A:guild_view()
    local ok,guild=pcall(require,"logic.guild")
    local tickets=0
    local ticket=(constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.guild_boss_ticket) or 51
    if ticket then
        local ok2,value=pcall(function() return resource_logic:GetResourceNum(ticket) end)
        if ok2 then tickets=tonumber(value) or 0 end
    end
    local boss=0
    local over=true
    if ok and guild then
        boss=tonumber(guild.guild_cur_boss_id) or 0
        over=guild.guild_boss_over~=false
    end
    local target=tonumber(self.cfg.guildBossCount) or 3
    return {boss=boss,tickets=tickets,over=over,target=target,
        label=string.format("首领 %d战 · 门票 %d",boss,tickets)}
end
