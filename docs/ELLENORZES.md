# Ellenőrzés és próbák

## Telepítés utáni gyorsellenőrzés (minden telepítés után)

Ezek az élő Windows-viselkedésen múlnak, unit-teszt nem fedi le őket.

| # | Lépés | Elvárt eredmény |
|---|---|---|
| S.1 | Rendszergazdai PowerShell: `Get-Acl "C:\Program Files\LolScrollLimiter\Limiter.Service.exe" \| Format-List` | Olvasható; SYSTEM és Rendszergazdák: FullControl, Felhasználók: ReadAndExecute (az `install.ps1` ezt maga is ellenőrzi, és hibával leáll, ha nem így van) |
| S.2 | `Get-Service LolScrollLimiter` | `Running` |
| S.3 | Normál felhasználóként: tálcaikon és a bővítmény felugró ablaka | Zöld ikon, „A védelem működik.” — nem „A szolgáltatás munkamenete nem kérdezhető le” |
| S.4 | Normál felhasználóként, futó LoL-klienssel: `Limiter.Service.exe --diagnose` | Kiírja a fázist és a játékost — nem „a LoL-kliens felderítése sikertelen” |

## Játék közbeni terhelés és akadás

| # | Lépés | Elvárt eredmény |
|---|---|---|
| G.1 | Feladatkezelő → Részletek: `Limiter.Service.exe` és `WmiPrvSE.exe` CPU-használata **nyitott LoL-kliens nélkül** 1 percig | Gyakorlatilag 0 % (a költséges WMI-lekérdezés nem fut, amíg nincs kliens) |
| G.2 | Ugyanez egy futó meccs közben | A szolgáltatás CPU-használata elhanyagolható, és nincs 3 másodperces ritmusú csúcs |
| G.3 | Egy teljes meccs végigjátszása a szolgáltatással, majd ugyanaz leállított szolgáltatással | Nincs különbség a képkockasebességben / akadásban |
| G.4 | Szolgáltatás naplója egy meccs után | Nincs „Új PvP-keresés megszakítva” / „meccselfogadás elutasítva” bejegyzés a meccs fázisai alatt (csak keresés és elfogadás közben lehet) |

## Admin beállítások

Normál (nem rendszergazdai) Windows-fiókból.

| # | Lépés | Elvárt eredmény |
|---|---|---|
| A.1 | Tálcaikon → Admin beállítások… (vagy a Részletek ablak gombja) | Windows UAC-ablak rendszergazdai jelszót kér; jelszó nélkül nem nyílik semmi |
| A.2 | UAC-ablakban Mégse | „A rendszergazdai jóváhagyás (jelszó) elmaradt”; semmi sem változik |
| A.3 | Helyes jelszó után | Megnyílik a szerkesztő a **jelenlegi** értékekkel (telepítéskor megadottakkal egyező) |
| A.4 | Értékek átírása (pl. rövid videó 20 perc, napi nullázás 04:00, hétvégi korlát be, LoL tiltott órák 23–6) → Mentés | „A beállítások mentve”; `C:\ProgramData\LolScrollLimiter\limits.json` az új értékeket tartalmazza |
| A.5 | 30 másodperc után a tálcán és a bővítmény felugró ablakában | Az új keret és a visszaállítási szabály látszik; a LoL-keresés tiltott órában leáll |
| A.6 | Tiltott időszak kezdete = vége bejelölve → Mentés | Hibaüzenet, a fájl nem változik |
| A.7 | Rendszergazdaként a `limits.json`-t elrontva (`{ nem json`), majd Admin beállítások | Figyelmeztetés, az alapértékekkel nyílik a szerkesztő; mentés után a fájl rendben van |
| A.8 | Normál felhasználóként közvetlenül `Limiter.Tray.exe --admin` | „csak rendszergazdai jogosultsággal nyitható meg”, nem nyílik szerkesztő |
| A.9 | Normál felhasználóként a `limits.json` megnyitása / módosítása a Windowsban | Hozzáférés megtagadva (a szerkesztő nélkül sem lehet) |
| A.10 | Mentés után az eredeti telepítési értékekre visszaállítva, majd frissítés gomb (1.4.x → újabb) | A frissítés megtartja a szerkesztővel mentett beállításokat |
| A.11 | A UAC jóváhagyása után | Megjelenik az **admin jelszó** kérése; mező nélkül/Mégse → nem nyílik szerkesztő |
| A.12 | Hibás jelszó | „Hibás jelszó (1/5)”, kb. 1 mp késés; az ötödik hibás próba után bezárul, a szerkesztő nem nyílik meg |
| A.13 | Helyes jelszó | Megnyílik a szerkesztő |
| A.14 | Jelszó módosítása… → új jelszó kétszer (eltérő, rövid, csupa szóköz is kipróbálva) | Eltérő/rövid/szóköz: hibaüzenet, nem változik; helyesnél „megváltozott”; a régi jelszó többé nem jó, az új igen |
| A.15 | Frissítés régebbi (jelszó nélküli) verzióról, majd Admin beállítások | A UAC után új jelszót kell megadni (kétszer); utána normálisan nyílik |
| A.16 | Rendszergazdaként a `C:\ProgramData\LolScrollLimiter\admin.json` elrontva, majd Admin beállítások | „A jelszófájl nem olvasható vagy sérült…”, a szerkesztő nem nyílik; a fájl törlése után új jelszót lehet beállítani |
| A.17 | Normál felhasználóként az `admin.json` megnyitása | Hozzáférés megtagadva; a fájlban nincs olvasható jelszó (csak `salt`/`hash`) |

