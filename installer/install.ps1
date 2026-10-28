#Requires -RunAsAdministrator
<#
.SYNOPSIS
  System-level setup of the LoL & Doomscroll Limiter (run by the installer, as administrator).

.DESCRIPTION
  - Permissions: the install folder is read-only for normal users, the data folder is not accessible at all.
  - Windows service: automatic start, restart after a crash.
  - Registers the Chrome Native Messaging host (HKLM).
  - Chrome policies: guest mode disabled; in production mode the extension is force-installed
    and the extensions' developer mode is disabled.
  - Starts the tray application automatically for every user.

.PARAMETER ExtensionId
  The extension's ID. Default: the ID of the extension published in the Chrome Web Store
  (mkoeoadekdfnbjpmhiabpjlmhdoimcml); with -DevMode the ID of the development (unpacked) version
  (derived from the manifest "key" field).

.PARAMETER Language
  Language of the program's texts: 'en' or 'hu'. Stored in HKLM\SOFTWARE\LolScrollLimiter\Language. Without it,
  an existing value is kept; otherwise the Windows display language decides (Hungarian for hu, English otherwise).

.PARAMETER ResetMode
  'rolling' (default): rolling 24 hours - every use is released exactly 24 hours later.
  'daily': once a day, at hour DailyResetHour, every limit resets.

.PARAMETER DailyResetHour
  Hour of the daily reset (0-23, local time), only with ResetMode = 'daily'. Default: 3 (03:00).

.PARAMETER ShortVideoMinutes
  Short-video limit in minutes (Reels, TikTok, Shorts together). Default: 60.

.PARAMETER FacebookFeedMinutes
  Facebook feed limit in minutes. Default: 60.

.PARAMETER EnforceOnWeekends
  If given, the limits also apply on Saturday and Sunday. Default: no limit on weekends.

.PARAMETER LolRestDayBonusMatches
  Rest-day bonus: if there was no PvP match on a day, the next day gives this many more (does not stack). Default: 1.

.PARAMETER ShortVideoRestDayBonusMinutes
  Rest-day bonus in minutes for short videos (after a day with 0 minutes of use). Default: 30.

.PARAMETER FacebookFeedRestDayBonusMinutes
  Rest-day bonus in minutes for the Facebook feed. Default: 30.

.PARAMETER LolBanFrom
  Start of the banned hours for LoL PvP (0-23, local time); together with LolBanTo. -1 = no ban (default).
  E.g. -LolBanFrom 1 -LolBanTo 5: no PvP match can be started between 01:00 and 05:00. May span midnight (22 -> 6).

.PARAMETER LolBanTo
  End of the banned hours (exclusive), see LolBanFrom.

.PARAMETER ShortVideoBanFrom
  Banned hours for short videos, see LolBanFrom. (ShortVideoBanTo is the end.)

.PARAMETER FacebookFeedBanFrom
  Banned hours for the Facebook feed, see LolBanFrom. (FacebookFeedBanTo is the end.)

.PARAMETER AdminPasswordFile
  Temporary file written by the installer with the admin password (UTF-16 codes in hex). Limiter.Tray.exe reads it, builds
  a verifier (PBKDF2) in the data folder (admin.json) and deletes the file. The password is never kept in readable form.

.PARAMETER KeepSettings
  On update: does not overwrite the existing limits.json (the limits, the bonus and the banned hours are kept).
  If there is no limits.json yet, it is created with the default values.

.PARAMETER DevMode
  Development test version: the extension has to be loaded manually, unpacked, so it is not force-installed,
  and the developer mode is not disabled. NOT suitable for real use.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = $PSScriptRoot,
    [string]$ExtensionId,
    [ValidateSet('en', 'hu')][string]$Language,
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
    [string]$AdminPasswordFile,
    [switch]$KeepSettings,
    [switch]$DevMode
)

# If the installer was started from PowerShell 7, the inherited PSModulePath breaks the built-in modules of
# Windows PowerShell 5.1 (Get-Acl: "The member Path is already present"). Reset it to the machine-level default.
$env:PSModulePath = [Environment]::GetEnvironmentVariable('PSModulePath', 'Machine')

$ErrorActionPreference = 'Stop'
# For troubleshooting, the full output also goes to the temporary folder of the (administrator) user running this.
try { Start-Transcript -Path (Join-Path $env:TEMP 'LolScrollLimiter-install.log') -Force | Out-Null } catch { }
$ServiceName = 'LolScrollLimiter'
$HostName = 'hu.kmsoft.lolscrolllimiter'
$DataDir = Join-Path $env:ProgramData 'LolScrollLimiter'
$ChromePolicy = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
$ForceList = Join-Path $ChromePolicy 'ExtensionInstallForcelist'
$Marker = 'HKLM:\SOFTWARE\LolScrollLimiter'

