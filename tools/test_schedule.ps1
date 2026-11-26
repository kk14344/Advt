$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$binary=Get-ChildItem -LiteralPath (Join-Path $taskRoot 'dist') -Filter '*.exe' | Select-Object -First 1
$assembly=[Reflection.Assembly]::LoadFile($binary.FullName)
$scheduleType=$assembly.GetType('AandmAuto.ScheduleSettings',$true)
$settings=[Activator]::CreateInstance($scheduleType)
$checks=0
function Assert-Check($condition,$message) { if(-not $condition){throw $message};$script:checks++ }
Assert-Check (-not $settings.Enabled -and -not $settings.LaunchWhenClosed) 'Defaults must be disabled'
$settings.Hour=40;$settings.Minute=-1;$settings.MiningMinutes=0;$settings.Normalize()
Assert-Check ($settings.Hour -eq 23 -and $settings.Minute -eq 0 -and $settings.MiningMinutes -eq 1) 'Normalize invalid numbers'
$settings.Tasks.Clear();foreach($key in @('autoMine','mine','daily','daily','invalid')){$settings.Tasks.Add($key)};$settings.Normalize()
Assert-Check (($settings.Tasks -join ',') -eq 'daily,mine,autoMine') 'Normalize task order and remove duplicates'
$settings.Enabled=$true;$settings.Hour=8;$settings.Minute=30
$day=[datetime]'2026-11-12T10:00:00'
Assert-Check ($settings.IsDue($day)) 'Due after configured time'
Assert-Check (-not $settings.IsDue([datetime]'2026-11-12T08:29:59')) 'Do not run early'
$settings.LastRunDay='2026-11-12'
Assert-Check (-not $settings.IsDue($day)) 'Deduplicate the same day'
$settings.LastRunDay='';$settings.Tasks.Clear()
Assert-Check (-not $settings.IsDue($day)) 'Do not run an empty queue'
$settings.Tasks.Add('daily')
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$xmlText=$settings.CreateTaskXml('C:\Assistant & Mining\helper.exe',$sid,$day)
$xml=[xml]$xmlText
Assert-Check ($xml.Task.Actions.Exec.Command -eq 'C:\Assistant & Mining\helper.exe' -and $xml.Task.Actions.Exec.Arguments -eq '--scheduled-run') 'Escape executable and preserve launch arguments'
Assert-Check ($xml.Task.Triggers.CalendarTrigger.StartBoundary -eq '2026-11-13T08:30:00') 'Schedule the next day when time has passed'
$service=New-Object -ComObject 'Schedule.Service';$service.Connect()
$definition=$service.NewTask(0);$definition.XmlText=$xmlText
Assert-Check ($definition.Triggers.Count -eq 1 -and $definition.Actions.Count -eq 1) 'Windows Task Scheduler must parse task XML'
@{result='passed';checks=$checks;windowsTaskXmlValidated=$true;registeredAnyWindowsTask=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'dist\schedule-test-results.json') -Encoding utf8
Write-Output "Passed $checks schedule checks; no Windows task registered."
