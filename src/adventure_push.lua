-- Push only real, unlocked adventure mazes. Native exploration time, event
-- costs, cooldowns and reward accounting remain authoritative.
A.PUSH_EVENTS={"query_adventure_maze_list_ret","enter_adventure_maze_ret","solve_adventure_event_ret","open_box_ret"}
local function push_logic()return require("logic.adventure")end
local function push_result(msg)return msg and (msg.result==nil or msg.result=="success" or msg.result==0)end
function A:start_push()
    self:cancel_task()
    self.push_state={loaded=false,mazes={},cd={},fails={},attempts={},retry_at={},won=0,lost=0,boxes=0}
    self:log("开始自动推关：简单、普通、困难，当前阵容持续重试，不升级或购买")
    return true
end
function A:push_snapshot(msg)
    local st=self.push_state;if type(msg.maze_list)~="table" then return false end
    local list={};for _,row in pairs(msg.maze_list)do if tonumber(row.id) then list[row.id]=row end end
    if next(list)==nil then return false end
    st.mazes=list;st.cur=tonumber(msg.cur_maze_id) or tonumber(push_logic().cur_maze_id)
    st.cd={};st.fails={}
    for _,row in pairs(msg.event_cd_list or {})do st.cd[row.id]=tonumber(row.cd_time) or 0 end
    for _,row in pairs(msg.event_fail_time_list or {})do st.fails[row.id]=tonumber(row.fail_time) or 0 end
    st.loaded=true;return true
