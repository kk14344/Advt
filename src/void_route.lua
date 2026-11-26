local function void_reply(self,text,fields)return reply(self,text,fields or {})end
local function void_claim(self,troop,maze,delayed)
    local p=self:task_request("get_vanity_mercenary_ret","void_claim",function()troop:GetVanityMercenaryByMazeId(maze)end)
    p.maze=maze;p.delayed=delayed
    p.accept_response=function(msg)return tonumber(msg.maze_id)==maze end
    return void_reply(self,delayed and "首遍末关已结束，现在领取倒数第二关佣兵" or (maze==1 and "领取今日初始佣兵，再评估整轮阵容" or "领取已通关的佣兵奖励"),{action="领取虚空佣兵"})
end
local function void_steps(self,troop,st)
    local layout=st.layout;local last,pen=layout.last,layout.penultimate
    local states=troop.vanity_maze_state_list or {};local ms=constants.VANITY_MAZE_STATE
    local list={}
    local function add(maze,phase)
        local row=layout.rows[maze] or {}
        list[#list+1]={maze=maze,phase=phase,capacity=tonumber(row.debut_num) or #(troop.vanity_troop or {}),boss_id=tonumber(row.boss_id)}
    end
    if states[last]==ms.maze_finish and states[pen]~=ms.challenge_success and not st.replay_available then return list end
    for maze=2,pen do
        if states[maze]==ms.challenge_able or states[maze]==ms.unlock then add(maze,"normal") end
    end
    if states[last]==ms.maze_finish and states[pen]==ms.challenge_success then st.first_final_done=true end
    local held=states[pen]~=ms.maze_finish and not st.fallback
    if st.first_final_done then
        if held or st.replay_available then add(last,"final_repeat") end
    elseif held then add(last,"final_first");add(last,"final_repeat")
    elseif states[last]==ms.challenge_able then add(last,"final_normal") end
    return list
end
local function void_recruit(self,troop,st,reason)
    local config=(config_manager.vanity_buy_other_pay_config or {})[st.week]
    local left=tonumber(troop.reduce_search_times)
    if not config or not left then return void_reply(self,"整轮阵容不足，但尚未获得虚空招募次数或价格，停止购买",{done=true,error=true}) end
    local maximum=tonumber(config.recruit_num) or 0
    local prices={}
    for part in tostring(config.consume or ""):gmatch("[^|]+")do
        local number=tonumber(part)
        if not number or number<0 then return void_reply(self,"虚空招募价格格式异常，未发送购买",{done=true,error=true}) end
        prices[#prices+1]=number
    end
    local index=maximum-left+1;local price=prices[index]
    if left<=0 then return void_reply(self,"虚空招募次数已用完，仍未满足整轮及末关重打要求："..tostring(reason),{done=true,partial=true}) end
    if left>maximum or not price or price<0 then return void_reply(self,"虚空招募价格数据异常，未发送购买",{done=true,error=true}) end
    local blood=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.blood_diamond
    if not blood or (tonumber(resource_logic:GetResourceNum(blood)) or 0)<price then
        return void_reply(self,"血钻不足以进行下一次虚空招募（需 "..price.."），停止购买",{done=true,partial=true})
    end
    local p=self:task_request("vanity_buy_recruit_ret","void_recruit",function()
        network:Send({vanity_buy_recruit={}})
    end)
    p.price=price;p.before_left=left;p.before_pool=#(troop.vanity_mercenarys_list or {})
    st.search=nil
    self:log("整轮阵容尚不足："..tostring(reason).."；进行一次虚空招募（"..price.."血钻）")
    return void_reply(self,"进行一次虚空招募，返回后重新评估全部剩余战斗",{action="血钻招募"})
end
function A:start_void()
    self:cancel_task()
    self.void_state={week=scene_manager:GetCurrentSceneName()=="world" and self:guide_week() or nil,
        queried=false,mazes=false,runes=false,attempts=0,team_for=nil,route_index=1,recruits=0,claims=0,final_wins=0}
    return true
end
function A:void_step()
    local st=self.void_state
    if not st then return void_reply(self,"虚空任务已停止",{done=true}) end
    local barrier=self:task_barrier();if barrier then return barrier end
    if scene_manager:GetCurrentSceneName()~="world" then return void_reply(self,"等待进入游戏",{wait=true}) end
    st.week=st.week or self:guide_week()
    if self:guide_week()~=st.week then return void_reply(self,"服务器日期已变化，停止本轮虚空",{done=true,partial=true}) end
    if self.inBattle and self.cfg.autoSkipBattle==false then return void_reply(self,"等待虚空战斗结算",{wait=true}) end
    self:hide_panel("reward_panel");self:hide_panel("vanity_adventure_result")
    self:hide_panel("new_vanity_mercenarys_panel")
    local troop=require("logic.troop");local ms=constants.VANITY_MAZE_STATE
    local p=self._task_pending
    if p then
        if not p.response then
            if task_now(self)-p.sent_at<=20 then return void_reply(self,"等待虚空服务器确认",{wait=true}) end
            -- Leave the uncertain mutation tracked so a new task cannot replay it.
            self:cancel_task()
            return void_reply(self,"虚空请求超时，本轮停止，未重复购买、挑战或领取",{done=true,error=true})
        end
        self._task_pending=nil
        local msg=p.response;local result=msg.result
        if p.kind=="void_query" then
            if result and result~="success" then return void_reply(self,"读取虚空阵容失败",{done=true,error=true}) end
            st.queried=true
        elseif p.kind=="void_mazes" or p.kind=="void_replay_check" then
            if result and result~="success" then return void_reply(self,"读取虚空关卡失败",{done=true,error=true}) end
            if type(msg.states)=="table" then troop.vanity_maze_state_list=msg.states end
            st.mazes=true
            if p.kind=="void_replay_check" then
                st.check_replay=false
                if (troop.vanity_maze_state_list or {})[st.layout.last]==ms.challenge_able then
                    st.replay_available=true;self:log("服务器确认末关重新可挑战，执行一次重打")
                else return void_reply(self,"延后佣兵奖励已领取；服务器未开放末关重打，本轮结束",{done=true,partial=true}) end
            end
        elseif p.kind=="void_runes" then
            st.runes=true
            if result and result~="success" then return void_reply(self,"符文状态读取失败，未开始付费招募",{done=true,error=true}) end
        elseif p.kind=="void_claim" then
            if result~="success" then return void_reply(self,"虚空佣兵奖励领取失败："..tostring(result),{done=true,error=true}) end
            st.claims=st.claims+1;st.team_for=nil
            if p.maze==1 then st.queried=false;st.assessed=false
            elseif p.delayed then st.check_replay=true
            elseif st.fallback then st.assessed=false;st.route_index=1 end
            return void_reply(self,p.delayed and "已领取倒数第二关佣兵，重新查询末关是否重置" or "佣兵奖励已领取",{action="佣兵领取确认"})
        elseif p.kind=="void_recruit" then
            if result~="success" then return void_reply(self,"虚空招募失败："..tostring(result).."，未重复购买",{done=true,error=true}) end
            local left=tonumber(troop.reduce_search_times)
            if not left or left>=p.before_left or #(troop.vanity_mercenarys_list or {})<=p.before_pool then
                return void_reply(self,"招募回执未确认次数减少及新佣兵，停止继续付费",{done=true,error=true})
            end
            st.recruits=st.recruits+1;self:spend_record(p.price,"虚空补充招募")
            st.assessed=false;st.search=nil;st.route_index=1
            return void_reply(self,"本次招募已确认，重新评估整轮阵容与剩余出战次数",{action="重新评估虚空"})
        elseif p.kind=="void_team" then
            if result~="success" then return void_reply(self,"虚空阵容上阵失败："..tostring(result),{done=true,error=true}) end
            for i,id in ipairs(p.formation)do
                if (troop.vanity_troop or {})[i]~=id then return void_reply(self,"虚空阵容同步不一致，停止挑战",{done=true,error=true}) end
            end
            st.team_for=st.route_index
            return void_reply(self,"阵容已按整轮计划上阵",{action="虚空上阵确认"})
        elseif p.kind=="void_challenge" then
            if result=="error_maze_state" and p.phase=="final_first" then
                st.fallback=true;st.assessed=false;st.team_for=nil;st.search=nil
                self:log("服务器要求先领取倒数第二关奖励，转为正常通关顺序")
                return void_reply(self,"服务器未接受延后领奖顺序，改为先领奖再通关末关",{action="调整虚空顺序"})
            end
            if result~="success" and result~="battle_failure" then
                return void_reply(self,"虚空挑战失败："..tostring(result),{done=true,error=true})
            end
            st.team_for=nil
            if result=="battle_failure" then
                st.attempts=st.attempts+1;self.stats.losses=self.stats.losses+1
                st.assessed=false;st.search=nil
                if st.attempts>=3 then return void_reply(self,"同关连续失败三次，停止本轮并保留未领取奖励",{done=true,error=true}) end
                return void_reply(self,"战斗未通过，按剩余出战次数重新评估",{action="重新评估虚空"})
            end
            st.attempts=0;self.stats.wins=self.stats.wins+1
            if p.phase=="final_first" then st.first_final_done=true;st.final_wins=st.final_wins+1
            elseif p.phase=="final_repeat" or p.phase=="final_normal" then
                st.final_wins=st.final_wins+1
                return void_reply(self,p.phase=="final_repeat" and "末关第二遍已通关，本轮虚空完成" or "今日末关已通关",{done=true})
            end
            st.route_index=st.route_index+1
            return void_reply(self,p.maze==st.layout.penultimate and "倒数第二关已通关，暂不领取佣兵，先挑战末关" or "本关已通关，按计划继续",{action="虚空战斗结算"})
        end
    end
    if not st.queried then
        self:task_request("query_vanity_troop_ret","void_query",function()troop:QueryVantiyToopInfo()end)
        return void_reply(self,"读取已有虚空佣兵及剩余出战次数",{wait=true})
    end
    if not st.mazes then
        self:task_request("query_vanity_maze_states_ret","void_mazes",function()network:Send({query_vanity_maze_states={}})end)
        return void_reply(self,"读取今天的虚空关卡与领奖状态",{wait=true})
    end
    st.layout=st.layout or self:void_layout(st.week)
    if not st.layout then return void_reply(self,"今天的虚空配置与攻略不匹配，未开始购买",{done=true,error=true}) end
    local states=troop.vanity_maze_state_list or {}
    for maze=1,st.layout.last do
        local value=tonumber(states[maze])
        if not value or value<1 or value>4 then return void_reply(self,"尚未获得完整的今日虚空状态，未开始招募或挑战",{done=true,error=true}) end
    end
    if states[1]==ms.challenge_success then return void_claim(self,troop,1,false) end
    for maze=2,st.layout.penultimate-1 do
        if states[maze]==ms.challenge_success then return void_claim(self,troop,maze,false) end
    end
    if st.fallback and states[st.layout.penultimate]==ms.challenge_success then return void_claim(self,troop,st.layout.penultimate,false) end
    if not st.runes then
        self:install_task_hooks()
        if self._task_hooks.query_rune_info_ret then
            self:task_request("query_rune_info_ret","void_runes",function()network:Send({query_rune_info={}})end)
            return void_reply(self,"读取已装备符文，纳入先攻与四维评估",{wait=true})
        end
        st.runes=true
    end
    if not st.assessed then
        st.search=st.search or self:void_plan_begin(troop,void_steps(self,troop,st),st.week)
        local plan,reason,exhausted=self:void_plan_next(troop,st.search)
        if plan then
            st.route=plan.route;st.route_index=1;st.assessed=true;st.search=nil
            if #st.route==0 then return void_reply(self,"今日虚空已完成，没有剩余可执行战斗",{done=true}) end
            self:log("整轮评估通过：预留 "..#st.route.." 场出战，包含可执行的末关重打；按游戏 Boss 与已装备符文核对攻略门槛")
            return void_reply(self,"整轮已有阵容满足攻略门槛，开始顺序通关",{action="整轮评估完成"})
        elseif exhausted then
            if st.search.fatal then return void_reply(self,reason,{done=true,error=true}) end
            return void_recruit(self,troop,st,reason)
        end
        return void_reply(self,"评估整轮阵容：方案 "..st.search.tried.."，同时预留重复末关的次数",{wait=true})
    end
    if st.first_final_done and states[st.layout.penultimate]==ms.challenge_success then
        return void_claim(self,troop,st.layout.penultimate,true)
    end
    if st.check_replay then
        self:task_request("query_vanity_maze_states_ret","void_replay_check",function()network:Send({query_vanity_maze_states={}})end)
        return void_reply(self,"向服务器确认末关是否重新开放",{wait=true})
    end
    local step=st.route[st.route_index]
    if not step then return void_reply(self,"本轮虚空计划已完成",{done=true}) end
    if step.phase=="final_repeat" and not st.replay_available then
        return void_reply(self,"尚未获得服务器的末关重置确认，停止重复挑战",{done=true,partial=true})
    end
    if st.team_for~=st.route_index then
        local formation={};local names={};local valid=true
        for i,merc in ipairs(step.chosen)do
            formation[i]=merc.id;names[i]=merc.name
            local entry=troop:GetVanityMercenarys(merc.id)
            if not entry or (tonumber(entry.battle_num) or 0)<1 then valid=false end
        end
        if not valid then st.assessed=false;st.search=nil;return void_reply(self,"佣兵可用次数已变化，重新评估整轮",{wait=true}) end
        local stats,err=A._void_stats(troop,step.chosen,self:void_rune_effects(),(config_manager.monster_config or {})[step.boss_id or 0])
        if not stats then return void_reply(self,"读取阵容属性失败："..tostring(err),{done=true,error=true}) end
        if (step.plan.bpMin and stats.bp<=step.plan.bpMin) or (step.plan.speedMin and stats.speed<=step.plan.speedMin)
            or (step.plan.speedMax and stats.speed>=step.plan.speedMax) then
            st.assessed=false;st.search=nil;return void_reply(self,"符文或阵容属性变化，重新评估",{wait=true})
        end
        for i=#formation+1,#(troop.vanity_troop or {})do formation[i]=0 end
        troop.change_troop=formation
        local p=self:task_request("vanity_sync_troop_ret","void_team",function()network:Send({vanity_sync_troop={troop=formation}})end)
        p.formation=formation
        self:log("第 "..(step.maze-1).." 关"..(step.phase=="final_repeat" and "（重打）" or "").."："..table.concat(names," → ").."；战力 "..stats.bp.."，含符文相对先攻 "..stats.speed)
        return void_reply(self,"按整轮计划上阵第 "..(step.maze-1).." 关",{action="虚空上阵"})
    end
    local p=self:task_request("vanity_challenge_ret","void_challenge",function()troop:FightingByMazeId(step.maze)end)
    p.maze=step.maze;p.phase=step.phase
    p.accept_response=function(msg)return tonumber(msg.maze_id)==step.maze end
    return void_reply(self,"挑战第 "..(step.maze-1).." 关"..(step.phase=="final_repeat" and "（第二遍）" or ""),{action="虚空挑战"})
end
