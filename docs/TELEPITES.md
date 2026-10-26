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
| `LolScrollLimiter-Setup-1.4.0.exe` | Windows-telepítő |
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

1. Futtasd a `LolScrollLimiter-Setup-1.4.0.exe` fájlt a saját fiókodból; az UAC-ablakba a megbízható személy írja be a rendszergazdai jelszót.
2. Telepítési mód: **Éles**.
3. Bővítményazonosító: az áruházi azonosító (3. pont).
4. **Korlátok** oldal:

   | Beállítás | Lehetőségek | Alapértelmezés |
   |---|---|---|
   | A keretek visszaállítása | **Gördülő 24 óra**: minden felhasználás pontosan 24 órával később szabadul fel (a webes idő percenként, fokozatosan). **Naponta nullázás**: a választott órában (00:00–23:00, helyi idő) minden keret egyszerre nullázódik | Gördülő 24 óra (napi módnál 03:00) |
   | Rövid videók (Reels, TikTok, Shorts) | 0–1440 perc | 60 perc |
   | Facebook kezdőlapi hírfolyam | 0–1440 perc | 60 perc |
   | A korlátok hétvégén is érvényesek | Ha nincs bejelölve, szombaton és vasárnap nincs korlát, és a hétvégi használat (meccs is) nem számít bele a keretbe | Nincs bejelölve |

   **Pihenőnap-jutalom** oldal: mennyivel nőjön a keret egy kihagyott nap után (részletek: 6. pont).

   | Beállítás | Lehetőségek | Alapértelmezés |
   |---|---|---|
   | LoL PvP | 0–10 meccs | +1 meccs |
   | Rövid videók | 0–1440 perc | +30 perc |
   | Facebook kezdőlapi hírfolyam | 0–1440 perc | +30 perc |

   0 = az adott jutalom kikapcsolva.

   **Tiltott órák** oldal: keretenként egy jelölőnégyzet és két óra (kezdete, vége). Bejelölve a megadott órák között
   az adott dolog egyáltalán nem használható, akkor sem, ha a keretből még maradt (részletek: 6. pont).

   | Sor | Alapértelmezés |
   |---|---|
   | LoL PvP-meccsek | nincs bejelölve (01:00–05:00 előre kitöltve) |
   | Rövid videók | nincs bejelölve (01:00–05:00) |
   | Facebook-hírfolyam | nincs bejelölve (01:00–05:00) |

   A kezdete és a vége nem lehet ugyanaz az óra; ilyenkor a telepítő nem lép tovább.

   Napi nullázásnál a „hétvége” a választott órától számít: 03:00-s nullázásnál szombat 03:00-tól hétfő 03:00-ig tart.
   Gördülő módban szombat 00:00-tól hétfő 00:00-ig. Ha pénteken elfogy egy keret, a hétvége kezdetén feloldódik.

5. A telepítő:
   - a programot a `C:\Program Files\LolScrollLimiter` mappába teszi (normál felhasználónak csak olvasható),
   - telepíti és elindítja a `LolScrollLimiter` szolgáltatást (automatikus indulás, összeomlás után újraindul, normál felhasználó nem állíthatja le),
   - létrehozza a `C:\ProgramData\LolScrollLimiter` adatmappát (normál felhasználó nem éri el),
   - regisztrálja a Native Messaging hostot,
   - beállítja a Chrome-szabályokat: kötelező bővítménytelepítés, vendégmód tiltva, bővítmények fejlesztői módja tiltva (az inkognitómód engedélyezett, lásd lent),
   - beállítja a tálcaalkalmazás automatikus indulását minden felhasználónak.
6. Indítsd újra a Chrome-ot. Ellenőrzés:
   - `chrome://policy` — megjelenik az `ExtensionInstallForcelist`, `BrowserGuestModeEnabled`, `ExtensionDeveloperModeSettings`.
   - `chrome://extensions` — a bővítmény „A rendszergazda telepítette”, nem kapcsolható ki.
   - A tálcán zöld óraikon; a menüben „Védelem: rendben”.

### Inkognitó mód

Az inkognitómód nincs tiltva. Hogy a bővítmény inkognitó ablakban is mérjen és korlátozzon, azt a
**chrome://extensions → a bővítmény Részletek → Engedélyezés inkognitó módban** kapcsoló dönti el:

| Kapcsoló | Inkognitó ablakban |
|---|---|
| Be | Ugyanúgy mér és blokkol, mint normál ablakban; a keret közös |
| Ki (a Chrome alapértéke) | A bővítmény nem fut: nincs mérés és nincs blokkolás |

