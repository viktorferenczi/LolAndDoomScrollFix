<#
.SYNOPSIS
  Windows end-to-end test (GitHub Actions, windows-latest): silent install, admin password,
  driving the admin panel with UI Automation, saving an extra allowance, uninstalling with the password.
  Evidence (screenshots, logs) goes to the out\e2e folder. Run once per UI language (-Language en | hu).
#>
[CmdletBinding()]
param(
    [string]$Installer = (Get-ChildItem (Join-Path $PSScriptRoot '..\out\LolScrollLimiter-Setup-*.exe') | Select-Object -First 1).FullName,
    [string]$Evidence = (Join-Path $PSScriptRoot '..\out\e2e'),
    [ValidateSet('en', 'hu')][string]$Language = 'en'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Evidence | Out-Null
$Evidence = (Resolve-Path $Evidence).Path
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32Click {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$Prop = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]

$AppDir = Join-Path $env:ProgramFiles 'LolScrollLimiter'
$DataDir = Join-Path $env:ProgramData 'LolScrollLimiter'
$TrayExe = Join-Path $AppDir 'Limiter.Tray.exe'
$AdminJson = Join-Path $DataDir 'admin.json'
$Password = 'árvíz-123'

# The texts of the user interface in the language under test (the window titles and button names are looked up by these).
$Ui = @{
    en = @{ Pw = 'Admin password'; Cancel = 'Cancel'; Wrong = 'Wrong password \(1/5\)'; Admin = 'Admin settings*'; SaveExtra = 'Save extra for the day';
            ExtraDay = 'Extra day'; ExtraLol = 'Extra LoL matches'; Save = 'Save'; Weekend = 'Saturday|Sunday'; NotSelectable = 'not selectable'; Current = 'current day' }
    hu = @{ Pw = 'Admin jelszó'; Cancel = 'Mégse'; Wrong = 'Hibás jelszó \(1/5\)'; Admin = 'Admin beállítások*'; SaveExtra = 'Extra mentése a napra';
            ExtraDay = 'Extra napja'; ExtraLol = 'Extra LoL-meccsek'; Save = 'Mentés'; Weekend = 'szombat|vasárnap'; NotSelectable = 'nem választható'; Current = 'most tartó nap' }
}[$Language]
$failures = New-Object System.Collections.Generic.List[string]

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "OK    $what" } else { Write-Host "FAIL  $what"; $script:failures.Add($what) }
}

