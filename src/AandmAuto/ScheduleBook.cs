using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Security;

namespace AandmAuto
{
    public sealed class ScheduleStep
    {
        public string Task {get;set;}
        public int Minutes {get;set;}
        public override string ToString(){return ScheduleBook.Label(Task)+(Minutes>0?" · 最多 "+Minutes+" 分钟":" · 执行至完成");}
    }
    public sealed class SchedulePlan
    {
        public string Id {get;set;}
        public string Name {get;set;}
        public bool Enabled {get;set;}
        public string Kind {get;set;}
        public string Times {get;set;}
        public string Dates {get;set;}
        public int Weekdays {get;set;}
        public int IntervalMinutes {get;set;}
        public string Anchor {get;set;}
        public string StartDate {get;set;}
        public string EndDate {get;set;}
        public string WindowStart {get;set;}
        public string WindowEnd {get;set;}
        public int MaxWaitMinutes {get;set;}
        public string Cursor {get;set;}
        public List<ScheduleStep> Steps {get;set;}
        public SchedulePlan(){Id=Guid.NewGuid().ToString("N");Name="新计划";Kind="daily";Times="08:00";Dates="";Weekdays=127;IntervalMinutes=60;Anchor=ScheduleBook.Stamp(DateTime.Now);WindowStart="00:00";WindowEnd="23:59";MaxWaitMinutes=30;Steps=new List<ScheduleStep>{new ScheduleStep{Task="daily"}};}
        public override string ToString(){return (Enabled?"✓ ":"○ ")+Name;}
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(Name))throw new ArgumentException("请填写计划名称");
            if(Kind!="daily"&&Kind!="weekly"&&Kind!="dates"&&Kind!="interval")throw new ArgumentException("触发方式无效");
            if(Kind!="interval"&&ScheduleBook.ParseTimes(Times).Count==0)throw new ArgumentException("至少填写一个时间");
            if(Kind=="dates"&&ScheduleBook.ParseDates(Dates).Count==0)throw new ArgumentException("至少填写一个日期");
            if((Kind=="weekly"||Kind=="interval")&&(Weekdays&127)==0)throw new ArgumentException("至少选择一天");
            if(IntervalMinutes<1||IntervalMinutes>10080)throw new ArgumentException("间隔须为 1—10080 分钟");
            DateTime d;if(!string.IsNullOrWhiteSpace(StartDate)&&!ScheduleBook.Date(StartDate,out d))throw new ArgumentException("开始日期格式应为 yyyy-MM-dd");
            if(!string.IsNullOrWhiteSpace(EndDate)&&!ScheduleBook.Date(EndDate,out d))throw new ArgumentException("结束日期格式应为 yyyy-MM-dd");
            if(!string.IsNullOrWhiteSpace(StartDate)&&!string.IsNullOrWhiteSpace(EndDate)&&string.CompareOrdinal(StartDate,EndDate)>0)throw new ArgumentException("结束日期应晚于开始日期");
            ScheduleBook.Time(WindowStart);ScheduleBook.Time(WindowEnd);
            if(MaxWaitMinutes<1||MaxWaitMinutes>10080)throw new ArgumentException("最长等待须为 1—10080 分钟");
            if(Steps==null||Steps.Count==0)throw new ArgumentException("至少添加一个任务");
            foreach(var s in Steps){if(s==null||!ScheduleBook.Known(s.Task))throw new ArgumentException("任务类型无效");if(s.Minutes<0||s.Minutes>10080)throw new ArgumentException("任务时长须为 0—10080 分钟");if((s.Task=="autoMine"||s.Task=="push"||s.Task=="runeLoop")&&s.Minutes==0)throw new ArgumentException("持续挖矿、推关或符文连抽请设置时长");}
        }
        public IEnumerable<DateTime> Occurrences(DateTime after,DateTime now)
        {
            DateTime start=now.AddMinutes(-MaxWaitMinutes);if(after>start)start=after;
            if(Kind=="interval"){
                DateTime anchor;if(!DateTime.TryParse(Anchor,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out anchor))anchor=now.Date;
                double interval=Math.Max(1,IntervalMinutes);long n=Math.Max(0,(long)Math.Floor((start-anchor).TotalMinutes/interval)+1);
                for(DateTime t=anchor.AddMinutes(n*interval);t<=now;t=t.AddMinutes(interval))if(Allowed(t))yield return t;
            }else{
                var times=ScheduleBook.ParseTimes(Times);var dates=Kind=="dates"?ScheduleBook.ParseDates(Dates):null;
                for(DateTime day=start.Date;day<=now.Date;day=day.AddDays(1)){
                    if(Kind=="dates"&&!dates.Contains(day))continue;
                    foreach(var time in times){DateTime t=day.Add(time);if(t>start&&t<=now&&Allowed(t))yield return t;}
                }
            }
        }
        private bool Allowed(DateTime t)
        {
            DateTime d;if(ScheduleBook.Date(StartDate,out d)&&t.Date<d)return false;if(ScheduleBook.Date(EndDate,out d)&&t.Date>d)return false;
            if((Kind=="weekly"||Kind=="interval")&&(Weekdays&(1<<(int)t.DayOfWeek))==0)return false;
            if(Kind=="interval"){var a=ScheduleBook.Time(WindowStart);var b=ScheduleBook.Time(WindowEnd).Add(TimeSpan.FromSeconds(59.999));var v=t.TimeOfDay;if(a<=b?(v<a||v>b):(v<a&&v>b))return false;}
            return true;
        }
    }
    public sealed class ScheduleRun
    {
        public string PlanId {get;set;}
        public string Name {get;set;}
        public DateTime Due {get;set;}
        public int MaxWaitMinutes {get;set;}
        public List<ScheduleStep> Steps {get;set;}
        public bool Expired(DateTime now){return now>Due.AddMinutes(MaxWaitMinutes);}
    }
    public sealed class HarvestRun
    {
        public string Task {get;set;}
        public string Account {get;set;}
        public DateTime DueUtc {get;set;}
        public string Label {get{return Task=="mine"?"矿山续跑":Task=="refine"?"炼化续跑":"矿车续跑";}}
        public double? RefineHeat {get;set;}
        public DateTime RefineMeasuredUtc {get;set;}
    }
    public sealed class ScheduleBook
    {
        public int Version {get;set;}
        public bool LaunchWhenClosed {get;set;}
        public List<SchedulePlan> Plans {get;set;}
        public List<ScheduleRun> Pending {get;set;}
        public bool MineFollowup {get;set;}
        public bool EscortFollowup {get;set;}
        public bool RefineFollowup {get;set;}
        public List<HarvestRun> Harvest {get;set;}
        public bool AutoCheckin {get;set;}
        public long LastCheckinSlot {get;set;}
        private static readonly int[] CheckinHours = {0, 12, 18};
        private static readonly TimeSpan CheckinOffset = TimeSpan.FromHours(8);
        public ScheduleBook(){Version=2;Plans=new List<SchedulePlan>();Pending=new List<ScheduleRun>();Harvest=new List<HarvestRun>();MineFollowup=EscortFollowup=RefineFollowup=true;}
        public static DateTime CheckinSlotUtc(DateTime utcNow)
        {
            DateTime gameTime=utcNow+CheckinOffset;
            int hour=gameTime.Hour>=18?18:gameTime.Hour>=12?12:0;
            return gameTime.Date.AddHours(hour)-CheckinOffset;
        }
        public bool CheckinDue(DateTime utcNow)
        {
            return AutoCheckin && CheckinSlotUtc(utcNow).Ticks>LastCheckinSlot;
        }
        public string CreateCheckinTaskXml(string exe,string sid,DateTime utcNow,string profile,string dataRoot)
        {
            var triggers=new StringBuilder();
            foreach(int hour in CheckinHours){
                DateTime first=(utcNow+CheckinOffset).Date.AddHours(hour)-CheckinOffset;
                if(first<=utcNow)first=first.AddDays(1);
                triggers.Append("<CalendarTrigger><StartBoundary>"+Stamp(first)+"Z</StartBoundary><Enabled>true</Enabled>"+
                    "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>");
            }
            return WindowsTaskXml(exe,sid,profile,dataRoot,triggers.ToString(),"--checkin-run");
        }
        public bool EnabledHarvest(string task){return task=="refine"?RefineFollowup:(task=="mine"?MineFollowup:task=="escort"&&EscortFollowup);}
        // 关闭任一续跑开关后立即丢弃该项已排的续跑；已开始的一轮不在此处理。
        // JavaScriptSerializer 把 "…Z" 读成本地时间，直接和 DateTime.UtcNow 比较会
        // 偏移一个时区（本机 +8 小时）。读档后统一还原成 UTC，Windows 触发时间、
        // 冷却估算和"是否到点"都使用同一基准。
        public void NormalizeHarvestTimes(){
            if(Harvest==null)return;
            foreach(var run in Harvest){
                if(run==null)continue;
                if(run.DueUtc.Kind!=DateTimeKind.Utc)run.DueUtc=run.DueUtc.ToUniversalTime();
                if(run.RefineMeasuredUtc.Kind!=DateTimeKind.Utc)run.RefineMeasuredUtc=run.RefineMeasuredUtc.ToUniversalTime();
            }
        }
        public bool RemoveDisabledHarvests(){
            if(Harvest==null)return false;
            return Harvest.RemoveAll(r=>r==null||!EnabledHarvest(r.Task))>0;
        }
        public HarvestRun DueHarvest(DateTime utcNow,string account=null){
            HarvestRun first=null;
            foreach(var run in Harvest){
                if(!EnabledHarvest(run.Task)||run.DueUtc>utcNow||(account!=null&&account!=run.Account))continue;
                if(first==null||run.DueUtc<first.DueUtc)first=run;
            }
            return first;
        }
        public bool SetHarvest(string task,string account,DateTime? dueUtc){
            if(!EnabledHarvest(task)||string.IsNullOrEmpty(account)||account=="0")return false;
            var current=Harvest.Find(r=>r.Task==task);
            if(!dueUtc.HasValue){return current!=null&&current.Account==account&&Harvest.Remove(current);}
            if(current!=null&&current.Account==account&&Math.Abs((current.DueUtc-dueUtc.Value).TotalSeconds)<2)return false;
            if(current!=null)Harvest.Remove(current);
            Harvest.Add(new HarvestRun{Task=task,Account=account,DueUtc=dueUtc.Value});
            return true;
        }
        public bool SetRefineCooling(string account,DateTime nowUtc,int seconds,double heat,bool estimated)
        {
            if(seconds<=0)return SetHarvest("refine",account,null);
            var previous=Harvest.Find(r=>r.Task=="refine"&&r.Account==account);
            if(!estimated&&heat>=1000&&previous!=null&&previous.RefineHeat.HasValue&&previous.RefineHeat.Value>heat){
                double elapsed=(nowUtc-previous.RefineMeasuredUtc).TotalSeconds;
                if(elapsed>=30&&elapsed<=7*86400){
                    double estimate=elapsed*(heat-999)/(previous.RefineHeat.Value-heat);
                    seconds=(int)Math.Max(30,Math.Min(12*3600,Math.Ceiling(estimate)));
                }
            }
            if(!SetHarvest("refine",account,nowUtc.AddSeconds(seconds)))return false;
            var run=Harvest.Find(r=>r.Task=="refine");
            run.RefineHeat=heat;run.RefineMeasuredUtc=nowUtc;
            return true;
        }
        public static readonly string[,] Catalog={
            {"daily","一键日常"},{"arena","竞技场"},{"void","虚空裂隙"},{"mine","矿山采集与研究"},
            {"workshop","工坊（收取+生产）"},{"guild","工会首领"},{"rune","符文单轮"},
            {"runeLoop","符文持续连抽"},{"destiny","宿命武器"},{"miningBoss","矿区首领"},{"exchange","虚空兑换"},
            {"orepacks","全部矿石包"},{"autoMine","自动挖矿"},{"push","自动推关"}};
        public static bool Known(string id){
            if(id=="collect"||id=="produce")return true;
            for(int i=0;i<Catalog.GetLength(0);i++)if(Catalog[i,0]==id)return true;
            return false;
        }
        public static string Label(string id){
            if(id=="collect")return "收取工坊";
            if(id=="produce")return "补满工坊";
            for(int i=0;i<Catalog.GetLength(0);i++)if(Catalog[i,0]==id)return Catalog[i,1];
            return id;
        }
        public static string Stamp(DateTime value){return value.ToString("s",CultureInfo.InvariantCulture);}
        public static bool Date(string value,out DateTime d){return DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out d);}
        public static TimeSpan Time(string value){DateTime d;if(!DateTime.TryParseExact((value??"").Trim(),"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out d))throw new ArgumentException("时间格式应为 HH:mm，例如 08:00");return d.TimeOfDay;}
        public static List<TimeSpan> ParseTimes(string text){var values=new SortedSet<TimeSpan>();foreach(var v in (text??"").Split(new[]{',','，',';','；',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries))values.Add(Time(v));return new List<TimeSpan>(values);}
        public static HashSet<DateTime> ParseDates(string text){var values=new HashSet<DateTime>();foreach(var v in (text??"").Split(new[]{',','，',';','；',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries)){DateTime d;if(!Date(v,out d))throw new ArgumentException("日期格式应为 yyyy-MM-dd");values.Add(d);}return values;}
        public bool CollectDue(DateTime now)
        {
            bool changed=false;foreach(var p in Plans){if(!p.Enabled)continue;DateTime after;if(!DateTime.TryParse(p.Cursor,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out after)){p.Cursor=Stamp(now);changed=true;continue;}
                foreach(var due in p.Occurrences(after,now)){bool exists=Pending.Exists(r=>r.PlanId==p.Id&&r.Due==due);if(!exists){Pending.Add(new ScheduleRun{PlanId=p.Id,Name=p.Name,Due=due,MaxWaitMinutes=p.MaxWaitMinutes,Steps=p.Steps.ConvertAll(s=>new ScheduleStep{Task=s.Task,Minutes=s.Minutes})});changed=true;}}
                // The cursor is persisted with queue changes, so restarts cannot replay enqueued runs.
                p.Cursor=Stamp(now);
            }
            Pending.Sort((a,b)=>{int c=a.Due.CompareTo(b.Due);return c!=0?c:string.CompareOrdinal(a.PlanId,b.PlanId);});return changed;
        }
        public static ScheduleBook Migrate(ScheduleSettings old,DateTime now)
        {
            old.Normalize();var b=new ScheduleBook{LaunchWhenClosed=old.LaunchWhenClosed};var p=new SchedulePlan{Name="原每日计划",Enabled=old.Enabled,Times=old.Hour.ToString("00")+":"+old.Minute.ToString("00"),Cursor=Stamp(old.LastRunDay==now.ToString("yyyy-MM-dd")?now:now.AddMinutes(-30)),Steps=new List<ScheduleStep>()};
            foreach(string id in old.Tasks)p.Steps.Add(new ScheduleStep{Task=id,Minutes=id=="autoMine"?old.MiningMinutes:0});b.Plans.Add(p);return b;
        }
        public string CreateTaskXml(string exe,string sid,DateTime now,string profile,string dataRoot)
        {
            var triggers=new StringBuilder();int count=0;
            foreach(var p in Plans){if(!p.Enabled)continue;p.Validate();DateTime last;if(Date(p.EndDate,out last)&&last.Date<now.Date)continue;
                if(p.Kind=="interval"){
                    DateTime anchor;if(!DateTime.TryParse(p.Anchor,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out anchor))anchor=now;
                    DateTime start;if(Date(p.StartDate,out start)&&start>anchor)anchor=anchor.AddMinutes(Math.Ceiling((start-anchor).TotalMinutes/p.IntervalMinutes)*p.IntervalMinutes);
                    string boundary="";DateTime end;if(Date(p.EndDate,out end))boundary="<EndBoundary>"+end.AddDays(1).AddSeconds(-1).ToString("s")+"</EndBoundary>";
                    triggers.Append("<TimeTrigger><Repetition><Interval>PT"+p.IntervalMinutes+"M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>"+Stamp(anchor)+"</StartBoundary>"+boundary+"<Enabled>true</Enabled></TimeTrigger>");count++;
                }else if(p.Kind=="dates"){
                    var ordered=new List<DateTime>(ParseDates(p.Dates));ordered.Sort();foreach(var day in ordered)foreach(var time in ParseTimes(p.Times)){
                        var date=day.Add(time);if(date<now.AddMinutes(-p.MaxWaitMinutes))continue;
                        triggers.Append("<TimeTrigger><StartBoundary>"+Stamp(date)+"</StartBoundary><Enabled>true</Enabled></TimeTrigger>");count++;
                    }
                }else foreach(var time in ParseTimes(p.Times)){
                    DateTime first=now.Date.Add(time),start;if(Date(p.StartDate,out start)&&start>first)first=start.Add(time);
                    string end="";DateTime until;if(Date(p.EndDate,out until))end="<EndBoundary>"+Stamp(until.AddDays(1).AddSeconds(-1))+"</EndBoundary>";
                    triggers.Append("<CalendarTrigger><StartBoundary>"+Stamp(first)+"</StartBoundary>"+end+"<Enabled>true</Enabled>");
                    if(p.Kind=="weekly"){
                        string[] names={"Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"};triggers.Append("<ScheduleByWeek><WeeksInterval>1</WeeksInterval><DaysOfWeek>");
                        for(int i=0;i<7;i++)if((p.Weekdays&(1<<i))!=0)triggers.Append("<"+names[i]+"/>");triggers.Append("</DaysOfWeek></ScheduleByWeek>");
                    }else triggers.Append("<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>");
                    triggers.Append("</CalendarTrigger>");count++;
                }
            }
            if(count>48)throw new ArgumentException("Windows 定时启动最多支持 48 个触发器，请减少时间或日期组合；助手打开时不受此限制。");
            return WindowsTaskXml(exe,sid,profile,dataRoot,triggers.ToString());
        }
        public string CreateHarvestTaskXml(string exe,string sid,DateTime now,string profile,string dataRoot){
            var triggers=new StringBuilder();
            foreach(var run in Harvest){
                if(!EnabledHarvest(run.Task))continue;
                DateTime due=run.DueUtc.ToLocalTime();
                // Task Scheduler stores whole seconds. Round up so the launch gate sees the run as due.
                due=due<=now?now.AddSeconds(5):due.AddSeconds(1);
                triggers.Append("<TimeTrigger><StartBoundary>"+Stamp(due)+"</StartBoundary><Enabled>true</Enabled></TimeTrigger>");
            }
            string launch=Harvest.Exists(r=>r.Task=="refine")?"--harvest-run":"--scheduled-run";
            return WindowsTaskXml(exe,sid,profile,dataRoot,triggers.ToString(),launch);
        }
        private static string WindowsTaskXml(string exe,string sid,string profile,string dataRoot,string triggers,string launch="--scheduled-run"){
            string args=launch+(profile=="main"?"":" --profile "+AppPaths.ValidateProfileId(profile))+" --data-root "+AppPaths.QuoteArgument(Path.GetFullPath(dataRoot));
            return "<?xml version=\"1.0\" encoding=\"utf-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers>"+triggers+"</Triggers><Principals><Principal id=\"User\"><UserId>"+SecurityElement.Escape(sid)+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings><Actions Context=\"User\"><Exec><Command>"+SecurityElement.Escape(exe)+"</Command><Arguments>"+SecurityElement.Escape(args)+"</Arguments><WorkingDirectory>"+SecurityElement.Escape(Path.GetDirectoryName(exe))+"</WorkingDirectory></Exec></Actions></Task>";
        }
        internal static bool HasDueFile(string root,DateTime now){
            string file=Path.Combine(root,"task-schedules-v2.json");if(!File.Exists(file))return true;
            try{var book=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ScheduleBook>(File.ReadAllText(file));book.NormalizeHarvestTimes();if(book.DueHarvest(now.ToUniversalTime())!=null)return true;if(!book.LaunchWhenClosed)return false;book.CollectDue(now);return book.Pending.Exists(r=>!r.Expired(now));}catch{return true;}
        }
        internal static bool HasDueCheckinFile(string root,DateTime utcNow)
        {
            try {
                string file=Path.Combine(root,"task-schedules-v2.json");
                if(!File.Exists(file))return false;
                var book=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ScheduleBook>(File.ReadAllText(file));
                return book!=null&&book.CheckinDue(utcNow);
            } catch {return false;}
        }
        internal static void UpdateCheckinWindowsTask(ScheduleBook book,string dataRoot,string exe)
        {
            string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string name=ScheduleSettings.WindowsTaskName(sid,AppPaths.BaseRoot,AppPaths.ProfileId)+"-Checkin";
            string xml=book.AutoCheckin?book.CreateCheckinTaskXml(exe,sid,DateTime.UtcNow,AppPaths.ProfileId,AppPaths.BaseRoot):null;
            ApplyWindowsTask(name,Path.Combine(dataRoot,"windows-checkin-task.xml"),xml);
        }
        internal static void UpdateHarvestWindowsTask(ScheduleBook book,string dataRoot,string exe){
            string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string name=ScheduleSettings.WindowsTaskName(sid,AppPaths.BaseRoot,AppPaths.ProfileId)+"-Harvest";
            bool install=book.Harvest.Exists(r=>book.EnabledHarvest(r.Task));
            string xml=install?book.CreateHarvestTaskXml(exe,sid,DateTime.Now,AppPaths.ProfileId,AppPaths.BaseRoot):null;
            ApplyWindowsTask(name,Path.Combine(dataRoot,"windows-harvest-task.xml"),xml);
        }
        public static void UpdateWindowsTask(ScheduleBook book,string dataRoot,string exe)
        {
            string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string name=ScheduleSettings.WindowsTaskName(sid,AppPaths.BaseRoot,AppPaths.ProfileId);
            bool install=book.LaunchWhenClosed&&book.Plans.Exists(p=>p.Enabled);
            string xml=install?book.CreateTaskXml(exe,sid,DateTime.Now,AppPaths.ProfileId,AppPaths.BaseRoot):null;
            ApplyWindowsTask(name,Path.Combine(dataRoot,"windows-task.xml"),xml);
        }
        internal static bool ShouldSkipWindowsTaskRegistration(){
            if(Program.NoWindowsTaskRegistration||Environment.GetEnvironmentVariable("AANDM_NO_SCHTASKS")=="1")return true;
            if(Environment.GetEnvironmentVariable("AANDM_ALLOW_SCHTASKS")=="1")return false;
            string[] smokeFlags=new string[]{
                "AANDM_SMOKE",
                "AANDM_FOLLOWUP_SMOKE",
                "AANDM_SCHEDULE_SMOKE",
                "AANDM_CHECKIN_SMOKE",
                "AANDM_REFINE_SMOKE",
                "AANDM_MULTI_SMOKE",
                "AANDM_BACKGROUND_SMOKE",
                "AANDM_PUBLIC_SMOKE",
                "AANDM_PUBLIC_INPUT_SMOKE"
            };
            for(int i=0;i<smokeFlags.Length;i++){
                if(Environment.GetEnvironmentVariable(smokeFlags[i])=="1")return true;
            }
            return false;
        }
        private static void ApplyWindowsTask(string name,string path,string xml){
            bool install=xml!=null;
            string args;
            if(install){File.WriteAllText(path,xml,Encoding.Unicode);args="/Create /TN \""+name+"\" /XML \""+path+"\" /F";}
            else{if(File.Exists(path)){try{File.Delete(path);}catch{}}args="/Delete /TN \""+name+"\" /F";}
            if(ShouldSkipWindowsTaskRegistration())return;
            using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe",args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(5000))throw new IOException("Windows 定时启动设置超时");
                if(install&&process.ExitCode!=0)throw new IOException((output.Result+error.Result).Trim());
            }
        }
    }
}
