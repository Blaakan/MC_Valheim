<#
.SYNOPSIS
    Regenerate docs/backlog.md from the idea sheet (Google Sheets CSV export) merged with the research data in
    docs/research/idea-research.json (feasibility, who needs it, hooks, existing mods). Ideas added to the sheet
    after the research show up as "not researched yet". Mods in src/ link themselves to an idea with <ModIdea>
    (a mod that implements several ideas lists them ';'-separated: <ModIdea>Idea A;Idea B</ModIdea>).
.DESCRIPTION
    The sheet's Status column (Idea, Implemented, Cancelled: the 'data' tab of the sheet) is shown for ideas no mod
    implements yet; a mod in src/ overrides it with a link to the mod (Status 'Implemented' in the sheet). The script
    also lists what is out of date: sheet rows that differ from the research snapshot (new, changed or removed
    ideas: run a research pass and update idea-research.json), sheet Status cells that do not match the repo, and
    EXISTS ALREADY cells the research clearly contradicts (coverage full or none), unless the user kept that value
    after seeing the flag (research field existsConfirmed). The user maintains the sheet: these lists are what to give
    them. A changed Status needs no research: the online run copies it into the snapshot
    (Windows PowerShell 5.1 only, the writer that made the file), so -Offline shows it too.
.PARAMETER Offline
    Use the ideas stored in idea-research.json instead of downloading the sheet.
.EXAMPLE
    ./tools/Update-Backlog.ps1
#>
[CmdletBinding()]
param([switch]$Offline)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$root = Get-RepoRoot
$sheetCsv = 'https://docs.google.com/spreadsheets/d/1nd_oWjYyphCcjt5F0SrstpkMK_UzDdKwj0U1UC3FHG0/export?format=csv'
$researchPath = Join-Path $root 'docs\research\idea-research.json'
$research = [IO.File]::ReadAllText($researchPath) | ConvertFrom-Json

# Me keep script ASCII (PS 5.1 read BOM-less script as ANSI). Fancy chars made at runtime.
$dot = [char]0x00B7; $arrow = [char]0x2192

function Norm([string]$x) { ($x -replace '[^A-Za-z0-9]', '').ToLower() }
function Cell([string]$x) { if ($null -eq $x) { return '' } ($x -replace '\|', '\|' -replace "`r?`n", ' ').Trim() }

# Me get sheet rows (category, name, description, scope, exists, status, link).
# Sheet have no Link column any more: link come from research snapshot then.
$rows = @()
if ($Offline) {
    $rows = $research.ideas | ForEach-Object { [pscustomobject]@{ c = $_.category; n = $_.name; d = $_.description; s = $_.scope; e = $_.existsPerUser; st = "$($_.status)".Trim(); l = $_.userLink } }
} else {
    Write-Step 'Download idea sheet'
    $csv = (Invoke-WebRequest -Uri $sheetCsv -UseBasicParsing).Content
    $rows = $csv | ConvertFrom-Csv | Where-Object { $_.NAME } | ForEach-Object {
        [pscustomobject]@{ c = "$($_.CATEGORY)".Trim(); n = "$($_.NAME)".Trim(); d = "$($_.DESCRIPTION)".Trim(); s = "$($_.SCOPE)".Trim(); e = "$($_.'EXISTS ALREADY')".Trim(); st = "$($_.Status)".Trim(); l = "$($_.Link)".Trim() }
    }
    Write-Ok "$($rows.Count) ideas"
}
foreach ($r in $rows) { if (-not $r.st) { $r.st = 'Idea' } }

$byName = @{}
foreach ($i in $research.ideas) { $byName[(Norm $i.name)] = $i }
foreach ($r in $rows) {
    $res = $byName[(Norm $r.n)]
    if (-not $r.l -and $res) { $r.l = $res.userLink }
}

