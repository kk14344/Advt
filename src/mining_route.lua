-- A known target needs a real path, not repeated digs around its perimeter.
-- Purple-gold rock is the native TNT-only tile: DigOrCollectBlock on it uses
-- TNT directly. Do not follow it with UseTNT (the latter skips timed ores).
local function route_tnt_cost(kind,options)
    local info=block_cfg(kind)
    if not info or (tonumber(info.tnt_count) or 0)<=0 then return nil end
    if kind==PURPLE then return tonumber(info.tnt_count) end
    -- Gems/long ores that the normal fast route cannot cross also need a
    -- deliberate TNT opening. Selecting them alone does not spend TNT.
    if can_dig(kind,current_pickaxe(),options.maxDigTime,true)
        and not can_dig(kind,current_pickaxe(),options.maxDigTime,options.allowLongOre) then return tonumber(info.tnt_count) end
end
local function route_less(a,b)
    if a.blasts~=b.blasts then return a.blasts<b.blasts end
    if a.tnt~=b.tnt then return a.tnt<b.tnt end
    if a.digs~=b.digs then return a.digs<b.digs end
    if a.y~=b.y then return a.y<b.y end
    return a.x<b.x
end
function A:blast_path(target)
    local grid=mining.block_list;local pickaxe=current_pickaxe()
    local max=tonumber(self.cfg.maxTargetPath) or 90
    local heap,seen={},{};local visited=0
    local function push(node)
        local i=#heap+1
        while i>1 do local parent=math.floor(i/2);if not route_less(node,heap[parent]) then break end
            heap[i]=heap[parent];i=parent end
        heap[i]=node
    end
    local function pop()
        local root=heap[1];local last=table.remove(heap)
        if #heap>0 then
            local i=1
            while i*2<=#heap do
                local child=i*2
                if child<#heap and route_less(heap[child+1],heap[child]) then child=child+1 end
                if not route_less(heap[child],last) then break end
                heap[i]=heap[child];i=child
            end
            heap[i]=last
        end
        return root
    end
    local function offer(x,y,parent)
        local kind=block_at(grid,x,y);local blast=route_tnt_cost(kind,self.cfg)
        if not is_empty(kind) and not is_free_chest(kind) and not blast and not can_dig(kind,pickaxe,self.cfg.maxDigTime,self.cfg.allowLongOre) then return end
        local node={x=x,y=y,parent=parent,blast=blast,
            blasts=(parent and parent.blasts or 0)+(blast and 1 or 0),
            tnt=(parent and parent.tnt or 0)+(blast or 0),
            digs=(parent and parent.digs or 0)+(is_empty(kind) and 0 or 1)}
        if node.digs>max then return end
        local key=x..":"..y;local previous=seen[key]
        if not previous or route_less(node,previous) then seen[key]=node;push(node) end
    end
    for _,dir in ipairs(DIRS)do offer(target.x+dir.x,target.y+dir.y,nil) end
    while #heap>0 and visited<math.max(2000,max*160) do
        route_work()
        local node=pop();local key=node.x..":"..node.y
        if seen[key]==node then
            visited=visited+1
            if is_empty(block_at(grid,node.x,node.y)) then
                local path={};local cursor=node.parent
                while cursor do
            path[#path+1]={x=cursor.x,y=cursor.y,blast=cursor.blast,free_chest=is_free_chest(block_at(grid,cursor.x,cursor.y))}
                    cursor=cursor.parent
                end
                return path,node.tnt
            end
            for _,dir in ipairs(DIRS)do offer(node.x+dir.x,node.y+dir.y,node) end
        end
    end
end
function A:plan_mining_target(targets)
    local best
    local rank=math.huge;local available=false
    for _,target in ipairs(targets)do
        target.rank=target.golem and 0 or ((target.chest or target.treasure) and 1 or 2)
        target.deferred=target.defer_until and target.defer_until>self.now or false
        if target.rank<rank then rank=target.rank;available=not target.deferred
        elseif target.rank==rank and not target.deferred then available=true end
    end
    -- A zero-dig route cannot be beaten. Avoid searching distant targets of
    -- the same rank before immediately actionable golems/chests.
    for _,target in ipairs(targets)do
        if target.rank==rank and not target.needs_snapshot and (not available or not target.deferred) then
            for _,dir in ipairs(DIRS)do
                if is_empty(block_at(mining.block_list,target.x+dir.x,target.y+dir.y)) then
                    if not best or target.manhattan<best.manhattan then
                        target.path={};target.route_tnt=0;target.route_cost=0;best=target
                    end
                    break
                end
            end
        end
    end
    if best then return best end
    for _,target in ipairs(targets)do
      if target.rank==rank and (not available or not target.deferred) then
        local path,immediate=find_path(mining.block_list,current_pickaxe(),target.x,target.y,
            self.cfg.maxTargetPath,self.cfg.maxDigTime,self.cfg.allowLongOre,false)
        if immediate then path={} end
        local tnt=0
        if target.needs_snapshot then path=nil
        elseif not path then
            -- A stream cache edge is unexplored, not a wall. Try every ordinary
            -- direction, including a route through not-yet-loaded cells, before
            -- concluding that a complete TNT barrier surrounds the target.
            local x,y=target.x,target.y
            local pos=mining.cur_position;local distance=math.abs(x-pos.x)+math.abs(y-pos.y)
            if distance>(tonumber(self.cfg.maxTargetPath) or 90)-4 then
                for ey,row in pairs(mining.block_list) do for ex,kind in pairs(row) do
                    route_work()
                    if is_empty(kind) then
                        local d=math.abs(target.x-ex)+math.abs(target.y-ey)
                        if d<distance then pos={x=ex,y=ey};distance=d end
                    end
                end end
            end
            local horizon=math.max(4,math.min(24,(tonumber(self.cfg.maxTargetPath) or 90)-4))
            if distance>(tonumber(self.cfg.maxTargetPath) or 90)-4 then
                x=pos.x+math.floor((x-pos.x)*horizon/distance+.5)
                y=pos.y+math.floor((y-pos.y)*horizon/distance+.5)
            end
            path=find_path(mining.block_list,current_pickaxe(),x,y,self.cfg.maxTargetPath,
                self.cfg.maxDigTime,self.cfg.allowLongOre,false,true)
            if not path then path,tnt=self:blast_path(target) end
        end
        target.path=path;target.route_tnt=tnt or 0
        target.rank=target.golem and 0 or ((target.chest or target.treasure) and 1 or 2)
        target.route_cost=path and #path or math.huge
        target.deferred=target.defer_until and target.defer_until>self.now or false
        if not best or target.rank<best.rank or (target.rank==best.rank and
            ((best.deferred and not target.deferred) or (target.deferred==best.deferred and
            (target.route_cost<best.route_cost or (target.route_cost==best.route_cost and target.manhattan<best.manhattan))))) then best=target end
      end
    end
    return best
end
function A:plan_treasure_approach(target)
    local grid, pickaxe = mining.block_list, current_pickaxe()
    local max_time, allow_long = self.cfg.maxDigTime, self.cfg.allowLongOre
    local avoid_gems = self:is_rush_mode()
    target.route_tnt = 0
    -- A bounded search for known tiles, followed by the 1.19 frontier score.
    local path = find_path(grid, pickaxe, target.x, target.y,
        self.cfg.maxTargetPath, max_time, allow_long, avoid_gems, false, 512)
    if path then target.path=path;return target end

    local enclosed = true
    for _, dir in ipairs(DIRS) do
        local kind = block_at(grid, target.x + dir.x, target.y + dir.y)
        if kind == nil or is_free_chest(kind) or can_dig(kind, pickaxe, max_time, allow_long) then
            enclosed = false
            break
        end
    end
    -- Keep the existing TNT route for a confirmed enclosure.
    if enclosed then return nil end

    local best, best_score
    for _, tile in ipairs(self:frontier(true)) do
        if not (avoid_gems and is_gem_block(tile.block_type)) then
            local distance = math.abs(tile.x - target.x) + math.abs(tile.y - target.y)
            local score = distance * 10 + tile.time
            if not best_score or score < best_score then best, best_score = tile, score end
        end
    end
    target.path = best and {best} or nil
    return target
end

function A:plan_mining_target_budgeted(targets)
    local treasure
    for _, target in ipairs(targets) do
        if target.golem then treasure = nil;break end
        if target.treasure and (not treasure or target.manhattan < treasure.manhattan) then treasure = target end
    end
    if treasure then
        local plan = self:plan_treasure_approach(treasure)
        if plan then self._route_job = nil;return plan, false end
        targets = {treasure}
    end
    local pos=mining.cur_position or {};local parts={tostring(pos.x),tostring(pos.y),tostring(self._route_revision or 0),
        tostring(mining.stream_generation),tostring(mining.stream_mutation),tostring(mining.stream_event_revision),
        tostring(self.cfg.maxTargetPath),tostring(self.cfg.maxDigTime),tostring(self.cfg.allowLongOre),tostring(current_pickaxe())}
    local target_keys={}
    for _,t in ipairs(targets)do target_keys[#target_keys+1]=table.concat({t.x,t.y,t.block_type or 0,t.golem and 1 or 0,t.treasure and 1 or 0,t.needs_snapshot and 1 or 0,(t.defer_until or 0)>self.now and 1 or 0},":")end
    table.sort(target_keys);parts[#parts+1]=table.concat(target_keys,";")
    local key=table.concat(parts,"|");local job=self._route_job
    if not job or job.key~=key or (job.done and self.now>job.until_at) then
        job={key=key};job.thread=coroutine.create(function()return self:plan_mining_target(targets)end);self._route_job=job
    end
    if job.done then return job.best,false end
    self._route_budget={work=0,clock=os and type(os.clock)=="function",started=os and type(os.clock)=="function" and os.clock() or 0}
    local ok,best=coroutine.resume(job.thread);self._route_budget=nil
    if not ok then self._route_job=nil;error(best)end
    if coroutine.status(job.thread)=="dead" then job.done=true;job.best=best;job.until_at=self.now+0.4;return best,false end
    return nil,true
end
function A:load_target_route(target,step)
    self.status="加载前往"..target.label.."的通路"
    self.detail="逐段接近目标；单侧挡路时绕行，不把未加载地块当成封闭"
    if not step then
        local enclosed=true
        for _,dir in ipairs(DIRS)do
            local kind=block_at(mining.block_list,target.x+dir.x,target.y+dir.y)
            if kind==nil or is_empty(kind) or is_free_chest(kind)
                or can_dig(kind,current_pickaxe(),self.cfg.maxDigTime,self.cfg.allowLongOre) then enclosed=false;break end
        end
        if enclosed then
            self.status="目标四周封闭，暂不可达"
            self.detail="四向均无普通通路，核对完整地形；不在外围反复消耗矿镐"
        end
    end
    if self.now<(self._route_query_at or 0) then return end
    self._route_query_at=self.now+1
    pcall(function()
        if type(mining.RequestMiningRegion)=="function" then
            local pos=mining.cur_position
            local focus=step or {x=math.floor((pos.x+target.x)/2),y=math.floor((pos.y+target.y)/2)}
            mining:RequestMiningRegion(focus,40,40,true)
            mining:RequestMiningRegion({x=target.x,y=target.y},6,6,true)
        else mining:QueryBlockInfo() end
    end)
end
function A:route_mutation(event,state,action)
    local previous=state.uncertain
    local accepted
    local request=self:task_request(event,"mine_target_blast",function()accepted=action();return true end)
    state.uncertain=request;state.at=self.now;state.next_read=nil
    request.accept_response=function(msg)
        local pos=msg.cur_position
        return event~="dig_block_ret" or msg.result~="success" or (pos and pos.x==state.x and pos.y==state.y)
    end
    if accepted==false then self._task_pending=nil;state.uncertain=previous;return false end
    return true
end
function A:blast_target_path(tile,target)
    local kind=mining:GetBlockType(tile.x,tile.y)
    local cost=route_tnt_cost(kind,self.cfg)
    local id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.tnt
    local available=id and tonumber(resource_logic:GetResourceNum(id)) or 0
    if not cost or available<cost then
        self.status="目标通路需要雷管";self.detail="保留矿镐，等待雷管足够后打通";return true
    end
    if self._task_pending or not self:can_act() or not mining:CheckConnected(tile.x,tile.y) then return true end
    local state={x=tile.x,y=tile.y,kind=kind,at=self.now,label=target.label,cost=cost,phase=kind==PURPLE and "direct" or "select"}
    self._mine_blast=state
    local accepted=self:route_mutation("dig_block_ret",state,function()return self:dig(tile.x,tile.y,"打通"..target.label.."的挡路矿石")end)
    if not accepted then self._mine_blast=nil;self._task_pending=nil end
    return true
end
function A:mine_blast_step()
    local state=self._mine_blast;if not state then return false end
    local p=self._task_pending
    if mining:GetBlockType(state.x,state.y)==EMPTY then
        if p and (p.kind=="mine_target_blast" or p.kind=="mine_target_blast_read") then self._task_pending=nil end
        self._mine_blast=nil;return false
    end
    if state.error then
        local count=tonumber(resource_logic:GetResourceNum(constants.RESOURCE_TYPE.tnt)) or 0
        if count~=state.stock then self._mine_blast=nil;return false end
        self.status="雷管通路暂未打通："..state.error;self.detail="等待资源或地形同步，不在目标外围消耗矿镐";return true
    end
    if state.phase=="ready_tnt" or state.phase=="collect" then
        local pos=mining.cur_position
        if not pos or pos.x~=state.x or pos.y~=state.y or mining.cur_block_type~=state.kind then
            self.status="等待挡路矿石位置同步";return true
        end
        local ok,autoplay=pcall(require,"logic.mining_autoplay")
        if ok and autoplay and type(autoplay.CanManualMine)=="function" and autoplay:CanManualMine()==false then
            self.status="等待游戏挖矿模式就绪";return true
        end
        if p then self.status="等待挡路矿石操作确认";return true end
        if state.phase=="ready_tnt" then
            if (tonumber(resource_logic:GetResourceNum(constants.RESOURCE_TYPE.tnt)) or 0)<state.cost then
                self.status="挡路宝石需要雷管，等待补充";return true
            end
            state.phase="tnt"
            if not self:route_mutation("use_tnt_ret",state,function()return network:Send({use_tnt={}})end) then state.phase="ready_tnt" end
            self.status="雷管炸开挡路宝石";return true
        end
        if (tonumber(mining.dig_endtime) or 0)>time_logic:Now()+0.01 then self.status="等待爆破完成";return true end
        state.phase="collect_wait"
        if not self:route_mutation("collect_mine_ret",state,function()
            if type(mining.SendCollectMine)=="function" then return mining:SendCollectMine(state.x,state.y) end
            return network:Send({collect_mine={x=state.x,y=state.y}})
        end) then state.phase="collect" end
        self.status="收取已炸开的挡路宝石";return true
    end
    -- A timed-out TNT dig may already have spent resources. Only refresh reads;
    -- the late ACK or cleared tile releases this barrier. Never replay it.
    if p and p.kind=="mine_target_blast_read" and p.response then self._task_pending=nil;p=nil end
    if self.now-state.at>3 and self.now>=(state.next_read or 0) then
        state.next_read=self.now+3
        if not p or p.kind=="mine_target_blast" or p.kind=="mine_target_blast_read" then
            self._task_pending=nil
            local accepted
            self:task_request("query_mining_info_ret","mine_target_blast_read",function()accepted=network:Send({query_mining_info={}});return true end)
            if accepted==false then self._task_pending=nil end
            pcall(function()mining:QueryBlockInfo()end)
        end
    end
    self.status="等待雷管通路确认";self.detail="仅核对地形，不重复消耗雷管";return true
end
local route_response=A.mining_on_response
function A:mining_on_response(name,msg)
    if route_response then route_response(self,name,msg) end
    if name=="dig_block_ret" or name=="collect_mine_ret" or name=="use_tnt_ret" or name=="query_mining_block_info_ret"
        or name=="query_mining_info_ret" or name=="accept_random_event_ret" or name=="finish_random_event_ret" then
        self._route_revision=(self._route_revision or 0)+1
    end
    local fight=self.fight
    if fight and fight.phase=="solve" then
        local pos=msg.cur_position
        if name=="dig_block_ret" and msg.result and msg.result~="success" and (not pos or (pos.x==fight.x and pos.y==fight.y)) then
            fight.rejection=tostring(msg.result)
        elseif fight.chest and name=="dig_block_ret" and pos and pos.x==fight.x and pos.y==fight.y then
            fight.settlement_received=true
        elseif not fight.chest and name=="solve_adventure_event_ret"
            and (not msg.event_id or msg.event_id==fight.event_id or (tonumber(msg.event_id)==0
                and mining.cur_position and mining.cur_position.x==fight.x and mining.cur_position.y==fight.y)) then
            if msg.result and msg.result~="success" then fight.rejection=tostring(msg.result)
            else fight.settlement_received=true end
        end
    end
    local state=self._mine_blast
    local request=state and state.uncertain
    if request and name==request.event and (not request.accept_response or request.accept_response(msg)) then
        request.response=msg
        local p=self._task_pending
        if p and (p.kind=="mine_target_blast" or p.kind=="mine_target_blast_read") then self._task_pending=nil end
        if msg.result~="success" then
            state.error=tostring(msg.result);state.stock=tonumber(resource_logic:GetResourceNum(constants.RESOURCE_TYPE.tnt)) or 0
        elseif name=="dig_block_ret" and state.phase=="select" then state.phase="ready_tnt"
        elseif name=="use_tnt_ret" then state.phase="collect"
        else self._mine_blast=nil end
    end
end