if (-not $ExtensionId) {
    $ExtensionId = if ($DevMode) { 'iidlgdeiobckahalgoabbobfdlefkagi' } else { 'mkoeoadekdfnbjpmhiabpjlmhdoimcml' }
}
if ($ExtensionId -notmatch '^[a-p]{32}$') { throw "Invalid extension ID: '$ExtensionId'" }
# Language of the program's texts: given by the installer; on update the stored one is kept; otherwise the display language.
if (-not $Language) {
    $stored = (Get-ItemProperty -Path $Marker -Name 'Language' -ErrorAction SilentlyContinue).Language
    $Language = if ($stored -in 'en', 'hu') { $stored } elseif ((Get-UICulture).TwoLetterISOLanguageName -eq 'hu') { 'hu' } else { 'en' }
}
$InstallDir = (Resolve-Path $InstallDir).Path.TrimEnd('\')
$ServiceExe = Join-Path $InstallDir 'Limiter.Service.exe'
$HostExe = Join-Path $InstallDir 'Limiter.NativeHost.exe'
$TrayExe = Join-Path $InstallDir 'Limiter.Tray.exe'
foreach ($f in $ServiceExe, $HostExe, $TrayExe) { if (-not (Test-Path $f)) { throw "Missing file: $f" } }

function Step($text) { Write-Host "==> $text" }

# Texts shown to the user outside the program (service list, Chrome's host manifest) follow the chosen language.
if ($Language -eq 'hu') {
    $ServiceDisplayName = 'LoL- és görgetéskorlátozó'
    $ServiceDescription = 'Gördülő 24 órás LoL-meccs- és görgetési keret. Leállítása rendszergazdai jogot igényel.'
    $HostDescription = 'LoL- és görgetéskorlátozó — kapcsolat a helyi szolgáltatással'
} else {
    $ServiceDisplayName = 'LoL & Doomscroll Limiter'
    $ServiceDescription = 'Rolling 24-hour LoL match and scrolling limit. Stopping it requires administrator rights.'
    $HostDescription = 'LoL & Doomscroll Limiter - connection to the local service'
}

$SidSystem = 'S-1-5-18'
$SidAdmins = 'S-1-5-32-544'
$SidUsers = 'S-1-5-32-545'

# Sets the permissions of one folder: on the folder only (no /T), the contents inherit.
# On some Windows builds, /inheritance:r ... /T made the files unreadable (even for the
# administrator and SYSTEM) while icacls reported success.
function Set-FolderAcl([string]$Path, [string[]]$Grants) {
    & icacls.exe $Path /inheritance:r /grant:r @Grants /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Cannot set the permissions: $Path" }
    # Delete the explicit rights of existing contents (e.g. from an earlier faulty install) so that they inherit.
    if (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1) {
        & icacls.exe (Join-Path $Path '*') /reset /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Cannot reset the permissions: $Path" }
    }
}

# icacls's exit code is not enough: the permissions and readability are actually verified.
function Assert-Acl([string]$Path, [string[]]$Required, [string[]]$Forbidden = @()) {
    try {
        $acl = Get-Acl -LiteralPath $Path
        if (Test-Path -LiteralPath $Path -PathType Leaf) { [System.IO.File]::OpenRead($Path).Dispose() }
    } catch {
        throw "The permission check failed: $Path is not readable ($($_.Exception.Message)). Fix: icacls `"$Path`" /reset"
    }
    $present = foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        try { $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value }
        catch { $rule.IdentityReference.Value }
    }
    foreach ($sid in $Required) {
        if ($present -notcontains $sid) { throw "Missing permission ($sid): $Path" }
    }
    foreach ($sid in $Forbidden) {
        if ($present -contains $sid) { throw "Unwanted permission ($sid): $Path" }
    }
}

# --- 1. Permissions -----------------------------------------------------------------
Step 'Install folder: read-only for normal users'
# SYSTEM and Administrators: full; Users: read and execute.
Set-FolderAcl $InstallDir @("*${SidSystem}:(OI)(CI)F", "*${SidAdmins}:(OI)(CI)F", "*${SidUsers}:(OI)(CI)RX")
foreach ($f in $InstallDir, $ServiceExe, $HostExe, $TrayExe, (Join-Path $InstallDir 'install.ps1')) {
    if (Test-Path -LiteralPath $f) { Assert-Acl $f -Required $SidSystem, $SidAdmins, $SidUsers }
}

Step 'Data folder: only SYSTEM and Administrators'
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
Set-FolderAcl $DataDir @("*${SidSystem}:(OI)(CI)F", "*${SidAdmins}:(OI)(CI)F")
& icacls.exe $DataDir /setowner "*$SidAdmins" /C /Q | Out-Null
Assert-Acl $DataDir -Required $SidSystem, $SidAdmins -Forbidden $SidUsers

if ($AdminPasswordFile) {
    Step 'Setting the admin password'
    try {
        $proc = Start-Process -FilePath $TrayExe -ArgumentList @('--set-password-file', "`"$AdminPasswordFile`"") -Wait -PassThru -WindowStyle Hidden
        if ($proc.ExitCode -ne 0) { throw "Setting the admin password failed (exit code: $($proc.ExitCode))." }
    } finally {
        Remove-Item -LiteralPath $AdminPasswordFile -Force -ErrorAction SilentlyContinue
    }
    Assert-Acl (Join-Path $DataDir 'admin.json') -Required $SidSystem, $SidAdmins -Forbidden $SidUsers
}

if (-not $KeepSettings) { Step "Saving the limits: $ResetMode$(if ($ResetMode -eq 'daily') { " ($('{0:00}' -f $DailyResetHour):00)" }), short videos $ShortVideoMinutes min, feed $FacebookFeedMinutes min, $(if ($EnforceOnWeekends) { 'limits also on weekends' } else { 'no limit on weekends' })" }
# Banned hours: both hours given and different -> enabled.
function BanSetting([int]$From, [int]$To) {
    $enabled = ($From -ge 0) -and ($To -ge 0) -and ($From -ne $To)
    if ($From -lt 0) { $From = 1 }
    if ($To -lt 0) { $To = 5 }
    return [ordered]@{ enabled = $enabled; from = $From; to = $To }
}

# The values chosen in the installer always override the earlier ones (the service picks them up within 30 s).
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
    Step 'Update: the existing limits are kept'
} else {
    [System.IO.File]::WriteAllText($limitsPath, ($limits | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
}
Assert-Acl $limitsPath -Required $SidSystem, $SidAdmins -Forbidden $SidUsers

# --- 2. Service ---------------------------------------------------------------------
Step 'Installing the Windows service'
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30') }
    & sc.exe config $ServiceName binPath= "`"$ServiceExe`"" start= auto | Out-Null
} else {
    New-Service -Name $ServiceName -BinaryPathName "`"$ServiceExe`"" -DisplayName $ServiceDisplayName -StartupType Automatic | Out-Null
}
& sc.exe description $ServiceName $ServiceDescription | Out-Null
# After a crash: restart after 5 s, 5 s, 30 s; the failure counter resets daily.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null
# Service permissions: Users (IU, SU, AU) can only query it, not stop it.
& sc.exe sdset $ServiceName 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWLOCRRC;;;AU)' | Out-Null
Start-Service -Name $ServiceName

