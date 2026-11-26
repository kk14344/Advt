$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter '*.exe' | Select-Object -First 1
$data = Join-Path $root ('dist\checkin-fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $data | Out-Null
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$assembly = [Reflection.Assembly]::LoadFile($exe.FullName)
$type = $assembly.GetType('AandmAuto.ScheduleBook')
$settings = $assembly.GetType('AandmAuto.ScheduleSettings')
$book = [Activator]::CreateInstance($type)
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$name = $settings::WindowsTaskName($sid,$data,'main') + '-Checkin'
$now = [datetime]::UtcNow
$boundary = [datetime]::SpecifyKind([datetime]'2026-11-19T04:00:00',[DateTimeKind]::Utc)
if ($book.CheckinDue($boundary)){ throw 'Check-in should be off by default' }
$book.AutoCheckin = $true
$book.LastCheckinSlot = $type::CheckinSlotUtc($boundary.AddSeconds(-1)).Ticks
if ($book.CheckinDue($boundary.AddSeconds(-1)) -or -not $book.CheckinDue($boundary)){ throw 'Noon boundary failed' }
$book.LastCheckinSlot = $type::CheckinSlotUtc($boundary).Ticks
if ($book.CheckinDue($boundary.AddSeconds(1)) -or -not $book.CheckinDue($boundary.AddHours(6))){ throw 'Evening boundary or duplicate gate failed' }
if ($type::CheckinSlotUtc($boundary.AddHours(12)) -ne $boundary.AddHours(12)){ throw 'Midnight boundary failed' }

$book.LastCheckinSlot = $type::CheckinSlotUtc($now).AddHours(-1).Ticks
[IO.File]::WriteAllText((Join-Path $data 'task-schedules-v2.json'),$serializer.Serialize($book))
$xmlPath = Join-Path $data 'windows-checkin-task.xml'
$xmlText = $book.CreateCheckinTaskXml($exe.FullName,$sid,$now,'main',$data)
[IO.File]::WriteAllText($xmlPath,$xmlText,[Text.Encoding]::Unicode)
[xml]$spec=$xmlText
$hours=@($spec.Task.Triggers.CalendarTrigger | ForEach-Object { ([DateTimeOffset]::Parse($_.StartBoundary)).UtcDateTime.AddHours(8).Hour })
if (($hours -join ',') -ne '0,12,18' -or $spec.Task.Actions.Exec.Arguments -notlike '--checkin-run*'){ throw 'Unexpected trigger XML' }
$process=$null
$duplicate=$null
$registered=$false
try {
    $registration = 'skipped-by-smoke-guard (xml verified)'
    if ($env:AANDM_ALLOW_SCHTASKS -eq '1') {
        & schtasks.exe /Create /TN $name /XML $xmlPath /F | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Failed to register check-in task'}
        $registered=$true
        $registration = 'verified'
    }

    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$exe.FullName
    $extraArgs = if ($env:AANDM_ALLOW_SCHTASKS -eq '1') { '' } else { '--no-schtasks ' }
    $start.Arguments=$extraArgs + '--checkin-run --data-root "'+$data+'"'
    $start.UseShellExecute=$false
    $start.EnvironmentVariables['AANDM_SMOKE']='1'
    $start.EnvironmentVariables['AANDM_CHECKIN_SMOKE']='1'
    $process=[Diagnostics.Process]::Start($start)
    if(-not $process.WaitForExit(25000)){throw 'Check-in did not exit'}
    $report=Get-Content -LiteralPath (Join-Path $data 'checkin-smoke.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if($report.result -ne 'passed'){throw ($report | ConvertTo-Json -Depth 5)}
    if(-not (Test-Path -LiteralPath (Join-Path $data 'task-review.json'))){throw 'Daily report not saved before exit'}

    $duplicate=[Diagnostics.Process]::Start($start)
    if(-not $duplicate.WaitForExit(3000)){try{$duplicate.Kill()}catch{};throw 'Consumed slot started a duplicate process'}
    if ($env:AANDM_ALLOW_SCHTASKS -ne '1') {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & schtasks.exe /Query /TN $name 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0){ throw 'Expected no real Windows scheduled task to be registered during smoke run' }
        } finally {
            $ErrorActionPreference = $previous
        }
    }
    $report | Add-Member -NotePropertyName windowsTriggers -NotePropertyValue $hours
    $report | Add-Member -NotePropertyName duplicateLaunchRejected -NotePropertyValue $true
    $report | Add-Member -NotePropertyName scheduledTaskRegistration -NotePropertyValue $registration
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'dist\checkin-test-results.json') -Encoding utf8
    $report | ConvertTo-Json -Depth 5
} finally {
    if($process -and -not $process.HasExited){$process.Kill()}
    if($process){$process.Dispose()}
    if($duplicate -and -not $duplicate.HasExited){$duplicate.Kill()}
    if($duplicate){$duplicate.Dispose()}
    if($registered){try { $ErrorActionPreference='Continue'; & schtasks.exe /Delete /TN $name /F 2>$null | Out-Null } catch {}}
    for ($i = 0; $i -lt 15 -and (Test-Path -LiteralPath $data); $i++) {
        Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $data) { Start-Sleep -Milliseconds 200 }
    }
}
