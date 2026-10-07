<#
.SYNOPSIS
    Multiplayer self tests: a Valheim dedicated server and a game client on this PC, the client joins the server, the
    probes run the multiplayer tests of every scenario asked, then both quit. Exit code 0 = every scenario passed.
.DESCRIPTION
    Nothing of yours is changed: the server runs from a cached copy of the dedicated server install
    (%LOCALAPPDATA%\MC_Valheim\server, refreshed from the Steam install on every run, plus the Doorstop loader), and
    server and client each load BepInEx from their own folder in the run folder (Doorstop's --doorstop-target-assembly),
    with only MC mods, the probes and fresh default configs. Your game folder, its plugins and configs, and your
    dedicated server install are only read. The client's saves go to the run folder (world probe save isolation), the
    server's to the run folder (-savedir); the server world is shared by the scenarios of one run.
    Scenarios:
      modded          server and client run every MC mod: probe.mp.baseline checks every Both mod is active on both
                      sides, then every mod test registered with SelfTest.RegisterMultiplayer(name, "modded", ...),
                      then probe.mp.server-toggle (the server turns each Both mod off and on: the client follows live).
      vanilla-server  server runs no MC mod (probe only): every Both mod must be inactive on the client (ServerMissing),
                      every client mod active.
      vanilla-client  client runs no MC mod (probe only): the server must refuse it ("Incompatible version").
      open-server     like vanilla-client, but the server has AllowPlayersWithoutMod = true for every mod that refuses
                      players: the client must stay in, and each such mod must log that it let the player in.
      each-off        server and client run every MC mod; one test per Both mod turns it off on the client while in:
                      a mod that refuses players must refuse (and again when joining with it off), the others not
                      (a mod with the older player check only refuses a player without the mod: the test says so).
    Run folder: %TEMP%\MC_Valheim_MP\<yyyyMMdd-HHmmss> (10 newest kept): per scenario the client and server BepInEx
    folders with their LogOutput.log, the client's screenshots (client\shots); saves are deleted afterwards.
    The game must be closed (one client at a time) and Steam running. Debug builds only.
.PARAMETER Scenario
    One or more of modded, vanilla-server, vanilla-client, open-server, each-off; 'all' (default) = every one, in that order.
.PARAMETER Only
    Only run mod tests whose name contains one of these strings (probe tests always run).
.PARAMETER NoModBuild
    Do not build the mods: use the mod DLLs built by the newest earlier run (its stage folder). The probes are always
    built. For re-running a scenario while mod code is being edited.
.PARAMETER KeepRunning
    Leave client and server running after the last scenario's tests (only with one scenario).
.PARAMETER Port
    Server port (default 2466, so a server of yours on 2456 is not in the way).
.PARAMETER ServerDir
    Dedicated server install (default: "Valheim dedicated server" in your Steam libraries).
.EXAMPLE
    ./tools/Test-Multiplayer.ps1
    ./tools/Test-Multiplayer.ps1 -Scenario modded -Only dive.
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'modded', 'vanilla-server', 'vanilla-client', 'open-server', 'each-off')]
    [string[]]$Scenario = @('all'),
    [string[]]$Only,
    [switch]$KeepRunning,
    [switch]$NoModBuild,
    [int]$TimeoutSec = 2400,
    [int]$TestTimeoutSec = 120,
    [int]$Port = 2466,
    [string]$ServerDir
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$root = Get-RepoRoot
$game = Get-ValheimDir
$allScenarios = @('modded', 'vanilla-server', 'vanilla-client', 'open-server', 'each-off')
$scenarios = if ($Scenario -contains 'all') { $allScenarios } else { @($allScenarios | Where-Object { $Scenario -contains $_ }) }
if ($KeepRunning -and $scenarios.Count -ne 1) { Write-Fail '-KeepRunning needs exactly one -Scenario.'; exit 1 }
$filters = @(((@($Only) | Where-Object { $_ }) -join ',') -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$password = 'mcprobe1'
$envNames = @('MC_INWORLD_DIR', 'MC_SELFTEST_FILTER', 'MC_SELFTEST_TIMEOUT', 'MC_INWORLD_KEEP', 'MC_MP_JOIN', 'MC_MP_PASSWORD',
    'MC_MP_SCENARIO', 'MC_MP_SERVER_DIR', 'MC_MP_REFUSING', 'MC_MP_INSTALL_ONLY', 'SteamAppId')
$author = Get-ModAuthor
$collection = Get-ModCollectionFolder

if (Get-ValheimProcess) { Write-Fail 'Valheim is running. Close it first (the test starts its own client).'; exit 1 }
if (-not (Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) { Write-Fail 'Steam is not running. Start Steam and log in first.'; exit 1 }

Write-Step 'Check tool scripts + ModRequires graph'
if (-not (Test-ScriptsParse)) { Write-Fail 'tool scripts are broken (see above)'; exit 1 }
Assert-ModRequiresAcyclic
Write-Ok 'scripts parse, no dependency cycle'

# --- Dedicated server copy -------------------------------------------------------------------------------------
if (-not $ServerDir) {
    foreach ($lib in Get-SteamLibraries) {
        $d = Join-Path $lib 'steamapps\common\Valheim dedicated server'
        if (Test-Path (Join-Path $d 'valheim_server.exe')) { $ServerDir = $d; break }
    }
}
if (-not $ServerDir -or -not (Test-Path (Join-Path $ServerDir 'valheim_server.exe'))) {
    Write-Fail 'Valheim dedicated server not found. Install it from the Steam library (Tools) or pass -ServerDir.'; exit 1
}
$cache = Join-Path $env:LOCALAPPDATA 'MC_Valheim\server'
$serverExe = Join-Path $cache 'valheim_server.exe'
if (@(Get-Process -Name 'valheim_server' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$cache\*" }).Count) {
    Write-Fail "a test server from an earlier run is still running ($serverExe). Close it first."; exit 1
}
Write-Step "Refresh the server copy ($ServerDir -> $cache)"
[void](New-Item -ItemType Directory -Force -Path $cache)
# Me keep Doorstop files (not in Steam install) out of the mirror's delete list.
& robocopy $ServerDir $cache /MIR /XD BepInEx /XF winhttp.dll doorstop_config.ini .doorstop_version /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) { Write-Fail "robocopy failed (code $LASTEXITCODE)"; exit 1 }
foreach ($f in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
    $src = Join-Path $game $f
    if (-not (Test-Path -LiteralPath $src)) { Write-Fail "Doorstop file missing in the game folder: $src (BepInEx not installed?)"; exit 1 }
    Copy-Item -LiteralPath $src -Destination $cache -Force
}
Write-Ok 'server copy ready (Doorstop added; BepInEx comes from each run folder)'

# --- Run folder + build ----------------------------------------------------------------------------------------
$runBase = Join-Path $env:TEMP 'MC_Valheim_MP'
# -NoModBuild: newest earlier run that still has its built mods (picked before old runs are pruned).
$modsFrom = $null
if ($NoModBuild -and (Test-Path $runBase)) {
    $modsFrom = Get-ChildItem $runBase -Directory | Where-Object { $_.Name -match '^\d{8}-\d{6}$' -and (Test-Path (Join-Path $_.FullName "stage\mods\BepInEx\plugins\$collection")) } |
        Sort-Object Name -Descending | Select-Object -First 1
}
if ($NoModBuild -and -not $modsFrom) { Write-Fail "-NoModBuild: no earlier run with built mods in $runBase. Run once without it."; exit 1 }
if (Test-Path $runBase) {
    Get-ChildItem $runBase -Directory | Where-Object { $_.Name -match '^\d{8}-\d{6}$' } | Sort-Object Name -Descending |
        Select-Object -Skip 9 | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}
$runDir = Join-Path $runBase (Get-Date -Format 'yyyyMMdd-HHmmss')
$stage = Join-Path $runDir 'stage'
$serverSaves = Join-Path $runDir 'server-saves'
[void](New-Item -ItemType Directory -Force -Path $stage, $serverSaves)
Write-Ok "run folder: $runDir"

Write-Step 'Build mods + probes (Debug) into the run folder'
if ($modsFrom) {
    Copy-Item -LiteralPath (Join-Path $modsFrom.FullName 'stage\mods') -Destination (Join-Path $stage 'mods') -Recurse
    Write-Warn2 "-NoModBuild: mods not built, DLLs taken from run $($modsFrom.Name)"
}
else {
    & dotnet build (Join-Path $root 'ValheimMods.slnx') -c Debug -nologo -v q -p:DeployToGame=true "-p:DeployBepInExDir=$stage\mods\BepInEx\"
    if ($LASTEXITCODE -ne 0) { Write-Fail 'build failed: mods'; exit 1 }
}
& dotnet build (Join-Path $root 'tests\Probes\Core.Probe.World\MC.Core.Probe.World.csproj') -c Debug -nologo -v q -p:DeployToGame=true "-p:DeployBepInExDir=$stage\client-probe\BepInEx\"
if ($LASTEXITCODE -ne 0) { Write-Fail 'build failed: world probe'; exit 1 }
& dotnet build (Join-Path $root 'tests\Probes\Core.Probe.Server\MC.Core.Probe.Server.csproj') -c Debug -nologo -v q -p:DeployToGame=true "-p:DeployBepInExDir=$stage\server-probe\BepInEx\"
if ($LASTEXITCODE -ne 0) { Write-Fail 'build failed: server probe'; exit 1 }
$modPlugins = Join-Path $stage "mods\BepInEx\plugins\$collection"
if (-not (Test-Path $modPlugins)) { Write-Fail "no mods built into $modPlugins"; exit 1 }
Write-Ok 'built'

# Mods that refuse players without them (PlayerCheck) and their display names (log source). Live = the check also
# refuses a player whose game has the mod turned off (NetworkGate.PeerCompatible); the older copy only looks at
# "installed" (NetworkGate.PeerHasMod), so turning the mod off on a client is not refused there.
$refusing = @()
foreach ($p in Get-ModProjects) {
    if ((Get-CsprojProp $p.FullName 'ModSide') -ne 'Both') { continue }
    $check = Join-Path $p.DirectoryName 'PlayerCheck.cs'
    if (-not (Test-Path $check)) { continue }
    $live = [IO.File]::ReadAllText($check) -match 'NetworkGate\.PeerCompatible'
    $refusing += [pscustomobject]@{ Guid = $p.BaseName; Name = (Get-CsprojProp $p.FullName 'ModName'); Live = $live }
}
$installOnly = @($refusing | Where-Object { -not $_.Live })
if ($installOnly.Count) { Write-Warn2 "older player check (a player who turns the mod off is NOT refused, only one without the mod): $(($installOnly | ForEach-Object { $_.Name }) -join ', ')" }
$modNames = @(Get-ModProjects | ForEach-Object { Get-CsprojProp $_.FullName 'ModName' } | Where-Object { $_ }) + 'Probe World', 'Probe Server'
$ourRegex = (@("\b$author\.", 'HarmonyLib', 'HarmonyX', 'Chainloader') + ($modNames | ForEach-Object { [regex]::Escape($_) })) -join '|'

# BepInEx folder of one side: core + BepInEx.cfg from your game (read only), MC mods (or not), one probe.
function New-Tree([string]$Dir, [bool]$WithMods, [string]$ProbeStage) {
    $b = Join-Path $Dir 'BepInEx'
    [void](New-Item -ItemType Directory -Force -Path (Join-Path $b 'plugins'), (Join-Path $b 'config'))
    Copy-Item -LiteralPath (Join-Path $game 'BepInEx\core') -Destination (Join-Path $b 'core') -Recurse
    $cfg = Join-Path $game 'BepInEx\config\BepInEx.cfg'
    if (Test-Path -LiteralPath $cfg) { Copy-Item -LiteralPath $cfg -Destination (Join-Path $b 'config') }
    if ($WithMods) { Copy-Item -LiteralPath $modPlugins -Destination (Join-Path $b "plugins\$collection") -Recurse }
    [void](New-Item -ItemType Directory -Force -Path (Join-Path $b "plugins\$collection"))
    $probe = Join-Path $ProbeStage "BepInEx\plugins\$collection"
    Copy-Item -Path (Join-Path $probe '*') -Destination (Join-Path $b "plugins\$collection") -Recurse -Force
    $b
}

function Read-LogText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

function Remove-WithRetry([string]$Path) {
    for ($i = 0; $i -lt 15; $i++) {
        if (-not (Test-Path -LiteralPath $Path)) { return $true }
        try { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop; return $true } catch { Start-Sleep -Seconds 1 }
    }
    -not (Test-Path -LiteralPath $Path)
}

# Start a process with env vars only for it (me set them, start, put them back).
function Start-WithEnv([string]$Exe, [string]$WorkDir, [string[]]$ArgList, [hashtable]$Vars, [switch]$Hidden) {
    $old = @{}
    foreach ($n in $envNames) { $old[$n] = [Environment]::GetEnvironmentVariable($n, 'Process') }
    try {
        foreach ($n in $envNames) { [Environment]::SetEnvironmentVariable($n, $null, 'Process') }
        foreach ($k in $Vars.Keys) { [Environment]::SetEnvironmentVariable($k, [string]$Vars[$k], 'Process') }
        $quoted = $ArgList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
        if ($Hidden) { Start-Process -FilePath $Exe -WorkingDirectory $WorkDir -ArgumentList $quoted -PassThru -WindowStyle Hidden }
        else { Start-Process -FilePath $Exe -WorkingDirectory $WorkDir -ArgumentList $quoted -PassThru }
    }
    finally {
        foreach ($n in $envNames) { [Environment]::SetEnvironmentVariable($n, $old[$n], 'Process') }
    }
}

$selftestRegex = '(?m)^\[(?<lvl>[A-Za-z]+)\s*:\s*(?<src>[^\]]*)\]\s\[selftest\]\s(?<kind>[A-Z]+)\s?(?<rest>[^\r\n]*)'
function Write-SelftestLine([string]$Side, [string]$Kind, [string]$Rest, [string]$Indent = '  ') {
    $color = switch ($Kind) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'SHOT' { 'Cyan' } 'NOTE' { 'Gray' } default { 'White' } }
    Write-Host ("{0}{1} {2,-5} {3}" -f $Indent, $Side, $Kind, $Rest) -ForegroundColor $color
}

# Error blocks (line + stack) of our mods / of others, like Test-Smoke. FAIL lines already count as test failures.
function Get-ErrorBlocks([string]$Text) {
    $ours = New-Object System.Collections.Generic.List[string]
    $other = New-Object System.Collections.Generic.List[string]
    $lines = $Text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch '^\[(Error|Fatal)') { continue }
        if ($lines[$i] -match '\[selftest\] FAIL ') { continue }
        $block = @($lines[$i]); $j = $i + 1
        while ($j -lt $lines.Count -and $lines[$j] -notmatch '^\[(Info|Message|Warning|Error|Fatal|Debug)') { $block += $lines[$j]; $j++ }
        $joined = ($block -join "`n").TrimEnd()
        if ($joined -match $ourRegex) { $ours.Add($joined) } else { $other.Add($joined) }
    }
    [pscustomobject]@{ Ours = $ours; Other = $other }
}

