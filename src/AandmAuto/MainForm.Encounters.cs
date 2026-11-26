using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private string EncounterClearDaysPath()
        {
            return Path.Combine(AppPaths.BaseRoot, "encounter-clear-days.json");
        }

        private void LoadEncounterClearDays()
        {
            try
            {
                string path = EncounterClearDaysPath();
                if (!File.Exists(path)) return;
                var days = json.Deserialize<Dictionary<string, long>>(File.ReadAllText(path, Encoding.UTF8));
                if (days == null) return;
                foreach (var entry in days)
                {
                    long previous;
                    encounterClearDays.TryGetValue(entry.Key, out previous);
                    if (entry.Value > previous) encounterClearDays[entry.Key] = entry.Value;
                }
            }
            catch (Exception ex)
            {
                AddLog("奇遇完成记录读取失败 · " + ex.Message, YellowColor);
            }
        }

        private void RecordEncounterClearDone(Dictionary<string, object> result)
        {
            if (previewMode) return;
            var entry = GetDictionary(result, "encounterClearDone");
            if (entry == null) return;
            string role = GetString(entry, "role");
            long day;
            if (string.IsNullOrEmpty(role) || !long.TryParse(GetString(entry,"day"), out day) || day <= 0)
                return;
            try
            {
                string mutexName = AppPaths.SignalName("EncounterClearDays", AppPaths.BaseRoot, "main");
                using (var mutex = new Mutex(false, mutexName))
                {
                    bool entered;
                    try { entered = mutex.WaitOne(TimeSpan.FromMilliseconds(500)); }
                    catch (AbandonedMutexException) { entered = true; }
                    if (!entered) throw new IOException("完成记录正在保存");
                    try
                    {
                        LoadEncounterClearDays();
                        long existing;
                        encounterClearDays.TryGetValue(role, out existing);
                        if (day > existing) encounterClearDays[role] = day;
                        SaveEncounterClearDays();
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                AddLog("奇遇已清空，完成记录保存失败 · " + ex.Message, YellowColor);
            }
        }

        private void SaveEncounterClearDays()
        {
            string path = EncounterClearDaysPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json.Serialize(encounterClearDays), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private async Task SyncEncounterClearDaysAsync()
        {
            LoadEncounterClearDays();
            var code = new StringBuilder("return __MWA:set_encounter_clear_days({");
            foreach (var entry in encounterClearDays)
                code.Append('[').Append(LuaStringLiteral(entry.Key)).Append("]=")
                    .Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append(',');
            code.Append("})");
            await ExecuteLuaAsync(code.ToString());
        }
    }
}
