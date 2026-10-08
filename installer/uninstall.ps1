#Requires -RunAsAdministrator
<#
.SYNOPSIS
  A LoL- és görgetéskorlátozó rendszerszintű beállításainak eltávolítása (rendszergazdaként).

.PARAMETER RemoveData
  A számlálókat és a limits.json-t tartalmazó adatmappát is törli.
#>
[CmdletBinding()]
param([switch]$RemoveData)

$ErrorActionPreference = 'Continue'
$ServiceName = 'LolScrollLimiter'
$HostName = 'hu.kmsoft.lolscrolllimiter'
$ChromePolicy = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
$ForceList = Join-Path $ChromePolicy 'ExtensionInstallForcelist'
$Marker = 'HKLM:\SOFTWARE\LolScrollLimiter'

Write-Host '==> Tálcaalkalmazás és szolgáltatás leállítása'
Get-Process -Name 'Limiter.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
}

Write-Host '==> Regisztrációk és Chrome-szabályok eltávolítása'
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'LolScrollLimiterTray' -ErrorAction SilentlyContinue
Remove-Item -Path "HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\$HostName" -Recurse -ErrorAction SilentlyContinue

$extId = (Get-ItemProperty -Path $Marker -ErrorAction SilentlyContinue).ExtensionId
if ($extId -and (Test-Path $ForceList)) {
    foreach ($name in (Get-Item $ForceList).Property) {
        if ((Get-ItemPropertyValue -Path $ForceList -Name $name) -like "$extId;*") { Remove-ItemProperty -Path $ForceList -Name $name }
    }
    if (-not (Get-Item $ForceList).Property) { Remove-Item $ForceList }
}
foreach ($p in 'IncognitoModeAvailability', 'BrowserGuestModeEnabled', 'ExtensionDeveloperModeSettings') {
    Remove-ItemProperty -Path $ChromePolicy -Name $p -ErrorAction SilentlyContinue
}
Remove-Item -Path $Marker -Recurse -ErrorAction SilentlyContinue

if ($RemoveData) {
    Write-Host '==> Adatmappa törlése'
    Remove-Item -Path (Join-Path $env:ProgramData 'LolScrollLimiter') -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host '==> Kész.'
exit 0
