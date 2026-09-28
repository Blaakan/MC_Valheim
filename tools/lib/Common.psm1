# Me shared helpers for tools/*.ps1. Work in Windows PowerShell 5.1 and pwsh 7.
Set-StrictMode -Version 3.0

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

function Get-RepoRoot { $script:RepoRoot }

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "  OK  $Message" -ForegroundColor Green }
function Write-Warn2([string]$Message) { Write-Host "  !!  $Message" -ForegroundColor Yellow }
function Write-Fail([string]$Message) { Write-Host "  XX  $Message" -ForegroundColor Red }

# Me read <ModAuthor> from Directory.Build.props so author live in one place.
function Get-ModAuthor {
    $props = Get-Content (Join-Path $script:RepoRoot 'Directory.Build.props') -Raw
    if ($props -match '<ModAuthor>([^<]+)</ModAuthor>') { return $Matches[1].Trim() }
    throw 'ModAuthor not found in Directory.Build.props'
}

# Me find Steam libraries from registry + libraryfolders.vdf.
function Get-SteamLibraries {
    $libs = New-Object System.Collections.Generic.List[string]
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $item = Get-ItemProperty -Path $key -ErrorAction Stop
            if ($item.PSObject.Properties['SteamPath']) { $steam = $item.SteamPath; break }
            if ($item.PSObject.Properties['InstallPath']) { $steam = $item.InstallPath; break }
        } catch { }
    }
    if ($steam) {
        $steam = $steam -replace '/', '\'
        $libs.Add($steam)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs.Add(($m.Groups[1].Value -replace '\\\\', '\'))
            }
        }
    }
    $libs | Select-Object -Unique
}

# Me find Valheim: env VALHEIM_DIR, then Local.props, then Steam libraries.
function Find-ValheimDir {
    if ($env:VALHEIM_DIR -and (Test-Path (Join-Path $env:VALHEIM_DIR 'valheim.exe'))) { return (Resolve-Path $env:VALHEIM_DIR).Path }
    $local = Join-Path $script:RepoRoot 'Local.props'
    if (Test-Path $local) {
        $text = Get-Content $local -Raw
        if ($text -match '<ValheimDir>([^<]+)</ValheimDir>') {
            $dir = $Matches[1].Trim()
            if (Test-Path (Join-Path $dir 'valheim.exe')) { return $dir.TrimEnd('\') }
        }
    }
    foreach ($lib in Get-SteamLibraries) {
        $dir = Join-Path $lib 'steamapps\common\Valheim'
        if (Test-Path (Join-Path $dir 'valheim.exe')) { return $dir }
    }
    return $null
}

function Get-ValheimDir {
    $dir = Find-ValheimDir
    if (-not $dir) { throw 'Valheim not found. Run tools/Setup.ps1 -ValheimDir <path> or set VALHEIM_DIR.' }
    $dir
}

function Get-BepInExLogPath { Join-Path (Get-ValheimDir) 'BepInEx\LogOutput.log' }

function Get-ValheimProcess { Get-Process -Name 'valheim' -ErrorAction SilentlyContinue }

# Me list mod csproj files: src/<Category>/<Feature>/<Author>.<Category>.<Feature>.csproj
function Get-ModProjects([string[]]$Filter) {
    $author = Get-ModAuthor
    $projects = Get-ChildItem (Join-Path $script:RepoRoot 'src') -Recurse -Filter "$author.*.csproj" |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    if ($Filter) {
        $projects = $projects | Where-Object {
            $name = $_.BaseName
            @($Filter | Where-Object { $name -like "*$_*" }).Count -gt 0
        }
    }
    @($projects)
}

# Me ask MSBuild for evaluated props (needs .NET 8+ SDK). Return hashtable.
function Get-ModProperties([string]$Project, [string]$Configuration = 'Debug') {
    $names = 'ModGuid', 'ModId', 'ModCategory', 'ModFeatureId', 'ModName', 'Version', 'ModScope', 'ModSide',
             'ModDescription', 'ModPackageName', 'ModAuthor', 'ModWebsiteUrl', 'ModDependencies',
             'BepInExPackDependency', 'TargetPath', 'TargetDir', 'TargetName', 'ModDeployDir'
    $msbuildArgs = @('msbuild', $Project, "-p:Configuration=$Configuration", '-nologo') + ($names | ForEach-Object { "-getProperty:$_" })
    $json = & dotnet @msbuildArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "msbuild property query failed for $Project`n$json" }
    $obj = ($json -join "`n") | ConvertFrom-Json
    $result = @{}
    foreach ($p in $obj.Properties.PSObject.Properties) { $result[$p.Name] = $p.Value }
    $result
}

# Me zip folder with forward-slash entry names. PS 5.1 Compress-Archive use backslash, Thunderstore hate it.
function New-ZipFromDirectory([string]$SourceDir, [string]$ZipPath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    $src = (Resolve-Path $SourceDir).Path.TrimEnd('\')
    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $src -Recurse -File) {
            $entry = $file.FullName.Substring($src.Length + 1) -replace '\\', '/'
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose() }
}

Export-ModuleMember -Function *
