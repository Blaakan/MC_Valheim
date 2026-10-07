<#
.SYNOPSIS
    In-world self tests: build + deploy mods and the world probe (tests/Probes/Core.Probe.World), launch Valheim
    with a throwaway save folder, let the probe create and start the single-player world "MCProbe" and run every
    registered self test (src/Shared/SelfTest.cs), then read the "[selftest]" log lines, close the game and remove
    the probe. Exit code 0 = the probe logged DONE with fail=0, no error came from our mods, and at least one mod
    test ran (plus: every -Only piece matched a test, and with -Mod a test of the selected mods ran).
.DESCRIPTION
    Your own characters and worlds are never touched: the probe points all save data at <run dir>\saves and turns
    Steam Cloud saves off for that game session only, and proves it in the log before it starts the world.
    Mod tests change real mod settings for a moment (BepInEx/config/MC.*.cfg): the script copies those files to
    <run dir>\config-backup before launch and puts them back byte for byte once the game has exited, also when the
    run is cut short (Ctrl+C, timeout, error) or left running with -KeepRunning.
    Run folder: %TEMP%\MC_Valheim_InWorld\<yyyyMMdd-HHmmss>. Its saves are deleted afterwards; the screenshots
    (shots\), the config backup and a copy of the BepInEx log are kept (10 newest run folders). A new world (fixed
    seed) is generated on every run: about 25-30 s from world start to spawn, about 1 minute from launch to quit
    plus the tests. Debug builds only (self tests do not exist in Release builds).
.PARAMETER Mod
    Only build/deploy mods whose project name contains one of these strings (all mods when omitted). Mods already
    deployed in the game folder still load and their tests still run: use -Only to choose tests. The run fails
    when no test of the selected mods ran, or when a selected mod that has self tests ran none (not counting tests
    that -Only filtered out).
.PARAMETER Only
    Only run self tests whose name contains one of these strings. probe.baseline and probe.runner always run.
    The run fails when a piece matches no test.
.PARAMETER KeepRunning
    Leave the game running in the world after the tests. When you close it, the probe and the run's saves are
    removed and the mod config files put back as before the run.
.PARAMETER TimeoutSec
    Whole run, from launch to the DONE line (world generation included).
.PARAMETER TestTimeoutSec
    Per-test timeout inside the game.
.PARAMETER NoBuild
    Do not rebuild mods (the probe is always built and deployed).
.PARAMETER AsConfigured
    Keep MC mods turned off in their .cfg (Enabled = false) off. By default the script turns them on for the run
    (the files are put back as before afterwards like every other change), so their tests run too.
.EXAMPLE
    ./tools/Test-InWorld.ps1 -Mod Sleep.ThroughDay
    ./tools/Test-InWorld.ps1 -NoBuild -Only sleep.