A bővítmény felugró ablaka kiírja, melyik állapot érvényes. A kapcsolót a felhasználó szabadon állíthatja,
ezért kikapcsolt állapotban az inkognitó ablak a webes korlát megkerülésére használható.

## 5. Fejlesztői tesztverzió

1. Telepítéskor válaszd a **Fejlesztői tesztverzió** módot, és hagyd az alapértelmezett azonosítót.
2. Csomagold ki az `out\extension-dev.zip` fájlt egy mappába.
3. `chrome://extensions` → Fejlesztői mód be → **Kicsomagolt bővítmény betöltése** → válaszd a mappát.
4. Az azonosítónak `iidlgdeiobckahalgoabbobfdlefkagi`-nek kell lennie (a manifest `key` mezője rögzíti).

Éles módra váltáshoz futtasd újra a telepítőt Éles módban az áruházi azonosítóval, és távolítsd el a kicsomagolt példányt.

## 6. Korlátok módosítása (rendszergazda)

A legegyszerűbb: futtasd újra a telepítőt, és válaszd az új értékeket — a telepítő a **Korlátok** oldal értékeivel mindig felülírja a korábbiakat (a számlálók megmaradnak).

Kézzel: rendszergazdai jogú szerkesztővel (pl. Jegyzettömb „Futtatás rendszergazdaként”) szerkeszd a
`C:\ProgramData\LolScrollLimiter\limits.json` fájlt:

```json
{
  "resetMode": "rolling",
  "dailyResetHour": 3,
  "lolPvpMatches": 3,
  "shortVideoMinutes": 60,
  "facebookFeedMinutes": 60,
  "enforceOnWeekends": false,
  "lolRestDayBonusMatches": 1,
  "shortVideoRestDayBonusMinutes": 30,
  "facebookFeedRestDayBonusMinutes": 30,
  "lolBannedHours": { "enabled": false, "from": 1, "to": 5 },
  "shortVideoBannedHours": { "enabled": false, "from": 1, "to": 5 },
  "facebookFeedBannedHours": { "enabled": false, "from": 1, "to": 5 }
}
```

| Mező | Érték |
|---|---|
| `resetMode` | `"rolling"` (gördülő 24 óra) vagy `"daily"` (napi nullázás) |
| `dailyResetHour` | 0–23, a napi nullázás órája helyi idő szerint (csak `"daily"` módban) |
| `lolPvpMatches` | PvP-meccsek száma |
| `shortVideoMinutes`, `facebookFeedMinutes` | 0–1440 perc |
| `enforceOnWeekends` | `true`: hétvégén is van korlát; `false`: hétvégén nincs |
| `lolRestDayBonusMatches` | Pihenőnap-jutalom: ennyi meccs jár pluszban, ha előző nap egy PvP-meccs sem volt (0–10; 0 = kikapcsolva) |
| `shortVideoRestDayBonusMinutes`, `facebookFeedRestDayBonusMinutes` | Pihenőnap-jutalom percben, ha előző nap 0 perc fogyott az adott keretből (0 = kikapcsolva) |
| `lolBannedHours`, `shortVideoBannedHours`, `facebookFeedBannedHours` | Tiltott órák: `enabled` (be/ki), `from` (kezdő óra, 0–23, benne van), `to` (záró óra, 0–23, nincs benne). `from` = `to` esetén nincs tiltás |

### Pihenőnap-jutalom

- A három keret külön-külön számít: pl. ha tegnap nem néztél rövid videót, de görgetted a hírfolyamot, ma csak a rövid videókhoz jár +30 perc.
- **Nem halmozódik:** két vagy több üres nap után is csak egyszer jár a jutalom.
- **Melyik nap számít:** napi nullázásnál a két nullázás közötti idő (pl. 03:00–03:00), gördülő módban a naptári nap (éjféltől éjfélig).
  A jutalom az adott napra emeli a keretet; gördülő módban éjfélkor megszűnik, és a tiltás feloldásának időpontja ezt már figyelembe veszi.
- **Hétvége:** ha hétvégén nincs korlát, a hétvégi használat nem rögzül, ezért hétfőn a péntek számít (nem a vasárnap).
- **0 perces / 0 meccses keretre** nem jár jutalom (különben minden második nap feloldódna).
- **Friss telepítés:** a program a telepítéskor kezdi a mérést, és jutalom csak olyan napért jár, amelyet **teljes egészében** mért.
  Telepítés napján és a rá következő napon ezért nincs jutalom (a telepítés napja nem teljes nap); az első jutalom a telepítést követő
  második napon járhat, ha az előző, teljesen mért nap üres volt. Újratelepítéskor a számlálók és a mérés kezdete megmarad.
  Régebbi verzióról frissítve a mérés kezdete az első rögzített használat ideje.
