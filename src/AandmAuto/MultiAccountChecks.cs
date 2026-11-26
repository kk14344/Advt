using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
namespace AandmAuto {
 internal sealed partial class MainForm {
  private async Task RunMultiChildFixtureAsync(){
   pollTimer.Stop();autoLoginEnabled=false;
   webView.CoreWebView2.NavigateToString("<!doctype html><meta charset='utf-8'><body style='background:#182829;color:#f2d78c;font:24px sans-serif;text-align:center'><h2>账号同屏离线验收</h2><p>"+AppPaths.ProfileId+"</p><p id='status'>等待助手任务</p></body>");await Task.Delay(600);
   await ExecuteScriptAsync(@"window.multiProbe={starts:[],steps:0,pulses:0,profile:"+json.Serialize(AppPaths.ProfileId)+@",cookie:''};
    window.__MWA_APP={ready:function(){return true},snapshot:function(){return {running:false}},backgroundPulse:function(){multiProbe.pulses++},cancelTask:function(){},stop:function(){return true},runLua:function(code){
     if(code.indexOf('harvest_account()')>=0)return 'world:'+multiProbe.profile;
     if(code.indexOf('GetCurrentSceneName')>=0)return true;
     if(code.indexOf(':start_')>=0){var m=code.match(/:start_([a-z_]+)/);multiProbe.starts.push(m?m[1]:'?');document.getElementById('status').textContent='任务：'+multiProbe.starts.join(' → ');return true}
     if(code.indexOf('_step(')>=0){multiProbe.steps++;if(code.indexOf('daily_step')>=0)return {done:true,message:'离线日常完成'};return {wait:true,message:'离线持续任务'}};return true;
    }};");
   var cm=webView.CoreWebView2.CookieManager;cm.AddOrUpdateCookie(cm.CreateCookie("multiProfile",AppPaths.ProfileId,"isolation.invalid","/"));var cookies=await cm.GetCookiesAsync("https://isolation.invalid/");
   File.WriteAllText(Path.Combine(DataRoot(),"multi-cookie-debug.json"),json.Serialize(new {count=cookies.Count,values=cookies.Select(c=>c.Value).ToArray()}));
   await ExecuteScriptAsync("window.multiProbe.cookie="+json.Serialize(cookies[0].Value));dailyTaskTimer.Start();
  }
 }
 internal static partial class MultiAccountChecks {
  internal static void Run(string output){
   Directory.CreateDirectory(output);Environment.SetEnvironmentVariable("AANDM_MULTI_SMOKE","1");
   var store=new AccountProfiles(AppPaths.BaseRoot);var ids=new List<string>{"main"};
   while(ids.Count<MultiAccountForm.MaxAccounts)
    ids.Add(store.Save(null,"offline-test-"+(ids.Count+1),"test-only",false,null).Id);
   PrepareDynamicProfiles(store,ids);
   var screen=new MultiAccountForm(store,ids);screen.Shown+=async delegate{
    var j=new JavaScriptSerializer();var checks=new List<string>();string error="";var children=new List<int>();
    try{
     if(screen.ColumnCount!=4)throw new Exception("四账号同屏布局未建立");checks.Add("four-account-layout");
     for(int i=0;i<100&&!screen.SmokeReady;i++)await Task.Delay(300);
     if(!screen.SmokeReady)throw new Exception("四账号未全部接入："+string.Join(" | ",screen.SmokeStatuses));
     checks.Add("four-native-windows-docked");await Task.Delay(1800);
     var first=await ReadAll(store,ids);for(int retry=0;retry<40&&first.Any(x=>!x.ContainsKey("probe")||x["probe"]==null||string.IsNullOrEmpty(Convert.ToString(((Dictionary<string,object>)x["probe"])["cookie"])));retry++){await Task.Delay(500);first=await ReadAll(store,ids);}File.WriteAllText(Path.Combine(output,"initial-probes.json"),j.Serialize(first));for(int i=0;i<ids.Count;i++){var probe=(Dictionary<string,object>)first[i]["probe"];if(Convert.ToString(probe["cookie"])!=ids[i])throw new Exception("Cookie 泄漏");}checks.Add("four-independent-cookie-stores");
     using(var other=new Form()){var foreign=other.Handle;var refused=await AccountCommand.Send(store.Root("main"),ids[0],new TaskCommand{Action="dock",Host=foreign.ToInt64()});if(Convert.ToBoolean(refused["ok"]))throw new Exception("账号被另一个同屏窗口夺走");var kept=await AccountCommand.Send(store.Root("main"),ids[0],new TaskCommand{Action="undock",Host=foreign.ToInt64()});if(Convert.ToBoolean(kept["ok"]))throw new Exception("其他窗口可错误解除归属");}checks.Add("second-host-cannot-steal-or-undock");
     if(!await screen.SmokeApplyGlass())throw new Exception("同屏主题未应用到全部账号。");
     var themed=await ReadAll(store,ids);
     for(int i=0;i<ids.Count;i++){
      if(store.ReadTheme(ids[i])!=UiTheme.ThemeLiquidGlass||Convert.ToInt32(themed[i]["theme"])!=UiTheme.ThemeLiquidGlass)
       throw new Exception("同屏主题没有实时应用并保存到独立账号。");
      var probe=(Dictionary<string,object>)themed[i]["probe"];
      if(Convert.ToString(probe["cookie"])!=ids[i])throw new Exception("主题切换重建了游戏会话。");
     }
     checks.Add("uniform-theme-ignores-task-selection");
     checks.Add("uniform-theme-live-and-persisted");
     checks.Add("uniform-theme-keeps-game-session");
     using(var preview=new Bitmap(screen.Width,screen.Height)){screen.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));preview.Save(Path.Combine(output,"four-glass.png"));}
     string[] savedProjects=themed.Select(x=>Convert.ToString(x["wallpaperProject"])).ToArray();
     string[] savedBgPaths=themed.Select(x=>Convert.ToString(x["customBgPath"])).ToArray();
     int[] savedBgModes=themed.Select(x=>Convert.ToInt32(x["customBgMode"])).ToArray();
     string smokeWallpaperPath=Path.Combine(output,"shared-wallpaper-fixture.png");
     using(var fixture=new Bitmap(1280,720))
     using(Graphics fg=Graphics.FromImage(fixture))
     {
      using(var brush=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0,0,1280,720),Color.FromArgb(24,78,128),Color.FromArgb(148,72,92),25f))
       fg.FillRectangle(brush,0,0,1280,720);
      using(var linePen=new Pen(Color.FromArgb(220,245,214,132),6f))
       fg.DrawLine(linePen,0,40,1280,680);
      fixture.Save(smokeWallpaperPath,System.Drawing.Imaging.ImageFormat.Png);
     }
     var smokeWallpaperItem=new WallpaperEngineItem{Title="同屏共享测试壁纸",Type="image",StaticImagePath=smokeWallpaperPath,PreviewPath=smokeWallpaperPath};
     if(!await screen.SmokeApplySharedWallpaper(smokeWallpaperItem))
      throw new Exception("同屏共享壁纸未能应用到全部账号。");
     var sharedStates=await ReadAll(store,ids);
     string activeSharedName=Convert.ToString(sharedStates[0]["sharedWallpaper"]);
     if(string.IsNullOrEmpty(activeSharedName))throw new Exception("同屏共享壁纸会话名为空。");
     for(int i=0;i<ids.Count;i++){
      if(Convert.ToString(sharedStates[i]["sharedWallpaper"])!=activeSharedName)
       throw new Exception("同屏账号未使用同一份共享壁纸会话。");
      if(Convert.ToInt64(sharedStates[i]["wallpaperSequence"])<=0)
       throw new Exception("同屏共享壁纸未收到有效帧。");
      if(Convert.ToBoolean(sharedStates[i]["ownWallpaperPlaying"]))
       throw new Exception("子账号额外启动了独立壁纸播放器。");
      if(Convert.ToString(sharedStates[i]["wallpaperProject"])!=savedProjects[i]
       ||Convert.ToString(sharedStates[i]["customBgPath"])!=savedBgPaths[i]
       ||Convert.ToInt32(sharedStates[i]["customBgMode"])!=savedBgModes[i])
       throw new Exception("同屏共享壁纸篡改了账号原有壁纸设置。");
     }
     await Task.Delay(250);
     using(var sharedPreview=new Bitmap(screen.Width,screen.Height)){screen.DrawToBitmap(sharedPreview,new Rectangle(Point.Empty,sharedPreview.Size));sharedPreview.Save(Path.Combine(output,"four-shared-wallpaper.png"));}
     checks.Add("shared-wallpaper-single-player-four-tiles");
     if(!await screen.SmokeClearSharedWallpaper())
      throw new Exception("同屏页恢复各自壁纸失败。");
     var clearedStates=await ReadAll(store,ids);
     if(clearedStates.Any(x=>!string.IsNullOrEmpty(Convert.ToString(x["sharedWallpaper"]))))
      throw new Exception("恢复各自壁纸后仍有账号残留同屏共享壁纸。");
     if(!await screen.SmokeApplySharedWallpaper(smokeWallpaperItem))
      throw new Exception("再次启用同屏共享壁纸失败。");
     await screen.SmokeBroadcast("push",1,false);
     Dictionary<string,object>[] started=null;
     for(int attempt=0;attempt<24;attempt++){
      started=await ReadAll(store,ids);
      if(started.All(x=>Convert.ToBoolean(x["busy"])&&Convert.ToInt32(x["queued"])==0))break;
      await Task.Delay(350);
     }
     if(started.Any(x=>!Convert.ToBoolean(x["busy"])||Convert.ToInt32(x["queued"])!=0))
      throw new Exception("四个账号的同步任务未全部启动："+j.Serialize(started));
     await screen.SmokeBroadcast("daily",0,false);
     var busy=await ReadAll(store,ids);
     if(busy.Any(x=>Convert.ToInt32(x["queued"])!=1))throw new Exception("繁忙任务未排队："+j.Serialize(busy));
     checks.Add("broadcast-and-busy-queue");
     using(var bmp=new Bitmap(screen.Width,screen.Height)){screen.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(output,"four-accounts.png"));}
     var before=busy.Select(x=>Convert.ToInt32(((Dictionary<string,object>)x["probe"])["pulses"])).ToArray();screen.WindowState=FormWindowState.Minimized;
     for(int i=0;i<13;i++){await Task.Delay(5000);File.WriteAllText(Path.Combine(output,"progress.json"),j.Serialize(new {seconds=(i+1)*5,phase="minimized-duration-and-queue"}));}
     var after=await ReadAll(store,ids);for(int i=0;i<ids.Count;i++){var probe=(Dictionary<string,object>)after[i]["probe"];if(Convert.ToInt32(probe["pulses"])-before[i]<200)throw new Exception("最小化后心跳停止");var starts=Items(probe["starts"]);if(string.Join(",",starts.Select(x=>Convert.ToString(x)))!="push,daily")throw new Exception("时长终止或后继任务错误");}
     checks.Add("four-minimized-heartbeats");checks.Add("per-task-duration-stops-and-advances");
     screen.WindowState=FormWindowState.Normal;await screen.SmokeBroadcast("orepacks",0,true);await Task.Delay(1800);var selected=await ReadAll(store,ids);for(int i=0;i<ids.Count;i++){var starts=Items(((Dictionary<string,object>)selected[i]["probe"])["starts"]);if(starts.Length!=(i==0?3:2))throw new Exception("同步任务选择账号失效");}checks.Add("selected-accounts-only");
     await screen.SmokeStop();var stopped=await ReadAll(store,ids);if(stopped.Any(x=>Convert.ToBoolean(x["busy"])||Convert.ToInt32(x["queued"])!=0))throw new Exception("停止未清队列");checks.Add("broadcast-stop-clears-queues");
     var command=new TaskCommand{Id=Guid.NewGuid().ToString("N"),Action="start",Task="push"};
     var receipt=await AccountCommand.Send(store.Root("main"),ids[0],command);
     var repeated=await AccountCommand.Send(store.Root("main"),ids[0],command);
     if(!Convert.ToBoolean(receipt["ok"])||j.Serialize(receipt)!=j.Serialize(repeated))
      throw new Exception("重复命令的回执不一致");
     Dictionary<string,object> duplicate=null;
     object[] starts0=null;
     for(int attempt=0;attempt<24;attempt++){
      duplicate=await AccountCommand.Send(store.Root("main"),ids[0],new TaskCommand{Action="status"});
      starts0=Items(((Dictionary<string,object>)duplicate["probe"])["starts"]);
      if(starts0.Length>=4)break;
      await Task.Delay(350);
     }
     if(starts0.Length!=4||Convert.ToInt32(duplicate["queued"])!=0)
      throw new Exception("重复命令启动次数或队列异常："+j.Serialize(duplicate));
     checks.Add("command-idempotency");await screen.SmokeStop();
     if(CheckDynamic)await VerifyDynamicWallpapers(screen,store,ids,output,checks);
     if(!CheckDynamic)await screen.SmokeDetachAsync();var released=await ReadAll(store,ids);if(released.Any(x=>DockWindow.GetParent(new IntPtr(Convert.ToInt64(x["window"])))!=IntPtr.Zero))throw new Exception("账号未恢复顶层窗口");
     for(int i=0;i<ids.Count;i++){
      if(!string.IsNullOrEmpty(Convert.ToString(released[i]["sharedWallpaper"])))
       throw new Exception("退出同屏后未清空共享壁纸。");
      if(Convert.ToString(released[i]["wallpaperProject"])!=savedProjects[i]
       ||Convert.ToString(released[i]["customBgPath"])!=savedBgPaths[i]
       ||Convert.ToInt32(released[i]["customBgMode"])!=savedBgModes[i])
       throw new Exception("退出同屏后账号原有壁纸设置未保留。");
     }
     checks.Add("shared-wallpaper-restores-account-wallpaper-on-exit");
     checks.Add("closing-host-detaches-accounts");
    }catch(Exception ex){error=ex.ToString();screen.DetachAllImmediate();}
    {
     foreach(string id in ids){try{var response=await AccountCommand.Send(store.Root("main"),id,new TaskCommand{Action="undock"});var h=new IntPtr(Convert.ToInt64(response["window"]));uint pid;GetWindowThreadProcessId(h,out pid);children.Add((int)pid);PostMessage(h,0x0010,IntPtr.Zero,IntPtr.Zero);}catch{}}
     File.WriteAllText(Path.Combine(output,"multi-account-test-results.json"),j.Serialize(new {result=error.Length==0?"passed":"failed",checks=checks,error=error,accountCount=ids.Count,minimizedSeconds=65,noRealAccountUsed=true,noRealNetworkMutations=true,childProcesses=children}));
     if(!screen.IsDisposed)screen.Close();Application.ExitThread();
    }
   };Application.Run(screen);
  }
  static object[] Items(object value){return ((System.Collections.IEnumerable)value).Cast<object>().ToArray();}
  static async Task<Dictionary<string,object>[]> ReadAll(AccountProfiles store,List<string> ids){return await Task.WhenAll(ids.Select(id=>AccountCommand.Send(store.Root("main"),id,new TaskCommand{Action="status"})));}
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
 }
}
