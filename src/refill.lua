-- Only one resource mutation is in flight. Chain acknowledged uses, then read
-- mining once; never invent a batch field for UseItem (the game has none).
A.REFILL_EVENTS={"query_bag_info_ret","use_item_ret","use_mining_tool_ret","query_mining_info_ret",
    "query_resource_recycle_ret","add_material_ret","resource_recycle_click_finish_ret"}
local function refill_bag()
    local ok,bag=pcall(require,"logic.bag")
    if ok then return bag end
end
function A:has_earth_heart()
    if self.cfg.autoUseEarthHeart==false then return false end
    local bag=refill_bag()
    for _,item in pairs(bag and bag.item_list or {}) do
        if tonumber(item.template_id)==6000005 and (tonumber(item.count) or 1)>0 then return true end
    end
    return false
end
function A:earth_inventory_unknown()
    local bag=refill_bag()
    return self.cfg.autoUseEarthHeart~=false and bag and (tonumber(bag.capacity) or 1)<=1
        and next(bag.item_list or {})==nil
end
function A:start_refill(owner,needed,id)
    if self.fast_refill then return end
    self.fast_refill={owner=owner,needed=needed,id=id,before=tonumber(mining.dig_count) or 0,
        seen={},uses=0,bag_queried=false,phase="choose"}
    self:refill_advance()
end
function A:refill_send(event,key,payload,phase)
    local st=self.fast_refill
    st.phase=phase
    if key=="use_item" then
        local bag=refill_bag();if bag then bag.quick_use_item=false end
    end
    local p=self:task_request(event,"fast_refill",function() network:Send({[key]=payload}) end)
    if key=="use_item" then
        p.accept_response=function(msg)return tonumber(msg.item_id)==tonumber(payload.item_id) end
    end
end
function A:refill_advance()
    local st=self.fast_refill
    if not st or st.done or self._task_pending then return end
    if st.next_at and task_now(self)<st.next_at then return end
    if st.uses>=10000 then st.done=true;st.error="补充操作达到安全上限";return end
    if st.phase=="snapshot" then
        self:refill_send("query_mining_info_ret","query_mining_info",{},"verify");return
    end
    local have=tonumber(mining.dig_count) or 0
    local cap=tonumber(mining.dig_max_count) or 0
    local target=st.needed
    if cap>0 then target=math.min(target,cap) end
    if have>=target and not st.all_started then
        if st.bags_active then st.bags_active=false;st.phase="snapshot";self:refill_advance();return end
        st.done=true;return
    end
    if self.cfg.autoUseEarthHeart~=false and not st.hearts_finished then
        local bag=refill_bag()
        if bag and not st.bag_queried and (type(bag.item_list)~="table"
            or ((tonumber(bag.capacity) or 1)<=1 and next(bag.item_list)==nil)) then
            st.bag_queried=true
            self:refill_send("query_bag_info_ret","query_bag_info",{},"bag");return
        end
        for _,item in pairs(bag and bag.item_list or {}) do
            if tonumber(item.template_id)==6000005 and not st.seen[item.item_id]
                and (tonumber(item.count) or 1)>0 then
                st.seen[item.item_id]=true;st.uses=st.uses+1
                st.all_started=self.cfg.earthUseAll==true
                self:refill_send("use_item_ret","use_item",{item_id=item.item_id},"heart")
                return
            end
        end
        st.hearts_finished=true
        if st.all_started then st.all_started=false;st.phase="snapshot";self:refill_advance();return end
    end
    if have>=target then st.done=true;return end
    local tool=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.ultimate_tool
    local stock=tool and tonumber(resource_logic:GetResourceNum(tool)) or 0
    if st.owner=="mine" and self.cfg.autoUseMiningBag==false then stock=0 end
    if st.bags_left==nil then st.bags_left=math.min(stock,math.ceil(math.max(0,target-have)/5)) end
    if tool and st.bags_left>0 then
        local n=math.min(5,st.bags_left,stock)
        if n>0 then
            st.bags_left=st.bags_left-n;st.uses=st.uses+1;st.bags_active=true
            self:refill_send("use_mining_tool_ret","use_mining_tool",{resource_id=tool,num=n},"bags");return
        end
    end
    st.done=true;st.error="矿镐不足，且没有可用的大地之心或矿工包"
