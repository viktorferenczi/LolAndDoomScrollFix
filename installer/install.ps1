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
  A bővítmény azonosítója. Alapértelmezés: a Chrome Web Store-ban közzétett bővítmény azonosítója
  (mkoeoadekdfnbjpmhiabpjlmhdoimcml); -DevMode esetén a fejlesztői (kicsomagolt) verzió azonosítója
  (a manifest "key" mezőjéből adódik).

.PARAMETER ResetMode
  'rolling' (alapértelmezett): gördülő 24 óra — minden felhasználás 24 órával később szabadul fel.
  'daily': naponta egyszer, a DailyResetHour órában minden keret nullázódik.

.PARAMETER DailyResetHour
  Napi nullázás órája (0–23, helyi idő), csak ResetMode = 'daily' esetén. Alapértelmezés: 3 (03:00).

.PARAMETER ShortVideoMinutes
  Rövidvideós keret percben (Reels, TikTok, Shorts együtt). Alapértelmezés: 60.

.PARAMETER FacebookFeedMinutes
  Facebook-hírfolyam keret percben. Alapértelmezés: 60.

.PARAMETER EnforceOnWeekends
  Ha meg van adva, a korlátok szombaton és vasárnap is érvényesek. Alapértelmezés: hétvégén nincs korlát.

.PARAMETER LolRestDayBonusMatches
  Pihenőnap-jutalom: ha egy napon egy PvP-meccs sem volt, másnap ennyivel több jár (nem halmozódik). Alapértelmezés: 1.

.PARAMETER ShortVideoRestDayBonusMinutes
  Pihenőnap-jutalom percben a rövid videókra (0 perc használat után másnap). Alapértelmezés: 30.

.PARAMETER FacebookFeedRestDayBonusMinutes
  Pihenőnap-jutalom percben a Facebook-hírfolyamra. Alapértelmezés: 30.

.PARAMETER LolBanFrom
  Tiltott órák kezdete a LoL PvP-re (0–23, helyi idő); a LolBanTo órával együtt. -1 = nincs tiltás (alapértelmezés).
  Pl. -LolBanFrom 1 -LolBanTo 5: 01:00 és 05:00 között nem indítható PvP-meccs. Átnyúlhat éjfélen (22 → 6).

.PARAMETER LolBanTo
  Tiltott órák vége (nincs benne), lásd LolBanFrom.

.PARAMETER ShortVideoBanFrom
  Tiltott órák a rövid videókra, lásd LolBanFrom. (ShortVideoBanTo a vége.)

.PARAMETER FacebookFeedBanFrom
  Tiltott órák a Facebook-hírfolyamra, lásd LolBanFrom. (FacebookFeedBanTo a vége.)

.PARAMETER KeepSettings
  Frissítéskor: a meglévő limits.json-t nem írja felül (a korlátok, a jutalom és a tiltott órák megmaradnak).
  Ha még nincs limits.json, az alapértelmezett értékekkel jön létre.

