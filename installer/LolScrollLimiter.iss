; LoL- és görgetéskorlátozó — Windows-telepítő (Inno Setup 6)
; Fordítás: installer\build.ps1 (előtte a programfájlok az out\app mappába kerülnek).

#define AppName "LoL- és görgetéskorlátozó"
#define AppVersion "1.6.0"
#define DevExtensionId "iidlgdeiobckahalgoabbobfdlefkagi"
#define StoreExtensionId "mkoeoadekdfnbjpmhiabpjlmhdoimcml"

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
; Frissítéskor (csendes telepítés) a telepítő leállította a tálcaalkalmazást, ezért újraindítjuk.
Filename: "{app}\Limiter.Tray.exe"; Flags: nowait runasoriginaluser; Check: IsUpdate

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "LimiterUninstall"

[Code]
var
  ExtensionPage: TInputQueryWizardPage;
  PasswordPage: TInputQueryWizardPage;
  ExistingPasswordChecked: Boolean;
  ModePage: TInputOptionWizardPage;
  LimitsPage: TWizardPage;
  RollingRadio, DailyRadio: TNewRadioButton;
  ResetHourCombo: TNewComboBox;
  ShortVideoEdit, FeedEdit: TNewEdit;
  WeekendCheck: TNewCheckBox;
  BonusPage: TWizardPage;
  LolBonusEdit, ShortVideoBonusEdit, FeedBonusEdit: TNewEdit;
  BanPage: TWizardPage;
  LolBanCheck, ShortVideoBanCheck, FeedBanCheck: TNewCheckBox;
  LolBanFrom, LolBanTo, ShortVideoBanFrom, ShortVideoBanTo, FeedBanFrom, FeedBanTo: TNewComboBox;

procedure ResetModeChanged(Sender: TObject);
begin
  ResetHourCombo.Enabled := DailyRadio.Checked;
end;

function AddLabel(Parent: TWizardPage; const Caption: String; Left, Top: Integer): TNewStaticText;
begin
  Result := TNewStaticText.Create(Parent);
  Result.Parent := Parent.Surface;
  Result.Caption := Caption;
  Result.Left := Left;
  Result.Top := Top;
end;

function AddNumberEdit(Parent: TWizardPage; const Caption: String; Top: Integer; const DefaultText, UnitText: String): TNewEdit;
begin
  AddLabel(Parent, Caption, 0, Top + ScaleY(3));
  Result := TNewEdit.Create(Parent);
  Result.Parent := Parent.Surface;
  Result.Left := ScaleX(230);
  Result.Top := Top;
  Result.Width := ScaleX(60);
  Result.Text := DefaultText;
  AddLabel(Parent, UnitText, ScaleX(298), Top + ScaleY(3));
end;

function AddMinutesEdit(Parent: TWizardPage; const Caption: String; Top: Integer): TNewEdit;
begin
  Result := AddNumberEdit(Parent, Caption, Top, '60', 'perc');
end;

function TwoDigits(Value: Integer): String;
begin
  Result := IntToStr(Value);
  if Value < 10 then
    Result := '0' + Result;
end;

procedure CreateLimitsPage(AfterID: Integer);
var
  I, Y: Integer;
  Heading: TNewStaticText;
