-- A known target needs a real path, not repeated digs around its perimeter.
-- Purple-gold rock is the native TNT-only tile: DigOrCollectBlock on it uses
-- TNT directly. Do not follow it with UseTNT (the latter skips timed ores).
do
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
    if a.score then
        if a.score~=b.score then return a.score<b.score end
        if a.picks~=b.picks then return a.picks<b.picks end
    end
    if a.blasts~=b.blasts then return a.blasts<b.blasts end
    if a.tnt~=b.tnt then return a.tnt<b.tnt end
    if a.digs~=b.digs then return a.digs<b.digs end
    if a.y~=b.y then return a.y<b.y end
    return a.x<b.x
end
local function mining_tnt_stock()
    local id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.tnt
    return id and (tonumber(resource_logic:GetResourceNum(id)) or 0) or 0
end

local function route_picks(path)
    local count=0
    for _,tile in ipairs(path) do
        if not tile.free_chest and not (tile.blast and tile.kind==PURPLE) then count=count+1 end
    end
    return count*(1+(tonumber(mining.golem_num) or 0))
end

function A:blast_path(target, ordinary_cost, stock)
    if self.cfg.autoMiningTnt==false then return end
    local grid=mining.block_list;local pickaxe=current_pickaxe()
    local max=tonumber(self.cfg.maxTargetPath) or 90
    local heap,seen,kinds={},{},{};local visited=0
    local threshold=tonumber(self.cfg.miningTntMinSaving) or 2
    local pick_cost=1+(tonumber(mining.golem_num) or 0)
    local node_limit=ordinary_cost and 512 or math.max(2000,max*160)
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
        if x==target.x and y==target.y then return end
        local kind=block_at(grid,x,y)
        if kind==nil then return end
        if is_free_chest(kind) and next(self.failed) and self:is_failed(x,y) then return end
        local pass=kinds[kind]
        if pass==nil then
            local blast=route_tnt_cost(kind,self.cfg)
            pass=blast or is_empty(kind) or is_free_chest(kind)
                or can_dig(kind,pickaxe,self.cfg.maxDigTime,self.cfg.allowLongOre) or false
            kinds[kind]=pass
        end
        if not pass then return end
        local blast=type(pass)=="number" and pass or nil
        local picks=(parent and parent.picks or 0)
            +((is_empty(kind) or is_free_chest(kind) or kind==PURPLE) and 0 or pick_cost)
        local tnt=(parent and parent.tnt or 0)+(blast or 0)
        local score=ordinary_cost and picks+tnt*threshold or nil
        if score and (score>ordinary_cost or tnt>stock) then return end
        local node={x=x,y=y,parent=parent,blast=blast,kind=kind,tnt=tnt,picks=picks,score=score,
            blasts=(parent and parent.blasts or 0)+(blast and 1 or 0),
            digs=(parent and parent.digs or 0)+(is_empty(kind) and 0 or 1)}
        if node.digs>max then return end
        local key=x..":"..y..(score and ":"..tnt or "")
        local previous=seen[key]
        if not previous or route_less(node,previous) then
            node.key=key;seen[key]=node;push(node)
        end
    end
    for _,dir in ipairs(DIRS)do offer(target.x+dir.x,target.y+dir.y,nil) end
    while #heap>0 and visited<node_limit do
        route_work()
        local node=pop()
        if seen[node.key]==node then
            visited=visited+1
            if is_empty(node.kind) then
                if ordinary_cost and node.tnt==0 then return end
                local path={};local cursor=node.parent
                while cursor do
                    path[#path+1]={x=cursor.x,y=cursor.y,blast=cursor.blast,kind=cursor.kind,free_chest=is_free_chest(cursor.kind)}
                    cursor=cursor.parent
                end
                return path,node.tnt
            end
            for _,dir in ipairs(DIRS)do offer(node.x+dir.x,node.y+dir.y,node) end
        end
    end
end
local function route_through_unloaded(self,target)
    local x,y=target.x,target.y
    local grid,pos=mining.block_list,mining.cur_position
    local max=tonumber(self.cfg.maxTargetPath) or 90
    local distance=math.abs(x-pos.x)+math.abs(y-pos.y)
    if distance>max-4 then
        for ey,row in pairs(grid) do
            for ex,kind in pairs(row) do
                route_work()
                if is_empty(kind) then
                    local d=math.abs(x-ex)+math.abs(y-ey)
                    if d<distance then pos={x=ex,y=ey};distance=d end
                end
            end
        end
    end
    if distance>max-4 then
        local horizon=math.max(4,math.min(24,max-4))
        x=pos.x+math.floor((x-pos.x)*horizon/distance+.5)
        y=pos.y+math.floor((y-pos.y)*horizon/distance+.5)
    end
    return find_path(grid,current_pickaxe(),x,y,max,self.cfg.maxDigTime,self.cfg.allowLongOre,false,true)
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
                    local reduces_penalty=target.golem==true and target.block_type==(BT.golem or 27)
                    local best_reduces=best and best.golem==true and best.block_type==(BT.golem or 27)
                    if not best or (reduces_penalty and not best_reduces)
                        or (reduces_penalty==best_reduces and target.manhattan<best.manhattan) then
                        target.path={};target.route_tnt=0;target.route_cost=0;best=target
                    end
                    break
                end
            end
        end
    end
    if best then return best end
    local stock=self.cfg.autoMiningTnt and mining_tnt_stock() or 0
    for _,target in ipairs(targets)do
      if target.rank==rank and (not available or not target.deferred) then
        local path,tnt=nil,0
        if not target.needs_snapshot and not target.deferred then
            path=find_path(mining.block_list,current_pickaxe(),target.x,target.y,
                self.cfg.maxTargetPath,self.cfg.maxDigTime,self.cfg.allowLongOre,false)
        end
        if path then
            local ordinary_cost=route_picks(path)
            if stock>0 and ordinary_cost>=(tonumber(self.cfg.miningTntMinSaving) or 2) then
                local shortcut,cost=self:blast_path(target,ordinary_cost,stock)
                if shortcut then path,tnt=shortcut,cost end
            end
        elseif not target.needs_snapshot and not target.deferred then
            local known_blast,known_tnt
            if target.golem and self.cfg.autoMiningTnt then
                known_blast,known_tnt=self:blast_path(target)
            end
            if known_blast and known_tnt<=stock then
                path,tnt=known_blast,known_tnt
            else
                -- An unloaded map edge is still a possible ordinary route.
                path=route_through_unloaded(self,target)
                if not path then
                    if target.golem then path,tnt=known_blast,known_tnt
                    else path,tnt=self:blast_path(target) end
                end
            end
        end
        target.path=path;target.route_tnt=tnt or 0
        target.route_cost=path and route_picks(path) or math.huge
        if not best or target.rank<best.rank or (target.rank==best.rank and
            ((best.deferred and not target.deferred) or (target.deferred==best.deferred and
            (target.route_cost<best.route_cost or (target.route_cost==best.route_cost
                and ((target.route_tnt or 0)<(best.route_tnt or 0)
                    or (target.route_tnt==best.route_tnt and target.manhattan<best.manhattan))))))) then best=target end
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
    if #targets==0 then self._route_job=nil;return nil,false end
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
        tostring(self.cfg.maxTargetPath),tostring(self.cfg.maxDigTime),tostring(self.cfg.allowLongOre),
        tostring(mining.pickaxe_id),tostring(self.cfg.autoMiningTnt),tostring(self.cfg.miningTntMinSaving),
        tostring(mining.golem_num),tostring(self.cfg.autoMiningTnt and mining_tnt_stock() or 0)}
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