#>
[CmdletBinding()]
param(
    [string[]]$Mod,
    [string[]]$Only,
    [switch]$KeepRunning,
    [int]$TimeoutSec = 900,
    [int]$TestTimeoutSec = 120,
    [switch]$NoBuild,
    [switch]$AsConfigured
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$root = Get-RepoRoot
$game = Get-ValheimDir
$log = Get-BepInExLogPath
$probeGuid = 'MC.Core.Probe.World'
$probeProj = Join-Path $root "tests\Probes\Core.Probe.World\$probeGuid.csproj"
$probeCfg = Join-Path $game "BepInEx\config\$probeGuid.cfg"
$probeDirs = @((Get-ModDeployDir $probeGuid), (Join-Path $game "BepInEx\plugins\$probeGuid"))
$collectionDir = Join-Path $game ('BepInEx\plugins\' + (Get-ModCollectionFolder))
$envNames = @('MC_INWORLD_DIR', 'MC_SELFTEST_FILTER', 'MC_SELFTEST_TIMEOUT', 'MC_INWORLD_KEEP')
$cfgDir = Join-Path $game 'BepInEx\config'
$cfgPattern = "$(Get-ModAuthor).*.cfg"
$probeCfgName = "$probeGuid.cfg"
$probeTests = @('probe.baseline', 'probe.runner')
# Me split -Only like the probe do (comma inside one string = two pieces).
$filters = @(((@($Only) | Where-Object { $_ }) -join ',') -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

if (Get-ValheimProcess) { Write-Fail 'Valheim is running. Close it first (the test needs a fresh log and its own save folder).'; exit 1 }
if (-not (Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) { Write-Fail 'Steam is not running. Start Steam and log in first: the game needs it.'; exit 1 }

Write-Step 'Check tool scripts + ModRequires graph'
if (-not (Test-ScriptsParse)) { Write-Fail 'tool scripts are broken (see above)'; exit 1 }
Assert-ModRequiresAcyclic
Write-Ok 'scripts parse, no dependency cycle'

# Me know -Mod pick also with -NoBuild: result check want "a test of these mods ran".
$selected = @()
if ($Mod) {
    $selected = @(Get-ModProjects $Mod)
    if ($selected.Count -eq 0) { Write-Fail "No mod projects matched -Mod $($Mod -join ',')."; exit 1 }
}
if (-not $NoBuild) {
    $projects = if ($Mod) { $selected } else { Get-ModProjects }
    if ($projects.Count -eq 0) { Write-Fail 'No mod projects matched.'; exit 1 }
    Write-Step 'Build + deploy mods (Debug)'
    foreach ($p in $projects) {
        & dotnet build $p.FullName -c Debug -nologo -v q -p:DeployToGame=true
        if ($LASTEXITCODE -ne 0) { Write-Fail "build failed: $($p.BaseName)"; exit 1 }
    }
}
Write-Step 'Build + deploy world probe (Debug)'
& dotnet build $probeProj -c Debug -nologo -v q -p:DeployToGame=true
if ($LASTEXITCODE -ne 0) { Write-Fail 'build failed: world probe'; exit 1 }
$probeDll = Join-Path $probeDirs[0] "$probeGuid.dll"
if (-not (Test-Path $probeDll)) { Write-Fail "probe not deployed: $probeDll"; exit 1 }
# Me start probe with default config (Enabled = true), never a leftover one.
if (Test-Path $probeCfg) { [IO.File]::Delete($probeCfg) }
Write-Ok "probe deployed: $probeDll"

# Me know every MC mod name: errors naming one of them (or MC. / Harmony) are ours.
$modNames = @(Get-ModProjects | ForEach-Object { Get-CsprojProp $_.FullName 'ModName' } | Where-Object { $_ }) + 'Probe World'
$ourRegex = (@("\b$(Get-ModAuthor)\.", 'HarmonyLib', 'HarmonyX', 'Chainloader') + ($modNames | ForEach-Object { [regex]::Escape($_) })) -join '|'

$runBase = Join-Path $env:TEMP 'MC_Valheim_InWorld'
# Earlier run killed before it put configs back (pending.txt still there): me stop, else this run would back up the
# changed files as "before" and the change stay for good. User compare, copy back what they want, delete the marker.
if (Test-Path $runBase) {
    $stale = @(Get-ChildItem $runBase -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'config-backup\pending.txt') })
    if ($stale.Count) {
        foreach ($s in $stale) {
            Write-Fail "the run $($s.Name) ended before it put the MC mod config files back (the script itself was stopped)"
            Write-Host "      Compare $cfgDir with $(Join-Path $s.FullName 'config-backup'), copy back what you want,"
            Write-Host "      then delete $(Join-Path $s.FullName 'config-backup\pending.txt') and run again."
        }
        exit 1
    }
}
# Me keep the 10 newest run folders (shots + log copy), older ones go.
if (Test-Path $runBase) {
    Get-ChildItem $runBase -Directory | Where-Object { $_.Name -match '^\d{8}-\d{6}$' } | Sort-Object Name -Descending |
        Select-Object -Skip 9 | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}
$runDir = Join-Path $runBase (Get-Date -Format 'yyyyMMdd-HHmmss')
$savesDir = Join-Path $runDir 'saves'
$shotsDir = Join-Path $runDir 'shots'
$cfgBackup = Join-Path $runDir 'config-backup'
$cleanupLog = Join-Path $runDir 'cleanup.log'
[void](New-Item -ItemType Directory -Force -Path $savesDir)
[void](New-Item -ItemType Directory -Force -Path $shotsDir)
[void](New-Item -ItemType Directory -Force -Path $cfgBackup)
Write-Ok "run folder: $runDir"

# Me copy every MC mod config before launch: tests set real settings for a moment, and a run cut short (Ctrl+C,
# timeout, kill) skip their clean-up. files.txt written last = backup whole; no files.txt = restore touch nothing.
$cfgNames = @(Get-ChildItem -LiteralPath $cfgDir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like $cfgPattern -and $_.Name -ne $probeCfgName } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $cfgBackup; $_.Name })
[IO.File]::WriteAllLines((Join-Path $cfgBackup 'files.txt'), [string[]]$cfgNames)
# pending.txt = configs not put back yet. Restore delete it when every file went back fine; next run check it.
[IO.File]::WriteAllText((Join-Path $cfgBackup 'pending.txt'), 'configs not put back yet')
Write-Ok "$($cfgNames.Count) mod config file(s) backed up (put back after the run)"