# Me find mods that implement an idea: <ModIdea> in csproj. Tag nexus/<GUID>/v<ver> = that version released.
# PS 5.1 + Stop make git stderr (no repo, no git) a crash: me ask softly, no tag = nothing released.
$tags = @()
$eap = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $tags = @(& git -C $root tag -l 'nexus/*' 2>$null | Where-Object { $_ -is [string] })
    }
} catch {
    $tags = @()
} finally {
    $ErrorActionPreference = $eap
}
$modsByIdea = @{}
foreach ($p in Get-ModProjects) {
    $x = [xml](Get-Content $p.FullName -Raw)
    $pg = $x.Project.PropertyGroup | Select-Object -First 1
    $ideaText = $x.Project.PropertyGroup | ForEach-Object { $_.ModIdea } | Where-Object { $_ } | Select-Object -First 1
    # Me take one or more ideas: <ModIdea>A;B</ModIdea> = one mod build two sheet ideas. Each idea get link + Status fix.
    $ideas = @(Split-ModIdea $ideaText)
    if ($ideas.Count -gt 0) {
        $rel = ($p.DirectoryName.Substring($root.Length + 1)) -replace '\\', '/'
        $guid = $p.BaseName
        $released = @($tags | Where-Object { $_ -like "nexus/$guid/v*" }).Count -gt 0
        $current = $tags -contains "nexus/$guid/v$($pg.Version)"
        $text = if ($current) { 'released' } elseif ($released) { 'new version in development' } else { 'in development' }
        foreach ($idea in $ideas) {
            $modsByIdea[(Norm $idea)] = [pscustomobject]@{
                Link = "[$($pg.ModName) $($pg.Version)](../$rel) ($text)"
                SheetStatus = 'Implemented'
                Idea = $idea
            }
        }
    }
}

$sideText = @{ 'client-only' = 'Client'; 'host/server-only' = 'Server'; 'everyone' = 'Both'; 'depends' = 'Depends' }
$categories = 'Combat', 'Exploration', 'Farming', 'Cooking', 'Building', 'Crafting', 'UX'
$feasOrder = @{ trivial = 0; easy = 1; medium = 2; hard = 3; 'very-hard' = 4; 'not researched yet' = 9 }

$items = foreach ($r in $rows) {
    $res = $byName[(Norm $r.n)]
    [pscustomobject]@{
        Row = $r; Res = $res
        Feas = if ($res) { $res.feasibility } else { 'not researched yet' }
        Side = if ($res -and $sideText.ContainsKey($res.side)) { $sideText[$res.side] } elseif ($res) { $res.side } else { '?' }
        Assets = if ($res) { if ($res.needsAssets) { 'yes' } else { 'no' } } else { '?' }
        Coverage = if ($res -and $res.coverage) { $res.coverage } else { '?' }
        Status = if ($modsByIdea.ContainsKey((Norm $r.n))) { $modsByIdea[(Norm $r.n)].Link } else { $r.st.ToLower() }
        Cancelled = $r.st -eq 'Cancelled'
    }
}

# Me tell what is out of date. Sheet row vs research snapshot (research pass needed), sheet Status vs repo,
# sheet EXISTS ALREADY vs research coverage. User keep sheet: me only list cells for user.
# Compare exact (-cne): snapshot is verbatim copy, case and spacing count too.
$drift = @()
$statusSync = @()
foreach ($r in $rows) {
    $res = $byName[(Norm $r.n)]
    if (-not $res) { $drift += "not researched yet: $($r.n)"; continue }
    $changed = @()
    if ($r.n -cne "$($res.name)".Trim()) { $changed += 'name' }
    if ($r.c -cne "$($res.category)".Trim()) { $changed += 'category' }
    if ($r.d -cne "$($res.description)".Trim()) { $changed += 'description' }
    if ($r.s -cne "$($res.scope)".Trim()) { $changed += 'scope' }
    if ($r.e -cne "$($res.existsPerUser)".Trim()) { $changed += 'exists already' }
    if ($changed) { $drift += "sheet row differs from the research snapshot ($($changed -join ', ')): $($r.n)" }
    # Status change = no research needed. Me copy it into snapshot (for -Offline) below.
    if (-not $Offline -and $r.st -cne "$($res.status)".Trim()) { $statusSync += [pscustomobject]@{ Res = $res; Status = $r.st } }
}
$sheetNames = @{}
foreach ($r in $rows) { $sheetNames[(Norm $r.n)] = $true }
foreach ($i in $research.ideas) { if (-not $sheetNames[(Norm $i.name)]) { $drift += "in the research but not in the sheet: $($i.name)" } }
$statusFix = @()
foreach ($r in $rows) {
    $mod = $modsByIdea[(Norm $r.n)]
    if ($mod -and $r.st -ne $mod.SheetStatus) { $statusFix += "$($r.n): '$($r.st)' -> '$($mod.SheetStatus)'" }
    if (-not $mod -and $r.st -eq 'Implemented') { $statusFix += "$($r.n): 'Implemented' but no mod in src/ has <ModIdea>$($r.n)</ModIdea> (typo in a ModIdea, or the cell is wrong)" }
}
# Me compare EXISTS ALREADY with research coverage. Only clear contradictions (partial = user judgment), and only for
# ideas still to decide (Status Idea, no mod yet): for built or cancelled ones the cell change nothing.
$existsCheck = @()
foreach ($r in $rows) {
    $res = $byName[(Norm $r.n)]
    if (-not $res -or $r.st -ne 'Idea' -or $modsByIdea.ContainsKey((Norm $r.n))) { continue }
    # User saw flag and kept cell: research entry have existsConfirmed = that value. Me stay quiet while cell same.
    if ("$($res.existsConfirmed)" -and $r.e -ceq "$($res.existsConfirmed)") { continue }
    $cov = "$($res.coverage)".Trim()
    if ($r.e -ne 'Yes' -and $cov -eq 'full') { $existsCheck += "$($r.n): '$($r.e)', but the research found mods that already do all of it (coverage full): 'Yes'?" }
    if ($r.e -ne 'No' -and $cov -eq 'none') { $existsCheck += "$($r.n): '$($r.e)', but the research found no mod doing it (coverage none): 'No'?" }
}

