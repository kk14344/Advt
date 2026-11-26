-- ============================================================
-- 符文抽奖/符文升级、宿命武器升星、一键大地之心
-- 只调用游戏客户端已有接口；不使用血钻，不购买资源。
-- ============================================================
A.GROWTH_EVENTS={
    "query_rune_info_ret","query_rune_loadouts_ret","draw_rune_ret","rune_one_key_receive_ret","upgrade_rune_ret",
    "query_weapon_star_info_ret","weapon_star_upgrade_ret",
    "query_bag_info_ret","use_item_ret","query_vanity_goods_ret","vanity_exchange_goods_ret",
}

local GROWTH_FLASH_GOLD_QUALITY = 6
local GROWTH_RUNE_EXP_TYPE = 1
local GROWTH_MAX_STAR = 12

local function growth_num(value,fallback)
    local n=tonumber(value)
    if n==nil then return fallback end
    return n
end

local function growth_bag()
    local ok,bag=pcall(require,"logic.bag")
    if not ok or not bag then return nil end
    return bag
end

local function growth_bag_loaded(bag)
    if not bag then return true end
    return ((tonumber(bag.capacity) or 1)>1) or (#(bag.item_list or {})>0)
end

local function growth_earth_heart()
    local bag=growth_bag()
    if not bag then return nil end
    for _,item in ipairs(bag.item_list or {}) do
        if tonumber(item.template_id)==6000005 then return item end
    end
    return nil
end

function A:rune_control_step(state,rune)
    if type(rune.QueryRuneLoadouts)~="function" then return nil end
    local pending=self._task_pending
    if pending and pending.kind=="growth_rune_loadouts" then
        if not pending.response then
            if self.now-pending.sent_at>9 and type(rune.UpdateRuneControl)=="function" then
                self._task_pending=nil;state.control_loaded=false
                rune:UpdateRuneControl()
                return reply(self,"重新同步未返回的符文出战页查询",{wait=true,delaySeconds=0.2})
            end
            return reply(self,"等待符文出战页和锁定状态同步",{wait=true,delaySeconds=0.2})
        end
        self._task_pending=nil
        local result=pending.response.result
        if result~=0 and result~="success" then
            state.control_loaded=false;state.control_retry_at=self.now+2
            return reply(self,"符文出战页同步未成功，稍后重新读取",{wait=true,delaySeconds=2})
        end
        state.control_loaded=true
    elseif pending then return nil end
    if not state.loaded then return nil end
    if self.now<(state.control_retry_at or 0) then return reply(self,"等待重新同步符文出战页",{wait=true,delaySeconds=0.2}) end
    -- A native upgrade can already have started this query inside its ACK.
    -- Adopt that read rather than issuing a duplicate. QueryRuneLoadouts also
    -- respects the game's serial login queue and its bootstrap completion ACK.
    if rune.rune_query_pending or rune.rune_query_needed or not state.control_loaded then
        local accepted
        self:task_request("query_rune_loadouts_ret","growth_rune_loadouts",function()
            if rune.rune_query_pending then accepted=true;return true end
            accepted=rune:QueryRuneLoadouts()~=false
            -- A blocked read is retried on a later tick, never a write.
            return true
        end)
        if not accepted then self._task_pending=nil end
        return reply(self,"同步当前符文出战页、装备与材料锁定状态",{wait=true,delaySeconds=0.2})
    end
    if type(rune.IsRuneControlPending)=="function" and rune:IsRuneControlPending() then
        if type(rune.UpdateRuneControl)=="function" then rune:UpdateRuneControl() end
        return reply(self,"等待游戏现有符文操作结算",{wait=true,delaySeconds=0.2})
    end
    return nil
end

function A:rune_active_equipment(rune)
    local ids={};local loadouts=rune.rune_loadouts
    if type(rune.GetRuneLoadouts)=="function" then loadouts=rune:GetRuneLoadouts() end
    local index=tonumber(rune.loadout_index) or 1
    if type(rune.GetLoadoutIndex)=="function" then index=tonumber(rune:GetLoadoutIndex()) or index end
    local page=type(loadouts)=="table" and loadouts[index]
    if page and type(page.slots)=="table" then
        for slot,id in pairs(page.slots)do if tonumber(id) and tonumber(id)>0 then ids[tonumber(id)]=tonumber(slot) or 0 end end
    else
        for _,info in ipairs(rune.rune_list or {})do
            if tonumber(info.rune_id) and (tonumber(info.equip_pos) or 0)>0 then ids[tonumber(info.rune_id)]=tonumber(info.equip_pos) end
        end
    end
    return ids
end

function A:rune_protected(rune,info)
    if info.locked==true or tonumber(info.locked)==1 or (tonumber(info.equip_pos) or 0)>0 then return true end
    if type(rune.IsRuneEquippedAnyLoadout)=="function" and rune:IsRuneEquippedAnyLoadout(info) then return true end
    for _,page in pairs(rune.rune_loadouts or {})do
        for _,id in pairs(page.slots or {})do if tonumber(id)==tonumber(info.rune_id) then return true end end
    end
    for _,index in pairs(info.equipped_loadouts or {})do if (tonumber(index) or 0)>0 then return true end end
    return false
end

local function growth_fate_crystal()
    local id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.fatecrystal
    if not id then return 0 end
    local ok,value=pcall(function() return resource_logic:GetResourceNum(id) end)
    return ok and (tonumber(value) or 0) or 0
end

local function growth_weapon_expected(info,conf)
    local star=growth_num(info.star_level,0)
    if star>=GROWTH_MAX_STAR then return nil end
    local cost=growth_num(conf.cost_num,0)
    local threshold=growth_num(conf.suc_min_exp,0)
    if cost<=0 or threshold<=0 then return nil end
    local need=math.max(1,threshold-growth_num(info.exp,0))
    local normal=math.max(1,growth_num(conf.get_exp,1))
    local crit_chance=math.max(0,math.min(1,growth_num(conf.crit_probability,0)/10000))
    local crit=math.max(normal,growth_num(conf.crit_get_exp,normal))
    local per_attempt=normal*(1-crit_chance)+crit*crit_chance
    if per_attempt<=0 then return nil end
    local attempts=need/per_attempt
    return attempts*cost,cost,need,per_attempt
end

function A:start_destiny()
    self:cancel_task()
    self.destiny_state={loaded=false,attempts=0,started=task_now(self)}
    self:log("开始宿命武器强化：优先选择升星期望消耗宿命结晶最低的未满级武器，持续到宿命结晶消耗完")
    return true
end

function A:start_earth_heart()
    self:cancel_task()
    self.earth_state={loaded=false,bag_loaded=false,used=0,attempts=0,check=nil,started=task_now(self)}
    self:log("开始一键使用大地之心：补足矿镐后结束")
    return true
end

A.cfg.voidExchangeGoldSoul=false
A.cfg.voidExchangePurpleSoul=false
local VOID_MAX_BATCH_NUM=500

local function void_good_excluded(good,cfg)
    for _,reward in ipairs(good.reward_list or {})do
    local reward_type=tonumber(reward.reward_type) or 0
    local param1=tonumber(reward.param1) or 0
    if reward_type==2 and ((param1==95 and cfg.voidExchangePurpleSoul~=true)
        or (param1==96 and cfg.voidExchangeGoldSoul~=true)) then return true end
    if reward_type==1 then
        local ok,conf=pcall(function() return config_manager.item_config[param1] end)
        local name=ok and conf and tostring(conf.name) or ""
        if (name:find("金色灵魂碎片",1,true) and not name:find("闪金",1,true) and cfg.voidExchangeGoldSoul~=true)
            or (name:find("紫色灵魂碎片",1,true) and cfg.voidExchangePurpleSoul~=true) then return true end
    end
    end
    return false
end

function A:start_void_exchange()
    self:cancel_task()
    self.void_exchange_state={loaded=false,purchased=0,attempts=0,bulk_rejected=false,started=task_now(self)}
    self:log("开始兑换虚空奖励：金色灵魂碎片"..(self.cfg.voidExchangeGoldSoul and "开启" or "关闭")..
        "，紫色灵魂碎片"..(self.cfg.voidExchangePurpleSoul and "开启" or "关闭").."；每种按最大数量兑换")
    return true
end

function A:void_exchange_step()
    local state=self.void_exchange_state
    if not state then return reply(self,"虚空兑换已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待战斗结束",{wait=true}) end
    self:hide_panel("reward_panel")
    local troop=require("logic.troop")
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>25 then
                return reply(self,"虚空兑换请求超时，已停止",{done=true,error=true,partial=state.purchased>0})
            end
            return reply(self,"等待虚空商店服务器响应",{wait=true})
        end
        local response=pending.response
        local result=tostring(response.result or "success")
        self._task_pending=nil
        if pending.kind=="growth_void_query" then
            if result~="success" then return reply(self,"读取虚空商店失败：" .. result,{done=true,error=true}) end
            state.loaded=true
        elseif pending.kind=="growth_void_buy" then
            if result~="success" then
                -- 批量兑换被服务器拒绝时，退回逐件兑换再试一次
                if (tonumber(pending.num) or 1)>1 and not state.bulk_rejected then
                    state.bulk_rejected=true
                    self:log("批量兑换被游戏拒绝（" .. result .. "），改为逐件兑换")
                else
                    return reply(self,"虚空奖励兑换失败：" .. result,{done=true,error=true,partial=state.purchased>0})
                end
            else
                state.purchased=state.purchased+(tonumber(pending.num) or 1)
                state.attempts=state.attempts+1
            end
        end
    end
    if not state.loaded then
        self:task_request("query_vanity_goods_ret","growth_void_query",function()
            network:Send({query_vanity_goods={}})
        end)
        return reply(self,"读取虚空商店",{wait=true})
    end
    local points=0
    local point_id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.vanity_adventure
    if point_id then
        local ok,value=pcall(function() return resource_logic:GetResourceNum(point_id) end)
        if ok then points=tonumber(value) or 0 end
    end
    local choice,choice_num
    for _,good in ipairs(troop.vanity_good_list or {}) do
        local price=tonumber(good.price) or 0
        local max_count=tonumber(good.max_count) or -1
        local cur_count=tonumber(good.cur_count) or 0
        if price>0 and points>=price and (max_count<=0 or cur_count<max_count) and not void_good_excluded(good,self.cfg) then
            local limit=max_count>0 and (max_count-cur_count) or VOID_MAX_BATCH_NUM
            local num=state.bulk_rejected and 1 or math.min(limit,math.floor(points/price))
            if num>0 then choice=good;choice_num=num;break end
        end
    end
    if not choice then
        return reply(self,"虚空兑换完成：" .. state.purchased .. " 件，剩余积分" .. points,{done=true})
    end
    choice_num=math.max(1,choice_num or 1)
    self:task_request("vanity_exchange_goods_ret","growth_void_buy",function()
        network:Send({vanity_exchange_goods={good_id=choice.good_id,num=choice_num}})
    end)
    self._task_pending.num=choice_num
    local detail=choice_num>1 and ("一次性兑换" .. choice_num .. "个") or "兑换1个"
    return reply(self,"兑换虚空奖励（已兑换" .. state.purchased .. "件，剩余积分" .. points .. "）：" .. detail,{wait=true})
end

function A:destiny_step()
    local state=self.destiny_state
    if not state then return reply(self,"宿命武器任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待战斗结束",{wait=true}) end
    self:hide_panel("reward_panel")
    local destiny=require("logic.destiny_weapon")
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>25 then
                return reply(self,"宿命武器请求超时，已停止",{done=true,error=true,partial=state.attempts>0})
            end
            return reply(self,"等待宿命武器服务器响应",{wait=true})
        end
        local response=pending.response
        local result=tostring(response.result or "success")
        self._task_pending=nil
        if pending.kind=="growth_star_query" then
            if result~="success" then return reply(self,"读取宿命武器失败：" .. result,{done=true,error=true}) end
            state.loaded=true
        elseif pending.kind=="growth_star_upgrade" then
            if result~="success" then
                return reply(self,"宿命武器强化失败：" .. result,{done=true,error=true,partial=state.attempts>0})
            end
            state.attempts=state.attempts+1
        end
    end
    if not state.loaded then
        self:task_request("query_weapon_star_info_ret","growth_star_query",function()
            network:Send({query_weapon_star_info={}})
        end)
        return reply(self,"读取宿命武器升星信息",{wait=true})
    end
    if destiny.weapon_star_can_upgrade==false then
        return reply(self,"宿命武器升星尚未解锁",{done=true,partial=state.attempts>0})
    end
    if state.attempts>=1000 then
        return reply(self,"宿命武器强化达到单次上限，已结束",{done=true})
    end
    local crystals=growth_fate_crystal()
    local candidates={}
    for key,info in pairs(destiny.weapon_star_info_list or {}) do
        local weapon_id=tonumber(info.weapon_id) or tonumber(key)
        local conf=(config_manager.weapon_star_upgrade_config or {})[weapon_id]
        conf=conf and conf[tonumber(info.star_level) or 0]
        if weapon_id and conf then
            local expected,cost,need,per=growth_weapon_expected(info,conf)
            if expected then candidates[#candidates+1]={id=weapon_id,info=info,conf=conf,
                expected=expected,cost=cost,need=need,per=per} end
        end
    end
    if #candidates==0 then
        return reply(self,"宿命武器任务完成：没有未满级或可强化的武器（已强化" .. state.attempts .. "次）",{done=true})
    end
    table.sort(candidates,function(a,b)
        if a.expected~=b.expected then return a.expected<b.expected end
        if a.id~=b.id then return a.id<b.id end
        return a.cost<b.cost
    end)
    local choice
    for _,candidate in ipairs(candidates) do
        if crystals>=candidate.cost then choice=candidate;break end
    end
    if not choice then
        return reply(self,"宿命结晶不足，已强化" .. state.attempts .. "次",{done=true,partial=state.attempts==0})
    end
    self:task_request("weapon_star_upgrade_ret","growth_star_upgrade",function()
        network:Send({weapon_star_upgrade={weapon_id=choice.id,upgrade_type="auto"}})
    end)
    self:log(string.format("宿命武器%d星%d→%d：预计消耗%d宿命结晶（当前%d，单次%d）",
        choice.id,tonumber(choice.info.star_level) or 0,(tonumber(choice.info.star_level) or 0)+1,
        math.ceil(choice.expected),crystals,choice.cost))
    return reply(self,string.format("强化宿命武器%d（期望%d结晶）",choice.id,math.ceil(choice.expected)),{wait=true})
end

function A:earth_heart_step()
    local state=self.earth_state
    if not state then return reply(self,"大地之心任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待战斗结束",{wait=true}) end
    self:hide_panel("reward_panel")
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>25 then
                return reply(self,"大地之心请求超时，已停止",{done=true,error=true,partial=state.used>0})
            end
            return reply(self,"等待大地之心服务器响应",{wait=true})
        end
        local response=pending.response
        local result=tostring(response.result or "success")
        self._task_pending=nil
        if pending.kind=="growth_earth_mining" then
            state.loaded=true
            if state.check then
                local before=state.check;state.check=nil
                if (tonumber(mining.dig_count) or 0)<=before then
                    return reply(self,"使用大地之心后矿镐次数未增加，已停止",{done=true,error=true,partial=state.used>0})
                end
            end
        elseif pending.kind=="growth_earth_bag" then
            state.bag_loaded=true
        elseif pending.kind=="growth_earth_use" then
            if result~="success" then
                return reply(self,"大地之心使用失败：" .. result,{done=true,error=true,partial=state.used>0})
            end
            state.used=state.used+1
            state.loaded=false
        end
    end
    if not state.loaded then
        self:task_request("query_mining_info_ret","growth_earth_mining",function()
            network:Send({query_mining_info={}})
        end)
        return reply(self,"读取矿镐次数",{wait=true})
    end
    local current=tonumber(mining.dig_count) or 0
    local maximum=tonumber(mining.dig_max_count) or 0
    if maximum>0 and current>=maximum then
        return reply(self,"矿镐已满（" .. current .. "/" .. maximum .. "），本次使用" .. state.used .. "个大地之心",{done=true})
    end
    local bag=growth_bag()
    if not state.bag_loaded then
        if growth_bag_loaded(bag) then state.bag_loaded=true
        else
            self:task_request("query_bag_info_ret","growth_earth_bag",function()
                network:Send({query_bag_info={}})
            end)
            return reply(self,"读取冒险背包中的大地之心",{wait=true})
        end
    end
    local item=growth_earth_heart()
    if not item then
        return reply(self,"背包中没有大地之心（本次使用" .. state.used .. "个）",{done=true,partial=state.used==0})
    end
    if state.attempts>=100 then
        return reply(self,"大地之心使用次数达到上限，已结束",{done=true})
    end
    state.attempts=state.attempts+1
    state.check=current
    state.bag_loaded=true
    self:task_request("use_item_ret","growth_earth_use",function()
        bag:UseItem(item.item_id)
    end)
    return reply(self,"使用1个大地之心（矿镐" .. current .. "/" .. maximum .. "）",{wait=true})
end
