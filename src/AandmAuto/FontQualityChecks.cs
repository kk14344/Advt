using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AandmAuto
{
    internal static class FontQualityChecks
    {
        private static Bitmap Sample(Font font,string text,bool smooth)
        {
            var image=new Bitmap(330,44);
            using(var g=Graphics.FromImage(image)) {
                g.Clear(Color.White);
                var flags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter;
                if(smooth)GameText.Draw(g,text,font,new Rectangle(8,0,314,44),Color.Black,flags);
                else TextRenderer.DrawText(g,text,font,new Rectangle(8,0,314,44),Color.Black,flags);
            }
            return image;
        }
        internal static void Run(string directory)
        {
            var checks=new List<object>();
            using(var comparison=new Bitmap(700,174))using(var canvas=Graphics.FromImage(comparison)) {
                canvas.Clear(Color.FromArgb(64,52,32));
                using(var title=UiTheme.GameFont(10f)) {
                    TextRenderer.DrawText(canvas,"原渲染",title,new Point(12,5),Color.Wheat);
                    TextRenderer.DrawText(canvas,"同一字体 · 3倍采样",title,new Point(362,5),Color.Wheat);
                }
                int row=0;
                foreach(float points in new[]{10.5f,13.5f,9f})using(var font=UiTheme.GameFont(points/1.15f)) {
                    using(var old=Sample(font,"冒险与挖矿 一键日常 Bonus",false))
                    using(var fresh=Sample(font,"冒险与挖矿 一键日常 Bonus",true)) {
                        int gray=0,colored=0;
                        for(int y=0;y<fresh.Height;y++)for(int x=0;x<fresh.Width;x++) {
                            Color c=fresh.GetPixel(x,y);
                            if(c.R>0&&c.R<255)gray++;
                            if(c.R!=c.G||c.G!=c.B)colored++;
                        }
                        if(gray<50||colored!=0)throw new InvalidOperationException("Text antialiasing is missing or has colored fringes");
                        using(var again=Sample(font,"冒险与挖矿 一键日常 Bonus",true)) {
                            for(int y=0;y<fresh.Height;y++)for(int x=0;x<fresh.Width;x++)
                                if(fresh.GetPixel(x,y)!=again.GetPixel(x,y))throw new InvalidOperationException("Cached text changed appearance");
                        }
                        checks.Add(new {points=font.SizeInPoints,family=font.FontFamily.Name,antialiasedPixels=gray,coloredFringes=colored,cacheStable=true});
                        canvas.DrawImageUnscaled(old,10,32+row*46);canvas.DrawImageUnscaled(fresh,360,32+row*46);row++;
                    }
                }
                comparison.Save(Path.Combine(directory,"font-comparison.png"));
            }
            File.WriteAllText(Path.Combine(directory,"font-quality-checks.json"),new JavaScriptSerializer().Serialize(new {
                result="passed",sampleScale=GameText.SampleScale,font=UiTheme.GameFontFamily,fontChanged=false,checks=checks}));
        }
    }
}
