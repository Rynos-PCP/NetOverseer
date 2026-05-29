#Requires -RunAsAdministrator
<#
.SYNOPSIS
    NetOverseer Installer

.DESCRIPTION
    Installs NetOverseer to %ProgramFiles%\NetOverseer, creates a Start Menu shortcut,
    and optionally registers the Startup Recorder service.

.PARAMETER SourceDir
    Directory containing the NetOverseer files to install.
    Defaults to the directory containing this script.

.PARAMETER InstallDir
    Target installation directory.
    Defaults to "$env:ProgramFiles\NetOverseer".

.PARAMETER NoShortcut
    Skip Start Menu shortcut creation.

.PARAMETER NoStartupService
    Skip Startup Recorder service registration.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -InstallDir "D:\Tools\NetOverseer"
#>

[CmdletBinding()]
param(
    [string] $SourceDir       = $PSScriptRoot,
    [string] $InstallDir      = "$env:ProgramFiles\NetOverseer",
    [switch] $NoShortcut,
    [switch] $NoStartupService
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
    Write-Warning "  [WARN] $Message"
}

function Test-WindowsVersion {
    $osVersion = [System.Environment]::OSVersion.Version
    # Windows 10 21H2 = 10.0.19044
    if ($osVersion.Major -lt 10 -or ($osVersion.Major -eq 10 -and $osVersion.Build -lt 19044)) {
        throw "NetOverseer requires Windows 10 21H2 (Build 19044) or newer. " +
              "Current: $($osVersion.ToString())"
    }
    return $osVersion
}

function Test-DotNetRuntime {
    try {
        $output = & dotnet --list-runtimes 2>&1
        $has8 = $output | Where-Object { $_ -match 'Microsoft\.NETCore\.App 8\.' }
        return $has8.Count -gt 0
    } catch {
        return $false
    }
}