# Mod off in its .cfg run no test. Me turn it on for this run (backup above put file back after), unless -AsConfigured.
$turnedOn = @()
if (-not $AsConfigured) {
    foreach ($n in $cfgNames) {
        $p = Join-Path $cfgDir $n
        $new = Enable-ModConfigText ([IO.File]::ReadAllText($p))
        if ($null -eq $new) { continue }
        [IO.File]::WriteAllText($p, $new, (New-Object System.Text.UTF8Encoding $false))
        $turnedOn += [IO.Path]::GetFileNameWithoutExtension($n)
    }
    if ($turnedOn.Count) { Write-Ok "turned on for this run (off in their .cfg; put back after): $($turnedOn -join ', ')" }
}

# Me put MC mod configs back byte for byte once game gone: changed or deleted ones from backup, ones born in run
# deleted. Self-contained (no module function): hidden -KeepRunning watcher run same text. One output line per file.
$restoreConfigs = {
    param([string]$ConfigDir, [string]$BackupDir, [string]$Pattern, [string]$Skip)
    $list = Join-Path $BackupDir 'files.txt'
    if (-not (Test-Path -LiteralPath $list)) { return }
    $names = @([IO.File]::ReadAllLines($list) | Where-Object { $_ })
    foreach ($n in $names) {
        $src = Join-Path $BackupDir $n
        $dst = Join-Path $ConfigDir $n
        try {
            $old = [Convert]::ToBase64String([IO.File]::ReadAllBytes($src))
            if ((Test-Path -LiteralPath $dst) -and $old -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($dst))) { continue }
            [IO.File]::Copy($src, $dst, $true)
            "restored $n"
        }
        catch { "failed $n $($_.Exception.Message)" }
    }
    foreach ($f in @(Get-ChildItem -LiteralPath $ConfigDir -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -notlike $Pattern -or $f.Name -eq $Skip -or $names -contains $f.Name) { continue }
        try { [IO.File]::Delete($f.FullName); "removed $($f.Name)" } catch { "failed $($f.Name) $($_.Exception.Message)" }
    }
    # Me check each backed-up file is back as before; only then drop the pending marker.
    $ok = $true
    foreach ($n in $names) {
        $src = Join-Path $BackupDir $n
        $dst = Join-Path $ConfigDir $n
        try {
            if (-not (Test-Path -LiteralPath $dst) -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($src)) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($dst))) { $ok = $false }
        }
        catch { $ok = $false }
    }
    $pending = Join-Path $BackupDir 'pending.txt'
    if ($ok -and (Test-Path -LiteralPath $pending)) { try { [IO.File]::Delete($pending) } catch { } }
}

