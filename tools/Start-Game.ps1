<#
.SYNOPSIS
    Launch Valheim with BepInEx (default), without mods (-Vanilla), or with the Mono debugger
    listening so VS/Rider can attach (-DebugMono).
.PARAMETER Vanilla
    Disable Doorstop for this launch (no BepInEx, no mods).
.PARAMETER DebugMono
    Start the Mono soft debugger on 127.0.0.1:10000 (attach with "Attach Unity debugger" / Rider "Attach to Unity Process" with that address).
.PARAMETER Suspend
    With -DebugMono: wait for the debugger to attach before running any game code.
.PARAMETER NoConsole
    Do not pass -console (the in-game F5 dev console is enabled by default).
.PARAMETER Wait
    Block until the game exits.
.EXAMPLE
    ./tools/Start-Game.ps1
    ./tools/Start-Game.ps1 -DebugMono -Suspend
#>
[CmdletBinding()]
param(
    [switch]$Vanilla,
    [switch]$DebugMono,
    [switch]$Suspend,
    [switch]$NoConsole,
    [switch]$Wait,
    [string[]]$ExtraArgs
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force

$game = Get-ValheimDir
if (Get-ValheimProcess) { Write-Fail 'Valheim is already running.'; exit 1 }
if (-not (Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) { Write-Warn2 'Steam is not running; the game needs it for login.' }

$gameArgs = New-Object System.Collections.Generic.List[string]
if (-not $NoConsole) { $gameArgs.Add('-console') }
# Me use Doorstop 4 command-line switches, so doorstop_config.ini stay untouched.
if ($Vanilla) { $gameArgs.Add('--doorstop-enabled'); $gameArgs.Add('false') }
if ($DebugMono) {
    $gameArgs.Add('--doorstop-mono-debug-enabled'); $gameArgs.Add('true')
    $gameArgs.Add('--doorstop-mono-debug-address'); $gameArgs.Add('127.0.0.1:10000')
    if ($Suspend) { $gameArgs.Add('--doorstop-mono-debug-suspend'); $gameArgs.Add('true') }
}
if ($ExtraArgs) { $ExtraArgs | ForEach-Object { $gameArgs.Add($_) } }

Write-Step "Launch Valheim $($gameArgs -join ' ')"
$proc = Start-Process -FilePath (Join-Path $game 'valheim.exe') -WorkingDirectory $game -ArgumentList $gameArgs -PassThru
Write-Ok "pid $($proc.Id)"
if ($DebugMono) { Write-Host '  Debugger: attach to 127.0.0.1:10000 (Mono soft debugger).' }
if ($Wait) { $proc.WaitForExit() }
$proc