function A:wait_for_golem_route()
    self.status="等待魔像地形，暂停普通挖掘"
    self.detail="先清理场上魔像，避免每次挖掘额外扣镐"
    if self.now<(self._golem_region_at or 0) then return end
    self._golem_region_at=self.now+1
    local target=self:seek_virtual_target(true)
    if target and target.golem then self:load_target_route(target,nil);return end
    pcall(function()mining:QueryBlockInfo()end)
end

local function exploration_anchor(self)
    local tip=self.exploration_tip
    if tip and is_empty(block_at(mining.block_list,tip.x,tip.y)) then return tip end
    return mining.cur_position
end

function A:exploration_depth_ready()
    return self:automatic_mining_depth_ready()
        or exploration_anchor(self).y>=self:automatic_mining_start_depth()
end

local extra_offsets={
    {1,1,1},{-1,1,1},{1,-1,1},{-1,-1,1},
    {2,0,2},{-2,0,2},{0,2,2},{0,-2,2},
}

local function remember_vision(self,grid,tile)
    local vision=self.exploration_vision
    if vision and (vision.samples>=8 or vision.radius==2) then return end
    local offsets={}
    for _,offset in ipairs(extra_offsets) do
        if block_at(grid,tile.x+offset[1],tile.y+offset[2])==nil then offsets[#offsets+1]=offset end
    end
    if #offsets>0 then self.exploration_observation={x=tile.x,y=tile.y,offsets=offsets} end
end

function A:observe_mining_vision()
    local sample,grid=self.exploration_observation,mining.block_list
    self.exploration_observation=nil
    if block_at(grid,sample.x,sample.y)~=EMPTY then return end
    local vision=self.exploration_vision or {radius=0,samples=0}
    for _,offset in ipairs(sample.offsets) do
        if block_at(grid,sample.x+offset[1],sample.y+offset[2])~=nil then
            vision.radius=math.max(vision.radius,offset[3])
        end
    end
    vision.samples=vision.samples+1
    self.exploration_vision=vision
end

local function vision_gain(grid,x,y,radius)
    local row,above,below=grid[y],grid[y-1],grid[y+1]
    local gain=(row[x-1]==nil and 1 or 0)+(row[x+1]==nil and 1 or 0)
        +((not above or above[x]==nil) and 1 or 0)+((not below or below[x]==nil) and 1 or 0)
    if radius>=1 then
        gain=gain+(above and ((above[x-1]==nil and 1 or 0)+(above[x+1]==nil and 1 or 0)) or 2)
            +(below and ((below[x-1]==nil and 1 or 0)+(below[x+1]==nil and 1 or 0)) or 2)
    end
    if radius==2 then
        local far_above,far_below=grid[y-2],grid[y+2]
        gain=gain+(row[x-2]==nil and 1 or 0)+(row[x+2]==nil and 1 or 0)
            +((not far_above or far_above[x]==nil) and 1 or 0)+((not far_below or far_below[x]==nil) and 1 or 0)
    end
    return gain
end

function A:estimated_reveal(grid,x,y)
    return vision_gain(grid,x,y,self.exploration_vision and self.exploration_vision.radius or 0)
end

local function next_step_gain(grid, tile, pickaxe, max_time, allow_long, radius)
    local gain=0
    for _,dir in ipairs(DIRS) do
        local x,y=tile.x+dir.x,tile.y+dir.y
        local kind=block_at(grid,x,y)
        if kind and can_dig(kind,pickaxe,max_time,allow_long) and not adjacent_empty(grid,x,y) then
            gain=math.max(gain,vision_gain(grid,x,y,radius))
        end
    end
    return gain
end

function A:exploration_shortcut(anchor, target_depth, row, normal, shallow_min, shallow_max)
    if not self.cfg.autoMiningTnt then return end
    local grid,pickaxe=mining.block_list,current_pickaxe()
    local max_time,allow_long=self.cfg.maxDigTime,self.cfg.allowLongOre
    local stock
    local threshold=tonumber(self.cfg.miningTntMinSaving) or 2
    local ordinary_cost=1+(tonumber(mining.golem_num) or 0)
    for _,dir in ipairs(DIRS) do
        local forward=(target_depth and dir.y==1) or shallow_min
            or (not target_depth and anchor.y==row and dir.y==0)
        if forward then
            local x,y=anchor.x+dir.x,anchor.y+dir.y
            local kind=block_at(grid,x,y)
            local cost=route_tnt_cost(kind,self.cfg)
            if cost then
                stock=stock or mining_tnt_stock()
                if stock<=0 then return end
                local gx,gy=x+dir.x,y+dir.y
                local goal_kind=block_at(grid,gx,gy)
                if cost<=stock and not adjacent_empty(grid,gx,gy) and can_dig(goal_kind,pickaxe,max_time,allow_long)
                    and (not target_depth or gy<=target_depth)
                    and (not shallow_min or (y>=shallow_min and y<=shallow_max and gy>=shallow_min and gy<=shallow_max))
                    and (not normal or dir.y==1 or (normal.x-anchor.x)*dir.x>=0)
                    and unknown_neighbor_count(grid,gx,gy)>0 then
                    local route=find_path(grid,pickaxe,gx,gy,16,max_time,allow_long,false,false,96)
                    local blast_picks=kind==PURPLE and 0 or ordinary_cost
                    if route and route_picks(route)-blast_picks>=cost*threshold then
                        return {x=x,y=y}, {label=target_depth and "下潜通路" or "横向通路"}
                    end
                end
            end
        end
    end
end

-- Cells added by the second step only. The first reveal has already been
-- counted. There are at most five cells to inspect in each direction.
local continuation_caps={}
local function continuation_offsets(radius)
    if continuation_caps[radius] then return continuation_caps[radius] end
    local function visible(dx,dy)
        if radius==1 then return math.max(math.abs(dx),math.abs(dy))<=1 end
        return math.abs(dx)+math.abs(dy)<=(radius==2 and 2 or 1)
    end
    local caps={}
    for i,d in ipairs(DIRS) do
        local offsets={}
        for dy=-3,3 do
            for dx=-3,3 do
                if not visible(dx,dy) and visible(dx-d.x,dy-d.y) then
                    offsets[#offsets+1]={x=dx,y=dy}
                end
            end
        end
        caps[i]=offsets
    end
    continuation_caps[radius]=caps
    return caps
end

local function frontier_profile(grid,tile,profile)
    if tile.profile==profile then return tile.gain,tile.continuation end
    local radius=profile.radius
    local gain=vision_gain(grid,tile.x,tile.y,radius)
    local caps=continuation_offsets(radius)
    local best=0
    for i,d in ipairs(DIRS) do
        local x,y=tile.x+d.x,tile.y+d.y
        if y>=profile.min_y and y<=profile.max_y then
            local kind=block_at(grid,x,y)
            local weight
            if kind==nil then
                weight=0.8  -- Unknown terrain may block the next cut.
            else
                weight=profile.weights[kind]
                if weight==nil then
                    if is_free_chest(kind) then weight=2.5  -- Checked before opening.
                    elseif can_dig(kind,profile.pickaxe,profile.max_time,profile.allow_long) then weight=1
                    else weight=0 end
                    profile.weights[kind]=weight
                end
            end
            if weight>0 then
                local extra=0
                for _,offset in ipairs(caps[i]) do
                    if block_at(grid,tile.x+offset.x,tile.y+offset.y)==nil then extra=extra+1 end
                end
                best=math.max(best,extra*weight)
            end
        end
    end
    tile.profile=profile;tile.gain=gain;tile.continuation=best
    return gain,best
end

local function better_frontier(tile,other)
    return not other or tile.score>other.score or (tile.score==other.score
        and (tile.x<other.x or (tile.x==other.x and tile.y<other.y)))
end

function A:mine_exploration(target_depth, shallow_min, shallow_max, index)
    local grid,anchor=mining.block_list,exploration_anchor(self)
    local row=anchor.y
    local layered=not shallow_min and not self.cfg.horizontalMining
    local next_depth
    if not target_depth and not shallow_min then
        self.exploration_depth=self.exploration_depth or row
        row=self.exploration_depth
        if layered then
            local max_depth=tonumber(self.cfg.layerMaxDepth) or 60
            next_depth=math.min(max_depth,row+(tonumber(self.cfg.layerStep) or 10))
            if self.layer_target and self.layer_target>row then
                next_depth=math.min(max_depth,math.max(next_depth,self.layer_target))
            end
            if next_depth<=row then next_depth=nil end
        end
    end
    local ranked,backup,descend
    local max_time,allow_long=self.cfg.maxDigTime,self.cfg.allowLongOre
    local pickaxe=current_pickaxe()
    local radius=self.exploration_vision and self.exploration_vision.radius or 0
    local avoid_gems=not target_depth and not layered and self.cfg.rushAvoidGems
    local limit=target_depth or shallow_max or (layered and math.max(row,next_depth or row)) or math.huge
    local min_y=shallow_min or -math.huge
    index=index or self:index_mining_grid(grid)
    local profile=index.profile
    if not profile or profile.radius~=radius or profile.pickaxe~=pickaxe or profile.max_time~=max_time
        or profile.allow_long~=allow_long or profile.max_y~=limit or profile.min_y~=min_y
        or profile.avoid_gems~=avoid_gems then
        profile={radius=radius,pickaxe=pickaxe,max_time=max_time,allow_long=allow_long,
            max_y=limit,min_y=min_y,avoid_gems=avoid_gems,times={},weights={}}
        index.profile=profile
    end
    local direction=self.layer_dir or 1
    for y,edges in pairs(index.edges) do
        if y>=min_y and y<=limit then
            for _,tile in pairs(edges) do
                local kind=tile.block_type
                local seconds=profile.times[kind]
                if seconds==nil then
                    seconds=can_dig(kind,pickaxe,max_time,allow_long)
                        and not (avoid_gems and is_gem_block(kind)) and dig_time(kind,pickaxe) or false
                    profile.times[kind]=seconds
                end
                if seconds then
                    local dx,dy=tile.x-anchor.x,y-anchor.y
                    local dy_row=math.abs(y-row)
                    local gain,future=frontier_profile(grid,tile,profile)
                    local distance=math.abs(dx)+math.abs(dy)
                    local score=(gain+future*0.55)*12-math.min(distance,100)*0.04-seconds*0.01
                    local forward=dx*direction>0
                    if target_depth then
                        score=score+(dy>0 and 80+dy*5 or (dy<0 and -50 or 0))-math.abs(dx)*0.4
                    elseif shallow_min then
                        score=score-(y-shallow_min)*0.02
                    elseif layered then
                        score=score+(forward and 1 or 0)
                    else
                        score=score-dy_row*4+(forward and 3 or 0)
                    end
                    tile.score=score
                    if better_frontier(tile,backup) and (not shallow_min or mining:CheckConnected(tile.x,y)) then
                        backup=tile
                    end
                    if layered and not target_depth then
                        if y==row and better_frontier(tile,ranked) then ranked=tile end
                        if next_depth and y>anchor.y and better_frontier(tile,descend) then descend=tile end
                    elseif not shallow_min and (target_depth or dy_row<=3) and better_frontier(tile,ranked) then
                        ranked=tile
                    end
                end
            end
        end
    end
    if ranked and ranked.gain==0 and not target_depth and next_step_gain(grid,ranked,pickaxe,max_time,allow_long,radius)==0 then
        if layered and descend then ranked=nil
        elseif backup and backup.gain>0 then ranked=nil end
    end
    local best=ranked or descend or backup
    local shortcut,goal=self:exploration_shortcut(anchor,target_depth,row,best,shallow_min,shallow_max)
    if shortcut and self:blast_target_path(shortcut,goal) then
        if self._mine_blast then self.exploration_tip=shortcut end
        return true
    end
    if not best then
        self.status="没有可挖的方块"
        self.detail="等待可挖掘的通路"
        return false
    end
    local hint=target_depth and ("自动下潜至第"..target_depth.."层")
        or (shallow_min and "浅层开拓" or (layered and "分层开拓" or "横向推进"))
    remember_vision(self,grid,best)
    if self:dig(best.x,best.y,hint) then
        self.exploration_tip=best
        if layered and not target_depth then
            if next_depth and best.y==next_depth then
                self.exploration_depth=next_depth;self.layer_target=nil
            elseif best.y==row then
                self.last_tunnel_y=row
                self.layer_dir=best.x>=anchor.x and 1 or -1
            end
        elseif not target_depth and not shallow_min and math.abs(best.y-row)>3 then
            self.exploration_depth=best.y
        end
        return true
    end
    self.exploration_observation=nil
    return false
end

function A:mine_for_workshop_then_explore(index)
    local _,required=self:configured_workshop_project()
    local actual=self:workshop_mining_depth()
    local min_depth,max_depth=20,60
    if required and actual<required then
        if mining.cur_position.y>=required then
            self.status="等待确认工坊深度"
            self.detail="生产档位需要 "..required.." 层"
            if self.now>=(self._workshop_depth_query_at or 0) then
                self._workshop_depth_query_at=self.now+2
                pcall(function()mining:QueryBlockInfo()end)
            end
        else
            self:mine_exploration(required,nil,nil,index)
        end
        return
    end

    local virtual_target=self:seek_virtual_target()
    if virtual_target then
        if virtual_target.golem then self:load_target_route(virtual_target,nil);return end
        if self:mine_frontier(virtual_target) then return end
    end

    if self:mine_exploration(nil,min_depth,max_depth,index) then return end
    local anchor=exploration_anchor(self)
    if anchor.y<min_depth and self:mine_exploration(min_depth,nil,nil,index) then return end
    self.status="寻找 20–60 层的可用矿道"
    self.detail="工坊深度已满足，正在核对浅层通路"
    if self.now<(self._shallow_query_at or 0) then return end
    self._shallow_query_at=self.now+2
    pcall(function()
        if type(mining.RequestMiningRegion)=="function" then
            mining:RequestMiningRegion({x=anchor.x,y=math.floor((min_depth+max_depth)/2)},40,22,true)
        else mining:QueryBlockInfo() end
    end)
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
    if self.cfg.autoMiningTnt==false then return false end
    local kind=mining:GetBlockType(tile.x,tile.y)
    local cost=route_tnt_cost(kind,self.cfg)
    local available=mining_tnt_stock()
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
        if state.phase=="ready_tnt" and self.cfg.autoMiningTnt==false then
            if (tonumber(mining.dig_endtime) or 0)>time_logic:Now()+0.01 then
                self.status="自动雷管已关闭，等待当前矿石挖完";return true
            end
            state.phase="collect"
        end
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
end
