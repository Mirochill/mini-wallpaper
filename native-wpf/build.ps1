$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $projectRoot "dist-native"
$outputExe = Join-Path $outputDir "mini_wallpaper_native.exe"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfRoot = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
$windowsBase = Join-Path $wpfRoot "WindowsBase.dll"
$presentationCore = Join-Path $wpfRoot "PresentationCore.dll"
$presentationFramework = Join-Path $wpfRoot "PresentationFramework.dll"
$systemXaml = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll"

if (-not (Test-Path $compiler)) {
    throw "csc.exe introuvable: $compiler"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

& $compiler `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /out:$outputExe `
    /r:$windowsBase `
    /r:$presentationCore `
    /r:$presentationFramework `
    /r:$systemXaml `
    /r:System.Windows.Forms.dll `
    /r:System.Drawing.dll `
    (Join-Path $PSScriptRoot "Program.cs")

Get-Item $outputExe
