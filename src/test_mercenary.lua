-- 佣兵养成（测试服）
--   强化佣兵：武器强化 → 锻造宝具 → 宝具强化
--   佣兵养成：升级 → 觉醒到 5 阶 → 继续升级 → 自动给上阵阵容签契约
--   自动契约：只给当前上阵阵容签订契约，签到满阶
--   洗界限突破：上阵阵容（除主角）自动补齐觉醒/突破，然后洗属性到目标并替换
-- 只调用游戏客户端已有的正常操作接口，不修改服务器数据。
-- ============================================================

(function()

for _,name in ipairs({"forge_mercenary_weapon_ret","alloc_mercenary_exp_ret","sign_contract_ret",
    "upgrade_mercenary_wakeup_ret","upgrade_mercenary_force_ret","change_mercenary_exproperty_ret",
    "open_mercenary_artifact_ret","upgrade_mercenary_artifact_ret",
    "query_store_info_ret","buy_store_goods_ret"}) do
    local exists=false
    for _,known in ipairs(A.GROWTH_EVENTS) do if known==name then exists=true;break end end
    if not exists then A.GROWTH_EVENTS[#A.GROWTH_EVENTS+1]=name end
end

local MERC_FORGE_LIMIT=2000
local MERC_LEVEL_LIMIT=6000
local MERC_CONTRACT_LIMIT=2000
local MERC_WASH_LIMIT=20000
local MERC_DELAY=0
local MERC_WAKE_TARGET=5
local MERC_PROGRESS_WAIT=1
-- 测试服资源无限：不设血钻/材料预算，武器强化直接取配置里最高追加档。
local MERC_UNLIMITED=true

local function merc_num(value,fallback)
    local n=tonumber(value)
    if n==nil then return fallback end
    return n
end

local function merc_troop()
    local ok,troop=pcall(require,"logic.troop")
    if not ok or type(troop)~="table" then return nil end
    return troop
end

local function merc_resource(id)
    if not id then return 0 end
    local ok,value=pcall(function() return resource_logic:GetResourceNum(id) end)
    return ok and merc_num(value,0) or 0
end

local function merc_resource_type(name)
    local types=constants.RESOURCE_TYPE or {}
    return types[name]
end

local function merc_has_list(troop)
    if type(troop.mercenary_list)~="table" then return false end
    return next(troop.mercenary_list)~=nil
end

local function merc_name(merc)
    local info=merc.template_info
    if info and info.name then return tostring(info.name) end
    return "佣兵" .. tostring(merc.instance_id)
end

local function merc_key(merc)
    return merc.instance_id or merc.template_id
end

local function merc_level(state,merc)
    local verified=state.level_receipts[merc_key(merc)]
    if verified and verified.row==merc and verified.exp==merc.exp
        and verified.wakeup==merc.wakeup and verified.local_level==merc.level then
        return verified.level
    end
    return merc_num(merc.level,1)
end

local function merc_blocked(state,merc)
    return state.blocked[merc_key(merc)]==true
end

local function merc_block(self,state,merc,reason)
    state.blocked[merc_key(merc)]=true
    self:log(merc_name(merc) .. " 本轮跳过：" .. tostring(reason))
end

local function merc_leader(merc)
    return merc.is_leader and 1 or 0
end

-- ------------------------------------------------------------
-- 觉醒与升级：未觉醒到目标阶的佣兵最高只能到可觉醒等级
-- ------------------------------------------------------------
local function merc_wake_target(merc)
    local info=merc.template_info
    local max_wake=info and merc_num(info.max_wakeup,MERC_WAKE_TARGET) or MERC_WAKE_TARGET
    if max_wake<1 then max_wake=1 end
    return math.min(MERC_WAKE_TARGET,max_wake)
end

local function merc_wake_need_level()
    return math.max(30,merc_num(constants.CAN_WAKEUP_LEVEL,30))
end

local function merc_level_cap(merc)
    local max_level=merc_num(constants.MAX_LEVEL,100)
    if merc_num(merc.wakeup,1)<merc_wake_target(merc) then
        return math.min(max_level,merc_wake_need_level())
    end
    return max_level
end

local function merc_exp_field(row,wakeup)
    local field="wakeup_factor" .. wakeup
    if type(row)=="table" and tonumber(row[field])~=nil then return field end
    if type(row)=="table" and tonumber(row.wakeup_factor1)~=nil then return "wakeup_factor1" end
    return nil
end

-- 一次请求直接升到当前上限，不再按本地经验库存推算「够得着的等级」。
-- 经验不足由服务器回执判定，再走血钻商店一次买 200 份补齐。
-- 服务端拒绝跨级时，只缩小这名佣兵当前觉醒阶段的批量跨度。
local function merc_level_target(state,merc,cap_override)
    local level=merc_level(state,merc)
    local cap=cap_override or merc_level_cap(merc)
    if level>=cap then return nil end
    local target=cap
    local span=state.level_spans[merc_key(merc)]
    if span and span.wakeup==merc.wakeup then target=math.min(cap,level+span.size) end
    local rows=config_manager.mercenary_exp_config or {}
    local field=merc_exp_field(rows[level],merc_num(merc.wakeup,1))
    local cost=0
    if field then
        local step=rows[target-1]
        if step and tonumber(step[field]) then
            cost=math.max(0,tonumber(step[field])-(tonumber(merc.exp) or 0))
        end
    end
    return target,cost
end

local function merc_level_pick(state,troop)
    local exp_have=merc_resource(merc_resource_type("exp"))
    local best
    state.level_left=0
    state.capped=0
    for _,merc in pairs(troop.mercenary_list) do
        if not merc_blocked(state,merc) and not state.level_unconfirmed[merc_key(merc)] then
            local level=merc_level(state,merc)
            if level<merc_level_cap(merc) then
                state.level_left=state.level_left+1
                local target,cost=merc_level_target(state,merc)
                if target then
                    local leader=merc_leader(merc)
                    if not best or leader>best.leader
                        or (leader==best.leader and level<best.level)
                        or (leader==best.leader and level==best.level and cost<best.cost) then
                        best={merc=merc,id=merc.instance_id,level=level,target=target,cost=cost,leader=leader}
                    end
                end
            else
                state.capped=state.capped+1
            end
        end
    end
    state.exp_have=exp_have
    return best
end

local function merc_wake_conf(merc)
    local conf=(config_manager.wakeup_info_config or {})[merc_num(merc.wakeup,1)]
    if type(conf)~="table" then return nil end
    return {gold=merc_num(conf.gold_coin,0),resource_id=conf.resource_id,
        resource_num=merc_num(conf.resource_num,0)}
end

local function merc_wake_affordable(merc,gold_have)
    local conf=merc_wake_conf(merc)
    if not conf then return true end  -- 测试服资源无限，配置缺失时交给服务器判定
    if conf.gold>0 and gold_have<conf.gold then return false end
    if conf.resource_num>0 and merc_resource(conf.resource_id)<conf.resource_num then return false end
    return true
end

local function merc_wake_pick(state,troop)
    local gold_have=merc_resource(merc_resource_type("gold_coin"))
    local need_level=merc_wake_need_level()
    local best
    state.wake_left=0
    state.need_level=0
    for _,merc in pairs(troop.mercenary_list) do
        if not merc_blocked(state,merc) and not state.wake_unconfirmed[merc_key(merc)] then
            local wake=merc_num(merc.wakeup,1)
            if wake<merc_wake_target(merc) then
                state.wake_left=state.wake_left+1
                local level=merc_level(state,merc)
                if level>=need_level then
                    -- 不再做本地金币/材料预检：私服的实际消耗与客户端读数不一定一致，
                    -- 直接发请求由服务器判定（与「洗界限突破」里的觉醒路径保持一致）。
                    -- 服务器真报资源不足时，交给下方的血钻商店补货流程。
                    local leader=merc_leader(merc)
                    if not best or leader>best.leader
                        or (leader==best.leader and wake<best.wake)
                        or (leader==best.leader and wake==best.wake and level>best.level) then
                        best={merc=merc,id=merc.instance_id,wake=wake,level=level,leader=leader}
                    end
                else
                    state.need_level=state.need_level+1
                end
            end
        end
    end
    state.gold_have=gold_have
    return best
end

-- ------------------------------------------------------------
-- 契约：只处理当前上阵阵容，主角由全队契约数自动提升，不单独签
-- ------------------------------------------------------------
local function merc_formation_id(troop)
    local id=merc_num(troop.cur_formation_id,0)
    if id<=0 then id=merc_num(troop.client_formation_id,0) end
    return id
end

local function merc_in_formation(troop,merc,id)
    if id<=0 then return false end
    if type(troop.IsMercenaryInFormation)=="function" then
        local ok,in_formation=pcall(function() return troop:IsMercenaryInFormation(merc,id) end)
        if ok then return in_formation==true end
    end
    local info=merc_num(merc.formation_info,0)
    local bit=2^(id-1)
    return math.floor(info/bit)%2==1
end

local function merc_contract_max()
    local max=merc_num(constants.MAX_CONTRACT_LV,0)
    local conf=config_manager.mercenary_contract_config
    if type(conf)=="table" then
        for level in pairs(conf) do
            if type(level)=="number" and level>max then max=level end
        end
    end
    if max<=0 then max=2 end
    return max
end

local function merc_contract_unlocked(troop)
    if type(troop.IsContractUnlock)~="function" then return true end
    local ok,unlocked=pcall(function() return troop:IsContractUnlock() end)
    return not ok or unlocked~=false
end

local function merc_contract_can_sign(troop,merc,next_lv)
    local tid=merc.template_id
    if not tid and merc.template_info then tid=merc.template_info.ID or merc.template_info.id end
    if type(troop.CanContractLv)=="function" then
        local ok,can=pcall(function() return troop:CanContractLv(tid,next_lv) end)
        if ok then return can and true or false end
    end
    local level_conf=(config_manager.mercenary_contract_config or {})[next_lv]
    return type(level_conf)=="table" and level_conf[tid]~=nil
end

local function merc_contract_pick(state,troop)
    local max=merc_contract_max()
    local fid=merc_formation_id(troop)
    state.contract_left=0
    state.contract_capped=0
    state.contract_formation=fid
    state.no_formation=false
    state.contract_locked=false
    if fid<=0 then state.no_formation=true;return nil end
    if state.contract_unlock==nil then state.contract_unlock=merc_contract_unlocked(troop) end
    if not state.contract_unlock then state.contract_locked=true;return nil end
    local best
    for _,merc in pairs(troop.mercenary_list) do
        if not merc.is_leader and not merc_blocked(state,merc) and merc_in_formation(troop,merc,fid) then
            local lv=merc_num(merc.contract_lv,0)
            if lv>=max then
                state.contract_capped=state.contract_capped+1
            else
                state.contract_left=state.contract_left+1
                if merc_contract_can_sign(troop,merc,lv+1) then
                    local level=merc_num(merc.level,1)
                    local bp=merc_num(merc.battle_point,0)
                    if not best or lv<best.lv
                        or (lv==best.lv and level>best.level)
                        or (lv==best.lv and level==best.level and bp>best.bp) then
                        best={merc=merc,id=merc.instance_id,lv=lv,level=level,bp=bp}
                    end
                end
            end
        end
    end
    return best
end

-- ------------------------------------------------------------
-- 洗界限突破：属性类型、突破上限、上阵目标
-- ------------------------------------------------------------
local function merc_prop_type_id(name)
    local names=constants.PROPERTY_TYPE_NAME
    if type(names)=="table" then
        for id,n in pairs(names) do
            if tostring(n)==tostring(name) then return tonumber(id) or id end
        end
    end
    return tonumber(name)
end

local function merc_force_max()
    return merc_num(constants.MAX_FORCE_LEVEL,0)
end

local function merc_wash_targets(state,troop)
    local fid=merc_formation_id(troop)
    local list={}
    state.no_formation=fid<=0
    if fid<=0 then return list end
    for _,merc in pairs(troop.mercenary_list) do
        if not merc.is_leader and not merc_blocked(state,merc) and merc_in_formation(troop,merc,fid) then
            list[#list+1]=merc
        end
    end
    table.sort(list,function(a,b) return merc_num(a.instance_id,0)<merc_num(b.instance_id,0) end)
    return list
end

local function merc_wash_match(state,t,v)
    if state.prop_type==nil or t==nil or v==nil then return false end
    return merc_num(t,-1)==merc_num(state.prop_type,-2) and merc_num(v,0)>=state.prop_target
end

-- 佣兵身上已经生效的属性就达标了：这一轮完全不用动他，避免白耗材料。
local function merc_current_matches(state,merc)
    return merc_wash_match(state,merc.ex_prop_type,merc.ex_prop_val)
end

-- 上一次洗出的候选属性已达标却没确认：只补一次替换，不重新洗。
local function merc_pending_matches(state,merc)
    return merc.ex_prop_type_temp~=nil and merc.ex_prop_val_temp~=nil
        and merc_wash_match(state,merc.ex_prop_type_temp,merc.ex_prop_val_temp)
end

-- 回执判定：候选值存在时按候选值判、需要替换确认；否则属性已直接生效，判完即可收工。
local function merc_wash_result_match(state,response)
    if response.ex_prop_type_temp~=nil and response.ex_prop_val_temp~=nil then
        return merc_wash_match(state,response.ex_prop_type_temp,response.ex_prop_val_temp),true
    end
    return merc_wash_match(state,response.ex_prop_type,response.ex_prop_val),false
end

local function merc_wash_action(state,troop,merc)
    local key=merc_key(merc)
    if merc_current_matches(state,merc) then return nil,"已达到目标属性" end
    local wake=merc_num(merc.wakeup,1)
    local need_level=merc_wake_need_level()

    -- 前置 1：觉醒到目标阶（先升到可觉醒等级，再觉醒）
    if wake<merc_wake_target(merc) then
        local level=merc_level(state,merc)
        if level<need_level then
            local target=merc_level_target(state,merc,need_level)
            if target then
                return {kind="wash_level",event="alloc_mercenary_exp_ret",
                    payload={alloc_mercenary_exp={mercenary_id=merc.instance_id,target_level=target}},
                    desc="补齐等级 " .. merc_name(merc) .. "（" .. level .. "→" .. target .. "）"}
            end
            return nil,"经验不足，升不到觉醒等级"
        end
        return {kind="wash_wake",event="upgrade_mercenary_wakeup_ret",
            payload={upgrade_mercenary_wakeup={mercenary_id=merc.instance_id}},
            desc="补齐觉醒 " .. merc_name(merc) .. "（" .. wake .. "→" .. (wake+1) .. " 阶）"}
    end

    -- 前置 2：界限突破到满（客户端硬性要求觉醒 ≥ 5 阶）
    local fmax=merc_force_max()
    if not state.force_done[key] and (fmax<=0 or merc_num(merc.force_lv,0)<fmax) then
        if wake<5 then return nil,"觉醒未满 5 阶，游戏不允许界限突破" end
        return {kind="wash_force",event="upgrade_mercenary_force_ret",
            payload={upgrade_mercenary_force={mercenary_id=merc.instance_id}},
            desc="界限突破 " .. merc_name(merc) .. "（" .. merc_num(merc.force_lv,0) .. "→" ..
                (merc_num(merc.force_lv,0)+1) .. "）"}
    end

    -- 达标后确认替换
    if state.replace_pending[key] then
        return {kind="wash_replace",event="change_mercenary_exproperty_ret",
            payload={change_mercenary_exproperty={mercenary_id=merc.instance_id,is_replace=true}},
            desc="确认替换 " .. merc_name(merc) .. " 的达标属性"}
    end

    return {kind="wash_roll",event="change_mercenary_exproperty_ret",
        payload={change_mercenary_exproperty={mercenary_id=merc.instance_id}},
        desc="洗练 " .. merc_name(merc) .. "（目标 " .. tostring(state.prop_label or state.prop_name) ..
            " ≥ " .. state.prop_target .. "）"}
end

-- ------------------------------------------------------------
-- 宝具：开启与强化
-- ------------------------------------------------------------
local function merc_artifact_open_type(troop)
    local ticket=merc_resource_type("forge_ticket")
    if ticket and merc_resource(ticket)>=1 then return "ticket" end
    return nil
end

local function merc_artifact_can_open(troop,merc,open_type)
    local info=merc.template_info
    if not info or not info.have_artifact or merc.is_open_artifact then return false end
    if merc_num(merc.weapon_lv,0)<merc_num(constants.CAN_OPEN_ARTIFACT_WEAPON_LV,20) then return false end
    if type(troop.CheckOpenArtifactResource)~="function" then return false end
    local ok,ready=pcall(function() return troop:CheckOpenArtifactResource(open_type,false) end)
    return ok and ready==true
end

local function merc_artifact_upgrade_conf(merc)
    local info=merc.template_info
    if not info or not info.have_artifact_upgrade or not merc.is_open_artifact then return nil end
    local tid=merc.template_id or info.ID or info.id
    local all=(config_manager.mercenary_artifact_config or {})[tid]
    if type(all)~="table" then return nil end
    local level=merc_num(merc.artifact_lv,1)
    local max_level=0
    for key in pairs(all) do if type(key)=="number" and key>max_level then max_level=key end end
    if max_level<=0 or level>=max_level then return nil end
    local conf=all[level]
    if type(conf)~="table" or type(conf.cost_list)~="table" then return nil end
    for id,num in pairs(conf.cost_list) do
        if merc_resource(id)<(tonumber(num) or 0) then return nil end
    end
    return {level=level,max_level=max_level}
end

-- ------------------------------------------------------------
-- 选人：强化佣兵（宝具锻造 → 武器强化 → 宝具强化）
-- ------------------------------------------------------------
local function merc_gear_pick(state,troop)
    local max_lv=merc_num(constants.MAX_WEAPON_LV,30)
    local artifact_lv=merc_num(constants.CAN_OPEN_ARTIFACT_WEAPON_LV,20)
    local open_type=merc_artifact_open_type(troop)
    local best_open,best_forge,best_upgrade
    state.gear_left=0
    for _,merc in pairs(troop.mercenary_list) do
        if not merc.is_leader and not merc_blocked(state,merc) then
            local weapon=merc_num(merc.weapon_lv,0)
            local info=merc.template_info

            if merc_artifact_can_open(troop,merc,open_type) then
                state.gear_left=state.gear_left+1
                local leader=merc_leader(merc)
                if not best_open or leader>best_open.leader or (leader==best_open.leader and weapon<best_open.weapon) then
                    best_open={merc=merc,id=merc.instance_id,weapon=weapon,open_type=open_type,leader=leader}
                end
            end

            local forge_blocked=weapon>=max_lv
            if not forge_blocked and info and info.have_artifact and weapon>=artifact_lv and not merc.is_open_artifact then
                forge_blocked=true
            end
            if not forge_blocked then
                state.gear_left=state.gear_left+1
                local affordable=false
                if (config_manager.weapon_forge_config or {})[weapon+1] then
                    local ok,ready=pcall(function() return troop:CheckForgeWeaponResource(weapon,false) end)
                    affordable=(ok and ready==true)
                end
                if affordable then
                    local bp=merc_num(merc.battle_point,0)
                    local leader=merc_leader(merc)
                    if not best_forge or leader>best_forge.leader
                        or (leader==best_forge.leader and weapon<best_forge.weapon)
                        or (leader==best_forge.leader and weapon==best_forge.weapon and bp>best_forge.bp) then
                        best_forge={merc=merc,id=merc.instance_id,weapon=weapon,bp=bp,leader=leader}
                    end
                end
            end

            local upgrade=merc_artifact_upgrade_conf(merc)
            if upgrade then
                state.gear_left=state.gear_left+1
                local leader=merc_leader(merc)
                if not best_upgrade or leader>best_upgrade.leader
                    or (leader==best_upgrade.leader and upgrade.level<best_upgrade.level) then
                    best_upgrade={merc=merc,id=merc.instance_id,level=upgrade.level,leader=leader}
                end
            end
        end
    end
    if best_open then return {kind="open",pick=best_open} end
    if best_forge then return {kind="forge",pick=best_forge} end
    if best_upgrade then return {kind="upgrade",pick=best_upgrade} end
    return nil
end

local function merc_forge_extra()
    if not MERC_UNLIMITED then return 0 end
    local best=0
    for key in pairs(config_manager.weapon_forge_extra_config or {}) do
        if type(key)=="number" and key>best then best=key end
    end
    return best
end

-- ------------------------------------------------------------
-- 启动
-- ------------------------------------------------------------
local function merc_start(self,mode,intro)
    self:cancel_task()
    self.merc_state={mode=mode,attempts=0,ok=0,fail=0,forged=0,opened=0,upgraded=0,
        leveled=0,signed=0,waked=0,force_up=0,washed=0,replaced=0,skipped=0,already=0,
        level_left=0,capped=0,gear_left=0,wake_left=0,need_level=0,
        contract_left=0,contract_capped=0,
        blocked={},done={},replace_pending={},force_done={},level_spans={},level_receipts={},
        level_unconfirmed={},wake_unconfirmed={},unconfirmed=0,started=task_now(self),
        supply=nil,supply_tries=0,supplied=0}
    self:log(intro)
    return true
end

function A:start_merc_gear()
    return merc_start(self,"gear","开始强化佣兵：武器到 20 级自动锻造宝具，之后继续强化武器，最后强化宝具")
end

function A:start_merc_grow()
    return merc_start(self,"grow","开始佣兵养成：升到可觉醒等级就觉醒，觉醒到 5 阶后升满级，最后自动签契约")
end

function A:start_merc_contract()
    return merc_start(self,"contract","开始自动契约：只给当前上阵阵容签订契约，签到满阶")
end

function A:start_merc_wash(prop_name,target_value)
    local ok=merc_start(self,"wash","开始洗界限突破：上阵阵容（除主角）自动补齐觉醒与突破，然后洗属性")
    if not ok then return false end
    local state=self.merc_state
    state.prop_name=prop_name
    state.prop_type=merc_prop_type_id(prop_name)
    state.prop_target=merc_num(target_value,0)
    state.prop_label=prop_name
    if state.prop_type==nil then
        self:log("无法识别目标属性：" .. tostring(prop_name) .. "，已停止")
        self.merc_state=nil
        return false
    end
    return true
end

-- ------------------------------------------------------------
-- 回执处理
-- ------------------------------------------------------------
local function merc_handle_result(self,state,pending,result,response)
    local kind=pending.kind
    local key=pending.merc and merc_key(pending.merc) or nil

    if kind=="wash_force" and result=="force_lv_reach_max" then
        if key then state.force_done[key]=true end
        return nil
    end
    if result=="success" then
        state.ok=state.ok+1
        if kind=="merc_forge" then state.forged=state.forged+1
        elseif kind=="merc_open" then state.opened=state.opened+1
        elseif kind=="merc_upgrade" then state.upgraded=state.upgraded+1
        elseif kind=="merc_level" then state.leveled=state.leveled+1
        elseif kind=="merc_contract" then state.signed=state.signed+1
        elseif kind=="merc_wake" or kind=="wash_wake" then state.waked=state.waked+1
        elseif kind=="wash_level" then state.leveled=state.leveled+1
        elseif kind=="wash_force" then state.force_up=state.force_up+1
        elseif kind=="wash_roll" then
            state.washed=state.washed+1
            local matched,needs_replace=merc_wash_result_match(state,response or {})
            if matched then
                if needs_replace then
                    if key then state.replace_pending[key]=true end
                else
                    -- 服务器已经把达标属性直接生效，不再多发一次替换请求。
                    state.replaced=state.replaced+1
                    if key then state.done[key]=true end
                end
            end
        elseif kind=="wash_replace" then
            state.replaced=state.replaced+1
            if key then
                state.done[key]=true
                state.replace_pending[key]=nil
            end
        end
        return nil
    end
    if kind=="merc_forge" and result=="forge_failure" then
        state.fail=state.fail+1
        return nil
    end
    if (kind=="merc_level" or kind=="wash_level")
        and (result=="illegal_operation" or result=="failure") then
        -- 不做「一级一级升」的回退：每次把允许跨度减半，减到 2 级仍被拒就停下并说明。
        local last=pending.target_level-pending.before
        local next_span=math.floor(last/2)
        if next_span<2 then
            return reply(self,"服务器不接受一次升多级（已试到一次 "..last.." 级），已停止，没有改为逐级",
                {done=true,error=true,partial=state.ok>0})
        end
        state.level_spans[key]={size=next_span,wakeup=pending.wakeup}
        self:log("服务器不接受一次升 "..last.." 级，改为一次最多升 "..next_span.." 级（不逐级）")
        return "retry"
    end
    if kind=="wash_replace" then
        -- 替换没被接受时保留上一次达标结果，不重复洗，交给用户手动确认。
        if key then
            state.done[key]=true
            state.replace_pending[key]=nil
        end
        self:log("达标属性的替换确认未被接受：" .. tostring(result) .. "，已保留结果")
        return nil
    end
    if kind=="merc_contract" then
        if result=="contract_unlock_first" then
            return reply(self,"契约尚未解锁（需要累计招募次数），已停止",{done=true,partial=state.ok>0})
        end
        return "skip"
    end
    if kind:match("^wash_") then
        -- 洗练流程里单个佣兵的条件/资源问题只跳过该佣兵，继续处理其他人。
        return "skip"
    end
    if result=="not_enough_resource" or result=="not_enough_gold_coin" or result=="not_enough_blood_diamond"
        or result=="not_enough_soul" or result=="not_enough_count" then
        return reply(self,"资源不足，已停止（成功" .. state.ok .. "次）",{done=true,partial=state.ok>0})
    end
    if result=="wakeup_reach_max" or result=="wakeup_not_enough_level"
        or result=="weapon_lv_reach_max" or result=="mercenary_is_mining"
        or result=="cant_forge_weapon" or result=="not_open_artifact"
        or result=="mercenary_not_can_artifact_upgrade" or result=="already_open_artifact"
        or result=="mercenary_not_has_artifact" or result=="mercenary_open_artifact_weapon_lv_not_enough" then
        return "skip"
    end
    return reply(self,"游戏拒绝了本次操作：" .. result,{done=true,error=true,partial=state.ok>0})
end

local merc_progress_fields={merc_level="level",wash_level="level",merc_wake="wakeup",wash_wake="wakeup",
    merc_contract="contract_lv",wash_force="force_lv"}
local merc_progress_labels={level="等级",wakeup="觉醒阶数",contract_lv="契约阶数",force_lv="界限突破等级"}

local function merc_send(self,state,event,kind,description,payload,extra)
    local sent
    self:task_request(event,kind,function()
        local pending=self._task_pending
        local merc=extra and extra.merc
        pending.merc=merc
        pending.description=description
        pending.progress_field=merc_progress_fields[kind]
        if merc and pending.progress_field then
            local id=tostring(merc.instance_id)
            pending.accept_response=function(response)
                return response.mercenary_id==nil or tostring(response.mercenary_id)==id
            end
            pending.wakeup=merc.wakeup
            if pending.progress_field=="level" then
                pending.before=merc_level(state,merc)
                pending.target_level=payload.alloc_mercenary_exp.target_level
            else
                pending.before=merc_num(merc[pending.progress_field],pending.progress_field=="wakeup" and 1 or 0)
            end
        end
        sent=network:Send(payload)
    end)
    if sent==false then self._task_pending=nil;return false end
    return true
end

local function merc_check_progress(self,state,troop,pending,response)
    local merc=pending.merc
    local id=merc.instance_id
    local key=merc_key(merc)
    local field=pending.progress_field
    local list=troop.mercenary_list or {}
    local current=list[id] or list[tostring(id)]
    if not current then
        for _,row in pairs(list) do
            if tostring(row.instance_id)==tostring(id) then current=row;break end
        end
    end
    local actual=current and tonumber(current[field])
    if actual and actual>pending.before then return true end

    -- 某些回执带服务端等级。只引用对应佣兵明确给出的等级，保留游戏原生数据。
    local reported=field=="level" and tonumber(response.level)
    if current and reported and response.mercenary_id~=nil and pending.accept_response(response)
        and reported==math.floor(reported) and reported>pending.before and reported<=pending.target_level then
        state.level_receipts[key]={row=current,local_level=current.level,exp=current.exp,wakeup=current.wakeup,level=reported}
        return true
    end
    if task_now(self)-(pending.received_at or pending.sent_at)<MERC_PROGRESS_WAIT then return nil end

    local reason="收到成功回执，但"..merc_progress_labels[field].."未确认提升（原值 "..pending.before..
        "，当前 "..tostring(actual or "未同步").."）"
    state.unconfirmed=state.unconfirmed+1
    if pending.kind=="merc_level" or pending.kind=="merc_wake" then
        if field=="level" then state.level_unconfirmed[key]=true else state.wake_unconfirmed[key]=true end
        self:log(merc_name(merc).." 本轮跳过该养成步骤："..reason)
    else
        merc_block(self,state,merc,reason)
        if state.mode=="wash" then state.skipped=state.skipped+1 end
    end
    return false
end

-- ------------------------------------------------------------
-- 血钻商店补货：经验/金币等材料不足时，到商店买完再重试原操作。
-- 直接发协议（不走 store:BuyGoods），避免客户端本地血钻余额误判。
-- ------------------------------------------------------------
local MERC_SUPPLY_MAX=30
-- 客户端商店允许 num <= 500；一次买满 200 份，不一次只补一点点。
local MERC_SUPPLY_NUM=200

local function merc_store()
    local ok,store=pcall(require,"logic.store")
    if not ok or type(store)~="table" then return nil end
    return store
end

local function merc_is_resource_error(result)
    return result=="not_enough_resource" or result=="not_enough_gold_coin"
        or result=="not_enough_soul" or result=="not_enough_soul_chip"
        or result=="not_enough_gold" or result=="not_enough_exp"
        or result=="resource_is_not_enough" or result=="not_enough_blood_diamond"
end

local function merc_supply_candidates(kind,reason,merc)
    local rt=constants.RESOURCE_TYPE or {}
    local ids={}
    local function add(name) if rt[name] then ids[#ids+1]=rt[name] end end
    if kind=="merc_level" or kind=="wash_level" or kind=="exp_short" then
        add("exp")
    elseif kind=="merc_wake" or kind=="wash_wake" then
        local conf=merc and merc_wake_conf(merc)
        local gold=rt.gold_coin
        if reason=="not_enough_gold_coin" or reason=="not_enough_gold" then
            add("gold_coin")
        elseif conf and conf.resource_id and conf.resource_num>merc_resource(conf.resource_id)
            and merc_resource(gold)>=conf.gold then
            ids[#ids+1]=conf.resource_id
        else
            add("gold_coin")
            if conf and conf.resource_id and conf.resource_num>0 and conf.resource_id~=gold then
                ids[#ids+1]=conf.resource_id
            end
        end
    elseif kind=="wash_force" then
        add("soul_chip");add("golem");add("gold_coin")
    elseif kind=="wash_roll" or kind=="wash_replace" then
        add("gold_coin");add("golem")
    else
        add("gold_coin");add("exp")
    end
    return ids
end

local function merc_supply_goods(store,ids)
    local goods=store and store.goods_list or {}
    local rtype=(constants.STORE_GOODS_TYPE or {}).resource
    for _,id in ipairs(ids or {}) do
        for _,info in ipairs(goods) do
            if type(info)=="table" and info.id~=nil
                and merc_num(info.data,-1)==merc_num(id,-2)
                and (rtype==nil or info.type==nil or merc_num(info.type,-1)==merc_num(rtype,-2)) then
                return info
            end
        end
    end
    return nil
end

local function merc_supply_buy(self,state,info,num)
    local ok=merc_send(self,state,"buy_store_goods_ret","merc_store_buy",
        "血钻商店补货 ×"..num,
        {buy_store_goods={goods_id=info.id,num=num}})
    if not ok then
        state.supply=nil
        return reply(self,"血钻商店购买请求未被接受，稍后重试",{wait=true,delaySeconds=0.2})
    end
    return reply(self,"血钻商店补货中（"..tostring(info.id).." ×"..num.."）",{wait=true,delaySeconds=MERC_DELAY})
end

-- 返回 nil 表示当前环境没有商店模块，沿用原来的「资源不足即结束」逻辑。
local function merc_supply_start(self,state,kind,reason,merc)
    local store=merc_store()
    if not store then return nil end
    -- 商店回执没注册时不要强行补给，沿用原来的资源不足结束逻辑。
    local listener=network.event_listener
    if type(listener)~="table" or type(listener["query_store_info_ret"])~="function"
        or type(listener["buy_store_goods_ret"])~="function" then return nil end
    state.supply_tries=(state.supply_tries or 0)+1
    if state.supply_tries>MERC_SUPPLY_MAX then
        return reply(self,"血钻商店已补货 "..MERC_SUPPLY_MAX.." 次仍资源不足（"..tostring(reason).."），已停止",
            {done=true,error=true,partial=state.ok>0})
    end
    local ids=merc_supply_candidates(kind,reason,merc)
    state.supply={ids=ids,num=MERC_SUPPLY_NUM,kind=kind}
    self:log("材料不足（"..tostring(reason).."），到血钻商店补货（第 "..state.supply_tries.." 次）")
    local info=merc_supply_goods(store,ids)
    if info then return merc_supply_buy(self,state,info,state.supply.num) end
    local ok=merc_send(self,state,"query_store_info_ret","merc_store_query","读取血钻商店商品",
        {query_store_info={time_stamp=merc_num(store.time_stamp,0)}})
    if not ok then
        state.supply=nil
        return reply(self,"读取血钻商店失败，稍后重试",{wait=true,delaySeconds=0.2})
    end
    return reply(self,"材料不足（"..tostring(reason).."），正在读取血钻商店商品",{wait=true,delaySeconds=MERC_DELAY})
end

local function merc_supply_step(self,state,kind,response)
    local store=merc_store()
    local result=tostring(response.result or "success")
    if kind=="merc_store_query" then
        local info=state.supply and merc_supply_goods(store,state.supply.ids) or nil
        if not info then
            state.supply=nil
            return reply(self,"血钻商店里没有对应的养成材料，已停止",
                {done=true,error=true,partial=state.ok>0})
        end
        return merc_supply_buy(self,state,info,state.supply.num)
    end
    if result~="success" then
        local num=state.supply and state.supply.num or MERC_SUPPLY_NUM
        if num>1 and state.supply and (result=="not_enough_buy_count"
            or result=="not_enough_resource" or result=="not_sale") then
            -- 限购时按该商品剩余可买数一次买满；取不到就整批减半，始终保持成批购买。
            local info=merc_supply_goods(store,state.supply.ids)
            local remain=info and (merc_num(info.num,0)-merc_num(info.already_buy_count,0)) or 0
            local next_num=remain>0 and math.min(num,remain) or math.floor(num/2)
            if next_num>1 and next_num<num and (state.supply.retries or 0)<3 then
                state.supply.num=next_num
                state.supply.retries=(state.supply.retries or 0)+1
                if info then return merc_supply_buy(self,state,info,next_num) end
            end
        end
        state.supply=nil
        return reply(self,"血钻商店补货失败："..result.."，已停止",{done=true,error=true,partial=state.ok>0})
    end
    state.supplied=(state.supplied or 0)+1
    state.supply=nil
    self:log("血钻商店补货成功（第 "..state.supplied.." 次），继续任务")
    return reply(self,"血钻商店补货成功，继续任务",{wait=true,delaySeconds=MERC_DELAY})
end

local function merc_contract_reason(state,mode)
    local reasons={}
    if mode=="grow" then
        if (state.level_left or 0)>0 then reasons[#reasons+1]="经验不足" end
        if (state.wake_left or 0)>0 then reasons[#reasons+1]="金币或觉醒材料不足" end
    end
    if state.no_formation then reasons[#reasons+1]="尚未取得当前上阵阵容"
    elseif state.contract_locked then reasons[#reasons+1]="契约尚未解锁"
    elseif (state.contract_left or 0)>0 then reasons[#reasons+1]="部分上阵佣兵暂不可签约"
    elseif (state.contract_capped or 0)>0 then reasons[#reasons+1]="上阵阵容契约已满阶"
    else reasons[#reasons+1]="上阵阵容没有可签约的佣兵" end
    if state.unconfirmed>0 then reasons[#reasons+1]="有 "..state.unconfirmed.." 项未确认提升，详见任务日志" end
    return table.concat(reasons,"，")
end

function A:merc_step()
    local state=self.merc_state
    if not state then return reply(self,"佣兵任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true,delaySeconds=0.5}) end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待战斗结束",{wait=true}) end
    self:hide_panel("reward_panel")

    local troop=merc_troop()
    if not troop then return reply(self,"等待游戏佣兵数据",{wait=true,delaySeconds=0.5}) end

    local pending=self._task_pending
    local skip_merc,skip_reason
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>25 then
                return reply(self,"佣兵任务请求超时，已停止（成功" .. state.ok .. "次）",{done=true,error=true,partial=state.ok>0})
            end
            return reply(self,"等待游戏确认" .. (pending.description or "佣兵操作"),{wait=true})
        end
        local response=pending.response
        local result=tostring(response.result or "success")
        if result=="success" and pending.progress_field then
            local progressed=merc_check_progress(self,state,troop,pending,response)
            if progressed==nil then
                return reply(self,"等待游戏同步"..merc_progress_labels[pending.progress_field],{wait=true,delaySeconds=0.15})
            end
            if not progressed then result=nil end
        end
        self._task_pending=nil
        if pending.kind=="merc_store_query" or pending.kind=="merc_store_buy" then
            return merc_supply_step(self,state,pending.kind,response)
        end
        if (pending.kind=="merc_level" or pending.kind=="merc_wake" or state.mode=="wash")
            and merc_is_resource_error(result) then
            local supplied=merc_supply_start(self,state,pending.kind,result,pending.merc)
            if supplied then return supplied end
        end
        local outcome=result and merc_handle_result(self,state,pending,result,response)
        if type(outcome)=="table" then return outcome end
        if outcome=="skip" then skip_merc=pending.merc;skip_reason=result end
    end
    if skip_merc then merc_block(self,state,skip_merc,skip_reason) end

    if not merc_has_list(troop) then return reply(self,"等待佣兵列表同步",{wait=true,delaySeconds=0.5}) end
    local mode=state.mode
    local limit=mode=="gear" and MERC_FORGE_LIMIT or (mode=="contract" and MERC_CONTRACT_LIMIT
        or (mode=="wash" and MERC_WASH_LIMIT or MERC_LEVEL_LIMIT))
    if state.attempts>=limit then
        return reply(self,"达到单次上限，已停止（成功" .. state.ok .. "次）",{done=true})
    end

    -- 洗界限突破：逐个处理上阵阵容里除主角以外的佣兵
    if mode=="wash" then
        local targets=merc_wash_targets(state,troop)
        local action,current,supply_reason
        for _,merc in ipairs(targets) do
            local key=merc_key(merc)
            if not state.done[key] and not merc_blocked(state,merc) then
                if merc_current_matches(state,merc) then
                    -- 已经达标的佣兵直接跳过，一次请求都不发。
                    state.done[key]=true
                    state.already=state.already+1
                    self:log(merc_name(merc) .. " 已达标，跳过洗练")
                else
                    if merc_pending_matches(state,merc) then state.replace_pending[key]=true end
                    local a,reason=merc_wash_action(state,troop,merc)
                    if a then action=a;current=merc;break end
                    if reason and reason:find("经验不足",1,true) then supply_reason=reason;break end
                    merc_block(self,state,merc,reason or "当前条件不满足")
                    state.skipped=state.skipped+1
                end
            end
        end
        if supply_reason and not action then
            local supplied=merc_supply_start(self,state,"exp_short",supply_reason)
            if supplied then return supplied end
        end
        if action and current then
            state.attempts=state.attempts+1
            local ok=merc_send(self,state,action.event,action.kind,action.desc,action.payload,{merc=current})
            if not ok then return reply(self,"游戏暂未接受请求，稍后重试",{wait=true,delaySeconds=0.2}) end
            return reply(self,action.desc,{wait=true,delaySeconds=MERC_DELAY})
        end
        local tail=state.no_formation and "尚未取得当前上阵阵容" or "上阵阵容已全部处理"
        return reply(self,"洗界限突破结束：" .. tail .. "（达标" .. state.replaced .. "名，已达标跳过" ..
            state.already .. "名，另跳过" .. state.skipped .. "名，洗" .. state.washed ..
            "次，觉醒" .. state.waked .. "次，突破" .. state.force_up .. "次）",
            {done=true,partial=state.unconfirmed>0 or (state.replaced==0 and state.already==0 and state.skipped>0)})
    end

    if mode=="grow" or mode=="contract" then
        if mode=="grow" then
            local pick=merc_level_pick(state,troop)
            if pick then
                state.attempts=state.attempts+1
                local target=pick.target
                local description="升级" .. merc_name(pick.merc) .. "（" .. pick.level .. "→" .. target .. "）"
                local ok=merc_send(self,state,"alloc_mercenary_exp_ret","merc_level",description,
                    {alloc_mercenary_exp={mercenary_id=pick.id,target_level=target}},{merc=pick.merc})
                if not ok then return reply(self,"游戏暂未接受升级请求，稍后重试",{wait=true,delaySeconds=0.2}) end
                return reply(self,description,{wait=true,delaySeconds=MERC_DELAY})
            end
            if (state.level_left or 0)>0 then
                local supplied=merc_supply_start(self,state,"exp_short","经验不足")
                if supplied then return supplied end
            end
            local wake=merc_wake_pick(state,troop)
            if wake then
                state.attempts=state.attempts+1
                local description="觉醒" .. merc_name(wake.merc) .. "（" .. wake.wake .. "→" .. (wake.wake+1) .. " 阶）"
                local ok=merc_send(self,state,"upgrade_mercenary_wakeup_ret","merc_wake",description,
                    {upgrade_mercenary_wakeup={mercenary_id=wake.id}},{merc=wake.merc})
                if not ok then return reply(self,"游戏暂未接受觉醒请求，稍后重试",{wait=true,delaySeconds=0.2}) end
                return reply(self,description,{wait=true,delaySeconds=MERC_DELAY})
            end
        end

        local contract=merc_contract_pick(state,troop)
        if contract then
            state.attempts=state.attempts+1
            local description="签约" .. merc_name(contract.merc) .. "（" .. contract.lv .. "→" .. (contract.lv+1) .. " 阶）"
            local ok=merc_send(self,state,"sign_contract_ret","merc_contract",description,
                {sign_contract={mercenary_id=contract.id}},{merc=contract.merc})
            if not ok then return reply(self,"游戏暂未接受契约请求，稍后重试",{wait=true,delaySeconds=0.2}) end
            return reply(self,description,{wait=true,delaySeconds=MERC_DELAY})
        end

        local prefix=mode=="contract" and "自动契约结束" or "佣兵养成结束"
        return reply(self,prefix .. "：" .. merc_contract_reason(state,mode) ..
            "（升级" .. state.leveled .. "次，觉醒" .. state.waked .. "次，契约" .. state.signed .. "次）",
            {done=true,partial=state.unconfirmed>0 or (state.ok==0 and ((state.level_left or 0)>0 or (state.wake_left or 0)>0
                or (state.contract_left or 0)>0))})
    end

    local choice=merc_gear_pick(state,troop)
    if not choice then
        local blocked_count=0
        for _,merc in pairs(troop.mercenary_list) do if merc_blocked(state,merc) then blocked_count=blocked_count+1 end end
        local tail=state.gear_left>0 and "强化或宝具材料不足" or "所有佣兵武器与宝具已满级"
        if state.gear_left==0 and blocked_count>0 then tail="本轮可尝试的佣兵已跳过（" .. blocked_count .. " 名，详见记录）" end
        return reply(self,"强化佣兵结束：" .. tail .. "（武器" .. state.forged .. " 次，宝具锻造" .. state.opened ..
            " 件，宝具强化" .. state.upgraded .. " 次）",{done=true,partial=state.ok==0 and state.gear_left>0})
    end

    state.attempts=state.attempts+1
    local pick=choice.pick
    local description,event,kind,payload
    if choice.kind=="open" then
        description="锻造宝具" .. merc_name(pick.merc) .. "（" .. (pick.open_type=="ticket" and "锻造券" or "金币与灵魂水晶") .. "）"
        event="open_mercenary_artifact_ret";kind="merc_open"
        payload={open_mercenary_artifact={mercenary_id=pick.id,open_type=pick.open_type}}
    elseif choice.kind=="forge" then
        local extra=merc_forge_extra()
        if troop.forge_weapon_inflight then pcall(function() troop:FinishForgeWeapon(troop.forge_weapon_inflight) end) end
        description="强化" .. merc_name(pick.merc) .. "武器（" .. pick.weapon .. "→" .. (pick.weapon+1) .. "）"
        event="forge_mercenary_weapon_ret";kind="merc_forge"
        payload={forge_mercenary_weapon={mercenary_id=pick.id,extra_chance=extra}}
    else
        description="强化宝具" .. merc_name(pick.merc) .. "（" .. pick.level .. "→" .. (pick.level+1) .. "）"
        event="upgrade_mercenary_artifact_ret";kind="merc_upgrade"
        payload={upgrade_mercenary_artifact={mercenary_id=pick.id}}
    end
    local ok=merc_send(self,state,event,kind,description,payload,{merc=pick.merc})
    if not ok then return reply(self,"游戏暂未接受请求，稍后重试",{wait=true,delaySeconds=0.2}) end
    return reply(self,description,{wait=true,delaySeconds=MERC_DELAY})
end

end)()