end
function A:refill_on_response(name,msg)
    local st=self.fast_refill;local p=self._task_pending
    if not st or st.done or not p or p.kind~="fast_refill" or not p.response then return end
    self._task_pending=nil
    local result=tostring(msg.result or "success")
    if result~="success" then st.done=true;st.error="补充失败："..result;return end
    if st.phase=="verify" then
        if msg.dig_count~=nil then mining.dig_count=msg.dig_count end
        local have=tonumber(mining.dig_count) or 0
        if have<=st.before then st.done=true;st.error="补充后矿镐未增加，停止重复消耗";return end
        st.before=have;st.bags_left=nil;st.bags_active=false;st.phase="choose"
    elseif st.phase=="heart" then
        st.phase=st.all_started and "choose" or "snapshot"
    elseif st.phase=="bags" then
        st.phase=st.bags_left>0 and "choose" or "snapshot"
    elseif st.phase=="bag" then
        local bag=refill_bag()
        if bag and type(msg.item_list)=="table" then bag.item_list=msg.item_list end
        st.phase="choose"
    end
    local gap=tonumber(self.cfg.refillInterval) or 0
    if gap>0 then st.next_at=task_now(self)+gap;return end
    local ok,err=pcall(function()self:refill_advance()end)
    if not ok then st.done=true;st.error=tostring(err) end
end
function A:refill_step()
    local st=self.fast_refill
    if not st then return nil end
    local p=self._task_pending
    if p and not p.response and task_now(self)-p.sent_at>20 then
        if not p.event:match('^query_') then self._cancelled_task_pending=p end
        self._task_pending=nil;st.done=true;st.error="补充请求超时，已停止，避免重复消耗"
    end
    if not st.done then self:refill_advance() end
    if st.done then
        self.fast_refill=nil
        if st.error then self:log(st.error) else self:log("矿镐批量补充完成，当前"..tostring(mining.dig_count).."次") end
        return {refill_done=true,error=st.error,refill_id=st.id}
    end
    return reply(self,"优先使用大地之心，按回包连续补充矿镐",{wait=true,action="补充矿镐"})
end

local REFINE_MAX=10000
local REFINE_COOL=1000
-- 目标点击次数。120 是冗余预算：部分点击可能不被游戏接受，所以按 120 发，
-- 实际以游戏原生计数为准。当前公开客户端把计数钳制在 100
-- （ui/resource_recycle_panel.lua 的 math.min(100, ...)，logic/resource_recycle.lua
-- 的 ClickPlayEnd 同样取 min(100)），因此接近上限后计数不再增长属于正常收尾。
local REFINE_TAPS=120
local REFINE_CLIENT_CAP=100
local REFINE_BATCH=8
local REFINE_STALL_LIMIT=2
local REFINE_MAX_BATCHES=40
local REFINE_ARM_DELAY=1.2
local REFINE_FORCE_ARM_DELAY=2.5
local REFINE_REWARD_TIMEOUT=120

-- 依次尝试的输入通道：游戏原生模拟触摸 → 画布 pointer → 画布 touch → 画布 mouse。
-- 真实客户端只监听 canvas 的 pointerdown/pointerup；touch/mouse 仅作兜底。
local REFINE_INPUTS={"sim","pointer","touch","mouse"}

local function refine_input_index(mode)
    for index,name in ipairs(REFINE_INPUTS) do
        if name==mode then return index end
    end
    return #REFINE_INPUTS
end

local function refine_sim_ready()
    local ok,value=pcall(function()
        return type(aandm)=="table" and type(aandm.simTouch)=="function"
    end)
    return ok and value==true
end

-- 与游戏新手引导相同的换算：设计坐标 → 引擎 simulateTouch 使用的屏幕坐标。
local function refine_touch_point()
    local x,y=320,568
    pcall(function()
        local view=cc.Director:getInstance():getOpenGLView()
        local frame=view:getFrameSize()
        local rect=view:getViewPortRect()
        local sx,sy=view:getScaleX(),view:getScaleY()
        x=320*sx+rect.x
        y=frame.height-568*sy+rect.y
    end)
    return x,y
end

local function refine_sim_taps(count)
    local x,y=refine_touch_point()
    for _=1,count do
        local ok=pcall(function() aandm.simTouch(x,y) end)
        if not ok then return false end
    end
    return true
end

local function refine_panel_of(self)
    local ok,context=pcall(function() return self:read_context() end)
    local sub=ok and context and context.sub_name=="resource_recycle_sub_scene" and context.sub_scene
    local panel=sub and sub.ui_root
    if panel and panel.root_node and panel.root_node:isVisible() then return panel end
end

