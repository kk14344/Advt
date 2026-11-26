$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter '*.exe' | Select-Object -First 1
$data = Join-Path $root ('dist\followup-fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $data | Out-Null
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$assembly = [Reflection.Assembly]::LoadFile($exe.FullName)
$type = $assembly.GetType('AandmAuto.ScheduleBook')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$tasks = $assembly.GetType('AandmAuto.ScheduleSettings')
$name = $tasks::WindowsTaskName($sid,$data,'main') + '-Harvest'
# 1.41 存档：没有 RefineFollowup，矿车已关闭，炼化有排程；矿车排程应在新版加载时被丢弃。
$fixture = '{"Version":2,"LaunchWhenClosed":false,"Plans":[],"Pending":[],"MineFollowup":true,"EscortFollowup":false,' +
  '"Harvest":[{"Task":"refine","Account":"TEST","DueUtc":"2030-01-01T00:00:00Z"},' +
  '{"Task":"escort","Account":"TEST","DueUtc":"2030-01-01T00:00:00Z"}],"AutoCheckin":false,"LastCheckinSlot":0}'
[IO.File]::WriteAllText((Join-Path $data 'task-schedules-v2.json'),$fixture,(New-Object Text.UTF8Encoding($false)))
$process = $null
try {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe.FullName
    $extraArgs = if ($env:AANDM_ALLOW_SCHTASKS -eq '1') { '' } else { '--no-schtasks ' }
    $start.Arguments = $extraArgs + '--data-root "' + $data + '"'
    $start.UseShellExecute = $false
    $env:AANDM_SMOKE = '1'
    $env:AANDM_FOLLOWUP_SMOKE = '1'
    $process = [Diagnostics.Process]::Start($start)
    Remove-Item Env:AANDM_SMOKE -ErrorAction SilentlyContinue
    Remove-Item Env:AANDM_FOLLOWUP_SMOKE -ErrorAction SilentlyContinue
    if (-not $process.WaitForExit(40000)){ throw 'Timed out waiting for the follow-up fixture' }
    $outputPath = Join-Path $data 'followup-smoke.json'
    if (-not (Test-Path -LiteralPath $outputPath)){ throw 'Missing follow-up smoke report' }
    $report = Get-Content -LiteralPath $outputPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($report.result -ne 'passed'){ throw ($report | ConvertTo-Json -Depth 4) }
    $stored = $serializer.Deserialize([IO.File]::ReadAllText((Join-Path $data 'task-schedules-v2.json')),$type)
    if (-not $stored.RefineFollowup){ throw 'Refine switch was not persisted' }
    if ($stored.EscortFollowup){ throw 'Saved escort switch was not preserved' }
    if ($stored.Harvest.Count -ne 1 -or $stored.Harvest[0].Task -ne 'refine'){ throw 'Unexpected harvest rows after migration' }
    $xmlPath = Join-Path $data 'windows-harvest-task.xml'
    if (-not (Test-Path -LiteralPath $xmlPath)){ throw 'Harvest task XML was not written' }
    $xml = [xml][IO.File]::ReadAllText($xmlPath)
    if (@($xml.Task.Triggers.TimeTrigger).Count -ne 1){ throw 'Expected exactly one follow-up trigger' }
    if ($xml.Task.Actions.Exec.Arguments -notlike '*--harvest-run*'){ throw 'Wrong harvest launch argument' }
    $registration = 'skipped-by-smoke-guard (xml verified)'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($env:AANDM_ALLOW_SCHTASKS -eq '1') {
            $query = (schtasks.exe /Query /TN $name /XML 2>$null) -join "`n"
            if ($LASTEXITCODE -eq 0 -and $query -like '*<TimeTrigger>*') { $registration = 'verified' }
            else { $registration = 'query-failed' }
        } else {
            schtasks.exe /Query /TN $name 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { throw 'Expected no real Windows scheduled task to be registered during smoke run' }
        }
    } finally {
        $ErrorActionPreference = $previous
    }
    $report | Add-Member -NotePropertyName windowsTriggers -NotePropertyValue 1
    $report | Add-Member -NotePropertyName scheduledTaskRegistration -NotePropertyValue $registration
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'dist\followup-test-results.json') -Encoding utf8
    $report | ConvertTo-Json -Depth 4
} finally {
    if ($process -and -not $process.HasExited){$process.Kill()}
    if ($process){$process.Dispose()}
    if ($env:AANDM_ALLOW_SCHTASKS -eq '1') {
        try { $ErrorActionPreference='Continue'; schtasks.exe /Delete /TN $name /F 2>$null | Out-Null } catch {}
    }
    for ($i = 0; $i -lt 15 -and (Test-Path -LiteralPath $data); $i++) {
        Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $data) { Start-Sleep -Milliseconds 200 }
    }
}
