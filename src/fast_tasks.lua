-- Additional task flows use the same single pending-request queue.
A.cfg.dailyActivityRewards=true
for _,event in ipairs({"query_carnival_config_ret","query_carnival_info_ret","take_carnival_reward_ret",
    "query_battle_pass_ret","ret_battle_pass_reward_receive","query_first_payment_ret","revice_first_payment_ret",
    "take_fund_profit_ret","take_sns_invitation_reward_ret"}) do
    A.DAILY_EVENTS[#A.DAILY_EVENTS+1]=event
end

local function optional_logic(name)
    local ok,value=pcall(require,name)
    return ok and type(value)=="table" and value or nil
end

local function activity_send(self,event,key,payload,kind,description)
    local p=self:task_request(event,"daily_action",function()return network:Send({[key]=payload or {}})end)
    p.extra="activity_" .. kind;p.description=description
    return reply(self,description,{action="活动奖励"})
end

function A:daily_activity_response(p,result)
    local state=self.daily_state
    local kind=p.extra:sub(10)
    if p.activity_query then
        state.activity_queries[p.activity_query]=true
        if result and result~="success" then
            state.activity_done=true;self:log("活动奖励读取失败：" .. tostring(result))
        end
    elseif result=="success" or (result==nil and kind=="battlepass_claim") then
        state.activity_claims=state.activity_claims+1
        if kind=="battlepass_claim" then state.activity_queries.battlepass=false end
    else
        self:log("活动奖励领取失败：" .. tostring(result) .. "，继续其他已达成奖励")
    end
end

function A:daily_activity_rewards()
    local state=self.daily_state
    local c=optional_logic("logic.carnival")
    self:install_task_hooks()
    local hooks=self._task_hooks or {}
    if not c or not hooks.query_carnival_info_ret or not hooks.take_carnival_reward_ret then
        state.activity_done=true
        return nil
    end
    local queries=state.activity_queries
    local spec
    if not queries.config and next(c.all_config or {})==nil and hooks.query_carnival_config_ret then
        spec={"config","query_carnival_config_ret","query_carnival_config"}
    elseif not queries.info then spec={"info","query_carnival_info_ret","query_carnival_info"}
    elseif not queries.battlepass and hooks.query_battle_pass_ret then
        spec={"battlepass","query_battle_pass_ret","query_battle_pass"}
    elseif not queries.firstpay and hooks.query_first_payment_ret then
        spec={"firstpay","query_first_payment_ret","query_first_payment"}
    end
    if spec then
        local result=activity_send(self,spec[2],spec[3],{},"query","读取活动奖励状态")
        self._task_pending.activity_query=spec[1]
        return result
    end
    if (state.activity_tries or 0)>=500 then state.activity_done=true;return nil end
    -- These types award completed goals. Exchanges, shops, lotteries and
    -- investment purchases use separate interfaces and are never inferred here.
    local earned_types={[1]=true,[2]=true,[3]=true,[5]=true,[7]=true,[8]=true,[9]=true,[10]=true,[15]=true,[16]=true,[20]=true,[21]=true}
    local ccst=optional_logic("util.client_constants") or {}
    local ready=(ccst.CARNIVAL_STEP_STATUS or {}).can_take or 2
    local keys={}
    for key in pairs(c.all_config or {}) do keys[#keys+1]=key end
    table.sort(keys,function(a,b)return tostring(a)<tostring(b)end)
    for _,key in ipairs(keys) do
        local conf=c.all_config[key]
        if earned_types[tonumber(conf.carnival_type)] then
            pcall(function()c:UpdateStageRewardMark(key,true)end)
            for step,mark in pairs((c.stages_reward_mark or {})[key] or {}) do
                local info=(c.info_map or {})[key] or {}
                local remaining=(info.step_reward or {})[step] or 1
                local token=tostring(key)..":"..step..":"..tostring(remaining)
                local owned=tonumber(conf.carnival_type)~=15 or (tonumber((info.cur_value_multi or {})[step]) or 0)>0
                local request=tonumber(conf.carnival_type)==15 and "take_fund_profit" or
                    (tonumber(conf.carnival_type)==16 and "take_sns_invitation_reward" or "take_carnival_reward")
                if owned and hooks[request.."_ret"] and mark==ready and not state.activity_seen[token] then
                    local ok,can=pcall(function()return c:CanTakeReward(conf,step)end)
                    if ok and can then
                        state.activity_seen[token]=true;state.activity_tries=(state.activity_tries or 0)+1
                        local r=activity_send(self,request.."_ret",request,
                            {key=key,step=step,exchange_id=0},"claim","领取活动奖励：" .. tostring(conf.name or key) .. " 第" .. step .. "档")
                        self._task_pending.accept_response=function(msg)return msg.key==key and msg.step==step end
                        return r
                    end
                end
            end
        end
    end
    local bp=c.battle_pass or {}
    if hooks.ret_battle_pass_reward_receive and (tonumber(bp.id) or 0)>0 then
        local level=tonumber(bp.level) or 0
        for _,gift in ipairs({0,1}) do
            local received=tonumber(gift==0 and bp.normal_revice_lv or bp.vip_revice_lv) or 0
            local allowed=gift==0 or (tonumber(bp.is_has) or 0)>0
            local token="bp:"..gift..":"..level..":"..received..":"..tostring(bp.gift_count)
            if allowed and received<level and not state.activity_seen[token] then
                state.activity_seen[token]=true;state.activity_tries=(state.activity_tries or 0)+1
                return activity_send(self,"ret_battle_pass_reward_receive","battle_pass_reward_receive",
                    {is_gift=gift},"battlepass_claim","一键领取已解锁战令奖励")
            end
        end
    end
    if hooks.revice_first_payment_ret then
        local day
        pcall(function()day=time_logic:GetNowDayTime()end)
        for _,info in pairs(c.first_payment_map or {}) do
            local conf=(config_manager.first_payment_config or {})[info.price]
            local token="firstpay:"..tostring(info.price)
            if day and conf and info.status==2 and (tonumber(info.count) or 0)<(tonumber(conf.count) or 0)
                and (tonumber(info.revice_time) or 0)<day and not state.activity_seen[token] then
                state.activity_seen[token]=true;state.activity_tries=(state.activity_tries or 0)+1
                return activity_send(self,"revice_first_payment_ret","revice_first_payment",
                    {price=info.price},"claim","领取已激活首充每日奖励")
            end
        end
    end
    state.activity_done=true
    self:log("活动奖励本轮已领取" .. state.activity_claims .. "份")
end

function A:start_mining_boss()
    self:cancel_task()
    self.mining_boss_state={loaded=false,count=0,started=task_now(self)}
    self:log("开始矿区首领挑战：消耗恶魔勋章，服务器结算后连续挑战")
    return true
end

function A:mining_boss_step()
    local state=self.mining_boss_state
    if not state then return reply(self,"矿区首领任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    self:close_reward_panel()
    local p=self._task_pending
    if p then
        if not p.response then
            if task_now(self)-p.sent_at<=25 then return reply(self,"等待矿区首领服务器回执",{wait=true}) end
            return reply(self,"矿区首领请求超时，停止重复挑战",{done=true,error=true})
        end
        self._task_pending=nil
        if p.response.result and p.response.result~="success" then
            return reply(self,"矿区首领停止：" .. tostring(p.response.result),{done=true,partial=true})
        end
        if p.kind=="mb_query" then state.loaded=true
        elseif p.kind=="mb_fight" then state.count=state.count+1 end
    end
    if not state.loaded then
        self:task_request("query_mining_info_ret","mb_query",function()network:Send({query_mining_info={}})end)
        return reply(self,"读取矿区首领与恶魔勋章",{wait=true})
    end
    if state.count>=1000 then return reply(self,"矿区首领本轮挑战达到1000次",{done=true}) end
    local id=(constants.RESOURCE_TYPE or {}).demon_medal
    local unlock=(tonumber(mining.cave_boss_lv) or 0)==0
    local cost=tonumber(unlock and constants.OPEN_CAVE_BOSS_DEMON_MEDAL or constants.CAVE_BOSS_CHALLANGE_SUB)
    if not id or not cost or not resource_logic:CheckResourceNum(id,cost,false) then
        return reply(self,"恶魔勋章不足，矿区首领已挑战" .. state.count .. "次",{done=true,partial=state.count==0})
    end
    if bag_logic:GetSpaceCount()<(tonumber(constants.EVENT_MAX_BAG_SPACE_COUNT) or 5) then
        return reply(self,"背包空间不足，停止矿区首领挑战",{done=true,partial=true})
    end
    local key=unlock and "unlock_cave_boss" or "challenge_cave_boss"
    self:task_request(key.."_ret",unlock and "mb_unlock" or "mb_fight",function()network:Send({[key]={}})end)
    return reply(self,unlock and "解锁矿区首领" or ("挑战矿区首领（第" .. (state.count+1) .. "次）"),{action="矿区首领"})
end