function Shot([string]$name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bmp.Save((Join-Path $Evidence "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

function Find-Window([string]$like, [int]$seconds = 30, [string]$withButton = $null) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $kids = $AE::RootElement.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($k in $kids) {
                if ($k.Current.Name -like $like) {
                    if (-not $withButton -or (Find-Named $k $withButton)) { return $k }
                }
            }
        } catch { } # the window disappeared in the meantime: try again
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Find-Named($parent, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($Prop::NameProperty, $name)
    return $parent.FindFirst($Scope::Descendants, $cond)
}

function Click($el) {
    # BM_CLICK: does not block if the button opens a modal window (InvokePattern may hang then).
    $h = [IntPtr]$el.Current.NativeWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "No window handle: $($el.Current.Name)" }
    [void][Win32Click]::PostMessage($h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Edits($win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $CT::Edit)
    return $win.FindAll($Scope::Descendants, $cond)
}

function Type-Password($win, [string]$text) {
    $edit = (Edits $win)[0]
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

function Texts($win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $CT::Text)
    return @($win.FindAll($Scope::Descendants, $cond) | ForEach-Object { $_.Current.Name })
}

function Hex([string]$s) { return (($s.ToCharArray() | ForEach-Object { '{0:x4}' -f [int]$_ }) -join '') }

function Run-Helper([string[]]$arguments) {
    return Start-Process -FilePath $TrayExe -ArgumentList $arguments -PassThru
}

# Bounded waiting: the test can never get stuck forever.
function Wait-Process-Bounded($proc, [int]$seconds, [string]$what) {
    if (-not $proc.WaitForExit($seconds * 1000)) {
        Shot "hang-$($what -replace '\W', '-')"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        $script:failures.Add("Timeout: $what")
        Write-Host "FAIL  Timeout: $what"
        return $false
    }
    return $true
}

# ---------------------------------------------------------------- 1. silent install
Write-Host "== Install ($Language): $Installer"
$log = Join-Path $Evidence 'setup.log'
$installerArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$log", "/LANG=$Language")
$p = Start-Process -FilePath $Installer -ArgumentList $installerArgs -PassThru
[void](Wait-Process-Bounded $p 240 'silent install')
Check ($p.HasExited -and $p.ExitCode -eq 0) "The silent install succeeds (exit code: $($p.ExitCode))"
$tr = Join-Path $env:TEMP 'LolScrollLimiter-install.log'
if (Test-Path $tr) { Copy-Item $tr (Join-Path $Evidence 'install-transcript.log') -Force } # before the diagnostic run
if (-not (Test-Path (Join-Path $DataDir 'limits.json'))) {
    # The installer runs the system setup hidden; on failure it is re-run with visible output for diagnosis.
    Write-Host '== Diagnosis: running install.ps1 by hand'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $AppDir 'install.ps1') -InstallDir $AppDir *>&1 |
        Tee-Object -FilePath (Join-Path $Evidence 'install-ps1.log') | Out-Host
    Write-Host "install.ps1 exit code: $LASTEXITCODE"
}
Check (Test-Path $TrayExe) 'The tray application is installed'
Check (Test-Path (Join-Path $DataDir 'limits.json')) 'limits.json was created'
Check (-not (Test-Path $AdminJson)) 'There is no admin.json yet after a silent install'
$svc = Get-Service LolScrollLimiter -ErrorAction SilentlyContinue
Check ($null -ne $svc -and $svc.Status -eq 'Running') 'The service is running'
Check ((Get-ItemProperty 'HKLM:\SOFTWARE\LolScrollLimiter').Language -eq $Language) "The installer recorded the language ($Language)"

# ---------------------------------------------------------------- 2. setting the password with the installer's helper command
Write-Host '== Setting the password (installer helper)'
$short = Join-Path $env:TEMP 'lsl-short.dat'
Set-Content -Path $short -Value (Hex 'abc') -Encoding ascii
$r = Run-Helper @('--set-password-file', "`"$short`"")
[void](Wait-Process-Bounded $r 30 'handing over a short password')
Check ($r.ExitCode -eq 3) "A short password is rejected (exit code: $($r.ExitCode))"
Check (-not (Test-Path $AdminJson)) 'A short password does not create admin.json'
Check (-not (Test-Path $short)) 'The hand-over file is deleted (also for a short password)'

$file = Join-Path $env:TEMP 'lsl-admin.dat'
Set-Content -Path $file -Value (Hex $Password) -Encoding ascii
$r = Run-Helper @('--set-password-file', "`"$file`"")
[void](Wait-Process-Bounded $r 30 'handing over the password')
Check ($r.ExitCode -eq 0) "Setting the password succeeds (exit code: $($r.ExitCode))"
Check (-not (Test-Path $file)) 'The hand-over file is deleted'
Check (Test-Path $AdminJson) 'admin.json was created'
if (Test-Path $AdminJson) {
    $json = Get-Content $AdminJson -Raw | ConvertFrom-Json
    Check ($json.salt -and $json.hash -and $json.iterations -ge 100000) 'admin.json: salted PBKDF2 verifier'
    Check (-not ((Get-Content $AdminJson -Raw) -match 'árvíz|arviz')) 'admin.json does not contain the password'
    $acl = (& icacls.exe $AdminJson) -join "`n"
    Set-Content -Path (Join-Path $Evidence 'admin-json.acl.txt') -Value $acl
    Check (-not ($acl -match 'BUILTIN\\Users|Everyone|Authenticated Users')) 'admin.json: normal users have no access'
    Check ($acl -match 'NT AUTHORITY\\SYSTEM' -and $acl -match 'BUILTIN\\Administrators') 'admin.json: SYSTEM and Administrators have access'
}

# ---------------------------------------------------------------- 3. verification dialog (wrong, then correct password)
Write-Host '== Password verification (UI)'
$proc = Run-Helper @('--verify-password')
$win = Find-Window $Ui.Pw 30
Check ($null -ne $win) 'The password prompt appears'
if ($win) {
    Type-Password $win 'rossz-jelszo'
    Click (Find-Named $win 'OK')
    Start-Sleep -Seconds 3
    $win = Find-Window $Ui.Pw 15
    $texts = if ($win) { Texts $win } else { @() }
    Check (($texts -join ' ') -match $Ui.Wrong) 'After a wrong password: "Wrong password (1/5)"'
    Shot 'verify-wrong'
    if ($win) { Click (Find-Named $win $Ui.Cancel) }
}
[void]$proc.WaitForExit(20000)
Check ($proc.HasExited -and $proc.ExitCode -eq 1) "Cancel -> denied (exit code: $($proc.ExitCode))"

$proc = Run-Helper @('--verify-password')
$win = Find-Window $Ui.Pw 30
if ($win) { Type-Password $win $Password; Click (Find-Named $win 'OK') }
[void]$proc.WaitForExit(20000)
Check ($proc.HasExited -and $proc.ExitCode -eq 0) "The correct (accented) password -> allowed (exit code: $($proc.ExitCode))"

# ---------------------------------------------------------------- 4. admin panel
Write-Host '== Admin panel'
$proc = Run-Helper @('--admin')
$win = Find-Window $Ui.Pw 30
Check ($null -ne $win) 'The admin panel asks for the password first'
if ($win) { Type-Password $win $Password; Click (Find-Named $win 'OK') }
$form = Find-Window $Ui.Admin 30 $Ui.SaveExtra
Check ($null -ne $form) 'The admin editor opens'
if ($form) {
    Start-Sleep -Seconds 1
    Shot 'admin-form'

    # Language leak check: no Hungarian text may appear in the English panel (and the other way round, the Hungarian panel must
    # contain its own typical words).
    $names = @($form.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name })
    Set-Content -Path (Join-Path $Evidence 'admin-form-names.txt') -Value $names
    if ($Language -eq 'en') {
        $leak = @($names | Where-Object { $_ -match '[áéíóöőúüűÁÉÍÓÖŐÚÜŰ]|\bmeccs|\bperc\b|PvP-meccsek' })
        if ($leak.Count -gt 0) { Write-Host ('Hungarian text found: ' + ($leak -join ' | ')) }
        Check ($leak.Count -eq 0) 'No Hungarian text in the English admin panel'
    } else {
        Check (@($names | Where-Object { $_ -match 'Mentés|perc|meccs' }).Count -gt 0) 'The Hungarian admin panel is in Hungarian'
        $english = @($names | Where-Object { $_ -match '\b(Save|Cancel|Short videos|Banned hours|matches)\b' })
        if ($english.Count -gt 0) { Write-Host ('English text found: ' + ($english -join ' | ')) }
        Check ($english.Count -eq 0) 'No English text in the Hungarian admin panel'
    }

    $combo = Find-Named $form $Ui.ExtraDay
    Check ($null -ne $combo) 'There is a day chooser'
    if ($combo) {
        try {
            $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Milliseconds 500
            $itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $CT::ListItem)
            $items = @($combo.FindAll($Scope::Descendants, $itemCond) | ForEach-Object { $_.Current.Name })
            Set-Content -Path (Join-Path $Evidence 'day-options.txt') -Value $items
            Check ($items.Count -ge 31) "The day chooser has $($items.Count) days"
            $weekend = @($items | Where-Object { $_ -match $Ui.Weekend })
            Check (($weekend.Count -gt 0) -and (@($weekend | Where-Object { $_ -notmatch $Ui.NotSelectable }).Count -eq 0)) 'Weekends are listed as not selectable'
            Check ($items[0] -match $Ui.Current) 'The first day is the current day'
            $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
        } catch { Write-Host "WARN  the day chooser items cannot be read: $_" }
    }

    $lol = Find-Named $form $Ui.ExtraLol
    try {
        $lol.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(2)
    } catch {
        Write-Host "WARN  RangeValue is not available, using the edit field: $_"
        ($lol.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -eq $CT::Edit } | Select-Object -First 1).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('2')
    }
    Click (Find-Named $form $Ui.SaveExtra)
    Start-Sleep -Seconds 1
    Shot 'admin-form-grant'
    Click (Find-Named $form $Ui.Save)
    $ok = Find-Window $Ui.Admin 20 'OK'
    Check ($null -ne $ok) 'A confirmation appears after saving'
    if ($ok) { Shot 'admin-saved'; Click (Find-Named $ok 'OK') }
}
[void]$proc.WaitForExit(20000)
if (-not $proc.HasExited) { Shot 'admin-stuck'; Stop-Process -Id $proc.Id -Force }