# Me keep snapshot status = sheet status. Only Windows PowerShell 5.1 write the file: its ConvertTo-Json made
# the file, same writer = only changed lines differ (pwsh 7 format everything different).
if ($statusSync) {
    if ($PSVersionTable.PSVersion.Major -eq 5) {
        foreach ($s in $statusSync) {
            if ($s.Res.PSObject.Properties['status']) { $s.Res.status = $s.Status }
            else { $s.Res | Add-Member -NotePropertyName status -NotePropertyValue $s.Status }
        }
        [IO.File]::WriteAllText($researchPath, ($research | ConvertTo-Json -Depth 30), (New-Object System.Text.UTF8Encoding($false)))
        Write-Ok "research snapshot: Status copied from the sheet for $($statusSync.Count) idea(s)"
    } else {
        Write-Warn2 "research snapshot: Status differs for $($statusSync.Count) idea(s); run this script with Windows PowerShell 5.1 (powershell.exe) to copy it"
    }
}
foreach ($k in $modsByIdea.Keys) {
    if (-not $sheetNames[$k]) { Write-Warn2 "a mod's ModIdea matches no sheet idea: '$($modsByIdea[$k].Idea)' in $($modsByIdea[$k].Link)" }
}

$sb = New-Object System.Text.StringBuilder
function W([string]$line = '') { [void]$sb.AppendLine($line) }

W '# Idea backlog'
W ''
W "Generated by ``tools/Update-Backlog.ps1`` from [the idea sheet](https://docs.google.com/spreadsheets/d/1nd_oWjYyphCcjt5F0SrstpkMK_UzDdKwj0U1UC3FHG0/edit)"
W "and the research in ``docs/research/idea-research.json`` (game $($research.gameVersion), researched $($research.generated))."
W 'Do not edit by hand: change the sheet or the research data, then re-run the script.'
W ''
W "**Feasibility:** trivial $arrow easy $arrow medium $arrow hard $arrow very-hard. **Who needs it:** Client = only the player who wants it;"
W 'Server = the host/dedicated server; Both = server and every player; Depends = see details. **Assets:** needs custom'
W 'models, textures, animations or sounds. **Existing mods:** how much of it already exists on Nexus/Thunderstore'
W '(details and links per idea below; the game-systems chapters are in `docs/game/`). **Status:** the sheet''s Status'
W '(idea, cancelled...), or a link to the mod once one in `src/` implements the idea (in development or released).'
W ''

W '## Quick wins'
W ''
W 'QoL, trivial or easy, client-side only, no custom assets, not cancelled.'
W ''
W '| Idea | Category | Feasibility | Existing mods | Status |'
W '|---|---|---|---|---|'
$items | Where-Object { -not $_.Cancelled -and $_.Row.s -eq 'QoL' -and ($_.Feas -eq 'trivial' -or $_.Feas -eq 'easy') -and $_.Side -eq 'Client' -and $_.Assets -eq 'no' } |
    Sort-Object { $feasOrder[$_.Feas] }, { $_.Row.c }, { $_.Row.n } | ForEach-Object {
        W "| [$(Cell $_.Row.n)](#$((Cell $_.Row.n).ToLower() -replace '[^a-z0-9 -]', '' -replace ' ', '-')) | $($_.Row.c) | $($_.Feas) | $($_.Coverage) | $($_.Status) |"
    }
W ''

W '## Overview'
W ''
W '| Category | Idea | Scope | Feasibility | Who needs it | Assets | Existing mods | Status |'
W '|---|---|---|---|---|---|---|---|'
foreach ($c in $categories + @($items | ForEach-Object { $_.Row.c } | Where-Object { $categories -notcontains $_ } | Select-Object -Unique)) {
    $items | Where-Object { $_.Row.c -eq $c } | Sort-Object { @{ QoL = 0; Revamp = 1; New = 2 }[$_.Row.s] }, { $feasOrder[$_.Feas] }, { $_.Row.n } | ForEach-Object {
        W "| $c | $(Cell $_.Row.n) | $($_.Row.s) | $($_.Feas) | $($_.Side) | $($_.Assets) | $($_.Coverage) | $($_.Status) |"
    }
}
W ''

