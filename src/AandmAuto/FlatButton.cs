using System.Drawing;
using System.Windows.Forms;
namespace AandmAuto
{
    internal sealed class FlatButton : GameButton
    {
        public Color NormalColor = Color.FromArgb(76,93,34);
        public Color HoverColor = Color.FromArgb(98,118,44);
        public Color PressedColor = Color.FromArgb(58,72,25);
        protected override void OnPaint(PaintEventArgs e)
        {
            Skin=NormalColor.G>NormalColor.R ? GameButtonSkin.Green :
                NormalColor.R>110 ? GameButtonSkin.Gold : GameButtonSkin.Stone;
            base.OnPaint(e);
        }
    }
}