begin
  LimitsPage := CreateCustomPage(AfterID, 'Korlátok', 'Mikor nullázódjanak a keretek, és mekkorák legyenek?');
  Y := 0;

  Heading := AddLabel(LimitsPage, 'A keretek visszaállítása:', 0, Y);
  Heading.Font.Style := [fsBold];
  Y := Y + ScaleY(22);

  RollingRadio := TNewRadioButton.Create(LimitsPage);
  RollingRadio.Parent := LimitsPage.Surface;
  RollingRadio.Top := Y;
  RollingRadio.Width := LimitsPage.SurfaceWidth;
  RollingRadio.Caption := 'Gördülő 24 óra (minden felhasználás 24 órával később szabadul fel)';
  RollingRadio.Checked := True;
  RollingRadio.OnClick := @ResetModeChanged;
  Y := Y + ScaleY(24);

  DailyRadio := TNewRadioButton.Create(LimitsPage);
  DailyRadio.Parent := LimitsPage.Surface;
  DailyRadio.Top := Y + ScaleY(2);
  DailyRadio.Width := ScaleX(220);
  DailyRadio.Caption := 'Naponta nullázás ebben az órában:';
  DailyRadio.OnClick := @ResetModeChanged;

  ResetHourCombo := TNewComboBox.Create(LimitsPage);
  ResetHourCombo.Parent := LimitsPage.Surface;
  ResetHourCombo.Style := csDropDownList;
  ResetHourCombo.Left := ScaleX(230);
  ResetHourCombo.Top := Y;
  ResetHourCombo.Width := ScaleX(80);
  for I := 0 to 23 do
    ResetHourCombo.Items.Add(TwoDigits(I) + ':00');
  ResetHourCombo.ItemIndex := 3;
  ResetHourCombo.Enabled := False;
  Y := Y + ScaleY(40);

  Heading := AddLabel(LimitsPage, 'Időkeretek:', 0, Y);
  Heading.Font.Style := [fsBold];
  Y := Y + ScaleY(22);
  ShortVideoEdit := AddMinutesEdit(LimitsPage, 'Rövid videók (Reels, TikTok, Shorts):', Y);
  Y := Y + ScaleY(28);
  FeedEdit := AddMinutesEdit(LimitsPage, 'Facebook kezdőlapi hírfolyam:', Y);
  Y := Y + ScaleY(40);

  WeekendCheck := TNewCheckBox.Create(LimitsPage);
  WeekendCheck.Parent := LimitsPage.Surface;
  WeekendCheck.Top := Y;
  WeekendCheck.Width := LimitsPage.SurfaceWidth;
  WeekendCheck.Caption := 'A korlátok hétvégén (szombaton és vasárnap) is érvényesek';
  WeekendCheck.Checked := False;
  Y := Y + ScaleY(22);
  Heading := AddLabel(LimitsPage, 'Ha nincs bejelölve, hétvégén nincs korlát, és a hétvégi használat nem számít bele a keretbe.', ScaleX(18), Y);
  Heading.AutoSize := False;
  Heading.WordWrap := True;
  Heading.Width := LimitsPage.SurfaceWidth - ScaleX(18);
  Heading.Height := ScaleY(32);
end;

function NumberInRange(Edit: TNewEdit; Max: Integer): Boolean;
var
  Value: Integer;
begin
  Value := StrToIntDef(Trim(Edit.Text), -1);
  Result := (Value >= 0) and (Value <= Max);
end;

function MinutesValid(Edit: TNewEdit): Boolean;
begin
  Result := NumberInRange(Edit, 1440);
end;

procedure CreateBonusPage(AfterID: Integer);
var
  Y: Integer;
  Note: TNewStaticText;
begin
  BonusPage := CreateCustomPage(AfterID, 'Pihenőnap-jutalom',
    'Mennyivel nőjön a keret egy kihagyott nap után?');

  Note := AddLabel(BonusPage,
    'Ha egy napon egy keretből semmi sem fogy (0 meccs, illetve 0 perc), másnap ahhoz ennyivel több jár. ' +
    'A jutalom nem halmozódik: több üres nap után is csak egyszer jár. 0 = kikapcsolva.', 0, 0);
  Note.AutoSize := False;
  Note.WordWrap := True;
  Note.Width := BonusPage.SurfaceWidth;
  Note.Height := ScaleY(48);
  Y := ScaleY(56);

  LolBonusEdit := AddNumberEdit(BonusPage, 'LoL PvP:', Y, '1', 'meccs');
  Y := Y + ScaleY(28);
  ShortVideoBonusEdit := AddNumberEdit(BonusPage, 'Rövid videók (Reels, TikTok, Shorts):', Y, '30', 'perc');
  Y := Y + ScaleY(28);
  FeedBonusEdit := AddNumberEdit(BonusPage, 'Facebook kezdőlapi hírfolyam:', Y, '30', 'perc');
end;

function MarkerKey: String;
begin
  Result := 'SOFTWARE\LolScrollLimiter';
end;

function HasExistingInstall: Boolean;
begin
  Result := RegValueExists(HKLM, MarkerKey, 'ExtensionId');
end;

// Frissítés: a tálcaalkalmazás /UPDATE kapcsolóval indítja a telepítőt. Csak meglévő telepítésen értelmezzük;
// ilyenkor a beállításokat nem kérdezzük újra, és a meglévő korlátok megmaradnak.
function IsUpdate: Boolean;
var
  I: Integer;
begin
  Result := False;
  if not HasExistingInstall then
    Exit;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/UPDATE') = 0 then
      Result := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if IsUpdate then
    Result := (PageID = ModePage.ID) or (PageID = ExtensionPage.ID) or (PageID = LimitsPage.ID) or
              (PageID = BonusPage.ID) or (PageID = BanPage.ID) or (PageID = PasswordPage.ID);
end;

