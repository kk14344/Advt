using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace AandmAuto
{
    // Keep the original font and layout sizes. Rasterize glyphs at 3x, then
    // downsample the transparent text layer, independent of system ClearType.
    internal static class GameText
    {
        internal const int SampleScale=3;
        private sealed class Entry {internal Bitmap Image;internal LinkedListNode<string> Node;}
        private static readonly Dictionary<string,Entry> cache=new Dictionary<string,Entry>();
        private static readonly LinkedList<string> recent=new LinkedList<string>();
        private static readonly object gate=new object();
        private static int pixels;
        private const int MaxPixels=2500000;

        internal static void ClearCache()
        {
            lock(gate) {
                foreach(Entry entry in cache.Values) {
                    if(entry!=null&&entry.Image!=null)entry.Image.Dispose();
                }
                cache.Clear();
                recent.Clear();
                pixels=0;
            }
        }

        internal static void Draw(Graphics g,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags)
        {
            if(g==null||font==null||string.IsNullOrEmpty(text)||bounds.Width<=0||bounds.Height<=0)return;
            if(UiTheme.ExtremePerformanceMode) {
                TextFormatFlags drawFlags=flags;
                if((drawFlags&TextFormatFlags.SingleLine)==0) {
                    bool multiLineText=text.IndexOf('\n')>=0||text.IndexOf('\r')>=0;
                    if(!multiLineText&&(drawFlags&TextFormatFlags.EndEllipsis)!=0) {
                        drawFlags|=TextFormatFlags.SingleLine;
                    } else if(!multiLineText&&(drawFlags&(TextFormatFlags.VerticalCenter|TextFormatFlags.Bottom))!=0) {
                        if(bounds.Height<font.Height*2) {
                            drawFlags|=TextFormatFlags.SingleLine;
                        } else {
                            TextFormatFlags measureFlags=(drawFlags&~(TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform))|TextFormatFlags.SingleLine;
                            Size oneLine=TextRenderer.MeasureText(text,font,new Size(int.MaxValue,int.MaxValue),measureFlags);
                            drawFlags|=(oneLine.Width<=bounds.Width)?TextFormatFlags.SingleLine:TextFormatFlags.WordBreak;
                        }
                    } else {
                        drawFlags|=TextFormatFlags.WordBreak;
                    }
                }
                if((drawFlags&TextFormatFlags.SingleLine)!=0&&(drawFlags&TextFormatFlags.NoPadding)==0) {
                    drawFlags|=TextFormatFlags.NoPadding;
                }
                TextRenderer.DrawText(g,text,font,bounds,color,drawFlags);
                return;
            }
            string key=text+"\u001f"+font.Name+"|"+font.SizeInPoints.ToString("R",CultureInfo.InvariantCulture)+"|"+
                (int)font.Style+"|"+bounds.Width+"|"+bounds.Height+"|"+color.ToArgb()+"|"+(int)flags+"|"+
                g.DpiX.ToString("R",CultureInfo.InvariantCulture)+"|"+g.DpiY.ToString("R",CultureInfo.InvariantCulture);
            lock(gate) {
                Entry entry;
                if(!cache.TryGetValue(key,out entry)) {
                    Bitmap image=Rasterize(text,font,bounds.Size,color,flags,g.DpiX,g.DpiY);
                    if(image.Width*image.Height>MaxPixels) {using(image)g.DrawImageUnscaled(image,bounds.Location);return;}
                    while(recent.Count>0&&(pixels+image.Width*image.Height>MaxPixels||cache.Count>=256)) {
                        string oldest=recent.Last.Value;Entry drop=cache[oldest];recent.RemoveLast();cache.Remove(oldest);
                        pixels-=drop.Image.Width*drop.Image.Height;drop.Image.Dispose();
                    }
                    entry=new Entry{Image=image,Node=recent.AddFirst(key)};cache.Add(key,entry);pixels+=image.Width*image.Height;
                } else {recent.Remove(entry.Node);recent.AddFirst(entry.Node);}
                g.DrawImageUnscaled(entry.Image,bounds.Location);
            }
        }

        private static Bitmap Rasterize(string text,Font font,Size size,Color color,TextFormatFlags flags,float dpiX,float dpiY)
        {
            var image=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
            using(var high=new Bitmap(size.Width*SampleScale,size.Height*SampleScale,PixelFormat.Format32bppPArgb))
            using(var largeFont=new Font(font.FontFamily,font.SizeInPoints*dpiY/72f*SampleScale,font.Style,GraphicsUnit.Pixel))
            using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
            using(var brush=new SolidBrush(color)) {
                format.FormatFlags=StringFormatFlags.MeasureTrailingSpaces;
                if((flags&TextFormatFlags.SingleLine)!=0)format.FormatFlags|=StringFormatFlags.NoWrap;
                format.Alignment=(flags&TextFormatFlags.Right)!=0?StringAlignment.Far:
                    ((flags&TextFormatFlags.HorizontalCenter)!=0?StringAlignment.Center:StringAlignment.Near);
                format.LineAlignment=(flags&TextFormatFlags.Bottom)!=0?StringAlignment.Far:
                    ((flags&TextFormatFlags.VerticalCenter)!=0?StringAlignment.Center:StringAlignment.Near);
                format.Trimming=(flags&TextFormatFlags.EndEllipsis)!=0?StringTrimming.EllipsisCharacter:StringTrimming.None;
                format.HotkeyPrefix=(flags&TextFormatFlags.NoPrefix)!=0?HotkeyPrefix.None:HotkeyPrefix.Show;
                using(var painter=Graphics.FromImage(high)) {
                    painter.Clear(Color.Transparent);painter.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                    painter.CompositingQuality=CompositingQuality.HighQuality;
                    painter.DrawString(text,largeFont,brush,new RectangleF(0,0,high.Width,high.Height),format);
                }
                using(var painter=Graphics.FromImage(image)) {
                    painter.Clear(Color.Transparent);painter.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    painter.PixelOffsetMode=PixelOffsetMode.HighQuality;painter.CompositingQuality=CompositingQuality.HighQuality;
                    painter.DrawImage(high,new Rectangle(0,0,size.Width,size.Height),0,0,high.Width,high.Height,GraphicsUnit.Pixel);
                }
            }
            return image;
        }
    }
}
