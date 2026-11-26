module.exports = ({run,test}) => {
run(`
 local saved_graphic=package.loaded['logic.graphic']
 local saved_room=package.loaded['scene.battle_room']
 local saved_types=client_constants.BATTLE_TYPE
 local saved_speed=A.cfg.battleReplaySpeed
 local saved_skip=A.cfg.autoSkipBattle
 local saved_settlements=A._fast_settlements
 local saved_fast_battles=A.fast_battles
 function replay_fixture(speed,skip)
  local room={visible=false,updates=0,elapsed=0,ends=0}
  room.start_spine_node={speed=1,setTimeScale=function(self,value)self.speed=value end}
  room.IsVisible=function(self)return self.visible end
  room.Show=function(self,battle_type,_,_,_,_,callback)
   self.battle_type=battle_type;self.visible=true;self.callback=callback
   self.start_spine_node.speed=1
   return 'shown'
  end
  room.Update=function(self,elapsed)
   if not self.visible then return end
   self.updates=self.updates+1
   self.elapsed=self.elapsed+math.min(elapsed,0.0166667)
   if self.finish_at==self.updates then
    self.ends=self.ends+1
    self.visible=false
    if self.callback then self.callback() end
   end
  end
  local graphics={DispatchEvent=function(self,event,battle_type,_,_,_,_,callback)
   assert(event=='show_battle_room')
   return room:Show(battle_type,nil,nil,nil,nil,callback)
  end}
  client_constants.BATTLE_TYPE={vs_guild_player=7,vs_arena_player=4}
  package.loaded['logic.graphic']=graphics
  package.loaded['scene.battle_room']=room
  A._fast_settlements=nil
  A.cfg.battleReplaySpeed=A:normalize_replay_speed(speed)
  A.cfg.autoSkipBattle=skip
  assert(A:install_fast_graphics())
  return room,graphics
 end
 function replay_restore()
  package.loaded['logic.graphic']=saved_graphic
  package.loaded['scene.battle_room']=saved_room
  client_constants.BATTLE_TYPE=saved_types
  A.cfg.battleReplaySpeed=saved_speed
  A.cfg.autoSkipBattle=saved_skip
  A._fast_settlements=saved_settlements
  A.fast_battles=saved_fast_battles
 end
`);
test('the four replay speeds advance the existing timeline and Spine together', `
 for _,case in ipairs({{100,60},{125,75},{150,90},{200,120}}) do
  local room,g=replay_fixture(case[1],false)
  assert(g:DispatchEvent('show_battle_room',7)=='shown')
  for i=1,60 do room:Update(0.0166667) end
  assert(room.updates==case[2])
  assert(room.start_spine_node.speed==case[1]/100)
  assert(math.abs(room.elapsed-case[2]*0.0166667)<0.000001)
 end
`);
test('all native battle types share the selected speed, including task battles', `
 local room,g=replay_fixture(200,false)
 for _,battle_type in ipairs({1,4,7,18,24,99}) do
  local before=room.updates
  g:DispatchEvent('show_battle_room',battle_type)
  room:Update(0.0166667)
  assert(room.updates==before+2 and room.start_spine_node.speed==2)
 end
 room.visible=false
 room:Update(0.0166667)
 assert(room.updates==12)
 room:Show(99)
 room:Update(0.0166667)
 assert(room.updates==14 and room.start_spine_node.speed==2)
`);
test('new battle starts with a fresh replay clock and current preference', `
 local room,g=replay_fixture(125,false)
 g:DispatchEvent('show_battle_room',7)
 for i=1,3 do room:Update(0.0166667) end
 assert(room.updates==3)
 g:DispatchEvent('show_battle_room',4)
 room:Update(0.0166667)
 assert(room.updates==4)
 A.cfg.battleReplaySpeed=100
 room:Show(2);room:Update(0.006)
 assert(room.updates==5 and room.start_spine_node.speed==1)
`);
test('old skip still preempts battle playback and settles once', `
 local room,g=replay_fixture(200,true)
 local callbacks=0
 assert(g:DispatchEvent('show_battle_room',7,{},nil,{},true,function()callbacks=callbacks+1 end)==true)
 assert(not room.visible and room.updates==0 and callbacks==0)
 A:drain_fast_settlements();A:drain_fast_settlements()
 assert(callbacks==1)
`);
test('changing speed in flight restores native rates and ends the replay once', `
 local room,g=replay_fixture(150,false)
 local callbacks=0
 g:DispatchEvent('show_battle_room',7,{},nil,{},true,function()callbacks=callbacks+1 end)
 room:Update(0.0166667)
 A.cfg.battleReplaySpeed=100;room:Update(0.01)
 assert(room.updates==2 and room.start_spine_node.speed==1)
 assert(math.abs(room.elapsed-0.0266667)<0.000001)
 assert(A:normalize_replay_speed(175)==100)
 A.cfg.battleReplaySpeed=200
 room.finish_at=3
 room:Update(0.0166667)
 assert(room.updates==3 and room.ends==1 and callbacks==1)
`);
run('replay_restore()');
};
