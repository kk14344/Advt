local FRAME_SECONDS=0.0166667
local SPEEDS={[100]=true,[125]=true,[150]=true,[200]=true}
local REPLAY_SPINES={
    "start_spine_node","skill_name_spine_node","skill_bp1_spine_node","skill_bp2_spine_node",
    "left_rune_spine_node","right_rune_spine_node",
}

function A:normalize_replay_speed(value)
    return SPEEDS[value] and value or 100
end

local function change_spine_speed(room,speed)
    for _,name in ipairs(REPLAY_SPINES) do
        local node=room[name]
        if node and type(node.setTimeScale)=="function" then
            node:setTimeScale(speed)
        end
    end
    for _,node in ipairs(room.rune_node_list or {}) do
        if type(node.setTimeScale)=="function" then node:setTimeScale(speed) end
    end
end

function A:install_battle_replay_speed()
    local room=package.loaded["scene.battle_room"]
    if not room or room.Update==room._mwa_replay_update
        or type(room.Update)~="function" or type(room.Show)~="function"
        or type(room.IsVisible)~="function" then return end

    local show,update=room.Show,room.Update
    local timing
    room.Show=function(scene,...)
        timing=nil
        return show(scene,...)
    end

    local wrapped=function(scene,elapsed,...)
        local active=__MWA
        if not active or not scene:IsVisible() then
            timing=nil
            return update(scene,elapsed,...)
        end
        local speed=active:normalize_replay_speed(active.cfg.battleReplaySpeed)
        if speed==100 and not timing then return update(scene,elapsed,...) end

        timing=timing or {speed=100,fraction=0}
        if speed~=timing.speed then
            timing.speed=speed
            timing.fraction=0
            change_spine_speed(scene,speed/100)
        end
        if speed==100 or type(elapsed)~="number" or elapsed<=0 then
            return update(scene,elapsed,...)
        end

        -- The native battle room caps each call to one simulation step.
        -- Run whole steps to preserve the order of effects and callbacks.
        local current=timing
        local due=current.fraction+(math.min(elapsed,FRAME_SECONDS)/FRAME_SECONDS)*speed/100
        local steps=math.min(2,math.floor(due+0.000001))
        current.fraction=math.max(0,due-steps)
        for _=1,steps do
            update(scene,FRAME_SECONDS,...)
            if timing~=current or not scene:IsVisible() then break end
        end
    end
    room.Update=wrapped
    room._mwa_replay_update=wrapped
end
