-- Native ore-bag UI uses the same UseTool(resource_id,num) request as mining
-- tools, with batches of five. Chain accepted batches in the ACK callback;
-- query inventory at the beginning/end, not between every batch.
A.ORE_PACK_EVENTS={"query_resource_list_ret","use_mining_tool_ret"}
local ORE_IDS={25,26,27}
local ORE_KEYS={[25]="junior_tool",[26]="intermediate_tool",[27]="senior_tool"}
local ORE_LABELS={[25]="低级矿石包",[26]="中级矿石包",[27]="高级矿石包"}

function A:ore_pack_count(id,msg)
    if msg then return tonumber(msg[ORE_KEYS[id]]) end
    local st=self.orepacks_state
    if st and st.remaining and st.remaining[id]~=nil then return st.remaining[id] end
    return tonumber(resource_logic:GetResourceNum(id))
end
function A:close_ore_pack_rewards()
    self:hide_panel("reward_panel")
    self:hide_panel("single_reward_panel")
end
function A:start_orepacks()
    self:cancel_task()
    self.orepacks_state={running=true,remaining={},used=0,by_type={[25]=0,[26]=0,[27]=0}}
    self:install_task_hooks()
    self:close_ore_pack_rewards()
    self:log("开始一键使用全部低级、中级、高级矿石包：每批最多5个，按回执连续开启")
    return true
end
function A:orepacks_advance()
    local st=self.orepacks_state
    if not st or not st.running or st.done or self._task_pending or st.retry_at and task_now(self)<st.retry_at then return end
    if st.loaded then
        for _,id in ipairs(ORE_IDS)do
            local have=math.max(0,math.floor(st.remaining[id] or 0))
            if have>0 then
                local num=math.min(5,have)
                local p=self:task_request("use_mining_tool_ret","ore_open",function()
                    return network:Send({use_mining_tool={resource_id=id,num=num}})
                end)
                p.resource_id=id;p.num=num
                p.accept_response=function(msg)
                    if msg.result and msg.result~="success" and msg.result~=0 then return msg.resource_id==nil or tonumber(msg.resource_id)==id end
                    local n=tonumber(msg.num)
                    return tonumber(msg.resource_id)==id and n~=nil and n>0 and n<=num
                end
                return
            end
        end
    end
    st.verifying=st.loaded
    self:task_request("query_resource_list_ret","ore_inventory",function()return network:Send({query_resource_list={}})end)
end
function A:orepacks_on_response(name,msg)
    local st=self.orepacks_state;local p=self._task_pending
    if not st or not st.running or not p or not p.response then return end
    if p.kind=="ore_inventory" and name==p.event then
        if msg.result and msg.result~="success" and msg.result~=0 then return end
        local counts={}
        for _,id in ipairs(ORE_IDS)do
            local n=self:ore_pack_count(id,msg)
            if not n or n<0 then
                self._task_pending=nil;st.retry_at=task_now(self)+1;st.loaded=false
                return
            end
            counts[id]=math.floor(n)
        end
        self._task_pending=nil;st.remaining=counts;st.loaded=true;st.retry_at=nil
        if counts[25]+counts[26]+counts[27]==0 then
            st.done=true;st.finish_at=task_now(self)+0.2
        end
    elseif p.kind=="ore_open" and name==p.event then
        if msg.result and msg.result~="success" and msg.result~=0 then
            if tostring(msg.result):find("not_enough",1,true) then
                self._task_pending=nil;st.loaded=false;st.retry_at=task_now(self)+0.2
            end
            return
        end
        local num=tonumber(msg.num)
        if tonumber(msg.resource_id)~=p.resource_id or not num or num<1 or num>p.num then return end
        self._task_pending=nil
        st.remaining[p.resource_id]=math.max(0,(st.remaining[p.resource_id] or 0)-num)
        st.used=st.used+num;st.by_type[p.resource_id]=st.by_type[p.resource_id]+num
    else return end
    self:close_ore_pack_rewards()
    self:orepacks_advance()
end
function A:orepacks_step()
    local st=self.orepacks_state
    if not st then return reply(self,"矿石包任务已停止",{done=true}) end
    local barrier=self:task_barrier();if barrier then return barrier end
    if scene_manager:GetCurrentSceneName()~="world" then return reply(self,"等待进入游戏后开启矿石包",{wait=true}) end
    self:close_ore_pack_rewards()
    local p=self._task_pending
    if p and p.response then self:orepacks_on_response(p.event,p.response) end
    if st.done then
        if task_now(self)<st.finish_at then return reply(self,"矿石包已用完，正在关闭获得窗口",{wait=true}) end
        st.running=false
        return reply(self,"矿石包已全部用完：低级"..st.by_type[25].."、中级"..st.by_type[26].."、高级"..st.by_type[27].."个",{done=true,used=st.used})
    end
    self:orepacks_advance()
    p=self._task_pending
    return reply(self,p and p.kind=="ore_open" and ("批量开启"..ORE_LABELS[p.resource_id].." · 已使用"..st.used.."个") or "核对矿石包库存，自动关闭获得窗口",{wait=true})
end
