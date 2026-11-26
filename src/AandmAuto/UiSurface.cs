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

        internal static void PaintWhole(Control control,PaintEventArgs e,Action<Graphics> paint)
        {
            if(control.Width<1 || control.Height<1)return;
            if (UiTheme.EngineFrame != null && e.ClipRectangle.Contains(control.ClientRectangle))
            {
                GraphicsState state = e.Graphics.Save();
                try { paint(e.Graphics); }
                finally { e.Graphics.Restore(state); }
                return;
            }
            // Render corners, shadows and text against one complete backdrop.
            // Copying a finished opaque image makes partial invalidations equal
            // to a full repaint, including when the parent DC is translated.
            using(var layer=new Bitmap(control.Width,control.Height)) {
                using(var g=Graphics.FromImage(layer))paint(g);
                e.Graphics.DrawImageUnscaled(layer,Point.Empty);
            }
        }

        private sealed class FrameBackdrop
        {
            internal Bitmap Image;
            internal long Frame = -1;
            internal Rectangle Area;

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
            if (UiTheme.EngineFrame == null || owner.Width <= 0 || owner.Height <= 0)
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
            if (cache.Frame != UiTheme.WallpaperFrameSequence || cache.Area != area)
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
            }

            Rectangle clip = Rectangle.Intersect(owner.ClientRectangle, Rectangle.Ceiling(graphics.VisibleClipBounds));
            if (clip.Width > 0 && clip.Height > 0)
                graphics.DrawImage(cache.Image, clip, clip, GraphicsUnit.Pixel);
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
            using(var brush=new SolidBrush(Color.FromArgb(63,51,32)))graphics.FillRectangle(brush,control.ClientRectangle);
        }
    }
}