# --- 3. Native Messaging host -------------------------------------------------------
Step 'Registering the Chrome Native Messaging host'
$hostManifest = Join-Path $InstallDir "$HostName.json"
$manifest = [ordered]@{
    name            = $HostName
    description     = $HostDescription
    path            = $HostExe
    type            = 'stdio'
    allowed_origins = @("chrome-extension://$ExtensionId/")
}
[System.IO.File]::WriteAllText($hostManifest, ($manifest | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
$nmKey = "HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\$HostName"
New-Item -Path $nmKey -Force | Out-Null
Set-ItemProperty -Path $nmKey -Name '(default)' -Value $hostManifest

# --- 4. Chrome policies -------------------------------------------------------------
Step 'Setting the Chrome policies'
New-Item -Path $ChromePolicy -Force | Out-Null
New-Item -Path $Marker -Force | Out-Null
Set-ItemProperty -Path $Marker -Name 'ExtensionId' -Value $ExtensionId
Set-ItemProperty -Path $Marker -Name 'InstallDir' -Value $InstallDir
Set-ItemProperty -Path $Marker -Name 'Language' -Value $Language

# Incognito mode is allowed: the extension works there if the user turns on the
# "Allow in Incognito" switch on the chrome://extensions page.
# The ban set by earlier versions is removed.
Remove-ItemProperty -Path $ChromePolicy -Name 'IncognitoModeAvailability' -ErrorAction SilentlyContinue
# Guest mode disabled (0): no extension runs there.
New-ItemProperty -Path $ChromePolicy -Name 'BrowserGuestModeEnabled' -PropertyType DWord -Value 0 -Force | Out-Null

if ($DevMode) {
    Write-Warning 'DEVELOPMENT MODE: the extension is not force-installed and the developer mode is not disabled. Load it unpacked on the chrome://extensions page.'
    Remove-ItemProperty -Path $ChromePolicy -Name 'ExtensionDeveloperModeSettings' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $Marker -Name 'DevMode' -Value 1
} else {
    # Developer mode of extensions disabled (1 = not allowed).
    New-ItemProperty -Path $ChromePolicy -Name 'ExtensionDeveloperModeSettings' -PropertyType DWord -Value 1 -Force | Out-Null
    # Forced installation: the user cannot turn it off or remove it.
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

# --- 5. Tray application ------------------------------------------------------------
Step 'Starting the tray application automatically'
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'LolScrollLimiterTray' -Value "`"$TrayExe`""

Step 'Done. The Chrome policies can be checked on the chrome://policy page (after restarting Chrome).'
exit 0
