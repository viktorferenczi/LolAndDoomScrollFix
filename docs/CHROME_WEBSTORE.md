# Publishing in the Chrome Web Store

English | [Magyar](#közzététel-a-chrome-web-store-ban-magyar)

For a production (mandatory, cannot be switched off by the user) installation, the extension has to be in the Chrome Web Store
([Chrome release terms](https://support.google.com/chrome/a/answer/7532015)). This guide contains everything needed for the upload;
the owner signs in to the developer account (Chrome Web Store developer dashboard).

## 1. Prerequisites

- A registered developer account (with the one-time registration fee), with the **contact e-mail address verified**
  (on the account's "Account" tab; the e-mail is shown on the store page).
- Two-step verification on the Google account (mandatory for publishing).
- The package to upload: **`extension-store.zip`** — either the release's [`extension-store.zip`](https://github.com/viktorferenczi/LolAndDoomScrollFix/releases/latest),
  or locally: `installer\build.ps1` → `out\extension-store.zip`. The package has **no** `key` field (the store assigns the ID).
- A privacy policy with a **public web address**: [`docs/PRIVACY.md`](PRIVACY.md), i.e.
  `https://github.com/viktorferenczi/LolAndDoomScrollFix/blob/main/docs/PRIVACY.md`.

## 2. Upload

1. Dashboard → **New item** → upload `extension-store.zip`.
2. The item's ID (32 letters) is created now — **note it down**; the installer needs it (see section 6).
3. The package contains the English and Hungarian message catalogs (`_locales`), and its default language is English. The store shows the extension's name and description in the user's language.

## 3. Store listing

| Field | Value |
|---|---|
| Languages | English (default) and Hungarian |
| Name | English: `LoL & Doomscroll Limiter` · Hungarian: `LoL- és görgetéskorlátozó` (both come from the package's `_locales`) |
| Short description (≤ 132 characters) | English: `Limits short videos and the Facebook feed according to the rules of the companion app running on this computer.` · Hungarian: `Korlátozza a rövid videók és a Facebook-hírfolyam használatát a gépen futó társalkalmazás szabályai szerint.` |
| Category | Productivity |
| Icon | 128×128: in the package (`icons/icon128.png`) |
| Screenshots | at least 1, 1280×800 or 640×400 (at most 5): ready-made ones in [`docs/images/en/`](images/en) (English) and [`docs/images/`](images) (Hungarian) |
| Small promo tile | 440×280: `docs/images/en/promo.png` (English), `docs/images/promo.png` (Hungarian) |
| Contact e-mail | the address verified in the account |

**Detailed description, English** (paste it):

```
Helps you curb endless scrolling. The extension works together with the "LoL & Doomscroll Limiter" Windows application running on your computer (the extension does not limit anything on its own).

What does it do?
• Measures how much time you spend on Instagram Reels, TikTok, YouTube Shorts and Facebook Reels (one shared limit), and on the Facebook home feed (a separate limit).
• Counts only video played on the active, visible tab; a background tab or a locked screen does not count.
• When a limit is used up - or during the banned hours you set in advance - it stops the video within a second and shows a blocking overlay.
• The limits, the reset (rolling 24 hours or a daily reset), the weekend rule and the banned hours are set in the installer and in the admin panel.

Good to know
• Windows only, together with the companion limiter application; without it the extension does not limit anything and shows an error (a red "!" on the extension icon).
• Your data does not leave the computer: the counter goes over Chrome Native Messaging to the local service only. No analytics, tracking or external server.
• In incognito mode it only works if you turn on "Allow in Incognito" on the chrome://extensions page.
• Source code (view only): https://github.com/viktorferenczi/LolAndDoomScrollFix
```

**Detailed description, Hungarian** (paste it for the Hungarian listing):

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

## 4. "Privacy" tab

**Single purpose:**
> Limits the use of short videos and the Facebook feed according to the rules of the companion app running on this computer (measuring usage and blocking).

**Permission justifications** (paste them):

| Permission | Justification |
|---|---|
| `nativeMessaging` | The extension exchanges the measured usage and the blocking state with the companion application (a Windows service) running on the same computer, over Chrome Native Messaging. Without it, it cannot count a limit or block. No data leaves the computer. |
| `idle` | Detecting a locked screen: usage is not counted on a locked computer. |
| `alarms` | Keeping the background process alive and reconnecting to the companion application when the connection was lost. |
| Host access: `instagram.com`, `tiktok.com`, `youtube.com`, `facebook.com` | On these sites the content script finds out whether a short video or the feed is visible and whether a video is playing, stops the video, and shows a blocking overlay when the limit is used up. It does not run on any other site. |

**Remote code:** not used (no `eval`, no external script, all code is in the package).

**Data usage** (for the developer dashboard's questions; you answer them exactly, based on how the extension works):
- The extension processes the page address and the playback state **only on the computer, for the time of the measurement**; it **does not store or send out** personal data (it only gives a seconds counter to the local service).
- If the form asks about page addresses ("web history") or page content ("website content"), the most careful and cleanest way is to **tick** them, and to explain in the justification that the processing is local only and nothing leaves the computer. This is safer than leaving them out and finding a mismatch later.
- Tick the three declarations that are true: you do not sell the data to third parties; you do not use it for a purpose unrelated to the main purpose; you do not use it to determine creditworthiness.
- Privacy policy address: see section 1.

## 5. "Distribution" and "Test instructions"

- Visibility: **Unlisted** — reachable only by the link; the forced installation works that way too.
- Regions: all regions (or Hungary only).
- **Test instructions for the store reviewer** (paste them):

```
The extension works together with a Windows companion application (a Windows service); the service counts the limit and the blocking, and the extension communicates with it over Native Messaging.
Without the companion application the extension shows an error (a red "!" on the icon) and blocks nothing - this is the intended "let everything through on error" behaviour.
To try the full behaviour: download the installer from https://github.com/viktorferenczi/LolAndDoomScrollFix/releases/latest (LolScrollLimiter-Setup-X.Y.Z.exe), install it in "Development test version" mode, load the extension, open a YouTube Shorts video (youtube.com/shorts/...), and set the short-video limit to 1 minute: after a minute the video stops and a blocking overlay appears.
Source code: https://github.com/viktorferenczi/LolAndDoomScrollFix
```

> **Important:** the reviewer can reach the installer and the source code only if the GitHub repository is **public**.

## 6. After approval

**The published item:** [https://chromewebstore.google.com/detail/lol-%C3%A9s-g%C3%B6rget%C3%A9skorl%C3%A1toz%C3%B3/mkoeoadekdfnbjpmhiabpjlmhdoimcml](https://chromewebstore.google.com/detail/lol-%C3%A9s-g%C3%B6rget%C3%A9skorl%C3%A1toz%C3%B3/mkoeoadekdfnbjpmhiabpjlmhdoimcml) — its ID: `mkoeoadekdfnbjpmhiabpjlmhdoimcml`.

1. The published item's ID (`mkoeoadekdfnbjpmhiabpjlmhdoimcml`) is the installer's default in production mode; it only has to be changed if a new item is published (on the installer's "Chrome extension" page or `install.ps1 -ExtensionId …`).
   It differs from the development ID (`iidlgdeiobckahalgoabbobfdlefkagi`).
2. The installer sets the forced installation and the Native Messaging host's allowed origin for this ID. Check: `chrome://policy`, `chrome://extensions` ("Installed by your administrator") and section 5 of the [installation guide](INSTALL.md).
3. On an existing development test installation: run the installer again in "Production" mode with the store ID, and delete the unpacked copy.

## 7. A new version

1. Raise the version in `extension/manifest.json` (Chrome only accepts a higher version) and build the new `extension-store.zip`.
2. Dashboard → the item → **Package** → upload a new package → publish. Users' Chrome updates by itself.

## Notes for the review

`nativeMessaging` and host access to several sites get a closer review, so the review may take longer; the justifications above and the test instructions speed it up.
If it is rejected, the dashboard gives the reason; the most common: a missing or non-public privacy policy, an unjustified permission, or a description that does not match the behaviour.

---

# Közzététel a Chrome Web Store-ban (magyar)

[English version above](#publishing-in-the-chrome-web-store)

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
| Nyelv | magyar (és angol, lásd fent) |
| Név | `LoL- és görgetéskorlátozó` (a csomag `_locales` mappájából) |
| Rövid leírás (≤ 132 karakter) | `Korlátozza a rövid videók és a Facebook-hírfolyam használatát a gépen futó társalkalmazás szabályai szerint.` |
| Kategória | Productivity (Termelékenység) |
| Ikon | 128×128: a csomagban (`icons/icon128.png`) |
| Képernyőkép | legalább 1 db, 1280×800 vagy 640×400 (legfeljebb 5): kész képek a [`docs/images/`](images) mappában (magyar) és a [`docs/images/en/`](images/en) mappában (angol) |
| Kis hirdetési csempe | 440×280: `docs/images/promo.png` (magyar), `docs/images/en/promo.png` (angol) |
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

**A közzétett elem:** [https://chromewebstore.google.com/detail/lol-%C3%A9s-g%C3%B6rget%C3%A9skorl%C3%A1toz%C3%B3/mkoeoadekdfnbjpmhiabpjlmhdoimcml](https://chromewebstore.google.com/detail/lol-%C3%A9s-g%C3%B6rget%C3%A9skorl%C3%A1toz%C3%B3/mkoeoadekdfnbjpmhiabpjlmhdoimcml) — azonosítója: `mkoeoadekdfnbjpmhiabpjlmhdoimcml`.

1. A közzétett elem azonosítója (`mkoeoadekdfnbjpmhiabpjlmhdoimcml`) a telepítő alapértelmezése éles módban; csak akkor kell átírni, ha új elemet tesznek közzé (a telepítő „Chrome-bővítmény” oldalán vagy `install.ps1 -ExtensionId …`).
   Ez eltér a fejlesztői azonosítótól (`iidlgdeiobckahalgoabbobfdlefkagi`).
2. A telepítő beállítja a kötelező telepítést és a Native Messaging host engedélyezett eredetét ehhez az azonosítóhoz. Ellenőrzés: `chrome://policy`, `chrome://extensions` („A rendszergazda telepítette”) és a [telepítési útmutató](TELEPITES.md) 4. pontja.
3. Meglévő fejlesztői teszttelepítésen: futtasd újra a telepítőt „Éles” módban az áruházi azonosítóval, és töröld a kicsomagolt példányt.

## 7. Új verzió

1. Emeld a verziót az `extension/manifest.json`-ban (a Chrome csak magasabb verziót fogad el), készítsd el az új `extension-store.zip`-et.
2. Irányítópult → az elem → **Package** → új csomag feltöltése → közzététel. A felhasználók Chrome-ja magától frissít.

## Megjegyzések az ellenőrzéshez

A `nativeMessaging` és a több oldalra szóló gazdagép-hozzáférés alaposabb ellenőrzést kap, ezért a bírálat tovább tarthat; a fenti indoklások és a tesztelési útmutató ezt gyorsítják.
Ha elutasítják, az irányítópult megadja az okot; a leggyakoribb: hiányzó vagy nem nyilvános adatvédelmi nyilatkozat, nem indokolt engedély, vagy a leírás nem egyezik a működéssel.