.PARAMETER DevMode
  Fejlesztői tesztverzió: a bővítményt kézzel, kicsomagolva kell betölteni, ezért nem kötelező telepítésű,
  és a fejlesztői mód nincs tiltva. Éles használatra NEM alkalmas.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = $PSScriptRoot,
    [string]$ExtensionId,
    [string]$UpdateUrl = 'https://clients2.google.com/service/update2/crx',
    [ValidateSet('rolling', 'daily')][string]$ResetMode = 'rolling',
    [ValidateRange(0, 23)][int]$DailyResetHour = 3,
    [ValidateRange(0, 1440)][int]$ShortVideoMinutes = 60,
    [ValidateRange(0, 1440)][int]$FacebookFeedMinutes = 60,
    [ValidateRange(0, 1000)][int]$LolPvpMatches = 3,
    [ValidateRange(0, 10)][int]$LolRestDayBonusMatches = 1,
    [ValidateRange(0, 1440)][int]$ShortVideoRestDayBonusMinutes = 30,
    [ValidateRange(0, 1440)][int]$FacebookFeedRestDayBonusMinutes = 30,
    [ValidateRange(-1, 23)][int]$LolBanFrom = -1,
    [ValidateRange(-1, 23)][int]$LolBanTo = -1,
    [ValidateRange(-1, 23)][int]$ShortVideoBanFrom = -1,
    [ValidateRange(-1, 23)][int]$ShortVideoBanTo = -1,
    [ValidateRange(-1, 23)][int]$FacebookFeedBanFrom = -1,
    [ValidateRange(-1, 23)][int]$FacebookFeedBanTo = -1,
    [switch]$EnforceOnWeekends,
    [switch]$KeepSettings,
    [switch]$DevMode
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'LolScrollLimiter'
$HostName = 'hu.kmsoft.lolscrolllimiter'
$DataDir = Join-Path $env:ProgramData 'LolScrollLimiter'
$ChromePolicy = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
$ForceList = Join-Path $ChromePolicy 'ExtensionInstallForcelist'
$Marker = 'HKLM:\SOFTWARE\LolScrollLimiter'

if (-not $ExtensionId) {
    $ExtensionId = if ($DevMode) { 'iidlgdeiobckahalgoabbobfdlefkagi' } else { 'mkoeoadekdfnbjpmhiabpjlmhdoimcml' }
}
if ($ExtensionId -notmatch '^[a-p]{32}$') { throw "Érvénytelen bővítményazonosító: '$ExtensionId'" }
$InstallDir = (Resolve-Path $InstallDir).Path.TrimEnd('\')
$ServiceExe = Join-Path $InstallDir 'Limiter.Service.exe'
$HostExe = Join-Path $InstallDir 'Limiter.NativeHost.exe'
$TrayExe = Join-Path $InstallDir 'Limiter.Tray.exe'
foreach ($f in $ServiceExe, $HostExe, $TrayExe) { if (-not (Test-Path $f)) { throw "Hiányzó fájl: $f" } }

function Step($text) { Write-Host "==> $text" }

$SidSystem = 'S-1-5-18'
$SidAdmins = 'S-1-5-32-544'
$SidUsers = 'S-1-5-32-545'

# Egy mappa jogosultságainak beállítása: csak a mappán (/T nélkül), a tartalom örököl.
# A /inheritance:r ... /T egyes Windows-buildeken olvashatatlanná tette a fájlokat (még a
# rendszergazdának és a SYSTEM-nek is), miközben az icacls sikert jelzett.
function Set-FolderAcl([string]$Path, [string[]]$Grants) {
    & icacls.exe $Path /inheritance:r /grant:r @Grants /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "A jogosultságok nem állíthatók be: $Path" }
    # A már meglévő tartalom (pl. egy korábbi, hibás telepítésből) explicit jogait töröljük, hogy örököljön.
    if (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1) {
        & icacls.exe (Join-Path $Path '*') /reset /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "A jogosultságok nem állíthatók vissza: $Path" }
    }
}

