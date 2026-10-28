; LoL & Doomscroll Limiter — Windows installer (Inno Setup 6), English and Hungarian.
; Build: installer\build.ps1 (the program files must be in out\app first).

#define AppName "LoL & Doomscroll Limiter"
#define AppVersion "1.6.1"
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
ShowLanguageDialog=auto
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
; English is listed first: it is the fallback. The language follows the Windows display language; if that is neither
; English nor Hungarian, the language dialog is shown. /LANG=hu forces Hungarian.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "hu"; MessagesFile: "compiler:Languages\Hungarian.isl"

[Files]
Source: "..\out\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Run]
Filename: "{app}\Limiter.Tray.exe"; Description: "{cm:LaunchTray}"; Flags: nowait postinstall runasoriginaluser skipifsilent
; On update (silent install) the installer stopped the tray application, so it is started again.
Filename: "{app}\Limiter.Tray.exe"; Flags: nowait runasoriginaluser; Check: IsUpdate

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "LimiterUninstall"

[CustomMessages]
en.LaunchTray=Start the tray application
hu.LaunchTray=Tálcaalkalmazás indítása
en.UnitMinutes=min
hu.UnitMinutes=perc
en.LimitsTitle=Limits
hu.LimitsTitle=Korlátok
en.LimitsSubtitle=When should the limits reset, and how large should they be?
hu.LimitsSubtitle=Mikor nullázódjanak a keretek, és mekkorák legyenek?
en.ResetHeading=Resetting the limits:
hu.ResetHeading=A keretek visszaállítása:
en.RollingCaption=Rolling 24 hours (every use is released exactly 24 hours later)
hu.RollingCaption=Gördülő 24 óra (minden felhasználás 24 órával később szabadul fel)
en.DailyCaption=Reset daily at this hour:
hu.DailyCaption=Naponta nullázás ebben az órában:
en.TimeLimitsHeading=Time limits:
hu.TimeLimitsHeading=Időkeretek:
en.ShortVideoLabel=Short videos (Reels, TikTok, Shorts):
hu.ShortVideoLabel=Rövid videók (Reels, TikTok, Shorts):
en.FeedLabel=Facebook home feed:
hu.FeedLabel=Facebook kezdőlapi hírfolyam:
en.WeekendCaption=The limits also apply on weekends (Saturday and Sunday)
hu.WeekendCaption=A korlátok hétvégén (szombaton és vasárnap) is érvényesek
en.WeekendNote=If unticked, there is no limit on weekends, and weekend use does not count toward the limit.
hu.WeekendNote=Ha nincs bejelölve, hétvégén nincs korlát, és a hétvégi használat nem számít bele a keretbe.
en.BonusTitle=Rest-day bonus
hu.BonusTitle=Pihenőnap-jutalom
en.BonusSubtitle=How much should a limit grow after a day without use?
hu.BonusSubtitle=Mennyivel nőjön a keret egy kihagyott nap után?
en.BonusNote=If nothing is used from a limit on a day (0 matches or 0 minutes), the next day it grows by this much. The bonus does not stack: it is given only once even after several empty days. 0 = off.
hu.BonusNote=Ha egy napon egy keretből semmi sem fogy (0 meccs, illetve 0 perc), másnap ahhoz ennyivel több jár. A jutalom nem halmozódik: több üres nap után is csak egyszer jár. 0 = kikapcsolva.
en.UnitMatches=matches
hu.UnitMatches=meccs
en.BanTitle=Banned hours
hu.BanTitle=Tiltott órák
en.BanSubtitle=During which hours should playing or scrolling be impossible?
hu.BanSubtitle=Mely órákban ne lehessen játszani vagy görgetni?
en.BanNote=When ticked, that activity cannot be used at all between the given hours (e.g. 01:00–05:00), even if some of the limit is left. The limit and the reset are not affected. The period may span midnight (e.g. 22:00–06:00). If there is no limit on weekends, there is no ban on weekends either.
hu.BanNote=Bejelölve a megadott órák között (pl. 01:00–05:00) az adott dolog egyáltalán nem használható, akkor sem, ha a keretből még maradt. A keretet és a nullázást nem érinti. Átnyúlhat éjfélen (pl. 22:00–06:00). Ha hétvégén nincs korlát, akkor hétvégén tiltás sincs.
en.BanStart=start
hu.BanStart=kezdete
en.BanEnd=end
hu.BanEnd=vége
en.BanLol=LoL PvP matches
hu.BanLol=LoL PvP-meccsek
en.BanShort=Short videos
hu.BanShort=Rövid videók
en.BanFeed=Facebook feed
hu.BanFeed=Facebook-hírfolyam
en.ModeTitle=Installation mode
hu.ModeTitle=Telepítési mód
en.ModeSubtitle=Production or development test version?
hu.ModeSubtitle=Éles vagy fejlesztői tesztverzió?
en.ModeDesc=In production mode the Chrome extension is installed by force, cannot be turned off, and the extensions' developer mode is disabled. This needs the extension to be published in the Chrome Web Store.%n%nIn the development test version the extension has to be loaded manually, unpacked; this mode gives no real protection.
hu.ModeDesc=Éles módban a Chrome-bővítmény kötelezően települ, nem kapcsolható ki, és a bővítmények fejlesztői módja tiltva lesz. Ehhez a bővítménynek a Chrome Web Store-ban közzétettnek kell lennie.%n%nFejlesztői tesztverziónál a bővítményt kézzel, kicsomagolva kell betölteni; ez a mód nem nyújt valódi védelmet.
en.ModeProd=Production (store extension, forced installation)
hu.ModeProd=Éles (áruházi bővítmény, kötelező telepítés)
en.ModeDev=Development test version (unpacked extension)
hu.ModeDev=Fejlesztői tesztverzió (kicsomagolt bővítmény)
en.ExtTitle=Chrome extension
hu.ExtTitle=Chrome-bővítmény
en.ExtSubtitle=The extension's ID
hu.ExtSubtitle=A bővítmény azonosítója
en.ExtDesc=Default: the ID of the extension published in the Chrome Web Store (production mode) or the development ID (development mode). Change it only if you know what you are doing.
hu.ExtDesc=Alapértelmezés: a Chrome Web Store-ban közzétett bővítmény azonosítója (éles mód) vagy a fejlesztői azonosító (fejlesztői mód). Csak akkor módosítsd, ha tudod, mit csinálsz.
en.ExtLabel=Extension ID:
hu.ExtLabel=Bővítményazonosító:
en.PwTitle=Admin password
hu.PwTitle=Admin jelszó
en.PwSubtitle=Password for the settings and for uninstalling
hu.PwSubtitle=Jelszó a beállításokhoz és az eltávolításhoz
en.PwDesc=This password is needed to open the admin settings and to uninstall the program (in addition to the Windows administrator approval). At least 6 characters. Remember it, or hand it to the trusted person.
hu.PwDesc=Ez a jelszó kell az admin beállítások megnyitásához és a program eltávolításához (a Windows rendszergazdai jóváhagyása mellett). Legalább 6 karakter. Jegyezd meg, vagy add át a megbízható személynek.
en.PwLabel=Password:
hu.PwLabel=Jelszó:
en.PwLabel2=Password again:
hu.PwLabel2=Jelszó újra:
en.ExistingPwInfo=An installation with an admin password already exists on this computer. To reinstall, enter the current password.
hu.ExistingPwInfo=A számítógépen már van telepített változat admin jelszóval. Az újratelepítéshez add meg a jelenlegi jelszót.
en.ExistingPwFail=The current password cannot be verified, so the installation cannot continue.
hu.ExistingPwFail=A jelenlegi jelszó nem ellenőrizhető, ezért a telepítés nem folytatható.
en.ErrPwEmpty=Enter the password.
hu.ErrPwEmpty=Add meg a jelszót.
en.ErrPwShort=The password must be at least 6 characters long.
hu.ErrPwShort=A jelszó legalább 6 karakter legyen.
en.ErrPwLong=The password can be at most 128 characters long.
hu.ErrPwLong=A jelszó legfeljebb 128 karakter lehet.
en.ErrPwMatch=The two passwords do not match.
hu.ErrPwMatch=A két jelszó nem egyezik.
en.ErrMinutes=The time limit must be a whole number between 0 and 1440 (minutes).
hu.ErrMinutes=Az időkeret 0 és 1440 közötti egész szám (perc) lehet.
en.ErrBan=The start and the end of a banned period cannot be the same hour.
hu.ErrBan=A tiltott időszak kezdete és vége nem lehet ugyanaz az óra.
en.ErrBonusLol=The LoL bonus must be a whole number between 0 and 10 (matches).
hu.ErrBonusLol=A LoL-jutalom 0 és 10 közötti egész szám (meccs) lehet.
en.ErrBonusMinutes=A bonus given in minutes must be a whole number between 0 and 1440.
hu.ErrBonusMinutes=A percben megadott jutalom 0 és 1440 közötti egész szám lehet.
en.ErrExtId=The extension ID is 32 characters long and can only contain the letters a–p.
hu.ErrExtId=A bővítményazonosító 32 karakter hosszú, és csak a–p betűket tartalmazhat.
en.ErrExtIdProd=Production mode needs the store ID; the given value is the development ID. Continue anyway?
hu.ErrExtIdProd=Éles módban az áruházi azonosítóra van szükség; a megadott érték a fejlesztői azonosító. Folytatod így?
en.StatusConfiguring=Configuring the service, permissions and Chrome policies…
hu.StatusConfiguring=Szolgáltatás, jogosultságok és Chrome-szabályok beállítása…
en.ErrSetupFailed=The system setup failed (code: %1). Run it manually as administrator: %2
hu.ErrSetupFailed=A rendszerbeállítás sikertelen (kód: %1). Futtasd rendszergazdaként kézzel: %2
en.UninstallPwRequired=The admin password is required to uninstall. The program was not uninstalled.
hu.UninstallPwRequired=Az eltávolításhoz az admin jelszó szükséges. A program nem lett eltávolítva.

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
  Result := AddNumberEdit(Parent, Caption, Top, '60', CustomMessage('UnitMinutes'));
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
  LimitsPage := CreateCustomPage(AfterID, CustomMessage('LimitsTitle'), CustomMessage('LimitsSubtitle'));
  Y := 0;

  Heading := AddLabel(LimitsPage, CustomMessage('ResetHeading'), 0, Y);
  Heading.Font.Style := [fsBold];
  Y := Y + ScaleY(22);

  RollingRadio := TNewRadioButton.Create(LimitsPage);
  RollingRadio.Parent := LimitsPage.Surface;
  RollingRadio.Top := Y;
  RollingRadio.Width := LimitsPage.SurfaceWidth;
  RollingRadio.Caption := CustomMessage('RollingCaption');
  RollingRadio.Checked := True;
  RollingRadio.OnClick := @ResetModeChanged;
  Y := Y + ScaleY(24);

  DailyRadio := TNewRadioButton.Create(LimitsPage);
  DailyRadio.Parent := LimitsPage.Surface;
  DailyRadio.Top := Y + ScaleY(2);
  DailyRadio.Width := ScaleX(220);
  DailyRadio.Caption := CustomMessage('DailyCaption');
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

  Heading := AddLabel(LimitsPage, CustomMessage('TimeLimitsHeading'), 0, Y);
  Heading.Font.Style := [fsBold];
  Y := Y + ScaleY(22);
  ShortVideoEdit := AddMinutesEdit(LimitsPage, CustomMessage('ShortVideoLabel'), Y);
  Y := Y + ScaleY(28);
  FeedEdit := AddMinutesEdit(LimitsPage, CustomMessage('FeedLabel'), Y);
  Y := Y + ScaleY(40);

  WeekendCheck := TNewCheckBox.Create(LimitsPage);
  WeekendCheck.Parent := LimitsPage.Surface;
  WeekendCheck.Top := Y;
  WeekendCheck.Width := LimitsPage.SurfaceWidth;
  WeekendCheck.Caption := CustomMessage('WeekendCaption');
  WeekendCheck.Checked := False;
  Y := Y + ScaleY(22);
  Heading := AddLabel(LimitsPage, CustomMessage('WeekendNote'), ScaleX(18), Y);
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
  BonusPage := CreateCustomPage(AfterID, CustomMessage('BonusTitle'),
    CustomMessage('BonusSubtitle'));

  Note := AddLabel(BonusPage,
    CustomMessage('BonusNote'), 0, 0);
  Note.AutoSize := False;
  Note.WordWrap := True;
  Note.Width := BonusPage.SurfaceWidth;
  Note.Height := ScaleY(48);
  Y := ScaleY(56);

  LolBonusEdit := AddNumberEdit(BonusPage, 'LoL PvP:', Y, '1', CustomMessage('UnitMatches'));
  Y := Y + ScaleY(28);
  ShortVideoBonusEdit := AddNumberEdit(BonusPage, CustomMessage('ShortVideoLabel'), Y, '30', CustomMessage('UnitMinutes'));
  Y := Y + ScaleY(28);
  FeedBonusEdit := AddNumberEdit(BonusPage, CustomMessage('FeedLabel'), Y, '30', CustomMessage('UnitMinutes'));
