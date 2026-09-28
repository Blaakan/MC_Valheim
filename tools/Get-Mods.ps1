<#
.SYNOPSIS
    List all mods in the repo with their metadata and whether they are deployed to the game.
.EXAMPLE
    ./tools/Get-Mods.ps1 | Format-Table
#>
[CmdletBinding()]
param([string[]]$Mod)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$game = Find-ValheimDir
foreach ($p in Get-ModProjects $Mod) {
    # Me read csproj xml direct. Fast, no msbuild.
    $x = [xml](Get-Content $p.FullName -Raw)
    $pg = $x.Project.PropertyGroup | Select-Object -First 1
    $deployed = $false
    if ($game) { $deployed = Test-Path (Join-Path $game "BepInEx\plugins\$($p.BaseName)\$($p.BaseName).dll") }
    [pscustomobject]@{
        Guid     = $p.BaseName
        Name     = $pg.ModName
        Version  = $pg.Version
        Scope    = $pg.ModScope
        Side     = $pg.ModSide
        Deployed = $deployed
        Path     = $p.DirectoryName.Substring((Get-RepoRoot).Length + 1)
    }
}