# Game must be gone before this (else its config watcher reload the files mid-session).
function Restore-ModConfigs([bool]$CutShort) {
    $out = @(& $restoreConfigs $cfgDir $cfgBackup $cfgPattern $probeCfgName)
    if (-not $out.Count) { Write-Ok "mod config files as before the run ($($cfgNames.Count) checked)"; return }
    foreach ($line in $out) {
        $word, $name, $msg = $line -split ' ', 3
        switch ($word) {
            'restored' { Write-Warn2 ("config put back as before the run: $name" + $(if ($CutShort) { ' (the run was cut short before the tests cleaned up)' } else { '' })) }
            'removed' { Write-Ok "config created during the run removed: $name" }
            default { Write-Warn2 "could not put back $name ($msg): the copy from before the run is in $cfgBackup" }
        }
    }
}

function Read-LogText([string]$Path) {
    if (-not (Test-Path $Path)) { return '' }
    $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

# Me read log only when this launch wrote it: old log (game died before BepInEx start) must never count.
function Read-RunLog {
    if ($launch -and (Test-Path $log) -and (Get-Item $log).LastWriteTime -ge $launch) { Read-LogText $log } else { '' }
}

function Get-DoneLine([string]$Text) {
    $m = [regex]::Match($Text, '\[selftest\] DONE pass=(\d+) fail=(\d+) tests=(\d+)')
    if ($m.Success) { [pscustomobject]@{ Pass = [int]$m.Groups[1].Value; Fail = [int]$m.Groups[2].Value; Tests = [int]$m.Groups[3].Value } }
}

# Me delete with retries: game may hold files a moment after it exits.
function Remove-WithRetry([string]$Path) {
    for ($i = 0; $i -lt 15; $i++) {
        if (-not (Test-Path -LiteralPath $Path)) { return $true }
        try { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop; return $true } catch { Start-Sleep -Seconds 1 }
    }
    -not (Test-Path -LiteralPath $Path)
}

function Remove-Probe {
    $ok = $true
    foreach ($dir in $probeDirs) { if (-not (Remove-WithRetry $dir)) { $ok = $false; Write-Warn2 "could not remove $dir" } }
    if (-not (Remove-WithRetry $probeCfg)) { $ok = $false; Write-Warn2 "could not remove $probeCfg" }
    # Me also drop category/collection folders left empty.
    foreach ($dir in @((Join-Path $collectionDir 'Core'), $collectionDir)) {
        try { if ((Test-Path $dir) -and -not (Get-ChildItem $dir -Force)) { [IO.Directory]::Delete($dir) } } catch { }
    }
    if ($ok) { Write-Ok 'probe removed from the game folder' }
}

# -KeepRunning: game still open, dll locked. Me leave a hidden PowerShell that cleans up once the game exits.
function Start-CleanupWatcher([int]$GamePid) {
    $q = { param($s) "'" + ($s -replace "'", "''") + "'" }
    $targets = @($probeDirs + $probeCfg + $savesDir) | ForEach-Object { & $q $_ }
    $cmd = "Wait-Process -Id $GamePid -ErrorAction SilentlyContinue; Start-Sleep -Seconds 3; " +
        "foreach (`$t in @($($targets -join ', '))) { for (`$i = 0; `$i -lt 15 -and (Test-Path -LiteralPath `$t); `$i++) " +
        "{ Remove-Item -LiteralPath `$t -Recurse -Force -ErrorAction SilentlyContinue; if (Test-Path -LiteralPath `$t) { Start-Sleep -Seconds 1 } } }; " +
        "foreach (`$d in @($(& $q (Join-Path $collectionDir 'Core')), $(& $q $collectionDir))) " +
        "{ if ((Test-Path `$d) -and -not (Get-ChildItem `$d -Force)) { Remove-Item -LiteralPath `$d -Force } }; " +
        "`$r = @(& {$restoreConfigs} $(& $q $cfgDir) $(& $q $cfgBackup) $(& $q $cfgPattern) $(& $q $probeCfgName)); " +
        "if (`$r.Count) { [IO.File]::WriteAllLines($(& $q $cleanupLog), [string[]]`$r) }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) | Out-Null
    Write-Warn2 "game left running (pid $GamePid): when it exits, the probe and saves are removed and the mod config files put back as before the run (changes listed in $cleanupLog)"
}

$selftestRegex = '(?m)^\[(?<lvl>[A-Za-z]+)\s*:\s*(?<src>[^\]]*)\]\s\[selftest\]\s(?<kind>[A-Z]+)\s?(?<rest>[^\r\n]*)'
function Write-SelftestLine([string]$Kind, [string]$Rest, [string]$Indent = '  ') {
    $color = switch ($Kind) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'SHOT' { 'Cyan' } 'NOTE' { 'Gray' } default { 'White' } }
    Write-Host ("{0}{1,-5} {2}" -f $Indent, $Kind, $Rest) -ForegroundColor $color
}

