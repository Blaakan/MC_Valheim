<#
.SYNOPSIS
    Build mods in Release and make upload-ready zips in dist/:
      dist/thunderstore/<Author>-<Package>-<Version>.zip  (manifest.json, README.md, CHANGELOG.md, icon.png, plugins/<Guid>/...)
      dist/nexus/<Package>-<Version>.zip                  (BepInEx/plugins/<Guid>/... for manual/Vortex install)
.PARAMETER Mod
    Only package mods whose project name contains one of these strings. Default: all mods.
.EXAMPLE
    ./tools/Package-Mod.ps1 -Mod Crossbow
#>
[CmdletBinding()]
param([string[]]$Mod)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force
Add-Type -AssemblyName System.Drawing

$root = Get-RepoRoot
$dist = Join-Path $root 'dist'
$projects = Get-ModProjects $Mod
if ($projects.Count -eq 0) { Write-Fail 'No mod projects matched.'; exit 1 }
$failed = 0

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
    elseif ((Get-Content $readme -Raw) -match '(?m)^- TODO') { $problems += 'README.md still has TODO' }
    if (-not (Test-Path $changelog)) { $problems += 'CHANGELOG.md missing' }
    elseif ((Get-Content $changelog -Raw) -notmatch "(?m)^## $([regex]::Escape($props.Version))\b") { $problems += "CHANGELOG.md has no '## $($props.Version)' entry" }
    if ($problems) { $problems | ForEach-Object { Write-Fail $_ }; $failed++; continue }

    $deps = @($props.BepInExPackDependency)
    if ($props.ModDependencies) { $deps += ($props.ModDependencies -split ';' | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) }
    $manifest = [ordered]@{
        name           = $props.ModPackageName
        version_number = $props.Version
        website_url    = [string]$props.ModWebsiteUrl
        description    = $props.ModDescription
        dependencies   = $deps
    }

    # Me stage Thunderstore layout.
    $stage = Join-Path $dist "staging\$($props.ModGuid)"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $pluginDir = Join-Path $stage "plugins\$($props.ModGuid)"
    New-Item -ItemType Directory -Force $pluginDir | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding($false)
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
    if (Test-Path $nexusStage) { Remove-Item $nexusStage -Recurse -Force }
    $nexusPlugin = Join-Path $nexusStage "BepInEx\plugins\$($props.ModGuid)"
    New-Item -ItemType Directory -Force $nexusPlugin | Out-Null
    Copy-Item (Join-Path $pluginDir '*') $nexusPlugin -Recurse
    $nxDir = Join-Path $dist 'nexus'
    New-Item -ItemType Directory -Force $nxDir | Out-Null
    $nxZip = Join-Path $nxDir ("{0}-{1}.zip" -f $props.ModPackageName, $props.Version)
    New-ZipFromDirectory $nexusStage $nxZip
    Write-Ok $nxZip
}
if ($failed) { Write-Fail "$failed mod(s) failed"; exit 1 }
