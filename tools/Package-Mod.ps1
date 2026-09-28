<#
.SYNOPSIS
    Nexus Mods publishing pipeline. Builds Release and prepares everything to upload, per mod and for the
    all-mods pack, in dist/nexus/:
      <Guid>/<Version>/<Package>-<Version>.zip   extract into the Valheim folder:
                                                BepInEx/plugins/MC_Valheim/<Category>/<Guid>/ (dll, README.md, CHANGELOG.md)
      <Guid>/<Version>/description.bbcode.txt   page description (README + shared install text, as Nexus BBCode)
      <Guid>/<Version>/nexus-page.md            every page/file field to fill in, test status, upload checklist
      <Guid>/<Version>/banner.png               placeholder primary image
      _Pack/<PackVersion>/...                   same for the pack (every mod in one zip; packaging/nexus/pack.json)
    Nothing is uploaded: you create/update the Nexus pages by hand with these files.
.PARAMETER Mod
    Only mods whose project name contains one of these strings. Without -Mod: every mod + the pack.
.PARAMETER Pack
    Also build the pack when -Mod is used.
.PARAMETER Release
    Release gate: clean git tree, version not released before (git tag nexus/<Guid>/v<Version>), CHANGELOG entry,
    no failed test, no untested item (unless -AllowPending). On success, tags the commit locally.
.PARAMETER AllowPending
    With -Release: allow untested TESTING.md items; they are listed on the page sheet.
.PARAMETER Thunderstore
    Also build Thunderstore zips in dist/thunderstore (not our target for now).
.EXAMPLE
    ./tools/Package-Mod.ps1                        # everything, as a dry run
    ./tools/Package-Mod.ps1 -Mod Crossbow
    ./tools/Package-Mod.ps1 -Release -AllowPending # real release, multiplayer untested
#>
[CmdletBinding()]
param([string[]]$Mod, [switch]$Pack, [switch]$Release, [switch]$AllowPending, [switch]$Thunderstore)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib\BBCode.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib\Icon.psm1') -Force
Add-Type -AssemblyName System.Drawing

$root = Get-RepoRoot
$dist = Join-Path $root 'dist'
$nexusDir = Join-Path $root 'packaging\nexus'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$author = Get-ModAuthor
$collection = Get-ModCollectionFolder
$dot = [char]0x00B7
$buildPack = (-not $Mod) -or $Pack

$projects = Get-ModProjects $Mod
if ($projects.Count -eq 0) { Write-Fail 'No mod projects matched.'; exit 1 }
Assert-ModRequiresAcyclic
$pages = [IO.File]::ReadAllText((Join-Path $nexusDir 'pages.json')) | ConvertFrom-Json
$installTemplate = [IO.File]::ReadAllText((Join-Path $nexusDir 'install.md'), [Text.Encoding]::UTF8)

# Nexus has its own category list; me suggest the closest. Author picks the final one on the page.
$nexusCategory = @{
    Combat = 'Gameplay'; Exploration = 'Gameplay'; Farming = 'Gameplay'; Cooking = 'Gameplay'
    Building = 'Building'; Crafting = 'Crafting'; UX = 'User Interface'; Core = 'Utilities'
}

if ($Release) {
    $dirty = @(& git -C $root status --porcelain)
    if ($dirty.Count -gt 0) { Write-Fail 'Release needs a clean git tree (commit first): the zip must match a commit.'; exit 1 }
}
$buildId = (& git -C $root rev-parse --short HEAD 2>$null)
if (-not $buildId) { $buildId = 'local' }
$failed = 0
$packaged = New-Object System.Collections.Generic.List[object]
$tagsToCreate = New-Object System.Collections.Generic.List[object]