end;

function MarkerKey: String;
begin
  Result := 'SOFTWARE\LolScrollLimiter';
end;

function HasExistingInstall: Boolean;
begin
  Result := RegValueExists(HKLM, MarkerKey, 'ExtensionId');
end;

// Update: the tray application starts the installer with /UPDATE. Only meaningful on an existing installation;
// the settings are not asked again, and the existing limits are kept.
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
  BanPage := CreateCustomPage(AfterID, CustomMessage('BanTitle'),
    CustomMessage('BanSubtitle'));

  Note := AddLabel(BanPage,
    CustomMessage('BanNote'), 0, 0);
  Note.AutoSize := False;
  Note.WordWrap := True;
  Note.Width := BanPage.SurfaceWidth;
  Note.Height := ScaleY(56);
  Y := ScaleY(66);

  AddLabel(BanPage, CustomMessage('BanStart'), ScaleX(230), Y);
  AddLabel(BanPage, CustomMessage('BanEnd'), ScaleX(330), Y);
  Y := Y + ScaleY(20);

  LolBanCheck := AddBanCheck(BanPage, CustomMessage('BanLol'), Y);
  LolBanFrom := AddHourCombo(BanPage, ScaleX(230), Y, 1);
  AddLabel(BanPage, '–', ScaleX(310), Y + ScaleY(3));
  LolBanTo := AddHourCombo(BanPage, ScaleX(330), Y, 5);
  Y := Y + ScaleY(30);

  ShortVideoBanCheck := AddBanCheck(BanPage, CustomMessage('BanShort'), Y);
  ShortVideoBanFrom := AddHourCombo(BanPage, ScaleX(230), Y, 1);
  AddLabel(BanPage, '–', ScaleX(310), Y + ScaleY(3));
  ShortVideoBanTo := AddHourCombo(BanPage, ScaleX(330), Y, 5);
  Y := Y + ScaleY(30);

  FeedBanCheck := AddBanCheck(BanPage, CustomMessage('BanFeed'), Y);
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
    CustomMessage('ModeTitle'), CustomMessage('ModeSubtitle'),
    CustomMessage('ModeDesc'),
    True, False);
  ModePage.Add(CustomMessage('ModeProd'));
  ModePage.Add(CustomMessage('ModeDev'));
  ModePage.Values[0] := True;

  ExtensionPage := CreateInputQueryPage(ModePage.ID,
    CustomMessage('ExtTitle'), CustomMessage('ExtSubtitle'),
    CustomMessage('ExtDesc'));
  ExtensionPage.Add(CustomMessage('ExtLabel'), False);
  ExtensionPage.Values[0] := '{#StoreExtensionId}';

  CreateLimitsPage(ExtensionPage.ID);
  CreateBonusPage(LimitsPage.ID);
  CreateBanPage(BonusPage.ID);

  PasswordPage := CreateInputQueryPage(BanPage.ID,
    CustomMessage('PwTitle'), CustomMessage('PwSubtitle'),
    CustomMessage('PwDesc'));
  PasswordPage.Add(CustomMessage('PwLabel'), True);
  PasswordPage.Add(CustomMessage('PwLabel2'), True);
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
  // The default ID follows the chosen mode; a value typed by hand is left alone.
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

