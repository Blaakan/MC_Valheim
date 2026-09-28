<#
.SYNOPSIS
    Build mods in Release and make upload-ready zips in dist/:
      dist/thunderstore/<Author>-<Package>-<Version>.zip  (manifest.json, README.md, CHANGELOG.md, icon.png, plugins/<Guid>/...)
      dist/nexus/<Package>-<Version>.zip                  (BepInEx/plugins/<Guid>/... for manual/Vortex install)
    With -Pack, also the "whole collection" bundles:
      dist/nexus/<Author>-AllMods-<PackVersion>.zip        (every mod, one download, BepInEx/plugins/<Guid>/...)
      dist/thunderstore/<Author>-ModPack-<PackVersion>.zip (Thunderstore modpack: depends on every mod)
    Players who install everything pick features in-game (Esc > MC Mods) or in each mod's config file.
.PARAMETER Mod
    Only package mods whose project name contains one of these strings. Default: all mods.
.PARAMETER Pack
    Also build the all-mods bundles.
.EXAMPLE
    ./tools/Package-Mod.ps1 -Mod Crossbow
    ./tools/Package-Mod.ps1 -Pack -PackVersion 0.1.0
#>
[CmdletBinding()]
param([string[]]$Mod, [switch]$Pack, [string]$PackVersion = '0.1.0')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force
Add-Type -AssemblyName System.Drawing

$root = Get-RepoRoot
$dist = Join-Path $root 'dist'
$projects = Get-ModProjects $Mod
if ($projects.Count -eq 0) { Write-Fail 'No mod projects matched.'; exit 1 }
Assert-ModRequiresAcyclic
$failed = 0
$packaged = New-Object System.Collections.Generic.List[object]
$utf8 = New-Object System.Text.UTF8Encoding($false)

# Me wipe a staging folder, but only inside dist/ (safety).
function Reset-StageDir([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith([IO.Path]::GetFullPath($dist), [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clear $full (outside dist/)" }
    if (Test-Path $full) { [IO.Directory]::Delete($full, $true) }
    [void][IO.Directory]::CreateDirectory($full)
}

# Me map guid -> Thunderstore dependency string for mods in this repo (for ModRequires).
function Get-RepoDependencyString([string]$Guid) {
    $proj = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter "$Guid.csproj" | Select-Object -First 1
    if (-not $proj) { return $null }
    $dp = Get-ModProperties $proj.FullName 'Release'
    "{0}-{1}-{2}" -f $dp.ModAuthor, $dp.ModPackageName, $dp.Version
}

foreach ($p in $projects) {
    Write-Step "Package $($p.BaseName)"
    & dotnet build $p.FullName -c Release -nologo -v q -p:DeployToGame=false
    if ($LASTEXITCODE -ne 0) { Write-Fail 'build failed'; $failed++; continue }
    $props = Get-ModProperties $p.FullName 'Release'
    $dir = $p.DirectoryName

    # Me check package bits before zip.
    $problems = @()
    $icon = Join-Path $dir 'icon.png'
    if (-not (Test-Path $icon)) { $problems += 'icon.png missing' }
    else {
        $img = [System.Drawing.Image]::FromFile($icon)
        try { if ($img.Width -ne 256 -or $img.Height -ne 256) { $problems += "icon.png is $($img.Width)x$($img.Height), must be 256x256" } } finally { $img.Dispose() }
    }
    $readme = Join-Path $dir 'README.md'
    $changelog = Join-Path $dir 'CHANGELOG.md'
    if (-not (Test-Path $readme)) { $problems += 'README.md missing' }
    elseif ((Get-Content $readme -Raw -Encoding UTF8) -match '(?m)^- TODO') { $problems += 'README.md still has TODO' }
    if (-not (Test-Path $changelog)) { $problems += 'CHANGELOG.md missing' }
    elseif ((Get-Content $changelog -Raw -Encoding UTF8) -notmatch "(?m)^## $([regex]::Escape($props.Version))\b") { $problems += "CHANGELOG.md has no '## $($props.Version)' entry" }

    $deps = @($props.BepInExPackDependency)
    if ($props.ModDependencies) { $deps += ($props.ModDependencies -split ';' | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) }
    foreach ($req in ($props.ModRequires -split ';' | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })) {
        $dep = Get-RepoDependencyString $req
        if ($dep) { $deps += $dep } else { $problems += "ModRequires '$req' is not a mod in this repo; put its Thunderstore string in <ModDependencies> instead." }
    }
    if ($problems) { $problems | ForEach-Object { Write-Fail $_ }; $failed++; continue }

    $manifest = [ordered]@{
        name           = $props.ModPackageName
        version_number = $props.Version
        website_url    = [string]$props.ModWebsiteUrl
        description    = $props.ModDescription
        dependencies   = $deps
    }

    # Me stage Thunderstore layout.
    $stage = Join-Path $dist "staging\$($props.ModGuid)"
    Reset-StageDir $stage
    $pluginDir = Join-Path $stage "plugins\$($props.ModGuid)"
    New-Item -ItemType Directory -Force $pluginDir | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), ($manifest | ConvertTo-Json -Depth 3), $utf8)
    Copy-Item $readme, $changelog, $icon $stage
    Copy-Item $props.TargetPath $pluginDir
    $contentDir = Join-Path $dir 'Content'
    if (Test-Path $contentDir) { Copy-Item (Join-Path $contentDir '*') $pluginDir -Recurse }

    $tsDir = Join-Path $dist 'thunderstore'
    New-Item -ItemType Directory -Force $tsDir | Out-Null
    $tsZip = Join-Path $tsDir ("{0}-{1}-{2}.zip" -f $props.ModAuthor, $props.ModPackageName, $props.Version)
    New-ZipFromDirectory $stage $tsZip
    Write-Ok $tsZip

    # Me stage Nexus layout: BepInEx/plugins/<Guid>/...
    $nexusStage = Join-Path $dist "staging\nexus-$($props.ModGuid)"
    Reset-StageDir $nexusStage
    $nexusPlugin = Join-Path $nexusStage "BepInEx\plugins\$($props.ModGuid)"
    New-Item -ItemType Directory -Force $nexusPlugin | Out-Null
    Copy-Item (Join-Path $pluginDir '*') $nexusPlugin -Recurse
    $nxDir = Join-Path $dist 'nexus'
    New-Item -ItemType Directory -Force $nxDir | Out-Null
    $nxZip = Join-Path $nxDir ("{0}-{1}.zip" -f $props.ModPackageName, $props.Version)
    New-ZipFromDirectory $nexusStage $nxZip
    Write-Ok $nxZip

    $packaged.Add([pscustomobject]@{
        Props      = $props
        PluginDir  = $pluginDir
        Dependency = ("{0}-{1}-{2}" -f $props.ModAuthor, $props.ModPackageName, $props.Version)
    })
}
if ($failed) { Write-Fail "$failed mod(s) failed"; exit 1 }

