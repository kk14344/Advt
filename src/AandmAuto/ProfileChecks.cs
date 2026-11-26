using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace AandmAuto
{
    public static class ProfileChecks
    {
        public static void Run(string directory)
        {
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            var checks=new List<string>();var profiles=new AccountProfiles(Path.Combine(directory,"data"));
            string template=Path.Combine(directory,"template");Directory.CreateDirectory(template);File.WriteAllText(Path.Combine(template,"options.json"),"{\"horizontalStartDepth\":200,\"battleReplaySpeed\":200}");
            var a=profiles.Save(null,"fixture-account-a","fixture-password-a",true,template);
            var b=profiles.Save(null,"fixture-account-b","fixture-password-b",false,template);
            if(a.Id==b.Id||profiles.Root(a.Id)==profiles.Root(b.Id))throw new Exception("Profiles share data");checks.Add("separate profile roots");
            if(profiles.ReadCredential(a.Id).Password!="fixture-password-a"||profiles.ReadCredential(b.Id).Username!="fixture-account-b")throw new Exception("Profile credentials mixed");checks.Add("DPAPI credential round trip");
            if(File.ReadAllText(Path.Combine(profiles.Root(a.Id),"account.dat")).Contains("fixture-password-a")||File.ReadAllText(Path.Combine(directory,"data/account-profiles.json")).Contains("fixture-password"))throw new Exception("Plaintext password");checks.Add("password absent from plaintext files");
            if(profiles.ReadCredential(b.Id).AutoLogin)throw new Exception("Auto login setting leaked");checks.Add("independent login preferences");
            if(!File.Exists(Path.Combine(profiles.Root(a.Id),"options.json"))||File.Exists(Path.Combine(profiles.Root(a.Id),"task-schedule.json")))throw new Exception("Incorrect template migration");checks.Add("copy settings without copying scheduled jobs");
            var newOptions = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                File.ReadAllText(Path.Combine(profiles.Root(a.Id), "options.json")));
            if (newOptions.ContainsKey("battleReplaySpeed") || Convert.ToInt32(newOptions["horizontalStartDepth"]) != 200)
                throw new Exception("A new account inherited another account's replay speed or lost its template settings");
            checks.Add("new accounts start at 1x while retaining other template preferences");
            profiles.Save(b.Id,"fixture-account-b-updated","fixture-password-b",false,null);
            if(!File.Exists(Path.Combine(profiles.Root(b.Id),"browser-reset-required"))||File.Exists(Path.Combine(profiles.Root(a.Id),"browser-reset-required")))throw new Exception("Account switch did not isolate its cache reset");checks.Add("changing account identity marks only its own browser cache for reset");
            File.WriteAllText(Path.Combine(profiles.Root(a.Id),"options.json"),"{}");if(File.ReadAllText(Path.Combine(profiles.Root(b.Id),"options.json"))=="{}")throw new Exception("Shared options");checks.Add("editing one profile leaves another unchanged");
            bool refused=false;try{profiles.Save(null,"fixture-account-a","fixture-password-duplicate",true,null);}catch(InvalidOperationException){refused=true;}if(!refused)throw new Exception("Duplicate account accepted");checks.Add("duplicate username rejected");
            refused=false;try{profiles.Root("../escape");}catch(ArgumentException){refused=true;}if(!refused)throw new Exception("Unsafe profile path");checks.Add("profile identifiers cannot escape their directory");
            string root=Path.Combine(directory,"data");if(AppPaths.InstanceName(root,a.Id)==AppPaths.InstanceName(root,b.Id)||AppPaths.SignalName("ScheduledRun",root,a.Id)==AppPaths.SignalName("ScheduledRun",root,b.Id))throw new Exception("Shared process signals");checks.Add("separate instance and scheduling signals");
            string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AandmAuto");
            if(AppPaths.SignalName("ScheduledRun",standard,"main")!="Local\\AandmAutoScheduledRun")throw new Exception("Legacy schedule signal changed");checks.Add("legacy main-account schedule signal remains compatible");
            using(var mutex=new Mutex(true,AppPaths.InstanceName(root,a.Id))){if(!profiles.IsRunning(a.Id)||profiles.IsRunning(b.Id))throw new Exception("Running profile detection failed");mutex.ReleaseMutex();}checks.Add("running-profile detection");
            using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,AppPaths.SignalName("Activate",root,a.Id))){if(!profiles.Activate(a.Id)||!signal.WaitOne(0))throw new Exception("Activation failed");}checks.Add("existing window activation signal");
            var launch=profiles.LaunchInfo("C:\\test\\helper.exe",a.Id);if(!launch.Arguments.StartsWith("--profile "+a.Id+" --data-root ")||launch.Arguments.Contains("fixture")||launch.EnvironmentVariables["AANDM_DATA_DIR"]!=root)throw new Exception("Credential leaked to process arguments");checks.Add("launch carries only profile ID and base path");
            var schedule=new ScheduleSettings();string xml=schedule.CreateTaskXml("C:\\test\\helper.exe","S-1-test",DateTime.Today,a.Id);if(!xml.Contains("--scheduled-run --profile "+a.Id))throw new Exception("Scheduled launch lost profile");checks.Add("scheduled launch selects its own profile");
            xml=schedule.CreateTaskXml("C:\\test\\helper.exe","S-1-test",DateTime.Today,a.Id,"C:\\账号 设置\\");if(!xml.Contains("--data-root"))throw new Exception("Scheduled task lost data root");
            if(ScheduleSettings.WindowsTaskName("sid",root,a.Id)==ScheduleSettings.WindowsTaskName("sid",root,b.Id))throw new Exception("Scheduled task names collide");checks.Add("separate scheduled task names and quoted data roots");
            Exception fault=null;var threads=new List<Thread>();for(int i=0;i<4;i++){int n=i;var thread=new Thread(delegate(){try{new AccountProfiles(root).Save(null,"fixture-parallel-"+n,"fixture-password-parallel",false,null);}catch(Exception e){fault=e;}});threads.Add(thread);thread.Start();}foreach(var thread in threads)thread.Join();if(fault!=null)throw fault;if(profiles.Read().Count!=7)throw new Exception("Concurrent catalog update lost an entry");checks.Add("concurrent saves preserve all entries");

            string govRoot=profiles.Root(a.Id);
            string webDef=Path.Combine(govRoot,@"WebView2\EBWebView\Default");
            Directory.CreateDirectory(Path.Combine(webDef,"Cache"));
            string cookieFile=Path.Combine(webDef,"Cookies");
            File.WriteAllBytes(cookieFile,new byte[1024]);
            string webCacheFile=Path.Combine(webDef,"Cache","cache.bin");
            File.WriteAllBytes(webCacheFile,new byte[4096]);
            string auditDir=Path.Combine(govRoot,"audit");
            Directory.CreateDirectory(auditDir);
            string oldAudit=Path.Combine(auditDir,"audit-20260101.log");
            string newAudit=Path.Combine(auditDir,"audit-today.log");
            File.WriteAllBytes(oldAudit,new byte[2048]);
            File.SetLastWriteTimeUtc(oldAudit,DateTime.UtcNow.AddDays(-30));
            File.WriteAllBytes(newAudit,new byte[512]);
            string activeBg=Path.Combine(govRoot,"wallpaper-active.png");
            string unusedBg=Path.Combine(govRoot,"wallpaper-unused.png");
            File.WriteAllBytes(activeBg,new byte[1024]);
            File.WriteAllBytes(unusedBg,new byte[3072]);
            File.WriteAllText(Path.Combine(govRoot,"options.json"),new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"customBgPath",activeBg}}));
            DiskUsageReport beforeClean=DiskGovernance.Scan(root,"",14);
            DiskCleanupResult cleaned=DiskGovernance.Clean(root,"",14);
            if(beforeClean.TotalCleanableBytes!=9216||cleaned.EstimatedBytes!=9216||cleaned.FreedBytes!=9216||cleaned.RemainingCleanableBytes!=0)
                throw new Exception("DiskGovernance byte calculation mismatch");
            if(!File.Exists(cookieFile)||!File.Exists(newAudit)||!File.Exists(activeBg)||!File.Exists(Path.Combine(govRoot,"account.dat"))||!File.Exists(Path.Combine(govRoot,"options.json")))
                throw new Exception("DiskGovernance removed protected files");
            if(File.Exists(webCacheFile)||File.Exists(oldAudit)||File.Exists(unusedBg))
                throw new Exception("DiskGovernance left cleanable files behind");
            checks.Add("disk governance cleans only temporary cache and expired audit logs");

            var scheduleJson = new JavaScriptSerializer();
            var interrupted = new ScheduleBook {
                LaunchWhenClosed = true,
                Checkpoint = new ScheduleCheckpoint {
                    Account = "4501", NextStep = 1, StepInFlight = true,
                    Run = new ScheduleRun {
                        Steps = new List<ScheduleStep> {
                            new ScheduleStep { Task = "daily" }, new ScheduleStep { Task = "workshop" }
                        }
                    }
                }
            };
            string scheduleFixture = Path.Combine(directory, "task-schedules-v2.json");
            File.WriteAllText(scheduleFixture, scheduleJson.Serialize(interrupted));
            var restored = scheduleJson.Deserialize<ScheduleBook>(File.ReadAllText(scheduleFixture));
            if (!restored.Checkpoint.IsValid() || !restored.Checkpoint.StepInFlight
                || restored.Checkpoint.Account != "4501"
                || restored.Checkpoint.Run.Steps[restored.Checkpoint.NextStep].Task != "workshop"
                || !ScheduleBook.HasDueFile(directory, DateTime.Now))
                throw new Exception("Scheduled checkpoint did not preserve the remaining steps for restart");
            checks.Add("schedule checkpoint keeps account, unverified step and unstarted work across restart");

            int ledgerDay=20000;
            var aReserved=MysteryBudgetLedger.Apply(root,"4701",ledgerDay,"shop-a:1","reserve","buy",60,100);
            var same=MysteryBudgetLedger.Apply(root,"4701",ledgerDay,"shop-a:1","reserve","buy",60,100);
            var over=MysteryBudgetLedger.Apply(root,"4701",ledgerDay,"shop-b:1","reserve","buy",41,100);
            var refresh=MysteryBudgetLedger.Apply(root,"4701",ledgerDay,"shop-b:2","reserve","refresh",41,100);
            var other=MysteryBudgetLedger.Apply(root,"4702",ledgerDay,"shop-b:3","reserve","buy",90,100);
            var tomorrow=MysteryBudgetLedger.Apply(root,"4701",ledgerDay+1,"shop-b:4","reserve","buy",90,100);
            var refund=MysteryBudgetLedger.Apply(root,"4701",ledgerDay,"shop-a:1","refund","buy",60,100);
            var replay=MysteryBudgetLedger.Apply(root,"4701",ledgerDay,"shop-a:1","reserve","buy",60,100);
            if(aReserved.Status!="ok"||aReserved.Remaining!=40||same.Remaining!=40
                ||over.Status!="cap"||refresh.Remaining!=59||other.Remaining!=10||tomorrow.Remaining!=10
                ||refund.Remaining!=100||replay.Status!="invalid")
                throw new Exception("Mystery shop ledger did not preserve account, category, day or token boundaries");
            checks.Add("mystery shop reservations persist, deduplicate and isolate accounts, days and categories");

            File.WriteAllText(Path.Combine(directory,"profile-test-results.json"),new JavaScriptSerializer().Serialize(new {result="passed",checks=checks,realAccountUsed=false,registeredWindowsTask=false}));
        }
    }
}
