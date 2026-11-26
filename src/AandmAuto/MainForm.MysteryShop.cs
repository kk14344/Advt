using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace AandmAuto
{
    internal sealed class MysteryShopCategory
    {
        internal static readonly MysteryShopCategory[] All = {
            new MysteryShopCategory("宇宙陨铁", "2:58"),
            new MysteryShopCategory("矿工包", "2:29"),
            new MysteryShopCategory("矿石包", "2:25", "2:26", "2:27"),
            new MysteryShopCategory("神秘之石", "1:10000031"),
            new MysteryShopCategory("黏土钥匙", "2:38"),
            new MysteryShopCategory("白银钥匙", "2:39"),
            new MysteryShopCategory("恶魔钥匙", "2:40"),
            new MysteryShopCategory("迷之钥匙", "1:10000048"),
            new MysteryShopCategory("闪金英雄包", "1:10000004")
        };

        private readonly string name;
        internal readonly string[] ProductKeys;

        private MysteryShopCategory(string name, params string[] keys)
        {
            this.name = name;
            ProductKeys = keys;
        }

        internal bool IsSelected(HashSet<string> keys) { return ProductKeys.All(keys.Contains); }

        public override string ToString() { return name; }
    }

    internal sealed partial class MainForm
    {
        private HashSet<string> mysteryProducts = new HashSet<string>();

        private void LoadMysteryShopOptions(Dictionary<string, object> saved)
        {
            mysteryProducts.Clear();
            object value;
            if (saved.TryGetValue("mysteryProducts", out value))
            {
                var keys = new HashSet<string>(json.ConvertToType<string[]>(value) ?? new string[0]);
                foreach (var category in MysteryShopCategory.All.Where(category => category.IsSelected(keys)))
                    mysteryProducts.UnionWith(category.ProductKeys);
            }
        }

        private async void MysteryBudgetReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                if (message == null || GetString(message, "type") != "mysteryBudget") return;
                Uri uri;
                if (!Uri.TryCreate(e.Source, UriKind.Absolute, out uri)
                    || !string.Equals(uri.GetLeftPart(UriPartial.Authority), new Uri(GameUrl).GetLeftPart(UriPartial.Authority),
                        StringComparison.OrdinalIgnoreCase)) return;
                if (activeTask != AssistantTaskKind.Daily) return;
                int currentPage = pageGeneration;
                var core = webView.CoreWebView2;
                string generation = GetString(message, "generation");
                string operation = GetString(message, "operation");
                string kind = GetString(message, "kind");
                int token = GetInt(message, "token");
                int day = GetInt(message, "day");
                string account = GetString(message, "account");
                int amount = GetInt(message, "amount");
                bool enabled;
                options.TryGetValue("dailyMysteryShop", out enabled);
                var decision = new MysteryBudgetDecision("invalid", -1);
                if (token > 0 && (operation == "refund" || (enabled && day == SpendDayNow())))
                {
                    int cap = GetNumberOption(kind == "buy" ? "mysteryBuyBudget" : "mysteryRefreshBudget", 0);
                    var root = AppPaths.BaseRoot;
                    try
                    {
                        decision = await Task.Run(() => MysteryBudgetLedger.Apply(root, account, day,
                            generation + ":" + token, operation, kind, amount, cap));
                    }
                    catch (Exception e1)
                    {
                        AddLog("神秘商店额度未能保存：" + e1.Message, YellowColor);
                        decision = new MysteryBudgetDecision("unavailable", -1);
                    }
                }
                if (currentPage != pageGeneration || IsDisposed) return;
                await core.ExecuteScriptAsync("window.__MWA_MYSTERY_ACK && window.__MWA_MYSTERY_ACK("
                    + json.Serialize(generation) + "," + token + "," + json.Serialize(operation) + ","
                    + json.Serialize(decision.Status) + "," + decision.Remaining + ")");
            }
            catch (Exception) { }
        }
    }
}
