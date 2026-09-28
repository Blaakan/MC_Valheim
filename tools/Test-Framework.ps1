<#
.SYNOPSIS
    Automated test of the shared mod framework, using two probe mods (tests/Probes):
      Probe A (no deps) and Probe B (requires A).
    Launch game -> both Active and patched -> edit A's config file on disk (Enabled = false) while the game runs
    -> A deactivates, B deactivates with "needs Probe A, which is turned off", B's Status written to its cfg
    -> A back on -> both re-activate and B's patch runs again. Then close game and remove probes.
    Covers: live toggle, config file watching, dependency gating + recovery, patch/unpatch, status write-back.
.PARAMETER KeepProbes
    Leave probe mods installed afterwards (to look at the MC Mods panel by hand).
.EXAMPLE
    ./tools/Test-Framework.ps1
#>
[CmdletBinding()]
param([int]$TimeoutSec = 240, [switch]$KeepProbes, [switch]$KeepRunning)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$root = Get-RepoRoot
$game = Get-ValheimDir
$log = Get-BepInExLogPath
$cfgDir = Join-Path $game 'BepInEx\config'
$probes = @('MC.Core.Probe.A', 'MC.Core.Probe.B')
if (Get-ValheimProcess) { Write-Fail 'Valheim is running. Close it first.'; exit 1 }

Write-Step 'Build + deploy probes'
foreach ($p in $probes) {
    $proj = Get-ChildItem (Join-Path $root 'tests\Probes') -Recurse -Filter "$p.csproj" | Select-Object -First 1
    & dotnet build $proj.FullName -c Debug -nologo -v q -p:DeployToGame=true
    if ($LASTEXITCODE -ne 0) { Write-Fail "build failed: $p"; exit 1 }
}

function Set-Enabled([string]$Guid, [bool]$Value) {
    $cfg = Join-Path $cfgDir "$Guid.cfg"
    if (-not (Test-Path $cfg)) { return }
    $text = [IO.File]::ReadAllText($cfg)
    $text = [regex]::Replace($text, '(?m)^(Enabled\s*=\s*)(true|false)', "`${1}$($Value.ToString().ToLower())")
    [IO.File]::WriteAllText($cfg, $text, (New-Object System.Text.UTF8Encoding($false)))
}
function Get-Status([string]$Guid) {
    $cfg = Join-Path $cfgDir "$Guid.cfg"
    if (-not (Test-Path $cfg)) { return $null }
    $m = [regex]::Match([IO.File]::ReadAllText($cfg), '(?m)^Status\s*=\s*(.*)$')
    if ($m.Success) { $m.Groups[1].Value.Trim() } else { $null }
}
function Read-LogText {
    if (-not (Test-Path $log)) { return '' }
    $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try { (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}
# Me wait until regex match N times in log (count grow past baseline).
function Wait-Log([string]$Pattern, [int]$Count = 1, [int]$Seconds = 30) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if ([regex]::Matches((Read-LogText), $Pattern).Count -ge $Count) { return $true }
        Start-Sleep -Milliseconds 500
    }
    $false
}

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    $results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
    if ($Ok) { Write-Ok $Name } else { Write-Fail "$Name $Detail" }
}

Set-Enabled 'MC.Core.Probe.A' $true
Set-Enabled 'MC.Core.Probe.B' $true
$launch = Get-Date
$proc = @(& (Join-Path $PSScriptRoot 'Start-Game.ps1')) | Where-Object { $_ -is [System.Diagnostics.Process] } | Select-Object -First 1