local function clear_refine_overlays(self)
    pcall(function() graphic:DispatchEvent("hide_all_sub_panel") end)
    local ok,context=pcall(function() return self:read_context() end)
    local scene=ok and context and context.scene
    if scene then
        if type(scene.HideAllSubPanels)=="function" then pcall(function() scene:HideAllSubPanels() end) end
        for _,key in ipairs({"msgbox","jump_panel","floating_panel","alert_panel"}) do
            local modal=scene[key]
            if modal and type(modal.Hide)=="function" then pcall(function() modal:Hide() end) end
        end
    end
    for _,name in ipairs({"reward_panel","resource_recycle_bouns_msgbox","confirm_msgbox"}) do
        pcall(function() self:hide_panel(name) end)
    end
end

local function refine_logic_module()
    local ok,recycle=pcall(require,"logic.resource_recycle")
    if ok and type(recycle)=="table" then return recycle end
end

local function refine_listener_enabled(panel)
    if not panel or not panel.listener or type(panel.listener.isEnabled)~="function" then return true end
    local ok,enabled=pcall(panel.listener.isEnabled,panel.listener)
    return not ok or enabled==true
end

local function refine_enable_listener(panel)
    if panel and panel.listener and type(panel.listener.setEnabled)=="function" then
        pcall(panel.listener.setEnabled,panel.listener,true)
    end
end

local function refine_query(self, purpose)
    local p=self:task_request("query_resource_recycle_ret","daily_refine",function()
        return network:Send({query_resource_recycle={}})
    end)
    p.refine_query=purpose
    return reply(self,"读取炼化过热度与进度",{wait=true})
end

local function refine_finish(state, text)
    state.refine_done=true
    state.refine_summary=state.refine_reward_claimed and ("最终奖励已领 · "..text) or text
end

function A:close_refine_game()
    pcall(function()self:hide_panel("resource_recycle_bouns_msgbox")end)
    local ok,context=pcall(function()return self:read_context()end)
    if ok and context and context.sub_name=="resource_recycle_sub_scene" then
        pcall(function()graphic:DispatchEvent("hide_world_sub_scene")end)
    end
end

function A:daily_refine_pending()
    local state=self.daily_state;local pending=self._task_pending
    if not state or not pending or pending.kind~="daily_refine" then return nil end
    if not pending.response and task_now(self)-pending.sent_at<=20 then return reply(self,"等待炼化服务器响应",{wait=true}) end
    local msg=pending.response;self._task_pending=nil
    if pending.refine_reward then
        state.refine_reward_active=nil
        state.refine_verify_reward=true
        state.refine_reward_claimed=msg and msg.result=="success" or nil
        state.refine_loaded=false
        return nil
    end
    if not msg then
        if not pending.refine_query then self._cancelled_task_pending=pending end
        refine_finish(state,"炼化请求超时，本轮暂停加料")
    elseif msg.result and msg.result~="success" then
        refine_finish(state,"炼化未完成："..tostring(msg.result))
    else
        local temperature=tonumber(msg.temperature);local process=tonumber(msg.process)
        if not temperature or not process then
            state.refine_loaded=false
            if pending.refine_query then refine_finish(state,"炼化查询缺少过热度或进度") end
            return nil
        end
        if temperature<0 or temperature>REFINE_MAX or process<0 then
            refine_finish(state,"炼化回执中的过热度或进度无效");return
        end
        local previous=state.refine_sample
        local now=task_now(self)
        if pending.refine_query and previous and previous.process==process and previous.heat>temperature and now>previous.at then
            state.refine_rate=(previous.heat-temperature)/(now-previous.at)
        end
        state.refine_sample={heat=temperature,process=process,at=now}
        state.refine_loaded=true;state.refine_temperature=temperature;state.refine_process=process
        if pending.refine_query=="schedule" then return end
        if state.refine_verify_reward then
            state.refine_verify_reward=nil
            if process>=REFINE_MAX then
                self:close_refine_game()
                refine_finish(state,"炼化进度尚未重置，暂停加料")
                return
            end
            state.refine_reward_claimed=true
            state.refine_reheated=true
            self:close_refine_game()
        end
        if temperature>=REFINE_MAX and process<REFINE_MAX then refine_finish(state,"炼化过热度已满") end
        if not pending.refine_query then
            state.refine_rounds=(state.refine_rounds or 0)+1
            if temperature>=REFINE_COOL and temperature>(pending.before or 0) then state.refine_heated=true end
            if temperature<=(pending.before or 0)+1e-9 and process<=(pending.before_process or 0) then
                refine_finish(state,"炼化回执未显示升温或进度变化，暂停加料")
            end
        end
    end
