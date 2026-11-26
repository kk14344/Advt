using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AandmAuto
{
	internal sealed class GlassRichTextBox : RichTextBox
	{
		private long nextBackgroundSample;

		internal static RichTextBox Create(Color background)
		{
			if (UiTheme.CurrentTheme != UiTheme.ThemeLiquidGlass) return new RichTextBox { BackColor = background };
			return new GlassRichTextBox { BackColor = UiTheme.BackgroundVeilColor };
		}

		internal static Control WithBackdrop(RichTextBox text)
		{
			if (!(text is GlassRichTextBox)) return text;
			var glass = new InsetPanel { Dock = DockStyle.Fill, Margin = text.Margin, Padding = new Padding(7, 4, 5, 4) };
			glass.Controls.Add(text);
			glass.Paint += ((GlassRichTextBox)text).UpdateGlassColor;
			return glass;
		}

		private void UpdateGlassColor(object sender, PaintEventArgs e)
		{
			long now = Stopwatch.GetTimestamp();
			if (!Visible || IsDisposed || now < nextBackgroundSample) return;
			nextBackgroundSample = now + Stopwatch.Frequency / 4;
			// Keep the RichEdit window native so its selection, scroll and handle creation work.
			Color next = UiTheme.SampleGlassTextBackground(this);
			if (BackColor != next) BackColor = next;
		}
	}
}