function Invoke-Scenario([string]$Name) {
    Write-Step "Scenario $Name"
    $sd = Join-Path $runDir $Name
    $serverMods = $Name -ne 'vanilla-server'
    $clientMods = @('modded', 'vanilla-server', 'each-off') -contains $Name
    $sTree = New-Tree (Join-Path $sd 'server') $serverMods (Join-Path $stage 'server-probe')
    $cTree = New-Tree (Join-Path $sd 'client') $clientMods (Join-Path $stage 'client-probe')
    if ($Name -eq 'open-server') {
        foreach ($m in $refusing) {
            [IO.File]::WriteAllText((Join-Path $sTree "config\$($m.Guid).cfg"), "[General]`r`n`r`nAllowPlayersWithoutMod = true`r`n")
        }
    }
    Write-Ok ("server: {0}; client: {1}" -f $(if ($serverMods) { 'MC mods + server probe' } else { 'server probe only' }),
        $(if ($clientMods) { 'MC mods + world probe' } else { 'world probe only' }))
    $sLog = Join-Path $sTree 'LogOutput.log'
    $cLog = Join-Path $cTree 'LogOutput.log'
    $result = [pscustomobject]@{ Name = $Name; Pass = $false; Problems = (New-Object System.Collections.Generic.List[string]); Dir = $sd }
    $server = $null; $client = $null
    try {
        $serverArgs = @('-nographics', '-batchmode', '-name', "MCProbe$Port", '-port', [string]$Port, '-world', 'MCProbeMP',
            '-password', $password, '-public', '0', '-savedir', $serverSaves, '-logFile', (Join-Path $sd 'server\unity.log'),
            '--doorstop-target-assembly', (Join-Path $sTree 'core\BepInEx.Preloader.dll'))
        $server = Start-WithEnv $serverExe $cache $serverArgs @{ SteamAppId = '892970'; MC_MP_SERVER_DIR = (Join-Path $sd 'server');
            MC_MP_SCENARIO = $Name; MC_SELFTEST_TIMEOUT = $TestTimeoutSec } -Hidden
        Write-Ok "server pid $($server.Id), waiting for the server probe"
        $deadline = (Get-Date).AddSeconds(300)
        while ($true) {
            Start-Sleep -Seconds 2
            $t = Read-LogText $sLog
            if ($t -match '\[selftest\] NOTE probe\.server: ready') { break }
            if ($server.HasExited) { $result.Problems.Add("the server exited before it was ready (code $($server.ExitCode))"); return $result }
            if ((Get-Date) -gt $deadline) { $result.Problems.Add('the server was not ready within 300 s'); return $result }
        }
        $ready = [regex]::Match($t, '\[selftest\] NOTE probe\.server: (ready[^\r\n]*)').Groups[1].Value
        Write-Ok "server $($ready.Substring(0, [Math]::Min(160, $ready.Length)))"

        $clientVars = @{ MC_INWORLD_DIR = (Join-Path $sd 'client'); MC_SELFTEST_FILTER = ($filters -join ','); MC_SELFTEST_TIMEOUT = $TestTimeoutSec;
            MC_MP_JOIN = "127.0.0.1:$Port"; MC_MP_PASSWORD = $password; MC_MP_SCENARIO = $Name; MC_INWORLD_KEEP = $(if ($KeepRunning) { '1' } else { '' });
            MC_MP_REFUSING = (($refusing | Where-Object { $_.Live } | ForEach-Object { $_.Guid }) -join ',');
            MC_MP_INSTALL_ONLY = (($installOnly | ForEach-Object { $_.Guid }) -join ',') }
        $client = Start-WithEnv (Join-Path $game 'valheim.exe') $game @('-console', '--doorstop-target-assembly', (Join-Path $cTree 'core\BepInEx.Preloader.dll')) $clientVars
        Write-Ok "client pid $($client.Id), waiting for DONE (timeout ${TimeoutSec}s)"
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        $printedC = 0; $printedS = 0; $done = $false
        while ($true) {
            Start-Sleep -Seconds 2
            $exited = $client.HasExited
            $ct = Read-LogText $cLog
            $st = Read-LogText $sLog
            $cl = [regex]::Matches($ct, $selftestRegex)
            for ($i = $printedC; $i -lt $cl.Count; $i++) { Write-SelftestLine 'C' $cl[$i].Groups['kind'].Value $cl[$i].Groups['rest'].Value }
            $printedC = $cl.Count
            $sl = [regex]::Matches($st, $selftestRegex)
            for ($i = $printedS; $i -lt $sl.Count; $i++) { Write-SelftestLine 'S' $sl[$i].Groups['kind'].Value $sl[$i].Groups['rest'].Value }
            $printedS = $sl.Count
            if ($ct -match '\[selftest\] DONE pass=\d+ fail=\d+ tests=\d+') { $done = $true; break }
            if ($exited) { $result.Problems.Add("the client exited before the probe finished (code $($client.ExitCode))"); break }
            if ($server.HasExited) { $result.Problems.Add("the server exited during the tests (code $($server.ExitCode))"); break }
            if ((Get-Date) -gt $deadline) { $result.Problems.Add("no DONE line within ${TimeoutSec}s"); break }
        }
        if ($done -and -not $KeepRunning) {
            if (-not $client.WaitForExit(90000)) { Write-Warn2 'client did not quit within 90 s: closing it'; Stop-Process -Id $client.Id -Force }
            # Client that ended refused (vanilla-client, last each-off test) never reach server probe: nobody asked the
            # server to quit. Me wait only when the server log say it was asked, else close it at once.
            Start-Sleep -Seconds 2
            $asked = (Read-LogText $sLog) -match '\[selftest\] NOTE probe\.server: quit asked'
            if (-not $asked) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
            elseif (-not $server.WaitForExit(60000)) { Write-Warn2 'server did not quit within 60 s: closing it'; Stop-Process -Id $server.Id -Force }
        }
    }
    finally {
        if (-not $KeepRunning) {
            foreach ($p in @($client, $server)) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; [void]$p.WaitForExit(20000) } }
        }
    }

    # Judge from final logs.
    $ct = Read-LogText $cLog
    $st = Read-LogText $sLog
    $doneLine = [regex]::Match($ct, '\[selftest\] DONE pass=(\d+) fail=(\d+) tests=(\d+)')
    if (-not $doneLine.Success) { $result.Problems.Add('the client probe never logged DONE') }
    elseif ([int]$doneLine.Groups[2].Value -gt 0) { $result.Problems.Add("$($doneLine.Groups[2].Value) test(s) failed") }
    elseif ($Name -eq 'modded' -and [int]$doneLine.Groups[3].Value -le 1 -and -not $filters.Count) { Write-Warn2 'no mod registered a multiplayer test (only the probe ran)' }
    foreach ($m in [regex]::Matches($st, $selftestRegex)) {
        if ($m.Groups['kind'].Value -eq 'FAIL') { $result.Problems.Add("server: $($m.Groups['rest'].Value)") }
    }
    if ($Name -eq 'vanilla-client') {
        # First refusing mod kick; the others see the kick and skip. One "Refused" line is enough, none is a failure.
        $who = @($refusing | Where-Object { $st -match ('\[Warning\s*:\s*' + [regex]::Escape($_.Name) + '\] Refused ') } | ForEach-Object { $_.Name })
        if ($who.Count) { Write-Ok "refused by: $($who -join ', ')" } else { $result.Problems.Add('no MC mod logged "Refused" on the server') }
    }
    if ($Name -eq 'open-server') {
        $missing = @($refusing | Where-Object { $st -notmatch ('\[Warning\s*:\s*' + [regex]::Escape($_.Name) + '\][^\r\n]*AllowPlayersWithoutMod is on') } | ForEach-Object { $_.Name })
        if ($missing.Count) { $result.Problems.Add("these mods did not log that they let the player in: $($missing -join ', ')") }
        else { Write-Ok "all $($refusing.Count) refusing mods let the player in (AllowPlayersWithoutMod)" }
        $refused = @($refusing | Where-Object { $st -match ('\[Warning\s*:\s*' + [regex]::Escape($_.Name) + '\] Refused ') } | ForEach-Object { $_.Name })
        if ($refused.Count) { $result.Problems.Add("refused although AllowPlayersWithoutMod = true: $($refused -join ', ')") }
    }
    foreach ($side in @(@{ N = 'client'; T = $ct }, @{ N = 'server'; T = $st })) {
        $e = Get-ErrorBlocks $side.T
        foreach ($b in $e.Ours) { $result.Problems.Add("$($side.N) error: $b") }
        if ($e.Other.Count) { Write-Warn2 "$($e.Other.Count) other error line(s) in the $($side.N) log (vanilla), first: $(($e.Other[0] -split "`n")[0])" }
    }
    if ($doneLine.Success) { Write-Host ("  DONE: {0} passed, {1} failed, {2} test(s)" -f $doneLine.Groups[1].Value, $doneLine.Groups[2].Value, $doneLine.Groups[3].Value) }
    if (-not $KeepRunning) {
        foreach ($s in @((Join-Path $sd 'client\saves'))) { [void](Remove-WithRetry $s) }
    }
    $result.Pass = $result.Problems.Count -eq 0
    $result
}

$results = @()
foreach ($s in $scenarios) { $results += Invoke-Scenario $s }
if (-not $KeepRunning) { [void](Remove-WithRetry $serverSaves) }

Write-Step 'Results'
$pass = $true
foreach ($r in $results) {
    if ($r.Pass) { Write-Ok "$($r.Name): passed" }
    else {
        $pass = $false
        Write-Fail "$($r.Name): FAILED"
        foreach ($p in $r.Problems) { Write-Host "      $p" -ForegroundColor Red }
    }
    Write-Host "      logs: $(Join-Path $r.Dir 'client\BepInEx\LogOutput.log') | $(Join-Path $r.Dir 'server\BepInEx\LogOutput.log')"
}
if ($pass) { Write-Host 'MULTIPLAYER TEST PASSED' -ForegroundColor Green; exit 0 }
Write-Host 'MULTIPLAYER TEST FAILED' -ForegroundColor Red
exit 1
