# Finder Standalone Installer Build Script
param(
    [string]$SourceExe = "$PSScriptRoot\..\Portable\FinderApp.exe",
    [string]$IconPath = "$PSScriptRoot\..\app.ico",
    [string]$OutputExe = "$PSScriptRoot\FinderSetup.exe"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $SourceExe)) {
    Write-Host "Portable executable not found. Building release..." -ForegroundColor Yellow
    dotnet publish "$PSScriptRoot\..\FinderApp.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$PSScriptRoot\..\Portable"
}

$payloadGz = Join-Path $PSScriptRoot "payload.gz"
$appIco = Join-Path $PSScriptRoot "app.ico"
Copy-Item $IconPath $appIco -Force

Write-Host "Compressing Portable payload..." -ForegroundColor Cyan
$inStream = [System.IO.File]::OpenRead($SourceExe)
$outStream = [System.IO.File]::Create($payloadGz)
$gz = New-Object System.IO.Compression.GZipStream($outStream, [System.IO.Compression.CompressionMode]::Compress)
$inStream.CopyTo($gz)
$gz.Close()
$outStream.Close()
$inStream.Close()

Write-Host "Compiling native installer with csc.exe..." -ForegroundColor Cyan
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$srcFile = Join-Path $PSScriptRoot "NativeInstaller.cs"

& $csc /target:winexe /platform:x64 /optimize+ "/win32icon:$appIco" "/resource:$payloadGz,payload.gz" "/resource:$appIco,app.ico" "/out:$OutputExe" "$srcFile"

# Cleanup temporary payload
Remove-Item $payloadGz -Force -ErrorAction SilentlyContinue
Remove-Item $appIco -Force -ErrorAction SilentlyContinue

if ($LASTEXITCODE -eq 0) {
    Write-Host "FinderSetup.exe generated successfully at: $OutputExe" -ForegroundColor Green
} else {
    Write-Error "Installer compilation failed."
}
