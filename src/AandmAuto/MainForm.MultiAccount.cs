using System;
using System.IO;
using System.IO.Pipes;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
namespace AandmAuto {
 internal sealed class TaskCommand {public string Id {get;set;}public string Action {get;set;}public string Task {get;set;}public int Minutes {get;set;}public long Host {get;set;}public int? Theme {get;set;}public string SharedWallpaper {get;set;}public bool? ExtremePerformance {get;set;}}
 internal static class AccountCommand {
  internal static string Pipe(string root,string id){return "AandmAutoTasks-"+AppPaths.ContextKey(root,id);}
  internal static async Task<Dictionary<string,object>> Send(string root,string id,TaskCommand command){
   var pipe=new NamedPipeClientStream(".",Pipe(root,id),PipeDirection.InOut,PipeOptions.Asynchronous);
   try{await Task.Run(()=>pipe.Connect(1200));var writer=new StreamWriter(pipe){AutoFlush=true};var reader=new StreamReader(pipe);
    await writer.WriteLineAsync(new JavaScriptSerializer().Serialize(command));var read=reader.ReadLineAsync();if(await Task.WhenAny(read,Task.Delay(3500))!=read)throw new TimeoutException("账号控制通道等待超时");
    string line=await read;if(string.IsNullOrEmpty(line))throw new IOException("账号窗口已断开");return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);
   }finally{pipe.Dispose();}
  }
 }
 internal static class DockWindow {
  [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetParent(IntPtr child,IntPtr parent);
  [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr handle);
  [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr handle);
  [DllImport("user32.dll")] internal static extern bool MoveWindow(IntPtr h,int x,int y,int w,int height,bool repaint);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
  [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int command);
  internal static void Activate(IntPtr h){if(IsIconic(h))ShowWindow(h,9);SetForegroundWindow(h);}
  internal static void SetChild(IntPtr h,bool child){int style=GetWindowLong(h,-16);SetWindowLong(h,-16,child?(style&~unchecked((int)0x80000000))|0x40000000:style&~0x40000000);}
 }
 internal sealed partial class MainForm {
  private NamedPipeServerStream commandServer;private bool commandServerClosed;private long dockHost;private Form detachedSidebar;
  private readonly Dictionary<string,Dictionary<string,object>> commandReceipts=new Dictionary<string,Dictionary<string,object>>();
  private void SetupTaskCommands(){
   Task.Run(async delegate{
    while(!commandServerClosed){
     try{using(var server=new NamedPipeServerStream(AccountCommand.Pipe(AppPaths.BaseRoot,AppPaths.ProfileId),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
      commandServer=server;await server.WaitForConnectionAsync();var reader=new StreamReader(server);var writer=new StreamWriter(server){AutoFlush=true};
      var lineTask=reader.ReadLineAsync();if(await Task.WhenAny(lineTask,Task.Delay(3500))!=lineTask)continue;string line=await lineTask;if(line==null||line.Length>4096)continue;
      var c=new JavaScriptSerializer().Deserialize<TaskCommand>(line);var completion=new TaskCompletionSource<Dictionary<string,object>>();
      BeginInvoke((MethodInvoker)async delegate{try{completion.SetResult(await HandleTaskCommand(c));}catch(Exception ex){completion.TrySetResult(new Dictionary<string,object>{{"ok",false},{"message",ex.Message}});}});
      var response=await completion.Task;await writer.WriteLineAsync(new JavaScriptSerializer().Serialize(response));
     }}catch(Exception){if(commandServerClosed)return;}
     await Task.Delay(50);
    }
   });
  }
  private async Task<Dictionary<string,object>> HandleTaskCommand(TaskCommand c){
   if(c==null)throw new ArgumentException("缺少命令");Dictionary<string,object> receipt;if(c.Id!=null&&commandReceipts.TryGetValue(c.Id,out receipt))return receipt;
   if(c.Action=="start"){
    if(!ScheduleBook.Known(c.Task))throw new ArgumentException("任务无效");if(c.Minutes<0||c.Minutes>10080)throw new ArgumentException("时长无效");
    EnqueueSchedule(new ScheduleRun{PlanId="sync-"+(c.Id??Guid.NewGuid().ToString("N")),Name="同屏任务",Due=DateTime.Now,MaxWaitMinutes=30,Steps=new List<ScheduleStep>{new ScheduleStep{Task=c.Task,Minutes=c.Minutes}}});
   }else if(c.Action=="stop"){CancelScheduledQueue();await StopCurrentTasksAsync();}
   else if(c.Action=="dock"){
    if(c.Host==0||!DockWindow.IsWindow(new IntPtr(c.Host)))throw new ArgumentException("同屏窗口已关闭");
    if(dockHost!=0&&dockHost!=c.Host&&DockWindow.IsWindow(new IntPtr(dockHost)))throw new InvalidOperationException("此账号已在另一个同屏窗口中");
    dockHost=c.Host;WindowState=FormWindowState.Normal;MinimumSize=Size.Empty;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;SetDockLayout(true);
   }else if(c.Action=="undock"){if(c.Host!=0&&dockHost!=0&&dockHost!=c.Host)throw new InvalidOperationException("此账号属于另一个同屏窗口");await UndockAccount();}
   else if(c.Action=="setTheme"){
    if(c.Host==0||dockHost!=c.Host||!DockWindow.IsWindow(new IntPtr(c.Host)))throw new InvalidOperationException("此账号未接入当前同屏窗口。");
    if(!c.Theme.HasValue)throw new ArgumentException("缺少主题。");
    await ApplyAccountThemeAsync(c.Theme.Value);
   }
   else if(c.Action=="sharedWallpaper"){
    if(c.Host==0||dockHost!=c.Host||!DockWindow.IsWindow(new IntPtr(c.Host)))throw new InvalidOperationException("此账号未接入当前同屏窗口。");
    await SetSharedWallpaperAsync(c.SharedWallpaper);
   }
   else if(c.Action=="setExtremePerf"){
    if(!c.ExtremePerformance.HasValue)throw new ArgumentException("缺少极致性能模式参数。");
    await ApplyExtremePerformanceModeAsync(c.ExtremePerformance.Value,false);
   }
   else if(c.Action=="smokeWallpaperInput"&&Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE")=="1"
       &&c.Host!=0&&c.Host==dockHost&&sharedWallpaper!=null){
    return new Dictionary<string,object>{{"ok",true},{"canvasClick",await VerifyWallpaperCanvasInputAsync()}};
   }
   else if(c.Action=="panel")ShowDockSidebar();
   else if(c.Action!="status")throw new ArgumentException("不支持的命令");
   int reportedTheme=(c.Action=="setTheme"&&extremePerformanceMode)?GetNumberOption("uiTheme",UiTheme.CurrentTheme):UiTheme.CurrentTheme;
   receipt=new Dictionary<string,object>{{"ok",true},{"profile",AppPaths.ProfileId},{"window",Handle.ToInt64()},{"busy",AnyTaskRunning()},{"message",c.Action=="start"?"已排队："+ScheduleBook.Label(c.Task):lblStatus.Text},{"queued",scheduleBook.Pending.Count},{"ready",coreReady},{"theme",reportedTheme},{"extremePerformance",extremePerformanceMode}};
   if(Program.QuietResourceTest && c.Action=="status"){
    receipt["mainFormMinimized"]=WindowState==FormWindowState.Minimized;
    receipt["gameSnapshotReady"]=gameSnapshotReady;
    receipt["presentationHidden"]=quietGamePresentation;
    receipt["rendererSuspended"]=coreReady&&webView.CoreWebView2.IsSuspended;
    receipt["loginSleepEligible"]=CanSleepLogin(DateTime.UtcNow);
    receipt["loginSleepResult"]=loginSleepResult;
    receipt["backgroundPulseCount"]=runtimePulseCount;
   }
   if(c.Action=="sharedWallpaper"||Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE")=="1"){
    receipt["sharedWallpaper"]=sharedWallpaper==null?"":sharedWallpaper.Name;
    receipt["wallpaperSequence"]=sharedWallpaper==null?0:sharedWallpaper.Sequence;
    receipt["sharedPaintSequence"]=sharedPaintSequence;
    receipt["sharedPaintX"]=sharedPaintX;
    receipt["ownWallpaperPlaying"]=wallpaperPlayback!=null;
    receipt["wallpaperProject"]=wallpaperEngineProject??"";
    receipt["customBgPath"]=customBgPath??"";
    receipt["customBgMode"]=UiTheme.CustomBackgroundMode;
   }
   if(Environment.GetEnvironmentVariable("AANDM_MULTI_SMOKE")=="1"&&coreReady)receipt["probe"]=new JavaScriptSerializer().DeserializeObject(await ExecuteScriptAsync("window.multiProbe||null"));
   if(!string.IsNullOrEmpty(c.Id)){if(commandReceipts.Count>512)commandReceipts.Clear();commandReceipts[c.Id]=receipt;}return receipt;
  }
  private async Task ApplyAccountThemeAsync(int theme)
  {
   new AccountProfiles(AppPaths.BaseRoot).SaveTheme(AppPaths.ProfileId,theme);
   numberOptions["uiTheme"]=theme;
   if(extremePerformanceMode)return;
   if(UiTheme.CurrentTheme==theme)return;
   SwitchUiAppearance(activeUiLayout,theme,customBgPath,UiTheme.CustomBackgroundOpacity,
    UiTheme.CustomBackgroundMode,UiTheme.GlassBlur,UiTheme.CustomBackgroundFit);
   if(detachedSidebar!=null&&!detachedSidebar.IsDisposed)detachedSidebar.BackColor=SideColor;
   await ApplyStageWallpaperAsync();
  }
  private void NotifyOtherProfilesExtremePerf(bool enabled)
  {
   if(previewMode)return;
   string root=AppPaths.BaseRoot;
   string selfId=AppPaths.ValidateProfileId(AppPaths.ProfileId??"main");
   Task.Run(async delegate{
    try{
     var profiles=new AccountProfiles(root);
     var list=profiles.Read();
     foreach(var p in list){
      if(p==null||string.IsNullOrEmpty(p.Id))continue;
      string id;
      try{id=AppPaths.ValidateProfileId(p.Id);}catch{continue;}
      if(id==selfId)continue;
      if(!profiles.IsRunning(id))continue;
      try{
       await AccountCommand.Send(root,id,new TaskCommand{
        Id=Guid.NewGuid().ToString("N"),
        Action="setExtremePerf",
        ExtremePerformance=enabled
       });
      }catch{}
     }
    }catch{}
   });
  }
  private void SetDockLayout(bool value){var root=sidebarScroll.Parent as TableLayoutPanel;if(root!=null)root.ColumnStyles[0].Width=value?0:SidebarWidth;sidebarScroll.Visible=!value;PerformLayout();}
  private void ShowDockSidebar(){
   if(dockHost==0){RestoreFromBackground();return;}if(detachedSidebar!=null&&!detachedSidebar.IsDisposed){detachedSidebar.Activate();return;}
   var root=sidebarScroll.Parent;detachedSidebar=new CompositedForm{Text="助手面板 · "+new AccountProfiles(AppPaths.BaseRoot).Label(AppPaths.ProfileId),ClientSize=new Size(SidebarWidth+6,860),MinimumSize=new Size(SidebarWidth+16,680),StartPosition=FormStartPosition.CenterScreen,BackColor=SideColor,Icon=Icon};
   root.Controls.Remove(sidebarScroll);detachedSidebar.Controls.Add(sidebarScroll);sidebarScroll.Visible=true;sidebarScroll.Dock=DockStyle.Fill;
   detachedSidebar.FormClosed+=delegate{detachedSidebar.Controls.Remove(sidebarScroll);((TableLayoutPanel)root).Controls.Add(sidebarScroll,0,0);sidebarScroll.Visible=dockHost==0;detachedSidebar=null;};detachedSidebar.Show();
  }
  private async Task UndockAccount(){
   if(dockHost==0)return;DockWindow.SetParent(Handle,IntPtr.Zero);DockWindow.SetChild(Handle,false);dockHost=0;if(detachedSidebar!=null)detachedSidebar.Close();FormBorderStyle=FormBorderStyle.Sizable;ShowInTaskbar=true;MinimumSize=new Size(1080,700);SetDockLayout(false);Size=new Size(1440,960);Show();
   await SetSharedWallpaperAsync(null);
  }
  private void DisposeTaskCommands(){commandServerClosed=true;if(commandServer!=null)commandServer.Dispose();ReleaseSharedWallpaper();if(detachedSidebar!=null)detachedSidebar.Close();}
 }
}
