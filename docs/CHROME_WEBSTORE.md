# Közzététel a Chrome Web Store-ban

Éles (kötelező, a felhasználó által nem kikapcsolható) telepítéshez a bővítménynek a Chrome Web Store-ban kell lennie
([Chrome kiadási feltételek](https://support.google.com/chrome/a/answer/7532015)). Ez az útmutató a feltöltéshez szükséges
mindent tartalmaz; a fejlesztői fiókba (Chrome Web Store fejlesztői irányítópult) a tulajdonos jelentkezik be.

## 1. Előfeltételek

- Regisztrált fejlesztői fiók (egyszeri regisztrációs díjjal), a fiókhoz megadott **kapcsolattartó e-mail-cím megerősítve**
  (a fiók „Account” / „Fiók” fülén; az e-mail megjelenik az áruházi oldalon).
- Kétlépcsős azonosítás a Google-fiókon (a közzétételhez kötelező).
- A feltöltendő csomag: **`extension-store.zip`** — vagy a kiadás [`extension-store.zip`](https://github.com/viktorferenczi/LolAndDoomScrollFix/releases/latest) fájlja,
  vagy helyben: `installer\build.ps1` → `out\extension-store.zip`. A csomagban **nincs** `key` mező (azt az áruház adja).
- Adatvédelmi nyilatkozat **nyilvános webcímmel**: [`docs/PRIVACY.md`](PRIVACY.md) — pl. a tároló nyilvánossá tétele után
  `https://github.com/viktorferenczi/LolAndDoomScrollFix/blob/main/docs/PRIVACY.md`.

## 2. Feltöltés

1. Irányítópult → **Új elem** → töltsd fel az `extension-store.zip` fájlt.
2. Az elem azonosítója (32 betű) ekkor jön létre — **ezt jegyezd fel**, a telepítőnek kell (lásd 6. pont).

## 3. „Store listing” (áruházi oldal)

| Mező | Érték |
|---|---|
| Nyelv | magyar |
| Név | `LoL- és görgetéskorlátozó` |
| Rövid leírás (≤ 132 karakter) | `Korlátozza a rövid videók és a Facebook-hírfolyam használatát a gépen futó társalkalmazás szabályai szerint.` |
| Kategória | Productivity (Termelékenység) |
| Ikon | 128×128: a csomagban (`icons/icon128.png`) |
| Képernyőkép | legalább 1 db, 1280×800 vagy 640×400 (legfeljebb 5): pl. a blokkoló felület, a bővítmény felugró ablaka, a tálcaalkalmazás — **ezeket neked kell elkészítened** |
| Kis hirdetési csempe | 440×280 — **te készíted** |
| Kapcsolattartó e-mail | a fiókban megerősített cím |

**Részletes leírás** (másold be):

```
Segít megfékezni a végtelen görgetést. A bővítmény a gépeden futó „LoL- és görgetéskorlátozó” Windows-alkalmazással működik együtt (a bővítmény önmagában nem korlátoz).

Mit csinál?
• Méri, mennyi időt töltesz Instagram Reelsen, TikTokon, YouTube Shortson és Facebook Reelsen (közös keret), valamint a Facebook kezdőlapi hírfolyamán (külön keret).
• Csak az aktív, látható lapon lejátszott videót számolja; háttérlap, zárolt képernyő nem számít.
• Elfogyott kereten – vagy az előre megadott tiltott órákban – egy másodpercen belül leállítja a videót, és blokkoló felületet mutat.
• A keretek, a visszaállítás (gördülő 24 óra vagy napi nullázás), a hétvégi szabály és a tiltott órák a telepítéskor és az admin felületen állíthatók.

Fontos tudnivalók
• Csak Windowson, a hozzá tartozó korlátozó alkalmazással működik; nélküle a bővítmény nem korlátoz, és hibát jelez (piros „!” a bővítmény ikonján).
• Az adatok nem hagyják el a számítógépet: a számlálót a Chrome Native Messaging kapcsolatán át csak a helyi szolgáltatás kapja. Nincs analitika, követés vagy külső szerver.
• Inkognitó módban csak akkor működik, ha a chrome://extensions oldalon engeded az „Engedélyezés inkognitó módban” kapcsolót.
• Forráskód (megtekintésre): https://github.com/viktorferenczi/LolAndDoomScrollFix
```

## 4. „Privacy” (adatvédelem) fül

**Single purpose (egyetlen cél):**
> Korlátozza a rövid videók és a Facebook-hírfolyam használatát a gépen futó társalkalmazás szabályai szerint (használat mérése és blokkolás).

**Engedélyek indoklása:**

| Engedély | Indoklás (másold be) |
|---|---|
| `nativeMessaging` | A bővítmény a mért használatot és a tiltási állapotot az ugyanazon a gépen futó társalkalmazással (Windows-szolgáltatás) cseréli ki a Chrome Native Messaging kapcsolatán. Enélkül nem tud keretet számolni vagy tiltani. Adat nem hagyja el a számítógépet. |
| `idle` | Zárolt képernyő észlelése: zárolt gépen nem számolunk használatot. |
| `alarms` | A háttérfolyamat életben tartása és újracsatlakozás a társalkalmazáshoz, ha a kapcsolat megszakadt. |
| Gazdagép-hozzáférés: `instagram.com`, `tiktok.com`, `youtube.com`, `facebook.com` | A tartalomszkript ezeken az oldalakon állapítja meg, hogy rövid videó vagy hírfolyam látható-e és lejátszik-e videó, megállítja a videót, és blokkoló felületet jelenít meg, ha a keret elfogyott. Más oldalon nem fut. |

**Távoli kód:** nem használ (nincs `eval`, nincs külső szkript, minden kód a csomagban van).

**Adathasználat (a fejlesztői irányítópult kérdéseihez; a pontos választ te felelsz meg — a bővítmény működése alapján):**
- A bővítmény az oldalak címét és a lejátszási állapotot **csak a gépen, a mérés idejére** dolgozza fel; **nem tárol és nem küld ki** személyes adatot (csak másodpercszámlálót ad a helyi szolgáltatásnak).
- Ha az űrlap rákérdez az oldalcímekre („web history”) vagy az oldal tartalmára („website content”), a legóvatosabb, legtisztább út ezeket **bejelölni**, és az indoklásban leírni, hogy a feldolgozás csak helyi, és semmi sem hagyja el a gépet. Ez biztonságosabb, mint kihagyni, és utólag eltérést találni.
- Pipáld be a három nyilatkozatot, amelyek igazak: nem adod el az adatot harmadik félnek; nem használod a fő céltól független célra; nem használod hitelképesség megállapítására.
- Adatvédelmi nyilatkozat webcíme: lásd 1. pont.

## 5. „Distribution” és „Test instructions”

- Láthatóság: **Unlisted (nem listázott)** — csak a linkkel érhető el, a kötelező telepítés így is működik.
- Régiók: minden régió (vagy csak Magyarország).
- **Tesztelési útmutató az áruházi ellenőrnek** (másold be):

```
A bővítmény egy Windows-os társalkalmazással (Windows-szolgáltatás) együtt működik; a keretet és a tiltást a szolgáltatás számolja, a bővítmény Native Messaging-en keresztül kommunikál vele.
Társalkalmazás nélkül a bővítmény hibát jelez (piros „!” az ikonon), és semmit sem blokkol – ez a szándékos „hiba esetén enged” működés.
A teljes működés kipróbálásához: töltsd le a telepítőt a https://github.com/viktorferenczi/LolAndDoomScrollFix/releases/latest oldalról (LolScrollLimiter-Setup-X.Y.Z.exe), telepítsd „Fejlesztői tesztverzió” módban, töltsd be a bővítményt, nyiss meg egy YouTube Shorts videót (youtube.com/shorts/…), és állítsd a rövid videók keretét 1 percre: egy perc után a videó megáll, és blokkoló felület jelenik meg.
Forráskód: https://github.com/viktorferenczi/LolAndDoomScrollFix
```

> **Fontos:** az ellenőr csak akkor éri el a telepítőt és a forráskódot, ha a GitHub-tároló **nyilvános**.

## 6. Elfogadás után

1. A közzétett elem **azonosítóját** (32 betű, a–p) add meg a telepítőnek: éles módban a „Chrome-bővítmény” oldalon (vagy `install.ps1 -ExtensionId …`).
   Ez eltér a fejlesztői azonosítótól (`iidlgdeiobckahalgoabbobfdlefkagi`).
2. A telepítő beállítja a kötelező telepítést és a Native Messaging host engedélyezett eredetét ehhez az azonosítóhoz. Ellenőrzés: `chrome://policy`, `chrome://extensions` („A rendszergazda telepítette”) és a [telepítési útmutató](TELEPITES.md) 4. pontja.
3. Meglévő fejlesztői teszttelepítésen: futtasd újra a telepítőt „Éles” módban az áruházi azonosítóval, és töröld a kicsomagolt példányt.

## 7. Új verzió

1. Emeld a verziót az `extension/manifest.json`-ban (a Chrome csak magasabb verziót fogad el), készítsd el az új `extension-store.zip`-et.
2. Irányítópult → az elem → **Package** → új csomag feltöltése → közzététel. A felhasználók Chrome-ja magától frissít.

## Megjegyzések az ellenőrzéshez

A `nativeMessaging` és a több oldalra szóló gazdagép-hozzáférés alaposabb ellenőrzést kap, ezért a bírálat tovább tarthat; a fenti indoklások és a tesztelési útmutató ezt gyorsítják.
Ha elutasítják, az irányítópult megadja az okot; a leggyakoribb: hiányzó vagy nem nyilvános adatvédelmi nyilatkozat, nem indokolt engedély, vagy a leírás nem egyezik a működéssel.
