-- Execute actual liveness actions, then verify progress from the server.
-- Paid actions are authorized only by the corresponding task phase.
A.DAILY_EVENTS={
 "query_daily_ret","query_liveness_info_ret","take_check_in_reward_ret",
 "take_daily_prayer_ret","take_daily_alchemy_ret","get_liveness_reward_ret","get_activity_reward_ret",
 "query_social_info_ret","send_gift_ret","recruit_mercenary_ret","alloc_mercenary_exp_ret",
 "forge_mercenary_weapon_ret","query_mining_info_ret","query_cave_event_config_info_ret",
 "add_mining_project_ret","get_mining_project_reward_ret","use_mining_tool_ret",
 "solve_cave_event_ret","unlock_cave_boss_ret","challenge_cave_boss_ret","buy_cave_challenge_ret",
 "query_ladder_info_ret","challenge_ladder_ret","query_rune_info_ret","upgrade_rune_ret",
 "query_escort_info_ret","query_escort_times_ret","query_rob_target_list_ret",
 "escort_refresh_tramcar_ret","escort_start_ret","escort_rob_target_ret","escort_receive_reward_ret",
 "query_vip_ret","take_vip_reward_ret",
 "query_mail_info_ret","open_mail_ret",
 -- 冒险页探险结果领取（1.42）：放大镜按钮绑定的原生开箱回包
 "open_box_ret",
 -- 合战 / 宿命远征阶段（1.18）
 "query_campaign_info_ret","query_campaign_level_ret","query_campaign_buff_ret",
 "query_campaign_overtime_ret","campaign_overtime_ret","battle_campaign_ret","convert_campaign_exp_ret",
 "query_expedition_ret","expedition_apend_times_ret","expedition_fight_ret","refresh_expedition_search_ret",
}
for id=1,15 do A.cfg["dailyTask"..id]=true end
for _,key in ipairs({"dailyRewards","dailyFreePrayer","dailyFreeAlchemy","dailyIncludeMine",
    "dailyGiftAll","dailyCheckinAll","dailyVipReward","dailyVoidExchange","dailyGuildBoss","dailyEvents",
    "dailyMail","dailyGoldSummon"})do A.cfg[key]=true end
A.cfg.mineResearch=tonumber(A.cfg.mineResearch) or 0
A.cfg.dailyMineGuards=true
A.cfg.dailyAlchemyTier=tonumber(A.cfg.dailyAlchemyTier) or 1
A.cfg.dailyTrollLevel=tonumber(A.cfg.dailyTrollLevel) or 0
A.cfg.dailyUndergroundLevel=tonumber(A.cfg.dailyUndergroundLevel) or 0
A._guild_boss_done_day=A._guild_boss_done_day or {}

function A:guild_boss_today()
    local now=0
    if time_logic and type(time_logic.Now)=="function" then
        local ok,v=pcall(time_logic.Now,time_logic)
        if ok and tonumber(v) then now=tonumber(v) end
    end
    -- 按北京时间（UTC+8）00:00 划分自然日
    return math.floor((now+28800)/86400)
end

function A:guild_boss_account_key()
    local acct=type(self.harvest_account)=="function" and self:harvest_account() or ""
    if not acct or acct=="" or acct=="0" then return "default" end
    return tostring(acct)
end

function A:set_guild_boss_done_days(map)
    self._guild_boss_done_day=self._guild_boss_done_day or {}
    if type(map)=="table" then
        for k,v in pairs(map) do
            local day=tonumber(v)
            if day then
                local key=tostring(k)
                day=math.floor(day)
                if (self._guild_boss_done_day[key] or 0)<day then
                    self._guild_boss_done_day[key]=day
                end
            end
        end
    end
    return true
end

function A:guild_boss_done_today()
    self._guild_boss_done_day=self._guild_boss_done_day or {}
    local key=self:guild_boss_account_key()
    return self._guild_boss_done_day[key]==self:guild_boss_today()
end

function A:mark_guild_boss_done_today()
    self._guild_boss_done_day=self._guild_boss_done_day or {}
    local key=self:guild_boss_account_key()
    local day=self:guild_boss_today()
    self._guild_boss_done_day[key]=day
    return {account=key,day=day}
end

function A:daily_enabled(id)
    local state=self.daily_state
    return not state or not state.selected or state.selected[id]~=false
end

function A:daily_message_is_free(name,payload)
    payload=payload or {}
    if name=="revice_first_payment" then return true end
    if name:sub(1,6)=="query_" or name:find("heart") or name:find("ping") then return true end
    if name=="buy_cave_challenge" then
        -- 巨魔巢穴/地下探险按用户要求购买额外次数；仅当日常流程明确允许时放行
        local active=__MWA
        return not not (active and active.daily_state and active.daily_state.buy_cave_challenge)
    end
    if name=="guild_boss_buy_ticket" then
        -- 工会首领许可证同样仅在许可阶段明确允许时放行（受血钻预算约束）
        local active=__MWA
        return not not (active and active.daily_state and active.daily_state.buy_guild_ticket)
    end
    if name=="buy_campaign_overtime" or name=="expedition_apend_times" then
        -- 合战加时与宿命远征挑战次数：仅在活动阶段明确允许时放行，
        -- 且该阶段已先按消费策略预算与血钻余额核对过价格。
        local active=__MWA
        return not not (active and active.daily_state and active.daily_state.allow_event_purchase)
    end
    if name:find("buy") or name:find("payment") or name:find("purchase") then return false end
    if name=="refresh_arena_rival" or name=="unlock_mining_project" or name=="refresh_mining_area"
        or name=="clear_recurit_library_time" or name=="vanity_buy_recruit" then return false end
    if name=="recruit_mercenary" then
        return payload.door_type=="friendship_door" or payload.door_type=="recruiting_door"
    end
    if name=="forge_mercenary_weapon" then return tonumber(payload.extra_chance)==0 end
    if name=="take_daily_prayer" then
        local config=constants.PRAYER_CONFIG
        return config and config[payload.index] and tonumber(config[payload.index].req_value)==0
    end
    if name=="take_daily_alchemy" then
        local config=constants.ALCHEMY_CONFIG
        if not (config and config[payload.index]) then return false end
        if tonumber(config[payload.index].req_value)==0 then return true end
        local active=__MWA
        local st=active and active.daily_state
        return not not (st and st.allow_paid_alchemy and tonumber(payload.index)==st.alchemy_tier)
    end
    if name=="escort_refresh_tramcar" then
        local escort=require("logic.escort")
        return payload.refresh_type=="random" and
            (tonumber(escort.escort_times.refresh_tramcar) or 0)<(tonumber(constants.FREE_REFRESH_TRAMCAR_TIMES) or 0)
    end
    if name=="escort_refresh_rob_target_list" then return payload.refresh_type~="immediately" end
    return true
end

function A:install_daily_spend_guard()
    if network.Send==network._mwa_daily_guard then return end
    local original=network.Send
    local wrapped=function(net,message,...)
        local active=__MWA
        if active and active.daily_state and active.daily_state.running then
            for name,payload in pairs(message or {}) do
                if not active:daily_message_is_free(name,payload) then
                    active.daily_state.blocked_spend=name
                    active:log("日常已拦截可能消耗血钻的操作：" .. name)
                    return false
                end
            end
        end
        return original(net,message,...)
    end
    network.Send=wrapped;network._mwa_daily_guard=wrapped
end

