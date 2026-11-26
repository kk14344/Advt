using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
    internal sealed partial class MainForm
    {
        private TableLayoutPanel sidebarLayout;
        private Panel sidebarScroll;
        private readonly Dictionary<string,Action<bool>> collapseSetters=new Dictionary<string,Action<bool>>();

        private void ResizeSidebar()
        {
            // 侧栏现已改为固定高度不滚动，保持 Dock=Fill 自适应窗口高度。
        }

        private void AttachCollapse(Control card,int row,string key,int expandedHeight)
        {
            var body=card.Controls[0] as TableLayoutPanel;
            if(body==null) return;
            Control header=body.GetControlFromPosition(0,0);
            var button=new GameButton {Text="收起",Width=78,Dock=DockStyle.Right,Skin=GameButtonSkin.Stone,
                Font=UiTheme.GameFont(7.5f),Margin=Padding.Empty};
            header.Controls.Add(button);
            var styles=new List<RowStyle>();
            foreach(RowStyle style in body.RowStyles) styles.Add(new RowStyle(style.SizeType,style.Height));
            Action<bool> apply=delegate(bool collapsed) {
                UiSurface.Batch(sidebarScroll,delegate {
                body.SuspendLayout();
                foreach(Control child in body.Controls) if(body.GetRow(child)>0) child.Visible=!collapsed;
                for(int i=1;i<body.RowStyles.Count;i++) {
                    body.RowStyles[i].SizeType=collapsed ? SizeType.Absolute : styles[i].SizeType;
                    body.RowStyles[i].Height=collapsed ? 0 : styles[i].Height;
                }
                sidebarLayout.RowStyles[row].SizeType=SizeType.Absolute;
                sidebarLayout.RowStyles[row].Height=collapsed ? 44 : expandedHeight;
                button.Text=collapsed ? "展开" : "收起";
                options[key]=collapsed;
                body.ResumeLayout(true);ResizeSidebar();
                card.Invalidate(true);
                });
            };
            collapseSetters[key]=apply;
            button.Click+=delegate {apply(!options[key]);if(!previewMode)SaveOptions();};
            apply(options.ContainsKey(key) && options[key]);
        }
    }
}