try {
    Write-Step 'Startup'
    $deadline = $launch.AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline -and (-not (Test-Path $log) -or (Get-Item $log).LastWriteTime -lt $launch)) { Start-Sleep 1 }
    Check 'A ready' (Wait-Log '\[MC:ready\] MC\.Core\.Probe\.A' 1 $TimeoutSec)
    Check 'B ready' (Wait-Log '\[MC:ready\] MC\.Core\.Probe\.B' 1 30)
    Check 'main menu' (Wait-Log 'Valheim version: ' 1 $TimeoutSec)
    Check 'B patch applied (active)' (Wait-Log 'Probe B\] \[probe\] patch running' 1 30)
    Start-Sleep 2
    Check 'B status Active in cfg' ((Get-Status 'MC.Core.Probe.B') -eq 'Active.') "got '$(Get-Status 'MC.Core.Probe.B')'"
    # Negative test: Probe A has a method using a dll never deployed. JitCheck must catch it (proves it compiles for real).
    Check 'JitCheck really compiles (catches missing dll in Probe A)' (Wait-Log 'Probe A\] JitCheck: .*UseMissingLib failed to compile' 1 5)
    Check 'JitCheck summary line present' (Wait-Log 'Probe B\] JitCheck: JIT-compiled \d+ methods, 0 failures' 1 5)

    Write-Step 'Turn A off by editing its cfg on disk'
    Set-Enabled 'MC.Core.Probe.A' $false
    Check 'A deactivated' (Wait-Log 'Probe A\] Deactivated: Off' 1 15)
    Check 'B deactivated because of A' (Wait-Log 'Probe B\] Deactivated: Inactive: needs Probe A, which is turned off\.' 1 15)
    Start-Sleep 2
    $aStatus = Get-Status 'MC.Core.Probe.A'
    Check 'A status Off in its own cfg (edited file)' ($aStatus -eq 'Off (disabled in settings).') "got '$aStatus'"
    $bStatus = Get-Status 'MC.Core.Probe.B'
    Check 'B status explains why in cfg' ($bStatus -eq 'Inactive: needs Probe A, which is turned off.') "got '$bStatus'"
    $bEnabled = [regex]::Match([IO.File]::ReadAllText((Join-Path $cfgDir 'MC.Core.Probe.B.cfg')), '(?m)^Enabled\s*=\s*(\w+)').Groups[1].Value
    Check 'B still Enabled=true (not auto-disabled)' ($bEnabled -eq 'true') "got '$bEnabled'"

    Write-Step 'While A is off, toggle B through its own cfg'
    Set-Enabled 'MC.Core.Probe.B' $false
    Start-Sleep 3
    Check 'B status Off after its own edit' ((Get-Status 'MC.Core.Probe.B') -eq 'Off (disabled in settings).') "got '$(Get-Status 'MC.Core.Probe.B')'"
    Set-Enabled 'MC.Core.Probe.B' $true
    Start-Sleep 3
    Check 'B status back to needs-A after its own edit' ((Get-Status 'MC.Core.Probe.B') -eq 'Inactive: needs Probe A, which is turned off.') "got '$(Get-Status 'MC.Core.Probe.B')'"

    Write-Step 'Turn A back on'
    Set-Enabled 'MC.Core.Probe.A' $true
    Check 'A re-activated' (Wait-Log 'Probe A\] Activated\.' 2 15)
    Check 'B re-activated by itself' (Wait-Log 'Probe B\] Activated\.' 2 15)
    Check 'B patch re-applied' (Wait-Log 'Probe B\] \[probe\] patch running' 2 15)
    Start-Sleep 2
    Check 'A status Active in its own cfg' ((Get-Status 'MC.Core.Probe.A') -eq 'Active.') "got '$(Get-Status 'MC.Core.Probe.A')'"
    Check 'B status Active in cfg' ((Get-Status 'MC.Core.Probe.B') -eq 'Active.') "got '$(Get-Status 'MC.Core.Probe.B')'"

    $text = Read-LogText
    # Expected: the JitCheck negative-test error from Probe A. Anything else = bug.
    $errors = [regex]::Matches($text, '(?m)^\[(Error|Fatal)\s*:\s*(Probe [AB]|HarmonyX|BepInEx)\].*$') | ForEach-Object { $_.Value } |
        Where-Object { $_ -notmatch 'JitCheck: .*UseMissingLib failed to compile' }
    Check 'no framework errors' (@($errors).Count -eq 0) (@($errors) -join ' | ')
}
finally {
    if (-not $KeepRunning -and $proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Start-Sleep 2 }
    if (-not $KeepProbes -and -not $KeepRunning) {
        Write-Step 'Remove probes'
        foreach ($p in $probes) {
            $dir = Join-Path $game "BepInEx\plugins\$p"
            if (Test-Path $dir) { [IO.Directory]::Delete($dir, $true) }
            $cfg = Join-Path $cfgDir "$p.cfg"
            if (Test-Path $cfg) { [IO.File]::Delete($cfg) }
        }
        Write-Ok 'probes removed'
    }
}

$failedCount = @($results | Where-Object { -not $_.Ok }).Count
if ($failedCount -eq 0) { Write-Host "FRAMEWORK TEST PASSED ($($results.Count) checks)" -ForegroundColor Green; exit 0 }
Write-Host "FRAMEWORK TEST FAILED ($failedCount of $($results.Count) checks)" -ForegroundColor Red
exit 1