procedure BanCheckChanged(Sender: TObject);
begin
  LolBanFrom.Enabled := LolBanCheck.Checked;
  LolBanTo.Enabled := LolBanCheck.Checked;
  ShortVideoBanFrom.Enabled := ShortVideoBanCheck.Checked;
  ShortVideoBanTo.Enabled := ShortVideoBanCheck.Checked;
  FeedBanFrom.Enabled := FeedBanCheck.Checked;
  FeedBanTo.Enabled := FeedBanCheck.Checked;
end;

function AddHourCombo(Parent: TWizardPage; Left, Top, Hour: Integer): TNewComboBox;
var
  I: Integer;
begin
  Result := TNewComboBox.Create(Parent);
  Result.Parent := Parent.Surface;
  Result.Style := csDropDownList;
  Result.Left := Left;
  Result.Top := Top;
  Result.Width := ScaleX(70);
  for I := 0 to 23 do
    Result.Items.Add(TwoDigits(I) + ':00');
  Result.ItemIndex := Hour;
  Result.Enabled := False;
end;

function AddBanCheck(Parent: TWizardPage; const Caption: String; Top: Integer): TNewCheckBox;
begin
  Result := TNewCheckBox.Create(Parent);
  Result.Parent := Parent.Surface;
  Result.Top := Top + ScaleY(2);
  Result.Width := ScaleX(220);
  Result.Caption := Caption;
  Result.Checked := False;
  Result.OnClick := @BanCheckChanged;
end;

procedure CreateBanPage(AfterID: Integer);
var
  Y: Integer;
  Note: TNewStaticText;
begin
  BanPage := CreateCustomPage(AfterID, 'Tiltott órák',
    'Mely órákban ne lehessen játszani vagy görgetni?');

  Note := AddLabel(BanPage,
    'Bejelölve a megadott órák között (pl. 01:00–05:00) az adott dolog egyáltalán nem használható, akkor sem, ' +
    'ha a keretből még maradt. A keretet és a nullázást nem érinti. Átnyúlhat éjfélen (pl. 22:00–06:00). ' +
    'Ha hétvégén nincs korlát, akkor hétvégén tiltás sincs.', 0, 0);
  Note.AutoSize := False;
  Note.WordWrap := True;
  Note.Width := BanPage.SurfaceWidth;
  Note.Height := ScaleY(56);
  Y := ScaleY(66);

  AddLabel(BanPage, 'kezdete', ScaleX(230), Y);
  AddLabel(BanPage, 'vége', ScaleX(330), Y);
  Y := Y + ScaleY(20);

  LolBanCheck := AddBanCheck(BanPage, 'LoL PvP-meccsek', Y);
  LolBanFrom := AddHourCombo(BanPage, ScaleX(230), Y, 1);
  AddLabel(BanPage, '–', ScaleX(310), Y + ScaleY(3));
  LolBanTo := AddHourCombo(BanPage, ScaleX(330), Y, 5);
  Y := Y + ScaleY(30);

  ShortVideoBanCheck := AddBanCheck(BanPage, 'Rövid videók', Y);
  ShortVideoBanFrom := AddHourCombo(BanPage, ScaleX(230), Y, 1);
  AddLabel(BanPage, '–', ScaleX(310), Y + ScaleY(3));
  ShortVideoBanTo := AddHourCombo(BanPage, ScaleX(330), Y, 5);
  Y := Y + ScaleY(30);

  FeedBanCheck := AddBanCheck(BanPage, 'Facebook-hírfolyam', Y);
  FeedBanFrom := AddHourCombo(BanPage, ScaleX(230), Y, 1);
  AddLabel(BanPage, '–', ScaleX(310), Y + ScaleY(3));
  FeedBanTo := AddHourCombo(BanPage, ScaleX(330), Y, 5);
end;

function BanValid(Check: TNewCheckBox; FromCombo, ToCombo: TNewComboBox): Boolean;
begin
  Result := not Check.Checked or (FromCombo.ItemIndex <> ToCombo.ItemIndex);
end;

function BanParams(const Name: String; Check: TNewCheckBox; FromCombo, ToCombo: TNewComboBox): String;
begin
  Result := '';
  if Check.Checked then
    Result := ' -' + Name + 'BanFrom ' + IntToStr(FromCombo.ItemIndex) + ' -' + Name + 'BanTo ' + IntToStr(ToCombo.ItemIndex);
