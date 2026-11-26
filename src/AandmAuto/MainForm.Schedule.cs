using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace AandmAuto {
 internal sealed partial class MainForm {
  private ScheduleBook scheduleBook=new ScheduleBook();
  private Timer scheduleTimer; private bool scheduleBusy,scheduleActive;
  private ScheduleRun activeSchedule; private int scheduleStepIndex;
  private DateTime scheduledStepUntil=DateTime.MinValue;
  private DateTime nextScheduleProbeUtc=DateTime.MinValue;
  private DateTime harvestRegistrationRetryUtc=DateTime.MaxValue;
  private DateTime checkinRegistrationRetryUtc=DateTime.MaxValue;
  private bool refreshSchedulePath;
  private bool checkinDaily, checkinFinished, keepScheduledWindowOpen;
  private bool refineDaily, refineFinished;
  private const string CheckinTaskFile="windows-checkin-task.xml";
  private string SchedulePath(){return Path.Combine(DataRoot(),"task-schedules-v2.json");}
  private void SaveSchedule(){string path=SchedulePath(),temp=path+".tmp";File.WriteAllText(temp,json.Serialize(scheduleBook));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  private void SetupScheduling(){
   if(previewMode)return;
   bool harvestSettingsChanged=false;
   try{
    if(File.Exists(SchedulePath())){
     string raw=File.ReadAllText(SchedulePath());
     scheduleBook=json.Deserialize<ScheduleBook>(raw)??new ScheduleBook();
     // 1.41 及更早的存档没有 RefineFollowup（炼化此前始终启用），升级时按开启处理；
     // 矿车与矿山沿用用户已保存的值，缺失时同样按开启处理。
     if(raw.IndexOf("\"RefineFollowup\"",StringComparison.Ordinal)<0){scheduleBook.RefineFollowup=true;harvestSettingsChanged=true;}
     if(raw.IndexOf("\"MineFollowup\"",StringComparison.Ordinal)<0){scheduleBook.MineFollowup=true;harvestSettingsChanged=true;}
     if(raw.IndexOf("\"EscortFollowup\"",StringComparison.Ordinal)<0){scheduleBook.EscortFollowup=true;harvestSettingsChanged=true;}
     scheduleBook.NormalizeHarvestTimes();
    }else{string old=Path.Combine(DataRoot(),"task-schedule.json");if(File.Exists(old)){scheduleBook=ScheduleBook.Migrate(json.Deserialize<ScheduleSettings>(File.ReadAllText(old)),DateTime.Now);harvestSettingsChanged=true;}else scheduleBook=new ScheduleBook();SaveSchedule();}
    if(scheduleBook.Plans==null)scheduleBook.Plans=new List<SchedulePlan>();if(scheduleBook.Pending==null)scheduleBook.Pending=new List<ScheduleRun>();
    if(scheduleBook.Harvest==null)scheduleBook.Harvest=new List<HarvestRun>();
    bool stale=scheduleBook.Harvest.RemoveAll(r=>r==null||string.IsNullOrEmpty(r.Account)||r.Account=="0"||r.DueUtc==DateTime.MinValue)>0;
    if(scheduleBook.RemoveDisabledHarvests()||stale)harvestSettingsChanged=true;
    foreach(var p in scheduleBook.Plans)p.Validate();
    if(harvestSettingsChanged)SaveSchedule();
   }catch(Exception e){
    scheduleBook=new ScheduleBook();AddLog("读取定时设置失败，已暂停计划："+e.Message,YellowColor);
    try{File.WriteAllText(Path.Combine(DataRoot(),"schedule-read-error.txt"),e.ToString());}catch{}
   }
   // 关闭的续跑项在启动时已被移除，Windows 定时任务需在首个计时周期同步。
   if(harvestSettingsChanged)harvestRegistrationRetryUtc=DateTime.MinValue;
   refreshSchedulePath=!Program.ScheduledLaunch;
   scheduleTimer=new Timer{Interval=500};scheduleTimer.Tick+=ScheduleTick;scheduleTimer.Start();SetupTaskCommands();
  }
  private bool AnyTaskRunning(){return HasActiveTask||lastRunning||startRequested||taskInitializing;}
  private void EnqueueSchedule(ScheduleRun run){scheduleBook.Pending.Add(run);SaveSchedule();AddLog(run.Name+"已排队，最长等待 "+run.MaxWaitMinutes+" 分钟。",BlueColor);}
  private void SyncHarvestWindowsTask(){
   harvestRegistrationRetryUtc=DateTime.MaxValue;
   try{ScheduleBook.UpdateHarvestWindowsTask(scheduleBook,DataRoot(),Application.ExecutablePath);}
   catch(Exception ex){harvestRegistrationRetryUtc=DateTime.UtcNow.AddMinutes(1);AddLog("续跑计划已保存，Windows 定时启动登记失败："+ex.Message,YellowColor);}
  }
  private bool CurrentScheduleLaunchPath(string file){
   string path=Path.Combine(DataRoot(),file);
   return File.Exists(path)&&File.ReadAllText(path).IndexOf("<Command>"+System.Security.SecurityElement.Escape(Application.ExecutablePath)+"</Command>",StringComparison.OrdinalIgnoreCase)>=0;
  }
  private void RecordHarvestUpdates(Dictionary<string,object> result){
   object raw;
   if(previewMode||!result.TryGetValue("harvest",out raw))return;
   var updates=raw as Dictionary<string,object>;
   if(updates==null)return;
   bool changed=false;
   foreach(var pair in updates){
    var update=pair.Value as Dictionary<string,object>;
    if(update==null||!update.ContainsKey("seconds"))continue;
    int seconds=GetInt(update,"seconds");
    if(seconds<0||seconds>31*86400)continue;
    DateTime? due=seconds>0?(DateTime?)DateTime.UtcNow.AddSeconds(seconds):null;
    string account=GetString(update,"account");
    if(pair.Key=="refine"){
     object rawHeat;double heat;
     if(!update.TryGetValue("heat",out rawHeat)||!double.TryParse(Convert.ToString(rawHeat,System.Globalization.CultureInfo.InvariantCulture),
        System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out heat)||heat<0||heat>10000)continue;
     if(!scheduleBook.SetRefineCooling(account,DateTime.UtcNow,seconds,heat,GetBool(update,"estimated")))continue;
     var run=scheduleBook.Harvest.Find(r=>r.Task=="refine"&&r.Account==account);
     due=run==null?(DateTime?)null:run.DueUtc;
    }else if(!scheduleBook.SetHarvest(pair.Key,account,due))continue;
    changed=true;
    string label=new HarvestRun{Task=pair.Key}.Label;
    AddLog(due.HasValue?label+"已安排在 "+due.Value.ToLocalTime().ToString("MM/dd HH:mm:ss"):label+"本轮结束",BlueColor);
   }
   if(changed){SaveSchedule();SyncHarvestWindowsTask();nextScheduleProbeUtc=DateTime.MinValue;UpdateSettingsSummary();}
  }
  private void SyncCheckinWindowsTask(){
   checkinRegistrationRetryUtc=DateTime.MaxValue;
   try{ScheduleBook.UpdateCheckinWindowsTask(scheduleBook,DataRoot(),Application.ExecutablePath);}
   catch(Exception ex){checkinRegistrationRetryUtc=DateTime.UtcNow.AddMinutes(1);AddLog("Windows 自动签到登记失败："+ex.Message,YellowColor);}
  }
  private void CloseFinishedSchedule(){
   if(keepScheduledWindowOpen||WindowState!=FormWindowState.Minimized)return;
   if((Program.CheckinLaunch&&checkinFinished)||(Program.HarvestLaunch&&refineFinished))Close();
  }
  private void CancelHarvest(string kind=null){
   if(scheduleBook.Harvest.RemoveAll(r=>kind==null||r.Task==kind)==0||previewMode)return;
   SaveSchedule();SyncHarvestWindowsTask();
  }
  private async void ScheduleTick(object sender,EventArgs e){
   if(scheduleBusy||IsDisposed)return;scheduleBusy=true;
   try{
    if(Program.ActivationSignal!=null&&Program.ActivationSignal.WaitOne(0))RestoreFromBackground();
    if(Program.ScheduleSignal!=null)Program.ScheduleSignal.WaitOne(0);
    DateTime now=DateTime.Now;
    DateTime utcNow=now.ToUniversalTime();
    if(refreshSchedulePath){
     refreshSchedulePath=false;
     if(scheduleBook.Harvest.Count>0&&!CurrentScheduleLaunchPath("windows-harvest-task.xml"))SyncHarvestWindowsTask();
     if(scheduleBook.AutoCheckin&&!CurrentScheduleLaunchPath(CheckinTaskFile))SyncCheckinWindowsTask();
     if(scheduleBook.LaunchWhenClosed&&scheduleBook.Plans.Exists(p=>p.Enabled)&&!CurrentScheduleLaunchPath("windows-task.xml"))
      ScheduleBook.UpdateWindowsTask(scheduleBook,DataRoot(),Application.ExecutablePath);
    }
    if(utcNow>=harvestRegistrationRetryUtc)SyncHarvestWindowsTask();
    if(scheduleBook.AutoCheckin&&utcNow>=checkinRegistrationRetryUtc)SyncCheckinWindowsTask();
    if(utcNow>=nextTrayUpdateUtc){nextTrayUpdateUtc=utcNow.AddSeconds(1);UpdateTrayStatus(utcNow);}
    if(dockHost!=0&&!DockWindow.IsWindow(new IntPtr(dockHost)))await UndockAccount();
    if(scheduleBook.CollectDue(now))SaveSchedule();
    bool expired=false;for(int n=scheduleBook.Pending.Count-1;n>=0;n--)if(scheduleBook.Pending[n].Expired(now)){AddLog(scheduleBook.Pending[n].Name+"等待超时，本轮跳过。",YellowColor);scheduleBook.Pending.RemoveAt(n);expired=true;}if(expired)SaveSchedule();
    if(scheduleActive){
     if(scheduledStepUntil!=DateTime.MinValue&&now>=scheduledStepUntil){scheduledStepUntil=DateTime.MinValue;await StopCurrentTasksAsync();return;}
     if(AnyTaskRunning())return;
     scheduledStepUntil=DateTime.MinValue;
     if(scheduleStepIndex>=activeSchedule.Steps.Count){AddLog(activeSchedule.Name+"队列执行完成。",GreenColor);scheduleActive=false;activeSchedule=null;return;}
     var step=activeSchedule.Steps[scheduleStepIndex++];AddLog(activeSchedule.Name+"："+step,BlueColor);
     if(step.Minutes>0)scheduledStepUntil=now.AddMinutes(step.Minutes);
     await StartAssistantTaskAsync(step.Task);return;
    }
    if(!coreReady||AnyTaskRunning()||utcNow<nextScheduleProbeUtc)return;
    if(scheduleBook.Pending.Count==0&&scheduleBook.DueHarvest(utcNow)==null&&!scheduleBook.CheckinDue(utcNow)){
     CloseFinishedSchedule();return;
    }
    nextScheduleProbeUtc=utcNow.AddSeconds(3);
    int generation=taskGeneration;
    string world=json.Deserialize<string>(await ExecuteLuaAsync("return require('scene.scene_manager'):GetCurrentSceneName()=='world' and 'world:'..__MWA:harvest_account() or ''"))??"";
    if(generation!=taskGeneration||AnyTaskRunning()||!world.StartsWith("world:",StringComparison.Ordinal))return;
    string account=world.Substring(6);
    if(!string.IsNullOrEmpty(account)&&account!="0"&&scheduleBook.CheckinDue(utcNow)){
     long previous=scheduleBook.LastCheckinSlot;
     scheduleBook.LastCheckinSlot=ScheduleBook.CheckinSlotUtc(utcNow).Ticks;
     try{SaveSchedule();}catch{scheduleBook.LastCheckinSlot=previous;throw;}
     AddLog("自动签到时段已到，开始一键日常。",BlueColor);
     await StartDailyAsync(false,scheduledCheckin:true);
     return;
    }
    var harvest=scheduleBook.DueHarvest(utcNow,account);
    if(harvest!=null&&(scheduleBook.Pending.Count==0||harvest.DueUtc<=scheduleBook.Pending[0].Due.ToUniversalTime())){
     nextScheduleProbeUtc=utcNow.AddSeconds(15);
     AddLog(harvest.Label+(harvest.Task=="refine"?"到点，核对游戏过热度。":"到点，开始收取并安排下一轮。"),BlueColor);
     if(harvest.Task=="mine")await StartMineAsync(true);
     else if(harvest.Task=="refine"){
      // Leave a recovery time if login drops or a server read never returns.
      harvest.DueUtc=utcNow.AddMinutes(15);
      SaveSchedule();SyncHarvestWindowsTask();
      await StartDailyAsync(false,refineFollowup:true);
     }else await StartDailyAsync(true);
     return;
    }
    if(scheduleBook.Pending.Count==0){CloseFinishedSchedule();return;}
    activeSchedule=scheduleBook.Pending[0];scheduleBook.Pending.RemoveAt(0);SaveSchedule();
    scheduleActive=true;scheduleStepIndex=0;
   }catch(Exception ex){AddLog("任务队列："+ex.Message,YellowColor);}
   finally{scheduleBusy=false;}
  }
  private async Task StartAssistantTaskAsync(string id){
   switch(id){case "daily":BtnDaily_Click(this,EventArgs.Empty);break;case "arena":BtnArena_Click(this,EventArgs.Empty);break;case "void":BtnVoid_Click(this,EventArgs.Empty);break;case "mine":BtnMine_Click(this,EventArgs.Empty);break;
    case "workshop":await StartWorkshopAsync("both");break;case "collect":await StartWorkshopAsync("collect");break;case "produce":await StartWorkshopAsync("produce");break;case "guild":await StartGuildAsync(GetNumberOption("guildBossCount",3));break;
    case "rune":await StartGrowthAsync("rune",false);break;case "runeLoop":await StartGrowthAsync("rune",true);break;case "destiny":await StartGrowthAsync("destiny");break;case "miningBoss":await StartGrowthAsync("miningBoss");break;case "orepacks":await StartGrowthAsync("orepacks");break;case "push":await StartGrowthAsync("push");break;
    case "exchange":await StartVoidExchangeAsync();break;case "autoMine":BtnMain_Click(this,EventArgs.Empty);break;default:throw new ArgumentException("未知助手任务");
   }
  }
  private async Task StopCurrentTasksAsync(){
   switch(activeTask){
    case AssistantTaskKind.Daily:StopDaily();break;
    case AssistantTaskKind.Arena:StopArena();break;
    case AssistantTaskKind.Void:StopVoid();break;
    case AssistantTaskKind.Mine:StopMine();break;
    case AssistantTaskKind.Workshop:StopWorkshop();break;
    case AssistantTaskKind.GuildBoss:StopGuild();break;
    case AssistantTaskKind.Growth:StopGrowth();break;
    case AssistantTaskKind.VoidExchange:StopVoidExchange();break;
   }
   if(lastRunning||startRequested)await StopAutomationAsync();
  }
  private void CancelScheduledQueue(){scheduleActive=false;activeSchedule=null;scheduleStepIndex=0;scheduledStepUntil=DateTime.MinValue;scheduleBook.Pending.Clear();if(!previewMode)SaveSchedule();}
  private async void BtnScheduleOptions_Click(object sender,EventArgs e){
   await OpenUnifiedSettingsAsync("schedule");
  }
  private string PlanDefinition(SchedulePlan p){var clone=json.Deserialize<SchedulePlan>(json.Serialize(p));clone.Cursor=null;return json.Serialize(clone);}
 }
}