### Extra keret egy napra

Szombati/vasárnapi próbákhoz a rendszeridő átállítható, vagy a naptár alapján a megfelelő napokat kell választani.

| # | Lépés | Elvárt eredmény |
|---|---|---|
| X.1 | Admin beállítások → Extra keret: a napválasztó első napja | A „most tartó nap”; előtte nincs múltbeli nap |
| X.2 | Napi nullázás 03:00-ra állítva, 03:00 előtt megnyitva | A „most tartó nap” az előző naptári nap |
| X.3 | Hétvégi korlát kikapcsolva: a szombat és a vasárnap a listában | Szürke, „nem választható: hétvége, nincs korlát”; kattintásra figyelmeztetés, a kijelölés az előző napon marad |
| X.4 | A hétvégi korlát bejelölése | A szombat és a vasárnap azonnal kiválaszthatóvá válik |
| X.5 | Hétvégére extra, majd a hétvégi korlát kikapcsolása | Piros figyelmeztetés a csoportban; Mentés → hibaüzenet, a fájl nem változik; az extra törlése után a mentés sikerül |
| X.6 | Mára +2 LoL, +15 perc rövid videó, +10 perc hírfolyam → Mentés; 30 mp után a tálca | „LoL: 5-ből, ebből +2 admin által adott extra”; „… 75 percből, ebből +15 perc …”; a felugró ablakban „(+2 extra)”, „(+15 p extra)” |
| X.7 | Minden érték 0 → Extra mentése a napra | Hibaüzenet („adj meg legalább egy értéket”) |
| X.8 | Ugyanarra a napra újabb értékek | Felülírja a meglévőt (a listában egy sor); a nap kiválasztásakor a mezők a meglévő értékeket mutatják |
| X.9 | Extra törlése a napról | A sor eltűnik, a mezők nullára állnak; Mentés után a tálcán nincs extra |
| X.10 | Holnapi napra extra, a mai keret kimerítve | A „újra elérhető” idő a holnapi extrát is figyelembe veszi (gördülő módban éjfélkor, ha az már elég) |
| X.11 | Rövid videó extra +10, a keret elfogyasztása a 60+10 percig | 70 percnél tilt, nem korábban |
| X.12 | A tiltott órák alatt extra | Nem oldja fel a tiltást |
| X.13 | Az extra napja lejár (éjfél / a nullázás órája) | A keret visszaáll az alapra |
| X.14 | Mentés után a `limits.json` | Az `extraGrants` tömb `"date": "ÉÉÉÉ-HH-NN"` bejegyzéseket tartalmaz; a lejárt napok nincsenek benne |

## Frissítés gomb

Előfeltétel: a tároló nyilvános, és van legalább két kiadás (pl. 1.3.2 és újabb) a `release.yml` szerint közzétéve.

