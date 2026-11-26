-- The actual assistant drives the model through A:step(). This fixture defines
-- synthetic server behavior for discovery, excavation, fights and explosions.
MINE={block_list={},cur_position={x=0,y=0},pickaxe_id=1}
constants={
    BLOCK_TYPE={empty=0,rock_purple_gold=60,chest1=40,chest7=48},
    BLOCK_COLLECT_TYPE={golem=2,chest=5},
    MINING_BOSS_MAP={},RANDOM_EVENT_TYPE={treasure_hunt=1},
    RESOURCE_TYPE={tnt=9}
}
local function block(id, level, collect, seconds, tnt)
    return {ID=id,destroy_level=level,collect_type=collect,need_time1=seconds,tnt_count=tnt or 0}
end
config_manager={
    mining_dig_info_config={
        [0]=block(0,0,0,0),[1]=block(1,1,0,0.2),[2]=block(2,0,0,0),
        [8]=block(8,1,1,1200,2),[60]=block(60,99,0,0,1),
        [27]=block(27,1,2,0),[28]=block(28,1,3,0),
        [40]=block(40,1,5,0),[48]=block(48,1,5,0),
    },
    mining_pickaxe_config={[1]={destroy_level=9,efficiency=1}}
}
package.loaded['logic.mining']=MINE
package.loaded['logic.config_manager']=config_manager
package.loaded['util.constants']=constants
package.loaded['logic.time']={Now=function()return __MWA and __MWA.now or 0 end}
package.loaded['logic.resource']={GetResourceNum=function(_,id)return id==9 and MINE.stock or 0 end}
for _,name in ipairs({'logic.graphic','logic.user','logic.bag','scene.scene_manager','util.network',
    'util.utils','logic.mine','logic.cultivation','util.client_constants'}) do
    package.loaded[name]={}
end

function MINE:GetBlockType(x,y)
    return self.block_list[y] and self.block_list[y][x]
end
function MINE:GetDigTime() return 0,0 end
function MINE:QueryBlockInfo() end

-- @LOAD_ASSISTANT@
local A=__MWA
local width,height=180,200
local dirs={{1,0},{-1,0},{0,1},{0,-1}}
local cross={{0,0},{1,0},{-1,0},{0,1},{0,-1}}
local square={{0,0},{1,0},{-1,0},{0,1},{0,-1},{1,1},{1,-1},{-1,1},{-1,-1}}
local diamond={{0,0},{1,0},{-1,0},{0,1},{0,-1},{1,1},{1,-1},{-1,1},{-1,-1},{2,0},{-2,0},{0,2},{0,-2}}
local world,mask,counters,spawn_interval,seed,spawning

local function terrain(x,y)
    if x<1 or x>width or y<0 or y>height then return 2 end
    local row=world[y]
    if not row then row={};world[y]=row end
    if not row[x] then
        -- Use the same 32-bit map in Fengari and native Lua 5.3.
        local key=(seed ~ (x*73856093) ~ (y*19349663)) & 0xffffffff
        key=((key ~ (key >> 16))*0x45d9f3b) & 0xffffffff
        key=((key ~ (key >> 16))*0x45d9f3b) & 0xffffffff
        local fraction=((key ~ (key >> 16)) & 0x7fffffff)/2147483648
        local kind=1
        if fraction<0.06 then kind=2
        elseif fraction<0.09 then kind=60
        elseif fraction<0.12 then kind=8
        elseif fraction<0.135 then kind=40
        elseif fraction<0.139 then kind=48
        elseif spawning==1 and fraction<0.166 then kind=27
        elseif spawning==1 and fraction<0.179 then kind=28 end
        row[x]=kind
    end
    return row[x]
end

local function reveal(x,y)
    local count=0
    for _,offset in ipairs(mask) do
        local nx,ny=x+offset[1],y+offset[2]
        local row=MINE.block_list[ny]
        if not row then row={};MINE.block_list[ny]=row end
        if row[nx]==nil then
            local kind=terrain(nx,ny)
            row[nx]=kind
            if kind==27 then MINE.golem_num=MINE.golem_num+1 end
            count=count+1
        end
    end
    return count
end

function MINE:CheckConnected(x,y)
    for _,d in ipairs(dirs) do
        if self:GetBlockType(x+d[1],y+d[2])==0 then return true end
    end
    return false
end
function A:read_context() return {sub_name='mining_district_sub_scene'} end
function A:mining_ready() return true end
function A:try_refuse_event() return false end
function A:try_use_mining_bag() return false end
function A:try_cave_boss() return false end
function A:try_collect() return false end
function A:log() end

