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
    $prop = { param($n) Get-CsprojProp $p.FullName $n }
    $deployed = $false
    if ($game) { $deployed = Test-Path (Join-Path (Get-ModDeployDir $p.BaseName) "$($p.BaseName).dll") }
    [pscustomobject]@{
        Guid     = $p.BaseName
        Name     = & $prop 'ModName'
        Version  = & $prop 'Version'
        Scope    = & $prop 'ModScope'
        Side     = & $prop 'ModSide'
        MP       = & $prop 'ModMultiplayer'
        Requires = & $prop 'ModRequires'
        Deployed = $deployed
        Path     = $p.DirectoryName.Substring((Get-RepoRoot).Length + 1)
    }
}