| # | Lépés | Elvárt eredmény |
|---|---|---|
| U.1 | Tálcaikon → Frissítések keresése, a legújabb verzióval | „A program naprakész (verzió: X)” |
| U.2 | Régebbi verzióról ugyanez | Megjelenik az új verzió és a változások; „Letöltöd és telepíted most?” |
| U.3 | Nem → | Semmi sem töltődik le |
| U.4 | Igen → UAC-ablak; a rendszergazdai jelszót beírva | Csendes telepítés előrehaladás-ablakkal, majd a tálcaalkalmazás újraindul az új verzióval (menü: „Verzió: …”) |
| U.5 | Frissítés után `limits.json`, a tálcán a számlálók | A korlátok, a jutalom, a tiltott órák és a felhasznált idő változatlan; a szolgáltatás fut (`Get-Service LolScrollLimiter`) |
| U.6 | Frissítés megkezdése, de az UAC-ablakban Mégse | „A rendszergazdai jóváhagyás elmaradt”; semmi sem változik, a korlátok működnek |
| U.7 | Hálózat kikapcsolva, majd gomb | „Nincs kapcsolat a GitHubbal…”; a korlátok tovább működnek |
| U.8 | Privát tárolóval vagy kiadás nélkül | „Még nincs közzétett kiadás, vagy a kiadások nem nyilvánosak.” |
| U.9 | (Fejlesztő) kiadás, amelynek `SHA256SUMS.txt`-je el van rontva | A letöltés után „az ellenőrzőösszeg nem egyezik”, a telepítő nem indul el |
| U.10 | Kézi telepítés (nem frissítés) egy meglévő telepítés fölé | A teljes varázsló megjelenik a korlátok oldalaival, és az új értékek felülírják a régieket |

## 0. Első lépés: a LoL-kliens igazolása

A Riot a kliens helyi API-ját nem támogatja hivatalosan, ezért éles használat előtt az **aktuális** kliensen igazolni kell a keresés megszakítását és a remake felismerését. Normál felhasználóként, futó LoL-klienssel:

| # | Lépés | Elvárt eredmény |
|---|---|---|
| 0.1 | `Limiter.Service.exe --diagnose` a kezdőképernyőn | Fázis: `None` vagy `Lobby`; látszik a bejelentkezett játékos |
| 0.2 | Ranked/Normal lobby, majd `--diagnose` | Besorolás: `Pvp` |
| 0.3 | TFT-, bot-, gyakorló- és egyéni lobby, majd `--diagnose` | Besorolás: `NotPvp` |
| 0.4 | PvP-keresés indítása, közben `--diagnose --test-cancel` | `Keresés megszakítása: HTTP 204` (vagy 200), a kliensben leáll a keresés |
| 0.5 | PvP-keresés, a meccselfogadás felugrásakor `--diagnose --test-cancel` | `Meccselfogadás elutasítása: HTTP 204`, a kliens elutasítottként mutatja |
| 0.6 | Egy korábbi remake után `--diagnose` | A remake-meccsnél `remake: True`, egy rövid, de nem remake meccsnél `False` |

Ha 0.4–0.6 bármelyike nem a várt eredményt adja, a program ettől még enged és jelez (fail-open), de a LoL-korlát nem lesz megbízható — jelezd a hibát a kimenettel együtt.

## 1. Automatikus tesztek

```bash
dotnet test tests/Limiter.Core.Tests
```

```bash
node --test extension/test/*.test.js
```

