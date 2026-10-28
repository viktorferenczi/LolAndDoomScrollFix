#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Removes the system-level settings of the LoL & Doomscroll Limiter (as administrator).

.PARAMETER RemoveData
  Also deletes the data folder with the counters and limits.json.
#>
[CmdletBinding()]
param([switch]$RemoveData)

# If the installer was started from PowerShell 7, the inherited PSModulePath breaks the built-in modules of
# Windows PowerShell 5.1 (Get-Acl: "The member Path is already present"). Reset it to the machine-level default.
$env:PSModulePath = [Environment]::GetEnvironmentVariable('PSModulePath', 'Machine')

$ErrorActionPreference = 'Continue'
$ServiceName = 'LolScrollLimiter'
$HostName = 'hu.kmsoft.lolscrolllimiter'
$ChromePolicy = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
$ForceList = Join-Path $ChromePolicy 'ExtensionInstallForcelist'
$Marker = 'HKLM:\SOFTWARE\LolScrollLimiter'

Write-Host '==> Stopping the tray application and the service'
Get-Process -Name 'Limiter.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
}

Write-Host '==> Removing the registrations and the Chrome policies'
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
# The uninstaller has already verified the admin password; a new one has to be given when reinstalling.
Remove-Item -LiteralPath (Join-Path $env:ProgramData 'LolScrollLimiter\admin.json') -Force -ErrorAction SilentlyContinue

if ($RemoveData) {
    Write-Host '==> Deleting the data folder'
    Remove-Item -Path (Join-Path $env:ProgramData 'LolScrollLimiter') -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host '==> Done.'
exit 0
