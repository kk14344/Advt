"""Build the standalone Windows acceptance runner."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
COMPILER = Path(r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe")

if __name__ == "__main__":
    output = ROOT / "dist" / "助手验收.exe"
    output.parent.mkdir(exist_ok=True)
    subprocess.run([
        str(COMPILER), "/nologo", "/codepage:65001", "/target:exe", "/platform:x64", "/optimize+",
        "/out:" + str(output), "/reference:System.Drawing.dll", "/reference:System.Windows.Forms.dll",
        "/reference:System.Web.Extensions.dll", str(ROOT / "tools" / "AcceptanceRunner.cs")
    ], check=True)
    print(output)