// If an installation with a password already exists, reinstalling (not updating) needs its password.
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
  MsgBox(CustomMessage('ExistingPwInfo'),
    mbInformation, MB_OK);
  if Exec(Helper, '--verify-password', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) and (Code = 0) then
    ExistingPasswordChecked := True
  else
  begin
    MsgBox(CustomMessage('ExistingPwFail'), mbError, MB_OK);
    Result := False;
  end;
end;

// In the temporary file every character is stored as four hex digits (UTF-16 code), so accented characters survive.
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
    // In a silent install the pages "advance" invisibly; it must not get stuck on an empty password.
    if WizardSilent then Exit;
    if Trim(PasswordPage.Values[0]) = '' then
      MsgBox(CustomMessage('ErrPwEmpty'), mbError, MB_OK)
    else if Length(PasswordPage.Values[0]) < 6 then
      MsgBox(CustomMessage('ErrPwShort'), mbError, MB_OK)
    else if Length(PasswordPage.Values[0]) > 128 then
      MsgBox(CustomMessage('ErrPwLong'), mbError, MB_OK)
    else if PasswordPage.Values[0] <> PasswordPage.Values[1] then
      MsgBox(CustomMessage('ErrPwMatch'), mbError, MB_OK)
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
      MsgBox(CustomMessage('ErrMinutes'), mbError, MB_OK);
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
      MsgBox(CustomMessage('ErrBan'), mbError, MB_OK);
      Result := False;
    end;
    Exit;
  end;
  if CurPageID = BonusPage.ID then
  begin
    if not NumberInRange(LolBonusEdit, 10) then
    begin
      MsgBox(CustomMessage('ErrBonusLol'), mbError, MB_OK);
      Result := False;
    end
    else if not MinutesValid(ShortVideoBonusEdit) or not MinutesValid(FeedBonusEdit) then
    begin
      MsgBox(CustomMessage('ErrBonusMinutes'), mbError, MB_OK);
      Result := False;
    end;
    Exit;
  end;
  if CurPageID = ExtensionPage.ID then
  begin
    ExtensionPage.Values[0] := Lowercase(Trim(ExtensionPage.Values[0]));
    if not IsValidExtensionId(ExtensionPage.Values[0]) then
    begin
      MsgBox(CustomMessage('ErrExtId'), mbError, MB_OK);
      Result := False;
    end
    else if ModePage.Values[0] and (ExtensionPage.Values[0] = '{#DevExtensionId}') then
      Result := MsgBox(CustomMessage('ErrExtIdProd'),
        mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  // On update the running files have to be released.
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop LolScrollLimiter', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Limiter.Tray.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(2000);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  DevModeValue: Cardinal;
  Params, ExtId, PwFile, Msg: String;
begin
  if CurStep = ssPostInstall then
  begin
    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\install.ps1') + '"' +
      ' -InstallDir "' + ExpandConstant('{app}') + '"';
    if IsUpdate then
    begin
      // Update: the extension ID and the mode come from the previous installation, the limits are kept.
      if RegQueryStringValue(HKLM, MarkerKey, 'ExtensionId', ExtId) then
        Params := Params + ' -ExtensionId ' + ExtId;
      if RegQueryDWordValue(HKLM, MarkerKey, 'DevMode', DevModeValue) and (DevModeValue = 1) then
        Params := Params + ' -DevMode';
      // An installation from before the language support has no stored language: take the installer's.
      if not RegValueExists(HKLM, MarkerKey, 'Language') then
        Params := Params + ' -Language ' + ActiveLanguage;
      Params := Params + ' -KeepSettings';
    end
    else
    begin
      Params := Params + ' -ExtensionId ' + ExtensionPage.Values[0];
      Params := Params + ' -Language ' + ActiveLanguage;
      // A silent install has no password page: the password is then set the first time the admin panel is opened.
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
    Log('install.ps1 parameters: ' + Params);
    WizardForm.StatusLabel.Caption := CustomMessage('StatusConfiguring');
    if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    begin
      // (A line must not start with "[": the Inno preprocessor would take it for a section tag.)
      Msg := FmtMessage(CustomMessage('ErrSetupFailed'), [IntToStr(Code), ExpandConstant('{app}\install.ps1')]);
      SuppressibleMsgBox(Msg, mbError, MB_OK, IDOK);
    end;
    if PwFile <> '' then DeleteFile(PwFile);
  end;
end;

// Uninstalling needs the admin password (if one is set); Limiter.Tray.exe verifies it.
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
    SuppressibleMsgBox(CustomMessage('UninstallPwRequired'), mbInformation, MB_OK, IDOK);
    Result := False;
  end;
end;