$limitsPath = Join-Path $DataDir 'limits.json'
Copy-Item $limitsPath (Join-Path $Evidence 'limits.after.json')
$limits = Get-Content $limitsPath -Raw | ConvertFrom-Json
Check ($limits.extraGrants.Count -eq 1 -and $limits.extraGrants[0].lolMatches -eq 2) 'limits.json contains the extra allowance (LoL +2)'
Check ($limits.extraGrants.Count -eq 1 -and $limits.extraGrants[0].date -match '^\d{4}-\d{2}-\d{2}$') 'The extra''s day is yyyy-MM-dd'
$e2eIdx = 0
Start-Sleep -Seconds 35
Write-Host '== Service and the new extra'
$svc = Get-Service LolScrollLimiter
Check ($svc.Status -eq 'Running') 'The service keeps running after saving'

# ---------------------------------------------------------------- 4b. protection against stopping and killing
Write-Host '== Protection of the service'
Start-Sleep -Seconds 3
$denied = $false
try { Stop-Service LolScrollLimiter -Force -ErrorAction Stop } catch { $denied = $true }
Check ($denied -and (Get-Service LolScrollLimiter).Status -eq 'Running') 'An administrator cannot stop the service with Stop-Service'
& sc.exe config LolScrollLimiter start= disabled | Out-Null
Check ($LASTEXITCODE -ne 0) 'An administrator cannot disable the service with sc config'
& sc.exe delete LolScrollLimiter | Out-Null
Check ($LASTEXITCODE -ne 0 -and $null -ne (Get-Service LolScrollLimiter -ErrorAction SilentlyContinue)) 'An administrator cannot delete the service with sc delete'

