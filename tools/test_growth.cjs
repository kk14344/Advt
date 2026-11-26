module.exports=({run,test,toLua})=>{
run(`
bag_logic={capacity=20,item_list={}}
function bag_logic:UseItem(item_id) network:Send({use_item={item_id=item_id}}) end
package.loaded['logic.bag']=bag_logic
mining={dig_count=0,dig_max_count=20}
resource_counts={}
function resource_logic:GetResourceNum(id) return resource_counts[id] or 0 end
function resource_logic:CheckResourceNum(id,n) return (resource_counts[id] or 0)>=n end
constants.RESOURCE_TYPE.crystal=54
constants.RESOURCE_TYPE.fatecrystal=92
constants.RESOURCE_TYPE.tnt=5
constants.MAX_RUNE_BAG_MUILT_SELECT_NUM=10
config_manager.rune_draw_config={[1]={cost_num=10}}
config_manager.weapon_star_upgrade_config={
 [1]={[0]={cost_num=80,get_exp=1,crit_probability=2500,crit_get_exp=3,suc_min_exp=20}},
 [2]={[0]={cost_num=100,get_exp=1,crit_probability=2500,crit_get_exp=3,suc_min_exp=20}}}
rune={cur_draw_platform_id=1,rune_bag_capacity=25,rune_list={},rune_temporary_list={}}
function rune:SetDrawState(v)end
function rune:SetIsMovingState(v)end
function rune:GetRunePreviewLevelAndExp(target,list)
 local total=target.exp or 0
 for _,m in ipairs(list)do total=total+(m.template_info.out_exp or 0)+(m.exp or 0)end
 if total>=100 then return target.template_info.level_limit,total end
 return target.level,total
end
destiny={weapon_star_can_upgrade=true,weapon_star_info_list={{weapon_id=1,star_level=0,exp=0},{weapon_id=2,star_level=0,exp=10}}}
package.loaded['logic.rune']=rune
package.loaded['logic.destiny_weapon']=destiny
network.event_listener.query_rune_info_ret=function(msg)end
network.event_listener.draw_rune_ret=function(msg)
 rune.cur_draw_platform_id=1
 resource_counts[54]=math.max(0,(resource_counts[54] or 0)-100)
 rune.rune_temporary_list[#rune.rune_temporary_list+1]={rune_id=100+math.random(1,100000),level=1,exp=0,equip_pos=0,
  template_info={type=2,quality=5,level_limit=10,out_exp=10}}
end
network.event_listener.rune_one_key_receive_ret=function(msg)
 while #rune.rune_temporary_list>0 do table.insert(rune.rune_list,table.remove(rune.rune_temporary_list)) end
end
network.event_listener.upgrade_rune_ret=function(msg)
 if msg.result=='success' then
  for _,id in ipairs(msg.exp_rune_id_list or {})do
   for i,v in ipairs(rune.rune_list)do if v.rune_id==id then table.remove(rune.rune_list,i);break end end
  end
  for _,v in ipairs(rune.rune_list)do if v.rune_id==msg.rune_info.rune_id then v.level=msg.rune_info.level;v.exp=msg.rune_info.exp end end
 end
end
network.event_listener.query_weapon_star_info_ret=function(msg)
 if msg.weapon_star_info_list then destiny.weapon_star_info_list=msg.weapon_star_info_list end
end
network.event_listener.weapon_star_upgrade_ret=function(msg)
 if msg.result=='success' then
  for _,v in ipairs(destiny.weapon_star_info_list)do
   if v.weapon_id==msg.weapon_id then v.star_level=msg.star_level;v.exp=msg.exp end
  end
 end
end
network.event_listener.query_bag_info_ret=function(msg)
 if msg.item_list then bag_logic.item_list=msg.item_list end
 if msg.max_count then bag_logic.capacity=msg.max_count end
end
network.event_listener.use_item_ret=function(msg)
 if msg.result=='success' then
  for i,v in ipairs(bag_logic.item_list)do if v.item_id==msg.item_id then table.remove(bag_logic.item_list,i);break end end
  mining.dig_count=mining.dig_count+(tonumber(msg.add_dig) or 0)
 end
end
network.event_listener.query_mining_info_ret=function(msg)
 if msg.dig_count~=nil then mining.dig_count=msg.dig_count end
 if msg.dig_max_count~=nil then mining.dig_max_count=msg.dig_max_count end
end
function reset_growth()
 A:cancel_task();A._cancelled_task_pending=nil;A.now=0;clock=1000;sent={};A.inBattle=false
 mining.dig_count=0;mining.dig_max_count=20
 bag_logic.item_list={};bag_logic.capacity=20
 resource_counts={[54]=1000,[92]=1000}
 rune.rune_list={};rune.rune_temporary_list={};rune.cur_draw_platform_id=1;rune.rune_bag_capacity=25
 destiny.weapon_star_can_upgrade=true;destiny.weapon_star_info_list={{weapon_id=1,star_level=0,exp=0},{weapon_id=2,star_level=0,exp=10}}
 A.cfg.runeUseFlashGold=true
end
`);
test('rune performs ten-draws, auto-composes and upgrades equipped runes',`
reset_growth()
rune.rune_list={{rune_id=10,level=1,exp=0,equip_pos=1,template_info={type=2,quality=5,level_limit=10}},
 {rune_id=11,level=1,exp=0,equip_pos=0,template_info={type=1,quality=6,level_limit=1,out_exp=100}}}
A:start_rune(false)
for round=1,10 do
 A:rune_step()
 assert(sent[#sent].draw_rune and sent[#sent].draw_rune.draw_type=='ten_times','must use game ten-draw')
 respond('draw_rune_ret',{result='success'})
 A:rune_step()
 assert(sent[#sent].rune_one_key_receive and sent[#sent].rune_one_key_receive.is_auto_compose==true,'auto compose must be on')
 respond('rune_one_key_receive_ret',{})
end
A:rune_step();assert(sent[#sent].upgrade_rune,'after 100 draws must upgrade equipped runes')
local ids=sent[#sent].upgrade_rune.exp_rune_id_list;assert(ids[1]==11,'flash gold material must be selected')
assert(A.rune_state.drawn==100 and A.rune_state.ten_pulls==10)
respond('upgrade_rune_ret',{result='success',rune_info={rune_id=10,level=10,exp=0},exp_rune_id_list={11}})
local r=A:rune_step();assert(r.done and A.rune_state.upgrades==1,tostring(r.message))
`);
test('rune flash-gold material is excluded when the option is off',`
reset_growth();A.cfg.runeUseFlashGold=false
rune.rune_list={{rune_id=10,level=1,exp=0,equip_pos=1,template_info={type=2,quality=5,level_limit=10}},
 {rune_id=11,level=1,exp=0,equip_pos=0,template_info={type=1,quality=6,level_limit=1,out_exp=100}},
 {rune_id=12,level=1,exp=0,equip_pos=0,template_info={type=1,quality=4,level_limit=1,out_exp=100}}}
resource_counts[54]=0
A:start_rune(false);A:rune_step();assert(sent[#sent].upgrade_rune)
local ids=sent[#sent].upgrade_rune.exp_rune_id_list
for _,id in ipairs(ids)do assert(id~=11,'flash gold must be excluded')end
assert(ids[1]==12)
`);
test('destiny picks the lowest expected star-up cost until crystals run out',`
reset_growth()
A:start_destiny();A:destiny_step();assert(sent[#sent].query_weapon_star_info)
respond('query_weapon_star_info_ret',{})
A:destiny_step();assert(sent[#sent].weapon_star_upgrade.weapon_id==2,'weapon 2 has lower expected crystal cost')
respond('weapon_star_upgrade_ret',{result='success',weapon_id=2,star_level=1,exp=0,is_upgrade=true})
resource_counts[92]=0
local r=A:destiny_step();assert(r.done and A.destiny_state.attempts==1,tostring(r.message))
`);
test('destiny stops when fate crystals are insufficient',`
reset_growth();resource_counts[92]=10
A:start_destiny();A:destiny_step();respond('query_weapon_star_info_ret',{})
local r=A:destiny_step();assert(r.done and r.message:find('结晶'),tostring(r.message))
`);
test('void reward exchange skips gold and purple fragments and buys the max batch',`
reset_growth();resource_counts[77]=100;constants.RESOURCE_TYPE.vanity_adventure=77
config_manager.item_config={[16000004]={name='紫色灵魂碎片*100'},[16000007]={name='金色灵魂碎片*100'}}
troop={vanity_good_list={}}
package.loaded['logic.troop']=troop
network.event_listener.query_vanity_goods_ret=function(msg)
 if msg.items then troop.vanity_good_list=msg.items end
end
network.event_listener.vanity_exchange_goods_ret=function(msg)
 if msg.result=='success' then
  for _,g in ipairs(troop.vanity_good_list)do
   if g.good_id==msg.good_id then
    g.cur_count=g.cur_count+(msg.num or 1);resource_counts[77]=resource_counts[77]-g.price*(msg.num or 1)
   end
  end
 end
end
A:start_void_exchange();A:void_exchange_step()
assert(sent[#sent].query_vanity_goods,'no query, last='..tostring(next(sent[#sent])))
respond('query_vanity_goods_ret',{result='success',items={
 {good_id=1,price=15,max_count=10,cur_count=0,reward_list={{reward_type=2,param1=95}}},
 {good_id=2,price=20,max_count=0,cur_count=0,reward_list={{reward_type=2,param1=96}}},
 {good_id=3,price=25,max_count=0,cur_count=0,reward_list={{reward_type=1,param1=16000004}}},
 {good_id=4,price=30,max_count=5,cur_count=0,reward_list={{reward_type=2,param1=2}}}}})
A:void_exchange_step();assert(sent[#sent].vanity_exchange_goods,'no buy, last='..tostring(next(sent[#sent])))
assert(sent[#sent].vanity_exchange_goods.good_id==4,'gold/purple fragments must be skipped')
assert(sent[#sent].vanity_exchange_goods.num==3,'must buy the max batch affordable: '..tostring(sent[#sent].vanity_exchange_goods.num))
respond('vanity_exchange_goods_ret',{result='success',good_id=4,num=3})
local r=A:void_exchange_step();assert(r.done and A.void_exchange_state.purchased==3,tostring(r.message))
`);
test('void reward exchange falls back to single buys when the batch is rejected',`
reset_growth();resource_counts[77]=50;constants.RESOURCE_TYPE.vanity_adventure=77
troop={vanity_good_list={}}
package.loaded['logic.troop']=troop
network.event_listener.query_vanity_goods_ret=function(msg)
 if msg.items then troop.vanity_good_list=msg.items end
end
network.event_listener.vanity_exchange_goods_ret=function(msg)
 if msg.result=='success' then
  for _,g in ipairs(troop.vanity_good_list)do
   if g.good_id==msg.good_id then
    g.cur_count=g.cur_count+(msg.num or 1);resource_counts[77]=resource_counts[77]-g.price*(msg.num or 1)
   end
  end
 end
end
A:start_void_exchange();A:void_exchange_step()
respond('query_vanity_goods_ret',{result='success',items={
 {good_id=9,price=20,max_count=5,cur_count=0,reward_list={{reward_type=2,param1=2}}}}})
A:void_exchange_step();assert(sent[#sent].vanity_exchange_goods.num==2,'must attempt the max batch first')
respond('vanity_exchange_goods_ret',{result='failure',good_id=9,num=2})
A:void_exchange_step();assert(sent[#sent].vanity_exchange_goods.num==1,'rejected batch must fall back to single buys')
respond('vanity_exchange_goods_ret',{result='success',good_id=9,num=1})
A:void_exchange_step();assert(sent[#sent].vanity_exchange_goods.num==1,'later buys stay single after a rejection')
respond('vanity_exchange_goods_ret',{result='success',good_id=9,num=1})
local r=A:void_exchange_step();assert(r.done and A.void_exchange_state.purchased==2,tostring(r.message))
`);
test('earth heart uses items until the pickaxe cap',`
reset_growth();bag_logic.item_list={{item_id=1,template_id=6000005},{item_id=2,template_id=6000005}}
A:start_earth_heart();A:earth_heart_step();respond('query_mining_info_ret',{})
A:earth_heart_step();assert(sent[#sent].use_item and sent[#sent].use_item.item_id==1)
respond('use_item_ret',{result='success',item_id=1,add_dig=12})
A:earth_heart_step();respond('query_mining_info_ret',{})
A:earth_heart_step();assert(sent[#sent].use_item and sent[#sent].use_item.item_id==2)
respond('use_item_ret',{result='success',item_id=2,add_dig=12})
A:earth_heart_step();respond('query_mining_info_ret',{})
local r=A:earth_heart_step();assert(r.done and A.earth_state.used==2,tostring(r.message))
`);
};