$proc = $null
$launch = $null
$done = $null
$exitedEarly = $false
$timedOut = $false
$text = ''
$oldEnv = @{}
foreach ($n in $envNames) { $oldEnv[$n] = [Environment]::GetEnvironmentVariable($n, 'Process') }
try {
    # Env vars only for the game process: me set them, launch, put them back at once.
    try {
        [Environment]::SetEnvironmentVariable('MC_INWORLD_DIR', $runDir, 'Process')
        [Environment]::SetEnvironmentVariable('MC_SELFTEST_FILTER', ($filters -join ','), 'Process')
        [Environment]::SetEnvironmentVariable('MC_SELFTEST_TIMEOUT', [string]$TestTimeoutSec, 'Process')
        [Environment]::SetEnvironmentVariable('MC_INWORLD_KEEP', $(if ($KeepRunning) { '1' } else { $null }), 'Process')
        $launch = Get-Date
        $proc = @(& (Join-Path $PSScriptRoot 'Start-Game.ps1')) | Where-Object { $_ -is [System.Diagnostics.Process] } | Select-Object -First 1
    }
    finally {
        foreach ($n in $envNames) { [Environment]::SetEnvironmentVariable($n, $oldEnv[$n], 'Process') }
    }
    if (-not $proc) { Write-Fail 'game did not start'; exit 1 }

    Write-Step "Wait for the probe (timeout ${TimeoutSec}s)"
    $deadline = $launch.AddSeconds($TimeoutSec)
    $printed = 0
    while ($true) {
        Start-Sleep -Seconds 2
        # Me look at exit BEFORE reading: game that already quit has flushed its log, so its DONE is in this read.
        $exited = $proc.HasExited
        $text = Read-RunLog
        if ($text) {
            $lines = [regex]::Matches($text, $selftestRegex)
            for ($i = $printed; $i -lt $lines.Count; $i++) { Write-SelftestLine $lines[$i].Groups['kind'].Value $lines[$i].Groups['rest'].Value }
            $printed = $lines.Count
            $done = Get-DoneLine $text
            if ($done) { break }
        }
        if ($exited) { $exitedEarly = $true; Write-Fail "game exited before the probe finished (code $($proc.ExitCode))"; break }
        if ((Get-Date) -gt $deadline) { $timedOut = $true; Write-Fail "no DONE line within ${TimeoutSec}s"; break }
    }

    if ($done -and -not $KeepRunning) {
        Write-Step 'Wait for the game to quit'
        if (-not $proc.WaitForExit(90000)) { Write-Warn2 'game did not quit within 90 s: closing it'; Stop-Process -Id $proc.Id -Force }
        else { Write-Ok 'game quit by itself' }
    }
    Start-Sleep -Seconds 1
    $text = Read-RunLog
    # Me judge from this last full read too: DONE can reach the file only at shutdown, after the last poll.
    $final = Get-DoneLine $text
    if ($final) { $done = $final; $exitedEarly = $false; $timedOut = $false }
    if ($text) { [IO.File]::WriteAllText((Join-Path $runDir 'LogOutput.log'), $text) }
}
finally {
    # Game started but no process object back: before launch no Valheim ran, so one running now is ours.
    if (-not $proc -and $launch) { $proc = Get-ValheimProcess | Select-Object -First 1 }
    if ($proc -and -not $proc.HasExited -and -not $KeepRunning) {
        Write-Step 'Close game'
        Stop-Process -Id $proc.Id -Force
        [void]$proc.WaitForExit(20000)
    }
    if ($proc -and -not $proc.HasExited) {
        Start-CleanupWatcher $proc.Id
    }
    else {
        Write-Step 'Clean up'
        # Me put configs back first: most precious. Each step alone, one failing never skip the others.
        try { Restore-ModConfigs (-not $done) } catch { Write-Warn2 "could not put the mod config files back: $($_.Exception.Message) (copies in $cfgBackup)" }
        try { Remove-Probe } catch { Write-Warn2 "could not remove the probe: $($_.Exception.Message)" }
        if (Remove-WithRetry $savesDir) { Write-Ok 'run saves deleted (screenshots kept)' } else { Write-Warn2 "could not delete $savesDir" }
    }
}

