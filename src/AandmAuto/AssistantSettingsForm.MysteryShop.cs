using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class AssistantSettingsForm
    {
        private CheckedListBox mysteryProductList;

        internal void BindMysteryShop(HashSet<string> chosen)
        {
            for (int i = 0; i < mysteryProductList.Items.Count; i++)
            {
                var category = (MysteryShopCategory)mysteryProductList.Items[i];
                mysteryProductList.SetItemChecked(i, category.IsSelected(chosen));
            }
        }

        internal HashSet<string> SelectedMysteryProducts()
        {
            var selected = new HashSet<string>();
            foreach (MysteryShopCategory category in mysteryProductList.CheckedItems)
                selected.UnionWith(category.ProductKeys);
            return selected;
        }

        private void AddMysteryBudget(Panel panel, string key, string label, int x)
        {
            panel.Controls.Add(new SmoothLabel {
                Text = label, Location = new Point(x, 65), Size = new Size(180, 32),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = BodyText, BackColor = Color.Transparent
            });
            int value = Clamp(GetNum(key, 0), 0, 1000000000);
            var budget = new GameNumber {
                Name = key, Minimum = 0, Maximum = 1000000000, Value = value,
                Location = new Point(x + 181, 65), Size = new Size(135, 32)
            };
            budget.ValueChanged += delegate { numbers[key] = budget.Value; };
            panel.Controls.Add(budget);
        }

        private void BuildMysteryShopPage(Panel panel)
        {
            var enabled = OptionCheck(panel, "dailyMysteryShop", "日常清单自动采购神秘商店", 12, 12, 384, false);
            enabled.Name = "DailyMysteryShopCheck";
            enabled.Tag = "shop";
            dailyChecks["dailyMysteryShop"] = enabled;
            AddMysteryBudget(panel, "mysteryBuyBudget", "买货每日血钻上限", 12);
            AddMysteryBudget(panel, "mysteryRefreshBudget", "刷新每日血钻上限", 405);
            panel.Controls.Add(new SmoothLabel {
                Text = "每日额度按账号累计，填 0 时该项不花血钻。",
                Location = new Point(12, 102), Size = new Size(758, 28),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = MutedText, BackColor = Color.Transparent
            });
            panel.Controls.Add(SectionTitle("购买种类", 8, 147));
            mysteryProductList = new GlassCheckedListBox {
                Name = "MysteryProducts", Location = new Point(12, 190), Size = new Size(756, 233),
                Font = UiTheme.GameFont(9f), IntegralHeight = false, CheckOnClick = true,
                BackColor = UiTheme.Palette.InsetColor, ForeColor = BodyText, BorderStyle = BorderStyle.FixedSingle
            };
            foreach (var category in MysteryShopCategory.All)
                mysteryProductList.Items.Add(category, false);
            panel.Controls.Add(mysteryProductList);
            panel.Controls.Add(new SmoothLabel {
                Text = "勾选后，日常采购会在额度内购买上架的对应商品。",
                Location = new Point(12, 426), Size = new Size(756, 42),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = MutedText, BackColor = Color.Transparent
            });
        }
    }
}