function A:start_daily(manual_runes,scheduled_checkin,refine_followup)
    self:cancel_task()
    self.daily_state={running=true,loaded={},skipped={},stalls={},attempts={},
        claims=0,actions=0,day=nil,started=task_now(self),robbed={}}
    local state=self.daily_state;state.selected={};state.defer_tail=true
    state.spend_started=self:spend_total()
    for id=1,15 do state.selected[id]=self.cfg["dailyTask"..id]~=false end
    state.rewards=self.cfg.dailyRewards~=false
    state.prayer=self.cfg.dailyFreePrayer~=false;state.alchemy=self.cfg.dailyFreeAlchemy~=false
    state.include_mine=self.cfg.dailyIncludeMine==true
    state.mine_guards=self.cfg.dailyMineGuards~=false and manual_runes==true
    state.manual_runes=manual_runes==true
    -- 附加阶段（1.15）：好友点数全送 / 全时段签到 / 月卡每日奖励 / 虚空商店兑换
    state.gift_all=self.cfg.dailyGiftAll~=false
    state.checkin_all=self.cfg.dailyCheckinAll~=false
    state.refine_followup=refine_followup==true
    if (scheduled_checkin or refine_followup) and state.selected[13] then
        state.skipped[13]="无人值守日常，强化需手动选择符文"
    end
    if scheduled_checkin then
        state.start_with_checkin=true
        state.selected[1]=true
        state.checkin_all=true
    end
    state.vip_reward=self.cfg.dailyVipReward~=false
    state.void_exchange=self.cfg.dailyVoidExchange~=false
    state.void_challenge=self.cfg.dailyVoidChallenge~=false
    state.market_exchange=self.cfg.dailyMarketExchange~=false
    state.mystery_shop_enabled=self.cfg.dailyMysteryShop==true
    state.mail_enabled=self.cfg.dailyMail~=false
    state.mail_done=false;state.mail_attempted={};state.mail_claimed=0;state.mail_failures=0
    state.gold_enabled=self.cfg.dailyGoldSummon~=false
    state.gold_summons=0;state.gold_done=false
    state.refine_enabled=self.cfg.dailyRefine~=false;state.refine_done=false
    -- 冒险页探险结果：没有可领奖励时也会立即核对一次后继续。
    state.adventure_claim=true;state.adventure_done=false
    state.activity_rewards=self.cfg.dailyActivityRewards~=false
    state.activity_done=false;state.activity_queries={};state.activity_seen={};state.activity_claims=0
    -- 工会首领阶段：买满当日剩余许可证并挑战1次
    state.guild_boss=self.cfg.dailyGuildBoss~=false
    state.guild_boss_ran=false
    state.guild_boss_tickets=0 -- 0 表示买满服务器剩余额度
    state.guild_boss_fights=1
    -- 合战 / 宿命远征阶段（1.18）：仅当天活动开启时执行，收尾链统一调度
    state.events_enabled=self.cfg.dailyEvents~=false
    state.events_ran=false
    state.child_events=false
    state.allow_event_purchase=false
    state.gifts_sent=0;state.checkin_claims=0
    state.gifts_done=false;state.checkin_stop=false;state.vip_done=false;state.void_ran=false
    -- 启动预览（PRD P0-01）：先说清楚这次会做什么、资源边界在哪里。
    local enabled_titles = {}
    for id = 1, 15 do
        if state.selected[id] then
            local conf = nil
            for _, item in pairs(config_manager.activity_config or {}) do
                if tonumber(item.liveness_id) == id then conf = item; break end
            end
            enabled_titles[#enabled_titles + 1] = conf and conf.active_title or tostring(id)
        end
    end
    self:log("本次日常将执行：" .. (#enabled_titles > 0 and table.concat(enabled_titles, "、") or "无"))
    local extras={}
    if state.gift_all then extras[#extras+1]="好友点数送全部好友" end
    if state.checkin_all then extras[#extras+1]="全时段签到" end
    if state.vip_reward then extras[#extras+1]="月卡每日奖励" end
    if state.void_exchange then extras[#extras+1]="虚空商店兑换" end
    if state.mail_enabled then extras[#extras+1]="领取全部邮件" end
    if state.gold_enabled then extras[#extras+1]="金币召唤4次" end
    if state.guild_boss then extras[#extras+1]="工会首领许可证买满并挑战1次（消耗血钻，预算不限）" end
    if state.events_enabled then extras[#extras+1]="合战与宿命远征（开启时买满次数并推进，购买预算不限）" end
    if state.selected[7] then extras[#extras+1]="矿车运送（优先最高级，余量不足按设置购买；本次只发一趟）" end
    if state.selected[14] then extras[#extras+1]="矿车拦截（先4级再3级，用完现有次数，按设置血钻刷新目标）" end
    self:log(#extras>0 and ("附加阶段：" .. table.concat(extras, "、")) or "附加阶段：无")
    self:log("购买按服务器剩余额度与实际余额执行；血钻预算不限，旧消费策略已移除")
    self:install_daily_spend_guard()
    return true
end

local function daily_item(daily,id)
    for _,item in pairs(daily.liveness_list or {}) do if item.liveness_id==id then return item end end
end

-- 与游戏 mining_main_panel 一致：副本开放表用 time_logic:GetDateInfo(Now).wday，
-- 也就是 os.date("!*t") 的 wday（1=周日 … 7=周六），不是攻略用的 getWDay（1=周一）。
function A:daily_cave_day()
    local ok,info=pcall(function() return time_logic:GetDateInfo(time_logic:Now()) end)
    if ok and type(info)=="table" and tonumber(info.wday) then return tonumber(info.wday) end
    local seconds=tonumber(time_logic:Now()) or os.time()
    local offset=tonumber(time_logic.time_zone_offset) or 28800
    -- 1970-01-01 是周四（wday=5），据此换算 os.date 的 wday。
    return (math.floor((seconds+offset)/86400)+4)%7+1
end

local function daily_conf(id)
    for _,conf in pairs(config_manager.activity_config or {}) do if conf.liveness_id==id then return conf end end
end

-- ------------------------------------------------------------
-- 副本任务（9=巨魔巢穴, 10=地下探险）是否仍有事可做（1.17）：
-- 活跃度达成后仍要求「可购买次数买完 + 挑战次数打完」。
-- ------------------------------------------------------------
function A:daily_cave_kinds(id)
    local opened=(mining.cave_config_info or {})[self:daily_cave_day()] or {}
    local kinds,seen={},{}
    local types=constants.CAVE_TYPE or {}
    local allowed=id==9 and {[types.cave3 or 3]=true,[types.cave4 or 4]=true,[types.cave5 or 5]=true}
        or {[types.cave1 or 1]=true,[types.cave2 or 2]=true}
    for _,kind in pairs(opened) do
        kind=tonumber(kind) or 0
        if allowed[kind] and not seen[kind] then kinds[#kinds+1]=kind;seen[kind]=true end
    end
    table.sort(kinds)
    return kinds
end

function A:daily_cave_pending(id)
    local state=self.daily_state
    if not state then return false end
    if not state.loaded.mining or not state.loaded.caves then return true end
    local kinds=self:daily_cave_kinds(id)
    if #kinds==0 then return false end
    -- 仍有可购买次数且购买未被停止
    if state.cave_buy_stop~=true then
        for _,kind in ipairs(kinds) do
            local left=tonumber((mining.cave_buy_challenge_nums or {})[kind])
            local max_buy=tonumber((constants.CAVE_DAILY_BUY_CHALLENGE_NUM or {})[kind])
            if left==nil then left=max_buy or 1 end
            local bought=(state.cave_bought or {})[kind] or 0
            if not (state.cave_buy_stops or {})[kind] and math.min(left,(max_buy or left)-bought)>0 then return true end
        end
    end
    -- 仍有挑战次数（含已购买的）
    for _,kind in ipairs(kinds) do
        if not (state.cave_skips or {})[kind] and (tonumber((mining.cave_challenge_nums or {})[kind]) or 0)>0 then return true end
    end
    return false
end

local function can_pay(name,count)
    if not count or count<=0 then return true end
    local id=constants.RESOURCE_TYPE[name]
    return id and name~="blood_diamond" and resource_logic:CheckResourceNum(id,count,false)
end

function A:daily_request(event,key,payload,description,id,flag)
    if not self:daily_message_is_free(key,payload) then error("日常禁止消耗血钻：" .. key) end
    local p=self:task_request(event,"daily_action",function() return network:Send({[key]=payload or {}}) end)
    p.task_id=id;p.description=description;p.flag=flag
    local state=self.daily_state
    if id then
        local item=daily_item(require("logic.daily"),id)
        p.before=tonumber(item and item.completion_count) or 0
        state.attempts[id]=(state.attempts[id] or 0)+1
    end
    return reply(self,description,{action="执行日常"})
end

function A:daily_query(event,key,flag)
    return self:daily_request(event,key,{},"读取日常相关数据",nil,flag)
end

function A:daily_skip(id,reason)
    self.daily_state.skipped[id]=reason
    local conf=daily_conf(id)
    local title=conf and conf.active_title or tostring(id)
    self:log("日常暂未完成「" .. title .. "」：" .. reason)
    return reply(self,"跳过「" .. title .. "」：" .. reason,{action="日常缺项"})
end

-- ------------------------------------------------------------
-- 附加阶段（1.15）：全时段签到 → 月卡每日奖励 → 好友点数送满全部好友。
-- 全部是免费操作，互不战斗，放在常规日常前执行；长任务跨时段时
-- 结束前会再次调用本函数补签/补领。签到与日常清单的「签到」共用开关，
-- 好友全送与月卡各有独立开关。
-- ------------------------------------------------------------
function A:daily_checkin()
    local state=self.daily_state
    if not state.checkin_all or state.checkin_stop or not self:daily_enabled(1) then return end
    if require("logic.daily"):AlreadyCheckin() then return end
    if (state.checkin_claims or 0)>=8 or bag_logic:GetSpaceCount()<=1 then
        state.checkin_stop=true
        self:log((state.checkin_claims or 0)>=8 and "自动签到达到安全上限，停止" or "背包空间不足，停止自动签到")
        return
    end
    local result=self:daily_request("take_check_in_reward_ret","take_check_in_reward",{},
        "领取当前时段签到奖励",nil)
    self._task_pending.extra="checkin"
    state.checkin_claims=(state.checkin_claims or 0)+1
    return result
end

function A:daily_extras()
    local state=self.daily_state
    local refining=self:daily_refine();if refining then return refining end
    if not state.defer_tail and state.activity_rewards and not state.activity_done then
        local result=self:daily_activity_rewards()
        if result then return result end
    end
    -- 邮件查询回执是本轮快照。用 ID 去重，避免客户端查询处理器追加重复邮件。
    if not state.defer_tail and state.mail_enabled and not state.mail_done then
        if not state.loaded.mail then
            local ok,mail=pcall(require,"logic.mail")
            if not ok or type(mail)~="table" or not self:install_task_hooks()
                or not self._task_hooks.query_mail_info_ret or not self._task_hooks.open_mail_ret then
                state.mail_done=true;self:log("邮件模块暂不可用，跳过领取")
            else
                local p=self:task_request("query_mail_info_ret","daily_action",function()
                    network:Send({query_mail_info={}})
                end)
                p.extra="mail_query";p.timeout=8;p.description="读取邮件"
                return reply(self,"读取待领取邮件",{wait=true})
            end
        else
            local target
            for _,mail in ipairs(state.mail_queue or {}) do
                if mail.id and not mail.mark_read and not state.mail_attempted[mail.id] then target=mail;break end
            end
            if not target or (state.mail_tries or 0)>=300 then
                state.mail_done=true
                self:log("邮件领取结束：成功" .. state.mail_claimed .. "封，失败" .. state.mail_failures .. "封")
            else
                state.mail_attempted[target.id]=true;state.mail_tries=(state.mail_tries or 0)+1
                local p=self:task_request("open_mail_ret","daily_action",function()
                    network:Send({open_mail={id=target.id}})
                end)
                p.extra="mail";p.mail_id=target.id;p.description="领取邮件" .. tostring(target.id)
                p.accept_response=function(msg)return msg.id==target.id end
                return reply(self,"领取邮件（第" .. state.mail_tries .. "封）",{action="邮件领取"})
            end
        end
    end
    -- 每次一键日常成功完成四次金币召唤；每次回执后重新读取递增金币价格。
    if state.gold_enabled and not state.gold_done then
        local troop=require("logic.troop")
        local cost=tonumber(require("logic.daily"):GetRecruitCost())
        if state.gold_summons>=4 then
            state.gold_done=true
        elseif cost and cost>20000000 then
            state.gold_done=true;self:log("金币召唤单次价格超过20M，停止本轮金币召唤")
        elseif not troop:CheckMercenaryNum() or not cost or cost<0 or not can_pay("gold_coin",cost) then
            state.gold_done=true
            self:log("金币召唤停止：已完成" .. state.gold_summons .. "/4次，金币不足、价格未加载或佣兵背包已满")
        else
            local p=self:task_request("recruit_mercenary_ret","daily_action",function()
                network:Send({recruit_mercenary={door_type="recruiting_door"}})
            end)
            p.extra="gold";p.description="金币召唤"
            p.accept_response=function(msg)return msg.door_type==nil or msg.door_type=="recruiting_door" end
            return reply(self,"金币召唤（" .. (state.gold_summons+1) .. "/4）",{action="金币召唤"})
        end
    end
    -- 活动页每日签到：签到按时段开放（CHECKIN_TIME 两/三个时段），当前时段可签则签
    local checkin=self:daily_checkin()
    if checkin then return checkin end
    -- 主界面月卡：冒险月卡/挖矿月卡每日奖励（reward_mark==buy 且未过期且未领）
    if state.vip_reward and not state.vip_done then
        local ok_vip,vip=pcall(require,"logic.vip")
        if not ok_vip or type(vip)~="table" then
            state.vip_done=true
        elseif not state.loaded.vip then
            local p=self:task_request("query_vip_ret","daily_action",function()
                network:Send({query_vip={}})
            end)
            p.extra="vip_query";p.description="读取月卡状态";p.timeout=8
            return reply(self,"读取月卡状态",{wait=true})
        else
            local now=time_logic:Now()
            local bought=constants.VIP_STATE and constants.VIP_STATE.buy
            local taken=constants.VIP_STATE and constants.VIP_STATE.daily_reward
            local claimed=false
            for vtype,names in pairs({[1]="冒险月卡",[2]="挖矿月卡"}) do
                local info=(vip.vip_list or {})[vtype]
                local mark=info and tonumber(info.reward_mark) or 0
                if bought and taken and info and mark==bought
                    and (tonumber(info.end_time) or 0)>now then
                    claimed=true
                    local p=self:task_request("take_vip_reward_ret","daily_action",function()
                        network:Send({take_vip_reward={vip_type=vtype}})
                    end)
                    p.extra="vip";p.description="领取" .. names .. "每日奖励"
                    return reply(self,p.description,{action="月卡领取"})
                end
            end
            if not claimed then
                state.vip_done=true
                self:log("月卡每日奖励：无待领取（未购买、已领或已过期）")
            end
        end
    end
    -- 好友点数：循环赠送直到无可赠送好友（原逻辑只送第一位）
    if state.gift_all and not state.gifts_done then
        local social=require("logic.social")
        if not state.loaded.social then return self:daily_query("query_social_info_ret","query_social_info","social") end
        local targets=social:GetUnsendGiftList() or {}
        if #targets==0 then
            if (state.gifts_sent or 0)>0 then
                self:log("好友点数已赠送全部好友，共" .. state.gifts_sent .. "位")
            end
            state.gifts_done=true
        elseif (state.gifts_sent or 0)>=300 then
            self:log("好友赠送达到安全上限（300位），剩余好友下次日常再送")
            state.gifts_done=true
        else
            local p=self:daily_request("send_gift_ret","send_gift",{user_id=targets[1]},
                "赠送好友点数（第" .. ((state.gifts_sent or 0)+1) .. "位，今日还可送" .. #targets .. "位）",nil)
            self._task_pending.extra="gift"
            state.gifts_sent=(state.gifts_sent or 0)+1
            return p
        end
    end
    return nil
end

function A:daily_mining_data()
    local state=self.daily_state
    if not state.loaded.mining then return self:daily_query("query_mining_info_ret","query_mining_info","mining") end
    if not state.loaded.refill_bag and self:earth_inventory_unknown() then
        return self:daily_query("query_bag_info_ret","query_bag_info","refill_bag")
    end
end

function A:daily_add_tools(needed,id)
    local have=tonumber(mining.dig_count) or 0
    if have>=needed then return nil end
    local cap=tonumber(mining.dig_max_count) or 0
    if cap>0 and needed>cap then return self:daily_skip(id,"所需矿镐超过当前上限") end
    self:start_refill("daily",needed,id)
    return reply(self,"按计划补齐矿镐，大地之心优先",{wait=true,action="补充矿镐"})
end

function A:daily_buy_cave(kind,cave_name)
    local state=self.daily_state
    state.cave_buy_stops=state.cave_buy_stops or {}
    if state.cave_buy_stops[kind] then return nil end
    local maximum=tonumber((constants.CAVE_DAILY_BUY_CHALLENGE_NUM or {})[kind])
    local left=tonumber((mining.cave_buy_challenge_nums or {})[kind]) or maximum or 0
    local bought=(state.cave_bought or {})[kind] or 0
    local remaining=math.min(left,(maximum or left)-bought)
    if remaining<=0 then return nil end
    local prices=(constants.CAVE_DAILY_BUY_PRICE or {})[kind] or {}
    local price=maximum and tonumber(prices[maximum-remaining+1])
    local reason
    if not price or price<=0 then reason="价格未加载"
    else
        local allowed,why=self:spend_can_afford(price)
        if not allowed then reason=why
        elseif not resource_logic:CheckResourceNum(constants.RESOURCE_TYPE.blood_diamond,price,false) then
            reason="血钻不足"
        end
    end
    if reason then
        state.cave_buy_stops[kind]=true
        self:log(cave_name .. "（副本" .. kind .. "）停止购买：" .. reason .. "，继续已有次数及其他副本")
        return nil
    end
    state.buy_cave_challenge=true;state.cave_last_send_kind=kind
    state.cave_buy_attempts=(state.cave_buy_attempts or 0)+1
    state.cave_bought=state.cave_bought or {};state.cave_bought[kind]=bought+1
    local result=self:daily_request("buy_cave_challenge_ret","buy_cave_challenge",{cave_type=kind},
        "购买" .. cave_name .. "额外次数（" .. price .. "血钻，该副本还可买" .. remaining .. "次）",nil)
    self._task_pending.cave_buy_kind=kind
    self._task_pending.cave_price=price
    self._task_pending.cave_buy_left=left
    self._task_pending.cave_buy_before=tonumber((mining.cave_challenge_nums or {})[kind]) or 0
    self._task_pending.accept_response=function(msg)return msg.cave_type==nil or tonumber(msg.cave_type)==kind end
    return result
end

local function daily_workshop_ready()
    local project=(mining.project_list or {})[1]
    return project and (tonumber(project.endtime) or math.huge)<=time_logic:Now()
end

function A:daily_action(id,conf,item)
    local state=self.daily_state
    local daily=require("logic.daily")
    local troop=require("logic.troop")
    if id==1 then
        if daily:AlreadyCheckin() then return self:daily_skip(id,"当前时段已签到，等待下一时段") end
        if bag_logic:GetSpaceCount()<=1 then return self:daily_skip(id,"背包空间不足") end
        return self:daily_request("take_check_in_reward_ret","take_check_in_reward",{},"执行今日签到",id)
    elseif id==3 then
        local social=require("logic.social")
        if not state.loaded.social then return self:daily_query("query_social_info_ret","query_social_info","social") end
        local target=(social:GetUnsendGiftList() or {})[1]
        if not target then return self:daily_skip(id,"可赠送的好友不足，不自动添加好友") end
        return self:daily_request("send_gift_ret","send_gift",{user_id=target},"完成1次好友赠送",id)
    elseif id==8 then
        if not troop:CheckMercenaryNum() then return self:daily_skip(id,"佣兵背包已满") end
        local friendship=tonumber((constants.RECRUIT_COST or {}).friendship_door)
        local gold=tonumber(daily:GetRecruitCost())
        local door
        if friendship and can_pay("friendship_pt",friendship) then door="friendship_door"
        elseif not state.gold_enabled and gold and gold>=0 and gold<=20000000 and can_pay("gold_coin",gold) then door="recruiting_door" end
        if not door then return self:daily_skip(id,"友情点和金币不足，不使用血钻召唤") end
        return self:daily_request("recruit_mercenary_ret","recruit_mercenary",{door_type=door},"完成1次" .. (door=="friendship_door" and "友情召唤" or "金币召唤"),id)
    elseif id==15 then
        local choices={}
        for _,merc in pairs(troop.mercenary_list or {}) do
            local cap=tonumber(constants.MAX_LEVEL) or 100
            if merc.template_info and (tonumber(merc.wakeup) or 1)<(tonumber(merc.template_info.max_wakeup) or 1) then
                cap=math.min(cap,tonumber(constants.CAN_WAKEUP_LEVEL) or 30)
            end
            local level=tonumber(merc.level) or cap
            local row=(config_manager.mercenary_exp_config or {})[level]
            local cost=row and tonumber(row["wakeup_factor" .. (merc.wakeup or 1)])
            cost=cost and math.max(0,cost-(tonumber(merc.exp) or 0))
            if level<cap and cost and can_pay("exp",cost) then choices[#choices+1]={merc=merc,cost=cost} end
        end
        table.sort(choices,function(a,b) return a.cost<b.cost end)
        if not choices[1] then return self:daily_skip(id,"没有可升级佣兵或经验不足") end
        local merc=choices[1].merc
        return self:daily_request("alloc_mercenary_exp_ret","alloc_mercenary_exp",{mercenary_id=merc.instance_id,target_level=merc.level+1},
            "佣兵升级1级：" .. tostring(merc.template_info.name),id)
    elseif id==4 then
        local choices={}
        if troop.forge_weapon_inflight then return self:daily_skip(id,"已有武器强化动画待结束，请完成后重试") end
        for _,merc in pairs(troop.mercenary_list or {}) do
            local level=tonumber(merc.weapon_lv) or 0
            if not merc.is_leader and level<(tonumber(constants.MAX_WEAPON_LV) or 30)
                and not (merc.template_info and merc.template_info.have_artifact and level>=(tonumber(constants.CAN_OPEN_ARTIFACT_WEAPON_LV) or 20) and not merc.is_open_artifact)
                and (config_manager.weapon_forge_config or {})[level+1]
                and troop:CheckForgeWeaponResource(level,false) then choices[#choices+1]=merc end
        end
        table.sort(choices,function(a,b)return (a.weapon_lv or 0)<(b.weapon_lv or 0) end)
        if not choices[1] then return self:daily_skip(id,"强化材料不足或没有可强化佣兵") end
        local merc=choices[1]
        return self:daily_request("forge_mercenary_weapon_ret","forge_mercenary_weapon",{mercenary_id=merc.instance_id,extra_chance=0},
            "佣兵强化1次，不增加血钻成功率：" .. tostring(merc.template_info.name),id)
    elseif id==2 then
        if not state.loaded.mining then return self:daily_query("query_mining_info_ret","query_mining_info","mining") end
        local count=#(mining.project_list or {})
        if daily_workshop_ready() then
            local result=self:daily_request("get_mining_project_reward_ret","get_mining_project_reward",{},"收取已完成工坊工程",nil)
            self._task_pending.workshop_count=count
            return result
        end
        if count>=(tonumber(mining.max_project_count) or 0) then
            return self:daily_skip(id,"工坊槽位已满，不使用血钻解锁或加速")
        end
        local empty=(tonumber(mining.max_project_count) or 0)-count
        local chosen,code,detail=self:workshop_production_project(empty)
        if not chosen then return self:workshop_block_reply(code,detail,id) end
        local loading=self:daily_mining_data();if loading then return loading end
        local chosen_dig=tonumber(chosen.dig_count) or 0
        local tool=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.ultimate_tool
        local bags=tool and tonumber(resource_logic:GetResourceNum(tool)) or 0
        if not self:has_earth_heart() and (tonumber(mining.dig_count) or 0)+math.max(0,bags)*5<chosen_dig then
            return self:daily_skip(id,"现有矿镐和矿工包不足以达到设定工坊档位，跳过生产")
        end
        local needed=chosen_dig*empty
        local cap=tonumber(mining.dig_max_count) or 0
        if cap>0 then needed=math.min(needed,cap) end
        if not self:has_earth_heart() then needed=math.max(chosen_dig,math.min(needed,(tonumber(mining.dig_count) or 0)+math.max(0,bags)*5)) end
        local tools=self:daily_add_tools(needed,id);if tools then return tools end
        if (state.attempts[id] or 0)>=32 then return self:daily_skip(id,"工坊生产达到安全上限") end
        local result=self:daily_request("add_mining_project_ret","add_mining_project",{id=chosen.ID or chosen.id},
            "开始工坊生产：" .. tostring(chosen.name) .. "（" .. tostring(chosen.need_layer) .. "层/" ..
            tostring(chosen.need_time) .. "分钟，与生产工坊档位一致）",id)
        self._task_pending.workshop_count=count
        return result
    elseif id==6 then
        local ladder=require("logic.ladder")
        if not state.loaded.ladder then return self:daily_query("query_ladder_info_ret","query_ladder_info","ladder") end
        if (tonumber(ladder.challenge_num) or 0)<=0 then return self:daily_skip(id,"排位免费次数不足，不购买次数") end
        local cd=(tonumber(ladder.challenge_cd) or 0)-time_logic:Now()
        if cd>0 then return reply(self,"等待排位冷却：" .. math.ceil(cd) .. "秒",{wait=true}) end
        local best
        for _,rival in pairs(ladder.rival_list or {}) do
            if rival.rank and (not best or (tonumber(rival.battle_point) or 0)<(tonumber(best.battle_point) or 0)) then best=rival end
        end
        if not best then return self:daily_skip(id,"没有可挑战的排位对手") end
        return self:daily_request("challenge_ladder_ret","challenge_ladder",{rank=best.rank},"完成1次免费排位挑战",id)
    elseif id==5 then
        local missing=math.max(1,(conf.condition_count or 9)-(item.completion_count or 0))
        self.arena_state={target=missing,count=0,queried=false,attempted={},refreshes=0,cost=0,freeOnly=true}
        state.child_arena=true
        return reply(self,"开始日常竞技场，剩余" .. missing .. "场；仅使用免费次数与免费刷新",{action="日常竞技场"})
    elseif id==9 or id==10 then
        local cave_name=id==9 and "巨魔巢穴" or "地下探险"
        state.cave_skips=state.cave_skips or {};state.cave_fights=state.cave_fights or {}
        local loading=self:daily_mining_data();if loading then return loading end
        if not state.loaded.caves then return self:daily_query("query_cave_event_config_info_ret","query_cave_event_config_info","caves") end
        -- 副本开放表 cave_config_info 由游戏按 time_logic:GetDateInfo(Now).wday 填写
        -- （os.date 的 wday：1=周日 … 7=周六）。用同一把钥取当天开放的副本，
        -- 不能用攻略用的 getWDay（1=周一），否则会错位一天、当天一个副本都取不到。
        local kinds=self:daily_cave_kinds(id)
        if not state.cave_buy_stop then
            for _,kind in ipairs(kinds) do
                local purchase=self:daily_buy_cave(kind,cave_name)
                if purchase then return purchase end
            end
        end
        local choices={}
        local target_level=id==9 and math.floor(tonumber(self.cfg.dailyTrollLevel) or 0)
            or math.floor(tonumber(self.cfg.dailyUndergroundLevel) or 0)
        for _,kind in ipairs(kinds) do
            if (state.cave_fights[kind] or 0)>=40 then state.cave_skips[kind]="该项目挑战已达40次，停止异常循环" end
            if not state.cave_skips[kind] and (tonumber((mining.cave_challenge_nums or {})[kind]) or 0)>0 then
                local reached=tonumber((mining.cave_levels or {})[kind]) or 0
                if target_level>0 then reached=math.min(reached,target_level) end
                local ceiling=(state.cave_level_ceiling or {})[kind]
                if ceiling then reached=math.min(reached,ceiling) end
                for level,event in pairs((config_manager.mining_event_config or {})[kind] or {}) do
                    if (tonumber(level) or 0)<=reached
                        and (tonumber(event.bp_limit) or 0)<=troop:GetTroopBP() then choices[#choices+1]=event end
                end
            end
        end
        -- 巨魔巢穴与地下探险按设定档位（0=自动最高；>0 时按上限自动降档匹配已解锁且战力达标最高档）：
        -- 等级最高优先，其次副本编号最高（1.16 起地下探险不再选最便宜的一档）。
        table.sort(choices,function(a,b)
            local la,lb=tonumber(a.level) or 0,tonumber(b.level) or 0
            if la~=lb then return la>lb end
            return (tonumber(a.cave_type) or 0)>(tonumber(b.cave_type) or 0)
        end)
        if not choices[1] then
            -- 当日开放的副本挑战次数全部用完时，按完成处理而不是缺项
            local all_spent=true
            for _,kind in ipairs(kinds) do
                if (tonumber((mining.cave_challenge_nums or {})[kind]) or 0)>0 then all_spent=false end
            end
            if all_spent then return self:daily_skip(id,"当日" .. cave_name .. "挑战次数已全部用完") end
            return self:daily_skip(id,"今日副本未开放、免费次数不足或战力未达标")
        end
        local event=choices[1]
        -- 预先统计剩余副本的最高难度矿镐需求，按容量分批补齐。
        local needed,seen=0,{}
        for _,choice in ipairs(choices) do
            if not seen[choice.cave_type] then
                seen[choice.cave_type]=true
                needed=needed+(tonumber(choice.pickaxe_count) or 0)*
                    (tonumber((mining.cave_challenge_nums or {})[choice.cave_type]) or 0)
            end
        end
        local cap=tonumber(mining.dig_max_count) or 0
        if cap>0 then needed=math.min(needed,cap) end
        local tool=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.ultimate_tool
        local available=tool and tonumber(resource_logic:GetResourceNum(tool)) or 0
        -- 总库存不够整个阶段时，仍先打完现有资源能负担的场次。
        needed=math.min(needed,(tonumber(mining.dig_count) or 0)+math.max(0,available)*5)
        needed=math.max(needed,tonumber(event.pickaxe_count) or 0)
        local tools=self:daily_add_tools(needed,id);if tools then return tools end
        state.cave_last_send_kind=event.cave_type
        local desc=target_level>0
            and ("执行" .. cave_name .. "（目标Lv" .. target_level .. "）：" .. tostring(event.name) .. " Lv" .. tostring(event.level))
            or ("执行" .. cave_name .. "最高难度：" .. tostring(event.name) .. " Lv" .. tostring(event.level))
        state.cave_fights[event.cave_type]=(state.cave_fights[event.cave_type] or 0)+1
        local result=self:daily_request("solve_cave_event_ret","solve_cave_event",{cave_type=event.cave_type,cave_lv=event.level},desc,id)
        local p=self._task_pending;p.cave_kind=event.cave_type
        p.cave_before=tonumber((mining.cave_challenge_nums or {})[event.cave_type]) or 0
        p.cave_level=event.level
        p.accept_response=function(msg)return (msg.cave_type==nil or tonumber(msg.cave_type)==event.cave_type) and (msg.cave_lv==nil or tonumber(msg.cave_lv)==event.level) end
        return result
    elseif id==12 then
        local loading=self:daily_mining_data();if loading then return loading end
        if (tonumber(mining.cave_boss_lv) or 0)==0 then
            if not can_pay("demon_medal",tonumber(constants.OPEN_CAVE_BOSS_DEMON_MEDAL) or math.huge) then
                return self:daily_skip(id,"首领试炼未解锁且恶魔勋章不足")
            end
            return self:daily_request("unlock_cave_boss_ret","unlock_cave_boss",{},"使用恶魔勋章解锁首领试炼",nil)
        end
        if not can_pay("demon_medal",tonumber(constants.CAVE_BOSS_CHALLANGE_SUB) or math.huge) then
            return self:daily_skip(id,"首领试炼所需恶魔勋章不足")
        end
        if bag_logic:GetSpaceCount()<(tonumber(constants.EVENT_MAX_BAG_SPACE_COUNT) or 5) then return self:daily_skip(id,"背包空间不足") end
        return self:daily_request("challenge_cave_boss_ret","challenge_cave_boss",{},"执行1次首领试炼",id)
    elseif id==13 then
        local rune=require("logic.rune")
        if not state.loaded.runes then return self:daily_query("query_rune_info_ret","query_rune_info","runes") end
        if state.manual_runes then return self:daily_manual_rune(rune) end
        local target,material
        for _,info in ipairs(rune.rune_list or {}) do
            local config=info.template_info or (config_manager.rune_config or {})[info.template_id]
            if config then
                if tonumber(config.type)==1 and (tonumber(info.equip_pos) or 0)==0 then
                    if not material or (tonumber(config.out_exp) or 0)<(tonumber(material.template_info.out_exp) or 0) then material=info end
                elseif (tonumber(info.level) or 1)<(tonumber(config.level_limit) or 1) then
                    if not target or (tonumber(config.quality) or 0)>(tonumber(target.template_info.quality) or 0) then target=info end
                end
            end
        end
        if not target or not material then return self:daily_skip(id,"缺少可强化符文或专用经验符文；保留其他符文") end
        return self:daily_request("upgrade_rune_ret","upgrade_rune",{rune_id=target.rune_id,exp_rune_id_list={material.rune_id}},"消耗1枚专用经验符文，强化符文1次",id)
    elseif id==7 or id==14 then
        return self:daily_cart_step(id)
    elseif id==11 then
        if self.cfg.mobileNoMining then return self:daily_skip(id,"手机版未包含自动挖矿") end
    end
    return self:daily_skip(id,"当前客户端未识别的任务类型")
end

-- 收尾链（1.17 抽取，1.18 扩展）：合战/宿命远征 → 虚空兑换 → 矿山采集 → 结束。
-- 工会首领阶段完成后也从这里继续，保证后续阶段顺序不变。
function A:daily_finish_chain(state,summary,partial)
    if state.events_summary and not summary:find(state.events_summary,1,true) then summary=summary .. "；" .. state.events_summary end
    local spent=math.max(0,self:spend_total()-(state.spend_started or self:spend_total()))
    summary=summary:gsub("血钻消耗%d+","血钻消耗" .. spent,1)
    if self.fast_refill then
        local r=self:refill_step()
        if not r.refill_done then return r end
        if r.error then return self:daily_skip(r.refill_id or 2,r.error) end
    end
    local refine_wait=self:daily_refine_pending();if refine_wait then return refine_wait end
    if self.daily_state==state then local refill=self:daily_refine();if refill then return refill end end
    if state.events_enabled and not state.events_ran then
        state.events_ran=true
        state.summary=summary;state.partial=partial
        self:start_daily_events()
        state.child_events=true
        return reply(self,summary .. "；开始合战与宿命远征阶段",{action="活动阶段",phase="events"})
    end
    if state.mine_guards and not state.guard_ran then
        state.guard_ran=true;state.guard_active=true
        return self:daily_guard_step(state)
    end
    if state.void_challenge and not state.void_challenge_ran then
        state.void_challenge_ran=true;state.summary=summary;state.partial=partial
        self.daily_state=nil;self:start_void();self.daily_state=state
        state.child_void_challenge=true
        return reply(self,summary.."；开始虚空裂隙挑战",{phase="void_challenge",action="虚空挑战阶段"})
    end
    if state.void_exchange and not state.void_ran then
        state.void_ran=true
        state.summary=summary;state.partial=partial
        self.daily_state=nil
        self:start_void_exchange()
        self.daily_state=state
        state.child_void=true
        return reply(self,summary .. "；开始虚空商店兑换",{action="虚空兑换阶段",phase="void"})
    end
    if state.include_mine then
        self:log(summary)
        state.summary=summary;state.partial=partial
        self:start_mine_collect(state)
        state.child_mine=true
        return reply(self,"日常委托已处理，开始矿山采集与研究",{action="矿山阶段",phase="mine"})
    end
    state.running=false
    return reply(self,summary,{done=true,partial=partial})
end

function A:daily_step()
    local state=self.daily_state
    if not state or not state.running then return reply(self,"日常任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏",{wait=true}) end
    if state.guard_active then return self:daily_guard_step(state) end
    if self._task_pending and self._task_pending.kind=="growth_rune_loadouts" and state.rune_selection then
        return self:daily_manual_rune(require("logic.rune"))
    end
    if self.fast_refill then
        local r=self:refill_step()
        if not r.refill_done then return r end
        if r.error then return self:daily_skip(r.refill_id or 2,r.error) end
    end
    local refine_wait=self:daily_refine_pending();if refine_wait then return refine_wait end
    if not state.start_with_checkin and state.events_enabled and not state.events_ran then
        state.events_ran=true;state.events_first=true;state.child_events=true
        self:start_daily_events()
        return reply(self,"一键日常第一步：合战买次数、推进新关、兑换赛点，再执行宿命远征",{phase="events",action="活动阶段"})
    end
    if state.child_guild then
        -- 工会首领阶段（1.17）：许可证买完后转入 guild_boss_step 挑战
        if self.guild_ticket_state then
            local ok_ticket,ticket=pcall(function()return self:daily_guild_ticket_step()end)
            if not ok_ticket then
                self.guild_ticket_state=nil;self._task_pending=nil
                self:log("工会首领许可证阶段出错：" .. tostring(ticket))
            elseif not ticket.done then
                ticket.phase="guild"
                return reply(self,ticket.message,ticket)
            else
                self.guild_ticket_state=nil
            end
        end
        if not self.guild_state then
            local fights=state.guild_boss_fights or 1
            self.daily_state=nil
            self:start_guild_boss(fights)
            self.daily_state=state
            return reply(self,"工会首领：开始挑战" .. fights .. "次",{action="工会首领挑战",phase="guild"})
        end
        local ok_fight,fight=pcall(function()return self:guild_boss_step()end)
        if not ok_fight then
            state.child_guild=false;self.guild_state=nil;self._task_pending=nil
            self:log("工会首领挑战出错：" .. tostring(fight))
            return self:daily_finish_chain(state,(state.summary or "日常结束") .. "；工会首领阶段出错",true)
        end
        if fight.done then
            local fought=self.guild_state and (tonumber(self.guild_state.count) or 0)>=1
            if fought then
                state.guild_boss_done_record=self:mark_guild_boss_done_today()
            end
            state.report_notes=state.report_notes or {};state.report_notes.guild_boss=fight.message
            state.child_guild=false;self.guild_state=nil;self._task_pending=nil
            local message=(state.summary or "日常结束") .. "；工会首领：" .. fight.message
            local next_res=self:daily_finish_chain(state,message,state.partial or fight.partial)
            if type(next_res)=="table" and state.guild_boss_done_record then
                next_res.guildBossDone=state.guild_boss_done_record
            end
            return next_res
        end
        fight.phase="guild"
        return reply(self,fight.message,fight)
    end
    if state.child_events then
        -- 合战 / 宿命远征阶段（1.18）：活动未开启时自动跳过，异常不会中断后续日常
        local ok_events,events=pcall(function()return self:daily_events_step()end)
        if not ok_events then
            state.child_events=false;state.allow_event_purchase=false
            self.event_activity_state=nil;self._task_pending=nil
            self:log("合战与宿命远征阶段出错：" .. tostring(events))
            if state.events_first then state.events_first=false;return reply(self,"活动阶段异常，继续常规日常",{action="日常活动结算"}) end
            return self:daily_finish_chain(state,(state.summary or "日常结束") .. "；合战与宿命远征阶段出错",true)
        end
        if events.done then
            state.report_notes=state.report_notes or {};state.report_notes.events_enabled=events.message
            state.child_events=false;state.allow_event_purchase=false
            self.event_activity_state=nil;self._task_pending=nil
            if state.events_first then
                state.events_first=false;state.events_summary=events.message
                return reply(self,events.message .. "；开始常规日常",{action="日常活动结算"})
            end
            local message=(state.summary or "日常结束") .. "；" .. events.message
            return self:daily_finish_chain(state,message,state.partial or events.partial)
        end
        events.phase="events"
        return reply(self,events.message,events)
    end
    if state.child_void_challenge then
        local ok,result=pcall(function()return self:void_step()end)
        if not ok then result={done=true,error=true,message="虚空挑战暂不可用："..tostring(result)} end
        if result.done then
            state.report_notes=state.report_notes or {};state.report_notes.void_challenge=result.message
            state.child_void_challenge=false;self.void_state=nil
            local message=(state.summary or "日常结束").."；虚空挑战："..tostring(result.message or "已结束")
            if result.error and self._task_pending and not self._task_pending.response then
                state.running=false
                return reply(self,message,{done=true,error=true,partial=true})
            end
            return self:daily_finish_chain(state,message,state.partial or result.partial or result.error)
        end
        result.phase="void_challenge";return reply(self,"虚空挑战："..tostring(result.message or "进行中"),result)
    end
    if state.child_mine then
        local result=self:mine_step()
        if result.done then
            state.report_notes=state.report_notes or {};state.report_notes.include_mine=result.message
            state.running=false
            result.message=(state.summary or "日常结束").."；"..result.message
            result.partial=result.partial or state.partial
        else result.message="矿山阶段："..result.message end
        result.phase="mine"
        return reply(self,result.message,result)
    end
    if state.child_void then
        local ok_void,result=pcall(function()return self:void_exchange_step()end)
        if not ok_void then
            state.child_void=false
            self.void_exchange_state=nil
            self._task_pending=nil
            local message=(state.summary or "日常结束") .. "；虚空兑换暂不可用：" .. tostring(result)
            self:log(message)
            if state.include_mine then
                self:start_mine_collect(state)
                state.child_mine=true
                return reply(self,message .. "；开始矿山采集与研究",{action="矿山阶段",phase="mine"})
            end
            state.running=false
            return reply(self,message,{done=true,partial=true})
        end
        if result.done then
            state.child_void=false
            state.report_notes=state.report_notes or {};state.report_notes.void_exchange=result.message
            self.void_exchange_state=nil
            local message=(state.summary or "日常结束").."；虚空兑换："..result.message
            if state.include_mine then
                self:log(message)
                self:start_mine_collect(state)
                state.child_mine=true
                return reply(self,message.."；开始矿山采集与研究",{action="矿山阶段",phase="mine"})
            end
            state.running=false
            return reply(self,message,{done=true,partial=result.partial or state.partial})
        end
        result.message="虚空兑换："..result.message
        result.phase="void"
        return reply(self,result.message,result)
    end
    local day=math.floor((time_logic:Now()+(tonumber(time_logic.time_zone_offset) or 28800))/86400)
    if not state.day then state.day=day end
    if day~=state.day then
        state.running=false;return reply(self,"游戏日期已变化，请重新启动日常以更新任务",{done=true,error=true})
    end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待日常战斗结算",{wait=true}) end
    self:hide_panel("reward_panel")
    local daily=require("logic.daily")
    if state.child_arena then
        local result=self:arena_step()
        if result.done or result.waitFree then
            state.child_arena=false;self.arena_state=nil;self._task_pending=nil
            if result.waitFree then state.skipped[5]="免费对手已处理，等待下一轮免费刷新" end
            state.needs_sync=true
            return reply(self,"日常竞技场阶段结束：" .. result.message,{action="日常竞技场结算"})
        end
        return result
    end
    local pending=self._task_pending
    if pending and pending.kind=="daily_cart" then return self:daily_cart_step(pending.cart_id) end
    if pending then
        if not pending.response then
            local timeout=tonumber(pending.timeout) or 20
            if task_now(self)-pending.sent_at<=timeout then return reply(self,"等待日常服务器响应",{wait=true}) end
            self._task_pending=nil
            if pending.extra then
                -- 附加阶段超时：只停该阶段，不影响整体日常
                if pending.extra:sub(1,9)=="activity_" then
                    state.activity_done=true
                elseif pending.extra=="mail_query" or pending.extra=="mail" then
                    state.mail_done=true
                elseif pending.extra=="gold" then
                    state.gold_done=true
                elseif pending.extra=="vip_query" then
                    state.vip_done=true
                    self:log("读取月卡状态超时，跳过月卡领取")
                else
                    state.gifts_done=true;state.checkin_stop=true;state.vip_done=true
                    self:log("日常附加操作超时，已跳过该阶段")
                end
                return reply(self,"日常附加操作超时，已跳过该阶段",{action="日常附加超时"})
            end
            state.running=false
            return reply(self,"日常请求超时，已停止，防止重复消耗资源",{done=true,error=true})
        end
        self._task_pending=nil
        local result=pending.response.result
        if pending.flag then state.loaded[pending.flag]=true end
        if pending.extra then
            if pending.extra:sub(1,9)=="activity_" then
                self:daily_activity_response(pending,result)
            elseif pending.extra=="mail_query" then
                state.loaded.mail=true
                if result and result~="success" then state.mail_done=true
                else
                    state.mail_queue=pending.response.mail_list or {}
                    -- 原客户端查询会直接追加列表；去重防止邮件界面显示重复行。
                    local mail=require("logic.mail")
                    for _,key in ipairs({"not_read_list","already_read_list","system_mail_not_read_list",
                        "system_mail_already_read_list","friend_mail_not_read_list","friend_mail_already_read_list"}) do
                        local list=mail[key]
                        if type(list)=="table" then
                            local seen={}
                            for i=#list,1,-1 do
                                local id=list[i].id
                                if id and seen[id] then table.remove(list,i)
                                elseif id then seen[id]=true end
                            end
                        end
                    end
                end
            elseif pending.extra=="mail" then
                if result=="success" then state.mail_claimed=state.mail_claimed+1
                else
                    state.mail_failures=state.mail_failures+1
                    self:log("邮件" .. tostring(pending.mail_id) .. "领取失败：" .. tostring(result) .. "，继续下一封")
                end
            elseif pending.extra=="gold" then
                if result=="success" then state.gold_summons=state.gold_summons+1;state.needs_sync=true
                else state.gold_done=true;self:log("金币召唤失败：" .. tostring(result) .. "，停止本轮召唤") end
            elseif pending.extra=="vip_query" then
                state.loaded.vip=true
                if result and result~="success" then state.vip_done=true end
            elseif result~="success" then
                if pending.extra=="gift" then
                    state.gifts_done=true
                    self:log("好友赠送失败：" .. tostring(result) .. "，停止赠送")
                elseif pending.extra=="checkin" then
                    state.checkin_stop=true
                    self:log("签到失败：" .. tostring(result))
                elseif pending.extra=="vip" then
                    state.vip_done=true
                    self:log("月卡领取失败：" .. tostring(result))
                end
            elseif pending.extra~="gift" then
                -- 签到/月卡成功后刷新标记，避免重复领取
                state.needs_sync=true
            end
        end
        local preparations={get_mining_project_reward_ret=2,unlock_cave_boss_ret=12,
            escort_refresh_tramcar_ret=7,escort_receive_reward_ret=7}
        if result and result~="success" and preparations[pending.event] then
            state.skipped[preparations[pending.event]]="准备操作失败：" .. tostring(result)
        end
        if pending.workshop_count and result=="success" then
            local count=#(mining.project_list or {})
            if pending.event=="get_mining_project_reward_ret" and count<pending.workshop_count then
                state.workshop_collected=(state.workshop_collected or 0)+1
            elseif pending.event=="add_mining_project_ret" and count>pending.workshop_count then
                state.workshop_produced=(state.workshop_produced or 0)+1
            else state.skipped[2]="工坊槽位未更新，已停止重复操作" end
        end
        if pending.event=="take_daily_alchemy_ret" then
            state.allow_paid_alchemy=false
            if result=="success" and (pending.alchemy_cost or 0)>0 then
                self:spend_record(pending.alchemy_cost,"每日炼金")
            end
        end
        if pending.event=="use_mining_tool_ret" then
            state.loaded.mining=false
            if result=="success" then state.refill_check={id=pending.refill_id,before=pending.refill_before}
            elseif pending.refill_id then state.skipped[pending.refill_id]="矿工包使用失败：" .. tostring(result) end
        end
        if pending.event=="buy_cave_challenge_ret" then
            state.loaded.mining=false
            if result~="success" then
                state.cave_buy_stops=state.cave_buy_stops or {}
                if pending.cave_buy_kind then state.cave_buy_stops[pending.cave_buy_kind]=true end
                self:log("购买副本" .. tostring(pending.cave_buy_kind) .. "额外次数失败：" .. tostring(result) .. "，继续其他副本和已有次数")
            end
        end
        if pending.event=="solve_cave_event_ret" and pending.cave_kind then
            state.loaded.mining=false
            if result=="success" or result=="failure" or result=="battle_failure" then
                local defeated=pending.response.is_winner==false or result=="battle_failure"
                if defeated then
                    state.cave_level_ceiling=state.cave_level_ceiling or {}
                    state.cave_level_ceiling[pending.cave_kind]=math.max(0,pending.cave_level-1)
                    self:log("副本"..pending.cave_kind.."当前难度未通关，尝试较低档位，继续其他开放项目")
                end
                state.cave_check={kind=pending.cave_kind,before=pending.cave_before,level=pending.cave_level,
                    lowered=defeated and pending.cave_level>1,winner=pending.response.is_winner,result=result}
            else
                state.cave_skips=state.cave_skips or {};state.cave_skips[pending.cave_kind]="游戏返回："..tostring(result)
                self:log("副本"..pending.cave_kind.."暂不可挑战，继续其他开放项目："..tostring(result))
            end
        end
        if pending.event=="query_mining_info_ret" and state.cave_check then
            local check=state.cave_check;state.cave_check=nil
            local left=tonumber((mining.cave_challenge_nums or {})[check.kind]) or 0
            if left>=check.before and not check.lowered then
                state.cave_skips=state.cave_skips or {}
                state.cave_skips[check.kind]="本次未消耗挑战次数，停止重复挑战当前难度"
                self:log("副本"..check.kind.."挑战后次数未减少，继续其他开放项目")
            end
        end
        if pending.event=="query_mining_info_ret" and state.refill_check then
            local check=state.refill_check;state.refill_check=nil
            if check.id and (tonumber(mining.dig_count) or 0)<=(check.before or 0) then
                state.skipped[check.id]="使用矿工包后矿镐未增加，可能达到上限，已停止重复使用"
            end
        end
        if pending.event=="query_liveness_info_ret" then
            if state.last_action then
                local last=state.last_action
                local item=daily_item(daily,last.id)
                -- 副本任务活跃度已满时（1.17），继续执行是为了买满打完次数，
                -- 进度由 cave_challenge_nums 驱动，不再按活跃度停滞计数。
                if last.id==2 or last.id==9 or last.id==10 then
                    state.stalls[last.id]=0
                elseif item and (tonumber(item.completion_count) or 0)<=last.before then
                    state.stalls[last.id]=(state.stalls[last.id] or 0)+1
                else state.stalls[last.id]=0 end
                if (state.stalls[last.id] or 0)>=3 then state.skipped[last.id]="连续操作后服务器进度未增加，已停止重复尝试" end
                state.last_action=nil
            end
        elseif pending.task_id then
            state.actions=state.actions+1
            state.last_action={id=pending.task_id,before=pending.before}
            state.needs_sync=true
            if not pending.cave_kind and result and result~="success" and result~="failure" and result~="battle_failure" then
                state.skipped[pending.task_id]="游戏返回：" .. tostring(result)
            end
        elseif pending.event=="get_liveness_reward_ret" or pending.event=="get_activity_reward_ret" then
            if result~="success" then state.running=false;return reply(self,"日常领奖失败：" .. tostring(result),{done=true,error=true}) end
            state.claims=state.claims+1
        end
        if result and result~="success" and not pending.task_id and pending.flag then
            state.running=false
            return reply(self,"日常数据读取失败：" .. tostring(result),{done=true,error=true})
        end
        return reply(self,(pending.description or pending.event) .. "：" .. tostring(result or "已返回"),{action="日常响应"})
    end
    if not state.loaded.daily then return self:daily_query("query_daily_ret","query_daily","daily") end
    if state.start_with_checkin then
        local checkin=self:daily_checkin()
        if checkin then return checkin end
        state.start_with_checkin=false
        return reply(self,"当前签到时段已核对，继续一键日常",{action="自动签到"})
    end
    if not state.loaded.liveness or state.needs_sync then
        state.needs_sync=false
        return self:daily_query("query_liveness_info_ret","query_liveness_info","liveness")
    end
    -- 附加阶段（1.15）：好友点数全送 → 全时段签到 → 月卡每日奖励。
    -- 与 liveness 签到（dailyTask1）独立：这里按 dailyCheckinAll 执行，
    -- 长任务跨时段时结束前还会再补一次。异常只跳过附加阶段，不中断日常。
    local ok_extra,extra=pcall(function()return self:daily_extras()end)
    if ok_extra and extra then return extra end
    if not ok_extra then self._task_pending=nil;self:log("附加阶段暂不可用：" .. tostring(extra)) end
    local prayer_info=daily.prayer_info
    if state.prayer and not state.loaded.prayer and prayer_info and tonumber(prayer_info.mark)==0 then
        state.loaded.prayer=true
        for index,conf in ipairs(constants.PRAYER_CONFIG or {}) do
            if tonumber(conf.req_value)==0 then
                return self:daily_request("take_daily_prayer_ret","take_daily_prayer",{index=index},"执行免费祈祷",nil)
            end
        end
    end
    local alchemy_info=daily.alchemy_info
    if state.alchemy and not state.loaded.alchemy and alchemy_info and tonumber(alchemy_info.mark)==0 then
        state.loaded.alchemy=true
        local alch_cfg=constants.ALCHEMY_CONFIG or {}
        local tier=math.floor(tonumber(self.cfg.dailyAlchemyTier) or 1)
        if tier<1 then tier=1 end
        if #alch_cfg>0 and tier>#alch_cfg then tier=#alch_cfg end
        local conf=alch_cfg[tier]
        if tier==1 and (not conf or tonumber(conf.req_value)~=0) then
            for index,row in ipairs(alch_cfg) do
                if tonumber(row.req_value)==0 then tier=index;conf=row;break end
            end
        end
        if conf then
            local cost=tonumber(conf.req_value) or 0
            if cost>0 then
                local budget_ok,budget_reason=self:spend_can_afford(cost)
                if not budget_ok then
                    self:log("消费策略拦截：跳过每日血钻炼金（" .. tostring(budget_reason) .. "）")
                elseif not resource_logic:CheckResourceNum(constants.RESOURCE_TYPE.blood_diamond,cost,false) then
                    self:log("血钻不足（需" .. cost .. "血钻），跳过每日血钻炼金")
                else
                    state.allow_paid_alchemy=true;state.alchemy_tier=tier
                    local label=tier==2 and "中级血钻炼金" or (tier==3 and "神圣血钻炼金" or "血钻炼金")
                    local res=self:daily_request("take_daily_alchemy_ret","take_daily_alchemy",{index=tier},"执行" .. label .. "（" .. cost .. "血钻）",nil)
                    if self._task_pending then self._task_pending.alchemy_cost=cost end
                    return res
                end
            else
                return self:daily_request("take_daily_alchemy_ret","take_daily_alchemy",{index=tier},"执行免费炼金",nil)
            end
        end
    end
    for _,item in pairs(daily.liveness_list or {}) do
        if not state.defer_tail and state.rewards and not item.is_reward and daily:CheckActivityComplet(item.liveness_id,item.completion_count or 0) then
            return self:daily_request("get_liveness_reward_ret","get_liveness_reward",{liveness_id=item.liveness_id},"领取任务奖励",nil)
        end
    end
    for _,index in pairs(daily.activity_receive_list or {}) do
        if not state.defer_tail and state.rewards and index then return self:daily_request("get_activity_reward_ret","get_activity_reward",{index=index},"领取活跃度宝箱",nil) end
    end
    -- Finish actions that can be completed immediately before anything that
    -- may enter a battle, cooldown, or mining wait.  In particular, a
    -- ladder cooldown must not hold up free arena, escort, or research
    -- preparation tasks behind it.
    local order={1,3,8,15,4,2,13,7,6,5,9,10,12,14}
    for _,id in ipairs(order) do
        local item=daily_item(daily,id)
        local conf=daily_conf(id)
        if (id==7 or id==14) and item and conf and self:daily_enabled(id) and self:daily_cart_pending(id) and not state.skipped[id] then
            return self:daily_cart_step(id)
        end
        local workshop_pending=id==2 and (not state.loaded.mining or daily_workshop_ready() or
            #(mining.project_list or {})<(tonumber(mining.max_project_count) or 0))
        if id==2 and self:daily_enabled(id) and not workshop_pending and not state.skipped[id] then
            state.workshop_done=true
        end
        local activity_pending=id~=2 and item and conf and not daily:CheckActivityComplet(id,item.completion_count or 0)
        if id~=7 and id~=14 and self:daily_enabled(id) and
            (workshop_pending or activity_pending) and not state.skipped[id] then
            local ok,result=pcall(function()return self:daily_action(id,conf,item)end)
            if ok then return result end
            self._task_pending=nil
            return self:daily_skip(id,"执行条件暂不满足：" .. tostring(result))
        end
        -- 副本任务（1.17）：活跃度达成后仍需「可购次数买完 + 挑战次数打完」
        if self:daily_enabled(id) and (id==9 or id==10) and not state.skipped[id]
            and self:daily_cave_pending(id) then
            local ok,result=pcall(function()return self:daily_action(id,conf,item)end)
            if ok then return result end
            self._task_pending=nil
            return self:daily_skip(id,"执行条件暂不满足：" .. tostring(result))
        end
    end
    local completed,total,remaining=0,0,{}
    for _,item in pairs(daily.liveness_list or {}) do
        local conf=daily_conf(item.liveness_id)
        if conf and item.liveness_id~=11 and self:daily_enabled(item.liveness_id) then
            total=total+1
            if daily:CheckActivityComplet(item.liveness_id,item.completion_count or 0) then completed=completed+1
            else remaining[#remaining+1]=conf.active_title .. "（" .. (state.skipped[item.liveness_id] or "条件不足") .. "）" end
        end
    end
    for _,reason in ipairs(remaining) do self:log("未完成：" .. reason) end
    if state.activity_rewards and not state.activity_final then
        state.activity_final=true;state.activity_done=false
    end
    -- 收尾前补领本轮执行后新达成的活动奖励。
    local ok_extra,extra=pcall(function()return self:daily_extras()end)
    if ok_extra and extra then return extra end
    local summary="日常执行结束：已完成" .. completed .. "/" .. total .. "项，领取" .. state.claims ..
        "份奖励；血钻消耗0" .. (#remaining>0 and "。未完成原因见日志" or "")
    -- 工会首领阶段：买满当日剩余许可证并挑战1次，一天只需执行一次
    if state.guild_boss and not state.guild_boss_ran then
        state.guild_boss_ran=true
        if self:guild_boss_done_today() then
            state.report_notes=state.report_notes or {}
            state.report_notes.guild_boss="今日已执行过，跳过"
            self:log("今日已执行过工会首领（买满+挑战），本次日常跳过")
            return self:daily_finish_chain(state,summary .. "；工会首领今日已执行过，跳过",#remaining>0)
        end
        state.summary=summary;state.partial=#remaining>0
        self.daily_state=nil
        self:start_daily_guild_boss(state.guild_boss_tickets or 0,state.guild_boss_fights or 1)
        self.daily_state=state
        state.child_guild=true
        return reply(self,summary .. "；开始工会首领阶段（许可证买满并挑战1次）",{action="工会首领阶段",phase="guild"})
    end
    return self:daily_finish_chain(state,summary,#remaining>0)
end

-- ============================================================
-- 一键日常 · 合战 / 宿命远征阶段（1.18）
--
-- 只在当天活动确实开启时执行：
--   合战：买满当日全部「加时次数」→ 逐关推进并把次数打完 →
--         用当期合战经验把赛点（合战点数）兑换到经验不足或售罄；
--   宿命远征：买满当日全部挑战次数 → 打完全部场次（对手耗尽时免费刷新）。
-- 购买一律先按消费策略血钻预算与血钻余额核对，失败/未开启只跳过该阶段，
-- 不影响后续虚空兑换与矿山；战斗只用游戏原有接口，不会复活或购买资源。
-- ============================================================
local EV_MAX_CAMPAIGN_BUY = 1000
local EV_MAX_CAMPAIGN_FIGHT = 1000
local EV_MAX_CAMPAIGN_CONVERT = 10000
local EV_MAX_EXPEDITION_FIGHT = 1000
local EV_MAX_REFRESH = 10
local EV_CD_WAIT = 90

local function ev_module(name)
    local ok,mod=pcall(require,name)
    if ok and type(mod)=="table" then return mod end
    return nil
end

local function ev_now(self)
    local ok,value=pcall(function() return time_logic:Now() end)
    if ok and tonumber(value) then return tonumber(value) end
    return task_now(self)
end

local function ev_blood_ok(cost)
    cost=tonumber(cost) or 0
    if cost<=0 then return true end
    local id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.blood_diamond
    if not id then return false end
    local ok,value=pcall(function() return resource_logic:CheckResourceNum(id,cost,false) end)
    return ok and value~=false
end

local function ev_score_type()
    return tonumber(constants.CAMPAIGN_CONVERT_SCORE) or 6
end

local function ev_challenge_type()
    local t=constants.EXPEDITION_TYPE
    return tonumber(t and t.challenge) or 1
end

local function ev_game_status()
    local s=constants.CAMPAIGN_STATUS
    return tonumber(s and s.game) or 2
end

function A:event_note(text)
    local st=self.event_activity_state
    if st then st.notes[#st.notes+1]=text end
    self:log("日常活动：" .. text)
end

function A:start_daily_events()
    -- 不调用 cancel_task：状态由一键日常的 child_events 阶段托管
    self.event_activity_state={
        phase="campaign",started=task_now(self),timeouts=0,partial=false,notes={},
        campaign={queried=false,levels=false,buffs=false,price=nil,reward_value=1,max_buys=0,
            buys=0,fights=0,converts=0,buy_stop=false,fight_stop=false,
            convert_stop=false,cd_started=nil,closed=false},
        expedition={queried=false,max_buys=0,buys=0,fights=0,refreshes=0,fought={},
            buy_stop=false,fight_stop=false,closed=false}
    }
    local state=self.daily_state
    if state then state.allow_event_purchase=true end
    self:log("日常活动阶段：检查今日合战与宿命远征是否开启")
    return true
end

-- 某个请求不被接受、超时或客户端缺少该接口时，只停掉对应子步骤。
local function ev_skip_step(st,kind)
    local cp,ep=st.campaign,st.expedition
    st.partial=true
    if kind=="c_info" then cp.queried=true;cp.closed=true
    elseif kind=="c_levels" then cp.levels=true;cp.fight_stop=true
    elseif kind=="c_buffs" then cp.buffs=true;cp.convert_stop=true
    elseif kind=="c_price" then cp.buy_stop=true;cp.price_queried=true
    elseif kind=="c_buy" then cp.buy_stop=true
    elseif kind=="c_fight" then cp.fight_stop=true
    elseif kind=="c_convert" then cp.convert_stop=true
    elseif kind=="e_info" then ep.queried=true;ep.closed=true
    elseif kind=="e_buy" then ep.buy_stop=true
    elseif kind=="e_fight" then ep.fight_stop=true
    elseif kind=="e_refresh" then ep.fight_stop=true end
end

local function ev_send(self,event,kind,key,payload,description,extra)
    local ready=pcall(function() return self:install_task_hooks() end)
    if not ready or not self._task_hooks or not self._task_hooks[event] then
        self:event_note("游戏未注册 " .. tostring(event) .. "，跳过：" .. tostring(description))
        return reply(self,"活动接口不可用，跳过：" .. tostring(description),
            {wait=true,skip=kind,action="日常活动阶段",phase="events"})
    end
    local p=self:task_request(event,"events",function()
        return network:Send({[key]=payload or {}})
    end)
    p.ev_kind=kind;p.description=description
    if extra then for k,v in pairs(extra) do p[k]=v end end
    return reply(self,description,{wait=true,action="日常活动阶段",phase="events"})
end

local function ev_apply_response(self,st,r)
    local kind=r.kind or "";local result=r.result;local msg=r.msg or {};local p=r.pending or {}
    local cp,ep=st.campaign,st.expedition
    if result~="success" then
        self:event_note(tostring(p.description or kind) .. " 未被接受：" .. tostring(result))
        ev_skip_step(st,kind)
        return
    end
    if kind=="c_info" then
        cp.queried=true
        local c=ev_module("logic.campaign")
        cp.server_status=tonumber(msg.status) or tonumber(c and c.status)
        cp.max_buys=math.max(0,math.min(tonumber(c and c.limit_count) or 0,EV_MAX_CAMPAIGN_BUY))
    elseif kind=="c_levels" then
        cp.levels=true
        local c=ev_module("logic.campaign")
        if c and c.status==nil then c.status=cp.server_status end
    elseif kind=="c_buffs" then cp.buffs=true
    elseif kind=="c_price" then
        cp.price_queried=true
        cp.price=tonumber(msg.req_value) or 0
        cp.reward_value=tonumber(msg.reward_value) or 1
        local left=tonumber(msg.limit_count)
        if left and left>=0 then cp.max_buys=math.min(EV_MAX_CAMPAIGN_BUY,cp.buys+left) end
    elseif kind=="c_buy" then
        cp.buys=cp.buys+1;cp.price=nil
        local c=ev_module("logic.campaign")
        if c and tonumber(msg.challenge_num) then c.challenge_num=tonumber(msg.challenge_num)
        else cp.buy_stop=true;cp.queried=false end
        self:event_note("合战加时次数购买成功（" .. tostring(p.cost or 0) .. "血钻）")
    elseif kind=="c_fight" then
        cp.fights=cp.fights+1
        local c=ev_module("logic.campaign")
        if c and tonumber(msg.challenge_num) then c.challenge_num=tonumber(msg.challenge_num)
        else cp.fight_stop=true end
    elseif kind=="c_convert" then
        cp.converts=cp.converts+1
        local c=ev_module("logic.campaign")
        if c and tonumber(msg.exp) and type(msg.buff_info)=="table" then
            c.exp=tonumber(msg.exp);c.buff_info_map=c.buff_info_map or {}
            c.buff_info_map[msg.buff_info.type]=msg.buff_info
            if msg.score then c.score=msg.score end
        else cp.convert_stop=true end
    elseif kind=="e_info" then
        ep.queried=true
        local lt=ev_module("logic.ladder_tower")
        local cfg=(config_manager.ladder_buy_config or {})[ev_challenge_type()] or {}
        local bought=tonumber(lt and lt.buy_figthing_count) or 0
        ep.max_buys=math.max(0,math.min(#cfg-bought,EV_MAX_CAMPAIGN_BUY))
    elseif kind=="e_buy" then
        local n=tonumber(p.buy_count) or 1
        ep.buys=ep.buys+n
        local lt=ev_module("logic.ladder_tower")
        local before=tonumber(p.before_fights) or 0
        local before_bought=tonumber(p.before_bought) or 0
        if lt then
            if (tonumber(lt.figthing_count) or 0)<=before then lt.figthing_count=before+n end
            if (tonumber(lt.buy_figthing_count) or 0)<=before_bought then lt.buy_figthing_count=before_bought+n end
        end
        self:event_note("宿命远征挑战次数购买成功（" .. n .. "次，" .. tostring(p.cost or 0) .. "血钻）")
    elseif kind=="e_fight" then
        ep.fights=ep.fights+1
        if p.enemy_id then ep.fought[p.enemy_id]=true end
        local lt=ev_module("logic.ladder_tower")
        local before=tonumber(p.before_fights) or 0
        if lt and (tonumber(lt.figthing_count) or 0)>=before and before>0 then
            lt.figthing_count=math.max(0,before-1)
        end
    elseif kind=="e_refresh" then
        ep.refreshes=ep.refreshes+1;ep.fought={}
        local lt=ev_module("logic.ladder_tower")
        local before=tonumber(p.before_search) or 0
        if lt and (tonumber(lt.search_count) or 0)>=before and before>0 then
            lt.search_count=math.max(0,before-1)
        end
    end
end

-- 合战：状态必须为「进行中」且未过结束时间（奖励兑换期不能战斗）。
local function ev_campaign_open(self,c)
    if (tonumber(c.status) or 0)~=ev_game_status() then
        return false,"当前不在合战进行中（status=" .. tostring(c.status) .. "）"
    end
    if type(c.IsOpen)=="function" then
        local ok,open=pcall(c.IsOpen,c)
        if ok and open==false then return false,"合战活动未开启或已结束" end
    elseif (tonumber(c.end_time) or 0)<=ev_now(self) then
        return false,"合战赛季已结束"
    end
    return true
end

-- 选当前可以推进的最高关卡：优先未通关/首领关，其次层数最高；跳过锁定、
-- 已通关(limit)以及仍在战斗冷却的关卡。全在冷却时返回最近的解锁时间。
local function ev_choose_campaign_level(self,c)
    local list=c and c.level_info_list
    if type(list)~="table" then return nil,nil end
    local now=ev_now(self)
    local best,soonest=nil,nil
    for _,info in pairs(list) do
        local id=tonumber(info and info.level_id)
        if id and info.status~="lock" and info.status~="limit" and tonumber(info.status)~=1 and tonumber(info.status)~=3 then
            local cd=tonumber(info.next_battle_time) or 0
            if cd<=now then
                local push=info.status=="new" or tonumber(info.status)==4
                if not best or (push and not best.push) or (push==best.push and id>best.id) then
                    best={id=id,push=push}
                end
            elseif cd>now and (not soonest or cd<soonest) then
                soonest=cd
            end
        end
    end
    if best then return best.id,nil end
    return nil,soonest
end

-- 宿命远征：功能开关 + 赛季开战时间窗 [countdown, duration)。
local function ev_expedition_open(self,lt)
    local feature=ev_module("logic.feature_config")
    if feature and type(feature.IsFeatureOpen)=="function" then
        local ok,open=pcall(feature.IsFeatureOpen,feature,"ladder_grade")
        if ok and open==false then return false,"宿命远征功能未开放" end
    end
    local now=ev_now(self)
    local countdown=tonumber(lt.countdown) or 0
    local duration=tonumber(lt.duration) or 0
    if countdown<=0 or duration<=countdown or now<countdown or now>=duration then
        return false,"当前不在宿命远征开战时段"
    end
    return true
end

function A:daily_campaign_step(st)
    local cp=st.campaign
    if cp.closed then
        st.phase="expedition"
        return reply(self,"合战未开启，检查宿命远征",{wait=true,action="日常活动阶段",phase="events"})
    end
    local c=ev_module("logic.campaign")
    if not c then
        cp.closed=true;self:event_note("合战模块未加载，跳过合战阶段")
        st.phase="expedition"
        return reply(self,"合战模块未加载，检查宿命远征",{wait=true,action="日常活动阶段",phase="events"})
    end
    if not cp.queried then
        return ev_send(self,"query_campaign_info_ret","c_info","query_campaign_info",{},"读取合战活动状态")
    end
    local opened,reason=ev_campaign_open(self,c)
    if not opened then
        cp.closed=true
        self:event_note("合战：" .. reason .. "，跳过合战阶段")
        st.phase="expedition"
        return reply(self,"合战未开启（" .. reason .. "）",{wait=true,action="日常活动阶段",phase="events"})
    end
    local cid=tonumber(c.cur_campaign_id) or 0
    if not cp.levels then
        return ev_send(self,"query_campaign_level_ret","c_levels","query_campaign_level",
            {campaign_id=cid},"读取合战关卡列表")
    end
    if not cp.buffs then
        return ev_send(self,"query_campaign_buff_ret","c_buffs","query_campaign_buff",
            {campaign_id=cid},"读取合战经验兑换价格")
    end
    -- 1) 买满当日全部可购的合战加时次数
    if not cp.price_queried then
        return ev_send(self,"query_campaign_overtime_ret","c_price","query_campaign_overtime",
            {campaign_id=cid},"读取合战可购次数与价格")
    end
    if not cp.buy_stop and cp.buys<cp.max_buys then
        if cp.price==nil then
            return ev_send(self,"query_campaign_overtime_ret","c_price","query_campaign_overtime",
                {campaign_id=cid},"读取合战加时次数价格")
        end
        local price=cp.price
        local budget_ok,budget_reason=self:spend_can_afford(price)
        if not budget_ok or not ev_blood_ok(price) then
            cp.buy_stop=true
            self:event_note("合战停止购买加时次数：" .. (budget_ok and "血钻不足" or budget_reason))
        else
            return ev_send(self,"campaign_overtime_ret","c_buy","buy_campaign_overtime",{},
                "购买合战加时次数（" .. price .. "血钻，今日还可买" .. (cp.max_buys-cp.buys) .. "次）",
                {cost=price,cost_context="合战加时次数",
                 before_challenge=tonumber(c.challenge_num) or 0,reward_value=cp.reward_value})
        end
    end
    -- 2) 自动推进关卡，把买到的次数全部打完
    if not cp.fight_stop and cp.fights<EV_MAX_CAMPAIGN_FIGHT and (tonumber(c.challenge_num) or 0)>0 then
        local level_id,soonest=ev_choose_campaign_level(self,c)
        if level_id then
            return ev_send(self,"battle_campaign_ret","c_fight","battle_campaign",{level_id=level_id},
                "推进合战第" .. level_id .. "关（剩余" .. tostring(c.challenge_num) .. "次）",
                {level_id=level_id,before_challenge=tonumber(c.challenge_num) or 0})
        elseif soonest then
            if not cp.cd_started then cp.cd_started=task_now(self) end
            if task_now(self)-cp.cd_started<EV_CD_WAIT then
                return reply(self,"等待合战关卡冷却：" .. math.max(1,math.ceil(soonest-ev_now(self))) .. "秒",
                    {wait=true,action="合战关卡冷却",phase="events"})
            end
            cp.fight_stop=true
            self:event_note("合战关卡冷却超过等待上限，剩余次数留待下次日常")
        else
            cp.fight_stop=true
            self:event_note("合战当前没有可挑战关卡，剩余次数留待下次日常")
        end
    end
    -- 3) 推进之后，用合战经验把赛点（合战点数）兑换到经验不足或售罄
    if not cp.convert_stop and cp.converts<EV_MAX_CAMPAIGN_CONVERT then
        local buff=(c.buff_info_map or {})[ev_score_type()]
        local req=tonumber(buff and buff.req_exp) or 0
        local exp=tonumber(c.exp) or 0
        if req>0 and exp>=req then
            return ev_send(self,"convert_campaign_exp_ret","c_convert","convert_campaign_exp",
                {convert_id=ev_score_type()},
                "用合战经验兑换赛点（经验" .. exp .. "，本次需" .. req .. "）",
                {before_exp=exp,before_req=req})
        end
        cp.convert_stop=true
        if req<=0 then self:event_note("合战赛点兑换已达上限，兑换结束")
        else self:event_note("合战经验不足，赛点兑换结束（经验" .. exp .. "/需" .. req .. "）") end
    end
    st.phase="expedition"
    return reply(self,string.format("合战阶段结束：加时%d次·推进%d关·赛点兑换%d次",
        cp.buys,cp.fights,cp.converts),{wait=true,action="日常活动阶段",phase="events"})
end

function A:daily_expedition_step(st)
    local ep=st.expedition
    if ep.closed then
        st.phase="done"
        return reply(self,"宿命远征未开启，活动阶段收尾",{wait=true,action="日常活动阶段",phase="events"})
    end
    local lt=ev_module("logic.ladder_tower")
    if not lt then
        ep.closed=true;self:event_note("宿命远征模块未加载，跳过该阶段")
        st.phase="done"
        return reply(self,"宿命远征模块未加载",{wait=true,action="日常活动阶段",phase="events"})
    end
    if not ep.queried then
        return ev_send(self,"query_expedition_ret","e_info","query_expedition",{},"读取宿命远征赛季状态")
    end
    local opened,reason=ev_expedition_open(self,lt)
    if not opened then
        ep.closed=true
        self:event_note("宿命远征：" .. reason .. "，跳过该阶段")
        st.phase="done"
        return reply(self,"宿命远征未开启（" .. reason .. "）",{wait=true,action="日常活动阶段",phase="events"})
    end
    local etype=ev_challenge_type()
    local cfg=(config_manager.ladder_buy_config or {})[etype] or {}
    local bought=tonumber(lt.buy_figthing_count) or 0
    -- 1) 买满当日全部可购挑战次数（价格阶梯 100/150/300…，按预算逐次缩减）
    if not ep.buy_stop and ep.buys<ep.max_buys then
        local function price_for(n)
            local cost=0
            for i=1,n do
                local row=cfg[bought+i]
                if not row then return nil end
                cost=cost+(tonumber(row.price) or 0)
            end
            return cost
        end
        local n=math.min(ep.max_buys-ep.buys,math.max(0,#cfg-bought))
        local cost,stop_reason=nil,""
        while n>0 do
            cost=price_for(n)
            if cost then
                local budget_ok,budget_reason=self:spend_can_afford(cost)
                if budget_ok and ev_blood_ok(cost) then break end
                stop_reason=budget_ok and "血钻不足" or budget_reason
            else
                stop_reason="购买价格配置缺失"
            end
            n=n-1
        end
        if n<=0 then
            ep.buy_stop=true
            self:event_note("宿命远征停止购买挑战次数：" .. stop_reason)
        else
            return ev_send(self,"expedition_apend_times_ret","e_buy","expedition_apend_times",
                {type=etype,num=n},
                "购买宿命远征挑战次数" .. n .. "次（" .. cost .. "血钻）",
                {cost=cost,cost_context="宿命远征挑战次数",buy_count=n,
                 before_fights=tonumber(lt.figthing_count) or 0,before_bought=bought})
        end
    end
    -- 2) 打完全部挑战次数，自动推进段位；对手用完时只用免费搜索次数刷新
    if not ep.fight_stop and ep.fights<EV_MAX_EXPEDITION_FIGHT and (tonumber(lt.figthing_count) or 0)>0 then
        local enemy_id
        for _,p in pairs(lt.players or {}) do
            local id=p.enemy_id or p.user_id or p.id
            if id and not ep.fought[id] then enemy_id=id;break end
        end
        if not enemy_id then
            if (tonumber(lt.search_count) or 0)>0 and ep.refreshes<EV_MAX_REFRESH then
                return ev_send(self,"refresh_expedition_search_ret","e_refresh","refresh_expedition_search",{},
                    "免费刷新宿命远征对手",{before_search=tonumber(lt.search_count) or 0})
            end
            ep.fight_stop=true
            self:event_note("宿命远征没有新的可挑战对手，剩余次数留待下次日常")
        else
            return ev_send(self,"expedition_fight_ret","e_fight","expedition_fight",{enemy_id=enemy_id},
                "宿命远征挑战对手" .. tostring(enemy_id) .. "（剩余" .. tostring(lt.figthing_count) .. "次）",
                {enemy_id=enemy_id,before_fights=tonumber(lt.figthing_count) or 0})
        end
    end
    st.phase="done"
    return reply(self,string.format("宿命远征阶段结束：购买%d次·挑战%d场",ep.buys,ep.fights),
        {wait=true,action="日常活动阶段",phase="events"})
end

function A:daily_events_step()
    local st=self.event_activity_state
    if not st then return reply(self,"合战与宿命远征阶段已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName()~="world" then
        return reply(self,"等待进入游戏",{wait=true,phase="events"})
    end
    if self.inBattle and self.cfg.autoSkipBattle==false then
        return reply(self,"等待合战/宿命远征战斗结算",{wait=true,phase="events"})
    end
    pcall(function() self:hide_panel("reward_panel") end)
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at<=25 then
                return reply(self,"等待活动服务器响应",{wait=true,phase="events"})
            end
            self._task_pending=nil
            st.timeouts=(st.timeouts or 0)+1
            self:event_note("请求超时（" .. tostring(pending.description) .. "），跳过该步骤")
            ev_skip_step(st,pending.ev_kind)
            if st.timeouts>=3 then st.phase="done" end
            return reply(self,"活动请求超时，跳过该步骤",{wait=true,action="活动超时",phase="events"})
        end
        local msg=pending.response or {}
        -- These four query messages deliberately have no result field in the protocol.
        local query=pending.ev_kind=="c_info" or pending.ev_kind=="c_levels" or
            pending.ev_kind=="c_buffs" or pending.ev_kind=="c_price"
        local result=tostring(msg.result or (query and "success" or "missing_result"))
        if query and msg.result==nil then
            local valid=(pending.ev_kind=="c_info" and tonumber(msg.cur_campaign_id) and tonumber(msg.status)) or
                (pending.ev_kind=="c_levels" and type(msg.level_info_list)=="table") or
                (pending.ev_kind=="c_buffs" and type(msg.buff_info_list)=="table") or
                (pending.ev_kind=="c_price" and tonumber(msg.req_value) and tonumber(msg.limit_count))
            if not valid then result="missing_query_data" end
        end
        self._task_pending=nil
        if pending.cost and result=="success" then
            self:spend_record(pending.cost,pending.cost_context or "活动挑战次数")
        end
        ev_apply_response(self,st,{kind=pending.ev_kind,result=result,msg=msg,pending=pending})
    end
    if st.timeouts>=3 then st.phase="done" end
    local result
    if st.phase=="campaign" then result=self:daily_campaign_step(st)
    elseif st.phase=="expedition" then result=self:daily_expedition_step(st)
    else
        local cp,ep=st.campaign,st.expedition
        local parts={}
        if cp.closed then parts[#parts+1]="合战未开启"
        else parts[#parts+1]=string.format("合战加时%d次·推进%d关·赛点兑换%d次",cp.buys,cp.fights,cp.converts) end
        if ep.closed then parts[#parts+1]="宿命远征未开启"
        else parts[#parts+1]=string.format("宿命远征购买%d次·挑战%d场",ep.buys,ep.fights) end
        local message="合战与宿命远征阶段结束：" .. table.concat(parts,"；")
        self:log(message)
        self.event_activity_state=nil
        local state=self.daily_state
        if state then state.allow_event_purchase=false end
        return reply(self,message,{done=true,partial=st.partial})
    end
    if result and result.skip then ev_skip_step(st,result.skip) end
    return result
end