| Terület | Tesztek |
|---|---|
| Gördülő 24 órás határ | `LolLimitTests.Three_pvp_matches_block_…` (23:59:59-kor tilt, 24:00:01-kor felszabadul), `WebLimitTests.Available_again_time_…` |
| Időkeret fokozatos felszabadulása | `WebLimitTests.Sixty_minutes_block_and_time_is_released_gradually_…` |
| Profilváltás / több Chrome-profil | `WebLimitTests.Two_chrome_profiles_reporting_simultaneously_…`, `LolLimitTests.Counters_are_per_windows_user_…` |
| Újraindítás | `LolLimitTests.Counters_survive_service_restart`, `WebLimitTests.Usage_survives_restart` |
| Remake | `LolLimitTests.Verified_remake_gives_back_the_slot_…`, `Remade_game_reported_again_…`, `LcuJsonTests.Remake_requires_the_early_surrender_flag` |
| Ismételt meccsesemények | `LolLimitTests.Reconnect_and_repeated_events_…`, `Third_game_in_progress_and_its_reconnect_are_allowed` |
| Nem PvP módok, néző mód | `LolLimitTests.Non_pvp_modes_never_count_…`, `Spectating_does_not_count`, `ClassifierTests` |
| Webes időmérés | `background.test.js`: fókusz, háttérlap, zárolás, alvás, 1 mp-en belüli tiltás, fail-open |
| Felismerés | `detect.test.js`: URL-szabályok, felugró Reels, Messenger/profil/csoport kivétel |
| Napi nullázás, hétvége | `ScheduleTests`: nullázás a választott órában (webes és LoL-keret), 00:00-s nullázás, hétvégi mentesség, vasárnap este indult meccs hétfőn sem számít, hétvége a nullázási órától, hétvégi korlát bekapcsolva, péntek esti tiltás a hétvége kezdetén feloldódik, nyári időszámítás, a telepítő által írt `limits.json` beolvasása |
| Pihenőnap-jutalom | `RestDayBonusTests`: +30 perc / +1 meccs üres nap után, a keretek függetlenek, nem halmozódik, bármilyen előző napi használat kizárja, remake nem rontja el, napi módban a nullázástól számított nap, mentes hétvégénél hétfőn a péntek, hétvégi korlátnál a vasárnap, 0-s keretre nem jár, gördülő módban a jutalom éjféli megszűnését a feloldási idő figyelembe veszi, friss telepítésnél és részlegesen mért előző napnál nincs jutalom, a mérés kezdete újraindítás után is megmarad, régebbi adatbázisnál az első rögzített használat a kezdet |
| Admin-szerkesztő háttere | `SettingsFileTests`: érték-ellenőrzés (minden tartomány, kezdet = vég csak bekapcsolva hiba), a mentett fájl visszaolvasása ugyanazzal a függvénnyel, mint a szolgáltatás, csere egy lépésben és ideiglenes fájl nélkül, érvénytelen mentés nem nyúl a fájlhoz, hiányzó / hibás / régi fájl kezelése |
| Frissítés | `UpdateTests`: verziók értelmezése és összehasonlítása (csak szigorúan újabb), csak a tároló saját kiadási címei és GitHub-átirányítás megbízható, ellenőrzőösszeg a `SHA256SUMS.txt`-ből vagy a GitHub `digest` mezőjéből, ellenőrzőösszeg nélküli / telepítő nélküli / túl nagy / vázlat / előzetes kiadás kezelése, privát tároló, korlátozás és hálózati hiba üzenete, letöltés egyező / hibás összeggel és mérettel, idegen cím és átirányítás elutasítása |
| Tiltott órák | `BannedHoursTests`: kezdő óra benne, záró nincs, éjfélen átnyúló időszak, azonos kezdet és vég = nincs tiltás, LoL-keresés tiltva maradék kerettel is (TFT nem), folyamatban lévő meccs és újracsatlakozás engedett, a webes keretek külön tilthatók, a keret és a nullázás nem változik, elfogyott keretnél a későbbi feloldás, mentes hétvégén nincs tiltás, helyi idő, `limits.json` beolvasása |

## 2. Windowsos próbák (LoL)

Tipp a gyorsításhoz: rendszergazdaként állítsd a `limits.json`-ban `"lolPvpMatches": 1` értékre, majd a végén vissza 3-ra.

| # | Lépés | Elvárt eredmény |
|---|---|---|
| 2.1 | Játssz le annyi PvP-meccset, amennyi a keret | A tálcán „LoL PvP: elfogyott (n/n) — újra: …” |
| 2.2 | Az utolsó meccs közben lépj ki, majd csatlakozz újra | Az újracsatlakozás engedett, a számláló nem nő |
| 2.3 | Egyéni lobbyból PvP-keresés | A keresés 1–2 mp-en belül leáll; értesítés: „Új PvP-keresés megszakítva…” |
| 2.4 | Csoportos lobbyból PvP-keresés (te vagy a vezető, majd más a vezető) | Vezetőként leáll a keresés; tagként legkésőbb a meccselfogadás elutasításra kerül |
| 2.5 | TFT-keresés, botmeccs, gyakorlás, egyéni játék, néző mód | Mind elérhető, a számláló nem nő |
| 2.6 | Remake-elt meccs után 1–2 perc | Értesítés: „Igazolt remake…”, a számláló eggyel csökken |
| 2.7 | Gép újraindítása tiltott állapotban | Tiltás megmarad |

