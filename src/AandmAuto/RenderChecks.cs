using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
    // Graphics tests for our controls; no desktop capture or external UI input.
    internal static class RenderChecks
    {
        private static readonly Color Sentinel=Color.Magenta;
        private static void Paint(Control control,Bitmap target,Point origin,Rectangle clip)
        {
            using(var g=Graphics.FromImage(target)) {
                g.TranslateTransform(origin.X,origin.Y);
                g.SetClip(clip);
                var e=new PaintEventArgs(g,clip);
                typeof(Control).GetMethod("OnPaintBackground",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{e});
                typeof(Control).GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{e});
            }
        }
        private static Bitmap Blank(int w,int h)
        {
            var bitmap=new Bitmap(w,h);using(var g=Graphics.FromImage(bitmap))g.Clear(Sentinel);return bitmap;
        }
        private static void Equal(Bitmap a,Bitmap b,string name)
        {
            for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
                if(a.GetPixel(x,y)!=b.GetPixel(x,y))throw new InvalidOperationException(name+" at "+x+","+y);
        }
        private static void SetResearchChoice(Control root,int index,bool value)
        {
            foreach(Control child in root.Controls) {
                var check=child as GameCheckBox;
                if(check!=null && check.Tag is int && (int)check.Tag==index)check.Checked=value;
                SetResearchChoice(child,index,value);
            }
        }
        internal static void Run(string directory)
        {
            var checks=new List<string>();
            using(var panel=new CompositedPanel{Size=new Size(300,200)})using(var host=new Form{ClientSize=new Size(320,220)}){
                host.Controls.Add(panel);host.CreateControl();panel.CreateControl();
                var cp=(CreateParams)typeof(Control).GetProperty("CreateParams",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel,null);
                if((cp.ExStyle&0x02000000)==0)throw new Exception("Assistant subtree is not composited");checks.Add("assistant native subtree composes one completed surface");
                bool nested=false,restored=false;try{UiSurface.Batch(panel,delegate{UiSurface.Batch(panel,delegate{nested=true;});throw new InvalidOperationException("expected");});}catch(InvalidOperationException){restored=true;}
                UiSurface.Batch(panel,delegate{panel.BackColor=Color.Brown;});if(!nested||!restored)throw new Exception("Paint transaction failed");checks.Add("nested and failed layout transactions restore redraw");
            }

            using(var host=new Form {ClientSize=new Size(520,280),BackColor=Color.FromArgb(63,51,32)})
            using(var card=new CardPanel {Location=new Point(12,12),Size=new Size(490,250)}) {
                host.Controls.Add(card);host.CreateControl();card.CreateControl();
                int parentPaints=0;card.Paint+=delegate{parentPaints++;};
                var controls=new Control[] {
                    new GameCheckBox {Text="自动进入矿区与战斗",Checked=true,Size=new Size(290,34)},
                    new GameButton {Text="保存设置",Skin=GameButtonSkin.Green,Size=new Size(180,42)},
                    new GameNumber {Value=120,Size=new Size(96,32)},
                    new GameHeader {Title="矿洞探索与战斗",Hint="保存后生效",Size=new Size(420,32)},
                    new BadgeLabel {Text="已暂停",BackColor=Color.FromArgb(41,33,23),Size=new Size(90,28)},
                    new SmoothLabel {Text="虚空裂隙与集市奖励",BackColor=Color.Transparent,ForeColor=Color.Wheat,Size=new Size(290,34)}
                };
                foreach(Control control in controls) {
                    control.Font=UiTheme.GameFont(9f);control.Location=new Point(26,54);
                    card.Controls.Add(control);control.CreateControl();
                    Point origin=new Point(37,29);var whole=control.ClientRectangle;
                    int w=control.Width+90,h=control.Height+70;
                    using(var full=Blank(w,h))using(var tiled=Blank(w,h))using(var local=Blank(w,h)) {
                        Paint(control,full,origin,whole);
                        Paint(control,local,Point.Empty,whole);
                        for(int y=0;y<whole.Height;y+=7)for(int x=0;x<whole.Width;x+=23)
                            Paint(control,tiled,origin,new Rectangle(x,y,Math.Min(23,whole.Width-x),Math.Min(7,whole.Height-y)));
                        Equal(full,tiled,control.GetType().Name+" partial redraw");checks.Add(control.GetType().Name+" tiled repaint");
                        for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
                            bool inside=x>=origin.X&&y>=origin.Y&&x<origin.X+whole.Width&&y<origin.Y+whole.Height;
                            var expected=inside?local.GetPixel(x-origin.X,y-origin.Y):Sentinel;
                            if(full.GetPixel(x,y).ToArgb()!=expected.ToArgb())throw new InvalidOperationException(control.GetType().Name+" translated clipping "+x+","+y);
                        }
                        checks.Add(control.GetType().Name+" translated clipping");
                        var check=control as GameCheckBox;
                        if(check!=null) {
                            check.Checked=false;Paint(control,full,origin,whole);
                            using(var clean=Blank(w,h)){Paint(control,clean,origin,whole);Equal(full,clean,"checkbox erase");}
                            checks.Add("checkbox previous state erased");
                        }
                        typeof(Control).GetMethod("OnMouseEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{EventArgs.Empty});
                        Paint(control,full,origin,whole);
                        using(var clean=Blank(w,h)){Paint(control,clean,origin,whole);Equal(full,clean,"hover repaint");}
                        checks.Add(control.GetType().Name+" hover repaint");
                    }
                    card.Controls.Remove(control);control.Dispose();
                }
                if(parentPaints!=0)throw new InvalidOperationException("Child replayed parent foreground paint");
                checks.Add("child backdrop never replays parent foreground");
            }
            File.WriteAllText(Path.Combine(directory,"render-checks.json"),new JavaScriptSerializer().Serialize(new {result="passed",checks=checks}));
            FontQualityChecks.Run(directory);
            var researchChecks=new List<string>();
            using(var empty=new ResearchSelectForm(new Dictionary<int,string>(),4095)) {
                if(empty.OptionCount!=12 || empty.SelectedCount!=12)throw new InvalidOperationException("Missing offline research choices");
                researchChecks.Add("all twelve choices exist before login");
                researchChecks.Add("all twelve choices can be selected together");
            }
            using(var partial=new ResearchSelectForm(new Dictionary<int,string>{{11,"战士竞技场·加成（1/100级）"}},9)) {
                if(partial.OptionCount!=12 || partial.SelectionMask!=9 || partial.SelectedCount!=2)throw new InvalidOperationException("Partial research data hid choices");
                researchChecks.Add("partial data still displays all twelve choices");
                researchChecks.Add("multiple choices retain their independent selections");
                SetResearchChoice(partial,11,true);
                if(partial.SelectionMask!=2057 || partial.SelectedCount!=3)throw new InvalidOperationException("Independent research selection failed");
                researchChecks.Add("changing one choice preserves the others");
            }
            using(var none=new ResearchSelectForm(new Dictionary<int,string>(),0)) {
                if(none.SelectedCount!=0 || none.SelectionMask!=0)throw new InvalidOperationException("Research clear selection failed");
                researchChecks.Add("cleared selections remain empty");
            }
            if(MainForm.LegacyResearchMask(11)!=1 || MainForm.LegacyResearchMask(12)!=2 || MainForm.LegacyResearchMask(62)!=2048 || MainForm.LegacyResearchMask(0)!=4095)
                throw new InvalidOperationException("Legacy research choice migration failed");
            researchChecks.Add("legacy single-branch and automatic choices migrate correctly");
            File.WriteAllText(Path.Combine(directory,"research-choice-checks.json"),new JavaScriptSerializer().Serialize(new {result="passed",checks=researchChecks}));
        }
    }
}
