<#
.SYNOPSIS
  Full build on Windows: tests, publishing the three programs, extension packages and the installer.

.DESCRIPTION
  Output in the out\ folder:
    out\app\                          the program files to install (self-contained .NET 10, win-x64)
    out\extension-dev.zip             for loading unpacked (fixed development ID, with the "key" field)
    out\extension-store.zip           for the Chrome Web Store upload (without the "key" field)
    out\LolScrollLimiter-Setup-*.exe  Windows installer (if Inno Setup 6 is installed)
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
$app = Join-Path $out 'app'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $app | Out-Null

if (-not $SkipTests) {
    Write-Host '==> .NET-tesztek'
    dotnet test (Join-Path $root 'tests\Limiter.Core.Tests') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'A .NET-tesztek elbuktak.' }

    Write-Host '==> Extension tests'
    Push-Location (Join-Path $root 'extension')
    try { node --test (Get-ChildItem test\*.test.js | ForEach-Object FullName) } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'The extension tests failed.' }
}

foreach ($project in 'Limiter.Service', 'Limiter.NativeHost', 'Limiter.Tray') {
    Write-Host "==> Publishing $project"
    dotnet publish (Join-Path $root "src\$project") -c $Configuration -r win-x64 --self-contained true -o $app
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}
Copy-Item (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1') $app

Write-Host '==> Extension packages'
$ext = Join-Path $root 'extension'
$stage = Join-Path $out 'ext-stage'
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $ext '*') $stage -Recurse -Exclude 'test', 'package.json'
Remove-Item (Join-Path $stage 'test') -Recurse -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $out 'extension-dev.zip')

$manifestPath = Join-Path $stage 'manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.PSObject.Properties.Remove('key')
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $out 'extension-store.zip')
Remove-Item $stage -Recurse -Force

if (-not $SkipInstaller) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        Write-Warning 'Inno Setup 6 was not found (https://jrsoftware.org/isdl.php) - the installer is skipped. The program files: out\app'
    } else {
        Write-Host '==> Installer'
        & $iscc (Join-Path $PSScriptRoot 'LolScrollLimiter.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Compiling the installer failed.' }
    }
}

Write-Host "==> Done: $out"
