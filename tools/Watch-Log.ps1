<#
.SYNOPSIS
    Follow BepInEx/LogOutput.log with colors. Errors red, warnings yellow, our mods cyan.
.PARAMETER Filter
    Only show lines matching this regex (e.g. 'Crossbow|Error').
.PARAMETER Mine
    Only show lines from our mods (sources/lines containing the mod author prefix or mod names) plus errors.
.PARAMETER Tail
    Lines of history to show first (default 40).
.EXAMPLE
    ./tools/Watch-Log.ps1 -Mine
#>
[CmdletBinding()]
param([string]$Filter, [switch]$Mine, [int]$Tail = 40)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$log = Get-BepInExLogPath
if (-not (Test-Path $log)) { Write-Fail "No log yet at $log (launch the game once)."; exit 1 }

$author = Get-ModAuthor
$names = @()
if ($Mine) {
    # Me collect plugin display names; BepInEx log source = plugin name.
    $names = Get-ModProjects | ForEach-Object {
        $n = Get-CsprojProp $_.FullName 'ModName'
        if ($n) { [regex]::Escape($n) }
    }
}
$mineRegex = (@("\b$author\.", [regex]::Escape('[MC:ready]')) + $names) -join '|'

Get-Content $log -Tail $Tail -Wait | ForEach-Object {
    $line = $_
    if ($Filter -and $line -notmatch $Filter) { return }
    $isError = $line -match '^\[(Error|Fatal)' -or $line -match 'Exception'
    $isMine = $line -match $mineRegex
    if ($Mine -and -not ($isMine -or $isError)) { return }
    $color = 'Gray'
    if ($line -match '^\[Warning') { $color = 'Yellow' }
    if ($isMine) { $color = 'Cyan' }
    if ($isError) { $color = 'Red' }
    Write-Host $line -ForegroundColor $color
}
