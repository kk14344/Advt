$ErrorActionPreference = 'SilentlyContinue'
$proc = Get-Process | Where-Object { $_.ProcessName -like '*冒险与挖矿自动助手*' } | Select-Object -First 1

if ($proc) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32Focus {
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
    $hwnd = $proc.MainWindowHandle
    if ($hwnd -and $hwnd -ne [IntPtr]::Zero) {
        if ([Win32Focus]::IsIconic($hwnd)) {
            [Win32Focus]::ShowWindowAsync($hwnd, 9) | Out-Null
        } else {
            [Win32Focus]::ShowWindowAsync($hwnd, 5) | Out-Null
        }
        [Win32Focus]::SetForegroundWindow($hwnd) | Out-Null
    }
    exit 0
}

$dir = 'F:\helper-adventure\冒险与挖矿助手-测试服1.41'
$exe = Join-Path $dir '冒险与挖矿自动助手-测试服1.41.exe'
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=19341 --mute-audio'
Start-Process -FilePath $exe -WorkingDirectory $dir