# Me group [selftest] lines by test: lines between BEGIN x and END x belong to x; others to the name they carry.
$groups = [ordered]@{}
$verdicts = @{}
$current = $null
foreach ($m in [regex]::Matches($text, $selftestRegex)) {
    $kind = $m.Groups['kind'].Value
    $rest = $m.Groups['rest'].Value
    $nm = [regex]::Match($rest, '^(?<name>[^\s:]+):?\s?(?<detail>.*)$')
    $name = $nm.Groups['name'].Value
    $detail = $nm.Groups['detail'].Value
    switch ($kind) {
        'BEGIN' { $current = $name; if (-not $groups.Contains($name)) { $groups[$name] = New-Object System.Collections.Generic.List[object] } }
        'END' { $verdicts[$name] = $detail; $current = $null }
        'DONE' { }
        default {
            $key = if ($current) { $current } else { $name }
            if (-not $groups.Contains($key)) { $groups[$key] = New-Object System.Collections.Generic.List[object] }
            $groups[$key].Add([pscustomobject]@{ Kind = $kind; Name = $name; Detail = $detail })
        }
    }
}

# Me judge errors like Test-Smoke: error line + its stack trace. FAIL lines are already counted as test failures.
$lines = $text -split "`r?`n"
$ourErrors = New-Object System.Collections.Generic.List[string]
$otherErrors = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\[(Error|Fatal)') {
        $block = @($lines[$i])
        $j = $i + 1
        while ($j -lt $lines.Count -and $lines[$j] -notmatch '^\[(Info|Message|Warning|Error|Fatal|Debug)') { $block += $lines[$j]; $j++ }
        if ($lines[$i] -match '\[selftest\] FAIL ') { continue }
        $joined = ($block -join "`n").TrimEnd()
        if ($joined -match $ourRegex) { $ourErrors.Add($joined) } else { $otherErrors.Add($joined) }
    }
}