# Me wipe a folder, but only inside dist/ (safety).
function Reset-StageDir([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith([IO.Path]::GetFullPath($dist), [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clear $full (outside dist/)" }
    if (Test-Path $full) { [IO.Directory]::Delete($full, $true) }
    [void][IO.Directory]::CreateDirectory($full)
}

function Test-TagExists([string]$Tag) { @(& git -C $root tag -l $Tag).Count -gt 0 }

function Get-ChangelogSection([string]$Path, [string]$Version) {
    $text = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) -replace "`r", ''
    $m = [regex]::Match($text, "(?ms)^## $([regex]::Escape($Version))\b[^\n]*\n(.*?)(?=^## |\z)")
    if ($m.Success) { $m.Groups[1].Value.Trim() } else { $null }
}

function Expand-Install([hashtable]$Tokens) {
    $t = $installTemplate
    foreach ($k in $Tokens.Keys) { $t = $t.Replace("{{$k}}", $Tokens[$k]) }
    $t
}

# README with its "Installation" section replaced by the shared Nexus install text.
function Merge-Readme([string]$ReadmeText, [string]$InstallText) {
    $parts = Split-MarkdownSections $ReadmeText
    $out = New-Object System.Collections.Generic.List[string]
    $inserted = $false
    foreach ($s in $parts) {
        if ($s.Title -eq 'Installation') { $out.Add($InstallText.Trim()); $out.Add(''); $inserted = $true; continue }
        foreach ($l in $s.Lines) { $out.Add($l) }
    }
    if (-not $inserted) { $out.Add(''); $out.Add($InstallText.Trim()) }
    ($out -join "`n").Trim() + "`n"
}

function Format-TestStatus($items) {
    $pass = @($items | Where-Object { $_.Mark -eq 'x' }).Count
    $todo = @($items | Where-Object { $_.Mark -eq ' ' })
    $fail = @($items | Where-Object { $_.Mark -eq '!' })
    $lines = @("- $pass passed, $($todo.Count) not tested, $($fail.Count) failed.")
    foreach ($t in $fail) { $lines += "- FAILED: $($t.Text)" }
    foreach ($t in $todo) { $lines += "- Not tested: $($t.Text)" }
    $lines -join "`n"
}

foreach ($p in $projects) {
    Write-Step "Nexus package: $($p.BaseName)"
    & dotnet build $p.FullName -c Release -nologo -v q -p:DeployToGame=false | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Fail 'build failed'; $failed++; continue }
    $props = Get-ModProperties $p.FullName 'Release'
    $dir = $p.DirectoryName
    $guid = $props.ModGuid
    $version = $props.Version

    # Me check package bits before zip.
    $problems = @()
    $warnings = @()
    $readme = Join-Path $dir 'README.md'
    $changelog = Join-Path $dir 'CHANGELOG.md'
    $testing = Join-Path $dir 'TESTING.md'
    $icon = Join-Path $dir 'icon.png'
    if (-not (Test-Path $readme)) { $problems += 'README.md missing' }
    elseif ((Get-Content $readme -Raw -Encoding UTF8) -match '(?m)^- TODO') { $problems += 'README.md still has TODO' }
    $changes = $null
    if (-not (Test-Path $changelog)) { $problems += 'CHANGELOG.md missing' }
    else {
        $changes = Get-ChangelogSection $changelog $version
        if (-not $changes) { $problems += "CHANGELOG.md has no '## $version' entry" }
    }
    $tests = @()
    if (Test-Path $testing) { $tests = @(Get-TestItems $testing) } else { $problems += 'TESTING.md missing' }
    $failedTests = @($tests | Where-Object { $_.Mark -eq '!' })
    $pendingTests = @($tests | Where-Object { $_.Mark -eq ' ' })
    if ($failedTests.Count) { $problems += "$($failedTests.Count) failed test(s) in TESTING.md" }
    if ($pendingTests.Count) {
        $msg = "$($pendingTests.Count) untested item(s) in TESTING.md"
        if ($Release -and -not $AllowPending) { $problems += "$msg (use -AllowPending to release anyway; they will be listed)" } else { $warnings += $msg }
    }
    $tag = "nexus/$guid/v$version"
    if ($Release -and (Test-TagExists $tag)) { $problems += "version $version already released ($tag): bump <Version> and add a CHANGELOG entry" }
    $warnings | ForEach-Object { Write-Warn2 $_ }
    if ($problems) { $problems | ForEach-Object { Write-Fail $_ }; $failed++; continue }

    $out = Join-Path $dist "nexus\$guid\$version"
    Reset-StageDir $out
    $folderRel = "BepInEx/plugins/$collection/$($props.ModCategory)/$guid/"
    $install = Expand-Install @{
        BEPINEX_LINK = $pages.bepinex; FOLDER = $folderRel; CONFIG = "$guid.cfg"; WHAT = "**$($props.ModName)** is"
    }
    $readmeText = Merge-Readme ([IO.File]::ReadAllText($readme, [Text.Encoding]::UTF8)) $install

    # Zip: rooted at the Valheim folder.
    $stage = Join-Path $dist "staging\nexus-$guid"
    Reset-StageDir $stage
    $modDir = Join-Path (Join-Path $stage 'BepInEx\plugins') (Get-ModInstallRelPath $guid)
    New-Item -ItemType Directory -Force $modDir | Out-Null
    Copy-Item $props.TargetPath $modDir
    [IO.File]::WriteAllText((Join-Path $modDir 'README.md'), $readmeText, $utf8)
    Copy-Item $changelog $modDir
    $contentDir = Join-Path $dir 'Content'
    if (Test-Path $contentDir) { Copy-Item (Join-Path $contentDir '*') $modDir -Recurse }
    $zipName = "$($props.ModPackageName)-$version.zip"
    New-ZipFromDirectory $stage (Join-Path $out $zipName)

    # Description: README (install section swapped) + link to the pack.
    $descMd = $readmeText
    if ($pages.pack) { $descMd += "`n## All MC mods`n`nWant every MC mod at once? Get the [all-mods pack]($($pages.pack)).`n" }
    [IO.File]::WriteAllText((Join-Path $out 'description.bbcode.txt'), (ConvertTo-NexusBBCode $descMd), $utf8)

    $side = @{ Client = 'Client-side'; Server = 'Server-side'; Both = 'Server + clients' }[$props.ModSide]
    New-ModBanner -Path (Join-Path $out 'banner.png') -Title $props.ModName -Subtitle $props.ModDescription -Category $props.ModCategory `
        -Footer "MC Valheim  |  $($props.ModCategory)  |  $($props.ModScope)  |  $side"

    $pageUrl = ''
    if ($pages.mods.PSObject.Properties[$guid]) { $pageUrl = [string]$pages.mods.$guid }
    $requires = @("BepInExPack for Valheim 5.4.2350 or newer ($($pages.bepinex))")
    foreach ($req in ($props.ModRequires -split ';' | Where-Object { $_.Trim() })) {
        $u = ''
        if ($pages.mods.PSObject.Properties[$req.Trim()]) { $u = [string]$pages.mods.($req.Trim()) }
        $requires += "$($req.Trim()) (MC mod$(if ($u) { ": $u" } else { ', page not created yet: create it first' }))"
    }
    $sheet = @"
# Nexus page: $($props.ModName) $version

$(if ($pageUrl) { "Existing page: $pageUrl (upload a new file version)" } else { 'No page yet: create a new mod page (then put its URL in packaging/nexus/pages.json).' })

| Field | Value |
|---|---|
| Mod name | $($props.ModName) |
| Summary | $($props.ModDescription) |
| Category (suggested) | $($nexusCategory[$props.ModCategory]) (our category: $($props.ModCategory), scope: $($props.ModScope)) |
| Version | $version |
| Author | $author |
| Primary image | banner.png (placeholder; in-game screenshots are better) |
| Description | paste description.bbcode.txt |
| Requirements | $($requires -join '; ') |
| AI content | Required tag: mark the mod as AI-generated/AI-assisted content (Nexus rule). |
| Permissions | your choice (suggested: ask before re-uploading; modding/converting allowed with credit) |

## File upload

| Field | Value |
|---|---|
| File | $zipName |
| File category | Main Files (move the previous version to Old Files) |
| File version | $version |
| File name | $($props.ModName) $version |
| File description | Extract into your Valheim folder (creates $folderRel). Needs BepInExPack for Valheim. |
| Build | $buildId |

## Changelog for $version

$changes

## Test status (TESTING.md)

$(Format-TestStatus $tests)

## Checklist

- [ ] Page fields filled (name, summary, category, requirements, AI tag, permissions)
- [ ] Description pasted (preview it: lists and links render)
- [ ] File uploaded as Main Files, version $version, old file moved to Old Files
- [ ] Changelog entry added on the page for $version
- [ ] Page URL saved in packaging/nexus/pages.json (first release only)
"@
    [IO.File]::WriteAllText((Join-Path $out 'nexus-page.md'), $sheet, $utf8)
    Write-Ok "dist\nexus\$guid\$version\ ($zipName, description, page sheet, banner)"

    if ($Thunderstore) {
        $tsStage = Join-Path $dist "staging\ts-$guid"
        Reset-StageDir $tsStage
        $tsPlugin = Join-Path $tsStage "plugins\$guid"
        New-Item -ItemType Directory -Force $tsPlugin | Out-Null
        $manifest = [ordered]@{ name = $props.ModPackageName; version_number = $version; website_url = [string]$props.ModWebsiteUrl
                                description = $props.ModDescription; dependencies = @($props.BepInExPackDependency) }
        [IO.File]::WriteAllText((Join-Path $tsStage 'manifest.json'), ($manifest | ConvertTo-Json -Depth 3), $utf8)
        [IO.File]::WriteAllText((Join-Path $tsStage 'README.md'), $readmeText, $utf8)
        Copy-Item $changelog, $icon $tsStage
        Copy-Item $props.TargetPath $tsPlugin
        New-Item -ItemType Directory -Force (Join-Path $dist 'thunderstore') | Out-Null
        New-ZipFromDirectory $tsStage (Join-Path $dist "thunderstore\$author-$($props.ModPackageName)-$version.zip")
        Write-Ok "dist\thunderstore\$author-$($props.ModPackageName)-$version.zip"
    }

    $packaged.Add([pscustomobject]@{ Props = $props; ModDir = $modDir; Changes = $changes; Pending = $pendingTests.Count })
    $tagsToCreate.Add([pscustomobject]@{ Tag = $tag; Message = "$($props.ModName) $version (Nexus)" })
}
if ($failed) { Write-Fail "$failed mod(s) not packaged"; exit 1 }

if ($buildPack) {
    Write-Step 'Nexus package: all-mods pack'
    $packInfo = [IO.File]::ReadAllText((Join-Path $nexusDir 'pack.json')) | ConvertFrom-Json
    $packVersion = $packInfo.version
    $packChanges = Get-ChangelogSection (Join-Path $nexusDir 'PACK_CHANGELOG.md') $packVersion
    $packTag = "nexus/pack/v$packVersion"
    $packProblems = @()
    if (-not $packChanges) { $packProblems += "packaging/nexus/PACK_CHANGELOG.md has no '## $packVersion' entry" }
    if ($Release -and (Test-TagExists $packTag)) { $packProblems += "pack $packVersion already released ($packTag): bump version in packaging/nexus/pack.json" }
    if ($packProblems) { $packProblems | ForEach-Object { Write-Fail $_ }; exit 1 }

    $out = Join-Path $dist "nexus\_Pack\$packVersion"
    Reset-StageDir $out
    $mods = $packaged | Sort-Object { $_.Props.ModCategory }, { $_.Props.ModName }

    # Pack README: what is inside, how to pick features, per-mod summary.
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add("# $($packInfo.name)")
    $md.Add('')
    $md.Add($packInfo.summary)
    $md.Add('')
    $md.Add('## Pick what you want')
    $md.Add('')
    $md.Add('Every mod can be turned on or off on its own, live: in the main menu or the pause menu (Esc) click **MC Mods**, or edit its config file in `BepInEx/config/`. If a feature needs another one that is turned off, it stays inactive and tells you why, and comes back by itself when you turn the other one back on. You can also simply delete the folders of the mods you do not want.')
    $md.Add('')
    $md.Add('## Included mods')
    $md.Add('')
    foreach ($m in $mods) {
        $pp = $m.Props
        $link = ''
        if ($pages.mods.PSObject.Properties[$pp.ModGuid] -and $pages.mods.($pp.ModGuid)) { $link = " ([page]($($pages.mods.($pp.ModGuid))))" }
        $sideText = @{ Client = 'only you need it'; Server = 'host/server needs it'; Both = 'server and every player need it' }[$pp.ModSide]
        $md.Add("- **$($pp.ModName)** $($pp.Version) $dot $($pp.ModCategory) $dot $($pp.ModScope) $dot $sideText$link. $($pp.ModDescription)")
    }
    $md.Add('')
    $md.Add((Expand-Install @{ BEPINEX_LINK = $pages.bepinex; FOLDER = "BepInEx/plugins/$collection/"; CONFIG = 'MC.*.cfg'; WHAT = 'the mods are' }).Trim())
    $packReadme = ($md -join "`n") + "`n"

    $stage = Join-Path $dist 'staging\nexus-pack'
    Reset-StageDir $stage
    $collectionDir = Join-Path $stage "BepInEx\plugins\$collection"
    New-Item -ItemType Directory -Force $collectionDir | Out-Null
    foreach ($m in $mods) {
        $target = Join-Path (Join-Path $stage 'BepInEx\plugins') (Get-ModInstallRelPath $m.Props.ModGuid)
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item (Join-Path $m.ModDir '*') $target -Recurse
    }
    # README inside the collection folder: zip extract into the Valheim folder, so no clutter next to valheim.exe.
    [IO.File]::WriteAllText((Join-Path $collectionDir 'README.md'), $packReadme, $utf8)
    $zipName = "$($packInfo.fileBaseName)-$packVersion.zip"
    New-ZipFromDirectory $stage (Join-Path $out $zipName)
    [IO.File]::WriteAllText((Join-Path $out 'description.bbcode.txt'), (ConvertTo-NexusBBCode $packReadme), $utf8)
    New-ModBanner -Path (Join-Path $out 'banner.png') -Title $packInfo.name -Subtitle $packInfo.summary -Category 'Core' `
        -Footer "MC Valheim  |  $($mods.Count) mod(s)  |  pick features in game"

    $contents = ($mods | ForEach-Object { "| $($_.Props.ModName) | $($_.Props.Version) | $($_.Props.ModCategory) | $(if ($_.Pending) { "$($_.Pending) untested" } else { 'all tested' }) |" }) -join "`n"
    $sheet = @"
# Nexus page: $($packInfo.name) $packVersion

$(if ($pages.pack) { "Existing page: $($pages.pack) (upload a new file version)" } else { 'No page yet: create a new mod page (then put its URL in packaging/nexus/pages.json "pack").' })

| Field | Value |
|---|---|
| Mod name | $($packInfo.name) |
| Summary | $($packInfo.summary) |
| Category (suggested) | Gameplay |
| Version | $packVersion |
| Author | $author |
| Primary image | banner.png |
| Description | paste description.bbcode.txt |
| Requirements | BepInExPack for Valheim 5.4.2350 or newer ($($pages.bepinex)) |
| AI content | Required tag: mark the mod as AI-generated/AI-assisted content (Nexus rule). |

## File upload

| Field | Value |
|---|---|
| File | $zipName |
| File category | Main Files (move the previous version to Old Files) |
| File version | $packVersion |
| File description | Every MC mod. Extract into your Valheim folder (creates BepInEx/plugins/$collection/). Needs BepInExPack for Valheim. |
| Build | $buildId |

## Contents

| Mod | Version | Category | Tests |
|---|---|---|---|
$contents

## Changelog for $packVersion

$packChanges

## Checklist

- [ ] Every included mod released first (so its page and version exist)
- [ ] Page fields filled, description pasted, AI tag set
- [ ] File uploaded as Main Files, version $packVersion, old file moved to Old Files
- [ ] Changelog entry added on the page
"@
    [IO.File]::WriteAllText((Join-Path $out 'nexus-page.md'), $sheet, $utf8)
    Write-Ok "dist\nexus\_Pack\$packVersion\ ($zipName, description, page sheet, banner)"
    $tagsToCreate.Add([pscustomobject]@{ Tag = $packTag; Message = "$($packInfo.name) $packVersion (Nexus): " + (($mods | ForEach-Object { "$($_.Props.ModGuid) $($_.Props.Version)" }) -join ', ') })
}

if ($Release) {
    Write-Step 'Tag release (local git tags)'
    foreach ($t in $tagsToCreate) {
        & git -C $root tag -a $t.Tag -m $t.Message
        if ($LASTEXITCODE -ne 0) { Write-Fail "could not tag $($t.Tag)"; exit 1 }
        Write-Ok $t.Tag
    }
}
Write-Host ''
Write-Host "Done. Upload files and page texts are in dist\nexus\ (open each nexus-page.md)." -ForegroundColor Green