function Install-StartupRecorderService {
    param([string] $ExePath)

    $serviceName = 'NetOverseerStartupRecorder'
    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

    if ($existing) {
        Write-Step "Updating existing service '$serviceName'..."
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        & sc.exe config $serviceName binPath= "`"$ExePath`"" | Out-Null
    } else {
        Write-Step "Registering service '$serviceName'..."
        & sc.exe create $serviceName `
            binPath= "`"$ExePath`"" `
            start= auto `
            DisplayName= "NetOverseer Startup Recorder" `
            description= "Records network connections during Windows startup for NetOverseer analysis." `
            | Out-Null
    }

    # Set service to delayed auto-start so it doesn't block boot
    & sc.exe config $serviceName start= delayed-auto | Out-Null

    Write-Success "Service '$serviceName' registered (delayed auto-start)"
}

# ─────────────────────────────────────────────────────────────────
# Pre-flight checks
# ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "NetOverseer Installer" -ForegroundColor White
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

Write-Step "Checking Windows version..."
$osVersion = Test-WindowsVersion
Write-Success "Windows $($osVersion.Major).$($osVersion.Minor) (Build $($osVersion.Build)) – OK"

Write-Step "Checking for .NET 8 runtime..."
$mainExe = Join-Path $SourceDir 'NetOverseer.App.exe'
if (Test-Path $mainExe) {
    # Self-contained build: runtime is bundled, no external check needed
    Write-Success ".NET 8 bundled (self-contained build)"
} elseif (Test-DotNetRuntime) {
    Write-Success ".NET 8 runtime found"
} else {
    Write-Warn ".NET 8 runtime not found. Attempting to download..."
    $dotnetInstallUrl = 'https://dot.net/v1/dotnet-install.ps1'
    $tempScript = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest -Uri $dotnetInstallUrl -OutFile $tempScript -UseBasicParsing
    & $tempScript -Runtime dotnet -Version 8.0 -InstallDir "$env:ProgramFiles\dotnet"
    Remove-Item $tempScript -Force
    Write-Success ".NET 8 runtime installed"
}

Write-Step "Checking source files..."
if (-not (Test-Path $mainExe)) {
    throw "NetOverseer.App.exe not found in '$SourceDir'. " +
          "Please run this script from the extracted ZIP folder."
}
Write-Success "Source files found in: $SourceDir"

# ─────────────────────────────────────────────────────────────────
# Stop running instance if any
# ─────────────────────────────────────────────────────────────────

Write-Step "Checking for running NetOverseer instance..."
$running = Get-Process -Name 'NetOverseer.App' -ErrorAction SilentlyContinue
if ($running) {
    Write-Step "Stopping running instance..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Write-Success "Previous instance stopped"
}

# ─────────────────────────────────────────────────────────────────
# Install files
# ─────────────────────────────────────────────────────────────────

Write-Step "Creating installation directory: $InstallDir"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Write-Success "Directory ready"

Write-Step "Copying files..."
# Exclude install/uninstall scripts from the copy to avoid confusion
$excludeFiles = @('install.ps1', 'uninstall.ps1')
Get-ChildItem -Path $SourceDir -File |
    Where-Object { $_.Name -notin $excludeFiles } |
    ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $InstallDir -Force
    }

# Copy subdirectories (runtimes, etc.)
Get-ChildItem -Path $SourceDir -Directory |
    ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $InstallDir -Recurse -Force
    }

# Copy uninstall script into install dir so users can find it
Copy-Item -Path "$SourceDir\uninstall.ps1" -Destination $InstallDir -Force -ErrorAction SilentlyContinue

Write-Success "Files copied to $InstallDir"

# ─────────────────────────────────────────────────────────────────
# Start Menu shortcut
# ─────────────────────────────────────────────────────────────────

if (-not $NoShortcut) {
    Write-Step "Creating Start Menu shortcut..."
    $shortcutDir  = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\NetOverseer"
    $shortcutPath = "$shortcutDir\NetOverseer.lnk"

    New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null

    $wsh     = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath      = Join-Path $InstallDir 'NetOverseer.App.exe'
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description     = 'NetOverseer – Real-time network transparency'
    $shortcut.IconLocation    = Join-Path $InstallDir 'NetOverseer.App.exe'
    $shortcut.Save()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($wsh) | Out-Null

    Write-Success "Start Menu shortcut created: $shortcutPath"
}

# ─────────────────────────────────────────────────────────────────
# Startup Recorder Service (optional)
# ─────────────────────────────────────────────────────────────────

if (-not $NoStartupService) {
    $recorderExe = Join-Path $InstallDir 'NetOverseer.StartupRecorder.exe'
    if (Test-Path $recorderExe) {
        Install-StartupRecorderService -ExePath $recorderExe
    } else {
        Write-Warn "StartupRecorder not found – skipping service registration"
    }
}

# ─────────────────────────────────────────────────────────────────
# Registry: uninstall entry
# ─────────────────────────────────────────────────────────────────

Write-Step "Registering uninstall entry in Programs and Features..."
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NetOverseer'
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name 'DisplayName'        -Value 'NetOverseer'
Set-ItemProperty -Path $uninstallKey -Name 'DisplayIcon'        -Value (Join-Path $InstallDir 'NetOverseer.App.exe')
Set-ItemProperty -Path $uninstallKey -Name 'Publisher'          -Value 'NetOverseer'
Set-ItemProperty -Path $uninstallKey -Name 'DisplayVersion'     -Value '1.0.0'
Set-ItemProperty -Path $uninstallKey -Name 'InstallLocation'    -Value $InstallDir
Set-ItemProperty -Path $uninstallKey -Name 'UninstallString'    -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
Set-ItemProperty -Path $uninstallKey -Name 'NoModify'           -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name 'NoRepair'           -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name 'EstimatedSize'      -Value 150000 -Type DWord  # ~150 MB in KB
Write-Success "Uninstall entry registered"

# ─────────────────────────────────────────────────────────────────
# Done
# ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "  NetOverseer is installed to: $InstallDir" -ForegroundColor White
Write-Host "  Start from the Start Menu or run:" -ForegroundColor White
Write-Host "    $InstallDir\NetOverseer.App.exe" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Note: NetOverseer requires administrator privileges" -ForegroundColor DarkYellow
Write-Host "  for network capture and firewall management." -ForegroundColor DarkYellow
Write-Host ""
