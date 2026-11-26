$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter '*.exe' | Select-Object -First 1
$data = Join-Path $root ('dist\harvest-fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $data | Out-Null
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$assembly = [Reflection.Assembly]::LoadFile($exe.FullName)
$type = $assembly.GetType('AandmAuto.ScheduleBook')
$book = [Activator]::CreateInstance($type)
$tasks = $assembly.GetType('AandmAuto.ScheduleSettings')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$name = $tasks::WindowsTaskName($sid,$data,'main') + '-Harvest'
$book.SetHarvest('mine','TEST',[datetime]::UtcNow.AddMinutes(-2)) | Out-Null
$book.SetHarvest('escort','TEST',[datetime]::UtcNow.AddMinutes(-1)) | Out-Null
$schedule = Join-Path $data 'task-schedules-v2.json'
[IO.File]::WriteAllText($schedule,$serializer.Serialize($book))
$process = $null
try {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe.FullName
    $extraArgs = if ($env:AANDM_ALLOW_SCHTASKS -eq '1') { '' } else { '--no-schtasks ' }
    $start.Arguments = $extraArgs + '--scheduled-run --data-root "' + $data + '"'
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['AANDM_SMOKE'] = '1'
    $start.EnvironmentVariables['AANDM_SCHEDULE_SMOKE'] = '1'
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(35000)){ throw 'Timed out waiting for the minimized window fixture' }
    $outputPath = Join-Path $data 'schedule-smoke.json'
    $report = Get-Content -LiteralPath $outputPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($report.result -ne 'passed'){ throw ($report | ConvertTo-Json -Depth 4) }
    $xmlPath = Join-Path $data 'windows-harvest-task.xml'
    if (-not (Test-Path -LiteralPath $xmlPath)){ throw 'Expected windows-harvest-task.xml on disk' }
    $xml = [xml][IO.File]::ReadAllText($xmlPath)
    if (@($xml.Task.Triggers.TimeTrigger).Count -ne 2){ throw 'Expected two completion triggers in XML' }
    if ($xml.Task.Actions.Exec.Arguments -notlike '*--scheduled-run*' -or $xml.Task.Actions.Exec.Command -ne $exe.FullName){ throw 'Wrong scheduled command' }
    $stored = $serializer.Deserialize([IO.File]::ReadAllText($schedule),$type)
    for ($i=0; $i -lt 2; $i++) {
        $boundary = [datetime]::Parse($xml.Task.Triggers.TimeTrigger[$i].StartBoundary)
        $difference = ($boundary.ToUniversalTime() - $stored.Harvest[$i].DueUtc).TotalSeconds
        if ($difference -lt 0 -or $difference -gt 2){ throw 'Windows trigger must not fire before the persisted deadline' }
    }
    $registration = 'skipped-by-smoke-guard (xml verified)'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($env:AANDM_ALLOW_SCHTASKS -eq '1') {
            $registeredXml = [xml]((schtasks.exe /Query /TN $name /XML 2>$null) -join "`n")
            if ($LASTEXITCODE -ne 0 -or @($registeredXml.Task.Triggers.TimeTrigger).Count -ne 2){ throw 'Expected two registered completion triggers' }
            $registration = 'verified'
        } else {
            schtasks.exe /Query /TN $name 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0){ throw 'Expected no real Windows scheduled task to be registered during smoke run' }
        }
    } finally {
        $ErrorActionPreference = $previous
    }
    $report | Add-Member -NotePropertyName windowsTriggers -NotePropertyValue 2
    $report | Add-Member -NotePropertyName scheduledTaskRegistration -NotePropertyValue $registration
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'dist\harvest-test-results.json') -Encoding utf8
    $report | ConvertTo-Json -Depth 4
} finally {
    if ($process -and -not $process.HasExited){$process.Kill()}
    if ($process){$process.Dispose()}
    if ($env:AANDM_ALLOW_SCHTASKS -eq '1' -and (Test-Path -LiteralPath (Join-Path $data 'windows-harvest-task.xml'))){
        try { $ErrorActionPreference='Continue'; schtasks.exe /Delete /TN $name /F 2>$null | Out-Null } catch {}
    }
    for ($i = 0; $i -lt 15 -and (Test-Path -LiteralPath $data); $i++) {
        Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $data) { Start-Sleep -Milliseconds 200 }
    }
}
