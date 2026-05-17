$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot "build.ps1"
$builtExe = Join-Path $projectRoot "dist-native\mini_wallpaper.exe"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\MiniWallpaper"
$installedExe = Join-Path $installDir "mini_wallpaper.exe"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

& $buildScript | Out-Null

New-Item -ItemType Directory -Force -Path $installDir | Out-Null

$runningApp = Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $installedExe }

if ($runningApp) {
    $runningApp | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Copy-Item -LiteralPath $builtExe -Destination $installedExe -Force
New-ItemProperty -Path $runKey -Name "MiniWallpaper" -PropertyType String -Value ('"' + $installedExe + '"') -Force | Out-Null

Start-Process -FilePath $installedExe -WindowStyle Hidden

[pscustomobject]@{
    InstalledExe = $installedExe
    StartupValue = (Get-ItemProperty $runKey).MiniWallpaper
}
