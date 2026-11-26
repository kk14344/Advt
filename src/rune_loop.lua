-- Use the live platform price and the game's normal draw, receive and upgrade
-- requests. One pending request at a time; the common pipeline verifies lost
-- write replies before it considers retrying.
do
local function read_runes(self,state)
    state.refresh_bag=false
    self:task_request("query_rune_info_ret","growth_rune_query",function()
        return network:Send({query_rune_info={}})
    end)
    return reply(self,"同步符文背包与当前抽取层",{wait=true})
end

local function send_rune_request(rune,message)
    if type(rune.SendRuneNative)=="function" then return rune:SendRuneNative(message) end
    return network:Send(message)
end

local function bag_space(rune)
    local capacity=tonumber(rune.rune_bag_capacity) or 0
    return math.max(0,capacity-#(rune.rune_list or {}))
end

local function receive_key(rune)
    local ids={}
    for _,info in ipairs(rune.rune_temporary_list or {}) do
        ids[#ids+1]=tostring(info.rune_id)
    end
    return tostring(bag_space(rune))..":"..table.concat(ids,",")
end

local function finish_runes(self,state,reason,failed)
    return reply(self,string.format("符文任务%s · %s · 十连 %d 批，单抽 %d 次 · 强化 %d 批，材料 %d 枚%s",
        failed and "停止" or "结束",reason,state.ten_pulls,state.single_pulls,state.upgrades,state.materials_used,
        state.upgrade_summary and (" · "..state.upgrade_summary) or ""),
        {done=true,error=failed or false,partial=state.drawn==0 and state.upgrades==0})
end

local function upgrade_progress(rune,previous)
    if not previous then return false end
    for _,info in ipairs(rune.rune_list or {}) do
        if tonumber(info.rune_id)==tonumber(previous.id) then
            local level=tonumber(info.level) or 0
            local exp=tonumber(info.exp) or 0
            if level>previous.level or (level==previous.level and exp>previous.exp) then return level,exp end
            return false
        end
    end
    return false
end

local function rune_name(info,conf)
    return tostring(conf.name or conf.title or info.name or ("符文 #"..tostring(info.template_id or info.rune_id)))
end

local function pick_rune_upgrade(self,rune,state)
    local equipped=self:rune_active_equipment(rune)
    if next(equipped)==nil then return end
    local allow_gold=self.cfg.runeUseFlashGold~=false
    local target,target_level
    local candidates={}
    for _,info in ipairs(rune.rune_list or {}) do
        local id=tonumber(info.rune_id)
        if id and equipped[id] then
            local conf=info.template_info or (config_manager.rune_config or {})[tonumber(info.template_id)]
            if conf then
                info.template_info=conf
                local level=tonumber(info.level) or 0
                local max=tonumber(conf.level_limit) or 0
                if level<max then
                    if state.manual_target then
                        candidates[#candidates+1]={id=tostring(info.rune_id),slot=equipped[id],name=rune_name(info,conf),level=level,max=max}
                        if state.target==tostring(info.rune_id) then target=info end
                    elseif not target or level<target_level or (level==target_level and id<tonumber(target.rune_id)) then
                        target,target_level=info,level
                    end
                end
            end
        end
    end
    if not target and (not state.manual_target or state.target or #candidates==0) then return end

    local materials={}
    for _,info in ipairs(rune.rune_list or {}) do
        local id=tonumber(info.rune_id)
        if id and id>0 and not equipped[id] and not self:rune_protected(rune,info) then
            local conf=info.template_info or (config_manager.rune_config or {})[tonumber(info.template_id)]
            if conf then
                info.template_info=conf
                local quality=tonumber(conf.quality) or 0
                local xp=(tonumber(conf.out_exp) or 0)+(tonumber(info.exp) or 0)
                if quality>0 and xp>0 and (allow_gold or quality<GROWTH_FLASH_GOLD_QUALITY) then
                    materials[#materials+1]={info=info,quality=quality,xp=xp,
                        exp_rune=tonumber(conf.type)==GROWTH_RUNE_EXP_TYPE}
                end
            end
        end
    end
    if #materials==0 then return end
    if not target then
        table.sort(candidates,function(a,b) return a.slot==b.slot and a.id<b.id or a.slot<b.slot end)
        return nil,nil,candidates
    end
    -- Use dedicated experience runes first, then the least valuable eligible
    -- spare runes. The flash-gold setting covers both kinds of material.
    table.sort(materials,function(a,b)
        if a.exp_rune~=b.exp_rune then return a.exp_rune end
        if a.quality~=b.quality then return a.quality<b.quality end
        if a.xp~=b.xp then return a.xp<b.xp end
        return tonumber(a.info.rune_id)<tonumber(b.info.rune_id)
    end)
    local selected={}
    local limit=tonumber(constants.MAX_RUNE_BAG_MUILT_SELECT_NUM) or 10
    for i=1,math.min(limit,#materials) do
        selected[i]=materials[i].info
        if type(rune.GetRunePreviewLevelAndExp)=="function" then
            local ok,level=pcall(rune.GetRunePreviewLevelAndExp,rune,target,selected)
            if ok and (tonumber(level) or 0)>=tonumber(target.template_info.level_limit) then break end
        end
    end
    return target,selected
end

local function advance_rune_upgrade(self,state,rune)
    local target,selected,candidates=pick_rune_upgrade(self,rune,state)
    if candidates then
        state.target_choices=candidates
        return reply(self,"等待选择强化目标",{wait=true,delaySeconds=0.5,runeTargets=candidates})
    end
    if not target or #selected==0 then return nil end
    local ids={}
    for i,info in ipairs(selected) do ids[i]=info.rune_id end
    state.last_target={id=target.rune_id,level=tonumber(target.level) or 0,exp=tonumber(target.exp) or 0,
        name=rune_name(target,target.template_info),materials=#ids}
    -- Keep the exact verified material IDs. Native SendRuneNative maintains
    -- the game's busy count and its original acknowledgement handlers.
    self:task_request("upgrade_rune_ret","growth_rune_upgrade",function()
        return send_rune_request(rune,{upgrade_rune={rune_id=target.rune_id,exp_rune_id_list=ids}})
    end)
    return reply(self,"强化已装备符文，使用 "..#ids.." 枚材料",{wait=true})
end

function A:choose_rune_target(id)
    local state=self.rune_state or (self.daily_state and self.daily_state.rune_selection)
    if not state or not state.target_choices or state.target then return false end
    if id=="" then state.target="skip";state.target_choices=nil;return true end
    local equipped=self:rune_active_equipment(require("logic.rune"))
    for _,candidate in ipairs(state.target_choices) do
        if candidate.id==id and equipped[tonumber(id)] then
            state.target=id;state.target_choices=nil;return true
        end
    end
    return false
end

function A:daily_manual_rune(rune)
    local st=self.daily_state
    st.rune_selection=st.rune_selection or {loaded=true,manual_target=true}
    local state=st.rune_selection
    if state.target=="skip" then return self:daily_skip(13,"本轮跳过强化") end
    local sync=self:rune_control_step(state,rune)
    if sync then return sync end
    local target,materials,candidates=pick_rune_upgrade(self,rune,state)
    if candidates then
        state.target_choices=candidates
        return reply(self,"等待选择强化目标",{wait=true,delaySeconds=0.5,runeTargets=candidates})
    end
    if not target then return self:daily_skip(13,"缺少可强化的已装备符文或可用材料") end
    local feed
    for _,info in ipairs(materials) do
        if tonumber(info.template_info.type)==GROWTH_RUNE_EXP_TYPE then feed=info;break end
    end
    if not feed then return self:daily_skip(13,"缺少专用经验符文") end
    return self:daily_request("upgrade_rune_ret","upgrade_rune",{rune_id=target.rune_id,exp_rune_id_list={feed.rune_id}},
        "强化 "..rune_name(target,target.template_info).." · 使用 1 枚经验符文",13)
end

local function record_upgrade(self,state,level,exp)
    local target=state.last_target
    state.upgrades=state.upgrades+1
    state.materials_used=state.materials_used+target.materials
    state.first_level=state.first_level or target.level
    state.upgrade_summary=target.name.." Lv."..state.first_level.." → Lv."..level.."（经验 "..exp.."）"
    self:log(state.upgrade_summary.." · 本批材料 "..target.materials.." 枚")
    state.last_target=nil
end

local function apply_rune_receipt(self,state,rune,pending)
    self._task_pending=nil
    local response=pending.response
    local result=tostring(response.result or "success")
    local ok=result=="success" or result=="0"
    if pending.kind=="growth_rune_query" then
        if not ok then return finish_runes(self,state,"符文背包同步失败："..result,true) end
        state.loaded=true
        state.control_loaded=false
        if state.verify_upgrade then
            state.verify_upgrade=false
            local level,exp=upgrade_progress(rune,state.last_target)
            if not level then
                return finish_runes(self,state,"背包已重读，强化进度未确认",true)
            end
            record_upgrade(self,state,level,exp)
        end
    elseif pending.kind=="growth_rune_draw" then
        pcall(function() rune:SetDrawState(false) end)
        if ok then
            local amount=type(response.draw_records)=="table" and #response.draw_records or pending.draw_count
            state.drawn=state.drawn+(amount or 0)
            if pending.draw_count==10 then state.ten_pulls=state.ten_pulls+1 else state.single_pulls=state.single_pulls+(amount or 0) end
            if #(rune.rune_temporary_list or {})==0 then state.refresh_bag=true end
        elseif result:find("not_enough",1,true) then
            if pending.draw_count==10 then state.single_only=true
            else state.draw_exhausted=true end
            state.phase="upgrade"
        elseif result:find("bag_full",1,true) then
            state.phase="upgrade"
            state.refresh_bag=true
            state.draw_bag_rejected=true
        else
            return finish_runes(self,state,"抽取回执："..result,true)
        end
    elseif pending.kind=="growth_rune_receive" then
        pcall(function() rune:SetIsMovingState(false) end)
        local count=#(rune.rune_temporary_list or {})
        state.receive_blocked=(count>0 and count>=(pending.temporary_before or 0)) and receive_key(rune) or nil
        if state.continuous or state.receive_blocked or not ok then state.phase="upgrade" end
        state.control_loaded=false
        if state.continuous or state.receive_blocked then state.refresh_bag=true end
    elseif pending.kind=="growth_rune_upgrade" then
        state.control_loaded=false
        if not ok then return finish_runes(self,state,"强化回执："..result,true) end
        local level,exp=upgrade_progress(rune,state.last_target)
        if level then
            record_upgrade(self,state,level,exp)
            state.draw_bag_rejected=false
        else
            state.verify_upgrade=true
            state.refresh_bag=true
        end
    end
end

function A:start_rune(continuous,manual_target)
    self:cancel_task()
    local loop=continuous~=false
    self.rune_state={continuous=loop,phase=loop and "upgrade" or "draw",refresh_bag=loop,
        loaded=false,drawn=0,ten_pulls=0,single_pulls=0,upgrades=0,materials_used=0,manual_target=manual_target}
    pcall(function()
        local rune=require("logic.rune")
        rune:SetSelectedAutoGo(false)
        rune:SetSelectedAutoCompose(true)
        if type(rune.SetSelectedMoreQuality)=="function" then
            rune:SetSelectedMoreQuality(self.cfg.runeUseFlashGold~=false)
        end
    end)
    self:log(loop and "符文连抽：十连余额不足后单抽，水晶用完后强化已装备符文并结束"
        or "符文单轮：最多抽取100次，收取并强化已装备符文后结束")
    return true
end

function A:rune_step()
    local state=self.rune_state
    if not state then return reply(self,"符文任务已停止",{done=true}) end
    local waiting=self:task_barrier()
    if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then
        return reply(self,"等待回到游戏继续符文任务",{wait=true,delaySeconds=1})
    end
    if self.inBattle and self.cfg.autoSkipBattle==false then
        return reply(self,"等待战斗结束后继续符文任务",{wait=true})
    end
    self:hide_panel("reward_panel")
    local rune=require("logic.rune")
    local pending=self._task_pending
    if pending and pending.kind~="growth_rune_loadouts" and pending.kind~="rune_recovery_bag" then
        if not pending.response then
            return reply(self,"等待符文服务器确认",{wait=true})
        end
        local outcome=apply_rune_receipt(self,state,rune,pending)
        if outcome then return outcome end
    end

    if state.refresh_bag or (not state.loaded and (tonumber(rune.rune_bag_capacity) or 0)<=1) then
        return read_runes(self,state)
    end
    state.loaded=true
    local sync=self:rune_control_step(state,rune)
    if sync then return sync end

    local temporary=#(rune.rune_temporary_list or {})
    local space=bag_space(rune)
    if temporary>0 and space>0 and receive_key(rune)~=state.receive_blocked then
        local p=self:task_request("rune_one_key_receive_ret","growth_rune_receive",function()
            return send_rune_request(rune,{rune_one_key_receive={is_auto_compose=true}})
        end)
        p.temporary_before=temporary
        return reply(self,"收取并合成临时符文",{wait=true})
    end

    local platform=tonumber(rune.cur_draw_platform_id)
    local conf=(config_manager.rune_draw_config or {})[platform]
    local cost=conf and tonumber(conf.cost_num)
    local crystal_id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.crystal
    local crystal=crystal_id and tonumber(resource_logic:GetResourceNum(crystal_id))
    local draw_limit=not state.continuous and state.drawn>=100
    local bag_blocked=space<=0 or temporary>0 or state.draw_bag_rejected
    local exhausted=state.draw_exhausted or (crystal and cost and crystal<cost)
    if crystal and cost and crystal<cost*10 then state.single_only=true end
    -- Once on single draws, save the final reinforcement pass for exhaustion.
    -- If the bag is full, reinforce immediately so remaining runes can be received.
    if (state.phase=="upgrade" and not state.single_only) or draw_limit or bag_blocked or exhausted or not cost or not crystal then
        local upgrade=advance_rune_upgrade(self,state,rune)
        if upgrade then return upgrade end
        state.phase="draw"
    end

    local ending=state.target=="skip" and "，已跳过本轮强化" or ""
    if exhausted then return finish_runes(self,state,"水晶不足"..ending) end
    if bag_blocked then return finish_runes(self,state,"符文背包或临时格不足"..ending) end
    if draw_limit then return finish_runes(self,state,"单轮抽取完成"..ending) end
    if not cost or cost<=0 or not crystal then
        return finish_runes(self,state,"无法读取当前层水晶价格或余额",true)
    end

    local capacity=tonumber(rune.rune_temporary_bag_capacity) or tonumber(constants.MAX_RUNE_TEMPORARY_BAG_CAPACITY) or 10
    local amount=(not state.single_only and capacity>=10 and (state.continuous or state.drawn<=90)) and 10 or 1
    local draw_type=amount==10 and "ten_times" or "once"
    local p=self:task_request("draw_rune_ret","growth_rune_draw",function()
        return send_rune_request(rune,{draw_rune={draw_type=draw_type,go_to_area_4=false}})
    end)
    p.draw_count=amount
    return reply(self,string.format("符文%s · 第 %d 层，水晶 %d",amount==10 and "十连" or "单抽",platform,crystal),{wait=true})
end
end