- Ha a gép egész nap ki volt kapcsolva, az üres napnak számít (ha a program korábban már mért).
- A jutalom mértékét a telepítő **Pihenőnap-jutalom** oldalán lehet megadni (vagy utólag a fenti mezőkben).

A szolgáltatás 30 másodpercen belül átveszi. Hibás fájlnál az előző korlát marad érvényben, és a tálcaikon figyelmeztet.
Módváltáskor a már rögzített használat az új szabály szerint számít (pl. gördülőről napira váltva csak az utolsó nullázás óta eltelt használat).

### Tiltott órák

- A tiltott időszakban az adott dolog nem használható: LoL-nál az új PvP-keresést a program megszakítja,
  a meccselfogadást elutasítja; a rövid videóknál és a hírfolyamnál a blokkoló felület jelenik meg „Tiltott időszak” címmel.
- **A keretet és a nullázást nem érinti:** tiltás alatt semmi sem fogy, és a napi nullázás a tiltás alatt is megtörténik.
  Ha a tiltás végén a keret is elfogyott, a feloldás a későbbi időpont.
- Egész órák, helyi idő szerint; a kezdő óra benne van, a záró nincs (01:00–05:00 = 01:00:00-tól 04:59:59-ig).
  Átnyúlhat éjfélen (22:00–06:00).
- **Folyamatban lévő meccs:** a tiltás kezdetekor futó meccs befejezhető, és az újracsatlakozás is működik.
- TFT, botmeccs, gyakorlás, egyéni játék és néző mód tiltott időben is elérhető.
- **Hétvége:** ha hétvégén nincs korlát, tiltás sincs. Napi nullázásnál a hétvége a nullázás órájától számít
  (03:00-s nullázásnál szombat 02:00 még a pénteki naphoz tartozik, így akkor még tilt).

## 7. Mit jelent a tálcaikon színe?

| Szín | Jelentés |
|---|---|
| Zöld | Minden működik |
| Sárga | Részleges hiba: pl. a LoL-kliens nem válaszol, ismeretlen játékmód, vagy a Chrome fut, de a bővítmény nem jelentkezik. Az érintett korlát most **enged** |
| Piros | A szolgáltatás nem érhető el — egyik korlát sem érvényesül. Automatikusan helyreáll, amint a szolgáltatás újraindul |

A bővítmény ikonján piros „!” jelzi, ha nincs kapcsolata a szolgáltatással.

## 7/b. Frissítés

A tálcaikon menüjében és a „Részletek” ablakban is van **Frissítések keresése** gomb (és látszik az aktuális verzió).

1. A gomb megnézi a GitHub legújabb kiadását (`viktorferenczi/LolAndDoomScrollFix`). Ha nincs újabb verzió: „A program naprakész”.
2. Ha van, kiírja az új verziót és a változásokat, és rákérdez, letöltse-e.
3. Igen esetén letölti a telepítőt, és **ellenőrzi az SHA-256 ellenőrzőösszegét** a kiadáshoz tartozó `SHA256SUMS.txt` (vagy a GitHub által adott összeg) alapján. Eltérés esetén törli a fájlt, és nem indítja el.
4. Elindítja a telepítőt, ami **rendszergazdai jóváhagyást kér** (Windows UAC). Ide a megbízható személy írja be a rendszergazdai jelszót — a felhasználó önmagában nem tudja telepíteni a frissítést, és ezzel a korlátozásokat sem tudja kikapcsolni.
5. A telepítő csendben frissít: a **beállítások (korlátok, jutalom, tiltott órák, hétvége) és a számlálók megmaradnak**, a tálcaalkalmazás újraindul. Az új verzió esetleges új beállításai alapértékkel jönnek létre; ha ezeket állítani akarod, futtasd a telepítőt kézzel (a korlátok oldalakkal), vagy szerkeszd a `limits.json`-t.

