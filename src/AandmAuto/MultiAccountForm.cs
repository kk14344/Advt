using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace AandmAuto {
 internal sealed class MultiAccountForm:Form,IBackdropOwner {
  internal const int MaxAccounts=4;
  internal static MultiAccountForm Current;
  internal int ColumnCount{get{return columns==null?0:columns.ColumnCount;}}
  sealed class Tile {internal string Id;internal Panel Card;internal Panel Slot;internal CheckBox Selected;internal Label Status;internal IntPtr Window;internal bool Connecting;internal Size LastSlotSize;internal bool Busy;}
  readonly AccountProfiles profiles;readonly List<Tile> tiles=new List<Tile>();readonly TableLayoutPanel columns;
  readonly ComboBox task;readonly NumericUpDown minutes;readonly Timer timer;bool polling,closing,detached;
  readonly FlowLayoutPanel bar;
  readonly ComboBox themePicker;
  readonly Button applyThemeButton;
  readonly Label themeStatus;
  readonly Button pickWallpaperButton;
  readonly Button clearWallpaperButton;
  readonly Label wallpaperStatus;
  readonly List<Button> styledButtons = new List<Button>();
  SharedWallpaperPlayer wallpaperPlayer;
  string wallpaperEngineDir = "";
  string sharedWallpaperTitle = "";
  int pageTheme;
  bool extremePerformanceMode;
  bool applyingTheme;
  bool applyingWallpaper;
  internal MultiAccountForm(AccountProfiles store,IEnumerable<string> ids){
   var selected=ids.Select(AppPaths.ValidateProfileId).Distinct().ToList();
   if(selected.Count==0||selected.Count>MaxAccounts)throw new ArgumentException("请选择 1—"+MaxAccounts+" 个账号");
   if(Current!=null&&!Current.IsDisposed)throw new InvalidOperationException("已有同屏窗口，请先关闭它再重新选择账号");Current=this;profiles=store;
   extremePerformanceMode=ExtremePerformanceSettings.Load(profiles.Root("main"));
   Text="冒险与挖矿 · 多账号同屏 "+BuildEdition.AppVersion;Size=new Size(1500,980);MinimumSize=new Size(1000,650);WindowState=FormWindowState.Maximized;BackColor=Color.FromArgb(41,32,20);ForeColor=Color.FromArgb(242,232,205);Font=UiTheme.GameFont(9f);
   DoubleBuffered = true;
   bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Height=88,Padding=new Padding(12,8,8,0),BackColor=Color.Transparent};
   bar.Controls.Add(new SmoothLabel{Text="同步任务",Size=new Size(92,32),TextAlign=ContentAlignment.MiddleLeft});
   task=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=190,Margin=new Padding(6,4,6,0)};for(int i=0;i<ScheduleBook.Catalog.GetLength(0);i++)task.Items.Add(ScheduleBook.Catalog[i,1]);task.SelectedIndex=0;bar.Controls.Add(task);
   bar.Controls.Add(new SmoothLabel{Text="时长/分钟",Size=new Size(100,32),TextAlign=ContentAlignment.MiddleCenter});minutes=new NumericUpDown{Minimum=0,Maximum=10080,Width=70,Margin=new Padding(0,4,8,0)};bar.Controls.Add(minutes);
   AddButton(bar,"同步开始",async()=>await Broadcast("start"));AddButton(bar,"同步停止",async()=>await Broadcast("stop"));
   bar.SetFlowBreak(bar.Controls[bar.Controls.Count-1],true);
   bar.Controls.Add(new SmoothLabel{Text="统一主题",Size=new Size(92,32),TextAlign=ContentAlignment.MiddleLeft});
   themePicker=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=148,Margin=new Padding(6,4,6,0),AccessibleName="同屏统一主题"};
   for(int theme=UiTheme.ThemeClassic;theme<=UiTheme.ThemeLiquidGlass;theme++)themePicker.Items.Add(UiTheme.ThemeName(theme));
   bar.Controls.Add(themePicker);
   applyThemeButton=AddButton(bar,"应用到所有账号",async()=>{await ApplyThemeToPage(themePicker.SelectedIndex);},148);
   applyThemeButton.Enabled=false;
   themeStatus=new SmoothLabel{Size=new Size(420,34),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
   bar.Controls.Add(themeStatus);
   bar.SetFlowBreak(themeStatus,true);
   bar.Controls.Add(new SmoothLabel{Text="同屏壁纸",Size=new Size(92,32),TextAlign=ContentAlignment.MiddleLeft});
   pickWallpaperButton=AddButton(bar,"选择同屏壁纸",async()=>{await PickSharedWallpaperAsync();},148);
   pickWallpaperButton.Enabled=false;
   clearWallpaperButton=AddButton(bar,"恢复各自壁纸",async()=>{await ClearSharedWallpaperAsync();},148);
   clearWallpaperButton.Enabled=false;
   wallpaperStatus=new SmoothLabel{Text=extremePerformanceMode?"极致性能模式已开启：同屏壁纸已停用（关闭极致性能模式后可用）":"当前：各账号使用各自壁纸（同屏壁纸仅本页生效）",Size=new Size(500,34),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
   bar.Controls.Add(wallpaperStatus);
   bar.SetFlowBreak(wallpaperStatus,true);
   var instructions=new SmoothLabel{Text="0 分钟表示直到完成或手动停止。繁忙账号会先排队，最长等待 30 分钟。",Size=new Size(760,28)};
   bar.Controls.Add(instructions);
   columns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=0,RowCount=1,Padding=new Padding(8),BackColor=Color.Transparent};columns.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(columns);Controls.Add(bar);
   foreach(string id in selected){var tile=new Tile{Id=AppPaths.ValidateProfileId(id)};var card=new Panel{Dock=DockStyle.Fill,Margin=new Padding(4)};tile.Card=card;
    var head=new Panel{Dock=DockStyle.Top,Height=48};tile.Selected=new CheckBox{Checked=true,Text=profiles.Label(id),Dock=DockStyle.Fill,Padding=new Padding(8,0,0,0),AutoEllipsis=true};head.Controls.Add(tile.Selected);
    var buttonSlot=new Panel{Dock=DockStyle.Right,Width=132};AddButton(buttonSlot,"助手面板",async()=>await SendTile(tile,"panel"),126);head.Controls.Add(buttonSlot);
    tile.Status=new SmoothLabel{Dock=DockStyle.Bottom,Height=46,Text="正在连接账号…",ForeColor=ForeColor,Padding=new Padding(8,6,0,0)};tile.Slot=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(24,31,32)};tile.Slot.Resize+=delegate{ResizeTile(tile);};
    card.Controls.Add(tile.Slot);card.Controls.Add(head);card.Controls.Add(tile.Status);columns.ColumnCount++;columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/selected.Count));columns.Controls.Add(card,tiles.Count,0);tiles.Add(tile);
   }
   pageTheme=UiTheme.CurrentTheme;
   try{
    int[] selectedThemes=selected.Select(profiles.ReadTheme).Distinct().ToArray();
    if(selectedThemes.Length==1)pageTheme=selectedThemes[0];
   }catch(System.IO.IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(System.Security.SecurityException){}catch(InvalidOperationException){}
   themePicker.SelectedIndex=pageTheme;
   StylePage(pageTheme);
   timer=new Timer{Interval=extremePerformanceMode?3000:1500};timer.Tick+=async delegate{if(polling)return;polling=true;try{await Task.WhenAll(tiles.Select(t=>RefreshTile(t)));UpdatePollInterval();}finally{polling=false;}};
   Shown+=async delegate{await Task.WhenAll(tiles.Select(t=>ConnectTile(t)));if(!closing&&!IsDisposed){applyThemeButton.Enabled=true;pickWallpaperButton.Enabled=!extremePerformanceMode;clearWallpaperButton.Enabled=!extremePerformanceMode&&wallpaperPlayer!=null;timer.Start();}};FormClosing+=OnClosing;
  }
  Button AddButton(Control parent,string label,Func<Task> action,int width=108)
  {
   var button=new Button{Text=label,Size=new Size(width,36),Location=new Point(3,6),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,Cursor=Cursors.Hand};
   button.Click+=async delegate{button.Enabled=false;try{await action();}finally{if(!button.IsDisposed)button.Enabled=true;}};
   parent.Controls.Add(button);
   styledButtons.Add(button);
   return button;
  }

  protected override void OnPaintBackground(PaintEventArgs e){PaintBackdrop(e.Graphics);}

  public void PaintBackdrop(Graphics graphics)
  {
   using(var fill=new SolidBrush(BackColor))graphics.FillRectangle(fill,ClientRectangle);
   if(extremePerformanceMode)return;
   if(wallpaperPlayer!=null&&wallpaperPlayer.Frames.Paint(graphics,ClientSize,Point.Empty,ClientRectangle))
   {
    using(var veil=new SolidBrush(Color.FromArgb(pageTheme==UiTheme.ThemeLiquidGlass?88:136,BackColor)))
     graphics.FillRectangle(veil,ClientRectangle);
    return;
   }
   if(pageTheme==UiTheme.ThemeLiquidGlass)graphics.DrawImage(UiTheme.EnsureDefaultGlassBackdrop(),ClientRectangle);
  }

  internal void OnExtremePerformanceChanged(bool enabled)
  {
   if(IsDisposed||closing)return;
   if(InvokeRequired){BeginInvoke(new Action<bool>(OnExtremePerformanceChanged),enabled);return;}
   if(extremePerformanceMode==enabled)return;
   extremePerformanceMode=enabled;
   if(enabled)
   {
    ReleaseWallpaperPlayer();
    sharedWallpaperTitle="";
    pickWallpaperButton.Enabled=false;
    clearWallpaperButton.Enabled=false;
    wallpaperStatus.Text="极致性能模式已开启：同屏壁纸已停用（关闭极致性能模式后可用）";
   }
   else
   {
    pickWallpaperButton.Enabled=!applyingWallpaper;
    clearWallpaperButton.Enabled=!applyingWallpaper&&wallpaperPlayer!=null;
    wallpaperStatus.Text="当前：各账号使用各自壁纸（同屏壁纸仅本页生效）";
   }
   StylePage(pageTheme);
   UpdatePollInterval();
  }

  void StylePage(int theme)
  {
   pageTheme=theme;
   int effectiveTheme=extremePerformanceMode?UiTheme.ThemeNativeLight:theme;
   UiPalette palette=UiTheme.PaletteFor(effectiveTheme);
   BackColor=palette.BgColor;
   ForeColor=palette.TextColor;
   bar.ForeColor=ForeColor;
   foreach(Control control in bar.Controls)control.ForeColor=ForeColor;
   task.BackColor=minutes.BackColor=themePicker.BackColor=palette.InputBackColor;
   bool glassOrShared=!extremePerformanceMode&&(wallpaperPlayer!=null||effectiveTheme==UiTheme.ThemeLiquidGlass);
   foreach(var tile in tiles){
    tile.Card.BackColor=glassOrShared?Color.FromArgb(effectiveTheme==UiTheme.ThemeLiquidGlass?96:156,palette.CardColor):palette.CardColor;
    tile.Card.ForeColor=ForeColor;
    tile.Selected.ForeColor=ForeColor;
    tile.Status.ForeColor=palette.MutedColor;
    tile.Slot.BackColor=palette.InsetColor;
   }
   foreach(Button button in styledButtons){
    button.BackColor=palette.InsetColor;
    button.ForeColor=ForeColor;
    button.FlatAppearance.BorderColor=palette.AccentColor;
    button.FlatAppearance.MouseOverBackColor=UiTheme.Mix(palette.InsetColor,palette.AccentColor,0.18f);
   }
   applyThemeButton.FlatAppearance.BorderColor=palette.GoldColor;
   WindowStyle.ApplyThemeChrome(this);
   Invalidate(true);
  }

  async Task<int> ApplyThemeToPage(int theme)
  {
   if(applyingTheme||closing)return 0;
   applyingTheme=true;
   themePicker.Enabled=false;
   themeStatus.Text="正在应用主题…";
   try{
    string[] results=await Task.WhenAll(tiles.Select(tile=>ApplyTileTheme(tile,theme)));
    StylePage(theme);
    string[] failures=results.Where(result=>result!=null).ToArray();
    int saved=tiles.Count-failures.Length;
    themeStatus.Text=failures.Length==0
     ?(extremePerformanceMode?"已为 "+saved+" 个账号保存「"+UiTheme.ThemeName(theme)+"」（关闭极致性能模式后生效）":"已为 "+saved+" 个账号保存「"+UiTheme.ThemeName(theme)+"」")
     :"已保存 "+saved+"/"+tiles.Count+" 个账号 · "+string.Join(" · ",failures);
    return saved;
   }finally{themePicker.Enabled=true;applyingTheme=false;}
  }

  async Task<string> ApplyTileTheme(Tile tile,int theme)
  {
   try{
    if(tile.Window==IntPtr.Zero||!DockWindow.IsWindow(tile.Window)){
     if(profiles.IsRunning(tile.Id))throw new InvalidOperationException("请先用当前版本连接账号。");
     profiles.SaveTheme(tile.Id,theme);
    }else{
     var response=await AccountCommand.Send(profiles.Root("main"),tile.Id,new TaskCommand{
      Id=Guid.NewGuid().ToString("N"),Action="setTheme",Theme=theme,Host=Handle.ToInt64()});
     if(!Convert.ToBoolean(response["ok"]))throw new InvalidOperationException(Convert.ToString(response["message"]));
     bool tileExtreme=extremePerformanceMode||(response.ContainsKey("extremePerformance")&&Convert.ToBoolean(response["extremePerformance"]));
     if(!tileExtreme&&Convert.ToInt32(response["theme"])!=theme)throw new InvalidOperationException("账号未确认主题。");
    }
    return null;
   }catch(Exception error){return tile.Selected.Text+" "+error.Message;}
  }
  SharedWallpaperPlayer EnsureWallpaperPlayer()
  {
   if(wallpaperPlayer!=null)return wallpaperPlayer;
   wallpaperPlayer=new SharedWallpaperPlayer(this);
   wallpaperPlayer.FrameChanged+=delegate
   {
    if(!IsDisposed&&!closing)Invalidate(false);
   };
   return wallpaperPlayer;
  }

  async Task PickSharedWallpaperAsync()
  {
   if(extremePerformanceMode||applyingWallpaper||closing)return;
   using(var picker=new WallpaperEnginePickerForm(wallpaperEngineDir,false))
   {
    if(picker.ShowDialog(this)!=DialogResult.OK||picker.SelectedWallpaper==null)return;
    if(!string.IsNullOrEmpty(picker.ResolvedCustomDir))wallpaperEngineDir=picker.ResolvedCustomDir;
    await ApplySharedWallpaperItemAsync(picker.SelectedWallpaper);
   }
  }

  async Task<bool> ApplySharedWallpaperItemAsync(WallpaperEngineItem item)
  {
   if(extremePerformanceMode||item==null||applyingWallpaper||closing)return false;
   applyingWallpaper=true;
   pickWallpaperButton.Enabled=false;
   clearWallpaperButton.Enabled=false;
   wallpaperStatus.Text="正在载入同屏壁纸："+(item.Title??"已选作品")+"…";
   string playbackError=null;
   try
   {
    SharedWallpaperPlayer player=EnsureWallpaperPlayer();
    string source=item.PlaybackSource;
    if(!string.IsNullOrEmpty(source))
    {
     string executable=WallpaperEngineLocator.FindPlaybackExecutable(wallpaperEngineDir);
     if(string.IsNullOrEmpty(executable))
     {
      string staticFallback=item.BestStaticImagePath;
      if(!string.IsNullOrEmpty(staticFallback))
       player.PublishStatic(staticFallback);
      else
       throw new InvalidOperationException("未检测到壁纸引擎，请先打开 Wallpaper Engine 或选择静态壁纸。");
     }
     else
     {
      await player.Play(executable,source);
     }
    }
    else
    {
     string imagePath=item.BestStaticImagePath;
     if(string.IsNullOrEmpty(imagePath))throw new InvalidOperationException("找不到可用的壁纸画面。");
     player.PublishStatic(imagePath);
    }
    sharedWallpaperTitle=string.IsNullOrEmpty(item.Title)?"同屏壁纸":item.Title;
    string[] results=await Task.WhenAll(tiles.Select(tile=>ApplyTileSharedWallpaper(tile,player.Frames.Name)));
    string[] failures=results.Where(result=>result!=null).ToArray();
    int applied=tiles.Count-failures.Length;
    StylePage(pageTheme);
    wallpaperStatus.Text=failures.Length==0
     ?"同屏壁纸生效中："+sharedWallpaperTitle+"（仅本页生效，"+applied+" 个账号共用）"
     :"已同步 "+applied+"/"+tiles.Count+" 个账号 · "+string.Join(" · ",failures);
    return failures.Length==0;
   }
   catch(Exception error)
   {
    playbackError=error.Message;
   }
   finally
   {
    applyingWallpaper=false;
    if(!IsDisposed&&!closing)
    {
     pickWallpaperButton.Enabled=!extremePerformanceMode;
     clearWallpaperButton.Enabled=!extremePerformanceMode&&wallpaperPlayer!=null;
    }
   }
   await ClearSharedWallpaperAsync();
   wallpaperStatus.Text="同屏壁纸未生效："+playbackError;
   return false;
  }

  async Task<bool> ClearSharedWallpaperAsync()
  {
   if(applyingWallpaper||closing)return false;
   applyingWallpaper=true;
   pickWallpaperButton.Enabled=false;
   clearWallpaperButton.Enabled=false;
   wallpaperStatus.Text="正在恢复各账号原有壁纸…";
   try
   {
    ReleaseWallpaperPlayer();
    sharedWallpaperTitle="";
    string[] results=await Task.WhenAll(tiles.Select(tile=>ApplyTileSharedWallpaper(tile,"")));
    string[] failures=results.Where(result=>result!=null).ToArray();
    StylePage(pageTheme);
    wallpaperStatus.Text=failures.Length==0
     ?(extremePerformanceMode?"极致性能模式已开启：同屏壁纸已停用（关闭极致性能模式后可用）":"当前：各账号使用各自壁纸（同屏壁纸仅本页生效）")
     :"部分账号未恢复："+string.Join(" · ",failures);
    return failures.Length==0;
   }
   finally
   {
    applyingWallpaper=false;
    if(!IsDisposed&&!closing)
    {
     pickWallpaperButton.Enabled=!extremePerformanceMode;
     clearWallpaperButton.Enabled=!extremePerformanceMode&&wallpaperPlayer!=null;
    }
   }
  }

  async Task<string> ApplyTileSharedWallpaper(Tile tile,string sharedName)
  {
   try
   {
    if(tile.Window==IntPtr.Zero||!DockWindow.IsWindow(tile.Window))
     return string.IsNullOrEmpty(sharedName)?null:"账号尚未接入同屏窗口";
    var response=await AccountCommand.Send(profiles.Root("main"),tile.Id,new TaskCommand{
     Id=Guid.NewGuid().ToString("N"),Action="sharedWallpaper",SharedWallpaper=sharedName??"",Host=Handle.ToInt64()});
    if(!Convert.ToBoolean(response["ok"]))throw new InvalidOperationException(Convert.ToString(response["message"]));
    return null;
   }
   catch(Exception error){return tile.Selected.Text+" "+error.Message;}
  }

  void ReleaseWallpaperPlayer()
  {
   if(wallpaperPlayer==null)return;
   SharedWallpaperPlayer old=wallpaperPlayer;
   wallpaperPlayer=null;
   old.Dispose();
  }

  async Task ConnectTile(Tile tile){
   tile.Connecting=true;try{
    if(!profiles.IsRunning(tile.Id))profiles.Open(tile.Id);
    Dictionary<string,object> response=null;Exception last=null;
    for(int i=0;i<16&&!closing;i++){try{response=await AccountCommand.Send(profiles.Root("main"),tile.Id,new TaskCommand{Action="dock",Host=Handle.ToInt64()});if(!Convert.ToBoolean(response["ok"]))throw new InvalidOperationException(Convert.ToString(response["message"]));break;}catch(Exception e){last=e;}await Task.Delay(500);}
    if(closing)return;if(response==null||!Convert.ToBoolean(response["ok"]))throw last??new InvalidOperationException("无法连接账号");
    tile.Window=new IntPtr(Convert.ToInt64(response["window"]));tile.LastSlotSize=Size.Empty;DockWindow.SetChild(tile.Window,true);DockWindow.SetParent(tile.Window,tile.Slot.Handle);ResizeTile(tile);tile.Status.Text="已连接，游戏载入后即可同步任务。";
    if(!extremePerformanceMode&&wallpaperPlayer!=null&&wallpaperPlayer.Frames.Sequence>0)await ApplyTileSharedWallpaper(tile,wallpaperPlayer.Frames.Name);
   }catch(Exception e){tile.Status.Text="连接失败："+e.Message+"；请用当前版本重新打开该账号。";}finally{tile.Connecting=false;}
  }
  void UpdatePollInterval()
  {
   if(IsDisposed||closing)return;
   bool anyBusy=tiles.Any(t=>t.Busy);
   int nextInterval=extremePerformanceMode
    ?(anyBusy?3000:(WindowState==FormWindowState.Minimized?10000:6000))
    :(anyBusy?1500:(WindowState==FormWindowState.Minimized?5000:3500));
   if(timer.Interval!=nextInterval)timer.Interval=nextInterval;
  }
  void ResizeTile(Tile tile)
  {
   if(tile.Window==IntPtr.Zero||!DockWindow.IsWindow(tile.Window))return;
   Size slotSize=new Size(Math.Max(1,tile.Slot.ClientSize.Width),Math.Max(1,tile.Slot.ClientSize.Height));
   if(tile.LastSlotSize==slotSize)return;
   tile.LastSlotSize=slotSize;
   DockWindow.MoveWindow(tile.Window,0,0,slotSize.Width,slotSize.Height,true);
  }
  async Task RefreshTile(Tile tile){if(tile.Connecting||closing||tile.Window==IntPtr.Zero)return;await SendTile(tile,"status");ResizeTile(tile);}
  async Task SendTile(Tile tile,string action){try{var response=await AccountCommand.Send(profiles.Root("main"),tile.Id,new TaskCommand{Id=action=="status"?null:Guid.NewGuid().ToString("N"),Action=action,Task=ScheduleBook.Catalog[task.SelectedIndex,0],Minutes=(int)minutes.Value});bool busy=response.ContainsKey("busy")&&Convert.ToBoolean(response["busy"]);int queued=response.ContainsKey("queued")?Convert.ToInt32(response["queued"]):0;tile.Busy=busy||queued>0;if(action=="status"&&response.ContainsKey("extremePerformance")){bool remoteExtreme=Convert.ToBoolean(response["extremePerformance"]);if(remoteExtreme!=extremePerformanceMode)OnExtremePerformanceChanged(remoteExtreme);}if(!IsDisposed)tile.Status.Text=Convert.ToString(response["message"])+(response.ContainsKey("queued")?" · 排队 "+queued:"");}catch(Exception e){if(!IsDisposed)tile.Status.Text="未送达："+e.Message;}}
  async Task Broadcast(string action){var picked=tiles.Where(t=>t.Selected.Checked).ToArray();await Task.WhenAll(picked.Select(t=>SendTile(t,action)));}
  internal void DetachAllImmediate(){foreach(var t in tiles)if(t.Window!=IntPtr.Zero&&DockWindow.IsWindow(t.Window)){DockWindow.SetParent(t.Window,IntPtr.Zero);DockWindow.SetChild(t.Window,false);}}
  internal string SmokeWallpaperStatus { get { return wallpaperStatus.Text; } }
  internal string SmokeWallpaperWindow { get { return wallpaperPlayer == null ? "" : wallpaperPlayer.WindowTitle; } }
  internal long SmokeWallpaperSequence { get { return wallpaperPlayer == null ? 0 : wallpaperPlayer.Frames.Sequence; } }
  internal int SmokeWallpaperSample() { return wallpaperPlayer == null ? 0 : wallpaperPlayer.Frames.SampleHash(); }
  internal bool SmokeReady {get{return tiles.All(t=>t.Window!=IntPtr.Zero&&!t.Connecting&&DockWindow.GetParent(t.Window)==t.Slot.Handle);}}
  internal IEnumerable<string> SmokeIds {get{return tiles.Select(t=>t.Id);}}
  internal IEnumerable<string> SmokeStatuses {get{return tiles.Select(t=>t.Status.Text);}}
  internal async Task SmokeBroadcast(string id,int duration,bool onlyFirst){for(int i=0;i<ScheduleBook.Catalog.GetLength(0);i++)if(ScheduleBook.Catalog[i,0]==id)task.SelectedIndex=i;minutes.Value=duration;for(int i=0;i<tiles.Count;i++)tiles[i].Selected.Checked=!onlyFirst||i==0;await Broadcast("start");}
  internal async Task SmokeStop(){foreach(var t in tiles)t.Selected.Checked=true;await Broadcast("stop");}
  internal async Task<bool> SmokeApplyGlass(){
   foreach(var tile in tiles)tile.Selected.Checked=false;
   themePicker.SelectedIndex=UiTheme.ThemeLiquidGlass;
   return await ApplyThemeToPage(themePicker.SelectedIndex)==tiles.Count&&pageTheme==UiTheme.ThemeLiquidGlass;
  }
  internal async Task<bool> SmokeApplySharedWallpaper(WallpaperEngineItem item)
  {
   return await ApplySharedWallpaperItemAsync(item);
  }
  internal async Task<bool> SmokeClearSharedWallpaper()
  {
   return await ClearSharedWallpaperAsync();
  }
  internal async Task SmokeDetachAsync(){closing=true;timer.Stop();ReleaseWallpaperPlayer();DetachAllImmediate();await Task.WhenAll(tiles.Select(async t=>{try{await AccountCommand.Send(profiles.Root("main"),t.Id,new TaskCommand{Action="undock",Host=Handle.ToInt64()});}catch{}}));detached=true;timer.Dispose();Current=null;}
  async void OnClosing(object sender,FormClosingEventArgs e){if(detached)return;e.Cancel=true;if(closing||applyingTheme||applyingWallpaper)return;await SmokeDetachAsync();Close();}
  protected override void Dispose(bool disposing){if(disposing)ReleaseWallpaperPlayer();if(ReferenceEquals(Current,this))Current=null;base.Dispose(disposing);}
 }
}
