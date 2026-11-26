-- A missed draw/receive reply or hidden animation can leave native visual busy
-- flags set. Re-read the server bag first, then obtain every loadout and lock
-- before upgrading. Never clear the login barrier or infer equipped materials.
local rune_control_previous=A.rune_control_step
function A:rune_control_step(state,rune)
    local p=self._task_pending
    if p and p.kind=="rune_recovery_bag" then
        if not p.response then return reply(self,"正在重新同步符文背包，恢复出战页查询",{wait=true}) end
        local msg=p.response
        self._task_pending=nil
        local valid=(not msg.result or msg.result=="success" or msg.result==0)
            and (type(msg.rune_list)=="table" or tonumber(msg.rune_bag_level)~=nil)
        if not valid then
            state._control_recover_at=self.now+2
            return reply(self,"符文快照未完整返回，等待重新同步",{wait=true,delaySeconds=1})
        end
        pcall(function()rune:SetDrawState(false)end)
        pcall(function()rune:SetIsMovingState(false)end)
        rune.is_drawing=false;rune.is_moving=false
        rune.rune_query_pending=false;rune.rune_query_needed=true
        state.loaded=true;state.control_loaded=false;state._control_wait_since=nil
        state._control_recover_at=self.now+2
        self:log("已重新读取符文背包，继续核对出战页与锁定状态")
    end
    p=self._task_pending
    local writing=p and p.kind~="growth_rune_loadouts"
    local blocked=rune.is_drawing or rune.is_moving or rune.rune_query_pending or rune.rune_query_needed
        or rune.rune_control_pending or (tonumber(rune.rune_native_pending) or 0)>0
    if not writing and blocked then
        state._control_wait_since=state._control_wait_since or self.now
        if self.now-state._control_wait_since>=10 and self.now>=(state._control_recover_at or 0) then
            local user=package.loaded["logic.user"]
            if type(user)=="table" and type(user.query_queue)=="table" and #user.query_queue>0 then
                return reply(self,"等待游戏原生登录队列完成后同步符文",{wait=true,delaySeconds=0.5})
            end
            self._task_pending=nil
            self:task_request("query_rune_info_ret","rune_recovery_bag",function()return network:Send({query_rune_info={}})end)
            state._control_recover_at=self.now+2
            return reply(self,"符文查询等待过久，自动重新读取背包与出战页",{wait=true})
        end
    elseif not blocked then state._control_wait_since=nil end
    return rune_control_previous(self,state,rune)
end
