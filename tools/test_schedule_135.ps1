$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$bin=Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter '*.exe' | Select-Object -First 1
Add-Type -AssemblyName System.Web.Extensions
$a=[Reflection.Assembly]::LoadFile($bin.FullName)
function New-Type($name){[Activator]::CreateInstance($a.GetType('AandmAuto.'+$name,$true))}
$script:checks=0
function Check($c,$s){if(-not $c){throw $s};$script:checks++}
$b=New-Type 'ScheduleBook';$p=New-Type 'SchedulePlan';$p.Enabled=$true;$p.Cursor='2026-11-18T07:59:59';$p.Times='08:00,12:30,08:00';$b.Plans.Add($p)
Check ($b.CollectDue([datetime]'2026-11-18T08:00:00')) 'daily due'
Check ($b.Pending.Count -eq 1) 'time dedup'
Check (-not $b.CollectDue([datetime]'2026-11-18T08:00:01')) 'same occurrence never queued twice'
$b.Pending.Clear();Check (-not $b.CollectDue([datetime]'2026-11-18T08:00:02')) 'dequeued occurrence never replayed'
Check ($b.CollectDue([datetime]'2026-11-18T12:30:00') -and $b.Pending.Count -eq 1) 'multiple daily times'
Check (-not $b.Pending[0].Expired([datetime]'2026-11-18T13:00:00')) 'wait boundary allowed'
Check ($b.Pending[0].Expired([datetime]'2026-11-18T13:00:01')) 'wait expires'
$p.Kind='weekly';$p.Weekdays=8;$p.Cursor='2026-11-18T07:59:59';$b.Pending.Clear();Check ($b.CollectDue([datetime]'2026-11-18T08:00:00')) 'Wednesday mask'
$p.Cursor='2026-11-19T07:59:59';$b.Pending.Clear();Check (-not $b.CollectDue([datetime]'2026-11-19T08:00:00')) 'other weekday excluded'
$p.Kind='dates';$p.Dates='2026-11-20,2026-11-22';$p.Cursor='2026-11-20T07:59:59';Check ($b.CollectDue([datetime]'2026-11-20T08:00:00')) 'specified date'
$p.Cursor='2026-11-21T07:59:59';$b.Pending.Clear();Check (-not $b.CollectDue([datetime]'2026-11-21T08:00:00')) 'unspecified date excluded'
$p.Kind='interval';$p.Weekdays=127;$p.Anchor='2026-11-18T08:00:00';$p.IntervalMinutes=15;$p.WindowStart='08:00';$p.WindowEnd='10:00';$p.Cursor='2026-11-18T07:59:59';$p.MaxWaitMinutes=120
Check ($b.CollectDue([datetime]'2026-11-18T08:31:00') -and $b.Pending.Count -eq 3) 'interval occurrences'
$b.Pending.Clear();$p.Cursor='2026-11-18T10:00:00';Check (-not $b.CollectDue([datetime]'2026-11-18T10:15:00')) 'interval window'
$p.WindowStart='23:00';$p.WindowEnd='01:00';$p.Cursor='2026-11-18T23:59:59';Check ($b.CollectDue([datetime]'2026-11-19T00:15:00') -and $b.Pending.Count -eq 2) 'overnight window'
$p.StartDate='2026-11-20';$p.Cursor='2026-11-18T23:59:59';$b.Pending.Clear();Check (-not $b.CollectDue([datetime]'2026-11-19T00:15:00')) 'start date bound'
$p.StartDate='';$p.EndDate='2026-11-18';$p.Cursor='2026-11-18T23:59:59';Check (-not $b.CollectDue([datetime]'2026-11-19T00:15:00')) 'end date bound'
$p.EndDate='';$p.Kind='daily';$p.MaxWaitMinutes=30;$p.Cursor='2026-11-01T00:00:00';$p.Times='08:00,09:00';$b.Pending.Clear();Check ($b.CollectDue([datetime]'2026-11-18T09:10:00') -and $b.Pending.Count -eq 1) 'expired missed occurrences are not replayed'
$b.Pending[0].Steps[0].Task='arena';Check ($p.Steps[0].Task -eq 'daily') 'queue snapshots isolate editor changes'
$step=New-Type 'ScheduleStep';$step.Task='autoMine';$step.Minutes=0;$p.Steps.Clear();$p.Steps.Add($step);$rejected=$false;try{$p.Validate()}catch{$rejected=$true};Check $rejected 'continuous tasks require duration'
$step.Minutes=10;$p.Validate();Check $true 'continuous duration accepted'
$p.Times='25:00';$rejected=$false;try{$p.Validate()}catch{$rejected=$true};Check $rejected 'invalid times rejected';$p.Times='08:00,12:30'
$old=New-Type 'ScheduleSettings';$old.Enabled=$true;$old.Hour=7;$old.Minute=15;$old.MiningMinutes=45;$old.Tasks.Add('autoMine')
$t=$a.GetType('AandmAuto.ScheduleBook');$m=$t.GetMethod('Migrate').Invoke($null,@($old,[datetime]'2026-11-18T06:00:00'))
Check ($m.Plans[0].Times -eq '07:15' -and $m.Plans[0].Steps[1].Minutes -eq 45) 'legacy migration preserves enabled time task order and mining duration'
Check ($m.Plans[0].Enabled) 'legacy enabled state'
$j=New-Object System.Web.Script.Serialization.JavaScriptSerializer
$round=$j.Deserialize($j.Serialize($b),$t);Check ($round.Pending.Count -eq $b.Pending.Count -and $round.Plans[0].Id -eq $p.Id) 'durable queue roundtrip'
$xmlBook=New-Type 'ScheduleBook';foreach($k in @('daily','weekly','dates','interval')){$v=New-Type 'SchedulePlan';$v.Kind=$k;$v.Enabled=$true;$v.Times='08:00,18:00';$v.Dates='2026-11-20,2026-11-22';$v.Anchor='2026-11-18T09:00:00';$v.IntervalMinutes=15;$xmlBook.Plans.Add($v)}
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$xml=$xmlBook.CreateTaskXml('C:\A & B\helper.exe',$sid,[datetime]'2026-11-18T07:00:00','main','C:\Data & Test')
$service=New-Object -ComObject 'Schedule.Service';$service.Connect();$definition=$service.NewTask(0);$definition.XmlText=$xml
Check ($definition.Triggers.Count -eq 9) 'Windows parser accepts daily weekly date interval triggers'
Check ($definition.Actions.Item(1).Path -eq 'C:\A & B\helper.exe') 'XML escaping'
$xml | Set-Content -LiteralPath (Join-Path $root 'dist\schedule-135-test.xml') -Encoding Unicode
@{result='passed';checks=$checks;windowsTaskXmlValidated=$true;registeredAnyWindowsTask=$false} | ConvertTo-Json | Set-Content (Join-Path $root 'dist\schedule-135-test-results.json') -Encoding utf8
Write-Output "Passed $checks expanded schedule checks. No Windows task registered."

