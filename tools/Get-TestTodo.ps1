<#
.SYNOPSIS
    Print the in-game test to-do list: every pending ([ ]) or failed ([!]) item from all TESTING.md files
    (each mod folder, src/Shared/TESTING.md for framework features, docs/testing/*.md).
.PARAMETER All
    Also show passed/skipped items.
.PARAMETER Mod
    Only files whose path contains one of these strings.
.EXAMPLE
    ./tools/Get-TestTodo.ps1
    ./tools/Get-TestTodo.ps1 -Mod Crossbow -All
#>
[CmdletBinding()]
param([switch]$All, [string[]]$Mod)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$root = Get-RepoRoot
$files = @()
$files += Get-ChildItem (Join-Path $root 'src') -Recurse -Filter 'TESTING.md' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$docsTesting = Join-Path $root 'docs\testing'
if (Test-Path $docsTesting) { $files += Get-ChildItem $docsTesting -Filter '*.md' }
if ($Mod) { $files = $files | Where-Object { $path = $_.FullName; @($Mod | Where-Object { $path -like "*$_*" }).Count -gt 0 } }

$labels = @{ ' ' = 'TODO'; '!' = 'FAIL'; 'x' = 'PASS'; '-' = 'SKIP' }
$totalPending = 0; $totalFailed = 0
foreach ($f in $files) {
    $title = (Get-Content $f.FullName -Encoding UTF8 | Where-Object { $_ -match '^# ' } | Select-Object -First 1) -replace '^# ', ''
    $items = @(Get-TestItems $f.FullName)
    $totalPending += @($items | Where-Object { $_.Mark -eq ' ' }).Count
    $totalFailed += @($items | Where-Object { $_.Mark -eq '!' }).Count
    $shown = @($items | Where-Object { $All -or $_.Mark -eq ' ' -or $_.Mark -eq '!' })
    if ($shown.Count -eq 0) { continue }

    Write-Host ''
    Write-Host "$title  ($($f.FullName.Substring($root.Length + 1)))" -ForegroundColor Cyan
    $lastSection = $null
    foreach ($it in $shown) {
        if ($it.Section -ne $lastSection) { Write-Host "  [$($it.Section)]"; $lastSection = $it.Section }
        $label = $labels[$it.Mark]
        if (-not $label) { $label = $it.Mark }
        $color = @{ TODO = 'White'; FAIL = 'Red'; PASS = 'Green'; SKIP = 'Gray' }[$label]
        if (-not $color) { $color = 'Gray' }
        Write-Host "    $label  $($it.Text)" -ForegroundColor $color
    }
}
Write-Host ''
Write-Host "$totalPending to test, $totalFailed failed." -ForegroundColor Yellow