Write-Step 'Results'
$failLines = 0
foreach ($key in $groups.Keys) {
    $items = $groups[$key]
    $fails = @($items | Where-Object { $_.Kind -eq 'FAIL' }).Count
    $failLines += $fails
    $verdict = if ($verdicts.ContainsKey($key)) { $verdicts[$key] } elseif ($fails) { 'FAILED' } else { 'ok' }
    $bad = $fails -gt 0 -or $verdict -match 'FAILED'
    Write-Host ('  {0} {1}' -f $key, $verdict) -ForegroundColor $(if ($bad) { 'Red' } else { 'White' })
    foreach ($it in $items) {
        $detail = if ($it.Name -ne $key) { "$($it.Name): $($it.Detail)" } else { $it.Detail }
        if ($it.Kind -eq 'SHOT') {
            $path = $it.Detail.Trim()
            $detail = if (Test-Path -LiteralPath $path) { "$path ($([math]::Round((Get-Item -LiteralPath $path).Length / 1KB)) KB)" } else { "$path (MISSING)" }
        }
        Write-SelftestLine $it.Kind $detail '      '
    }
}
if (-not $groups.Count) { Write-Warn2 'no [selftest] lines in the log (did the probe load? look for "Probe World" in the log)' }

# Me list what really ran: "BEGIN <name> (<mod GUID>)"; filtered ones in "NOTE probe: filtered out: <name> (<GUID>), ...".
$ran = @([regex]::Matches($text, '\[selftest\] BEGIN (?<name>\S+)(?: \((?<owner>[^)]*)\))?') |
    ForEach-Object { [pscustomobject]@{ Name = $_.Groups['name'].Value; Owner = $_.Groups['owner'].Value } })
$filteredOut = @()
$fm = [regex]::Match($text, '\[selftest\] NOTE probe: filtered out: (?<list>[^\r\n]*)')
if ($fm.Success) {
    $filteredOut = @([regex]::Matches($fm.Groups['list'].Value, '(?<name>[^\s,]+) \((?<owner>[^)]*)\)') |
        ForEach-Object { [pscustomobject]@{ Name = $_.Groups['name'].Value; Owner = $_.Groups['owner'].Value } })
}