local function clear(x,y,tnt,picks)
    if MINE.dig_count<picks or MINE.stock<tnt or not MINE:CheckConnected(x,y) then return false end
    MINE.dig_count=MINE.dig_count-picks
    MINE.stock=MINE.stock-tnt
    MINE.block_list[y][x]=0
    world[y][x]=0
    MINE.cur_position={x=x,y=y}
    MINE.dig_depth=math.max(MINE.dig_depth,y)
    MINE.stream_mutation=MINE.stream_mutation+1
    local gain=reveal(x,y)
    counters.revealed=counters.revealed+gain
    counters.actions=counters.actions+1
    if picks>0 then
        counters.penalty=counters.penalty+picks-1
        counters.excavations=counters.excavations+1
        if y>=20 and y<=60 then counters.shallowPicks=counters.shallowPicks+picks end
        if gain==0 then counters.noVision=counters.noVision+1 end
        if spawning==2 and counters.excavations%spawn_interval==0 then
            local kind=counters.excavations%(spawn_interval*3)==0 and 28 or 27
            for _,d in ipairs(dirs) do
                local nx,ny=x+d[1],y+d[2]
                if MINE:GetBlockType(nx,ny)==1 then
                    MINE.block_list[ny][nx]=kind
                    world[ny][nx]=kind
                    if kind==27 then MINE.golem_num=MINE.golem_num+1 end
                    break
                end
            end
        end
    end
    A.last_action_at=A.now
    return true
end

function A:dig(x,y)
    local kind=MINE:GetBlockType(x,y)
    if kind~=1 then return false end
    return clear(x,y,0,1+MINE.golem_num)
end
function A:start_target(target)
    local kind=MINE:GetBlockType(target.x,target.y)
    if kind~=27 and kind~=28 and kind~=40 and kind~=48 then return false end
    if not clear(target.x,target.y,0,0) then return false end
    if kind==27 then counters.golems=counters.golems+1;MINE.golem_num=MINE.golem_num-1 end
    if kind==28 then counters.nightmares=counters.nightmares+1 end
    return true
end
function A:blast_target_path(tile)
    local kind=MINE:GetBlockType(tile.x,tile.y)
    if kind~=60 and kind~=8 then return false end
    return clear(tile.x,tile.y,kind==60 and 1 or 2,kind==60 and 0 or 1+MINE.golem_num)
end

function bench_run(map_seed,vision,mode,spawn_mode,budget,tnt_budget)
    seed,spawning=map_seed,spawn_mode
    spawn_interval=9+map_seed%7
    world={}
    mask=({cross,square,diamond})[vision]
    counters={golems=0,nightmares=0,excavations=0,noVision=0,penalty=0,revealed=0,actions=0,shallowPicks=0}
    tnt_budget=tnt_budget or 12
    MINE.block_list={};MINE.stream_mutation=0;MINE.golem_num=0;MINE.stock=tnt_budget
    MINE.dig_count=budget;MINE.dig_max_count=budget
    local workshop_layer=mode==3 and 35 or 120
    MINE.cur_position={x=math.floor(width/2),y=mode==1 and 6 or workshop_layer}
    MINE.dig_depth=MINE.cur_position.y
    config_manager.mining_quarry_config={
        {ID=1,need_layer=0,need_time=120},{ID=4,need_layer=35,need_time=120},
        {ID=7,need_layer=120,need_time=120},
    }
    local x,y=MINE.cur_position.x,MINE.cur_position.y
    world[y]={[x]=0}
    if mode==2 then
        -- Restore the same already-open access shaft for both versions.
        for row=20,y-1 do world[row]={[x]=0} end
        for row=20,y-1 do reveal(x,row) end
    end
    reveal(x,y)
    A.cfg.enabled=true;A.cfg.autoBoss=false;A.cfg.autoChest=false;A.cfg.actionInterval=0
    A.cfg.autoMiningTnt=tnt_budget>0
    A.cfg.horizontalMining=mode~=3
    A.cfg.horizontalStartDepth=mode==3 and 35 or 150
    A.cfg.workshopDepth=mode==3 and 35 or 150
    A.now=0;A.status="";A.failed={};A.seen_golems=nil;A._route_job=nil;A._route_query_at=nil
    A.last_action_at=nil;A.last_stall_at=nil;A.last_descent_log=nil;A.last_rush_log=nil
    A.horizontal_started=nil;A.exploration_tip=nil;A.exploration_depth=nil
    A.exploration_observation=nil;A.exploration_vision=nil;A._shallow_query_at=nil;A._golem_region_at=nil
    A.layer_target=nil;A.last_tunnel_y=nil;A.layer_dir=nil;A.blocked_until=nil
    local idle,ticks,stalled=0,0,0
    while ticks<budget*5 do
        A.now=A.now+0.25
        ticks=ticks+1
        local actions=counters.actions
        local started=bench_clock()
        A:step()
        bench_tick(bench_clock()-started)
        if actions==counters.actions then idle=idle+1 else idle=0 end
        if MINE.dig_count==0 and idle>=4 then break end
        if idle>=18 then
            if MINE.dig_count>=1+MINE.golem_num then stalled=1 end
            break
        end
    end
    return table.concat({budget-MINE.dig_count,counters.golems,counters.nightmares,counters.revealed,
        counters.penalty,counters.noVision,tnt_budget-MINE.stock,counters.actions,ticks,stalled,counters.shallowPicks,
        MINE.dig_depth>=workshop_layer and 1 or 0}," ")
end
