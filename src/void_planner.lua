-- Plan all remaining fights together. A mercenary has one seat per fight and
-- a limited number of appearances across the entire route, including replay.
local function void_rows(week)
    local out={}
    for _,value in pairs(config_manager.vanity_maze_config or {}) do
        if type(value)=="table" then
            if tonumber(value.week)==week and tonumber(value.map_id) then out[value.map_id]=value
            elseif not value.map_id then
                for _,row in pairs(value) do if type(row)=="table" and tonumber(row.week)==week and tonumber(row.map_id) then out[row.map_id]=row end end
            end
        end
    end
    return out
end
function A:void_layout(week)
    local rows=void_rows(week);local last=0
    for id in pairs(rows) do last=math.max(last,tonumber(id) or 0) end
    if last==0 then last=#(GUIDE[week] or {})+1 end
    if last<3 or not GUIDE[week] or #GUIDE[week]~=last-1 then return nil,"今日关卡配置与攻略数量不一致" end
    return {last=last,penultimate=last-1,rows=rows}
end
function A:void_rune_effects()
    local ok,rune=pcall(require,"logic.rune")
    if not ok or type(rune.GenerateRuneListPropertys)~="function" then return {mine_property={},enemy_property={}} end
    local worked,properties,equipped=pcall(function()
        local equipped=type(rune.GetRuneEquipmentList)=="function" and rune:GetRuneEquipmentList() or rune.rune_equipment_list
        return rune:GenerateRuneListPropertys(equipped or {}),equipped or {}
    end)
    local out={mine_property={},enemy_property={}}
    if worked and type(properties)=="table" then
        out.mine_property=properties.mine_property or {};out.enemy_property=properties.enemy_property or {}
        for _,info in pairs(equipped or {}) do
            local conf=info.template_info or (config_manager.rune_config or {})[info.template_id] or {}
            for id in tostring(conf.skill_id or ""):gmatch("%d+") do
                local skill=(config_manager.active_skill_config or {})[tonumber(id)]
                if skill and (tonumber(skill.turn_fixed) or 0)>0 then out.adds_turns=true end
            end
        end
    end
    return out
end
local function void_stats(troop,chosen,effects,boss)
    local stats,err=formation_stats(troop,chosen)
    if not stats then return nil,err end
    local mine=(effects or {}).mine_property or {};local enemy=(effects or {}).enemy_property or {}
    -- The hosted client's native vanity property getter excludes equipped
    -- runes. Keep base values intact and apply rune deltas only in the estimate.
    stats.base_speed=stats.speed
    stats.speed=math.max(0,stats.speed+(tonumber(mine.speed) or 0))
    local enemy_speed=tonumber(boss and boss.speed)
    if enemy_speed then
        stats.speed=stats.speed+enemy_speed-math.max(0,enemy_speed+(tonumber(enemy.speed) or 0))
    end
    stats.defense=(stats.defense or 0)+(tonumber(mine.defense) or 0)
    stats.dodge=(stats.dodge or 0)+(tonumber(mine.dodge) or 0)
    stats.authority=(stats.authority or 0)+(tonumber(mine.authority) or 0)
    return stats
end
local function reliability(info,tag)
    if tag=="随意" then return 100 end
    local best=0;local template=info.template_info or {}
    for k=1,3 do
        local sk=(config_manager.active_skill_config or {})[tonumber(template["skill"..k]) or 0]
        if sk then
            local probe={template_info={quality=template.quality,skill1=template["skill"..k],name=template.name,genre=template.genre}}
            local tags=skill_tags(probe)
            if tags[tag] then best=math.max(best,tonumber(sk.trigger_chance) or 0) end
        end
    end
    return best
end