$selfTestSource = @{}
function Test-HasSelfTests($Project) {
    if (-not $selfTestSource.ContainsKey($Project.FullName)) {
        $files = @(Get-ChildItem $Project.DirectoryName -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
        $selfTestSource[$Project.FullName] = $files.Count -gt 0 -and @($files | Select-String -Pattern 'SelfTest\.Register\(' -List).Count -gt 0
    }
    $selfTestSource[$Project.FullName]
}

# Why a mod ran no test. Bad = it has tests that should have run (not just none written, or filtered by -Only).
function Get-NoTestReason($Project) {
    $guid = $Project.BaseName
    if (-not (Test-HasSelfTests $Project)) { return [pscustomobject]@{ Bad = $false; Text = 'has no self tests' } }
    if (@($filteredOut | Where-Object { $_.Owner -eq $guid }).Count) { return [pscustomobject]@{ Bad = $false; Text = 'its tests were filtered out by -Only' } }
    $ready = [regex]::Match($text, "(?m)^\[[A-Za-z]+\s*:(?<src>[^\]]+)\] \[MC:ready\] $([regex]::Escape($guid)) ")
    $why = if (-not $ready.Success) { 'did not load (not deployed? run without -NoBuild)' }
        elseif ($text -notmatch "(?m)^\[[A-Za-z]+\s*:$([regex]::Escape($ready.Groups['src'].Value))\] JitCheck:") { 'a Release build is deployed (self tests exist in Debug builds only; run without -NoBuild)' }
        elseif ($AsConfigured -and (Test-CfgDisabled (Join-Path $cfgBackup "$guid.cfg"))) { 'it is turned off in its .cfg (Enabled = false; run without -AsConfigured to turn it on for the run)' }
        else { 'it registered no test (not Active? see its Status line in the log)' }
    [pscustomobject]@{ Bad = $true; Text = $why }
}

# Enabled = false in [General] of the config as it was before the run.
function Test-CfgDisabled([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $general = [regex]::Match([IO.File]::ReadAllText($Path), '(?ms)^\[General\][ \t]*\r?$(?<body>.*?)(?=^\[|\z)')
    $general.Success -and $general.Groups['body'].Value -match '(?m)^\s*Enabled\s*=\s*false\s*$'
}

# Me refuse a green run that tested nothing: all filtered out, mods off in their .cfg, Release dll, -Only typo.
function Test-Coverage {
    $ok = $true
    $modRan = @($ran | Where-Object { $probeTests -notcontains $_.Name })
    $perMod = @($modRan | Group-Object Owner | ForEach-Object { "$($_.Name) $($_.Count)" })
    Write-Host ("  mod tests run: {0}{1}" -f $modRan.Count, $(if ($perMod.Count) { ' (' + ($perMod -join ', ') + ')' } else { '' }))
    $known = @($modRan + $filteredOut | ForEach-Object { $_.Name })
    $knownText = if ($known.Count) { "tests registered by mods: $($known -join ', ')" } else { 'no mod registered a test' }
    $listed = $false
    foreach ($f in $filters) {
        if (-not @($ran | Where-Object { $_.Name.IndexOf($f, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count) {
            Write-Fail "-Only '$f' matched no test ($knownText)"
            $ok = $false
            $listed = $true
        }
    }
    # Mods to explain: the -Mod pick, else every mod whose source registers self tests.
    $check = if ($Mod) { $selected } else { @(Get-ModProjects | Where-Object { Test-HasSelfTests $_ }) }
    foreach ($p in $check) {
        if (@($modRan | Where-Object { $_.Owner -eq $p.BaseName }).Count) { continue }
        $why = Get-NoTestReason $p
        if ($Mod -and $why.Bad) { Write-Fail "$($p.BaseName) ran no test: $($why.Text)"; $ok = $false }
        elseif ($Mod -or $why.Bad) { Write-Warn2 "$($p.BaseName) ran no test: $($why.Text)" }
    }
    $guids = @($selected | ForEach-Object { $_.BaseName })
    if (-not $modRan.Count) {
        Write-Fail ("no mod test ran, only the probe's own: a run that tests no mod does not pass" + $(if ($listed) { '' } else { " ($knownText)" }))
        $ok = $false
    }
    elseif ($Mod -and -not @($modRan | Where-Object { $guids -contains $_.Owner }).Count) {
        Write-Fail "no test of the mods picked with -Mod $($Mod -join ',') ran (only other mods' tests did)"
        $ok = $false
    }
    $ok
}

$pass = $true
if ($done) {
    $color = if ($done.Fail -gt 0) { 'Red' } else { 'Green' }
    Write-Host ("  DONE: {0} passed, {1} failed, {2} test(s)" -f $done.Pass, $done.Fail, $done.Tests) -ForegroundColor $color
    if ($done.Fail -gt 0) { $pass = $false }
}
else {
    $pass = $false
    $why = if ($text -match 'Steam is not initialized') { 'Steam login failed (is Steam running and logged in?)' }
        elseif ($exitedEarly) { 'the game exited early' }
        elseif ($timedOut) { "timeout after ${TimeoutSec}s" }
        else { 'unknown' }
    Write-Fail "the probe never logged DONE: $why"
}
if ($failLines -gt 0) { $pass = $false }
# Coverage only when the world came up (a BEGIN line): a failed setup already fails with its own reason.
if ($done -and $ran.Count -and -not (Test-Coverage)) { $pass = $false }
if ($ourErrors.Count) {
    $pass = $false
    Write-Fail "$($ourErrors.Count) error(s) from our mods / Harmony:"
    $ourErrors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
}
if ($otherErrors.Count) {
    Write-Warn2 "$($otherErrors.Count) other error(s) in log (vanilla/other mods), first 3:"
    $otherErrors | Select-Object -First 3 | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow }
}
Write-Host "  shots: $shotsDir"
Write-Host "  log copy: $(Join-Path $runDir 'LogOutput.log')"
if ($pass) { Write-Host 'IN-WORLD TEST PASSED' -ForegroundColor Green; exit 0 }
Write-Host 'IN-WORLD TEST FAILED' -ForegroundColor Red
exit 1
