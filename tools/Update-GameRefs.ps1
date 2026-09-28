<#
.SYNOPSIS
    Decompile the game's assemblies into .ref/decompiled (git-ignored reference source) and track
    game updates. Each game version is committed to a local, nested git repo in .ref/decompiled, so
    after a Valheim patch you can see exactly what changed, and which classes our mods patch were touched.
.PARAMETER Force
    Re-decompile even if the game assemblies did not change.
.EXAMPLE
    ./tools/Update-GameRefs.ps1            # after a Valheim update
    git -C .ref/decompiled log --oneline   # game versions seen
    git -C .ref/decompiled diff HEAD~1 -- assembly_valheim/Player.cs
#>
[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$root = Get-RepoRoot
$game = Get-ValheimDir
$managed = Join-Path $game 'valheim_Data\Managed'
$refDir = Join-Path $root '.ref'
$outDir = Join-Path $refDir 'decompiled'
$stampFile = Join-Path $refDir 'game-version.json'

# Me decompile these. assembly_valheim = main game code.
$assemblies = 'assembly_valheim', 'assembly_utils', 'assembly_guiutils', 'gui_framework',
              'SoftReferenceableAssets', 'Splatform', 'assembly_postprocessing'

$hashes = [ordered]@{}
foreach ($a in $assemblies) { $hashes[$a] = (Get-FileHash (Join-Path $managed "$a.dll") -Algorithm SHA256).Hash.Substring(0, 16) }

$old = $null
if (Test-Path $stampFile) { $old = Get-Content $stampFile -Raw | ConvertFrom-Json }
$changed = $true
if ($old -and -not $Force) {
    $changed = $false
    foreach ($a in $assemblies) { if (-not $old.hashes.PSObject.Properties[$a] -or $old.hashes.$a -ne $hashes[$a]) { $changed = $true } }
    # Me also redo if folder got deleted.
    if (-not (Test-Path (Join-Path $outDir 'assembly_valheim'))) { $changed = $true }
}
if (-not $changed) {
    Write-Ok "Game refs up to date (Valheim $($old.version))."
    return
}

if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) { throw 'ilspycmd missing: dotnet tool install -g ilspycmd' }

Write-Step "Decompile game assemblies -> $outDir"
New-Item -ItemType Directory -Force $outDir | Out-Null
foreach ($a in $assemblies) {
    $target = Join-Path $outDir $a
    # Me wipe old output but keep nested .git at top level.
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    & ilspycmd -p -o $target -r $managed --disable-updatecheck (Join-Path $managed "$a.dll") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ilspycmd failed on $a" }
    # Me drop csproj noise; we only read .cs.
    Get-ChildItem $target -Filter '*.csproj' | Remove-Item -Force
    Write-Ok ("{0}: {1} files" -f $a, (Get-ChildItem $target -Recurse -Filter *.cs).Count)
}

# Me read game version from Version.CurrentVersion.
$version = 'unknown'
$versionFile = Join-Path $outDir 'assembly_valheim\Version.cs'
if ((Test-Path $versionFile) -and ((Get-Content $versionFile -Raw) -match 'CurrentVersion\s*\{\s*get;\s*\}\s*=\s*new GameVersion\((\d+),\s*(\d+),\s*(\d+)\)')) {
    $version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
}
$unity = (Get-Item (Join-Path $game 'UnityPlayer.dll')).VersionInfo.ProductVersion
[ordered]@{ version = $version; unity = $unity; hashes = $hashes } | ConvertTo-Json | Set-Content $stampFile -Encoding UTF8
Write-Ok "Valheim $version (Unity $unity)"

# Me commit snapshot into nested git so game updates can be diffed.
Push-Location $outDir
try {
    if (-not (Test-Path '.git')) { & git init -q -b main; & git config core.autocrlf false }
    & git add -A 2>$null
    & git -c user.name=GameRefs -c user.email=gamerefs@localhost commit -q -m "Valheim $version (Unity $unity)" 2>$null
    $commits = @(& git rev-list --count HEAD 2>$null)
    if ($commits -and [int]$commits[0] -gt 1) {
        Write-Step 'Changed game files since previous snapshot'
        $changedFiles = @(& git diff --name-only HEAD~1 HEAD)
        Write-Host ("  {0} files changed" -f $changedFiles.Count)
        # Me find classes our mods patch: [HarmonyPatch(typeof(X) ...)] and typeof(X) inside patch files.
        $patched = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
            Select-String -Pattern 'HarmonyPatch\(typeof\(([\w\.]+)\)' -AllMatches |
            ForEach-Object { $_.Matches } | ForEach-Object { ($_.Groups[1].Value -split '\.')[0] } | Sort-Object -Unique
        $hits = $changedFiles | Where-Object { $patched -contains [IO.Path]::GetFileNameWithoutExtension($_) }
        if ($hits) {
            Write-Warn2 'Classes patched by our mods changed. Review these diffs:'
            $hits | ForEach-Object { Write-Host "      git -C .ref/decompiled diff HEAD~1 -- $_" }
        } else {
            Write-Ok 'No class patched by our mods changed.'
        }
    }
} finally { Pop-Location }
