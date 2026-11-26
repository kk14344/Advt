using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace AandmAuto
{
    public sealed class ScheduleSettings
    {
        public bool Enabled {get;set;}
        public bool LaunchWhenClosed {get;set;}
        public int Hour {get;set;}
        public int Minute {get;set;}
        public int MiningMinutes {get;set;}
        public List<string> Tasks {get;set;}
        public string LastRunDay {get;set;}
        public ScheduleSettings() {Hour=8;MiningMinutes=30;Tasks=new List<string>{"daily"};LastRunDay="";}
        public static readonly string[,] Catalog={
            {"daily","一键日常"},{"arena","竞技场"},{"void","虚空裂隙"},{"mine","矿山采集与研究"},
            {"workshop","工坊（收取+生产）"},{"collect","收取工坊"},{"produce","补满工坊"},{"guild","工会首领"},
            {"rune","符文（单轮）"},{"destiny","宿命武器"},{"miningBoss","矿区首领"},{"exchange","虚空兑换"},{"autoMine","自动挖矿"}};
        public void Normalize()
        {
            Hour=Math.Max(0,Math.Min(23,Hour));Minute=Math.Max(0,Math.Min(59,Minute));
            MiningMinutes=Math.Max(1,Math.Min(1440,MiningMinutes));
            var ordered=new List<string>();
            for(int i=0;i<Catalog.GetLength(0);i++) if(Tasks!=null && Tasks.Contains(Catalog[i,0])) ordered.Add(Catalog[i,0]);
            Tasks=ordered;
        }
        public bool IsDue(DateTime now)
        {
            return Enabled && Tasks.Count>0 && LastRunDay!=now.ToString("yyyy-MM-dd") && now.TimeOfDay>=new TimeSpan(Hour,Minute,0);
        }
        public string CreateTaskXml(string exe,string sid,DateTime now)
        {return CreateTaskXml(exe,sid,now,"main");}
        public string CreateTaskXml(string exe,string sid,DateTime now,string profile)
        {return CreateTaskXml(exe,sid,now,profile,null);}
        public string CreateTaskXml(string exe,string sid,DateTime now,string profile,string dataRoot)
        {
            profile=AppPaths.ValidateProfileId(profile);
            string arguments="--scheduled-run"+(profile=="main"?"":" --profile "+profile);
            if(!string.IsNullOrEmpty(dataRoot))arguments+=" --data-root "+AppPaths.QuoteArgument(Path.GetFullPath(dataRoot));
            DateTime next=now.Date.AddHours(Hour).AddMinutes(Minute);
            if(next<=now)next=next.AddDays(1);
            string start=next.ToString("yyyy-MM-ddTHH:mm:ss");
            return "<?xml version=\"1.0\" encoding=\"utf-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"+
                "<Triggers><CalendarTrigger><StartBoundary>"+start+"</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger></Triggers>"+
                "<Principals><Principal id=\"User\"><UserId>"+SecurityElement.Escape(sid)+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>"+
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>"+
                "<Actions Context=\"User\"><Exec><Command>"+SecurityElement.Escape(exe)+"</Command><Arguments>"+SecurityElement.Escape(arguments)+"</Arguments><WorkingDirectory>"+
                SecurityElement.Escape(Path.GetDirectoryName(exe))+"</WorkingDirectory></Exec></Actions></Task>";
        }
        public static void UpdateWindowsTask(ScheduleSettings settings,string dataRoot,string exe)
        {
            // One task owned by this assistant and current Windows user.
            string sid=WindowsIdentity.GetCurrent().User.Value;
            string name=WindowsTaskName(sid,AppPaths.BaseRoot,AppPaths.ProfileId);
            bool install=settings.Enabled && settings.LaunchWhenClosed;
            string xml=Path.Combine(dataRoot,"windows-task.xml");
            string args;
            if(install) {
                File.WriteAllText(xml,settings.CreateTaskXml(exe,sid,DateTime.Now,AppPaths.ProfileId,AppPaths.BaseRoot),Encoding.Unicode);
                args="/Create /TN \""+name+"\" /XML \""+xml+"\" /F";
            } else {
                if(File.Exists(xml)) { try { File.Delete(xml); } catch { } }
                args="/Delete /TN \""+name+"\" /F";
            }
            if(ScheduleBook.ShouldSkipWindowsTaskRegistration()) return;
            var info=new ProcessStartInfo("schtasks.exe",args) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=Process.Start(info)) {
                string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();
                if(!p.WaitForExit(10000)) throw new InvalidOperationException("Windows 定时启动设置超时");
                if(p.ExitCode!=0 && install) throw new InvalidOperationException(output.Trim());
            }
        }
        public static string WindowsTaskName(string sid,string root,string id){return "AandmAutoDaily-"+sid+(AppPaths.InstanceName(root,id)=="Local\\AandmAutoDesktopSingleInstance"?"":"-"+AppPaths.ContextKey(root,id));}
    }
}
