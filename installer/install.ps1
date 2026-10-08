#Requires -RunAsAdministrator
<#
.SYNOPSIS
  A LoL- és görgetéskorlátozó rendszerszintű beállítása (a telepítő futtatja, rendszergazdaként).

.DESCRIPTION
  - Jogosultságok: a telepítési mappa normál felhasználónak csak olvasható, az adatmappa el sem érhető.
  - Windows-szolgáltatás: automatikus indulás, összeomlás után újraindítás.
  - Chrome Native Messaging host regisztrálása (HKLM).
  - Chrome-szabályok: inkognitó és vendégmód tiltása; éles módban a bővítmény kötelező telepítése
    és a bővítmények fejlesztői módjának tiltása.
  - A tálcaalkalmazás automatikus indítása minden felhasználónak.

.PARAMETER ExtensionId
  A bővítmény azonosítója. Fejlesztői (kicsomagolt) verziónál a manifest "key" mezőjéből adódó azonosító;
  áruházi kiadásnál a Chrome Web Store által adott azonosító.

.PARAMETER DevMode
  Fejlesztői tesztverzió: a bővítményt kézzel, kicsomagolva kell betölteni, ezért nem kötelező telepítésű,
  és a fejlesztői mód nincs tiltva. Éles használatra NEM alkalmas.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = $PSScriptRoot,
    [string]$ExtensionId = 'iidlgdeiobckahalgoabbobfdlefkagi',
    [string]$UpdateUrl = 'https://clients2.google.com/service/update2/crx',
    [switch]$DevMode
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'LolScrollLimiter'
$HostName = 'hu.kmsoft.lolscrolllimiter'
$DataDir = Join-Path $env:ProgramData 'LolScrollLimiter'
$ChromePolicy = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
$ForceList = Join-Path $ChromePolicy 'ExtensionInstallForcelist'
$Marker = 'HKLM:\SOFTWARE\LolScrollLimiter'

if ($ExtensionId -notmatch '^[a-p]{32}$') { throw "Érvénytelen bővítményazonosító: '$ExtensionId'" }
$InstallDir = (Resolve-Path $InstallDir).Path.TrimEnd('\')
$ServiceExe = Join-Path $InstallDir 'Limiter.Service.exe'
$HostExe = Join-Path $InstallDir 'Limiter.NativeHost.exe'
$TrayExe = Join-Path $InstallDir 'Limiter.Tray.exe'
foreach ($f in $ServiceExe, $HostExe, $TrayExe) { if (-not (Test-Path $f)) { throw "Hiányzó fájl: $f" } }

function Step($text) { Write-Host "==> $text" }

# --- 1. Jogosultságok ---------------------------------------------------------------
Step 'Telepítési mappa: normál felhasználónak csak olvasható'
# SYSTEM és Rendszergazdák: teljes; Felhasználók: olvasás és futtatás.
& icacls.exe $InstallDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'A telepítési mappa jogosultságai nem állíthatók be.' }

Step 'Adatmappa: csak SYSTEM és Rendszergazdák'
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
& icacls.exe $DataDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Az adatmappa jogosultságai nem állíthatók be.' }
& icacls.exe $DataDir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null

$limits = Join-Path $DataDir 'limits.json'
if (-not (Test-Path $limits)) {
    Set-Content -Path $limits -Encoding UTF8 -Value @'
{
  "lolPvpMatches": 3,
  "shortVideoMinutes": 60,
  "facebookFeedMinutes": 60
}
'@
}

