$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter '*.exe' | Select-Object -First 1
$assembly = [Reflection.Assembly]::LoadFile($exe.FullName)
$settings = $assembly.GetType('AandmAuto.ScheduleSettings')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value

function New-RefineFixture {
    $data = Join-Path $root ('dist\refine-fixture-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $data | Out-Null
    # 使用与 JavaScriptSerializer 相同的 /Date(ms)/ 时间格式，两分钟前到期。
    $ms = [DateTimeOffset]::UtcNow.AddMinutes(-2).ToUnixTimeMilliseconds()
    $fixture = '{"Version":2,"LaunchWhenClosed":false,"Plans":[],"Pending":[],"MineFollowup":true,' +
        '"EscortFollowup":true,"RefineFollowup":true,"Harvest":[{"Task":"refine","Account":"TEST",' +
        '"DueUtc":"\/Date(' + $ms + ')\/","RefineHeat":1000,"RefineMeasuredUtc":"\/Date(' + $ms + ')\/"}],' +
        '"AutoCheckin":false,"LastCheckinSlot":0}'
    [IO.File]::WriteAllText((Join-Path $data 'task-schedules-v2.json'),$fixture,(New-Object Text.UTF8Encoding($false)))
    return $data
}

function Invoke-RefineSmoke([string]$mode) {
    $data = New-RefineFixture
    $taskName = $settings::WindowsTaskName($sid,$data,'main') + '-Harvest'
    $process = $null
    try {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $exe.FullName
        $extraArgs = if ($env:AANDM_ALLOW_SCHTASKS -eq '1') { '' } else { '--no-schtasks ' }
        $start.Arguments = $extraArgs + '--data-root "' + $data + '"'
        $start.UseShellExecute = $false
        $env:AANDM_SMOKE = '1'
        $env:AANDM_REFINE_SMOKE = '1'
        Remove-Item Env:AANDM_REFINE_FOREGROUND -ErrorAction SilentlyContinue
        Remove-Item Env:AANDM_REFINE_MINIMIZE -ErrorAction SilentlyContinue
        if ($mode -eq 'foreground') { $env:AANDM_REFINE_FOREGROUND = '1' }
        if ($mode -eq 'minimize') { $env:AANDM_REFINE_MINIMIZE = '1' }
        $process = [Diagnostics.Process]::Start($start)
        foreach ($name in @('AANDM_SMOKE','AANDM_REFINE_SMOKE','AANDM_REFINE_FOREGROUND','AANDM_REFINE_MINIMIZE')) {
            Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
        }
        if (-not $process.WaitForExit(90000)) { throw ('Refine smoke timed out, mode=' + $mode) }
        $report = Get-Content -LiteralPath (Join-Path $data 'refine-smoke.json') -Raw -Encoding utf8 | ConvertFrom-Json
        if ($report.result -ne 'passed') { throw ($mode + ': ' + ($report | ConvertTo-Json -Depth 5)) }
        $xmlPath = Join-Path $data 'windows-harvest-task.xml'
        if (-not (Test-Path -LiteralPath $xmlPath)) { throw ('Refine harvest task XML missing, mode=' + $mode) }
        $xml = [xml][IO.File]::ReadAllText($xmlPath)
        if (@($xml.Task.Triggers.TimeTrigger).Count -lt 1) { throw ('Refine trigger missing in XML, mode=' + $mode) }
        if ($xml.Task.Actions.Exec.Arguments -notlike '*--harvest-run*') { throw ('Wrong harvest argument in XML, mode=' + $mode) }
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            if ($env:AANDM_ALLOW_SCHTASKS -eq '1') {
                & schtasks.exe /Query /TN $taskName 2>$null | Out-Null
                if ($LASTEXITCODE -ne 0) { throw ('Refine wake-up missing, mode=' + $mode) }
            } else {
                & schtasks.exe /Query /TN $taskName 2>$null | Out-Null
                if ($LASTEXITCODE -eq 0) { throw ('Expected no real Windows scheduled task to be registered during smoke run, mode=' + $mode) }
            }
        } finally {
            $ErrorActionPreference = $previous
        }
        return $report
    } finally {
        if ($process -and -not $process.HasExited) { $process.Kill() }
        if ($process) { $process.Dispose() }
        if ($env:AANDM_ALLOW_SCHTASKS -eq '1' -and (Test-Path -LiteralPath (Join-Path $data 'windows-harvest-task.xml'))) {
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            & schtasks.exe /Delete /TN $taskName /F 2>$null | Out-Null
            $ErrorActionPreference = $previous
        }
        for ($i = 0; $i -lt 15 -and (Test-Path -LiteralPath $data); $i++) {
            Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path -LiteralPath $data) { Start-Sleep -Milliseconds 200 }
        }
    }
}

$foreground = Invoke-RefineSmoke 'foreground'
$minimized = Invoke-RefineSmoke 'minimize'
$regStatus = if ($env:AANDM_ALLOW_SCHTASKS -eq '1') { 'verified' } else { 'skipped-by-smoke-guard (xml verified)' }
$result = [ordered]@{
    result = 'passed'
    taps = $foreground.taps
    pointerTaps = $foreground.pointerTaps
    touchTaps = $foreground.touchTaps
    mouseTaps = $foreground.mouseTaps
    captureErrors = $foreground.captureErrors
    foreground = $foreground.checks
    minimized = $minimized.checks
    minimizedTaps = $minimized.taps
    coldStartMinimized = 'not covered by this pointer test; separate scheduled startup tests cover the launch path'
    registrationVerified = $true
    scheduledTaskRegistration = $regStatus
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'dist\refine-test-results.json') -Encoding utf8
$result | ConvertTo-Json -Depth 6
