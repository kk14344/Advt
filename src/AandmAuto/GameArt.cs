using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace AandmAuto
{
    internal static class GameArt
    {
        private static readonly Dictionary<string,Image> images=new Dictionary<string,Image>();

        public static Image Get(string name)
        {
            Image image;
            if(images.TryGetValue(name,out image)) return image;
            try {
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AandmAuto.art."+name)) {
                    if(stream!=null) using(Image original=Image.FromStream(stream)) image=new Bitmap(original);
                }
            } catch { image=null; }
            images[name]=image;
            return image;
        }

        public static void DrawNinePatch(Graphics graphics,Image image,Rectangle target,int edge)
        {
            DrawNinePatch(graphics,image,target,edge,null);
        }

        public static void DrawNinePatch(Graphics graphics,Image image,Rectangle target,int edge,ImageAttributes attributes)
        {
            if(image==null || target.Width<1 || target.Height<1) return;
            int sx=Math.Min(edge,(image.Width-1)/2),sy=Math.Min(edge,(image.Height-1)/2);
            int dx=Math.Min(sx,target.Width/2),dy=Math.Min(sy,target.Height/2);
            int[] sourceX={0,sx,image.Width-sx,image.Width};
            int[] sourceY={0,sy,image.Height-sy,image.Height};
            int[] targetX={target.Left,target.Left+dx,target.Right-dx,target.Right};
            int[] targetY={target.Top,target.Top+dy,target.Bottom-dy,target.Bottom};
            var oldInterpolation=graphics.InterpolationMode;
            var oldOffset=graphics.PixelOffsetMode;
            var oldCompositing=graphics.CompositingMode;
            graphics.InterpolationMode=InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode=PixelOffsetMode.Half;
            graphics.CompositingMode=CompositingMode.SourceOver;
            for(int y=0;y<3;y++) for(int x=0;x<3;x++) {
                var destination=new Rectangle(targetX[x],targetY[y],targetX[x+1]-targetX[x],targetY[y+1]-targetY[y]);
                if(destination.Width>0 && destination.Height>0) {
                    var source=new Rectangle(sourceX[x],sourceY[y],sourceX[x+1]-sourceX[x],sourceY[y+1]-sourceY[y]);
                    if(attributes==null) graphics.DrawImage(image,destination,source,GraphicsUnit.Pixel);
                    else graphics.DrawImage(image,destination,source.X,source.Y,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
                }
            }
            graphics.InterpolationMode=oldInterpolation;
            graphics.PixelOffsetMode=oldOffset;
            graphics.CompositingMode=oldCompositing;
        }

        // The button textures use a rounded rectangle; the radius is measured
        // once from the transparent corner so the stretched copy and the
        // painted outline share exactly the same shape.
        private static readonly Dictionary<Image,int> radii=new Dictionary<Image,int>();

        public static int CornerRadius(Image image)
        {
            if(image==null) return 4;
            int cached;
            if(radii.TryGetValue(image,out cached)) return cached;
            int radius=4;
            try {
                Bitmap bitmap=image as Bitmap;
                bool created=false;
                if(bitmap==null) { bitmap=new Bitmap(image); created=true; }
                int half=Math.Min(bitmap.Width,bitmap.Height)/2;
                int top=0,left=0;
                while(top<half && bitmap.GetPixel(top,0).A<=32) top++;
                while(left<half && bitmap.GetPixel(0,left).A<=32) left++;
                radius=Math.Max(2,Math.Min(half,Math.Max(top,left)));
                if(created) bitmap.Dispose();
            } catch { radius=4; }
            radii[image]=radius;
            return radius;
        }

        public static void DrawNinePatch(Graphics graphics,Image image,Rectangle target,int sourceEdge,int edge,ImageAttributes attributes)
        {
            if(image==null || target.Width<1 || target.Height<1) return;
            int sx=Math.Min(sourceEdge,(image.Width-1)/2),sy=Math.Min(sourceEdge,(image.Height-1)/2);
            int dx=Math.Min(edge,target.Width/2),dy=Math.Min(edge,target.Height/2);
            int[] sourceX={0,sx,image.Width-sx,image.Width};
            int[] sourceY={0,sy,image.Height-sy,image.Height};
            int[] targetX={target.Left,target.Left+dx,target.Right-dx,target.Right};
            int[] targetY={target.Top,target.Top+dy,target.Bottom-dy,target.Bottom};
            var oldInterpolation=graphics.InterpolationMode;
            var oldOffset=graphics.PixelOffsetMode;
            var oldCompositing=graphics.CompositingMode;
            graphics.InterpolationMode=InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode=PixelOffsetMode.Half;
            graphics.CompositingMode=CompositingMode.SourceOver;
            for(int y=0;y<3;y++) for(int x=0;x<3;x++) {
                var destination=new Rectangle(targetX[x],targetY[y],targetX[x+1]-targetX[x],targetY[y+1]-targetY[y]);
                if(destination.Width>0 && destination.Height>0) {
                    var source=new Rectangle(sourceX[x],sourceY[y],sourceX[x+1]-sourceX[x],sourceY[y+1]-sourceY[y]);
                    if(attributes==null) graphics.DrawImage(image,destination,source,GraphicsUnit.Pixel);
                    else graphics.DrawImage(image,destination,source.X,source.Y,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
                }
            }
            graphics.InterpolationMode=oldInterpolation;
            graphics.PixelOffsetMode=oldOffset;
            graphics.CompositingMode=oldCompositing;
        }

        public static ImageAttributes Overlay(Color color,float amount)
        {
            var matrix=new ColorMatrix();
            matrix.Matrix00=1f;matrix.Matrix11=1f;matrix.Matrix22=1f;matrix.Matrix33=1f;
            var attributes=new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            attributes.SetColorKey(Color.Transparent,Color.Transparent,ColorAdjustType.Default);
            if(color.GetBrightness()>0.55f) {
                float add=amount;
                matrix=new ColorMatrix(new float[][] {
                    new float[]{1,0,0,0,0},new float[]{0,1,0,0,0},new float[]{0,0,1,0,0},
                    new float[]{0,0,0,1,0},new float[]{add,add,add,0,1}
                });
            } else {
                float scale=Math.Max(0f,1f-amount);
                matrix=new ColorMatrix(new float[][] {
                    new float[]{scale,0,0,0,0},new float[]{0,scale,0,0,0},new float[]{0,0,scale,0,0},
                    new float[]{0,0,0,1,0},new float[]{0,0,0,0,1}
                });
            }
            attributes.SetColorMatrix(matrix);
            return attributes;
        }

        public static ImageAttributes Grayscale(float alpha)
        {
            var matrix=new ColorMatrix(new float[][] {
                new float[]{0.30f,0.30f,0.30f,0,0},
                new float[]{0.59f,0.59f,0.59f,0,0},
                new float[]{0.11f,0.11f,0.11f,0,0},
                new float[]{0,0,0,alpha,0},
                new float[]{0,0,0,0,1}
            });
            var attributes=new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            attributes.SetColorKey(Color.Transparent,Color.Transparent,ColorAdjustType.Default);
            return attributes;
        }

        public static GraphicsPath PixelFrame(Rectangle rectangle)
        {
            int x=rectangle.X,y=rectangle.Y,r=rectangle.Right,b=rectangle.Bottom;
            var path=new GraphicsPath();
            path.AddPolygon(new [] {
                new Point(x+6,y),new Point(r-6,y),new Point(r-6,y+2),new Point(r-2,y+2),
                new Point(r-2,y+6),new Point(r,y+6),new Point(r,b-6),new Point(r-2,b-6),
                new Point(r-2,b-2),new Point(r-6,b-2),new Point(r-6,b),new Point(x+6,b),
                new Point(x+6,b-2),new Point(x+2,b-2),new Point(x+2,b-6),new Point(x,b-6),
                new Point(x,y+6),new Point(x+2,y+6),new Point(x+2,y+2),new Point(x+6,y+2)
            });
            path.CloseFigure();
            return path;
        }
    }
}
