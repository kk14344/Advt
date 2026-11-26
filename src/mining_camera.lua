function A:set_mining_route(target,step,label)
    if not self.cfg.showMiningRoute then return end
    target=target or step
    if not target then self.mining_route_preview=nil;return end
    local current=self.mining_route_preview
    label=label or target.label or "探索"
    if current and current.x==target.x and current.y==target.y and current.label==label
        and current.nextX==(step and step.x) and current.nextY==(step and step.y)
        and current.blast==(step and step.blast) then return end
    self.mining_route_preview={x=target.x,y=target.y,nextX=step and step.x,nextY=step and step.y,label=label,blast=step and step.blast}
end

function A:mining_wait_view()
    local fight=self.fight
    if fight then
        local label=fight.label or "矿区目标"
        local operation=fight.phase=="query" and ("查询"..label) or (fight.chest and ("开启"..label) or ("挑战"..label))
        return {operation=operation,seconds=math.floor(math.max(0,self.now-(fight.wait_started or fight.at or self.now))),
            state=fight.transport and fight.transport.queued and "等待发送" or "等待游戏确认"}
    end
    local dig=self.pending_dig
    if dig then return {operation="挖掘 ("..dig.x..", "..dig.y..")",seconds=math.floor(math.max(0,self.now-dig.at)),
        state=self._mine_sync and "核对矿区状态" or "等待游戏确认"} end
end

function A:update_mining_route_overlay(scene)
    local route=self.cfg.enabled and self.cfg.showMiningRoute and self.mining_route_preview
    local overlay=self._route_overlay
    if overlay and (not route or overlay.scene~=scene) then
        pcall(function()overlay.node:removeFromParent()end)
        self._route_overlay=nil;overlay=nil
    end
    if not route or not scene or not scene.mini_scene_node
        or not cc or not cc.DrawNode or self._route_overlay_failed==scene then return end
    local previous=overlay and overlay.route
    if previous and previous.x==route.x and previous.y==route.y
        and previous.nextX==route.nextX and previous.nextY==route.nextY
        and previous.blast==route.blast then return end
    local ok=pcall(function()
        if not overlay then
            overlay={node=cc.DrawNode:create(),scene=scene};self._route_overlay=overlay
            scene.mini_scene_node:addChild(overlay.node,6)
        end
        local x0=scene:ConvertToUISpace(0,0)
        local x1=scene:ConvertToUISpace(1,0)
        local pitch=math.abs(x1-x0)/(tonumber(scene.block_scale) or 1)
        if pitch<=0 then return end
        local function outline(x,y,inset,color)
            local cx,cy=x*pitch,-y*pitch
            local half=pitch/2-inset
            overlay.node:drawRect(cc.p(cx-half,cy-half),cc.p(cx+half,cy+half),color)
        end
        overlay.node:clear()
        outline(route.x,route.y,2,cc.c4f(1,0.83,0.28,1))
        if route.nextX then
            outline(route.nextX,route.nextY,5,route.blast and cc.c4f(1,0.46,0.21,1) or cc.c4f(0.32,1,0.76,1))
        end
        overlay.route=route
    end)
    if not ok then
        self._route_overlay_failed=scene
        self:update_mining_route_overlay(nil)
    end
end

function A:follow_mining_camera()
    if not self.cfg.enabled or self.inBattle
        or (self.daily_state and self.daily_state.running) or not mining.cur_position then return false end
    if self.cfg.followMiningCamera==false and not self.cfg.showMiningRoute and not self._route_overlay then return false end
    local ok,context=pcall(function()return self:read_context()end)
    if not ok or not context or context.sub_name~="mining_district_sub_scene" then
        if self._route_overlay then self:update_mining_route_overlay(nil) end
        return false
    end
    local scene=context.sub_scene;local position=mining.cur_position
    if self.cfg.showMiningRoute or self._route_overlay then self:update_mining_route_overlay(scene) end
    if self.cfg.followMiningCamera==false then return false end
    if not scene or type(scene.MoveTo)~="function" or self._camera_failed_scene==scene then return false end
    if (tonumber(scene.touch_num) or 0)>0 or scene.is_moving_camera or scene.is_scaling_camera then
        self._camera_resume_at=self.now+2;self._camera_position=nil;return false
    end
    if self.now<(self._camera_resume_at or 0) or self.now<(self._camera_next_at or 0) then return false end
    local previous=self._camera_position
    if previous and previous.scene==scene and previous.x==position.x and previous.y==position.y then return true end
    self._camera_next_at=self.now+0.15
    -- Native MoveTo updates the visual viewport; it does not request digging,
    -- use tools, change formation, change the current position or wait on tasks.
    local moved=pcall(function()
        -- One tile lower on screen than the native anchor, toward the center.
        -- Only offset the viewport argument; preserve real mining coordinates.
        scene:MoveTo({x=position.x,y=position.y-1})
        local text=scene.ui_root and scene.ui_root.coordinate_text
        if text and type(text.setString)=="function" then text:setString("X:"..tostring(position.x).." Y:"..tostring(position.y)) end
    end)
    if not moved then self._camera_failed_scene=scene;return false end
    self._camera_position={scene=scene,x=position.x,y=position.y}
    return true
end
