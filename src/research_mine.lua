-- Mine collection and research form a single, server-confirmed cycle.
local mine_logic=require("logic.mine")
local cultivation_logic=require("logic.cultivation")
local mine_client=require("util.client_constants")
local MINE_STATE=mine_client.MINE_STATE
local BUILDINGS={"战士竞技场","魔法研究院","牧师祈祷院","潜行者工会","炼金工坊","骑士学院"}
A.cfg.mineResearchMask=-1 -- negative: migrate the legacy single-branch choice
A.RESEARCH_MINE_EVENTS={"query_mine_info_ret","query_all_mine_rewards_ret","query_cultivation_info_ret",
    "query_resource_list_ret","mine_receive_reward_ret","mine_start_ret","cultivate_ret"}

local function numbers(value)
    local out={}
    for num in tostring(value or ""):gmatch("%d+") do out[#out+1]=tonumber(num) end
    return out
end
local function resource_name(id)
    local conf=(config_manager.resource_config or {})[id]
    return conf and conf.name or "材料"..tostring(id)
end
local function research_title(kind,branch)
    return (BUILDINGS[kind] or "研究")..(branch==1 and "·加成" or "·抵抗")
end
local function slot_info(index)
    for _,info in pairs(mine_logic.mine_info_list or {}) do
        if tonumber(info.mine_index)==index then return info end
    end
end
local function stock(id)
    return math.max(0,tonumber(resource_logic:GetResourceNum(id)) or 0)
end
local function branch_level(kind,branch)
    local row=(cultivation_logic.cultivation_info or {})[kind]
    return row and tonumber(row["coefficient"..branch])
end
local function research_mask()
    local mask=tonumber(A.cfg.mineResearchMask) or -1
    if mask>=0 then return math.max(0,math.min(4095,math.floor(mask))) end
    local sel=tonumber(A.cfg.mineResearch) or 0
    local kind,branch=math.floor(sel/10),sel%10
    if kind>=1 and kind<=6 and (branch==1 or branch==2) then return 2^((kind-1)*2+branch-1) end
    return 4095
end
local function research_selected(kind,branch)
    return math.floor(research_mask()/2^((kind-1)*2+branch-1))%2==1
end
local function selection_count()
    local n=0
    for kind=1,6 do for branch=1,2 do if research_selected(kind,branch) then n=n+1 end end end
    return n
end
local function full_rewards(index,level)
    for _,row in pairs(mine_logic.all_mine_rewards or {}) do
        if tonumber(row.mine_index)==index and tonumber(row.mine_level)==level then
            local parts=row.all_resource_list
            local last=type(parts)=="table" and parts[#parts]
            local rewards={}
            for _,r in pairs(last and last.part_resource_list or {}) do
                local id,amount=tonumber(r.resource_id),tonumber(r.resource_num)
                if id and amount and amount>0 then rewards[id]=(rewards[id] or 0)+amount end
            end
            return rewards
        end
    end
end

function A:mine_analysis()
    if type(cultivation_logic.cultivation_info)~="table" then return nil,"等待研究所数据" end
    local config=config_manager.cultivation_config
    if type(config)~="table" then return nil,"研究配置缺失" end
    local targets,required={},{}
    local missing_rows=0
    for kind=1,6 do
        for branch=1,2 do
            local current=branch_level(kind,branch)
            if current~=nil then
                local max_level=0
                for level in pairs(config[kind] or {}) do max_level=math.max(max_level,tonumber(level) or 0) end
                if current<0 or current~=math.floor(current) or max_level==0 or current>max_level then
                    return nil,"研究等级或配置异常，已停止"
                end
                local next_conf=config[kind] and config[kind][current+1]
                if not next_conf and current<max_level then return nil,"研究下一阶段配置缺失，已停止" end
                if next_conf then
                    local ids,costs=numbers(next_conf.resource_ids),numbers(next_conf.cost_nums)
                    if #ids==0 or #ids~=#costs then return nil,"研究材料配置不完整，已停止自动选择" end
                    local target={kind=kind,branch=branch,current=current,next=current+1,costs={},ready=true,
                        title=research_title(kind,branch),key=kind..":"..branch}
                    for pos,id in ipairs(ids) do
                        local count=costs[pos]
                        if id==constants.RESOURCE_TYPE.blood_diamond then return nil,"研究升级需要血钻，已停止" end
                        target.costs[id]=(target.costs[id] or 0)+count
                    end
                    for id,count in pairs(target.costs) do
                        if stock(id)<count then target.ready=false end
                    end
                    targets[#targets+1]=target
                end
            else missing_rows=missing_rows+1 end
        end
    end
    if missing_rows>0 then return nil,"研究等级数据不完整" end
    -- 指定研究细分小类（1.15）：只按选中的分支生产与升级；0=按全部研究缺口自动。
    -- 若指定分支已满级，targets 为空 → 视为 complete，矿队仍会领取成熟奖励。
    local filtered={}
    for _,t in ipairs(targets) do if research_selected(t.kind,t.branch) then filtered[#filtered+1]=t end end
    targets=filtered
    table.sort(targets,function(a,b)
        if a.current~=b.current then return a.current<b.current end
        if a.kind~=b.kind then return a.kind<b.kind end
        return a.branch<b.branch
    end)
    local required={}
    for _,t in ipairs(targets) do
        for id,count in pairs(t.costs) do required[id]=(required[id] or 0)+count end
    end
    local gaps={}
    for id,need in pairs(required) do
        local lack=math.max(0,need-stock(id))
        if lack>0 then gaps[id]={id=id,name=resource_name(id),need=need,have=stock(id),missing=lack,reserved=0} end
    end
    -- Reserve each active queue's actual cached yield before allocating another queue.
    for index=1,3 do
        local info=slot_info(index)
        if info and mine_logic:GetMinesStatus(index)==MINE_STATE.mining then
            for id,amount in pairs(full_rewards(index,tonumber(info.mine_level)) or {}) do
                if gaps[id] then gaps[id].reserved=gaps[id].reserved+amount end
            end
        end
    end
    local sorted={}
    for _,gap in pairs(gaps) do
        gap.unreserved=math.max(0,gap.missing-gap.reserved)
        sorted[#sorted+1]=gap
    end
    table.sort(sorted,function(a,b)
        if a.missing~=b.missing then return a.missing>b.missing end
        return a.id<b.id
    end)
    return {targets=targets,gaps=gaps,sorted=sorted,hasGap=#sorted>0,complete=#targets==0}
end

function A:mine_view()
    local ok,analysis,err=pcall(function()return self:mine_analysis()end)
    local plan,reason="按研究缺口自动选层","进入游戏后读取研究等级和材料库存"
    local count=selection_count()
    plan=count==12 and "全部十二项研究 · 按缺口选层" or ("已选择 "..count.." / 12 项研究")
    if ok and analysis then
        if analysis.complete then plan=count==0 and "未选择研究目标" or "选中的研究已达上限";reason="仍会领取已完成的矿队奖励"
        elseif #analysis.sorted>0 then
            local bits={}
            for i=1,math.min(3,#analysis.sorted) do
                local gap=analysis.sorted[i]
                bits[#bits+1]=gap.name.." 缺"..gap.missing
            end
            reason=table.concat(bits," · ")
        else plan="研究材料已足够";reason="采集结算后自动升级研究，再重新计算缺口" end
    elseif ok then reason=tostring(err) end
    local slots={}
    local earliest_sec,earliest_slot,finish_count,active_count=nil,nil,0,0
    for i=1,3 do
        local info=slot_info(i)
        local status=mine_logic:GetMinesStatus(i)
        local label=status==MINE_STATE.lock and "未解锁" or status==MINE_STATE.ready and "空闲" or status==MINE_STATE.finish and "待领取" or "采集中"
        if status==MINE_STATE.finish then
            finish_count=finish_count+1
        elseif status==MINE_STATE.mining and info then
            active_count=active_count+1
            local seconds=math.max(0,mine_logic:GetMineEndTime(info)-time_logic:Now())
            if earliest_sec==nil or seconds<earliest_sec then
                earliest_sec=seconds
                earliest_slot=i
            end
            label=tostring(info.mine_level).."层 "..math.floor(seconds/3600).."时"..math.ceil(seconds%3600/60).."分"
        end
        slots[#slots+1]=i.."号 "..label
    end
    local earliest_label=finish_count>0 and ("待领取×"..tostring(finish_count)) or (earliest_slot and (tostring(earliest_slot).."号 "..string.format("%02d:%02d",math.floor((earliest_sec or 0)/60),math.floor((earliest_sec or 0)%60))) or "空闲")
    local state=self.mine_state
    if state and state.plan_text then
        plan=state.plan_text
    end
    return {plan=plan,reason=reason,slots=table.concat(slots," · "),gap=analysis and analysis.hasGap or false,
        earliestSeconds=earliest_sec or -1,earliestSlot=earliest_slot or 0,earliestLabel=earliest_label,finishCount=finish_count,activeCount=active_count}
end

function A:mine_choose_level(index,analysis)
    if not mine_logic:CheckFormation(index) then return nil,"尚未配置采集队伍" end
    local best,best_score,known=nil,0,false
    for level=1,5 do
        local reward=full_rewards(index,level)
        if reward then
            known=true
            if mine_logic:BattlePointIsFull(index,level,false) then
                local score=0
                for id,amount in pairs(reward) do
                    local gap=analysis.gaps[id]
                    if gap and gap.unreserved>0 then score=score+math.min(gap.unreserved,amount)/math.max(1,gap.missing) end
                end
                if score>best_score then best=level;best_score=score end
            end
        end
    end
    if best then return best end
    if not known then return nil,"矿山收益数据不完整" end
    for _,gap in pairs(analysis.gaps) do
        if gap.unreserved>0 then return nil,"现有队伍或矿层无法产出尚缺材料" end
    end
    return nil,"已有采集队覆盖本轮材料缺口"
end

local function require_snapshot(state,key,event,message)
    return not state.loaded[key] and {key=key,event=event,message=message} or nil
end
function A:harvest_account()
    local user = package.loaded["logic.user"]
    return user and user.user_id and tostring(user.user_id) or ""
end

for _,event in ipairs({"query_mine_times_ret","query_mine_rob_target_list_ret","mine_rob_target_ret"}) do
    A.DAILY_EVENTS[#A.DAILY_EVENTS+1]=event
end

local old_guard=A.daily_message_is_free
function A:daily_message_is_free(name,payload)
    if name=="mine_rob_target" then
        local p=self._task_pending
        local st=self.daily_state
        return st and st.guard_active and p and p.guard_target
            and tostring(payload.target_user_id)==tostring(p.guard_target.user_id)
            and tonumber(payload.mine_index)==tonumber(p.guard_target.mine_index)
            and payload.rob_type==p.guard_rob_type
    end
    return old_guard(self,name,payload)
end

local guard_reads={
    {"times","query_mine_times","读取矿山护卫剩余次数"},
    {"research","query_cultivation_info","读取已选研究等级"},
    {"stock","query_resource_list","读取研究材料库存"},
    {"rewards","query_all_mine_rewards","读取各矿层产出"},
    {"targets","query_mine_rob_target_list","读取矿山对手"},
}

function A:daily_guard_step(st)
    local g=st.guard_state
    if not g then g={loaded={},tried={},fought=0,won=0};st.guard_state=g end
    local function finish(reason)
        st.guard_active=false
        st.guard_summary=reason.." · 挑战 "..g.fought.." 次，胜利 "..g.won.." 次"
        self:hide_panel("reward_panel")
        return reply(self,st.guard_summary,{action="矿山护卫"})
    end
    if selection_count()==0 then return finish("未选择矿山研究") end
    local p=self._task_pending
    if p then
        if not p.response then
            if task_now(self)-p.sent_at<=20 then return reply(self,"等待矿山护卫确认",{wait=true}) end
            self._task_pending=nil
            if p.guard_target then
                g.loaded.times=false;g.loaded.stock=false;g.loaded.targets=false
                return reply(self,"护卫挑战回执未确认，先同步次数和库存",{action="护卫核对"})
            end
            return finish("矿山读取超时")
        end
        self._task_pending=nil
        local msg=p.response
        local ok=msg.result==nil or msg.result==0 or msg.result=="success"
        if p.guard_read then
            if not ok then return finish("矿山读取失败") end
            if p.guard_read=="times" then
                g.remain=msg.mine_times and tonumber(msg.mine_times.remain_rob)
                if g.remain==nil then return finish("护卫次数未同步") end
            elseif p.guard_read=="targets" then
                g.targets=msg.rob_target_list
                if type(g.targets)~="table" then return finish("矿山对手未同步") end
            end
            g.loaded[p.guard_read]=true
        elseif p.guard_target then
            if ok then
                g.fought=g.fought+1
                if msg.is_winner==true then g.won=g.won+1 end
            end
            g.loaded.times=false;g.loaded.stock=false;g.loaded.targets=false
            self:hide_panel("reward_panel")
            return reply(self,"矿山护卫结果已处理",{action="矿山护卫"})
        end
    end
    if self.inBattle then return reply(self,"等待矿山护卫战斗结束",{wait=true}) end
    if g.loaded.times and g.remain<=0 then return finish("已有护卫次数已用完") end
    for _,q in ipairs(guard_reads) do
        if not g.loaded[q[1]] then
            local request=self:task_request(q[2].."_ret","daily_guard",function()return network:Send({[q[2]]={}})end)
            request.guard_read=q[1];request.description=q[3]
            return reply(self,q[3],{wait=true})
        end
    end
    local analysis=self:mine_analysis()
    if not analysis then return finish("矿山研究缺口尚未同步") end
    local client=require("util.client_constants")
    local rob_type=(client.ROB_TYPE or {}).rob
    local max=tonumber(constants.MINE_BE_ROBBED_MAX_TIMES)
    if rob_type==nil or not max then return finish("矿山护卫配置尚未同步") end
    local target,key,best_score,best_power
    local now=time_logic:Now()
    for _,row in pairs(g.targets) do
        local uid,index,level=tostring(row.user_id or ""),tonumber(row.mine_index),tonumber(row.mine_level)
        local candidate=uid..":"..tostring(index)
        local limit=tonumber(row.be_robbed_times)
        local ending=tonumber(row.mine_end_time or row.end_time)
        if uid~="" and uid~=self:harvest_account() and index and level and limit and limit<max
            and not g.tried[candidate] and (not ending or ending>now) then
            local score=0
            for id,value in pairs(full_rewards(index,level) or {}) do
                local gap=analysis.gaps[id]
                if gap then score=score+math.min(value,gap.missing)/math.max(1,gap.missing) end
            end
            local power=tonumber(row.battle_point) or math.huge
            if score>0 and (not target or score>best_score or score==best_score and power<best_power) then
                target,key,best_score,best_power=row,candidate,score,power
            end
        end
    end
    if not target then return finish("当前列表没有产出所缺材料的可挑战对手") end
    g.tried[key]=true
    local operation="挑战矿山护卫 · "..tostring(target.leader_name or "对手").." · "..target.mine_level.." 层"
    self:task_request("mine_rob_target_ret","daily_guard",function()
        local request=self._task_pending
        request.guard_target=target;request.guard_rob_type=rob_type;request.description=operation
        request.accept_response=function(msg)
            return msg.target_user_id==nil or tostring(msg.target_user_id)==tostring(target.user_id)
        end
        return mine_logic:MineRobTarget(rob_type,target.user_id,target.mine_index)
    end)
    return reply(self,operation,{wait=true})
end

function A:record_harvest(kind, seconds)
    local account = self:harvest_account()
    if account == "" or account == "0" then return end
    self.harvest_updates = self.harvest_updates or {}
    self.harvest_updates[kind] = {account=account, seconds=seconds or 0}
end

function A:harvest_result(result)
    if result then
        result.harvest = self.harvest_updates
        self.harvest_updates = nil
    end
    return result
end

local function mine_completion()
    local latest
    for index=1,3 do
        local info=slot_info(index)
        if info and mine_logic:GetMinesStatus(index)==MINE_STATE.mining then
            local ending=tonumber(mine_logic:GetMineEndTime(info))
            if not ending or ending<=0 then return 10 end
            latest=math.max(latest or ending,ending)
        end
    end
    if latest then return math.max(5,latest-time_logic:Now()) end
end

function A:start_mine_collect(parent_daily,followup)
    if parent_daily then self.daily_state=nil end
    self:cancel_task()
    if parent_daily then self.daily_state=parent_daily end
    self.mine_state={loaded={},collected=0,started=0,upgraded=0,failed={},errors={},confirmed={},
        refresh_at=0,plan_text="准备读取矿山与研究所",followup=followup}
    self:log("开启采集与研究：成熟领奖 → 核对材料 → 升级研究 → 按新缺口选矿层")
    return true
end

function A:mine_step()
    local state=self.mine_state
    if not state then return reply(self,"矿山与研究已停止",{done=true}) end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if self.task_barrier then local waiting=self:task_barrier();if waiting then return waiting end end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待当前战斗结束",{wait=true}) end
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at>20 then
                return reply(self,"矿山或研究请求超时，已停止；请核对游戏状态后重试",{done=true,error=true})
            end
            return reply(self,"等待服务器确认" .. (pending.description or "矿山操作"),{wait=true})
        end
        self._task_pending=nil
        local response=pending.response
        if pending.kind=="mine_read" then
            if response.result and response.result~="success" then return reply(self,"读取矿山数据失败："..tostring(response.result),{done=true,error=true}) end
            state.loaded[pending.key]=true
            if pending.key=="research" and state.verify_upgrade then
                local target=state.verify_upgrade;state.verify_upgrade=nil
                if branch_level(target.kind,target.branch)~=target.next then
                    return reply(self,"研究等级与服务器确认不一致，已停止避免重复升级",{done=true,error=true})
                end
            end
        else
            if response.result~="success" then
                return reply(self,(pending.description or "矿山操作").."失败："..tostring(response.result),{done=true,error=true})
            end
            if pending.kind=="mine_claim" then
                state.collected=state.collected+1
                self.stats.mineClaims=(self.stats.mineClaims or 0)+1
                state.loaded.resources=false;state.loaded.mines=false
                state.verify_claim=pending.index
            elseif pending.kind=="mine_start" then
                state.started=state.started+1
                self.stats.mineStarts=(self.stats.mineStarts or 0)+1
                state.loaded.mines=false
                state.verify_start={index=pending.index,level=pending.level}
            elseif pending.kind=="mine_upgrade" then
                state.upgraded=state.upgraded+1
                self.stats.researchUpgrades=(self.stats.researchUpgrades or 0)+1
                state.loaded.research=false;state.loaded.resources=false
                state.verify_upgrade=pending.target
            end
            self:log((pending.description or "矿山操作").."已确认")
            return reply(self,(pending.description or "矿山操作").."已确认",{action="矿山与研究"})
        end
    end
    local queries={
        require_snapshot(state,"mines","query_mine_info_ret","query_mine_info"),
        require_snapshot(state,"research","query_cultivation_info_ret","query_cultivation_info"),
        require_snapshot(state,"resources","query_resource_list_ret","query_resource_list"),
        require_snapshot(state,"rewards","query_all_mine_rewards_ret","query_all_mine_rewards"),
    }
    -- Nil holes in a query list must not suppress later snapshots.
    for i=1,4 do
        local q=queries[i]
        if q then
            local p=self:task_request(q.event,"mine_read",function()return network:Send({[q.message]={}})end)
            p.key=q.key
            return reply(self,"读取"..({mines="矿山队列",research="研究等级",resources="材料库存",rewards="各矿层产出"})[q.key],{wait=true})
        end
    end
    if state.verify_start then
        local expected=state.verify_start;state.verify_start=nil
        local info=slot_info(expected.index)
        if not info or tonumber(info.mine_level)~=expected.level or mine_logic:GetMinesStatus(expected.index)~=MINE_STATE.mining then
            return reply(self,"矿山开始状态与服务器不一致，已停止避免重复派遣",{done=true,error=true})
        end
    end
    if state.verify_claim then
        local index=state.verify_claim;state.verify_claim=nil
        if mine_logic:GetMinesStatus(index)==MINE_STATE.finish then
            return reply(self,"领奖后矿队状态未更新，已停止避免重复领取",{done=true,error=true})
        end
    end
    if state.followup then
        local seconds=mine_completion()
        if seconds then
            self:record_harvest("mine",seconds)
            return reply(self,"等待所有在采矿队成熟，已更新下一次收取时间",{done=true})
        end
    end
    -- Collect before spending materials. Only full maturity is eligible.
    for index=1,3 do
        if mine_logic:GetMinesStatus(index)==MINE_STATE.finish then
            local p=self:task_request("mine_receive_reward_ret","mine_claim",function()return mine_logic:MineReceiveReward(index)end)
            p.index=index;p.description="领取"..index.."号矿队奖励"
            p.accept_response=function(message)return message.mine_index==nil or tonumber(message.mine_index)==index end
            return reply(self,p.description,{action="领取矿山奖励"})
        end
    end
    local analysis,err=self:mine_analysis()
    if not analysis then return reply(self,tostring(err),{done=true,error=true}) end
    -- After harvest, upgrade all currently affordable branches in balanced order.
    for _,target in ipairs(analysis.targets) do
        if target.ready then
            local p=self:task_request("cultivate_ret","mine_upgrade",function()
                cultivation_logic:SendCultivation(target.kind,"coefficient"..target.branch)
            end)
            p.target=target;p.description=target.title.."升级至"..target.next.."级"
            p.accept_response=function(message)return message.result~="success" or tonumber(message.cur_state)==target.next end
            state.plan_text=p.description
            return reply(self,p.description,{action="自动升级研究"})
        end
    end
    if analysis.complete then
        self:record_harvest("mine",mine_completion())
        local scope=selection_count()==0 and "未选择研究，仅领取成熟奖励" or "选中的研究已达到当前上限"
        return reply(self,scope.."；本轮领取"..state.collected.."队、升级"..state.upgraded.."次",{done=true})
    end
    local notes={}
    for index=1,3 do
        if mine_logic:GetMinesStatus(index)==MINE_STATE.ready then
            local level,reason=self:mine_choose_level(index,analysis)
            if level then
                local p=self:task_request("mine_start_ret","mine_start",function()return mine_logic:StartMine(index,level)end)
                p.index=index;p.level=level;p.description=index.."号矿队采集"..level.."层"
                p.accept_response=function(message)
                    local info=message.mine_info
                    return message.result~="success" or type(info)=="table" and tonumber(info.mine_index)==index and tonumber(info.mine_level)==level
                end
                state.plan_text="按研究缺口选择："..index.."号 → "..level.."层"
                return reply(self,p.description,{action="安排矿山采集"})
            end
            notes[#notes+1]=index.."号："..tostring(reason)
        end
    end
    local active,ready=0,0
    for index=1,3 do
        local info=slot_info(index)
        local status=mine_logic:GetMinesStatus(index)
        if info and status==MINE_STATE.mining then
            active=active+1
        elseif status==MINE_STATE.ready then
            ready=ready+1
        end
    end
    local seconds=mine_completion()
    self:record_harvest("mine",seconds)
    if active==0 and ready==0 then
        local reason=#notes>0 and table.concat(notes,"；") or "尚无可用矿队，或所缺材料不由当前矿层产出"
        return reply(self,"研究材料未齐："..reason.."。已升级"..state.upgraded.."次",{done=true,partial=true})
    end
    local summary="本轮矿山操作完成：领取"..state.collected.."队、研究"..state.upgraded.."次"
    if state.started>0 then summary=summary.."、派遣"..state.started.."队" end
    if seconds then
        summary=summary.."；矿队冷却中，最后一队约"..math.ceil(seconds/60).."分钟后成熟"
    end
    if ready>0 then
        summary=summary.."；有空闲矿队暂无可产出的目标层，本轮结束"
    end
    if #notes>0 then summary=summary.."（"..table.concat(notes,"；").."）" end
    state.plan_text=summary
    return reply(self,summary,{done=true,partial=(#notes>0 and state.started==0)})
end

-- 研究细分小类子页面数据源：6 建筑 × 加成/抵抗 分支的当前等级与上限。
-- 供 C# ResearchSelectForm 展示；数据未就绪时返回空列表，仍可选择目标。
-- list 用字符串键（"1".."n"），保证跨 Lua→JS→JSON 往返形状稳定。
function A:research_branches()
    local config=config_manager.cultivation_config
    local out={sel=tonumber(A.cfg.mineResearch) or 0,mask=research_mask(),count=0,list={}}
    if type(config)~="table" then config={} end
    for kind=1,6 do
        local max_level=0
        for level in pairs(config[kind] or {}) do max_level=math.max(max_level,tonumber(level) or 0) end
        for branch=1,2 do
            local current=branch_level(kind,branch)
            out.count=out.count+1
            out.list[tostring(out.count)]={key=kind*10+branch,title=research_title(kind,branch),
                level=current,max=max_level,available=current~=nil,selected=research_selected(kind,branch),
                maxed=current~=nil and max_level>0 and current>=max_level}
        end
    end
    return out
end
