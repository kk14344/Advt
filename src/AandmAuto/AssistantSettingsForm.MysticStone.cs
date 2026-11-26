using System;
using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class AssistantSettingsForm
    {
        private void BuildMysticStoneOptions(Panel page)
        {
            page.Controls.Add(SectionTitle("神秘之石重置矿区", 8, 284));
            var enabled = OptionCheck(page, "autoMysticStoneReset", "自动使用神秘之石", 12, 322, 370, false);
            page.Controls.Add(new SmoothLabel {
                Text = "杂兵触发等级", Location = new Point(402, 322), Size = new Size(165, 32),
                ForeColor = BodyText, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent
            });
            var threshold = new GameNumber {
                Name = "MysticStoneThreshold", Minimum = 2, Maximum = 9999,
                Value = Clamp(GetNum("mysticStoneThreshold", 150), 2, 9999),
                Location = new Point(576, 322), Size = new Size(100, 32), Enabled = enabled.Checked
            };
            threshold.ValueChanged += delegate { numbers["mysticStoneThreshold"] = threshold.Value; };
            page.Controls.Add(threshold);
            page.Controls.Add(new SmoothLabel {
                Text = "重置后杂兵等级", Location = new Point(12, 374), Size = new Size(165, 32),
                ForeColor = BodyText, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent
            });
            var tier = new ComboBox {
                Name = "MysticStoneResetTier", Location = new Point(180, 378), Size = new Size(590, 32),
                DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.GameFont(9f), Enabled = enabled.Checked
            };
            int[] levels = { 1, 100, 200, 300, 350 };
            tier.Items.AddRange(new object[] {
                "1 级 · 1 颗神秘之石",
                "100 级 · 需杂兵 150 级 · 另耗雷管 3、神力矿工包 20",
                "200 级 · 需杂兵 250 级 · 另耗雷管 6、神力矿工包 40",
                "300 级 · 需杂兵 360 级 · 另耗雷管 9、神力矿工包 60",
                "350 级 · 需杂兵 490 级 · 另耗雷管 12、神力矿工包 70"
            });
            tier.SelectedIndex = Math.Max(0, Array.IndexOf(levels, GetNum("mysticStoneResetTier", 1)));
            tier.SelectedIndexChanged += delegate {
                if (tier.SelectedIndex>=0) numbers["mysticStoneResetTier"] = levels[tier.SelectedIndex];
            };
            enabled.CheckedChanged += delegate { threshold.Enabled = tier.Enabled = enabled.Checked; };
            page.Controls.Add(tier);
            page.Controls.Add(new SmoothLabel {
                Text = "达到触发等级且满足档位条件后重置，载入新地图后继续挖掘。",
                Location = new Point(12, 422), Size = new Size(758, 36), ForeColor = MutedText,
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent
            });
        }
    }
}
