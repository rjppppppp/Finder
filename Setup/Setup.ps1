# Finder Automated Setup Script
$ErrorActionPreference = "SilentlyContinue"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "         Finder - Quick Setup            " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Stop active processes
Write-Host "[1/5] Stopping active Finder processes..." -ForegroundColor Yellow
Stop-Process -Name FinderApp, Finder -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 2. Target Directory
$targetDir = "$env:LOCALAPPDATA\Programs\Finder"
$targetExe = "$targetDir\FinderApp.exe"
Write-Host "[2/5] Installing to $targetDir..." -ForegroundColor Yellow

if (!(Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

$sourceExe = "$PSScriptRoot\..\PublishOutput\FinderApp.exe"
if (!(Test-Path $sourceExe)) {
    $sourceExe = "$PSScriptRoot\FinderApp.exe"
}
if (!(Test-Path $sourceExe)) {
    $sourceExe = "$PSScriptRoot\..\bin\Release\net8.0-windows\FinderApp.exe"
}

Copy-Item -Path $sourceExe -Destination $targetExe -Force
Write-Host "      Installed FinderApp.exe successfully." -ForegroundColor Green

# 3. Windows Startup Registry
Write-Host "[3/5] Registering in Windows Startup..." -ForegroundColor Yellow
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Finder' -Value "`"$targetExe`""
Write-Host "      Added to Task Manager Startup Apps." -ForegroundColor Green

# 4. Create Shortcuts
Write-Host "[4/5] Creating Desktop & Start Menu Shortcuts..." -ForegroundColor Yellow
$wsh = New-Object -ComObject WScript.Shell

# Desktop Shortcut
$desktopPath = [Environment]::GetFolderPath('Desktop') + "\Finder.lnk"
$sc = $wsh.CreateShortcut($desktopPath)
$sc.TargetPath = $targetExe
$sc.WorkingDirectory = $targetDir
$sc.Description = "Finder - Lightning Fast System Search (Alt + Space)"
$sc.Save()

# Start Menu Shortcut
$startMenuDir = [Environment]::GetFolderPath('StartMenu') + "\Programs"
$startMenuPath = "$startMenuDir\Finder.lnk"
$sc2 = $wsh.CreateShortcut($startMenuPath)
$sc2.TargetPath = $targetExe
$sc2.WorkingDirectory = $targetDir
$sc2.Description = "Finder - Lightning Fast System Search (Alt + Space)"
$sc2.Save()
Write-Host "      Shortcuts created." -ForegroundColor Green

# 5. Launch
Write-Host "[5/5] Launching Finder..." -ForegroundColor Yellow
Start-Process -FilePath $targetExe -WorkingDirectory $targetDir

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "       Installation Complete!            " -ForegroundColor Green
Write-Host " Finder is now running & will start on   " -ForegroundColor Green
Write-Host " Windows restart automatically.          " -ForegroundColor Green
Write-Host " Press Alt + Space anytime to search!   " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