end

function A:daily_refine_reward(state)
    if state.refine_reheated then refine_finish(state,"已完成重新加料，下一轮处理新奖励");return end
    local hooks=self._task_hooks or {}
    if not hooks.resource_recycle_click_finish_ret then
        refine_finish(state,"最终奖励界面暂不可用");return
    end
    if self.inBattle then
        return reply(self,"等待战斗结束后打开炼化最终奖励小游戏",{wait=true,delaySeconds=0.3})
    end
    local panel=refine_panel_of(self)
    local now=task_now(self)
    if not state.refine_reward_active then
        state.refine_reward_active={started=now,open_sent=now,last=nil,stall=0,sent=false,armed=false,
            mode=refine_sim_ready() and "sim" or "pointer"}
        clear_refine_overlays(self)
        if not panel then
            graphic:DispatchEvent("show_world_sub_scene","resource_recycle_sub_scene")
            return reply(self,"打开炼化最终奖励小游戏",{wait=true,delaySeconds=0.4})
        end
    end
    local active=state.refine_reward_active
    local recycle=refine_logic_module()
    local finish_pending=recycle and recycle.click_finish_pending==true or false
    if finish_pending then
        active.seen_click_finish=true
        return reply(self,"等待炼化点击结算",{wait=true,delaySeconds=0.3})
    end
    if (active.seen_click_finish or active.cap or active.grew) and recycle then
        local live_process=tonumber(recycle.process)
        if live_process~=nil and live_process<REFINE_MAX then
            state.refine_reward_active=nil
            state.refine_verify_reward=true
            state.refine_reward_claimed=true
            state.refine_loaded=false
            return reply(self,"炼化最终奖励已结算，正在核对进度重置",{wait=true,delaySeconds=0.2})
        end
    end
    if now-active.started>REFINE_REWARD_TIMEOUT then
        state.refine_reward_active=nil
        self:close_refine_game()
        refine_finish(state,"未确认最终奖励，保留当前游戏进度")
        return
    end
    if not panel then
        if now-(active.open_sent or active.started)>=1.5 then
            active.open_sent=now
            clear_refine_overlays(self)
            graphic:DispatchEvent("show_world_sub_scene","resource_recycle_sub_scene")
        end
        return reply(self,"等待炼化小游戏界面",{wait=true,delaySeconds=0.3})
    end
    local count=tonumber(panel.touche_numbers) or 0
    local layer_enabled=panel.touch_layer and panel.touch_layer:isTouchEnabled() and true or false
    local listener_enabled=refine_listener_enabled(panel)
    local countdown=tonumber(panel.touches_time) or 0
    local settled_or_failed=(active.cap or active.grew or active.seen_click_finish)
        and not panel.start_click and count==0 and countdown<=0 and not listener_enabled
    if settled_or_failed then
        if active.seen_click_finish then
            state.refine_reward_active=nil
            state.refine_verify_reward=true
            state.refine_loaded=false
            return reply(self,"等待炼化最终奖励进度同步",{wait=true,delaySeconds=0.25})
        end
        if not active.settle_at then
            active.settle_at=now
            return reply(self,"等待炼化点击结算",{wait=true,delaySeconds=0.3})
        end
        if now-active.settle_at<2.5 then
            return reply(self,"等待炼化点击结算",{wait=true,delaySeconds=0.3})
        end
        -- 若原生结算未能发出或失败，重置本轮点击状态并重新唤出点击层。
        active.cap=nil;active.grew=nil;active.settle_at=nil;active.armed=false;active.armed_at=nil
        active.batches=0;active.stall=0;active.last=nil;active.sent=false
        self:log("炼化点击结算未确认，重新唤出小游戏点击区域")
        clear_refine_overlays(self)
        pcall(function() panel.click_play=true; panel:TouchEventLayer() end)
        active.armed=true;active.armed_at=now
        return reply(self,"重新启用炼化点击区域",{wait=true,delaySeconds=REFINE_ARM_DELAY})
    end
    if not layer_enabled then
        if count>0 or panel.start_click then
            -- 倒计时结束，游戏已发出 resource_recycle_click_finish，等待回执。
            return reply(self,"等待炼化点击结算",{wait=true,delaySeconds=0.3})
        end
        -- 点击层没有启用时先给原生动画留出时间，再主动唤出一次并重新读取触摸区域。
        if not active.armed and now-(active.started or now)>=1.5 then
            active.armed=true;active.armed_at=now
            clear_refine_overlays(self)
            pcall(function() panel.click_play=true; panel:TouchEventLayer() end)
            self:log("炼化点击层未启用，重新唤出点击区域")
            return reply(self,"重新启用炼化点击区域",{wait=true,delaySeconds=REFINE_ARM_DELAY})
        end
        if active.armed and active.armed_at and now-active.armed_at>=REFINE_FORCE_ARM_DELAY then
            clear_refine_overlays(self)
            pcall(function()
                panel.click_play=true
                if panel.touch_layer and type(panel.touch_layer.setTouchEnabled)=="function" then
                    panel.touch_layer:setTouchEnabled(true)
                end
                refine_enable_listener(panel)
            end)
            active.armed_at=now
            self:log("炼化点击层动画未完成，已直接启用点击区域")
            return reply(self,"已启用炼化点击区域",{wait=true,delaySeconds=0.2})
        end
        return reply(self,"等待炼化点击区域启用",{wait=true,delaySeconds=0.25})
    end
    if not listener_enabled then
        if count>0 or panel.start_click then
            return reply(self,"等待炼化点击结算",{wait=true,delaySeconds=0.3})
        end
        clear_refine_overlays(self)
        refine_enable_listener(panel)
        self:log("炼化触摸监听未启用，已自动恢复监听")
        return reply(self,"恢复炼化小游戏触摸监听",{wait=true,delaySeconds=0.15})
    end
    if count>=REFINE_TAPS then
        return reply(self,string.format("炼化点击已到冗余目标（%d/%d），等待游戏倒计时结束",count,REFINE_TAPS),
            {wait=true,delaySeconds=0.3})
    end
    if active.cap then
        return reply(self,string.format("炼化点击已到客户端上限（%d 次），等待游戏倒计时结束",active.cap),
            {wait=true,delaySeconds=0.3})
    end
    -- 只以游戏原生点击计数是否增长判断输入是否送达，不依赖发送次数。
    if active.last==nil then
        active.last=count
    elseif count>active.last then
        if not active.peak or count>active.peak then active.peak=count end
        active.last=count;active.stall=0;active.sent=false;active.grew=true
    elseif active.sent then
        active.stall=(active.stall or 0)+1
        if active.stall>=REFINE_STALL_LIMIT then
            active.stall=0;active.sent=false
            if count==REFINE_CLIENT_CAP then
                -- 只有正好停在已知的客户端上限才属于正常收尾。
                active.cap=count
                self:log("炼化点击停在 "..count.."（客户端上限），等待倒计时结算")
                return reply(self,string.format("炼化点击已到客户端上限（%d 次），等待游戏倒计时结束",count),
                    {wait=true,delaySeconds=0.3})
            end
            clear_refine_overlays(self)
            refine_enable_listener(panel)
            local index=refine_input_index(active.mode)
            if index<#REFINE_INPUTS then
                local previous=active.mode
                active.mode=REFINE_INPUTS[index+1]
                self:log("炼化点击连续 2 批未增长（当前 "..count.."），输入方式 "..previous.." → "..active.mode)
            else
                self:log("炼化点击连续未增长（当前 "..count.."），四种输入方式均已尝试，继续按 "..active.mode.." 点击")
            end
            active.last=count
        end
    end
    if (active.batches or 0)>=REFINE_MAX_BATCHES then
        return reply(self,string.format("炼化点击已发出 %d 批（当前 %d/%d），等待游戏倒计时结束",
            active.batches,count,REFINE_TAPS),{wait=true,delaySeconds=0.3})
    end
    local batch=REFINE_TAPS-count
    if batch>REFINE_BATCH then batch=REFINE_BATCH end
    if batch<1 then batch=1 end
    active.sent=true;active.batches=(active.batches or 0)+1
    if active.mode=="sim" then
        if not refine_sim_ready() or not refine_sim_taps(batch) then
            active.mode="pointer";active.sent=false
            self:log("炼化原生模拟点击不可用，改用画布 pointer 事件")
        end
    end
    local text=string.format("炼化最终奖励 · 点击 %d/%d",count,REFINE_TAPS)
    if countdown>0 then text=text.." · 剩余"..math.ceil(countdown).."秒" end
    if active.mode~="sim" then text=text.." · "..active.mode end
    if active.mode=="sim" then
        return reply(self,text,{wait=true,delaySeconds=0.12})
    end
    return reply(self,text,{wait=true,refineTap=true,refineInput=active.mode,
        refineTapBatch=batch,refineClickCount=count,delaySeconds=0.12})