# Az icacls kilépési kódja nem elég: ténylegesen ellenőrizzük a jogosultságokat és az olvashatóságot.
function Assert-Acl([string]$Path, [string[]]$Required, [string[]]$Forbidden = @()) {
    try {
        $acl = Get-Acl -LiteralPath $Path
        if (Test-Path -LiteralPath $Path -PathType Leaf) { [System.IO.File]::OpenRead($Path).Dispose() }
    } catch {
        throw "A jogosultság-ellenőrzés sikertelen: $Path nem olvasható ($($_.Exception.Message)). Javítás: icacls `"$Path`" /reset"
    }
    $present = foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        try { $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value }
        catch { $rule.IdentityReference.Value }
    }
    foreach ($sid in $Required) {
        if ($present -notcontains $sid) { throw "Hiányzó jogosultság ($sid): $Path" }
    }
    foreach ($sid in $Forbidden) {
        if ($present -contains $sid) { throw "Nem kívánt jogosultság ($sid): $Path" }
    }
}

# --- 1. Jogosultságok ---------------------------------------------------------------
Step 'Telepítési mappa: normál felhasználónak csak olvasható'
# SYSTEM és Rendszergazdák: teljes; Felhasználók: olvasás és futtatás.
Set-FolderAcl $InstallDir @("*${SidSystem}:(OI)(CI)F", "*${SidAdmins}:(OI)(CI)F", "*${SidUsers}:(OI)(CI)RX")
foreach ($f in $InstallDir, $ServiceExe, $HostExe, $TrayExe, (Join-Path $InstallDir 'install.ps1')) {
    if (Test-Path -LiteralPath $f) { Assert-Acl $f -Required $SidSystem, $SidAdmins, $SidUsers }
}

Step 'Adatmappa: csak SYSTEM és Rendszergazdák'
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
Set-FolderAcl $DataDir @("*${SidSystem}:(OI)(CI)F", "*${SidAdmins}:(OI)(CI)F")
& icacls.exe $DataDir /setowner "*$SidAdmins" /C /Q | Out-Null
Assert-Acl $DataDir -Required $SidSystem, $SidAdmins -Forbidden $SidUsers

if (-not $KeepSettings) { Step "Korlátok mentése: $ResetMode$(if ($ResetMode -eq 'daily') { " ($('{0:00}' -f $DailyResetHour):00)" }), rövid videó $ShortVideoMinutes perc, hírfolyam $FacebookFeedMinutes perc, $(if ($EnforceOnWeekends) { 'hétvégén is van korlát' } else { 'hétvégén nincs korlát' })" }
# Tiltott órák: mindkét óra megadva és különböző → bekapcsolva.
function BanSetting([int]$From, [int]$To) {
    $enabled = ($From -ge 0) -and ($To -ge 0) -and ($From -ne $To)
    if ($From -lt 0) { $From = 1 }
    if ($To -lt 0) { $To = 5 }
    return [ordered]@{ enabled = $enabled; from = $From; to = $To }
}

# A telepítőben választott értékek mindig felülírják a korábbiakat (a szolgáltatás 30 mp-en belül átveszi).
$limits = [ordered]@{
    resetMode           = $ResetMode
    dailyResetHour      = $DailyResetHour
    lolPvpMatches       = $LolPvpMatches
    shortVideoMinutes   = $ShortVideoMinutes
    facebookFeedMinutes = $FacebookFeedMinutes
    enforceOnWeekends   = [bool]$EnforceOnWeekends
    lolRestDayBonusMatches          = $LolRestDayBonusMatches
    shortVideoRestDayBonusMinutes   = $ShortVideoRestDayBonusMinutes
    facebookFeedRestDayBonusMinutes = $FacebookFeedRestDayBonusMinutes
    lolBannedHours                  = (BanSetting $LolBanFrom $LolBanTo)
    shortVideoBannedHours           = (BanSetting $ShortVideoBanFrom $ShortVideoBanTo)
    facebookFeedBannedHours         = (BanSetting $FacebookFeedBanFrom $FacebookFeedBanTo)
}
$limitsPath = Join-Path $DataDir 'limits.json'
if ($KeepSettings -and (Test-Path -LiteralPath $limitsPath)) {
    Step 'Frissítés: a meglévő korlátok megmaradnak'
} else {
    [System.IO.File]::WriteAllText($limitsPath, ($limits | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
}
Assert-Acl $limitsPath -Required $SidSystem, $SidAdmins -Forbidden $SidUsers

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

# Inkognitómód engedélyezett: a bővítmény ott akkor működik, ha a felhasználó bekapcsolja
# a chrome://extensions oldalon az „Engedélyezés inkognitó módban” kapcsolót.
# A korábbi verziók által beállított tiltást eltávolítjuk.
Remove-ItemProperty -Path $ChromePolicy -Name 'IncognitoModeAvailability' -ErrorAction SilentlyContinue
# Vendégmód tiltva (0): ott semmilyen bővítmény nem fut.
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