if ($Pack) {
    Import-Module (Join-Path $PSScriptRoot 'lib\Icon.psm1') -Force
    $author = Get-ModAuthor
    $rows = $packaged | Sort-Object { $_.Props.ModCategory }, { $_.Props.ModName } | ForEach-Object {
        "| $($_.Props.ModName) | $($_.Props.ModCategory) | $($_.Props.ModScope) | $($_.Props.ModSide) | $($_.Props.Version) |"
    }
    $packReadme = @(
        ('# MC Mods ' + [char]0x2014 + ' full collection'),
        '',
        'Every MC mod in one install. Each feature can be turned on or off on its own, in game (open the menu with Esc',
        'and click **MC Mods**) or in its config file (`BepInEx/config/MC.<Category>.<Feature>.cfg`, also editable in',
        'r2modman / Thunderstore Mod Manager). Changes apply immediately. If a feature needs another one that is turned',
        'off, it stays inactive and says why, and comes back by itself when you turn the other one back on.',
        '',
        '| Mod | Category | Scope | Who needs it | Version |',
        '|---|---|---|---|---|'
    ) + $rows
    $packReadme = $packReadme -join "`n"

    Write-Step 'Pack: Nexus all-in-one'
    $allStage = Join-Path $dist 'staging\all-mods'
    Reset-StageDir $allStage
    foreach ($m in $packaged) {
        $target = Join-Path $allStage "BepInEx\plugins\$($m.Props.ModGuid)"
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item (Join-Path $m.PluginDir '*') $target -Recurse
    }
    [IO.File]::WriteAllText((Join-Path $allStage 'README.md'), $packReadme, $utf8)
    $allZip = Join-Path $dist "nexus\$author-AllMods-$PackVersion.zip"
    New-ZipFromDirectory $allStage $allZip
    Write-Ok $allZip

    Write-Step 'Pack: Thunderstore modpack'
    $packStage = Join-Path $dist 'staging\modpack'
    Reset-StageDir $packStage
    $packManifest = [ordered]@{
        name           = 'ModPack'
        version_number = $PackVersion
        website_url    = ''
        description    = 'Every MC mod in one install. Turn each feature on or off in game (Esc > MC Mods) or in its config file.'
        dependencies   = @($packaged | ForEach-Object { $_.Dependency })
    }
    [IO.File]::WriteAllText((Join-Path $packStage 'manifest.json'), ($packManifest | ConvertTo-Json -Depth 3), $utf8)
    [IO.File]::WriteAllText((Join-Path $packStage 'README.md'), $packReadme, $utf8)
    New-ModIcon -Path (Join-Path $packStage 'icon.png') -Category 'Core' -FeatureId 'Mods.Collection'
    $packZip = Join-Path $dist "thunderstore\$author-ModPack-$PackVersion.zip"
    New-ZipFromDirectory $packStage $packZip
    Write-Ok $packZip
}
