using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class GlassRichTextBox : RichTextBox
	{
		private long nextBackgroundSample;
        private Bitmap textLayer;
        private bool textLayerDirty = true;
        internal long LastWallpaperPaint { get; private set; }

        internal bool HasPrintedText()
        {
            if (textLayer == null) return false;
            for (int y = 0; y < textLayer.Height; y++)
                for (int x = 0; x < textLayer.Width; x++)
                    if (textLayer.GetPixel(x, y).A != 0)
                        return true;
            return false;
        }
        private const int WmPaint = 0x000F;
        private const int WmEraseBackground = 0x0014;

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintInfo
        {
            internal IntPtr Dc;
            internal int Erase, Left, Top, Right, Bottom, Restore, IncUpdate;
            // PAINTSTRUCT reserves the last 32 bytes for the window manager.
            private long reserved0, reserved1, reserved2, reserved3;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr window, out PaintInfo paint);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr window, ref PaintInfo paint);

        private const uint EmFormatRange = 0x0439;
        private const uint EmGetFirstVisibleLine = 0x00CE;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FormatRange
        {
            internal IntPtr DrawDc, TargetDc;
            internal NativeRect Content, Page;
            internal int FirstChar, LastChar;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr value, ref FormatRange range);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr value, IntPtr unused);

        private const uint EmGetCharFormat = 0x043A;
        private const uint EmSetCharFormat = 0x0444;
        private const uint EmSetLanguageOptions = 0x0478;
        private const uint EmGetLanguageOptions = 0x0479;
        private const uint CfmSize = 0x80000000, CfmFace = 0x20000000, CfmCharset = 0x08000000;
        private const int ScfSelection = 1, ScfAll = 4;
        private const int ImfAutoFont = 0x0002, ImfAutoFontSizeAdjust = 0x0010, ImfDualFont = 0x0080;
        private const byte Gb2312Charset = 134;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CharacterFormat
        {
            internal uint Size, Mask, Effects;
            internal int Height, Offset;
            internal uint Color;
            internal byte Charset, PitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string FaceName;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr value, ref CharacterFormat format);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // The shared font covers Latin and Chinese. RichEdit's default
            // dual-font mode otherwise changes both font and size by script.
            var options = SendMessage(Handle, EmGetLanguageOptions, IntPtr.Zero, IntPtr.Zero).ToInt32();
            options &= ~(ImfAutoFont | ImfAutoFontSizeAdjust | ImfDualFont);
            SendMessage(Handle, EmSetLanguageOptions, IntPtr.Zero, new IntPtr(options));
            ApplyBodyFont();
            RefreshGlassChrome();
        }

        internal void RefreshGlassChrome()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                if (UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass)
                {
                    SetWindowTheme(Handle, LiquidGlassEngine.IsBrightBackdrop() ? "Explorer" : "DarkMode_Explorer", null);
                }
            }
            catch { }
        }

        internal void SetBodyText(string text)
        {
            if (Text == text) return;
            Clear();
            ApplyBodyFont();
            AppendText(text);
            Select(0, 0);
            ScrollToCaret();
        }

        private void ApplyBodyFont()
        {
            var format = new CharacterFormat {
                Size = (uint)Marshal.SizeOf(typeof(CharacterFormat)),
                Mask = CfmSize | CfmFace | CfmCharset,
                FaceName = Font.Name,
                Height = (int)Math.Round(Font.SizeInPoints * 20),
                Charset = Gb2312Charset
            };
            SendMessage(Handle, EmSetCharFormat, IntPtr.Zero, ref format);
            if (TextLength > 0) SendMessage(Handle, EmSetCharFormat, new IntPtr(ScfAll), ref format);
        }

        internal bool SelectionMatchesBodyFont(Font font, out string effective)
        {
            var format = new CharacterFormat { Size = (uint)Marshal.SizeOf(typeof(CharacterFormat)) };
            SendMessage(Handle, EmGetCharFormat, new IntPtr(ScfSelection), ref format);
            effective = format.FaceName + " " + (format.Height / 20f);
            return (format.Mask & (CfmFace | CfmSize)) == (CfmFace | CfmSize)
                && string.Equals(format.FaceName, font.Name, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(format.Height / 20f - font.SizeInPoints) <= .06f;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmPaint && m.WParam == IntPtr.Zero && UiTheme.UsesEngineCanvas(this))
            {
                PaintInfo paint;
                IntPtr dc = BeginPaint(Handle, out paint);
                try
                {
                    Rectangle area = ClientRectangle;
                    if (dc != IntPtr.Zero && area.Width > 0 && area.Height > 0)
                    {
                        using (Graphics graphics = Graphics.FromHdc(dc))
                        using (BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(graphics, area))
                        {
                            buffer.Graphics.Clear(BackColor);
                            UiTheme.DrawCustomBackground(buffer.Graphics, this, area, BackColor);
                            using (var tint = new SolidBrush(Color.FromArgb(112, BackColor)))
                                buffer.Graphics.FillRectangle(tint, area);
                            DrawLogSelection(buffer.Graphics, area);
                            DrawTextLayer(buffer.Graphics, area);
                            buffer.Render(graphics);
                            LastWallpaperPaint = UiTheme.WallpaperFrameSequence;
                        }
                    }
                }
                finally { EndPaint(Handle, ref paint); }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WmEraseBackground && UiTheme.UsesEngineCanvas(this))
            {
                m.Result = new IntPtr(1);
                return;
            }
            // Focus, input, scrolling and RichEdit formatting can change the
            // native foreground even when the text itself has not changed.
            switch (m.Msg)
            {
                case 0x0005: case 0x0007: case 0x0008: case 0x000C:
                case 0x0030: case 0x0114: case 0x0115: case 0x020A:
                case 0x00B1: case 0x00B7: case 0x0443: case 0x0461: case 0x04DE:
                    textLayerDirty = true;
                    break;
            }
            base.WndProc(ref m);
        }

        private void DrawTextLayer(Graphics graphics, Rectangle area)
        {
            if (textLayer == null || textLayer.Size != area.Size ||
                Math.Abs(textLayer.HorizontalResolution - graphics.DpiX) > 0.1f ||
                Math.Abs(textLayer.VerticalResolution - graphics.DpiY) > 0.1f)
            {
                if (textLayer != null) textLayer.Dispose();
                textLayer = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
                textLayer.SetResolution(graphics.DpiX, graphics.DpiY);
                textLayerDirty = true;
            }
            if (textLayerDirty)
            {
                using (Graphics mask = Graphics.FromImage(textLayer))
                {
                    mask.Clear(BackColor);
                    PrintVisibleText(mask, area);
                }
                MakeTextLayerTransparent(area);
                textLayerDirty = false;
            }
            graphics.DrawImageUnscaled(textLayer, area.Location);
        }

        private unsafe void MakeTextLayerTransparent(Rectangle area)
        {
            uint matte = (uint)BackColor.ToArgb() & 0x00ffffff;
            BitmapData data = textLayer.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < area.Height; y++)
                {
                    uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                    for (int x = 0; x < area.Width; x++)
                    {
                        uint rgb = row[x] & 0x00ffffff;
                        row[x] = rgb == matte ? 0 : rgb | 0xff000000;
                    }
                }
            }
            finally { textLayer.UnlockBits(data); }
        }

        private void DrawLogSelection(Graphics graphics, Rectangle area)
        {
            int firstChar = SelectionStart;
            int endChar = firstChar + SelectionLength;
            if (endChar == firstChar) return;
            int firstVisible = GetFirstVisibleLine();
            int firstLine = Math.Max(firstVisible, GetLineFromCharIndex(firstChar));
            int lastLine = GetLineFromCharIndex(endChar - 1);
            using (var highlight = new SolidBrush(Color.FromArgb(190, SystemColors.Highlight)))
            {
                for (int line = firstLine; line <= lastLine; line++)
                {
                    int lineStart = GetFirstCharIndexFromLine(line);
                    if (lineStart < 0) break;
                    int nextStart = GetFirstCharIndexFromLine(line + 1);
                    Point start = GetPositionFromCharIndex(Math.Max(lineStart, firstChar));
                    if (start.Y >= area.Bottom) break;
                    Point end = GetPositionFromCharIndex(endChar);
                    int right = nextStart >= 0 && endChar >= nextStart ? area.Right : end.X;
                    graphics.FillRectangle(highlight, start.X, start.Y,
                        Math.Max(1, right - start.X), Math.Max(1, Font.Height));
                }
            }
        }

        private int GetFirstVisibleLine()
        {
            return SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
        }

        private void PrintVisibleText(Graphics graphics, Rectangle area)
        {
            int line = GetFirstVisibleLine();
            int first = line >= 0 ? GetFirstCharIndexFromLine(line) : -1;
            if (first < 0 || first >= TextLength) return;
            Point start = GetPositionFromCharIndex(first);
            float twipsX = 1440f / graphics.DpiX, twipsY = 1440f / graphics.DpiY;
            var page = new NativeRect { Right = (int)(area.Width * twipsX), Bottom = (int)(area.Height * twipsY) };
            var content = page;
            content.Left = (int)(Math.Max(0, start.X) * twipsX);
            content.Top = (int)(start.Y * twipsY);
            var range = new FormatRange { Content = content, Page = page, FirstChar = first, LastChar = -1 };
            IntPtr dc = graphics.GetHdc();
            try
            {
                range.DrawDc = range.TargetDc = dc;
                SendMessage(Handle, EmFormatRange, new IntPtr(1), ref range);
            }
            finally
            {
                SendMessage(Handle, EmFormatRange, IntPtr.Zero, IntPtr.Zero);
                graphics.ReleaseHdc(dc);
            }
        }

        protected override void OnTextChanged(EventArgs e) { textLayerDirty = true; base.OnTextChanged(e); }
        protected override void OnSelectionChanged(EventArgs e) { textLayerDirty = true; base.OnSelectionChanged(e); }
        protected override void OnBackColorChanged(EventArgs e) { textLayerDirty = true; base.OnBackColorChanged(e); }
        protected override void OnForeColorChanged(EventArgs e) { textLayerDirty = true; base.OnForeColorChanged(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && textLayer != null)
            {
                textLayer.Dispose();
                textLayer = null;
            }
            base.Dispose(disposing);
        }

        internal static Font BodyFont()
        {
            return UiTheme.GameFont(8.25f);
        }

		internal static GlassRichTextBox Create(Color background)
		{
            return new GlassRichTextBox
            {
                Font = BodyFont(),
                BackColor = UiTheme.CurrentTheme == UiTheme.ThemeLiquidGlass ? UiTheme.BackgroundVeilColor : background
            };
		}

		internal static Control WithBackdrop(RichTextBox text)
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass || !(text is GlassRichTextBox)) return text;
			var glass = new InsetPanel { Dock = DockStyle.Fill, Margin = text.Margin, Padding = new Padding(7, 4, 5, 4) };
			glass.Controls.Add(text);
			glass.Paint += ((GlassRichTextBox)text).UpdateGlassColor;
			return glass;
		}

		private void UpdateGlassColor(object sender, PaintEventArgs e)
		{
			long now = Stopwatch.GetTimestamp();
			if (!Visible || IsDisposed || UiTheme.UsesEngineCanvas(this) || now < nextBackgroundSample) return;
			nextBackgroundSample = now + Stopwatch.Frequency / 4;
			// Keep the RichEdit window native so its selection, scroll and handle creation work.
			Color next = UiTheme.SampleGlassTextBackground(this);
			if (BackColor != next) BackColor = next;
		}
	}
}
