using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AandmAuto
{
    internal interface IBackdropOwner
    {
        void PaintBackdrop(Graphics graphics);
    }

    internal static class UiSurface
    {
        private sealed class BatchState {internal int Depth;internal bool Redraw;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,BatchState> batches=new System.Runtime.CompilerServices.ConditionalWeakTable<Control,BatchState>();
        internal static void Batch(Control control,Action update)
        {
            if(control==null){update();return;}var state=batches.GetOrCreateValue(control);
            bool outer=state.Depth++==0;
            if(outer){state.Redraw=control.IsHandleCreated&&control.Visible;control.SuspendLayout();if(state.Redraw)WindowStyle.SetRedraw(control.Handle,false);}
            try{update();}
            finally{if(--state.Depth==0){try{control.ResumeLayout(true);}finally{if(state.Redraw&&!control.IsDisposed){WindowStyle.SetRedraw(control.Handle,true);control.Invalidate(true);WindowStyle.RedrawAll(control.Handle);}}}}
        }
        internal const TextFormatFlags TextFlags=TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;

        private sealed class PaintLayerCache
        {
            internal Bitmap Image;
            internal int W;
            internal int H;
            internal float DpiX;
            internal float DpiY;

            internal void Release()
            {
                if (Image != null) Image.Dispose();
                Image = null;
                W = 0;
                H = 0;
            }
        }

        private static readonly ConditionalWeakTable<Control, PaintLayerCache> paintLayers =
            new ConditionalWeakTable<Control, PaintLayerCache>();

        internal static void PaintWhole(Control control,PaintEventArgs e,Action<Graphics> paint)
        {
            if(control.Width<1 || control.Height<1)return;
            if (UiTheme.ExtremePerformanceMode)
            {
                // Avoid Graphics.Save() around TextRenderer.DrawText with PreserveGraphicsClipping
                // on translated Graphics (GDI+ state containers break Region.GetHrgn clipping).
                SmoothingMode prevSmoothing = e.Graphics.SmoothingMode;
                InterpolationMode prevInterpolation = e.Graphics.InterpolationMode;
                PixelOffsetMode prevPixelOffset = e.Graphics.PixelOffsetMode;
                CompositingQuality prevCompositing = e.Graphics.CompositingQuality;
                using (Matrix prevTransform = e.Graphics.Transform)
                using (Region prevClip = e.Graphics.Clip)
                {
                    try { paint(e.Graphics); }
                    finally
                    {
                        e.Graphics.Transform = prevTransform;
                        e.Graphics.Clip = prevClip;
                        e.Graphics.SmoothingMode = prevSmoothing;
                        e.Graphics.InterpolationMode = prevInterpolation;
                        e.Graphics.PixelOffsetMode = prevPixelOffset;
                        e.Graphics.CompositingQuality = prevCompositing;
                    }
                }
                return;
            }
            if (UiTheme.EngineFrame != null && e.ClipRectangle.Contains(control.ClientRectangle))
            {
                GraphicsState state = e.Graphics.Save();
                try { paint(e.Graphics); }
                finally { e.Graphics.Restore(state); }
                return;
            }
            // Reuse a per-control Format32bppPArgb offscreen buffer (zero per-frame heap allocation,
            // zero 32bppArgb -> 32bppPArgb software conversion, and 100% partial/tiled repaint equivalence).
            PaintLayerCache cache = paintLayers.GetValue(control, c =>
            {
                var value = new PaintLayerCache();
                c.Disposed += delegate { value.Release(); };
                return value;
            });
            float dpiX = e.Graphics.DpiX;
            float dpiY = e.Graphics.DpiY;
            if (cache.Image == null || cache.W != control.Width || cache.H != control.Height
                || Math.Abs(cache.DpiX - dpiX) > 0.1f || Math.Abs(cache.DpiY - dpiY) > 0.1f)
            {
                cache.Release();
                cache.Image = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppPArgb);
                cache.Image.SetResolution(dpiX, dpiY);
                cache.W = control.Width;
                cache.H = control.Height;
                cache.DpiX = dpiX;
                cache.DpiY = dpiY;
            }
            using (var g = Graphics.FromImage(cache.Image))
            {
                paint(g);
            }
            e.Graphics.DrawImageUnscaled(cache.Image, Point.Empty);
        }

        private sealed class FrameBackdrop
        {
            internal Bitmap Image;
            internal long Frame = -1;
            internal Rectangle Area;
            internal Size Canvas;
            internal int Theme = -1;
            internal int Preset = -1;
            internal int Blur = -1;

            internal void Release()
            {
                if (Image != null) Image.Dispose();
                Image = null;
                Frame = -1;
            }
        }

        private static readonly ConditionalWeakTable<Control, FrameBackdrop> frameBackdrops =
            new ConditionalWeakTable<Control, FrameBackdrop>();

        internal static void PaintFrameBackdrop(Control owner, IBackdropOwner backdrop, Graphics graphics)
        {
            bool useCache = !UiTheme.ExtremePerformanceMode
                && owner.Width > 0 && owner.Height > 0
                && (UiTheme.EngineFrame != null || UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass);
            if (!useCache)
            {
                FrameBackdrop existing;
                if (frameBackdrops.TryGetValue(owner, out existing)) existing.Release();
                backdrop.PaintBackdrop(graphics);
                return;
            }

            int canvasWidth, canvasHeight, x, y;
            UiTheme.GetWindowCanvasMetrics(owner, out canvasWidth, out canvasHeight, out x, out y);
            var area = new Rectangle(x, y, owner.Width, owner.Height);
            FrameBackdrop cache = frameBackdrops.GetValue(owner, control =>
            {
                var value = new FrameBackdrop();
                control.Disposed += delegate { value.Release(); };
                return value;
            });
            var canvas = new Size(canvasWidth, canvasHeight);
            int curTheme = UiTheme.CurrentTheme;
            int curPreset = LiquidGlassEngine.Preset;
            int curBlur = UiTheme.GlassBlur;
            if (cache.Frame != UiTheme.WallpaperFrameSequence || cache.Area != area || cache.Canvas != canvas
                || cache.Theme != curTheme || cache.Preset != curPreset || cache.Blur != curBlur)
            {
                if (cache.Image == null || cache.Image.Size != area.Size)
                {
                    cache.Release();
                    cache.Image = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
                    cache.Image.SetResolution(graphics.DpiX, graphics.DpiY);
                }
                using (var layer = Graphics.FromImage(cache.Image))
                {
                    layer.Clear(owner.BackColor.A == 255 ? owner.BackColor : UiTheme.BackgroundVeilColor);
                    backdrop.PaintBackdrop(layer);
                }
                cache.Frame = UiTheme.WallpaperFrameSequence;
                cache.Area = area;
                cache.Canvas = canvas;
                cache.Theme = curTheme;
                cache.Preset = curPreset;
                cache.Blur = curBlur;
            }

            Rectangle clip = Rectangle.Intersect(owner.ClientRectangle, Rectangle.Ceiling(graphics.VisibleClipBounds));
            if (clip.Width > 0 && clip.Height > 0)
            {
                CompositingMode prevComp = graphics.CompositingMode;
                graphics.CompositingMode = CompositingMode.SourceCopy;
                try
                {
                    if (clip == owner.ClientRectangle)
                        graphics.DrawImageUnscaled(cache.Image, Point.Empty);
                    else
                        graphics.DrawImage(cache.Image, clip, clip, GraphicsUnit.Pixel);
                }
                finally
                {
                    graphics.CompositingMode = prevComp;
                }
            }
        }

        internal static void PaintBackdrop(Control control,Graphics graphics)
        {
            Control owner=control.Parent;
            while(owner!=null) {
                var backdrop=owner as IBackdropOwner;
                if(backdrop!=null) {
                    Point point=owner.PointToClient(control.PointToScreen(Point.Empty));
                    GraphicsState saved=graphics.Save();
                    try {
                        graphics.TranslateTransform(-point.X,-point.Y);
                        PaintFrameBackdrop(owner, backdrop, graphics);
                    } finally {graphics.Restore(saved);}
                    return;
                }
                if(owner.BackColor.A==255 && !(owner is TableLayoutPanel) && !(owner is FlowLayoutPanel)) {
                    using(var brush=new SolidBrush(owner.BackColor))graphics.FillRectangle(brush,control.ClientRectangle);
                    return;
                }
                owner=owner.Parent;
            }
            Color fallback=UiTheme.ExtremePerformanceMode?UiTheme.Palette.BgColor:Color.FromArgb(63,51,32);
            using(var brush=new SolidBrush(fallback))graphics.FillRectangle(brush,control.ClientRectangle);
        }
    }
}