foreach ($c in $categories) {
    $group = $items | Where-Object { $_.Row.c -eq $c }
    if (-not $group) { continue }
    W "## $c"
    W ''
    foreach ($it in ($group | Sort-Object { @{ QoL = 0; Revamp = 1; New = 2 }[$_.Row.s] }, { $feasOrder[$_.Feas] }, { $_.Row.n })) {
        $r = $it.Row; $res = $it.Res
        W "### $(Cell $r.n)"
        W ''
        if ($r.d) { W "> $(Cell $r.d)"; W '' }
        W "- **Scope:** $($r.s) $dot **Feasibility:** $($it.Feas) $dot **Who needs it:** $($it.Side) $dot **Custom assets:** $($it.Assets) $dot **Status:** $($it.Status)"
        if ($res) {
            if ($res.sketch) { W "- **Approach:** $(Cell $res.sketch)" }
            if ($res.hooks) { W "- **Hooks:** $((@($res.hooks) | ForEach-Object { '`' + (Cell $_) + '`' }) -join ', ')" }
            if ($res.risks) { W "- **Risks:** $(Cell $res.risks)" }
            if ($res.relatedIdeas) { W "- **Related:** $((@($res.relatedIdeas) | ForEach-Object { Cell $_ }) -join '; ')" }
            if ($res.chapter) { W "- **Game systems:** [docs/game/$($res.chapter).md](game/$($res.chapter).md)" }
            W "- **Existing mods:** $($it.Coverage)$(if ($r.l) { " (sheet link: $($r.l))" })"
            $mods = @($res.existingMods)
            if ($mods.Count -gt 0 -and $mods[0]) {
                W ''
                W '  | Mod | Status | Notes |'
                W '  |---|---|---|'
                foreach ($m in $mods) { W "  | [$(Cell $m.name)]($($m.url)) | $(Cell $m.status) | $(Cell $m.notes) |" }
                W ''
            }
            if ($res.inspiration) { W "- **Inspiration:** $(Cell $res.inspiration)" }
        } else {
            W '- Not researched yet: run a research pass for this idea.'
        }
        W ''
    }
}

if ($research.infrastructure) {
    W '## Infrastructure proposed by the research'
    W ''
    W 'Shared building blocks the research agents recommended while assessing the ideas.'
    W ''
    foreach ($inf in $research.infrastructure) {
        W "### $(Cell $inf.name)"
        W ''
        W "- **Feasibility:** $($inf.feasibility) $dot **Who needs it:** $(if ($sideText.ContainsKey($inf.side)) { $sideText[$inf.side] } else { $inf.side })"
        if ($inf.sketch) { W "- **Approach:** $(Cell $inf.sketch)" }
        if ($inf.risks) { W "- **Risks:** $(Cell $inf.risks)" }
        W ''
    }
}

if ($research.notableMods) {
    W '## Notable mods to study'
    W ''
    W '| Mod | Why |'
    W '|---|---|'
    $research.notableMods | Sort-Object url -Unique | ForEach-Object { W "| [$(Cell $_.name)]($($_.url)) | $(Cell $_.why) |" }
    W ''
}

$out = Join-Path $root 'docs\backlog.md'
[IO.File]::WriteAllText($out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Ok "docs/backlog.md ($($items.Count) ideas)"

if ($drift) {
    Write-Step "Research snapshot out of date$(if ($Offline) { ' (offline: snapshot vs itself, only research gaps show)' })"
    foreach ($d in $drift) { Write-Warn2 $d }
    Write-Host '      Run a research pass for these ideas and update docs/research/idea-research.json (sheet fields verbatim).'
}
if ($statusFix) {
    Write-Step 'Sheet Status cells to update (the repo says otherwise)'
    foreach ($s in $statusFix) { Write-Warn2 $s }
}
if ($existsCheck) {
    Write-Step 'Sheet EXISTS ALREADY cells the research contradicts (check them)'
    foreach ($s in $existsCheck) { Write-Warn2 $s }
}
if ($statusFix -or $existsCheck) {
    Write-Host '      The user maintains the sheet: give them these cells to change (never edit the sheet yourself).'
}
if ($existsCheck) {
    Write-Host '      A cell the user keeps as it is: set "existsConfirmed" to that value in the idea''s research entry.'
}
