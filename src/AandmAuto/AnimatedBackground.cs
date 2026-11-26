using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace AandmAuto
{
	internal sealed class AnimatedBackground : IDisposable
	{
		private readonly Stream stream;
		private readonly Image source;
		private readonly FrameDimension dimension;
		private readonly int count;
		private readonly byte[] frameDelays;
		private int index;

		internal Bitmap Frame { get; private set; }
		internal int Delay
		{
			get
			{
				if (frameDelays == null || index >= frameDelays.Length / 4) return 100;
				long delay = BitConverter.ToUInt32(frameDelays, index * 4) * 10L;
				return delay <= 10 ? 100 : (int)Math.Min(delay, int.MaxValue);
			}
		}

		internal static AnimatedBackground Open(string path)
		{
			var stream = new MemoryStream(File.ReadAllBytes(path), false);
			Image source = null;
			try
			{
				source = Image.FromStream(stream);
				var dimension = new FrameDimension(source.FrameDimensionsList[0]);
				int count = source.GetFrameCount(dimension);
				if (count < 2) return null;
				var animation = new AnimatedBackground(stream, source, dimension, count);
				stream = null;
				source = null;
				return animation;
			}
			finally
			{
				if (source != null) source.Dispose();
				if (stream != null) stream.Dispose();
			}
		}

		private AnimatedBackground(Stream stream, Image source, FrameDimension dimension, int count)
		{
			this.stream = stream;
			this.source = source;
			this.dimension = dimension;
			this.count = count;
			if (Array.IndexOf(source.PropertyIdList, 0x5100) >= 0)
				frameDelays = source.GetPropertyItem(0x5100).Value;
			double scale = Math.Min(1.0, 960.0 / Math.Max(source.Width, source.Height));
			Frame = new Bitmap(Math.Max(1, (int)Math.Round(source.Width * scale)),
				Math.Max(1, (int)Math.Round(source.Height * scale)), PixelFormat.Format32bppArgb);
			try { Render(); }
			catch { Frame.Dispose(); throw; }
		}

		internal void Advance()
		{
			index = (index + 1) % count;
			Render();
		}

		private void Render()
		{
			source.SelectActiveFrame(dimension, index);
			using (var graphics = Graphics.FromImage(Frame))
			{
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.DrawImage(source, 0, 0, Frame.Width, Frame.Height);
			}
		}

		public void Dispose()
		{
			Frame.Dispose();
			source.Dispose();
			stream.Dispose();
		}
	}
}
