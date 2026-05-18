param(
    [string]$FfmpegPath
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot "build.ps1"
$builtExe = Join-Path $projectRoot "dist-native\mini_wallpaper.exe"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\MiniWallpaper"
$installedExe = Join-Path $installDir "mini_wallpaper.exe"
$installedFfmpeg = Join-Path $installDir "ffmpeg.exe"
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

$ffmpegCandidates = @()

if ($FfmpegPath) {
    $ffmpegCandidates += $FfmpegPath
}

$ffmpegCandidates += (Get-Command ffmpeg.exe -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)

$pythonRoot = Join-Path $env:APPDATA "Python"
if (Test-Path $pythonRoot) {
    $ffmpegCandidates += Get-ChildItem -Path $pythonRoot -Recurse -File -Filter "ffmpeg*.exe" -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
}

$ffmpegSource = $ffmpegCandidates |
    Where-Object { $_ -and (Test-Path $_) } |
    Select-Object -First 1

if (-not $ffmpegSource) {
    throw "ffmpeg.exe introuvable. Installez ffmpeg ou relancez le script avec -FfmpegPath 'C:\chemin\vers\ffmpeg.exe'."
}

$ffmpegSourceFullPath = [System.IO.Path]::GetFullPath($ffmpegSource)
$installedFfmpegFullPath = [System.IO.Path]::GetFullPath($installedFfmpeg)
if (-not [System.String]::Equals($ffmpegSourceFullPath, $installedFfmpegFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $ffmpegSource -Destination $installedFfmpeg -Force
}

New-ItemProperty -Path $runKey -Name "MiniWallpaper" -PropertyType String -Value ('"' + $installedExe + '"') -Force | Out-Null

Start-Process -FilePath $installedExe -WindowStyle Hidden

[pscustomobject]@{
    InstalledExe = $installedExe
    InstalledFfmpeg = $installedFfmpeg
    StartupValue = (Get-ItemProperty $runKey).MiniWallpaper
}
