# Installation guide

English | [Magyar](TELEPITES.md)

## 1. Roles and prerequisites

- **You** use a normal (non-administrator) Windows account.
- **The trusted person** knows the administrator password. For installing, uninstalling and changing the limits, they type the password into the UAC prompt. You should not have that password.
- Windows 11 (x64) and Google Chrome.
- For building (once, on any machine): .NET 10 SDK, Node.js 18+, [Inno Setup 6](https://jrsoftware.org/isdl.php).

If your computer currently has an administrator account, first create an administrator account for the trusted person, then change your own account to a normal user (Settings → Accounts → Other users → Change account type).

## 2. Language

The installer, the tray application, the admin panel, the service messages and the Chrome extension are available in **English and Hungarian**.

- The installer follows the Windows display language. If that is neither English nor Hungarian, it asks. Start it with `/LANG=hu` or `/LANG=en` to force a language.
- The language chosen in the installer is recorded in `HKLM\SOFTWARE\LolScrollLimiter\Language` (`en` or `hu`) and used by the tray application, the admin panel and the service. On an update the stored language is kept. Without that value, the Windows display language decides.
- The Chrome extension follows the browser's display language (English is the default).

## 3. Building

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Output in the `out\` folder:

| File | Purpose |
|---|---|
| `LolScrollLimiter-Setup-1.6.1.exe` | Windows installer |
| `extension-store.zip` | Upload to the Chrome Web Store |
| `extension-dev.zip` | Development test version (loaded unpacked, fixed ID: `iidlgdeiobckahalgoabbobfdlefkagi`) |
| `app\` | The program files to install |

## 4. Publishing the extension (mandatory for real use)

> Everything needed for the upload (store texts, permission justifications, test instructions, privacy policy) is in [CHROME_WEBSTORE.md](CHROME_WEBSTORE.md); the steps are summarized below.

On Windows, on a computer that is not domain-joined, Chrome allows the forced installation of an extension **only from the Chrome Web Store** ([Chrome release terms](https://support.google.com/chrome/a/answer/7532015)).

1. Register as a developer: <https://chrome.google.com/webstore/devconsole> (one-time fee).
2. New item → upload `out\extension-store.zip`.
3. Visibility: **Unlisted** — reachable only by the link; forced installation works that way too.
4. On the privacy tab, state that the extension does not collect or transmit data; the `nativeMessaging` permission is needed for the connection to the local limiter service.
5. Wait for Google's approval. Note the **32-letter extension ID** you get.

> **The published extension:** [Chrome Web Store page](https://chromewebstore.google.com/detail/lol-%C3%A9s-g%C3%B6rget%C3%A9skorl%C3%A1toz%C3%B3/mkoeoadekdfnbjpmhiabpjlmhdoimcml) — its ID: `mkoeoadekdfnbjpmhiabpjlmhdoimcml`. This is what the installer needs in production mode.

Until the approval arrives, the development test version can be used (section 6), but it **gives no real protection**, because the extension can be switched off.

## 5. Installation (production)

1. Run `LolScrollLimiter-Setup-1.6.1.exe` from your own account; the trusted person types the administrator password into the UAC prompt.
2. Installation mode: **Production**.
3. Extension ID: the default is the published store ID (`mkoeoadekdfnbjpmhiabpjlmhdoimcml`); leave it. In development mode the development ID is put into the field.
4. **Limits** page:

   | Setting | Options | Default |
   |---|---|---|
   | Resetting the limits | **Rolling 24 hours**: every use is released exactly 24 hours later (web time minute by minute, gradually). **Reset daily**: at the chosen hour (00:00-23:00, local time) every limit resets at once | Rolling 24 hours (03:00 for the daily mode) |
   | Short videos (Reels, TikTok, Shorts) | 0-1440 minutes | 60 minutes |
   | Facebook home feed | 0-1440 minutes | 60 minutes |
   | The limits also apply on weekends | If unticked, there is no limit on Saturday and Sunday, and weekend use (matches too) does not count toward the limit | Unticked |

   **Rest-day bonus** page: how much a limit grows after a skipped day (details: section 7).

   | Setting | Options | Default |
   |---|---|---|
   | LoL PvP | 0-10 matches | +1 match |
   | Short videos | 0-1440 minutes | +30 minutes |
   | Facebook home feed | 0-1440 minutes | +30 minutes |

   0 = that bonus is off.

   **Banned hours** page: one checkbox and two hours (start, end) per limit. When ticked, that activity cannot be used at all between the given hours, even if some of the limit is left (details: section 7).

   | Row | Default |
   |---|---|
   | LoL PvP matches | unticked (01:00-05:00 prefilled) |
   | Short videos | unticked (01:00-05:00) |
   | Facebook feed | unticked (01:00-05:00) |

   The start and the end cannot be the same hour; the installer does not proceed then.

   **Admin password** page (the last one): enter the admin password twice (6-128 characters). It is needed to open the admin settings and to uninstall the program, in addition to the Windows administrator approval. The user should not know it. If an installation with a password already exists, reinstalling first needs its password (updating with the update button does not).
   The password is not stored: only a verifier (PBKDF2-SHA256 with a random salt) is written to `C:\ProgramData\LolScrollLimiter\admin.json`, which only administrators and SYSTEM can access. A silent install (`/SILENT`) has no password page; the password is then asked the first time the Admin settings are opened.

   With a daily reset, the "weekend" counts from the chosen hour: with a 03:00 reset it lasts from Saturday 03:00 to Monday 03:00.
   In rolling mode, from Saturday 00:00 to Monday 00:00. If a limit is used up on Friday, it is released when the weekend begins.

5. The installer:
   - puts the program into `C:\Program Files\LolScrollLimiter` (read-only for normal users),
   - installs and starts the `LolScrollLimiter` service (automatic start, restart after a crash, a normal user cannot stop it),
   - creates the `C:\ProgramData\LolScrollLimiter` data folder (not accessible to normal users),
   - registers the Native Messaging host,
   - sets the Chrome policies: forced extension installation, guest mode disabled, extensions' developer mode disabled (incognito mode is allowed, see below),
   - sets the tray application to start automatically for every user.
6. Restart Chrome. Check:
   - `chrome://policy` — `ExtensionInstallForcelist`, `BrowserGuestModeEnabled` and `ExtensionDeveloperModeSettings` appear.
   - `chrome://extensions` — the extension is "Installed by your administrator" and cannot be switched off.
   - A green clock icon in the tray; "Protection: OK" in the menu.

### Incognito mode

Incognito mode is not disabled. Whether the extension also measures and limits in an incognito window is decided by the
**chrome://extensions → the extension's Details → Allow in Incognito** switch:

| Switch | In an incognito window |
|---|---|
| On | Measures and blocks the same way as in a normal window; the limit is shared |
| Off (Chrome's default) | The extension does not run: no measuring and no blocking |

The extension's popup says which state applies. The user can freely change the switch,
so while it is off, an incognito window can be used to get around the web limit.

## 6. Development test version

1. In the installer choose the **Development test version** mode and leave the default ID.
2. Unzip `out\extension-dev.zip` into a folder.
3. `chrome://extensions` → Developer mode on → **Load unpacked** → choose the folder.
4. The ID must be `iidlgdeiobckahalgoabbobfdlefkagi` (fixed by the manifest's `key` field).

To switch to production, run the installer again in Production mode with the store ID, and remove the unpacked copy.

## 7. Changing the limits (administrator)

### Admin settings (tray application)

The easiest way: the **Admin settings…** button in the tray icon's menu or in the "Details" window (under the "Check for updates" button).

1. The button restarts the program with administrator rights, so Windows **asks for the administrator password** (UAC prompt). The trusted person knows it; without approval the window does not even open.
2. Then the program asks for **the admin password** (the one set at install time). After five wrong attempts it closes; there is a one-second delay after every wrong attempt. If there is no password yet (updated from an older version, or installed silently), a new one has to be set here.
3. The editor appears with every setting that can be given at install time: reset (rolling 24 hours / daily reset and the hour), the number of LoL matches, the minutes of short videos and the Facebook feed, weekend limit, rest-day bonus, banned hours; below them the **Extra allowance for a chosen day**. The **Change password…** button sets a new admin password.
4. **Save**: validates the values (for an invalid value it says what to fix and does not save), then replaces `limits.json` in one step. The service picks it up within 30 seconds at the latest; no restart is needed. Usage that is already recorded counts under the new rule.

#### Extra allowance for a day

In the **Extra allowance for a chosen day** group, choose a day, enter the extra LoL matches and the short-video and feed minutes, then **Save extra for the day**; it takes effect after the **Save** button. There is one entry per day (saving overwrites the existing one). The extra is added to the day's base limit and the rest-day bonus, on that one day only. It does not override banned hours.

- With a daily reset, the "day" lasts from the reset hour to the next one (with a 03:00 reset, "Friday" lasts from Friday 03:00 to Saturday 03:00). So before the reset the first item of the list is still the previous calendar day: for example, on Wednesday at 01:00 the "current day" is Tuesday.
- With rolling 24 hours, the day lasts from midnight to midnight; after midnight the extra ends, but usage that is already recorded still counts for 24 hours.
- **Days that cannot be chosen** (greyed, with a text reason): when the weekend limit is off, Saturday and Sunday, because there is no limit on weekends anyway. The list refreshes immediately when you change the weekend checkbox or the reset mode. Past days are not listed, and an extra can be given at most 30 days ahead.
- If you later turn the weekend limit off, an already given weekend extra causes an error when saving: delete it first.
- At least one value must be given (LoL: 0-100 matches, minutes: 0-1440). Extras of expired days are deleted when saving.
- The extra is shown separately in the tray and in the extension's popup ("including +30 min extra from the admin", "+30 min extra").

Security: the panel is not a new "door" to the service — it writes the same file that only administrators can access anyway, and the service has not received any new command that loosens the limits. If the program receives the `--admin` switch without elevation, it does not open.
If your Windows account is not a normal but an administrator account, UAC by default only asks for approval without a password — password protection needs a normal user account (see section 1).

The same can be done by running the installer again: it always overwrites the earlier values with the values of the **Limits** pages (the counters are kept).

By hand: with an editor running as administrator (e.g. Notepad "Run as administrator"), edit
`C:\ProgramData\LolScrollLimiter\limits.json`:

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
  "facebookFeedBannedHours": { "enabled": false, "from": 1, "to": 5 },
  "extraGrants": [
    { "date": "2026-11-02", "lolMatches": 2, "shortVideoMinutes": 15, "facebookFeedMinutes": 0 }
  ]
}
```

| Field | Value |
|---|---|
| `resetMode` | `"rolling"` (rolling 24 hours) or `"daily"` (daily reset) |
| `dailyResetHour` | 0-23, the hour of the daily reset in local time (only in `"daily"` mode) |
| `lolPvpMatches` | Number of PvP matches |
| `shortVideoMinutes`, `facebookFeedMinutes` | 0-1440 minutes |
| `enforceOnWeekends` | `true`: there is a limit on weekends too; `false`: no limit on weekends |
| `lolRestDayBonusMatches` | Rest-day bonus: this many extra matches if there was no PvP match the day before (0-10; 0 = off) |
| `shortVideoRestDayBonusMinutes`, `facebookFeedRestDayBonusMinutes` | Rest-day bonus in minutes if 0 minutes were used from that limit the day before (0 = off) |
| `lolBannedHours`, `shortVideoBannedHours`, `facebookFeedBannedHours` | Banned hours: `enabled` (on/off), `from` (start hour, 0-23, inclusive), `to` (end hour, 0-23, exclusive). With `from` = `to` there is no ban |
| `extraGrants` | Extra allowances for single days: `date` (`yyyy-MM-dd`, the day's start date), `lolMatches` (0-100), `shortVideoMinutes`, `facebookFeedMinutes` (0-1440). One entry per day; weekends are ignored when the weekend limit is off |

### Rest-day bonus

- The three limits count separately: e.g. if you did not watch short videos yesterday but scrolled the feed, only the short videos get +30 minutes today.
- **It does not stack:** after two or more empty days the bonus is still given only once.
- **Which day counts:** with a daily reset, the time between two resets (e.g. 03:00-03:00); in rolling mode, the calendar day (midnight to midnight).
  The bonus raises the limit for that day; in rolling mode it ends at midnight, and the release time of a block already takes this into account.
- **Weekends:** if there is no limit on weekends, weekend use is not recorded, so on Monday Friday counts (not Sunday).
- **A 0-minute / 0-match limit** earns no bonus (otherwise it would be released every second day).
- **Fresh install:** the program starts measuring at install time, and a bonus is only given for a day that was measured **completely**.
  So there is no bonus on the day of the installation and the day after (the install day is not a full day); the first bonus can come on the second
  day after the installation, if the previous, fully measured day was empty. When reinstalling, the counters and the start of the measurement are kept.
  When updating from an older version, the start of the measurement is the time of the first recorded usage.
- If the computer was off all day, it counts as an empty day (if the program had already been measuring).
- The size of the bonus can be given on the installer's **Rest-day bonus** page (or later in the fields above).

The service picks it up within 30 seconds. With an invalid file the previous limit stays in effect, and the tray icon warns.
When switching modes, usage that is already recorded counts under the new rule (e.g. switching from rolling to daily, only the usage since the last reset).

### Banned hours

- During the banned period the activity cannot be used: for LoL the program cancels a new PvP search and declines a match accept;
  for short videos and the feed the blocking overlay appears with the title "Banned period".
- **The limit and the reset are not affected:** nothing is used up during a ban, and the daily reset also happens during a ban.
  If the limit is also used up at the end of the ban, the release is the later of the two times.
- Whole hours, in local time; the start hour is inclusive, the end hour is exclusive (01:00-05:00 = from 01:00:00 to 04:59:59).
  It may span midnight (22:00-06:00).
- **A match in progress:** a match that is running when the ban starts may finish, and reconnecting works too.
- TFT, bot games, practice, custom games and spectating are available during a ban as well.
- **Weekends:** if there is no limit on weekends, there is no ban either. With a daily reset the weekend counts from the reset hour
  (with a 03:00 reset, Saturday 02:00 still belongs to Friday, so the ban still applies then).

## 8. What does the tray icon's colour mean?

| Colour | Meaning |
|---|---|
| Green | Everything works |
| Yellow | Partial fault: e.g. the LoL client does not respond, an unknown game mode, or Chrome runs but the extension does not report. The affected limit **lets everything through** now |
| Red | The service is not reachable — no limit is enforced. It recovers automatically as soon as the service restarts |

A red "!" on the extension's icon shows that it has no connection to the service.

## 8/b. Updating

The tray icon's menu and the "Details" window both have a **Check for updates** button (and show the current version).

1. The button looks at the latest GitHub release (`viktorferenczi/LolAndDoomScrollFix`). If there is no newer version: "The program is up to date".
2. If there is, it shows the new version and the changes, and asks whether to download it.
3. On Yes it downloads the installer and **verifies its SHA-256 checksum** against the release's `SHA256SUMS.txt` (or the checksum given by GitHub). On a mismatch it deletes the file and does not start it.
4. It starts the installer, which **asks for administrator approval** (Windows UAC). The trusted person types the administrator password here — the user alone cannot install the update, and cannot switch the limits off with it.
5. The installer updates silently: the **settings (limits, bonus, banned hours, weekends) and the counters are kept**, and the tray application restarts. Any new settings of the new version are created with default values; if you want to change them, run the installer by hand (with the limits pages), or edit `limits.json`.

Safety rules: it only downloads from the repository's own release (https://github.com/…/releases/download/…), only offers a **strictly newer** version (no downgrade, no same version), does not install a release without a checksum, and after the download it only lets redirects go to GitHub addresses.
Checking for updates does not affect how the limits work; if the check fails (no internet, GitHub does not respond), the program says so, and every limit keeps working.

**Prerequisite for the releases:** the repository's releases must be public (the tray application queries GitHub without signing in). With a private repository, the button says: "There is no published release yet, or the releases are not public."

**Making a release (developer):** raise the version in `Directory.Build.props`, `extension/manifest.json` and `installer/LolScrollLimiter.iss`, commit and push, then:

```bash
git tag v1.6.1 && git push origin v1.6.1
```

`.github/workflows/release.yml` builds the installer on Windows, creates `SHA256SUMS.txt`, and publishes the release (`LolScrollLimiter-Setup-1.6.1.exe`, `extension-store.zip`, `extension-dev.zip`, `SHA256SUMS.txt`).
The file name and the tag format are fixed (`LolScrollLimiter-Setup-X.Y.Z.exe` / `vX.Y.Z`), otherwise the button does not find the installer.

In production mode the Chrome extension updates itself from the Chrome Web Store; with the development test version, the unpacked extension has to be reloaded on the `chrome://extensions` page after an update.
Without code signing, the installer appears as "Unknown publisher" in the Windows prompt; before approving, the trusted person should check that the user started the update on purpose.

## 9. Uninstalling (administrator)

Settings → Apps → Installed apps → LoL & Doomscroll Limiter → Uninstall. After the Windows administrator approval, the program **asks for the admin password**; with a wrong password or the Cancel button the uninstall is aborted. On a successful uninstall the password verifier is deleted too (a new one has to be given when reinstalling).

**Forgotten password:** the password cannot be recovered, because only a verifier is stored. The Windows administrator can get around it by deleting `C:\ProgramData\LolScrollLimiter\admin.json`, and set a new one the first time the Admin settings are opened. (So the password protects *in addition to* the Windows administrator rights; whoever also owns the administrator account can do anything.) If the file is damaged, the admin panel and the uninstaller do not open, and ask for this file deletion.
The counters are kept (they continue after a reinstall). Full deletion from an administrator PowerShell:

```powershell
& "C:\Program Files\LolScrollLimiter\uninstall.ps1" -RemoveData
```

(run it before the uninstall, or delete the `C:\ProgramData\LolScrollLimiter` folder by hand).

## 10. Troubleshooting

- **Logs:** Event Viewer → Windows Logs → Application, source: `LolScrollLimiter`.
- **Installer log:** a silent install can write a log with `/LOG=setup.log`; the system-setup script also keeps its output in `%TEMP%\LolScrollLimiter-install.log` of the administrator who ran it.
- **Checking the LoL connection by hand** (can be run as a normal user too, with the LoL client running):

  ```powershell
  & "C:\Program Files\LolScrollLimiter\Limiter.Service.exe" --diagnose
  ```

  It prints the phase, the queue's classification (Pvp / NotPvp / Unknown) and the remake flag of the last 10 matches.
  With the `--test-cancel` switch it cancels a search / declines a match accept while searching or at the accept prompt.
- **LoL stutters or freezes during a game:** the program never opens, suspends or stops the game's process;
  it only sends commands to the LoL client while searching or at a match accept (cancelling the search, declining the accept), and
  only if, right before, the client is still in the same phase. During a game it queries the client at most every 15 seconds.
  If you still notice stuttering, isolate the cause:
  1. In an administrator PowerShell: `Stop-Service LolScrollLimiter`, then play a match. If it still stutters, the program is not the cause (graphics driver, overheating, Vanguard, etc.).
  2. If it only stutters with the service: Task Manager → Details → CPU use of `Limiter.Service.exe` and `WmiPrvSE.exe` during a game; send the log (Event Viewer → Application, source: `LolScrollLimiter`) with the time of the game.
- **The extension does not report:** `chrome://extensions` → the extension's details → "Service worker" → console. Check the `HKLM\SOFTWARE\Google\Chrome\NativeMessagingHosts\hu.kmsoft.lolscrolllimiter` key and the `allowed_origins` ID in the JSON file it refers to.

## 11. Known limitations

- **Incognito mode:** if the "Allow in Incognito" switch is off, there is no web limit in an incognito window.
- **Only the managed Chrome is covered.** Other browsers (e.g. Microsoft Edge, Firefox) and mobile apps are not limited. If that is a problem, the trusted person can block them (e.g. with Edge policies or parental controls).
- **Riot does not officially support the LoL client's local API for third parties** ([Riot documentation](https://developer.riotgames.com/docs/lol#league-client-api)). After a client update, check with the `--diagnose` command; on a failed connection the program lets everything through and shows yellow.
- **In a group lobby**, if you are not the leader, the client does not always allow cancelling the search; the program then declines the match accept, which puts the others back in the queue.
- **A remake** is only confirmed by the client's `gameEndedInEarlySurrender` flag; until the match history updates (usually 1-2 minutes) the match counts. After 6 hours it counts for good without confirmation.
- **Gradual release (rolling mode):** web time is released minute by minute, 24 hours + at most 1 minute after the use.
- **Time zone:** the daily reset and the weekend follow the computer's local time; when daylight saving time starts, the skipped hour is replaced by resetting one hour later.
