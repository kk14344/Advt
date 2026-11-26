using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
    internal class SmoothLabel:Label
    {
        public SmoothLabel(){DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnPaint(PaintEventArgs e)
        {
            UiSurface.PaintWhole(this,e,delegate(Graphics g){
                if(BackColor.A==255)using(var brush=new SolidBrush(BackColor))g.FillRectangle(brush,ClientRectangle);
                else UiSurface.PaintBackdrop(this,g);
                Rectangle box=new Rectangle(Padding.Left,Padding.Top,Width-Padding.Horizontal,Height-Padding.Vertical);
                TextFormatFlags flags=UiSurface.TextFlags;
                if(!UseMnemonic)flags|=TextFormatFlags.NoPrefix;
                if(AutoEllipsis){flags|=TextFormatFlags.EndEllipsis;if(Text==null||Text.IndexOf('\n')<0)flags|=TextFormatFlags.SingleLine;}
                switch(TextAlign){
                    case ContentAlignment.TopCenter:case ContentAlignment.MiddleCenter:case ContentAlignment.BottomCenter:flags|=TextFormatFlags.HorizontalCenter;break;
                    case ContentAlignment.TopRight:case ContentAlignment.MiddleRight:case ContentAlignment.BottomRight:flags|=TextFormatFlags.Right;break;
                }
                switch(TextAlign){
                    case ContentAlignment.MiddleLeft:case ContentAlignment.MiddleCenter:case ContentAlignment.MiddleRight:flags|=TextFormatFlags.VerticalCenter;break;
                    case ContentAlignment.BottomLeft:case ContentAlignment.BottomCenter:case ContentAlignment.BottomRight:flags|=TextFormatFlags.Bottom;break;
                }
                Color ink = Enabled ? ForeColor : SystemColors.GrayText;
                if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass && !UiTheme.ExtremePerformanceMode && BackColor.A == 0 && Enabled)
                {
                    Color shadow = LiquidGlassEngine.GlassTextShadow();
                    if (shadow.A > 0)
                        GameText.Draw(g, Text, Font, new Rectangle(box.X + 1, box.Y + 1, box.Width, box.Height), shadow, flags);
                }
                GameText.Draw(g,Text,Font,box,ink,flags);
            });
        }
    }
}
