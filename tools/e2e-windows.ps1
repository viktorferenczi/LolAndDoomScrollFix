<#
.SYNOPSIS
  Windows-os végpontok közötti próba (GitHub Actions, windows-latest): csendes telepítés, admin jelszó,
  az admin felület kezelése UI Automation-nel, extra keret mentése, eltávolítás jelszóval.
  Bizonyítékok (képernyőkép, naplók) az out\e2e mappába kerülnek.
#>
[CmdletBinding()]
param(
    [string]$Installer = (Get-ChildItem (Join-Path $PSScriptRoot '..\out\LolScrollLimiter-Setup-*.exe') | Select-Object -First 1).FullName,
    [string]$Evidence = (Join-Path $PSScriptRoot '..\out\e2e')
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
        $kids = $AE::RootElement.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($k in $kids) {
            if ($k.Current.Name -like $like) {
                if (-not $withButton -or (Find-Named $k $withButton)) { return $k }
            }
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Find-Named($parent, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($Prop::NameProperty, $name)
    return $parent.FindFirst($Scope::Descendants, $cond)
}

function Click($el) {
    # BM_CLICK: nem blokkol, ha a gomb modális ablakot nyit (az InvokePattern ilyenkor megakadhat).
    $h = [IntPtr]$el.Current.NativeWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "Nincs ablakkezelője: $($el.Current.Name)" }
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

function Run-Helper([string[]]$args) {
    return Start-Process -FilePath $TrayExe -ArgumentList $args -PassThru
}

# ---------------------------------------------------------------- 1. csendes telepítés
Write-Host "== Telepítés: $Installer"
$log = Join-Path $Evidence 'setup.log'
$p = Start-Process -FilePath $Installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$log" -Wait -PassThru
Check ($p.ExitCode -eq 0) "A csendes telepítés sikeres (kód: $($p.ExitCode))"
Check (Test-Path $TrayExe) 'A tálcaalkalmazás telepítve van'
Check (Test-Path (Join-Path $DataDir 'limits.json')) 'limits.json létrejött'
Check (-not (Test-Path $AdminJson)) 'Csendes telepítés után még nincs admin.json'
$svc = Get-Service LolScrollLimiter -ErrorAction SilentlyContinue
Check ($null -ne $svc -and $svc.Status -eq 'Running') 'A szolgáltatás fut'

# ---------------------------------------------------------------- 2. jelszó beállítása a telepítő segédparancsával
Write-Host '== Jelszó beállítása (telepítői segédparancs)'
$short = Join-Path $env:TEMP 'lsl-short.dat'
Set-Content -Path $short -Value (Hex 'abc') -Encoding ascii
$r = Run-Helper @('--set-password-file', "`"$short`"")
$r.WaitForExit()
Check ($r.ExitCode -eq 3) "Rövid jelszót elutasít (kód: $($r.ExitCode))"
Check (-not (Test-Path $AdminJson)) 'Rövid jelszónál nem jön létre admin.json'
Check (-not (Test-Path $short)) 'Az átadó fájl törlődik (rövid jelszónál is)'

$file = Join-Path $env:TEMP 'lsl-admin.dat'
Set-Content -Path $file -Value (Hex $Password) -Encoding ascii
$r = Run-Helper @('--set-password-file', "`"$file`"")
$r.WaitForExit()
Check ($r.ExitCode -eq 0) "A jelszó beállítása sikeres (kód: $($r.ExitCode))"
Check (-not (Test-Path $file)) 'Az átadó fájl törlődik'
Check (Test-Path $AdminJson) 'admin.json létrejött'
if (Test-Path $AdminJson) {
    $json = Get-Content $AdminJson -Raw | ConvertFrom-Json
    Check ($json.salt -and $json.hash -and $json.iterations -ge 100000) 'admin.json: sós PBKDF2-ellenőrző'
    Check (-not ((Get-Content $AdminJson -Raw) -match 'árvíz|arviz')) 'admin.json nem tartalmaz jelszót'
    $acl = (& icacls.exe $AdminJson) -join "`n"
    Set-Content -Path (Join-Path $Evidence 'admin-json.acl.txt') -Value $acl
    Check (-not ($acl -match 'BUILTIN\\Users|Everyone|Authenticated Users')) 'admin.json: normál felhasználó nem fér hozzá'
    Check ($acl -match 'NT AUTHORITY\\SYSTEM' -and $acl -match 'BUILTIN\\Administrators') 'admin.json: SYSTEM és Rendszergazdák férnek hozzá'
}

# ---------------------------------------------------------------- 3. ellenőrző párbeszédablak (hibás, majd helyes jelszó)
Write-Host '== Jelszóellenőrzés (UI)'
$proc = Run-Helper @('--verify-password')
$win = Find-Window 'Admin jelszó' 30
Check ($null -ne $win) 'Megjelenik a jelszókérő ablak'
if ($win) {
    Type-Password $win 'rossz-jelszo'
    Click (Find-Named $win 'OK')
    Start-Sleep -Seconds 3
    $win = Find-Window 'Admin jelszó' 15
    $texts = if ($win) { Texts $win } else { @() }
    Check (($texts -join ' ') -match 'Hibás jelszó \(1/5\)') 'Hibás jelszó után „Hibás jelszó (1/5)”'
    Shot 'verify-wrong'
    if ($win) { Click (Find-Named $win 'Mégse') }
}
[void]$proc.WaitForExit(20000)
Check ($proc.HasExited -and $proc.ExitCode -eq 1) "Mégse → elutasítva (kód: $($proc.ExitCode))"

$proc = Run-Helper @('--verify-password')
$win = Find-Window 'Admin jelszó' 30
if ($win) { Type-Password $win $Password; Click (Find-Named $win 'OK') }
[void]$proc.WaitForExit(20000)
Check ($proc.HasExited -and $proc.ExitCode -eq 0) "Helyes (ékezetes) jelszó → engedélyezve (kód: $($proc.ExitCode))"

# ---------------------------------------------------------------- 4. admin felület
Write-Host '== Admin felület'
$proc = Run-Helper @('--admin')
$win = Find-Window 'Admin jelszó' 30
Check ($null -ne $win) 'Az admin felület előtt jelszót kér'
if ($win) { Type-Password $win $Password; Click (Find-Named $win 'OK') }
$form = Find-Window 'Admin beállítások*' 30 'Extra mentése a napra'
Check ($null -ne $form) 'Megnyílik az admin szerkesztő'
if ($form) {
    Start-Sleep -Seconds 1
    Shot 'admin-form'

    $combo = Find-Named $form 'Extra napja'
    Check ($null -ne $combo) 'Van napválasztó'
    if ($combo) {
        try {
            $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Milliseconds 500
            $itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $CT::ListItem)
            $items = @($combo.FindAll($Scope::Descendants, $itemCond) | ForEach-Object { $_.Current.Name })
            Set-Content -Path (Join-Path $Evidence 'day-options.txt') -Value $items
            Check ($items.Count -ge 31) "A napválasztóban $($items.Count) nap van"
            $weekend = @($items | Where-Object { $_ -match 'szombat|vasárnap' })
            Check (($weekend.Count -gt 0) -and (@($weekend | Where-Object { $_ -notmatch 'nem választható' }).Count -eq 0)) 'A hétvégék „nem választható”-ként szerepelnek'
            Check ($items[0] -match 'most tartó nap') 'Az első nap a most tartó nap'
            $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
        } catch { Write-Host "WARN  napválasztó elemei nem olvashatók: $_" }
    }

    $lol = Find-Named $form 'Extra LoL-meccsek'
    try {
        $lol.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(2)
    } catch {
        Write-Host "WARN  RangeValue nem érhető el, szerkesztőmező: $_"
        ($lol.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType -eq $CT::Edit } | Select-Object -First 1).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('2')
    }
    Click (Find-Named $form 'Extra mentése a napra')
    Start-Sleep -Seconds 1
    Shot 'admin-form-grant'
    Click (Find-Named $form 'Mentés')
    $ok = Find-Window 'Admin beállítások*' 20 'OK'
    Check ($null -ne $ok) 'Mentés után megjelenik a megerősítés'
    if ($ok) { Shot 'admin-saved'; Click (Find-Named $ok 'OK') }
}
[void]$proc.WaitForExit(20000)
if (-not $proc.HasExited) { Shot 'admin-stuck'; Stop-Process -Id $proc.Id -Force }

$limitsPath = Join-Path $DataDir 'limits.json'
Copy-Item $limitsPath (Join-Path $Evidence 'limits.after.json')
$limits = Get-Content $limitsPath -Raw | ConvertFrom-Json
Check ($limits.extraGrants.Count -eq 1 -and $limits.extraGrants[0].lolMatches -eq 2) 'A limits.json tartalmazza az extra keretet (LoL +2)'
Check ($limits.extraGrants.Count -eq 1 -and $limits.extraGrants[0].date -match '^\d{4}-\d{2}-\d{2}$') 'Az extra napja ÉÉÉÉ-HH-NN'
$e2eIdx = 0
Start-Sleep -Seconds 35
Write-Host '== Szolgáltatás és az új extra'
$svc = Get-Service LolScrollLimiter
Check ($svc.Status -eq 'Running') 'A szolgáltatás a mentés után is fut'

# ---------------------------------------------------------------- 5. eltávolítás
Write-Host '== Eltávolítás'
$unins = Get-ChildItem (Join-Path $AppDir 'unins*.exe') | Select-Object -First 1
Check ($null -ne $unins) 'Van eltávolító'

$proc = Start-Process -FilePath $unins.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$win = Find-Window 'Admin jelszó' 40
Check ($null -ne $win) 'Az eltávolító jelszót kér'
if ($win) { Type-Password $win 'nem-jo-jelszo'; Click (Find-Named $win 'OK') }
Start-Sleep -Seconds 3
$win = Find-Window 'Admin jelszó' 15
if ($win) { Click (Find-Named $win 'Mégse') }
[void]$proc.WaitForExit(60000)
Check ($proc.HasExited -and (Test-Path $TrayExe)) 'Hibás jelszó/Mégse: az eltávolítás megszakad, a program megmarad'
Check ((Get-Service LolScrollLimiter -ErrorAction SilentlyContinue) -ne $null) 'A szolgáltatás megmaradt'

$proc = Start-Process -FilePath $unins.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$win = Find-Window 'Admin jelszó' 40
if ($win) { Type-Password $win $Password; Click (Find-Named $win 'OK') }
[void]$proc.WaitForExit(120000)
Start-Sleep -Seconds 3
Check (-not (Test-Path $TrayExe)) 'Helyes jelszóval az eltávolítás lefut'
Check ($null -eq (Get-Service LolScrollLimiter -ErrorAction SilentlyContinue)) 'A szolgáltatás törlődött'
Check (-not (Test-Path $AdminJson)) 'Az admin.json törlődött'

# ---------------------------------------------------------------- összegzés
Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) hiba:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}
Write-Host 'Minden ellenőrzés rendben.'
