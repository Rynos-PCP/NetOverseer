#Requires -RunAsAdministrator
<#
.SYNOPSIS
    NetOverseer Uninstaller

.DESCRIPTION
    Removes NetOverseer from the system: stops the process, removes files,
    deletes the Start Menu shortcut, unregisters the Startup Recorder service,
    and removes the uninstall registry entry.

.PARAMETER InstallDir
    The directory where NetOverseer is installed.
    Defaults to "$env:ProgramFiles\NetOverseer".

.PARAMETER KeepUserData
    If specified, user data (SQLite database, settings, logs) in
    %AppData%\NetOverseer is preserved. Default: prompt user.

.PARAMETER RemoveUserData
    If specified, user data is removed without prompting.

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -KeepUserData
    .\uninstall.ps1 -RemoveUserData
#>

[CmdletBinding(DefaultParameterSetName = 'Prompt')]
param(
    [string] $InstallDir  = "$env:ProgramFiles\NetOverseer",

    [Parameter(ParameterSetName = 'Keep')]
    [switch] $KeepUserData,

    [Parameter(ParameterSetName = 'Remove')]
    [switch] $RemoveUserData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─────────────────────────────────────────────────────────────────
# Helper functions
# ─────────────────────────────────────────────────────────────────

function Write-Step {
    param([string] $Message)
    Write-Host "  --> $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string] $Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string] $Message)
    Write-Warning $Message
}

function Remove-DirectorySafely {
    param([string] $Path, [string] $Description)
    if (Test-Path $Path) {
        Write-Step "Removing $Description..."
        Remove-Item -Path $Path -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $Path) {
            Write-Warn "Could not fully remove '$Path'. Some files may be in use."
        } else {
            Write-Success "$Description removed"
        }
    } else {
        Write-Host "  [--] $Description not found, skipping" -ForegroundColor DarkGray
    }
}

# ─────────────────────────────────────────────────────────────────
# Preamble
# ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "NetOverseer Uninstaller" -ForegroundColor White
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

if (-not (Test-Path $InstallDir)) {
    Write-Warn "Installation directory not found: $InstallDir"
    Write-Host "NetOverseer may not be installed, or was installed to a custom path." -ForegroundColor Yellow
    Write-Host "Use -InstallDir to specify the correct path." -ForegroundColor Yellow
    Write-Host ""
}

# ─────────────────────────────────────────────────────────────────
# User data decision
# ─────────────────────────────────────────────────────────────────

$userDataDir = Join-Path $env:APPDATA 'NetOverseer'
$shouldRemoveUserData = $false

if ($RemoveUserData) {
    $shouldRemoveUserData = $true
} elseif ($KeepUserData) {
    $shouldRemoveUserData = $false
} else {
    # Interactive prompt
    if (Test-Path $userDataDir) {
        Write-Host "  User data found at: $userDataDir" -ForegroundColor White
        Write-Host "  This includes your settings, connection history database, and logs." -ForegroundColor White
        Write-Host ""
        $choice = Read-Host "  Remove user data? [y/N]"
        $shouldRemoveUserData = ($choice -match '^[Yy]$')
    }
}

# ─────────────────────────────────────────────────────────────────
# Stop running process
# ─────────────────────────────────────────────────────────────────

Write-Step "Checking for running NetOverseer instances..."
$processes = @('NetOverseer.App', 'NetOverseer.StartupRecorder')
foreach ($procName in $processes) {
    $proc = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Step "Stopping $procName..."
        $proc | Stop-Process -Force
        Start-Sleep -Milliseconds 300
        Write-Success "$procName stopped"
    }
}

# ─────────────────────────────────────────────────────────────────
# Stop and remove Startup Recorder service
# ─────────────────────────────────────────────────────────────────

$serviceName = 'NetOverseerStartupRecorder'
Write-Step "Checking for service '$serviceName'..."
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Step "Stopping service '$serviceName'..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue

    Write-Step "Removing service '$serviceName'..."
    & sc.exe delete $serviceName | Out-Null
    Write-Success "Service '$serviceName' removed"
} else {
    Write-Host "  [--] Service '$serviceName' not found, skipping" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────────
# Remove installation directory
# ─────────────────────────────────────────────────────────────────

Remove-DirectorySafely -Path $InstallDir -Description "installation directory ($InstallDir)"

# ─────────────────────────────────────────────────────────────────
# Remove Start Menu shortcut
# ─────────────────────────────────────────────────────────────────

$shortcutDir = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\NetOverseer"
Remove-DirectorySafely -Path $shortcutDir -Description "Start Menu folder"

# ─────────────────────────────────────────────────────────────────
# Remove user data (conditional)
# ─────────────────────────────────────────────────────────────────

if ($shouldRemoveUserData) {
    Remove-DirectorySafely -Path $userDataDir -Description "user data ($userDataDir)"
} else {
    Write-Host "  [--] User data preserved at: $userDataDir" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────────
# Remove registry entries
# ─────────────────────────────────────────────────────────────────

Write-Step "Removing registry entries..."

$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NetOverseer'
if (Test-Path $uninstallKey) {
    Remove-Item -Path $uninstallKey -Force -ErrorAction SilentlyContinue
    Write-Success "Uninstall registry key removed"
} else {
    Write-Host "  [--] Uninstall registry key not found, skipping" -ForegroundColor DarkGray
}

# Remove any autostart registry entries (HKCU Run)
$autorunKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty -Path $autorunKey -Name 'NetOverseer' -ErrorAction SilentlyContinue)) {
    Remove-ItemProperty -Path $autorunKey -Name 'NetOverseer' -ErrorAction SilentlyContinue
    Write-Success "Autostart (HKCU Run) entry removed"
}

# Remove HKLM autostart entry if present
$autorunKeyLM = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty -Path $autorunKeyLM -Name 'NetOverseer' -ErrorAction SilentlyContinue)) {
    Remove-ItemProperty -Path $autorunKeyLM -Name 'NetOverseer' -ErrorAction SilentlyContinue
    Write-Success "Autostart (HKLM Run) entry removed"
}

# Remove the app autostart scheduled task (ONLOGON / RL HIGHEST)
$appAutostartTask = 'NetOverseer-AppAutostart'
& schtasks.exe /Query /TN $appAutostartTask 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    & schtasks.exe /Delete /TN $appAutostartTask /F | Out-Null
    Write-Success "Autostart scheduled task '$appAutostartTask' removed"
}

# ─────────────────────────────────────────────────────────────────
# Done
# ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  NetOverseer has been uninstalled." -ForegroundColor Green
Write-Host ""

if (-not $shouldRemoveUserData -and (Test-Path $userDataDir)) {
    Write-Host "  Your data is still available at:" -ForegroundColor White
    Write-Host "    $userDataDir" -ForegroundColor Yellow
    Write-Host "  To remove it manually: Remove-Item '$userDataDir' -Recurse -Force" -ForegroundColor DarkGray
    Write-Host ""
}
