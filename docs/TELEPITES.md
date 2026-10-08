# Telepítési útmutató

## 1. Szerepek és előfeltételek

- **Te** normál (nem rendszergazdai) Windows-fiókot használsz.
- **A megbízható személy** ismeri a rendszergazdai jelszót. Telepítéshez, eltávolításhoz és a korlátok módosításához ő írja be a jelszót az UAC-ablakba. A jelszó nálad ne legyen meg.
- Windows 11 (x64) és Google Chrome.
- Fordításhoz (egyszer, bármelyik gépen): .NET 10 SDK, Node.js 18+, [Inno Setup 6](https://jrsoftware.org/isdl.php).

Ha a géped jelenleg rendszergazdai fiók, előbb hozz létre egy rendszergazdai fiókot a megbízható személynek, majd a saját fiókodat állítsd normál felhasználóra (Gépház → Fiókok → Más felhasználók → Fióktípus módosítása).

## 2. Fordítás

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Kimenet az `out\` mappában:

| Fájl | Mire való |
|---|---|
| `LolScrollLimiter-Setup-1.0.0.exe` | Windows-telepítő |
| `extension-store.zip` | Feltöltés a Chrome Web Store-ba |
| `extension-dev.zip` | Fejlesztői tesztverzió (kicsomagolt betöltés, fix azonosító: `iidlgdeiobckahalgoabbobfdlefkagi`) |
| `app\` | A telepítendő programfájlok |

## 3. A bővítmény közzététele (éles használathoz kötelező)

A Chrome Windows alatt, tartományba nem léptetett gépen **csak a Chrome Web Store-ból** engedi a kötelező bővítménytelepítést ([Chrome kiadási feltételek](https://support.google.com/chrome/a/answer/7532015)).

1. Regisztrálj fejlesztőként: <https://chrome.google.com/webstore/devconsole> (egyszeri díj).
2. Új elem → töltsd fel az `out\extension-store.zip` fájlt.
3. Láthatóság: **Nem listázott** (unlisted) — csak a linkkel érhető el, a kötelező telepítés így is működik.
4. Az adatvédelmi lapon jelöld: a bővítmény nem gyűjt és nem továbbít adatot; a `nativeMessaging` engedély a helyi korlátozó szolgáltatással való kapcsolathoz kell.
5. Várd meg a Google jóváhagyását. Jegyezd fel a kapott **32 betűs bővítményazonosítót**.

Amíg a jóváhagyás nincs meg, a fejlesztői tesztverzió használható (5. pont), de az **nem nyújt valódi védelmet**, mert a bővítmény kikapcsolható.

## 4. Telepítés (éles)

1. Futtasd a `LolScrollLimiter-Setup-1.0.0.exe` fájlt a saját fiókodból; az UAC-ablakba a megbízható személy írja be a rendszergazdai jelszót.
2. Telepítési mód: **Éles**.
3. Bővítményazonosító: az áruházi azonosító (3. pont).
4. A telepítő:
   - a programot a `C:\Program Files\LolScrollLimiter` mappába teszi (normál felhasználónak csak olvasható),
   - telepíti és elindítja a `LolScrollLimiter` szolgáltatást (automatikus indulás, összeomlás után újraindul, normál felhasználó nem állíthatja le),
   - létrehozza a `C:\ProgramData\LolScrollLimiter` adatmappát (normál felhasználó nem éri el),
   - regisztrálja a Native Messaging hostot,
   - beállítja a Chrome-szabályokat: kötelező bővítménytelepítés, inkognitómód és vendégmód tiltva, bővítmények fejlesztői módja tiltva,
   - beállítja a tálcaalkalmazás automatikus indulását minden felhasználónak.
5. Indítsd újra a Chrome-ot. Ellenőrzés:
   - `chrome://policy` — megjelenik az `ExtensionInstallForcelist`, `IncognitoModeAvailability`, `BrowserGuestModeEnabled`, `ExtensionDeveloperModeSettings`.
   - `chrome://extensions` — a bővítmény „A rendszergazda telepítette”, nem kapcsolható ki.
   - A tálcán zöld óraikon; a menüben „Védelem: rendben”.

## 5. Fejlesztői tesztverzió

1. Telepítéskor válaszd a **Fejlesztői tesztverzió** módot, és hagyd az alapértelmezett azonosítót.
2. Csomagold ki az `out\extension-dev.zip` fájlt egy mappába.
3. `chrome://extensions` → Fejlesztői mód be → **Kicsomagolt bővítmény betöltése** → válaszd a mappát.
4. Az azonosítónak `iidlgdeiobckahalgoabbobfdlefkagi`-nek kell lennie (a manifest `key` mezője rögzíti).

Éles módra váltáshoz futtasd újra a telepítőt Éles módban az áruházi azonosítóval, és távolítsd el a kicsomagolt példányt.

## 6. Korlátok módosítása (rendszergazda)

Rendszergazdai jogú szerkesztővel (pl. Jegyzettömb „Futtatás rendszergazdaként”) szerkeszd:
`C:\ProgramData\LolScrollLimiter\limits.json`

```json
{
  "lolPvpMatches": 3,
  "shortVideoMinutes": 60,
  "facebookFeedMinutes": 60
}
```

A szolgáltatás 30 másodpercen belül átveszi. Hibás fájlnál az előző korlát marad érvényben, és a tálcaikon figyelmeztet.

## 7. Mit jelent a tálcaikon színe?

| Szín | Jelentés |
|---|---|
| Zöld | Minden működik |
| Sárga | Részleges hiba: pl. a LoL-kliens nem válaszol, ismeretlen játékmód, vagy a Chrome fut, de a bővítmény nem jelentkezik. Az érintett korlát most **enged** |
| Piros | A szolgáltatás nem érhető el — egyik korlát sem érvényesül. Automatikusan helyreáll, amint a szolgáltatás újraindul |

A bővítmény ikonján piros „!” jelzi, ha nincs kapcsolata a szolgáltatással.

## 8. Eltávolítás (rendszergazda)

Gépház → Alkalmazások → Telepített alkalmazások → LoL- és görgetéskorlátozó → Eltávolítás.
A számlálók megmaradnak (újratelepítés után folytatódnak). Teljes törlés rendszergazdai PowerShellből:

```powershell
& "C:\Program Files\LolScrollLimiter\uninstall.ps1" -RemoveData
```

(ezt az eltávolítás előtt kell futtatni, vagy töröld kézzel a `C:\ProgramData\LolScrollLimiter` mappát).

## 9. Hibaelhárítás

- **Naplók:** Eseménynapló → Windows-naplók → Alkalmazás, forrás: `LolScrollLimiter`.
- **LoL-kapcsolat kézi ellenőrzése** (normál felhasználóként is futtatható, a LoL-kliens fusson):

  ```powershell
  & "C:\Program Files\LolScrollLimiter\Limiter.Service.exe" --diagnose
  ```

  Kiírja a fázist, a sor besorolását (Pvp / NotPvp / Unknown) és az utolsó 10 meccs remake-jelzőjét.
  `--test-cancel` kapcsolóval keresés vagy meccselfogadás közben megszakít / elutasít.
- **A bővítmény nem jelentkezik:** `chrome://extensions` → a bővítmény részletei → „Service worker” → konzol. Ellenőrizd a `HKLM\SOFTWARE\Google\Chrome\NativeMessagingHosts\hu.kmsoft.lolscrolllimiter` kulcsot és a hivatkozott JSON-fájlban az `allowed_origins` azonosítót.

## 10. Ismert korlátok

- **Csak a kezelt Chrome-ra terjed ki.** Más böngészők (pl. Microsoft Edge, Firefox) és mobilalkalmazások nincsenek korlátozva. Ha ez gond, a megbízható személy tilthatja őket (pl. Edge-szabályokkal vagy szülői felügyelettel).
- **A LoL-kliens helyi API-ját a Riot hivatalosan nem támogatja harmadik fél számára** ([Riot dokumentáció](https://developer.riotgames.com/docs/lol#league-client-api)). Kliensfrissítés után ellenőrizd a `--diagnose` paranccsal; hibás kapcsolatnál a program enged és sárga jelzést ad.
- **Csoportos lobbyban** ha nem te vagy a vezető, a kliens nem mindig engedi a keresés megszakítását; ilyenkor a program a meccselfogadást utasítja el, ami a többieket visszateszi a sorba.
- **Remake** csak a kliens `gameEndedInEarlySurrender` jelzése alapján igazolt; a meccstörténet frissüléséig (általában 1-2 perc) a meccs számít. 6 óra után igazolás nélkül végleg beleszámít.
- **Fokozatos felszabadulás:** a webes idő percenként szabadul fel, a felhasználás után 24 óra + legfeljebb 1 perc elteltével.