end
function A:push_candidates()
    local st=self.push_state;local out={}
    for id,maze in pairs(st.mazes)do
        local conf=(config_manager.adventure_maze_config or {})[id]
        local income=conf and (config_manager.adventure_income_config or {})[conf.income_id]
        if conf and income and tonumber(income.difficulty) and income.difficulty>=1 and income.difficulty<=3
            and not maze.event_is_finish and tonumber(conf.event_id) then
            out[#out+1]={id=id,conf=conf,maze=maze,area=tonumber(income.area_id) or 0,difficulty=tonumber(income.difficulty) or 0}
        end
    end
    table.sort(out,function(a,b)
        local aa,bb=st.attempts[a.id] or -1,st.attempts[b.id] or -1
        if aa~=bb then return aa<bb end
        if a.area~=b.area then return a.area<b.area end
        if a.difficulty~=b.difficulty then return a.difficulty<b.difficulty end
        return a.id<b.id
    end)
    return out
end
function A:push_event_affordable(event)
    local ids={};for id in tostring(event.need_resource_id or ""):gmatch("%d+")do ids[#ids+1]=tonumber(id)end
    local nums={};for n in tostring(event.need_num or ""):gmatch("%d+")do nums[#nums+1]=tonumber(n)end
    for i,id in ipairs(ids)do
        if id~=(constants.RESOURCE_TYPE.gold_coin or 1) then return false,"关卡需要金币以外的资源，等待手动处理" end
        local need=(nums[i] or math.huge)*2^(self.push_state.fails[event.ID] or 0)
        if (tonumber(resource_logic:GetResourceNum(id)) or 0)<need then return false,"金币不足，等待恢复后继续推关" end
    end
    return true
end
function A:push_step()
    local st=self.push_state;if not st then return reply(self,"自动推关已停止",{done=true})end
    local barrier=self:task_barrier();if barrier then return barrier end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏后继续推关",{wait=true,delaySeconds=1})end
    if self.inBattle then return reply(self,"等待主线战斗结算",{wait=true})end
    self:hide_panel("reward_panel");self:hide_panel("single_reward_panel")
    local adv=push_logic();local p=self._task_pending
    if p then
        if not p.response then return reply(self,"等待主线请求确认",{wait=true})end
        local msg=p.response;self._task_pending=nil
        if p.kind=="push_query" then
            if not push_result(msg) or not self:push_snapshot(msg) then
                st.loaded=false;st.query_at=task_now(self)+1
                return reply(self,"主线快照不完整，重新读取",{wait=true,delaySeconds=1})
            end
        else
            st.loaded=false
            if p.kind=="push_solve" then
                st.attempts[p.maze_id]=task_now(self)
                if push_result(msg) then st.won=st.won+1;st.claim=p.maze_id
                else
                    st.lost=st.lost+1
                    local conf=(config_manager.event_config or {})[p.event_id] or {}
                    st.retry_at[p.maze_id]=task_now(self)+math.max(0.2,tonumber(conf.fail_cd) or 2)
                end
            elseif p.kind=="push_enter" and not push_result(msg) then
                st.attempts[p.maze_id]=task_now(self);st.retry_at[p.maze_id]=task_now(self)+2
            elseif p.kind=="push_box" then
                st.boxes=st.boxes+(tonumber(msg.open_box_num) or 0)
                if not push_result(msg) and msg.result~="bag_full" then st.claim=nil end
            end
        end
    end
    if not st.loaded then
        if task_now(self)<(st.query_at or 0) then return reply(self,"等待重新读取主线状态",{wait=true,delaySeconds=1})end
        self:task_request("query_adventure_maze_list_ret","push_query",function()return network:Send({query_adventure_maze_list={}})end)
        return reply(self,"读取主线进度、探索时间与挑战冷却",{wait=true})
    end
    local bag=require("logic.bag")
    local space=type(bag.GetSpaceCount)=="function" and tonumber(bag:GetSpaceCount()) or 0
    if space<(tonumber(constants.EVENT_MAX_BAG_SPACE_COUNT) or 5) then return reply(self,"背包空间不足，等待整理后继续推关",{wait=true,delaySeconds=1})end
    if st.claim then
        local row=st.mazes[st.claim]
        if st.cur==st.claim and row and (tonumber(row.box_num) or 0)>0 then
            local pending=self:task_request("open_box_ret","push_box",function()return network:Send({open_box={}})end)
            pending.maze_id=st.claim
            return reply(self,"领取当前关卡可领取的免费探索宝箱",{wait=true})
        end
        st.claim=nil
    end
    local wait_reason="当前已解锁主线已通关，等待新关卡开放"
    for _,choice in ipairs(self:push_candidates())do
        local id,conf=choice.id,choice.conf;local event=(config_manager.event_config or {})[conf.event_id]
        local cd=math.max((st.cd[conf.event_id] or 0)-(tonumber(time_logic:Now()) or 0),(st.retry_at[id] or 0)-task_now(self))
        if cd<=0 and event then
            local affordable,why=self:push_event_affordable(event)
            if not affordable then wait_reason=why
            elseif st.cur~=id then
                local area=(config_manager.area_info_config or {})[choice.area] or {}
                local troop=require("logic.troop")
                local bp=type(troop.GetTroopBP)=="function" and tonumber(troop:GetTroopBP()) or 0
                if choice.difficulty==1 and tonumber(conf.type)==1 and (tonumber(choice.maze.event_time) or 0)==0 and bp<(tonumber(area.bp_limit) or 0) then
                    wait_reason="战力未达到新区域要求，等待阵容变化"
                else
                    local pending=self:task_request("enter_adventure_maze_ret","push_enter",function()return network:Send({enter_adventure_maze={maze_id=id}})end)
                    pending.maze_id=id
                    return reply(self,"进入主线 "..tostring(conf.name or id).." · 难度"..choice.difficulty,{wait=true})
                end
            else
                local native=(adv.maze_list or {})[id] or {}
                local elapsed=math.max(tonumber(choice.maze.event_time) or 0,tonumber(native.event_time) or 0)
                if elapsed<(tonumber(conf.event_time) or math.huge) then
                    wait_reason="等待主线探索完成："..tostring(conf.name or id)
                    -- Keep exploring this stage instead of resetting its timer by hopping.
                    break
                end
                local pending=self:task_request("solve_adventure_event_ret","push_solve",function()return network:Send({solve_adventure_event={event_id=conf.event_id}})end)
                pending.maze_id=id;pending.event_id=conf.event_id
                pending.accept_response=function(msg)return tonumber(msg.event_id)==tonumber(conf.event_id)end
                return reply(self,"挑战主线 "..tostring(conf.name or id).." · 胜"..st.won.." / 重试"..st.lost,{wait=true})
            end
        elseif cd>0 then wait_reason="等待主线战败冷却，随后继续重试" end
    end
    if task_now(self)>=(st.refresh_at or 0) then st.loaded=false;st.refresh_at=task_now(self)+2 end
    return reply(self,wait_reason,{wait=true,delaySeconds=0.2})
end
