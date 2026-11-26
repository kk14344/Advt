using System;
using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private const string BattleReplaySpeedOption = "battleReplaySpeed";
        private const int BattlePlaybackRowHeight = 34;
        private static readonly int[] BattleReplaySpeeds = { 100, 125, 150, 200 };

        private Control BuildPrimaryActionsWithPlayback(Control actions)
        {
            var section = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Margin = Padding.Empty, BackColor = Color.Transparent
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, BattlePlaybackRowHeight));
            section.Controls.Add(actions, 0, 0);

            var playback = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1,
                Margin = new Padding(4, 0, 2, 4), BackColor = Color.Transparent
            };
            playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            for (int i = 0; i < BattleReplaySpeeds.Length; i++)
                playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            playback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            section.Controls.Add(playback, 0, 1);
            Color playbackInk = UiTheme.CurrentTheme == UiTheme.ThemeNativeLight ? TextColor
                : UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? Color.White : Color.FromArgb(255, 238, 204);
            playback.Controls.Add(new SmoothLabel {
                Text = "倍速", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty, BackColor = Color.Transparent,
                ForeColor = playbackInk, Font = UiTheme.GameFont(9f)
            }, 0, 0);

            string[] labels = { "1×", "1.25×", "1.5×", "2×" };
            var speeds = new GameButton[BattleReplaySpeeds.Length];
            for (int i = 0; i < speeds.Length; i++)
            {
                int value = BattleReplaySpeeds[i];
                var speed = new GameButton {
                    Name = "BattleReplaySpeed" + value, Text = labels[i],
                    AccessibleName = "战斗倍速 " + labels[i],
                    Size = new Size(78, 26), Anchor = AnchorStyles.None, Margin = Padding.Empty
                };
                speed.Click += async delegate {
                    if (speed.Selected) return;
                    foreach (var button in speeds)
                    {
                        button.Selected = button == speed;
                        button.Skin = button.Selected ? GameButtonSkin.Gold : GameButtonSkin.Stone;
                    }
                    numberOptions[BattleReplaySpeedOption] = value;
                    SaveOptions();
                    if (!coreReady || IsDisposed) return;
                    try {
                        await ExecuteScriptAsync("window.__MWA_APP && __MWA_APP.setOption("
                            + json.Serialize(BattleReplaySpeedOption) + "," + json.Serialize(value) + ")");
                    }
                    catch (InvalidOperationException) { }
                };
                speed.Selected = GetNumberOption(BattleReplaySpeedOption, 100) == value;
                speed.Skin = speed.Selected ? GameButtonSkin.Gold : GameButtonSkin.Stone;
                speeds[i] = speed;
                playback.Controls.Add(speed, i + 1, 0);
                hints.SetToolTip(speed, "所有实际播放的战斗都使用所选倍速。");
            }

            var skip = OptionCheck("autoSkipBattle", "跳过战斗", 8.5f);
            skip.Name = "MainBattleSkip";
            skip.Margin = new Padding(3, 0, 0, 0);
            skip.LabelColorOverride = playbackInk;
            playback.Controls.Add(skip, 5, 0);
            hints.SetToolTip(skip, "能跳过的战斗优先跳过。");
            return section;
        }
    }
}