foreach ($round in 1, 2, 3, 4) {
    $before = (Get-Process Limiter.Service).Id
    Stop-Process -Id $before -Force
    $back = $null
    $killedAt = Get-Date
    while (-not $back -and ((Get-Date) - $killedAt).TotalSeconds -lt 15) {
        Start-Sleep -Milliseconds 250
        $p = Get-Process Limiter.Service -ErrorAction SilentlyContinue
        if ($p -and $p.Id -ne $before) { $back = $p }
    }
    $seconds = [math]::Round(((Get-Date) - $killedAt).TotalSeconds, 1)
    Write-Host "   kill $round`: new process after $seconds s"
    Check ($null -ne $back -and $seconds -lt 6) "Killed service restarts quickly (round $round, $seconds s)"
    Start-Sleep -Seconds 2
}

# Re-running the setup over a running, locked service: the same path an update takes. The weekly table comes from the parameter.
Write-Host '== Setup over the locked service, banned hours per day'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $AppDir 'install.ps1') -InstallDir $AppDir -LolBanDays '1-5,,,,22-6,0-24,' *>&1 |
    Tee-Object -FilePath (Join-Path $Evidence 'install-over-locked.log') | Out-Null
Check ($LASTEXITCODE -eq 0) "install.ps1 runs over the locked service (exit code: $LASTEXITCODE)"
$limits = Get-Content $limitsPath -Raw | ConvertFrom-Json
Check ($limits.lolBannedDays.monday.enabled -and $limits.lolBannedDays.monday.to -eq 5) 'Monday''s banned hours are in limits.json'
Check (-not $limits.lolBannedDays.tuesday.enabled) 'Tuesday has no banned hours'
Check ($limits.lolBannedDays.friday.from -eq 22 -and $limits.lolBannedDays.friday.to -eq 6) 'Friday''s period runs past midnight'
Check ($limits.lolBannedDays.saturday.from -eq 0 -and $limits.lolBannedDays.saturday.to -eq 24) 'Saturday is banned all day'
Check (-not $limits.lolBannedDays.sunday.enabled) 'Sunday has no banned hours'
Check ((Get-Service LolScrollLimiter).Status -eq 'Running') 'The service runs after the setup'
$denied = $false
try { Stop-Service LolScrollLimiter -Force -ErrorAction Stop } catch { $denied = $true }
Check $denied 'The permissions are closed again after the setup'

# ---------------------------------------------------------------- 5. uninstall
Write-Host '== Uninstall'
$unins = Get-ChildItem (Join-Path $AppDir 'unins*.exe') | Select-Object -First 1
Check ($null -ne $unins) 'There is an uninstaller'

$proc = Start-Process -FilePath $unins.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$win = Find-Window $Ui.Pw 40
Check ($null -ne $win) 'The uninstaller asks for the password'
if ($win) { Type-Password $win 'nem-jo-jelszo'; Click (Find-Named $win 'OK') }
Start-Sleep -Seconds 3
$win = Find-Window $Ui.Pw 15
if ($win) { Click (Find-Named $win $Ui.Cancel) }
[void](Wait-Process-Bounded $proc 60 'uninstall with a wrong password')
Check ($proc.HasExited -and (Test-Path $TrayExe)) 'Wrong password/Cancel: the uninstall is aborted, the program stays'
Check ((Get-Service LolScrollLimiter -ErrorAction SilentlyContinue) -ne $null) 'The service stays'

$proc = Start-Process -FilePath $unins.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$win = Find-Window $Ui.Pw 40
if ($win) { Type-Password $win $Password; Click (Find-Named $win 'OK') }
[void](Wait-Process-Bounded $proc 120 'uninstall with the correct password')
Start-Sleep -Seconds 3
Check (-not (Test-Path $TrayExe)) 'With the correct password the uninstall runs'
Check ($null -eq (Get-Service LolScrollLimiter -ErrorAction SilentlyContinue)) 'The service was deleted'
Check (-not (Test-Path $AdminJson)) 'admin.json was deleted'

# ---------------------------------------------------------------- summary
Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) failure(s):"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}
Write-Host 'All checks passed.'