end;

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
    'Alapértelmezés: a Chrome Web Store-ban közzétett bővítmény azonosítója (éles mód) vagy a fejlesztői azonosító (fejlesztői mód). Csak akkor módosítsd, ha tudod, mit csinálsz.');
  ExtensionPage.Add('Bővítményazonosító:', False);
  ExtensionPage.Values[0] := '{#StoreExtensionId}';

  CreateLimitsPage(ExtensionPage.ID);
  CreateBonusPage(LimitsPage.ID);
  CreateBanPage(BonusPage.ID);

  PasswordPage := CreateInputQueryPage(BanPage.ID,
    'Admin jelszó', 'Jelszó a beállításokhoz és az eltávolításhoz',
    'Ez a jelszó kell az admin beállítások megnyitásához és a program eltávolításához (a Windows rendszergazdai jóváhagyása mellett). ' +
    'Legalább 6 karakter. Jegyezd meg, vagy add át a megbízható személynek.');
  PasswordPage.Add('Jelszó:', True);
  PasswordPage.Add('Jelszó újra:', True);
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

procedure CurPageChanged(CurPageID: Integer);
var
  Current: String;
begin
  // Az alapértelmezett azonosítót a választott módhoz igazítjuk; a kézzel beírt értéket nem bántjuk.
  if CurPageID = ExtensionPage.ID then
  begin
    Current := Lowercase(Trim(ExtensionPage.Values[0]));
    if (Current = '{#DevExtensionId}') or (Current = '{#StoreExtensionId}') then
    begin
      if ModePage.Values[1] then
        ExtensionPage.Values[0] := '{#DevExtensionId}'
      else
        ExtensionPage.Values[0] := '{#StoreExtensionId}';
    end;
  end;
end;

// Ha már van telepített változat jelszóval, újratelepítéshez (nem frissítéshez) annak jelszava kell.
function ExistingPasswordOk: Boolean;
var
  Code: Integer;
  Helper: String;
begin
  Result := True;
  if ExistingPasswordChecked then Exit;
  if not FileExists(ExpandConstant('{commonappdata}\LolScrollLimiter\admin.json')) then Exit;
  Helper := ExpandConstant('{app}\Limiter.Tray.exe');
  if not FileExists(Helper) then Exit;
  MsgBox('A számítógépen már van telepített változat admin jelszóval. Az újratelepítéshez add meg a jelenlegi jelszót.',
    mbInformation, MB_OK);
  if Exec(Helper, '--verify-password', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) and (Code = 0) then
    ExistingPasswordChecked := True
  else
  begin
    MsgBox('A jelenlegi jelszó nem ellenőrizhető, ezért a telepítés nem folytatható.', mbError, MB_OK);
    Result := False;
  end;
end;

// Az ideiglenes fájlba minden karakter négy hexa számjegyként kerül (UTF-16 kód), így az ékezetek sem romlanak el.
function HexDigit(N: Integer): String;
begin
  Result := Copy('0123456789abcdef', N + 1, 1);
end;

function EncodePassword(const S: String): String;
var
  I, C: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
  begin
    C := Ord(S[I]);
    Result := Result + HexDigit((C shr 12) and 15) + HexDigit((C shr 8) and 15) +
      HexDigit((C shr 4) and 15) + HexDigit(C and 15);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PasswordPage.ID then
  begin
    if Trim(PasswordPage.Values[0]) = '' then
      MsgBox('Add meg a jelszót.', mbError, MB_OK)
    else if Length(PasswordPage.Values[0]) < 6 then
      MsgBox('A jelszó legalább 6 karakter legyen.', mbError, MB_OK)
    else if Length(PasswordPage.Values[0]) > 128 then
      MsgBox('A jelszó legfeljebb 128 karakter lehet.', mbError, MB_OK)
    else if PasswordPage.Values[0] <> PasswordPage.Values[1] then
      MsgBox('A két jelszó nem egyezik.', mbError, MB_OK)
    else
    begin
      Result := ExistingPasswordOk;
      Exit;
    end;
    Result := False;
    Exit;
  end;
  if CurPageID = LimitsPage.ID then
  begin
    if not MinutesValid(ShortVideoEdit) or not MinutesValid(FeedEdit) then
    begin
      MsgBox('Az időkeret 0 és 1440 közötti egész szám (perc) lehet.', mbError, MB_OK);
      Result := False;
    end;
    Exit;
  end;
  if CurPageID = BanPage.ID then
  begin
    if not BanValid(LolBanCheck, LolBanFrom, LolBanTo) or
       not BanValid(ShortVideoBanCheck, ShortVideoBanFrom, ShortVideoBanTo) or
       not BanValid(FeedBanCheck, FeedBanFrom, FeedBanTo) then
    begin
      MsgBox('A tiltott időszak kezdete és vége nem lehet ugyanaz az óra.', mbError, MB_OK);
      Result := False;
    end;
    Exit;
  end;
  if CurPageID = BonusPage.ID then
  begin
    if not NumberInRange(LolBonusEdit, 10) then
    begin
      MsgBox('A LoL-jutalom 0 és 10 közötti egész szám (meccs) lehet.', mbError, MB_OK);
      Result := False;
    end
    else if not MinutesValid(ShortVideoBonusEdit) or not MinutesValid(FeedBonusEdit) then
    begin
      MsgBox('A percben megadott jutalom 0 és 1440 közötti egész szám lehet.', mbError, MB_OK);
      Result := False;
    end;
    Exit;
  end;
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
  DevModeValue: Cardinal;
  Params, ExtId, PwFile: String;
