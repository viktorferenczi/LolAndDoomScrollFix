# Ellenőrzés és próbák

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
| Pihenőnap-jutalom | `RestDayBonusTests`: +30 perc / +1 meccs üres nap után, a keretek függetlenek, nem halmozódik, bármilyen előző napi használat kizárja, remake nem rontja el, napi módban a nullázástól számított nap, mentes hétvégénél hétfőn a péntek, hétvégi korlátnál a vasárnap, 0-s keretre nem jár, gördülő módban a jutalom éjféli megszűnését a feloldási idő figyelembe veszi |

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
| 3b.10 | Telepítés a Pihenőnap-jutalom oldalon: LoL 2, rövid videó 15, hírfolyam 0 | `limits.json`: `2` / `15` / `0`; üres nap után a tálcán +2 meccs és +15 perc, a hírfolyamnál nincs jutalom |
| 3b.11 | Érvénytelen jutalom (pl. LoL `11`, perc `abc`) | A telepítő hibaüzenettel nem lép tovább |

## 4. Védelempróbák (normál felhasználóként)

| # | Lépés | Elvárt eredmény |
|---|---|---|
| 4.1 | `chrome://extensions` — kikapcsolás / eltávolítás | Nem lehetséges („A rendszergazda telepítette”) |
| 4.2 | Inkognitó ablak, vendégmód | Nem nyitható |
| 4.3 | Fejlesztői mód bekapcsolása | Nem lehetséges |
| 4.4 | `C:\ProgramData\LolScrollLimiter` megnyitása / `limiter.db` törlése vagy módosítása | Hozzáférés megtagadva |
| 4.5 | `sc stop LolScrollLimiter` vagy Szolgáltatások → Leállítás | Hozzáférés megtagadva |
| 4.6 | Feladatkezelőből `Limiter.Service.exe` leállítása | Hozzáférés megtagadva |
| 4.7 | Feladatkezelőből `Limiter.NativeHost.exe` leállítása | A bővítmény pár mp-en belül újracsatlakozik |
| 4.8 | (Rendszergazdaként) szolgáltatás leállítása | Tálcaikon piros, a bővítményen „!”, minden enged; a szolgáltatás indítása után 5–10 mp-en belül magától helyreáll |
| 4.9 | (Rendszergazdaként) a szolgáltatás folyamatának leölése | A Windows 5 mp múlva újraindítja; helyreáll |
| 4.10 | `limits.json` elrontása (rendszergazdaként) | Sárga figyelmeztetés, az előző korlát marad |