## 3. Böngészős próbák

Tipp: `"shortVideoMinutes": 2`, `"facebookFeedMinutes": 2`.

| # | Lépés | Elvárt eredmény |
|---|---|---|
| 3.1 | YouTube Shorts 1 perc, majd TikTok 1 perc | A közös keret elfogy; mindkét oldalon blokkoló felület, a videó 1 mp-en belül megáll |
| 3.2 | Ezután Instagram Reels és Facebook Reels | Azonnal blokkolva (közös keret) |
| 3.3 | Facebook-kezdőlap | Külön keret: még használható; a Messenger, profil, csoport soha nem blokkolt |
| 3.4 | Hírfolyamon felugró Reels | A rövidvideós keretet fogyasztja (popup tiltva, ha az elfogyott), nem a hírfolyamét |
| 3.5 | Lapváltás másik lapra / ablak kis méretre | Nem fogy idő (popup ikon: a maradék nem változik) |
| 3.6 | Közvetlen link (pl. `youtube.com/shorts/…`) új lapon, tiltott állapotban | Blokkolva |
| 3.7 | Tiltott oldal újratöltése (F5) | A blokkolás megmarad |
| 3.8 | Videó szól, egér nem mozdul 3 percig | Az idő fogy |
| 3.9 | Gép zárolása (Win+L) videó közben | Zárolás alatt nem fogy |
| 3.10 | Második Chrome-profil | Ugyanaz a keret, ugyanaz a tiltás |

## 3/b. Telepítési beállítások próbái