end

function A:daily_refine()
    local state=self.daily_state
    if not state.refine_enabled or state.refine_done then return nil end
    self:install_task_hooks()
    if not self._task_hooks.query_resource_recycle_ret or not self._task_hooks.add_material_ret then
        refine_finish(state,"炼化模块尚未初始化");return nil
    end
    if not state.refine_loaded then return refine_query(self,"work") end
    if state.refine_process>=REFINE_MAX then
        return self:daily_refine_reward(state)
    end
    local left=REFINE_MAX-state.refine_temperature
    if left<=0 then refine_finish(state,"炼化过热度已满");return nil end
    local materials,candidates={},{}
    local reserve=math.max(0,math.floor(tonumber(self.cfg.refineReserve) or 1000))
    local ok,recycle=pcall(require,"logic.resource_recycle")
    for _,id in ipairs({23,7,8,9,10,11,14,15,16,17,12,13}) do
        local stock=math.floor(tonumber(resource_logic:GetResourceNum(id)) or 0)
        if stock>reserve then candidates[#candidates+1]={id=id,stock=stock} end
    end
    table.sort(candidates,function(a,b)return a.stock>b.stock or (a.stock==b.stock and a.id<b.id)end)
    for _,candidate in ipairs(candidates) do
        local id=candidate.id
        local conf=(config_manager.resource_config or {})[id]
        local heat=conf and tonumber(conf.temperature) or 0
        if ok and recycle and type(recycle.GetMaterialHeat)=="function" then
            local success,value=pcall(recycle.GetMaterialHeat,recycle,id,1)
            if success and tonumber(value) then heat=tonumber(value) end
        end
        local count=heat>0 and math.min(candidate.stock-reserve,math.floor((left+1e-9)/heat)) or 0
        if count>0 then materials[#materials+1]={type=id,num=count};left=left-count*heat end
    end
    if #materials==0 then refine_finish(state,"炼化可用材料不足或过热度已接近上限");return nil end
    local p=self:task_request("add_material_ret","daily_refine",function()
        network:Send({add_material={materials=materials}})
    end)
    p.before=state.refine_temperature;p.before_process=state.refine_process;p.left=left
    return reply(self,"按库存从多到少加入炼化材料",{action="自动炼化"})
end

function A:publish_refine_cooling(state)
    local sample=state.refine_sample
    if not sample then return end
    local seconds=0
    if sample.process>=REFINE_MAX and not state.refine_reward_claimed then
        seconds=300
    elseif sample.heat>=REFINE_COOL then
        seconds=state.refine_rate and math.ceil((sample.heat-(REFINE_COOL-1))/state.refine_rate) or 900
        seconds=math.max(30,math.min(seconds,12*3600))
    elseif state.refine_heated or (state.refine_reheated and sample.process>=REFINE_MAX) then seconds=30 end
    self:record_harvest("refine",seconds)
    if self.harvest_updates and self.harvest_updates.refine then
        self.harvest_updates.refine.heat=sample.heat
        self.harvest_updates.refine.estimated=state.refine_rate~=nil
    end
end

function A:daily_refine_schedule(state)
    if not state.refine_enabled or not state.refine_sample or not state.refine_done then return end
    if not state.refine_schedule_read and task_now(self)-state.refine_sample.at>=10 and not self._cancelled_task_pending then
        state.refine_schedule_read=true
        return refine_query(self,"schedule")
    end
    local wait=self:daily_refine_pending()
    if wait then return wait end
    self:publish_refine_cooling(state)
end

function A:daily_refine_gate(state)
    if not state.refine_followup then return end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待登录后核对炼化过热度",{wait=true,delaySeconds=1}) end
    local wait=self:task_barrier() or self:daily_refine_pending()
    if wait then return wait end
    if not state.refine_sample and not state.refine_done and state.refine_enabled then
        return refine_query(self,"cooling")
    end
    if state.refine_sample and (state.refine_sample.heat<REFINE_COOL or state.refine_sample.process>=REFINE_MAX) then
        state.refine_followup=false
        return
    end
    self:publish_refine_cooling(state)
    state.running=false
    return reply(self,"炼化续跑已核对，过热度未低于 10% 或暂无法读取",{done=true,refineProbeOnly=true})
end
