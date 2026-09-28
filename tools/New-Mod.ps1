<#
.SYNOPSIS
    Scaffold a new mod from templates/Mod: project, Plugin.cs, README, CHANGELOG, placeholder icon,
    and add it to ValheimMods.slnx under its category folder.
.PARAMETER Category
    Combat, Exploration, Farming, Cooking, Building, Crafting, UX (or Core for shared framework mods).
.PARAMETER Feature
    <System>.<Feature> in PascalCase, e.g. Crossbow.StaysLoaded. GUID becomes <Author>.<Category>.<Feature>.
.PARAMETER Side
    Client = only the player using it needs it; Server = host/dedicated server needs it; Both = everyone needs it.
.EXAMPLE
    ./tools/New-Mod.ps1 -Category Combat -Feature Crossbow.StaysLoaded -Name 'Crossbow Stays Loaded' -Scope QoL -Side Client -Description 'Crossbows stay loaded when put away.'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Combat', 'Exploration', 'Farming', 'Cooking', 'Building', 'Crafting', 'UX', 'Core')][string]$Category,
    [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)*$')][string]$Feature,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][ValidateSet('QoL', 'Revamp', 'New')][string]$Scope,
    [ValidateSet('Client', 'Server', 'Both')][string]$Side = 'Client',
    [Parameter(Mandatory)][ValidateLength(1, 250)][string]$Description
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib\Icon.psm1') -Force

$root = Get-RepoRoot
$author = Get-ModAuthor
$guid = "$author.$Category.$Feature"
$dir = Join-Path $root "src\$Category\$Feature"
if (Test-Path $dir) { Write-Fail "Already exists: $dir"; exit 1 }
if ($Description -match '["<>&]') { Write-Fail 'Description cannot contain " < > & (it goes into XML and C#).'; exit 1 }

$sideText = @{
    Client = 'Client-side only: only the players who want the feature need to install it. Works on vanilla servers and with vanilla friends.'
    Server = 'Server-side: install it on the host / dedicated server. Clients do not need it.'
    Both   = 'Required on the server and on every client.'
}[$Side]
# Me only lock client mods to valheim.exe. Server/Both mods also load in valheim_server.exe.
$processAttr = ''
if ($Side -eq 'Client') { $processAttr = "[BepInProcess(`"valheim.exe`")]`r`n" }

$tokens = [ordered]@{
    '__GUID__'              = $guid
    '__NAME__'              = $Name
    '__DESCRIPTION__'       = $Description
    '__SCOPE__'             = $Scope
    '__SIDE__'              = $Side
    '__SIDE_TEXT__'         = $sideText
    '__CATEGORY__'          = $Category
    '__PROCESS_ATTRIBUTE__' = $processAttr
}

Write-Step "Create $guid"
New-Item -ItemType Directory -Force $dir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($file in Get-ChildItem (Join-Path $root 'templates\Mod') -File) {
    $text = [IO.File]::ReadAllText($file.FullName)
    foreach ($k in $tokens.Keys) { $text = $text.Replace($k, $tokens[$k]) }
    $targetName = $file.Name.Replace('__GUID__', $guid)
    [IO.File]::WriteAllText((Join-Path $dir $targetName), $text, $utf8)
    Write-Ok $targetName
}
New-ModIcon -Path (Join-Path $dir 'icon.png') -Category $Category -FeatureId $Feature
Write-Ok 'icon.png (placeholder)'

Write-Step 'Add to solution'
$csproj = Join-Path $dir "$guid.csproj"
& dotnet sln (Join-Path $root 'ValheimMods.slnx') add $csproj --solution-folder $Category
if ($LASTEXITCODE -ne 0) { Write-Warn2 'dotnet sln add failed; add the project to ValheimMods.slnx by hand.' }

Write-Host ''
Write-Host "Done: $dir" -ForegroundColor Green
Write-Host "  Next: write patches under $dir\Patches, fill README.md, then: dotnet build `"$csproj`""
