# LoL- és görgetéskorlátozó

Windows 11-es alkalmazás Chrome-bővítménnyel.

| Tevékenység | Korlát (alapértelmezés) | A keret elfogyásakor |
|---|---:|---|
| LoL PvP | 3 meccs | Új PvP-keresés megszakítva, meccselfogadás elutasítva |
| Instagram Reels, TikTok, YouTube Shorts, Facebook Reels | Együtt 60 perc (telepítéskor állítható) | A felület blokkolva |
| Facebook kezdőlapi hírfolyam | Külön 60 perc (telepítéskor állítható) | A hírfolyam blokkolva |

Telepítéskor választható:

- **Visszaállítás:** gördülő 24 óra (alapértelmezés — minden felhasználás pontosan 24 órával később szabadul fel), vagy napi nullázás egy választott órában (pl. 03:00).
- **Időkeretek** percben a rövid videókra és a Facebook-hírfolyamra (alapértelmezés: 60-60 perc).
- **Hétvége:** alapértelmezésként szombaton és vasárnap nincs korlát, és a hétvégi használat nem számít bele a keretbe; bejelölve a korlátok hétvégén is érvényesek.

**Pihenőnap-jutalom:** ha egy napon egy PvP-meccset sem játszol, másnap +1 meccs jár; ha egy napon 0 percet nézel rövid videót, másnap +30 perc; ugyanígy a Facebook-hírfolyamnál +30 perc. A három jutalom független egymástól, és nem halmozódik: több üres nap után is csak egyszer jár.

TFT, botmeccs, gyakorlás, egyéni játék és néző mód nem fogyasztja a LoL-keretet; az igazolt remake visszaadja az alkalmat.
Felismerési vagy működési hibánál a program **enged tovább, és hibát jelez** (tálcaikon, a bővítmény piros „!” jelvénye).

## Szabad felhasználás

Nyugodtan használd, másold, módosítsd és terjeszd tovább – személyes és egyéb célra is. A projekt [MIT licenc](LICENSE) alatt áll. A program garancia nélkül, „ahogy van” formában készült; a korlátok értékeit (3 meccs, 60 perc) és a támogatott oldalakat a saját igényeid szerint át lehet írni. Ha hasznosnak találod, egy csillag vagy egy hivatkozás örömet okoz, de nem kötelező.

## Mit tud az alkalmazás?

A cél az, hogy megfékezze a végtelen görgetést és a túlzott League of Legends-játékot. Három, egymással együttműködő rész dolgozik:

- **Windows-szolgáltatás** (`Limiter.Service`): a háttérben, rendszerjogosultsággal fut, ezért normál felhasználó nem tudja leállítani. Számolja a felhasználást egy védett SQLite-adatbázisban, és ő dönti el, hogy a keret elfogyott-e.
- **Chrome-bővítmény**: figyeli, mennyi időt töltesz Instagram Reelsen, TikTokon, YouTube Shortson és Facebook Reelsen, valamint a Facebook kezdőlapi hírfolyamán. Csak az aktív, látható, használt lapot számolja (háttérlap vagy zárolt képernyő nem számít). Elfogyott kereten blokkolja a felületet. A bővítmény ikonjára kattintva a popupban látod a hátralévő időt.
- **Tálcaalkalmazás** (`Limiter.Tray`): magyar nyelvű tálcaikon, 5 másodpercenként frissül. Zöld = minden rendben, sárga = figyelmeztetés, piros = hiba. A menüben és a „Részletek” ablakban látszik a hátralévő LoL-meccs, a rövidvideó-idő, a hírfolyamidő és a következő felszabaduló keret időpontja.

### Működés röviden

