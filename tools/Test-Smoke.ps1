<#
.SYNOPSIS
    Smoke test: build + deploy mods, launch Valheim, wait until every mod logged its ready marker
    (plugin Awake + Harmony patching finished) and the main menu started, then scan the BepInEx log
    for errors and close the game. Exit code 0 = pass.
.DESCRIPTION
    Catches: plugin load failures, Harmony patch target not found / signature mismatch, exceptions in
    Awake, and exceptions thrown while the main menu starts. It does not load a world; in-world
    behaviour still needs a manual test (see each mod's test checklist).
.PARAMETER Mod
    Only build/check mods whose project name contains one of these strings.
.PARAMETER KeepRunning
    Leave the game running after the check (to continue testing by hand).
.EXAMPLE
    ./tools/Test-Smoke.ps1
    ./tools/Test-Smoke.ps1 -Mod Crossbow -KeepRunning
#>
[CmdletBinding()]
param(
    [string[]]$Mod,
    [int]$TimeoutSec = 240,
    [int]$SettleSec = 15,
    [switch]$NoBuild,
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$root = Get-RepoRoot
$game = Get-ValheimDir
$log = Get-BepInExLogPath
$projects = Get-ModProjects $Mod
if ($projects.Count -eq 0) { Write-Fail 'No mod projects matched.'; exit 1 }
if (Get-ValheimProcess) { Write-Fail 'Valheim is running. Close it first (the smoke test needs a fresh log).'; exit 1 }

if (-not $NoBuild) {
    Write-Step 'Build + deploy (Debug)'
    foreach ($p in $projects) {
        & dotnet build $p.FullName -c Debug -nologo -v q -p:DeployToGame=true
        if ($LASTEXITCODE -ne 0) { Write-Fail "build failed: $($p.BaseName)"; exit 1 }
    }
}

# Me know what to expect: guid -> display name.
$expected = [ordered]@{}
foreach ($p in $projects) {
    $x = [xml](Get-Content $p.FullName -Raw)
    $name = $x.Project.PropertyGroup | ForEach-Object { $_.ModName } | Where-Object { $_ } | Select-Object -First 1
    $expected[$p.BaseName] = $name
    $dll = Join-Path $game "BepInEx\plugins\$($p.BaseName)\$($p.BaseName).dll"
    if (-not (Test-Path $dll)) { Write-Fail "not deployed: $dll"; exit 1 }
}
Write-Ok ("expecting: " + ($expected.Keys -join ', '))

$launchTime = Get-Date
$proc = & (Join-Path $PSScriptRoot 'Start-Game.ps1')
$proc = @($proc) | Where-Object { $_ -is [System.Diagnostics.Process] } | Select-Object -First 1

function Read-LogText([string]$Path) {
    # Me open with share ReadWrite; game still write to file.
    $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

Write-Step "Wait for mods + main menu (timeout ${TimeoutSec}s)"
$deadline = $launchTime.AddSeconds($TimeoutSec)
$menuSeenAt = $null
$text = ''
$ready = @{}
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    if ($proc.HasExited) { Write-Fail "game exited early (code $($proc.ExitCode))"; break }
    if (-not (Test-Path $log) -or (Get-Item $log).LastWriteTime -lt $launchTime) { continue }
    $text = Read-LogText $log
    foreach ($m in [regex]::Matches($text, '\[MC:ready\] (\S+) (\S+)')) { $ready[$m.Groups[1].Value] = $m.Groups[2].Value }
    if (-not $menuSeenAt -and $text -match 'Valheim version: ') { $menuSeenAt = Get-Date; Write-Ok 'main menu started' }
    $allReady = @($expected.Keys | Where-Object { -not $ready.ContainsKey($_) }).Count -eq 0
    if ($menuSeenAt -and $allReady -and ((Get-Date) - $menuSeenAt).TotalSeconds -ge $SettleSec) { break }
}
if (Test-Path $log) { $text = Read-LogText $log }

if (-not $KeepRunning -and -not $proc.HasExited) {
    Write-Step 'Close game'
    Stop-Process -Id $proc.Id -Force
}

# Me judge. Error lines + next lines (stack trace). Ours = mention guid, mod name, MC namespace, or Harmony.
$lines = $text -split "`r?`n"
$ourRegex = (@("\b$(Get-ModAuthor)\.", 'HarmonyLib', 'HarmonyX', 'Chainloader') + ($expected.Values | ForEach-Object { [regex]::Escape($_) })) -join '|'
$ourErrors = New-Object System.Collections.Generic.List[string]
$otherErrors = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\[(Error|Fatal)') {
        $block = @($lines[$i])
        $j = $i + 1
        while ($j -lt $lines.Count -and $lines[$j] -notmatch '^\[(Info|Message|Warning|Error|Fatal|Debug)') { $block += $lines[$j]; $j++ }
        $joined = $block -join "`n"
        if ($joined -match $ourRegex) { $ourErrors.Add($joined) } else { $otherErrors.Add($joined) }
    }
}

Write-Step 'Result'
$pass = $true
foreach ($guid in $expected.Keys) {
    if ($ready.ContainsKey($guid)) { Write-Ok "$guid $($ready[$guid]) ready" } else { Write-Fail "$guid never logged ready"; $pass = $false }
}
if (-not $menuSeenAt) { Write-Fail 'main menu never started'; $pass = $false }
if ($ourErrors.Count) {
    $pass = $false
    Write-Fail "$($ourErrors.Count) error(s) from our mods / Harmony:"
    $ourErrors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
}
if ($otherErrors.Count) {
    Write-Warn2 "$($otherErrors.Count) other error(s) in log (vanilla/other mods), first 3:"
    $otherErrors | Select-Object -First 3 | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow }
}
Write-Host "  log: $log"
if ($pass) { Write-Host 'SMOKE TEST PASSED' -ForegroundColor Green; exit 0 }
Write-Host 'SMOKE TEST FAILED' -ForegroundColor Red
exit 1