begin
  if CurStep = ssPostInstall then
  begin
    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\install.ps1') + '"' +
      ' -InstallDir "' + ExpandConstant('{app}') + '"';
    if IsUpdate then
    begin
      // Frissítés: a bővítményazonosító és a mód a korábbi telepítésből jön, a korlátok megmaradnak.
      if RegQueryStringValue(HKLM, MarkerKey, 'ExtensionId', ExtId) then
        Params := Params + ' -ExtensionId ' + ExtId;
      if RegQueryDWordValue(HKLM, MarkerKey, 'DevMode', DevModeValue) and (DevModeValue = 1) then
        Params := Params + ' -DevMode';
      Params := Params + ' -KeepSettings';
    end
    else
    begin
      Params := Params + ' -ExtensionId ' + ExtensionPage.Values[0];
      // Csendes telepítésnél nincs jelszó-oldal: ilyenkor az első admin-megnyitáskor kell jelszót beállítani.
      if PasswordPage.Values[0] <> '' then
      begin
        PwFile := ExpandConstant('{tmp}\lsl-admin.dat');
        SaveStringToFile(PwFile, EncodePassword(PasswordPage.Values[0]), False);
        Params := Params + ' -AdminPasswordFile "' + PwFile + '"';
      end;
      if ModePage.Values[1] then
        Params := Params + ' -DevMode';
      if DailyRadio.Checked then
        Params := Params + ' -ResetMode daily -DailyResetHour ' + IntToStr(ResetHourCombo.ItemIndex)
      else
        Params := Params + ' -ResetMode rolling';
      Params := Params + ' -ShortVideoMinutes ' + IntToStr(StrToInt(Trim(ShortVideoEdit.Text))) +
        ' -FacebookFeedMinutes ' + IntToStr(StrToInt(Trim(FeedEdit.Text)));
      if WeekendCheck.Checked then
        Params := Params + ' -EnforceOnWeekends';
      Params := Params +
        ' -LolRestDayBonusMatches ' + IntToStr(StrToInt(Trim(LolBonusEdit.Text))) +
        ' -ShortVideoRestDayBonusMinutes ' + IntToStr(StrToInt(Trim(ShortVideoBonusEdit.Text))) +
        ' -FacebookFeedRestDayBonusMinutes ' + IntToStr(StrToInt(Trim(FeedBonusEdit.Text))) +
        BanParams('Lol', LolBanCheck, LolBanFrom, LolBanTo) +
        BanParams('ShortVideo', ShortVideoBanCheck, ShortVideoBanFrom, ShortVideoBanTo) +
        BanParams('FacebookFeed', FeedBanCheck, FeedBanFrom, FeedBanTo);
    end;
    WizardForm.StatusLabel.Caption := 'Szolgáltatás, jogosultságok és Chrome-szabályok beállítása…';
    if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      MsgBox('A rendszerbeállítás sikertelen (kód: ' + IntToStr(Code) + '). ' +
        'Futtasd rendszergazdaként kézzel: ' + ExpandConstant('{app}\install.ps1'), mbError, MB_OK);
    if PwFile <> '' then DeleteFile(PwFile);
  end;
end;

// Eltávolítás előtt az admin jelszó kell (ha van beállítva); a Limiter.Tray.exe ellenőrzi.
function InitializeUninstall(): Boolean;
var
  Code: Integer;
  Helper: String;
begin
  Result := True;
  Helper := ExpandConstant('{app}\Limiter.Tray.exe');
  if not FileExists(Helper) then Exit;
  if not Exec(Helper, '--verify-password', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) or (Code <> 0) then
  begin
    MsgBox('Az eltávolításhoz az admin jelszó szükséges. A program nem lett eltávolítva.', mbInformation, MB_OK);
    Result := False;
  end;
end;
