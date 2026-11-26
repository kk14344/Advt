using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace AandmAuto
{
    internal sealed class MysteryBudgetEntry
    {
        public string Kind { get; set; }
        public int Amount { get; set; }
        public bool Refunded { get; set; }
    }

    internal sealed class MysteryBudgetDecision
    {
        internal readonly string Status;
        internal readonly int Remaining;
        internal MysteryBudgetDecision(string status, int remaining)
        {
            Status = status;
            Remaining = remaining;
        }
    }

    internal static class MysteryBudgetLedger
    {
        internal static MysteryBudgetDecision Apply(string root, string account, int day,
            string key, string operation, string kind, int amount, int cap)
        {
            if (!Regex.IsMatch(account ?? "", @"\A[0-9]{1,30}\z") || day <= 0
                || !Regex.IsMatch(key ?? "", @"\A[a-z0-9-]{4,100}:[0-9]{1,10}\z")
                || (kind != "buy" && kind != "refresh") || amount <= 0 || cap < 0
                || (operation != "reserve" && operation != "refund"))
            {
                return new MysteryBudgetDecision("invalid", -1);
            }
            string directory = Path.Combine(root, "MysteryShopBudget", account);
            string path = Path.Combine(directory, day.ToString() + ".json");
            using (var mutex = new Mutex(false, AppPaths.SignalName("MysteryShopBudget", root, "main")))
            {
                bool locked = false;
                try
                {
                    try { locked = mutex.WaitOne(500); }
                    catch (AbandonedMutexException) { locked = true; }
                    if (!locked) return new MysteryBudgetDecision("unavailable", -1);
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var entries = File.Exists(path)
                        ? serializer.Deserialize<Dictionary<string, MysteryBudgetEntry>>(File.ReadAllText(path, Encoding.UTF8))
                        : new Dictionary<string, MysteryBudgetEntry>();
                    if (entries == null) return new MysteryBudgetDecision("unavailable", -1);
                    long spent = 0;
                    foreach (var entry in entries.Values)
                    {
                        if (entry == null || entry.Amount <= 0) return new MysteryBudgetDecision("unavailable", -1);
                        if (entry.Kind == kind && !entry.Refunded) spent += entry.Amount;
                    }
                    int remaining = (int)Math.Max(0L, cap - spent);
                    MysteryBudgetEntry existing;
                    if (entries.TryGetValue(key, out existing))
                    {
                        if (existing.Kind != kind || existing.Amount != amount)
                            return new MysteryBudgetDecision("invalid", -1);
                        if (operation == "reserve")
                            return new MysteryBudgetDecision(existing.Refunded ? "invalid" : "ok", remaining);
                        if (existing.Refunded) return new MysteryBudgetDecision("ok", remaining);
                        existing.Refunded = true;
                        Save(directory, path, serializer.Serialize(entries));
                        return new MysteryBudgetDecision("ok", (int)Math.Max(0L, cap - spent + amount));
                    }
                    if (operation == "refund") return new MysteryBudgetDecision("invalid", -1);
                    if (amount > remaining) return new MysteryBudgetDecision("cap", remaining);
                    entries.Add(key, new MysteryBudgetEntry { Kind = kind, Amount = amount });
                    Save(directory, path, serializer.Serialize(entries));
                    return new MysteryBudgetDecision("ok", remaining - amount);
                }
                finally
                {
                    if (locked) mutex.ReleaseMutex();
                }
            }
        }

        private static void Save(string directory, string path, string content)
        {
            Directory.CreateDirectory(directory);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