-- Successive shortest augmenting paths. The stage/card edge enforces one seat
-- per card, while the card/sink edge enforces its real remaining battle count.
local function assign_route(pool,steps,plans,mode)
    local graph={};local function node()graph[#graph+1]={};return #graph end
    local source=node();local sink=node()
    local function edge(a,b,cap,cost)
        local f={to=b,cap=cap,cost=cost,rev=#graph[b]+1};local r={to=a,cap=0,cost=-cost,rev=#graph[a]+1}
        graph[a][#graph[a]+1]=f;graph[b][#graph[b]+1]=r;return f
    end
    local cards={};for j,merc in ipairs(pool) do cards[j]=node();edge(cards[j],sink,merc.count,0) end
    local slots={};local required=0
    for stage,plan in ipairs(plans) do
        local stage_cards={}
        for j=1,#pool do stage_cards[j]=node();edge(stage_cards[j],cards[j],1,0) end
        local row={};slots[stage]=row
        for tag in plan.slots:gmatch("%S+") do
            required=required+1;local slot=node();edge(source,slot,1,0);local choices={};row[#row+1]=choices
            for j,merc in ipairs(pool) do
                if merc.tags[tag] and merc.quality>=(plan.minQuality or 4) and not (plan.strongHeal and tag=="恢复" and not merc.tags.strong_heal) then
                    local score=merc.bp
                    if mode==1 then score=reliability(merc.info,tag)*10000000+merc.bp
                    elseif mode==3 and (plan.speedMin or plan.speedMax) then
                        score=(plan.speedMax and -1 or 1)*(tonumber(merc.info.speed) or 0)*100000000+merc.bp
                    end
                    choices[#choices+1]={merc=merc,edge=edge(slot,stage_cards[j],1,-score)}
                end
            end
            if #choices==0 then return nil,"缺少「"..tag.."」或可用出战次数" end
        end
        if #row>steps[stage].capacity then return nil,"关卡上阵席位不足" end
    end
    for flow=1,required do
        local distance,prev_node,prev_edge,in_queue={[source]=0},{},{},{[source]=true}
        local queue={source};local head=1
        while head<=#queue do
            local a=queue[head];head=head+1;in_queue[a]=false
            for index,e in ipairs(graph[a]) do
                if e.cap>0 then
                    local d=distance[a]+e.cost
                    if distance[e.to]==nil or d<distance[e.to]-0.001 then
                        distance[e.to]=d;prev_node[e.to]=a;prev_edge[e.to]=index
                        if not in_queue[e.to] then queue[#queue+1]=e.to;in_queue[e.to]=true end
                    end
                end
            end
        end
        if not prev_node[sink] then return nil,"同一佣兵剩余出战次数不足以覆盖全程及末关重打" end
        local at=sink
        while at~=source do
            local a=prev_node[at];local e=graph[a][prev_edge[at]]
            e.cap=e.cap-1;graph[at][e.rev].cap=graph[at][e.rev].cap+1;at=a
        end
    end
    local route={}
    for stage,row in ipairs(slots) do
        local chosen={}
        for i,choices in ipairs(row) do for _,choice in ipairs(choices) do if choice.edge.cap==0 then chosen[i]=choice.merc;break end end end
        route[stage]={chosen=chosen,plan=plans[stage],maze=steps[stage].maze,phase=steps[stage].phase,capacity=steps[stage].capacity,boss_id=steps[stage].boss_id}
    end
    return route
end
function A:void_plan_begin(troop,steps,week)
    local pool={};local seen={}
    for _,entry in ipairs(troop.vanity_mercenarys_list or {}) do
        if (tonumber(entry.battle_num) or 0)>0 then
            local info=troop:GetVanityMercenarys(entry.instance_id)
            if info and not seen[entry.instance_id] then
                seen[entry.instance_id]=true
                local tags,adds,quality,name=skill_tags(info)
                if not (week==5 and adds) then pool[#pool+1]={id=entry.instance_id,info=info,tags=tags,quality=quality,name=name,
                    bp=tonumber(info.battle_point) or 0,count=math.floor(tonumber(entry.battle_num) or 0)} end
            end
        end
    end
    table.sort(pool,function(a,b)return tostring(a.id)<tostring(b.id)end)
    local indices={};for i=1,#steps do indices[i]=1 end
    return {pool=pool,steps=steps,week=week,indices=indices,mode=1,tried=0,effects=self:void_rune_effects()}
end
function A:void_plan_next(troop,search)
    if search.week==5 and search.effects.adds_turns then
        search.fatal=true;return nil,"已装备符文会增加回合，周五攻略的固定出手顺序不再适用；未自动更换符文或付费",true
    end
    if #search.steps==0 then return {route={}} end
    local plans={}
    for i,step in ipairs(search.steps) do
        local original=GUIDE[search.week] and GUIDE[search.week][step.maze-1] and GUIDE[search.week][step.maze-1][search.indices[i]]
        if not original then search.fatal=true;return nil,"攻略缺少今日关卡方案",true end
        local plan={};for k,v in pairs(original)do plan[k]=v end
        -- 神影的永久封印要求先手；原攻略只有战力门槛。
        local boss=(config_manager.monster_config or {})[step.boss_id or 0]
        if boss then
            local permanent_seal=tonumber(boss.ID)==41015
            for id in tostring(boss.skill or ""):gmatch("%d+") do
                local skill=(config_manager.active_skill_config or {})[tonumber(id)]
                if skill and (tonumber(skill.seal_turn) or 0)>=99 then permanent_seal=true end
            end
            if permanent_seal then plan.speedMin=math.max(tonumber(plan.speedMin) or -1,tonumber(boss.speed) or 0) end
        end
        plans[i]=plan
    end
    search.tried=search.tried+1
    local route,reason=assign_route(search.pool,search.steps,plans,search.mode)
    if route then
        for _,step in ipairs(route) do
            local boss=(config_manager.monster_config or {})[step.boss_id or 0]
            local stats,err=void_stats(troop,step.chosen,search.effects,boss);step.stats=stats
            if not stats then search.fatal=true;return nil,"阵容属性计算失败："..tostring(err),true
            elseif step.plan.bpMin and stats.bp<=step.plan.bpMin then reason="战力未达到攻略门槛";route=nil;break
            elseif step.plan.speedMin and stats.speed<=step.plan.speedMin then reason="含符文的相对先攻未达到先手门槛";route=nil;break
            elseif step.plan.speedMax and stats.speed>=step.plan.speedMax then reason="含符文的相对先攻超过后手上限";route=nil;break end
        end
    end
    if route then return {route=route,tried=search.tried,effects=search.effects} end
    search.reason=reason
    search.mode=search.mode+1
    if search.mode<=3 then return nil,reason,false end
    search.mode=1
    for i=#search.indices,1,-1 do
        search.indices[i]=search.indices[i]+1
        if search.indices[i]<=#GUIDE[search.week][search.steps[i].maze-1] then return nil,reason,false end
        search.indices[i]=1
    end
    return nil,reason,true
end
A._void_stats=void_stats