Biztonsági szabályok: csak a tároló saját kiadásából tölt le (https://github.com/…/releases/download/…), csak **szigorúan újabb** verziót ajánl (visszalépést és azonos verziót nem), ellenőrzőösszeg nélküli kiadást nem telepít, és a letöltés után is csak GitHub-címre engedi az átirányítást.
A frissítés keresése nem érinti a korlátozások működését; ha a keresés nem sikerül (nincs net, a GitHub nem válaszol), a program ezt jelzi, és minden korlát tovább működik.

**A kiadások előfeltétele:** a tároló kiadásainak nyilvánosnak kell lenniük (a tálcaalkalmazás bejelentkezés nélkül kérdezi le a GitHubot). Privát tárolónál a gomb azt jelzi: „Még nincs közzétett kiadás, vagy a kiadások nem nyilvánosak.”

**Kiadás készítése (fejlesztő):** emeld a verziót a `Directory.Build.props`, az `extension/manifest.json` és az `installer/LolScrollLimiter.iss` fájlban, commit és push, majd:

```bash
git tag v1.4.0 && git push origin v1.4.0
```

A `.github/workflows/release.yml` Windows alatt lefordítja a telepítőt, elkészíti a `SHA256SUMS.txt`-t, és közzéteszi a kiadást (`LolScrollLimiter-Setup-1.4.0.exe`, `extension-store.zip`, `extension-dev.zip`, `SHA256SUMS.txt`).
A fájlnév és a címke formátuma kötött (`LolScrollLimiter-Setup-X.Y.Z.exe` / `vX.Y.Z`), különben a gomb nem találja a telepítőt.

A Chrome-bővítmény éles módban a Chrome Web Store-ból magától frissül; a fejlesztői tesztverziónál a frissítés után a `chrome://extensions` oldalon újra be kell tölteni a kicsomagolt bővítményt.
Kódaláírás nélkül a telepítő a Windows-ablakban „Ismeretlen közzétevő”-ként jelenik meg; a jóváhagyás előtt a megbízható személy ellenőrizze, hogy a frissítést a felhasználó tudatosan indította.

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
- **A LoL játék közben akad vagy lefagy:** a program a játék folyamatát soha nem nyitja meg, nem függeszti fel és nem állítja le;
  a LoL-klienshez csak keresés és meccselfogadás közben küld parancsot (a keresés megszakítása, az elfogadás elutasítása), és
  azt is csak akkor, ha közvetlenül előtte a kliens még ugyanabban a fázisban van. Játék közben legfeljebb 15 másodpercenként kérdezi le a klienst.
  Ha mégis akadást tapasztalsz, különítsd el az okot:
  1. Rendszergazdai PowerShellben: `Stop-Service LolScrollLimiter`, majd játssz egy meccset. Ha így is akad, nem a program okozza (videokártya-illesztőprogram, túlmelegedés, Vanguard stb.).
  2. Ha csak a szolgáltatással akad: Feladatkezelő → Részletek → `Limiter.Service.exe` és `WmiPrvSE.exe` CPU-használata játék közben; küldd el a naplót (Eseménynapló → Alkalmazás, forrás: `LolScrollLimiter`) a játék időpontjával.
- **A bővítmény nem jelentkezik:** `chrome://extensions` → a bővítmény részletei → „Service worker” → konzol. Ellenőrizd a `HKLM\SOFTWARE\Google\Chrome\NativeMessagingHosts\hu.kmsoft.lolscrolllimiter` kulcsot és a hivatkozott JSON-fájlban az `allowed_origins` azonosítót.

## 10. Ismert korlátok

- **Inkognitó mód:** ha az „Engedélyezés inkognitó módban” kapcsoló ki van kapcsolva, inkognitó ablakban nincs webes korlát.
- **Csak a kezelt Chrome-ra terjed ki.** Más böngészők (pl. Microsoft Edge, Firefox) és mobilalkalmazások nincsenek korlátozva. Ha ez gond, a megbízható személy tilthatja őket (pl. Edge-szabályokkal vagy szülői felügyelettel).
- **A LoL-kliens helyi API-ját a Riot hivatalosan nem támogatja harmadik fél számára** ([Riot dokumentáció](https://developer.riotgames.com/docs/lol#league-client-api)). Kliensfrissítés után ellenőrizd a `--diagnose` paranccsal; hibás kapcsolatnál a program enged és sárga jelzést ad.
- **Csoportos lobbyban** ha nem te vagy a vezető, a kliens nem mindig engedi a keresés megszakítását; ilyenkor a program a meccselfogadást utasítja el, ami a többieket visszateszi a sorba.
- **Remake** csak a kliens `gameEndedInEarlySurrender` jelzése alapján igazolt; a meccstörténet frissüléséig (általában 1-2 perc) a meccs számít. 6 óra után igazolás nélkül végleg beleszámít.
- **Fokozatos felszabadulás (gördülő mód):** a webes idő percenként szabadul fel, a felhasználás után 24 óra + legfeljebb 1 perc elteltével.
- **Időzóna:** a napi nullázás és a hétvége a gép helyi idejét követi; nyári időszámítás kezdetekor a kihagyott óra helyett egy órával később nullázódik.
