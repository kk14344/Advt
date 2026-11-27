param(
    [ValidateSet('official','testpvp','all')]
    [string]$Edition = 'all'
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot

function Test-Contains([byte[]]$hay,[byte[]]$needle){
    if($needle.Length -eq 0){return $false}
    $limit=$hay.Length-$needle.Length
    for($i=0;$i -le $limit;$i++){
        if($hay[$i] -ne $needle[0]){continue}
        $ok=$true
        for($j=1;$j -lt $needle.Length;$j++){if($hay[$i+$j] -ne $needle[$j]){$ok=$false;break}}
        if($ok){return $true}
    }
    return $false
}

$buildEdText = Get-Content -LiteralPath (Join-Path $taskRoot 'src\AandmAuto\BuildEdition.cs') -Raw -Encoding utf8
$appVersion = if($buildEdText -match 'AppVersion\s*=\s*"([^"]+)"'){$Matches[1]}else{'1.1.1'}
$assemblyVersion = $appVersion + '.0'

function Verify-One([string]$ed){
    $exeName = if($ed -eq 'testpvp'){'冒险与挖矿自动助手-测试服PvP版.exe'}else{'冒险与挖矿自动助手-优化版.exe'}
    $binaryPath = Join-Path (Join-Path $taskRoot 'dist') $exeName
    if(-not (Test-Path -LiteralPath $binaryPath)){throw ('Missing binary: '+$binaryPath)}
    $bytes=[IO.File]::ReadAllBytes($binaryPath)
    $version=$assemblyVersion
    $flat=([Text.Encoding]::ASCII.GetString($bytes)) -replace ([string][char]0),''
    if(-not $flat.Contains($version)){throw ('Assembly version marker missing in '+$exeName)}
    if($flat.Contains('1.44.0.0')){throw ('Stale 1.44 version marker still present in '+$exeName)}

    $injectPath = if($ed -eq 'testpvp'){Join-Path $taskRoot 'dist\AandmAuto.testpvp.inject.js'}else{Join-Path $taskRoot 'src\AandmAuto.inject.js'}
    $checks=@{}
    foreach($entry in @(
        @('inject',$injectPath,32),
        @('background',(Join-Path $taskRoot 'src\background_guard.js'),32),
        @('font',(Join-Path $taskRoot 'src\fonts\general.ttf'),32),
        @('updateConfig',(Join-Path $taskRoot 'src\update-config.json'),16)
    )){
        $source=[IO.File]::ReadAllBytes($entry[1])
        $offset=[Math]::Min([Math]::Max(0,$source.Length-$entry[2]),2048)
        $needle=New-Object byte[] $entry[2]
        [Array]::Copy($source,$offset,$needle,0,$entry[2])
        if(-not (Test-Contains $bytes $needle)){throw ('Embedded marker missing ('+$ed+'): '+$entry[0])}
        $checks[$entry[0]]=([BitConverter]::ToString((New-Object Security.Cryptography.SHA256Managed).ComputeHash($source))).Replace('-','').ToLowerInvariant()
    }
    $luaFile = if($ed -eq 'testpvp'){Join-Path $taskRoot 'dist\automation.testpvp.generated.lua'}else{Join-Path $taskRoot 'dist\automation.generated.lua'}
    $lua=Get-Content -LiteralPath $luaFile -Raw
    $verMarker = if($ed -eq 'testpvp'){$appVersion + '-testpvp'}else{$appVersion}
    foreach($marker in @('refine_sim_taps','daily_adventure_claim','clear_refine_overlays','guild_boss_done_today','battle_replay','mystic_stone','mystery_shop',$verMarker)){
        if($lua.IndexOf($marker) -lt 0){throw ('Generated Lua ('+$ed+') missing: '+$marker)}
    }
    return @{edition=$ed;exe=$exeName;assemblyVersion=$version;resources=$checks}
}

$targets = if($Edition -eq 'all'){@('official','testpvp')}else{@($Edition)}
$results=@()
foreach($t in $targets){
    $results += Verify-One $t
}
@{result='passed';assemblyVersion=$assemblyVersion;editions=$results} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'dist\embedded-test-results.json') -Encoding utf8
Write-Output ('Embedded script, update-config, game font and version match ('+$assemblyVersion+') for: '+($targets -join ', '))
