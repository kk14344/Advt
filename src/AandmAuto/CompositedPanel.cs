using System;
using System.Windows.Forms;
namespace AandmAuto {
    // Composite the native assistant subtree as one surface. Keep this off the
    // WebView container, whose GPU surface and game frames must stay independent.
    internal sealed class CompositedPanel:Panel {
        public CompositedPanel(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x02000000;return p;}}
    }
    internal class CompositedForm:Form {
        public CompositedForm(){DoubleBuffered=true;}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x02000000;return p;}}
    }
}
