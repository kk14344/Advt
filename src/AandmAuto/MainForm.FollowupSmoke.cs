using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        // End-to-end check for the two independent follow-up switches: upgrade
        // migration, immediate cancellation, Windows task rebuild and late
        // receipts. The fixture file is written by tools/test_followup_142.ps1.
        private async Task RunFollowupSmokeAsync()
        {
            keepScheduledWindowOpen=true;
            pollTimer.Stop();dailyTaskTimer.Stop();
            var checks=new Dictionary<string,bool>();
            string error="";
            try
            {
                await Task.Yield();
                string sid=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                checks["upgradeKeepsMineOn"]=scheduleBook.MineFollowup;
                checks["upgradeKeepsEscortOff"]=!scheduleBook.EscortFollowup;
                checks["upgradeEnablesRefine"]=scheduleBook.RefineFollowup;
                checks["disabledEscortRunDropped"]=!scheduleBook.Harvest.Exists(r=>r.Task=="escort");
                checks["enabledRefineRunKept"]=scheduleBook.Harvest.Exists(r=>r.Task=="refine");

                scheduleBook.RefineFollowup=false;
                checks["refineSwitchRemovesRun"]=scheduleBook.RemoveDisabledHarvests()
                    &&!scheduleBook.Harvest.Exists(r=>r.Task=="refine");
                SaveSchedule();SyncHarvestWindowsTask();
                checks["windowsTaskRebuilt"]=TriggerCount(scheduleBook,sid)==0;

                // A receipt that arrives after the switch was turned off must not
                // recreate the run.
                RecordHarvestUpdates(json.Deserialize<Dictionary<string,object>>(
                    "{\"harvest\":{\"refine\":{\"account\":\"TEST\",\"seconds\":900,\"heat\":9600,\"estimated\":false}}}"));
                checks["lateRefineReceiptIgnored"]=!scheduleBook.Harvest.Exists(r=>r.Task=="refine")
                    &&TriggerCount(scheduleBook,sid)==0;

                scheduleBook.RefineFollowup=true;SaveSchedule();SyncHarvestWindowsTask();
                checks["reopenDoesNotReschedule"]=scheduleBook.Harvest.Count==0;
                RecordHarvestUpdates(json.Deserialize<Dictionary<string,object>>(
                    "{\"harvest\":{\"refine\":{\"account\":\"TEST\",\"seconds\":900,\"heat\":9600,\"estimated\":false}}}"));
                checks["nextReceiptSchedulesAgain"]=scheduleBook.Harvest.Count==1
                    &&scheduleBook.Harvest[0].Task=="refine";
                checks["windowsTaskHasOneTrigger"]=TriggerCount(scheduleBook,sid)==1;
                checks["switchesStayIndependent"]=scheduleBook.EnabledHarvest("refine")
                    &&scheduleBook.EnabledHarvest("mine")&&!scheduleBook.EnabledHarvest("escort");
            }
            catch(Exception ex){error=ex.ToString();}
            bool passed=checks.Count==12&&!checks.ContainsValue(false)&&error.Length==0;
            File.WriteAllText(Path.Combine(DataRoot(),"followup-smoke.json"),
                json.Serialize(new {result=passed?"passed":"failed",checks,error}));
            Close();
        }

        private int TriggerCount(ScheduleBook book,string sid)
        {
            string xml=book.CreateHarvestTaskXml(Application.ExecutablePath,sid,DateTime.Now,AppPaths.ProfileId,AppPaths.BaseRoot);
            int count=0,index=0;
            while((index=xml.IndexOf("<TimeTrigger>",index,StringComparison.Ordinal))>=0){count++;index+=13;}
            return count;
        }
    }
}