1. **Visszaállítás.** Alapértelmezésként gördülő 24 órás keret: nincs éjféli nullázás, mindig az elmúlt 24 óra felhasználását nézi, így egy meccs vagy egy perc pontosan 24 óra múlva szabadul fel újra. Telepítéskor választható helyette napi nullázás egy adott órában (pl. 03:00). Alapértelmezésként hétvégén nincs korlát; ez is a telepítőben kapcsolható.
2. **LoL-meccsek.** A szolgáltatás a League kliens helyi (LCU) API-ját figyeli. Csak a PvP-sorok (pl. rangsorolt, normál) számítanak; TFT, botmeccs, gyakorlás, egyéni játék és néző mód nem. Ha a keret elfogyott, az új PvP-keresést megszakítja, a meccselfogadást elutasítja. Az igazolt remake (újrakezdett meccs) visszaadja az elhasznált alkalmat.
3. **Rövidvideók.** Instagram Reels, TikTok, YouTube Shorts és Facebook Reels **közös** keretet használ (alapértelmezés: 60 perc, telepítéskor állítható).
4. **Facebook-hírfolyam.** A kezdőlapi hírfolyam **külön** keretet kap (alapértelmezés: 60 perc, telepítéskor állítható).
5. **Pihenőnap-jutalom.** Ha az előző napon egy keretből semmi sem fogyott, aznap ahhoz több jár (LoL +1 meccs, rövid videó +30 perc, hírfolyam +30 perc). Nem halmozódik. Mentes hétvégénél hétfőn a péntek számít.
6. **Hibatűrés.** Ha a felismerés vagy a működés hibázik, a program nem zár ki, hanem tovább enged, és hibát jelez (piros tálcaikon, a bővítmény piros „!” jelvénye).
7. **Kijátszás elleni védelem.** Éles telepítésben a Chrome-szabályok kötelezővé teszik a bővítményt, tiltják az inkognitó- és vendégmódot, a bővítmények fejlesztői módját; a beállítások módosításához rendszergazdai jelszó kell (a gondolat: ezt egy megbízható személy ismeri, nem te).

A telepítésről és az ellenőrzésről lásd lent a hivatkozott útmutatókat.

## Felépítés

```
src/
  Limiter.Core/        Gördülő 24 órás keretek, SQLite-tár, LoL-sorbesorolás, meccsszámláló (platformfüggetlen, tesztelt)
  Limiter.Protocol/    A named pipe protokoll üzenetei és kliense (a szolgáltatás hitelességének ellenőrzésével)
  Limiter.Service/     Windows-szolgáltatás: számlálók, LoL-kliens (LCU) figyelése, pipe-szerver
  Limiter.NativeHost/  Chrome Native Messaging host: továbbítja a bővítmény üzeneteit a szolgáltatásnak
  Limiter.Tray/        Magyar nyelvű tálcaalkalmazás
extension/             Chrome-bővítmény (Manifest V3) + Node-tesztek
installer/             build.ps1, install.ps1, uninstall.ps1, Inno Setup-szkript
tests/                 xUnit-tesztek
docs/                  TELEPITES.md (telepítési útmutató), ELLENORZES.md (próbák)
```

```mermaid
flowchart LR
  subgraph User[Normál Windows-felhasználó]
    Ext[Chrome-bővítmény] -- Native Messaging --> Host[Limiter.NativeHost]
    Tray[Limiter.Tray]
    LCU[LoL-kliens LCU API]
  end
  subgraph System[SYSTEM, 0-s munkamenet]
    Svc[Limiter.Service] --> DB[(SQLite\nProgramData, csak admin)]
  end
  Host -- named pipe --> Svc
  Tray -- named pipe --> Svc
  Svc -- HTTPS/WSS 127.0.0.1 --> LCU
```

## Gyors parancsok

```bash
dotnet test tests/Limiter.Core.Tests
```

```bash
node --test extension/test/*.test.js
```

Windows alatt a teljes csomag (tesztek, közzététel, bővítménycsomagok, telepítő):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Részletek: [docs/TELEPITES.md](docs/TELEPITES.md) és [docs/ELLENORZES.md](docs/ELLENORZES.md).
