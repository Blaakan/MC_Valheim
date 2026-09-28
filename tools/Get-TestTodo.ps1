<#
.SYNOPSIS
    Print the in-game test to-do list: every pending ([ ]) or failed ([!]) item from all TESTING.md files
    (each mod folder, src/Shared/TESTING.md for framework features, docs/testing/*.md).
.PARAMETER All
    Also show passed/skipped items.
.PARAMETER Mod
    Only mods whose folder/path contains one of these strings.
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

$totalPending = 0; $totalFailed = 0
foreach ($f in $files) {
    $lines = Get-Content $f.FullName -Encoding UTF8
    $title = ($lines | Where-Object { $_ -match '^# ' } | Select-Object -First 1) -replace '^# ', ''
    $section = ''
    $out = New-Object System.Collections.Generic.List[string]
    $lastSection = $null
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^##+ (.+)') { $section = $Matches[1]; continue }
        if ($line -match '^\s*- \[(.)\] (.+)') {
            $mark = $Matches[1]; $text = $Matches[2]
            # Me glue wrapped lines (indented, not new item).
            while ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\s{2,}\S' -and $lines[$i + 1] -notmatch '^\s*- \[') { $i++; $text += ' ' + $lines[$i].Trim() }
            $show = $All -or $mark -eq ' ' -or $mark -eq '!'
            if ($mark -eq ' ') { $totalPending++ }
            if ($mark -eq '!') { $totalFailed++ }
            if (-not $show) { continue }
            if ($section -ne $lastSection) { $out.Add("  [$section]"); $lastSection = $section }
            $label = @{ ' ' = 'TODO'; '!' = 'FAIL'; 'x' = 'PASS'; '-' = 'SKIP' }[$mark]
            if (-not $label) { $label = $mark }
            $out.Add("    $label  $text")
        }
    }
    if ($out.Count) {
        Write-Host ''
        Write-Host "$title  ($($f.FullName.Substring($root.Length + 1)))" -ForegroundColor Cyan
        $out | ForEach-Object {
            $color = 'Gray'
            if ($_ -match '^\s+FAIL') { $color = 'Red' } elseif ($_ -match '^\s+TODO') { $color = 'White' } elseif ($_ -match '^\s+PASS') { $color = 'Green' }
            Write-Host $_ -ForegroundColor $color
        }
    }
}
Write-Host ''
Write-Host "$totalPending to test, $totalFailed failed." -ForegroundColor Yellow
