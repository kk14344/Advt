module.exports=({run,test})=>{
run(`
  MYSTIC_ORIGINAL_CHECK=MINE.CheckRefreshArea
  MYSTIC_ORIGINAL_RESET=MINE.ResetArea
  MYSTIC_ORIGINAL_QUERY=MINE.QueryBlockInfo
  MYSTIC_ORIGINAL_CONFIG=config_manager.mining_refresh_config
  function mystic_fixture(level,threshold,tier)
    route_fixture(120)
    A.cfg.autoMysticStoneReset=true;A.cfg.mysticStoneThreshold=threshold;A.cfg.mysticStoneResetTier=tier
    A._mystic_reset=nil;A._mystic_reset_next=nil;A._mystic_reset_blocked=nil
    MINE.golem_lv=level;MINE.block_list[121]={[5]=1};MINE.stream_generation=5
    config_manager.mining_refresh_config={{golem_lv=1,reset_lv=1,tnt=0,ultimate_tool=0},
        {golem_lv=100,reset_lv=150,tnt=3,ultimate_tool=20}}
    local resources=constants.RESOURCE_TYPE;resources.tnt=9;resources.ultimate_tool=29
    MINE.CheckRefreshArea=function(self,lv,with_item)
        assert(lv==tier and with_item==true);return true
    end
    MINE.ResetArea=function(self,lv)
        self.golem_lv=lv;self.cur_position={x=5,y=1};self.dig_depth=1
        self.stream_generation=self.stream_generation+1;self.block_list={}
    end
    MINE.QueryBlockInfo=function(self)
        RTNET:Send({query_mining_block_info={}})
    end
    RTNET.event_listener.query_bag_info_ret=function()end
    RTNET.event_listener.use_item_ret=function()end
    RTNET.event_listener.query_mining_info_ret=function()end
    RTNET.event_listener.refresh_mining_area_ret=function(msg)
        MINE:ResetArea(msg.golem_lv)
        if msg.loaded then MINE.block_list={[1]={[5]=0},[2]={[5]=1}} end
    end
  end
  function mystic_inventory()
    rt_reply('query_bag_info_ret',{item_list={
        {item_id=62,template_id=6000005,count=3},
        {item_id=74,template_id=10000031,count=2}}})
  end
`);
test('mystic stone triggers exactly at the chosen level and resumes on the confirmed new map',`
 mystic_fixture(150,150,1);A.exploration_tip={x=5,y=120};A.horizontal_started=true
 A:step();assert(#RTSENT==1 and RTSENT[1].query_bag_info and #ROUTE_HITS==0)
 mystic_inventory();route_tick(1)
 local sent=RTSENT[2].use_item;assert(sent.item_id==74 and sent.golem_lv==1 and #ROUTE_HITS==0)
 rt_reply('use_item_ret',{result='success',item_id=74})
 route_tick(.1);assert(#RTSENT==2 and #ROUTE_HITS==0)
 rt_reply('refresh_mining_area_ret',{result='success',golem_lv=1,loaded=true})
 route_tick(.2);assert(#ROUTE_HITS==1 and ROUTE_HITS[1].y==2 and A._mystic_reset==nil)
 assert(not A.horizontal_started and #RTSENT==2)
`);
test('mystic stone waits for the configured level, native tier requirement and material balance',`
 mystic_fixture(149,150,1);A:step();assert(#RTSENT==0 and #ROUTE_HITS==1)
 mystic_fixture(149,140,100);A:step();assert(#RTSENT==0 and #ROUTE_HITS==1)
 mystic_fixture(170,150,100);RTTNT=2;A:step();assert(#RTSENT==0 and #ROUTE_HITS==1)
 mystic_fixture(170,150,100);RTSTOCK=19;A:step();assert(#RTSENT==0 and #ROUTE_HITS==1)
`);
test('a missing mystic stone resumes excavation without sending any item-use request',`
 mystic_fixture(175,150,100);A:step()
 rt_reply('query_bag_info_ret',{item_list={{item_id=40,template_id=6000005,count=3}}})
 route_tick(1);assert(#RTSENT==1 and #ROUTE_HITS==1 and A._mystic_reset==nil)
`);
test('lost reset notification is recovered by confirmed server level and a fresh map',`
 mystic_fixture(175,150,100);A:step();mystic_inventory();route_tick(1)
 assert(RTSENT[2].use_item.golem_lv==100)
 route_tick(5.1);assert(#RTSENT==3 and RTSENT[3].query_mining_info)
 rt_reply('query_mining_info_ret',{result='success',golem_lv=100})
 route_tick(.1);assert(#RTSENT==4 and RTSENT[4].query_mining_block_info and #ROUTE_HITS==0)
 MINE.block_list={[1]={[5]=0},[2]={[5]=1}}
 route_tick(1);assert(#ROUTE_HITS==1 and ROUTE_HITS[1].y==2 and A._mystic_reset==nil)
`);
test('an uncertain mystic stone use never repeats against the same mine',`
 mystic_fixture(175,150,1);A:step();mystic_inventory();route_tick(1)
 route_tick(5.1);rt_reply('query_mining_info_ret',{result='success',golem_lv=175})
 route_tick(.1);assert(#RTSENT==3 and #ROUTE_HITS==1 and A._mystic_reset_blocked)
 A.now=A.now+180;A:step();assert(#RTSENT==3 and not A._task_pending)
`);
run(`
 A.cfg.autoMysticStoneReset=false;A._mystic_reset=nil;A._mystic_reset_next=nil;A._mystic_reset_blocked=nil
 MINE.CheckRefreshArea=MYSTIC_ORIGINAL_CHECK;MINE.ResetArea=MYSTIC_ORIGINAL_RESET
 MINE.QueryBlockInfo=MYSTIC_ORIGINAL_QUERY;MINE.stream_generation=nil
 config_manager.mining_refresh_config=MYSTIC_ORIGINAL_CONFIG
`);
};
