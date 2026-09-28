<#
.SYNOPSIS
    One-time (or after-move) workspace setup: find Valheim, write Local.props, check toolchain,
    decompile game reference source, restore packages.
.PARAMETER ValheimDir
    Valheim install folder. Auto-detected from Steam if omitted.
.PARAMETER DevBepInExConfig
    Also tweak the game's BepInEx.cfg for development: show the BepInEx console window and
    write Debug-level messages to LogOutput.log.
.EXAMPLE
    ./tools/Setup.ps1
    ./tools/Setup.ps1 -ValheimDir 'E:\SteamLibrary\steamapps\common\Valheim' -DevBepInExConfig
#>
[CmdletBinding()]
param(
    [string]$ValheimDir,
    [switch]$DevBepInExConfig,
    [switch]$SkipGameRefs
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force
$root = Get-RepoRoot

Write-Step 'Find Valheim'
if (-not $ValheimDir) { $ValheimDir = Find-ValheimDir }
if (-not $ValheimDir -or -not (Test-Path (Join-Path $ValheimDir 'valheim.exe'))) {
    Write-Fail 'Valheim not found. Pass -ValheimDir <path>.'
    exit 1
}
$ValheimDir = (Resolve-Path $ValheimDir).Path.TrimEnd('\')
Write-Ok $ValheimDir

# Me write Local.props. Git ignore it. MSBuild import it first.
$localProps = @"
<Project>
  <!-- Me made by tools/Setup.ps1. Machine-only. Git no see me. -->
  <PropertyGroup>
    <ValheimDir>$ValheimDir</ValheimDir>
  </PropertyGroup>
</Project>
"@
Set-Content -Path (Join-Path $root 'Local.props') -Value $localProps -Encoding UTF8
Write-Ok 'Local.props written'

Write-Step 'Check BepInEx'
$bepinex = Join-Path $ValheimDir 'BepInEx\core\BepInEx.dll'
if (Test-Path $bepinex) {
    Write-Ok ("BepInEx " + (Get-Item $bepinex).VersionInfo.FileVersion)
} else {
    Write-Fail 'BepInEx is not installed in the game folder. Install BepInExPack_Valheim (Thunderstore: denikson/BepInExPack_Valheim) first.'
    exit 1
}
New-Item -ItemType Directory -Force (Join-Path $ValheimDir 'BepInEx\plugins') | Out-Null

Write-Step 'Check toolchain'
$sdks = & dotnet --list-sdks 2>$null
$okSdk = $sdks | Where-Object { $_ -match '^(\d+)\.(\d+)\.(\d+)' -and ([int]$Matches[1] -gt 9 -or ([int]$Matches[1] -eq 9 -and [int]$Matches[3] -ge 300)) }
if ($okSdk) { Write-Ok ".NET SDK $(($okSdk | Select-Object -Last 1) -replace ' .*$','')" } else { Write-Fail '.NET SDK 9.0.300+ needed (slnx support). Install from https://dotnet.microsoft.com/download'; exit 1 }
if (Get-Command ilspycmd -ErrorAction SilentlyContinue) { Write-Ok 'ilspycmd' } else { Write-Warn2 'ilspycmd missing: dotnet tool install -g ilspycmd  (needed for tools/Update-GameRefs.ps1)' }
if (Get-Command git -ErrorAction SilentlyContinue) { Write-Ok 'git' } else { Write-Warn2 'git missing' }

if ($DevBepInExConfig) {
    Write-Step 'Tweak BepInEx.cfg for development'
    $cfg = Join-Path $ValheimDir 'BepInEx\config\BepInEx.cfg'
    if (Test-Path $cfg) {
        $text = Get-Content $cfg -Raw
        $backup = "$cfg.bak"
        if (-not (Test-Path $backup)) { Copy-Item $cfg $backup; Write-Ok "backup: $backup" }
        # Me turn on console window, and let Debug lines reach disk log.
        $text = [regex]::Replace($text, '(?ms)(\[Logging\.Console\].*?^Enabled\s*=\s*)false', '${1}true')
        $text = [regex]::Replace($text, '(?ms)(\[Logging\.Disk\].*?^LogLevels\s*=\s*)([^\r\n]+)', { param($m) if ($m.Groups[2].Value -match 'Debug|All') { $m.Value } else { $m.Groups[1].Value + $m.Groups[2].Value.TrimEnd() + ', Debug' } })
        Set-Content -Path $cfg -Value $text -NoNewline -Encoding UTF8
        Write-Ok 'console on, disk log includes Debug'
    } else {
        Write-Warn2 'BepInEx.cfg not created yet: launch the game once, then re-run with -DevBepInExConfig.'
    }
}

if (-not $SkipGameRefs) {
    Write-Step 'Game reference source'
    & (Join-Path $PSScriptRoot 'Update-GameRefs.ps1')
}

Write-Step 'Restore packages'
& dotnet restore (Join-Path $root 'ValheimMods.slnx') --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Fail 'dotnet restore failed'; exit 1 }
Write-Ok 'restored'

Write-Host ''
Write-Host 'Setup done. Next: dotnet build ValheimMods.slnx   (Debug builds deploy into the game)' -ForegroundColor Green