# --- 2. Szolgáltatás ----------------------------------------------------------------
Step 'Windows-szolgáltatás telepítése'
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30') }
    & sc.exe config $ServiceName binPath= "`"$ServiceExe`"" start= auto | Out-Null
} else {
    New-Service -Name $ServiceName -BinaryPathName "`"$ServiceExe`"" -DisplayName 'LoL- és görgetéskorlátozó' -StartupType Automatic | Out-Null
}
& sc.exe description $ServiceName 'Gördülő 24 órás LoL-meccs- és görgetési keret. Leállítása rendszergazdai jogot igényel.' | Out-Null
# Összeomlás után: újraindítás 5 mp, 5 mp, 30 mp múlva; a hibaszámláló naponta nullázódik.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null
# Szolgáltatás-jogosultság: a Felhasználók (IU, SU, AU) csak lekérdezhetik, nem állíthatják le.
& sc.exe sdset $ServiceName 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWLOCRRC;;;AU)' | Out-Null
Start-Service -Name $ServiceName

# --- 3. Native Messaging host -------------------------------------------------------
Step 'Chrome Native Messaging host regisztrálása'
$hostManifest = Join-Path $InstallDir "$HostName.json"
$manifest = [ordered]@{
    name            = $HostName
    description     = 'LoL- és görgetéskorlátozó — kapcsolat a helyi szolgáltatással'
    path            = $HostExe
    type            = 'stdio'
    allowed_origins = @("chrome-extension://$ExtensionId/")
}
[System.IO.File]::WriteAllText($hostManifest, ($manifest | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
$nmKey = "HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Path $nmKey -Force | Out-Null
Set-ItemProperty -Path $nmKey -Name '(default)' -Value $hostManifest

# --- 4. Chrome-szabályok ------------------------------------------------------------
Step 'Chrome-szabályok beállítása'
New-Item -Path $ChromePolicy -Force | Out-Null
New-Item -Path $Marker -Force | Out-Null
Set-ItemProperty -Path $Marker -Name 'ExtensionId' -Value $ExtensionId
Set-ItemProperty -Path $Marker -Name 'InstallDir' -Value $InstallDir

# Inkognitómód tiltva (1), vendégmód tiltva (0).
New-ItemProperty -Path $ChromePolicy -Name 'IncognitoModeAvailability' -PropertyType DWord -Value 1 -Force | Out-Null
New-ItemProperty -Path $ChromePolicy -Name 'BrowserGuestModeEnabled' -PropertyType DWord -Value 0 -Force | Out-Null

if ($DevMode) {
    Write-Warning 'FEJLESZTŐI MÓD: a bővítmény nincs kötelezően telepítve, és a fejlesztői mód nincs tiltva. Töltsd be kicsomagolva a chrome://extensions oldalon.'
    Remove-ItemProperty -Path $ChromePolicy -Name 'ExtensionDeveloperModeSettings' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $Marker -Name 'DevMode' -Value 1
} else {
    # Bővítmények fejlesztői módja tiltva (1 = nem engedélyezett).
    New-ItemProperty -Path $ChromePolicy -Name 'ExtensionDeveloperModeSettings' -PropertyType DWord -Value 1 -Force | Out-Null
    # Kötelező telepítés: a felhasználó nem kapcsolhatja ki és nem távolíthatja el.
    New-Item -Path $ForceList -Force | Out-Null
    $entry = "$ExtensionId;$UpdateUrl"
    $props = (Get-Item $ForceList).Property
    $already = $props | Where-Object { (Get-ItemPropertyValue -Path $ForceList -Name $_) -like "$ExtensionId;*" }
    foreach ($name in $already) { Remove-ItemProperty -Path $ForceList -Name $name }
    $index = 1
    while ((Get-Item $ForceList).Property -contains "$index") { $index++ }
    New-ItemProperty -Path $ForceList -Name "$index" -PropertyType String -Value $entry -Force | Out-Null
    Set-ItemProperty -Path $Marker -Name 'DevMode' -Value 0
}

# --- 5. Tálcaalkalmazás -------------------------------------------------------------
Step 'Tálcaalkalmazás automatikus indítása'
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'LolScrollLimiterTray' -Value "`"$TrayExe`""

Step 'Kész. A Chrome-szabályok a chrome://policy oldalon ellenőrizhetők (Chrome újraindítása után).'
exit 0
