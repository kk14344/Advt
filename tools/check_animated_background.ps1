param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = New-Item -ItemType Directory -Path $Output
$env:AANDM_GIF_TEST_PATH = Join-Path $fixture.FullName '120-frames.gif'
@'
import os
from PIL import Image, ImageDraw

frames = []
for index in range(120):
    frame = Image.new("RGBA", (960, 540), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rectangle((4, 4, 90, 90), fill=(index + 1, 95, 235, 255))
    frames.append(frame)
frames[0].save(os.environ["AANDM_GIF_TEST_PATH"], save_all=True,
               append_images=frames[1:], duration=[40, 90] * 60, loop=0,
               disposal=2, transparency=0, optimize=False)
'@ | python -B -
if ($LASTEXITCODE -ne 0) { throw 'Could not generate GIF fixture.' }
$probeSource = Join-Path $fixture.FullName 'GifProbe.cs'
@'
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using AandmAuto;

static class GifProbe
{
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    static void Main(string[] args)
    {
        int pixelBytes;
        using (var animation = AnimatedBackground.Open(args[0]))
        {
            Bitmap frame = animation.Frame;
            for (int index = 0; index <= 120; index++)
            {
                int expectedColor = index % 120 + 1;
                int expectedDelay = index % 2 == 0 ? 40 : 90;
                Color actual = animation.Frame.GetPixel(20, 20);
                if (!ReferenceEquals(frame, animation.Frame) || actual.R != expectedColor ||
                    animation.Delay != expectedDelay || animation.Frame.GetPixel(100, 100).A != 0)
                    throw new Exception("Unexpected frame " + index + ": " + actual +
                        ", delay=" + animation.Delay);
                animation.Advance();
            }
            pixelBytes = frame.Width * frame.Height * 4;
        }
        using (var process = Process.GetCurrentProcess())
        {
            uint before = GetGuiResources(process.Handle, 0);
            uint peak = before;
            for (int i = 0; i < 20; i++)
            {
                using (var animation = AnimatedBackground.Open(args[0]))
                    for (int j = 0; j < 6; j++) animation.Advance();
                peak = Math.Max(peak, GetGuiResources(process.Handle, 0));
            }
            uint after = GetGuiResources(process.Handle, 0);
            if (after > before + 2 || peak > before + 2)
                throw new Exception("GDI objects grew across GIF reopen cycles.");
            Console.WriteLine("{\"result\":\"passed\",\"frames\":120,\"loop\":true,\"sharedBitmap\":true,\"pixelBytes\":" +
                pixelBytes + ",\"reopens\":20,\"gdiBefore\":" + before + ",\"gdiPeak\":" + peak + ",\"gdiAfter\":" + after + "}");
        }
    }
}
'@ | Set-Content -LiteralPath $probeSource -Encoding utf8
$probe = Join-Path $fixture.FullName 'GifProbe.exe'
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /reference:System.Drawing.dll "/out:$probe" $probeSource (Join-Path $root 'src/AandmAuto/AnimatedBackground.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not compile GIF probe.' }
& $probe $env:AANDM_GIF_TEST_PATH | Tee-Object -FilePath (Join-Path $fixture.FullName 'gif.json')
if ($LASTEXITCODE -ne 0) { throw 'GIF probe failed.' }