| # | Lépés | Elvárt eredmény |
|---|---|---|
| 3b.1 | Telepítés alapértékekkel | `limits.json`: `rolling`, 60/60 perc, `enforceOnWeekends: false`; tálca: „Minden keret az előző 24 órára vonatkozik…”, „Hétvégén … nincs korlát” |
| 3b.2 | Újratelepítés: napi nullázás 03:00, 20/10 perc | A tálca és a bővítmény felugró ablaka „naponta 03:00-kor nullázódnak” szöveget mutat; a blokkoló felületen „20 percet naponta (nullázás 03:00-kor)” |
| 3b.3 | Napi mód: keret elfogyasztása, majd az óra átállítása 03:00 utánra (rendszergazdaként) | A keret teljesen felszabadul |
| 3b.4 | Hétvégén (vagy a rendszeridőt szombatra állítva), hétvégi korlát nélkül | Tálca: „hétvége — nincs korlát”; nincs blokkolás, nincs LoL-megszakítás; hétfőn a hétvégi használat nem látszik |
| 3b.5 | Ugyanez „A korlátok hétvégén is érvényesek” bejelölésével | A korlátok szombaton is tiltanak |
| 3b.6 | Érvénytelen perc (pl. `abc` vagy `2000`) a telepítőben | A telepítő hibaüzenettel nem lép tovább |
| 3b.7 | Egy teljes napig (kedd) semmi rövid videó, LoL és hírfolyam; szerdán megnézni a tálcát | „… maradt (90 percből, ebből +30 perc pihenőnap-jutalom)”, LoL: „4-ből, ebből +1 pihenőnap-jutalom” |
| 3b.8 | Szerdán 1 perc rövid videó; csütörtökön a tálca | Rövid videó: 60 perc (nincs jutalom); a hírfolyamnál, ha szerdán sem volt, továbbra is +30 |
| 3b.9 | Hétfő reggel, pénteken nem volt LoL (mentes hétvégével) | +1 meccs |
| 3b.9b | **Friss telepítés** (üres adatbázis), a telepítés napján és másnap megnézni a tálcát | Nincs jutalom: LoL „3-ból”, rövid videó és hírfolyam „60 percből”; az első jutalom a telepítést követő második napon jelenhet meg |
| 3b.10 | Telepítés a Pihenőnap-jutalom oldalon: LoL 2, rövid videó 15, hírfolyam 0 | `limits.json`: `2` / `15` / `0`; üres nap után a tálcán +2 meccs és +15 perc, a hírfolyamnál nincs jutalom |
| 3b.11 | Érvénytelen jutalom (pl. LoL `11`, perc `abc`) | A telepítő hibaüzenettel nem lép tovább |
| 3b.12 | Tiltott órák: LoL bejelölve, a jelenlegi órától a következőig (pl. 14:00–15:00) | PvP-keresés azonnal leáll, értesítés: „…tiltott időszak van”; tálca: „LoL PvP: tiltott időszak (14:00–15:00) — újra: ma 15:00”; TFT és gyakorlás elérhető; 15:00 után a keresés újra működik |
| 3b.13 | Tiltott órák: rövid videók bejelölve a jelenlegi órára | YouTube Shorts / TikTok azonnal „Tiltott időszak” blokkoló felületet mutat, a Facebook-hírfolyam nem (ha az nincs bejelölve) |
| 3b.14 | Tiltott időszak: kezdete = vége (pl. 03:00–03:00) | A telepítő hibaüzenettel nem lép tovább |
| 3b.15 | Admin jelszó oldal: üres, 5 karakteres, két eltérő jelszó | Hibaüzenet, a telepítő nem lép tovább |
| 3b.16 | Telepítés ékezetes jelszóval (pl. `árvíztűrő1`) | Az Admin beállításoknál az ékezetes jelszó működik |
| 3b.17 | Az ideiglenes jelszófájl a telepítés után | A `%TEMP%\is-*.tmp\lsl-admin.dat` nem marad meg; az `admin.json` csak `salt`/`hash` mezőket tartalmaz |
| 3b.18 | Újratelepítés (nem frissítés gombbal) már jelszavas telepítés fölé | Először a jelenlegi jelszót kéri; hibás jelszóval nem folytatódik |
| 3b.19 | Eltávolítás (Gépház) hibás jelszóval / Mégse | Az eltávolítás megszakad, a program és a szolgáltatás megmarad |
| 3b.20 | Eltávolítás helyes jelszóval | Eltávolítja a programot; az `admin.json` törlődik |
| 3b.21 | Csendes telepítés (`/SILENT`), jelszó-oldal nélkül | A telepítés sikerül; az Admin beállítások első megnyitásakor jelszót kell megadni |

## 4. Védelempróbák (normál felhasználóként)

| # | Lépés | Elvárt eredmény |
|---|---|---|
| 4.1 | `chrome://extensions` — kikapcsolás / eltávolítás | Nem lehetséges („A rendszergazda telepítette”) |
| 4.2 | Vendégmód | Nem nyitható |
| 4.2b | Inkognitó ablak | Nyitható. „Engedélyezés inkognitó módban” bekapcsolva: rövid videó mér, elfogyott keretnél blokkol, a keret közös a normál ablakokkal; kikapcsolva: nem mér, nem blokkol. A felugró ablak a kapcsoló állapotát mutatja |
| 4.3 | Fejlesztői mód bekapcsolása | Nem lehetséges |
| 4.4 | `C:\ProgramData\LolScrollLimiter` megnyitása / `limiter.db` törlése vagy módosítása | Hozzáférés megtagadva |
| 4.5 | `sc stop LolScrollLimiter` vagy Szolgáltatások → Leállítás | Hozzáférés megtagadva |
| 4.6 | Feladatkezelőből `Limiter.Service.exe` leállítása | Hozzáférés megtagadva |
| 4.7 | Feladatkezelőből `Limiter.NativeHost.exe` leállítása | A bővítmény pár mp-en belül újracsatlakozik |
| 4.8 | (Rendszergazdaként) szolgáltatás leállítása | Tálcaikon piros, a bővítményen „!”, minden enged; a szolgáltatás indítása után 5–10 mp-en belül magától helyreáll |
| 4.9 | (Rendszergazdaként) a szolgáltatás folyamatának leölése | A Windows 5 mp múlva újraindítja; helyreáll |
| 4.10 | `limits.json` elrontása (rendszergazdaként) | Sárga figyelmeztetés, az előző korlát marad |
