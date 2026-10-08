; LoL- és görgetéskorlátozó — Windows-telepítő (Inno Setup 6)
; Fordítás: installer\build.ps1 (előtte a programfájlok az out\app mappába kerülnek).

#define AppName "LoL- és görgetéskorlátozó"
#define AppVersion "1.0.0"
#define DevExtensionId "iidlgdeiobckahalgoabbobfdlefkagi"

[Setup]
AppId={{6E0C3D52-8B7A-4F1E-9C55-2A4B7D1E9F30}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=LolScrollLimiter
DefaultDirName={autopf}\LolScrollLimiter
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\out
OutputBaseFilename=LolScrollLimiter-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
CloseApplications=no

[Languages]
Name: "hu"; MessagesFile: "compiler:Languages\Hungarian.isl"

[Files]
Source: "..\out\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Run]
Filename: "{app}\Limiter.Tray.exe"; Description: "Tálcaalkalmazás indítása"; Flags: nowait postinstall runasoriginaluser skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "LimiterUninstall"

[Code]
var
  ExtensionPage: TInputQueryWizardPage;
  ModePage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpWelcome,
    'Telepítési mód', 'Éles vagy fejlesztői tesztverzió?',
    'Éles módban a Chrome-bővítmény kötelezően települ, nem kapcsolható ki, és a bővítmények fejlesztői módja tiltva lesz. ' +
    'Ehhez a bővítménynek a Chrome Web Store-ban közzétettnek kell lennie.' + #13#10 + #13#10 +
    'Fejlesztői tesztverziónál a bővítményt kézzel, kicsomagolva kell betölteni; ez a mód nem nyújt valódi védelmet.',
    True, False);
  ModePage.Add('Éles (áruházi bővítmény, kötelező telepítés)');
  ModePage.Add('Fejlesztői tesztverzió (kicsomagolt bővítmény)');
  ModePage.Values[0] := True;

  ExtensionPage := CreateInputQueryPage(ModePage.ID,
    'Chrome-bővítmény', 'A bővítmény azonosítója',
    'Éles módban a Chrome Web Store által adott 32 betűs azonosító. Fejlesztői módban hagyd az alapértelmezettet.');
  ExtensionPage.Add('Bővítményazonosító:', False);
  ExtensionPage.Values[0] := '{#DevExtensionId}';
end;

function IsValidExtensionId(const Id: String): Boolean;
var
  I: Integer;
begin
  Result := Length(Id) = 32;
  if Result then
    for I := 1 to 32 do
      if (Id[I] < 'a') or (Id[I] > 'p') then
      begin
        Result := False;
        Exit;
      end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ExtensionPage.ID then
  begin
    ExtensionPage.Values[0] := Lowercase(Trim(ExtensionPage.Values[0]));
    if not IsValidExtensionId(ExtensionPage.Values[0]) then
    begin
      MsgBox('A bővítményazonosító 32 karakter hosszú, és csak a–p betűket tartalmazhat.', mbError, MB_OK);
      Result := False;
    end
    else if ModePage.Values[0] and (ExtensionPage.Values[0] = '{#DevExtensionId}') then
      Result := MsgBox('Éles módban az áruházi azonosítóra van szükség; a megadott érték a fejlesztői azonosító. Folytatod így?',
        mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  // Frissítéskor a futó fájlokat el kell engedni.
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop LolScrollLimiter', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Limiter.Tray.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(2000);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  Params: String;
begin
  if CurStep = ssPostInstall then
  begin
    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\install.ps1') + '"' +
      ' -InstallDir "' + ExpandConstant('{app}') + '"' +
      ' -ExtensionId ' + ExtensionPage.Values[0];
    if ModePage.Values[1] then
      Params := Params + ' -DevMode';
    WizardForm.StatusLabel.Caption := 'Szolgáltatás, jogosultságok és Chrome-szabályok beállítása…';
    if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      MsgBox('A rendszerbeállítás sikertelen (kód: ' + IntToStr(Code) + '). ' +
        'Futtasd rendszergazdaként kézzel: ' + ExpandConstant('{app}\install.ps1'), mbError, MB_OK);
  end;
end;
