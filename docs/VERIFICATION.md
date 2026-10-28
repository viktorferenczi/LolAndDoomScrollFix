# Verification and tests

English | [Magyar](ELLENORZES.md)

The texts in the "Expected result" columns are the ones of the **English** interface. In the Hungarian interface the same messages appear in Hungarian
(see [ELLENORZES.md](ELLENORZES.md)). The language can be forced for a test with `/LANG=en` / `/LANG=hu` in the installer,
or by changing `HKLM\SOFTWARE\LolScrollLimiter\Language` (restart the service and the tray application).

## Quick check after installation (after every installation)

These depend on live Windows behaviour; unit tests do not cover them.

| # | Step | Expected result |
|---|---|---|
| S.1 | Administrator PowerShell: `Get-Acl "C:\Program Files\LolScrollLimiter\Limiter.Service.exe" \| Format-List` | Readable; SYSTEM and Administrators: FullControl, Users: ReadAndExecute (`install.ps1` checks this itself and stops with an error if not) |
| S.2 | `Get-Service LolScrollLimiter` | `Running` |
| S.3 | As a normal user: the tray icon and the extension's popup | Green icon, "Protection is working." — not "The service's session cannot be queried" |
| S.4 | As a normal user, with a running LoL client: `Limiter.Service.exe --diagnose` | Prints the phase and the player — not "discovering the LoL client failed" |

## Load during a game and stuttering

| # | Step | Expected result |
|---|---|---|
| G.1 | Task Manager → Details: CPU use of `Limiter.Service.exe` and `WmiPrvSE.exe` **with no LoL client open** for 1 minute | Practically 0 % (the expensive WMI query does not run while there is no client) |
| G.2 | The same during a running match | The service's CPU use is negligible, and there is no 3-second-rhythm peak |
| G.3 | Play a whole match with the service, then the same with the service stopped | No difference in frame rate / stuttering |
| G.4 | The service's log after a match | No "New PvP search cancelled" / "match accept declined" entry during the match's phases (only possible while searching and at the accept prompt) |

## Admin settings

From a normal (non-administrator) Windows account.

| # | Step | Expected result |
|---|---|---|
| A.1 | Tray icon → Admin settings… (or the Details window's button) | The Windows UAC prompt asks for the administrator password; without it nothing opens |
| A.2 | Cancel in the UAC prompt | "The admin panel did not open: the administrator approval (password) was not given"; nothing changes |
| A.3 | After the correct password | The editor opens with the **current** values (equal to the ones given at install time) |
| A.4 | Change values (e.g. short videos 20 minutes, daily reset 04:00, weekend limit on, LoL banned hours 23-6) → Save | "The settings were saved"; `C:\ProgramData\LolScrollLimiter\limits.json` contains the new values |
| A.5 | After 30 seconds, in the tray and in the extension's popup | The new limit and the reset rule are visible; a LoL search stops in a banned hour |
| A.6 | Banned period with start = end ticked → Save | An error message, the file does not change |
| A.7 | As administrator, break `limits.json` (`{ not json`), then Admin settings | A warning, the editor opens with the defaults; after saving the file is fine |
| A.8 | As a normal user, `Limiter.Tray.exe --admin` directly | "can only be opened with administrator rights", no editor opens |
| A.9 | As a normal user, opening / changing `limits.json` in Windows | Access denied (not possible without the editor either) |
| A.10 | After saving, restore the original install values, then the update button (1.4.x → newer) | The update keeps the settings saved with the editor |
| A.11 | After approving UAC | The **admin password** prompt appears; empty field / Cancel → no editor opens |
| A.12 | Wrong password | "Wrong password (1/5)", about 1 s delay; after the fifth wrong attempt it closes, the editor does not open |
| A.13 | Correct password | The editor opens |
| A.14 | Change password… → new password twice (also try a mismatch, a short one, only spaces) | Mismatch/short/spaces: error message, nothing changes; with a valid one "changed"; the old password no longer works, the new one does |
| A.15 | Update from an older (password-less) version, then Admin settings | After UAC a new password has to be given (twice); then it opens normally |
| A.16 | As administrator, break `C:\ProgramData\LolScrollLimiter\admin.json`, then Admin settings | "The password file is unreadable or damaged…", the editor does not open; after deleting the file a new password can be set |
| A.17 | As a normal user, opening `admin.json` | Access denied; the file contains no readable password (only `salt`/`hash`) |

### Extra allowance for a day

For Saturday/Sunday checks, the system time can be changed, or choose the right days from the calendar.

| # | Step | Expected result |
|---|---|---|
| X.1 | Admin settings → Extra allowance: the day chooser's first day | The "current day"; no past day before it |
| X.2 | Daily reset set to 03:00, opened before 03:00 | The "current day" is the previous calendar day |
| X.3 | Weekend limit off: Saturday and Sunday in the list | Grey, "not selectable: weekend, no limit"; a warning on click, the selection stays on the previous day |
| X.4 | Tick the weekend limit | Saturday and Sunday become selectable immediately |
| X.5 | An extra for a weekend, then turn the weekend limit off | A red warning in the group; Save → error message, the file does not change; after deleting the extra, saving works |
| X.6 | Today +2 LoL, +15 min short videos, +10 min feed → Save; the tray after 30 s | "LoL: ... of 5, including +2 extra from the admin"; "... of 75 min, including +15 min ..."; in the popup "(+2 extra)", "(+15 min extra)" |
| X.7 | All values 0 → Save extra for the day | An error message ("Enter at least one value") |
| X.8 | New values for the same day | Overwrites the existing one (one row in the list); choosing the day shows the existing values in the fields |
| X.9 | Delete extra from the day | The row disappears, the fields go to zero; after Save the tray shows no extra |
| X.10 | An extra for tomorrow, today's limit used up | The "available again" time takes tomorrow's extra into account (at midnight in rolling mode, if that is enough) |
| X.11 | Short-video extra +10, using up the limit to 60+10 minutes | It blocks at 70 minutes, not earlier |
| X.12 | An extra during banned hours | Does not lift the ban |
| X.13 | The extra's day ends (midnight / the reset hour) | The limit returns to the base |
| X.14 | `limits.json` after saving | The `extraGrants` array contains `"date": "yyyy-MM-dd"` entries; expired days are not in it |

## Update button

Prerequisite: the repository is public, and at least two releases (e.g. 1.3.2 and newer) are published according to `release.yml`.

| # | Step | Expected result |
|---|---|---|
| U.1 | Tray icon → Check for updates, with the latest version | "The program is up to date (version: X)" |
| U.2 | The same from an older version | The new version and the changes appear; "Download and install it now?" |
| U.3 | No → | Nothing is downloaded |
| U.4 | Yes → UAC prompt; after typing the administrator password | A silent install with a progress window, then the tray application restarts with the new version (menu: "Version: ...") |
| U.5 | After the update, `limits.json`, the counters in the tray | The limits, the bonus, the banned hours and the used time are unchanged; the service runs (`Get-Service LolScrollLimiter`) |
| U.6 | Start an update but Cancel in the UAC prompt | "The installation did not start: the administrator approval was not given"; nothing changes, the limits work |
| U.7 | Network off, then the button | "Cannot reach GitHub…"; the limits keep working |
| U.8 | With a private repository or without a release | "There is no published release yet, or the releases are not public." |
| U.9 | (Developer) a release whose `SHA256SUMS.txt` is corrupted | After the download "the checksum does not match", the installer does not start |
| U.10 | A manual install (not an update) over an existing installation | The whole wizard appears with the limits pages, and the new values override the old ones |

## 0. First step: verifying the LoL client

Riot does not officially support the client's local API, so before real use the cancelling of a search and the remake detection have to be verified on the **current** client. As a normal user, with a running LoL client:

| # | Step | Expected result |
|---|---|---|
| 0.1 | `Limiter.Service.exe --diagnose` on the home screen | Phase: `None` or `Lobby`; the signed-in player is shown |
| 0.2 | A ranked/normal lobby, then `--diagnose` | Classification: `Pvp` |
| 0.3 | TFT, bot, practice and custom lobbies, then `--diagnose` | Classification: `NotPvp` |
| 0.4 | Start a PvP search, meanwhile `--diagnose --test-cancel` | `Cancelling the search: HTTP 204` (or 200), the search stops in the client |
| 0.5 | A PvP search, at the match-accept pop-up `--diagnose --test-cancel` | `Declining the match accept: HTTP 204`, the client shows it as declined |
| 0.6 | After an earlier remake, `--diagnose` | `remake: True` for the remade match, `False` for a short but not remade match |

If any of 0.4-0.6 does not give the expected result, the program still lets everything through and reports (fail-open), but the LoL limit will not be reliable — report the problem with the output.

## 1. Automatic tests

```bash
dotnet test tests/Limiter.Core.Tests
```

```bash
node --test extension/test/*.test.js
```

| Area | Tests |
|---|---|
| Rolling 24-hour boundary | `LolLimitTests.Three_pvp_matches_block_…` (blocks at 23:59:59, released at 24:00:01), `WebLimitTests.Available_again_time_…` |
| Gradual release of the time limit | `WebLimitTests.Sixty_minutes_block_and_time_is_released_gradually_…` |
| Profile switching / several Chrome profiles | `WebLimitTests.Two_chrome_profiles_reporting_simultaneously_…`, `LolLimitTests.Counters_are_per_windows_user_…` |
| Restart | `LolLimitTests.Counters_survive_service_restart`, `WebLimitTests.Usage_survives_restart` |
| Remake | `LolLimitTests.Verified_remake_gives_back_the_slot_…`, `Remade_game_reported_again_…`, `LcuJsonTests.Remake_requires_the_early_surrender_flag` |
| Repeated match events | `LolLimitTests.Reconnect_and_repeated_events_…`, `Third_game_in_progress_and_its_reconnect_are_allowed` |
| Non-PvP modes, spectating | `LolLimitTests.Non_pvp_modes_never_count_…`, `Spectating_does_not_count`, `ClassifierTests` |
| Web time measurement | `background.test.js`: focus, background tab, locking, sleep, blocking within 1 s, fail-open |
| Detection | `detect.test.js`: URL rules, pop-up Reels, Messenger/profile/group exception |
| Extension texts | `locales.test.js`: English and Hungarian catalogs have the same keys and placeholders, every key used by the code exists, no dead texts |
| Daily reset, weekends | `ScheduleTests`: reset at the chosen hour (web and LoL limit), 00:00 reset, weekend exemption, a match started on Sunday evening does not count on Monday either, weekend from the reset hour, weekend limit on, Friday-evening ban released at the start of the weekend, daylight saving time, reading the `limits.json` written by the installer |
| Rest-day bonus | `RestDayBonusTests`: +30 min / +1 match after an empty day, the limits are independent, it does not stack, any usage the day before rules it out, a remake does not spoil it, the day counted from the reset in daily mode, Friday counts on Monday for an exempt weekend, Sunday with the weekend limit on, no bonus for a 0 limit, in rolling mode the release time accounts for the bonus ending at midnight, no bonus on a fresh install and after a partially measured day, the start of the measurement survives a restart, with an older database the first recorded usage is the start |
| Extra allowance | `ExtraGrantTests`: the extra adds to the limits, only on its own day, banned hours still win, 0 limit + extra, stacking with the rest-day bonus, exempt weekends, rolling midnight, daily reset hour, the day chooser (current day, weekends, time zone, DST, existing grants), pruning, validation, JSON round trip |
| Admin editor background | `SettingsFileTests`: value validation (every range, start = end is an error only when enabled), reading the saved file back with the same function as the service, replacement in one step and without a temporary file, an invalid save does not touch the file, handling of missing / invalid / old files |
| Admin password | `AdminPasswordTests`: length rules, verifying right/wrong passwords, Unicode composition, salted verifier, save/load, damaged files are "corrupt" not "missing", failing closed, the installer hand-over format |
| Languages | `LocalizationTests`, `LimitSettingsEqualityTests`: the active language picks the text, validation and password messages are translated |
| Updating | `UpdateTests`: parsing and comparing versions (only strictly newer), only the repository's own release addresses and GitHub redirects are trusted, checksum from `SHA256SUMS.txt` or GitHub's `digest` field, handling of releases without checksum / without installer / too large / draft / prerelease, private repository, rate limiting and network error messages, download with matching / wrong checksum and size, rejecting a foreign address and redirect |
| Banned hours | `BannedHoursTests`: start hour inclusive, end exclusive, a period spanning midnight, equal start and end = no ban, LoL search banned even with a limit left (not TFT), a match in progress and reconnecting allowed, the web limits can be banned separately, the limit and the reset do not change, a later release when the limit is used up, no ban on an exempt weekend, local time, reading `limits.json` |

### Windows end-to-end test

`.github/workflows/e2e-windows.yml` (push to the `ci/e2e` branch, or start it by hand) installs the program silently on a Windows machine and drives it with UI Automation, **once in English and once in Hungarian**: the installer language is recorded, the password helper, the password prompt (wrong password, correct accented password), the admin panel (day chooser, saving an extra) and the uninstaller (wrong password aborts, correct one removes). Screenshots and logs are in the run's artifacts. See `tools/e2e-windows.ps1`.

## 2. Windows tests (LoL)

A tip to speed up: as administrator set `"lolPvpMatches": 1` in `limits.json`, then back to 3 at the end.

| # | Step | Expected result |
|---|---|---|
| 2.1 | Play as many PvP matches as the limit | In the tray "LoL PvP: used up (n/n) — available again: …" |
| 2.2 | Quit during the last match, then reconnect | Reconnecting is allowed, the counter does not grow |
| 2.3 | A PvP search from a custom lobby | The search stops within 1-2 s; notification: "New PvP search cancelled…" |
| 2.4 | A PvP search from a group lobby (you are the leader, then someone else is) | As leader the search stops; as a member the match accept is declined at the latest |
| 2.5 | TFT search, bot game, practice, custom game, spectating | All available, the counter does not grow |
| 2.6 | 1-2 minutes after a remade match | Notification: "Confirmed remake…", the counter drops by one |
| 2.7 | Restarting the computer in a blocked state | The block stays |

## 3. Browser tests

Tip: `"shortVideoMinutes": 2`, `"facebookFeedMinutes": 2`.

| # | Step | Expected result |
|---|---|---|
| 3.1 | YouTube Shorts for 1 minute, then TikTok for 1 minute | The shared limit is used up; a blocking overlay on both sites, the video stops within 1 s |
| 3.2 | Then Instagram Reels and Facebook Reels | Blocked at once (shared limit) |
| 3.3 | Facebook home page | Separate limit: still usable; Messenger, profile and groups are never blocked |
| 3.4 | A pop-up Reel on the feed | It uses the short-video limit (the pop-up is blocked once that is used up), not the feed's |
| 3.5 | Switching to another tab / minimizing the window | No time is used (popup icon: the remainder does not change) |
| 3.6 | A direct link (e.g. `youtube.com/shorts/…`) in a new tab, while blocked | Blocked |
| 3.7 | Reloading a blocked page (F5) | The block stays |
| 3.8 | A video plays, the mouse does not move for 3 minutes | Time is used |
| 3.9 | Locking the computer (Win+L) during a video | No time is used while locked |
| 3.10 | A second Chrome profile | The same limit, the same block |
| 3.11 | The browser in English, then in Hungarian (Chrome display language) | The popup and the blocking overlay change language; the tray follows the installer's language |

## 3/b. Installer settings tests

| # | Step | Expected result |
|---|---|---|
| 3b.1 | Install with the defaults | `limits.json`: `rolling`, 60/60 minutes, `enforceOnWeekends: false`; tray: "Every limit covers the previous 24 hours…", "There is no limit on weekends…" |
| 3b.2 | Reinstall: daily reset 03:00, 20/10 minutes | The tray and the extension's popup show "reset daily at 03:00"; the blocking overlay says "20 minutes per day (reset at 03:00)" |
| 3b.3 | Daily mode: use up the limit, then set the clock to after 03:00 (as administrator) | The limit is released completely |
| 3b.4 | On a weekend (or with the system time set to Saturday), without the weekend limit | Tray: "weekend — no limit"; no blocking, no LoL cancelling; on Monday the weekend use is not visible |
| 3b.5 | The same with "The limits also apply on weekends" ticked | The limits block on Saturday too |
| 3b.6 | An invalid minute value (e.g. `abc` or `2000`) in the installer | The installer does not proceed, with an error message |
| 3b.7 | A whole day (Tuesday) without short videos, LoL and feed; look at the tray on Wednesday | "… left (of 90 min, including +30 min rest-day bonus)", LoL: "of 4, including +1 rest-day bonus" |
| 3b.8 | 1 minute of short videos on Wednesday; the tray on Thursday | Short videos: 60 minutes (no bonus); for the feed, if there was none on Wednesday either, still +30 |
| 3b.9 | Monday morning, no LoL on Friday (with an exempt weekend) | +1 match |
| 3b.9b | **Fresh install** (empty database), look at the tray on the install day and the next day | No bonus: LoL "of 3", short videos and feed "of 60 min"; the first bonus can appear on the second day after the install |
| 3b.10 | Install with the Rest-day bonus page: LoL 2, short videos 15, feed 0 | `limits.json`: `2` / `15` / `0`; after an empty day the tray shows +2 matches and +15 minutes, no bonus for the feed |
| 3b.11 | An invalid bonus (e.g. LoL `11`, minutes `abc`) | The installer does not proceed, with an error message |
| 3b.12 | Banned hours: LoL ticked, from the current hour to the next (e.g. 14:00-15:00) | A PvP search stops at once, notification: "…a banned period is active"; tray: "LoL PvP: banned period (14:00–15:00) — available again: today 15:00"; TFT and practice are available; after 15:00 searching works again |
| 3b.13 | Banned hours: short videos ticked for the current hour | YouTube Shorts / TikTok show the "Banned period" blocking overlay at once, the Facebook feed does not (if it is not ticked) |
| 3b.14 | Banned period: start = end (e.g. 03:00-03:00) | The installer does not proceed, with an error message |
| 3b.15 | Admin password page: empty, 5 characters, two different passwords | An error message, the installer does not proceed |
| 3b.16 | Install with an accented password (e.g. `árvíztűrő1`) | The accented password works in Admin settings |
| 3b.17 | The temporary password file after the install | `%TEMP%\is-*.tmp\lsl-admin.dat` does not remain; `admin.json` only contains `salt`/`hash` fields |
| 3b.18 | Reinstall (not with the update button) over an installation that already has a password | It asks for the current password first; with a wrong password it does not continue |
| 3b.19 | Uninstall (Settings) with a wrong password / Cancel | The uninstall is aborted, the program and the service stay |
| 3b.20 | Uninstall with the correct password | It removes the program; `admin.json` is deleted |
| 3b.21 | A silent install (`/SILENT`), without the password page | The install succeeds; a password has to be given the first time the Admin settings are opened |
| 3b.22 | Installer language: a Hungarian Windows, an English Windows, another language (a dialog asks), `/LANG=hu` | The installer, the tray and the admin panel use the right language; `HKLM\SOFTWARE\LolScrollLimiter\Language` has `en` or `hu`; an update keeps the stored language |

## 4. Protection tests (as a normal user)

| # | Step | Expected result |
|---|---|---|
| 4.1 | `chrome://extensions` — switching off / removing | Not possible ("Installed by your administrator") |
| 4.2 | Guest mode | Cannot be opened |
| 4.2b | An incognito window | Can be opened. With "Allow in Incognito" on: short videos are measured, blocked when the limit is used up, the limit is shared with the normal windows; off: not measured, not blocked. The popup shows the switch's state |
| 4.3 | Turning developer mode on | Not possible |
| 4.4 | Opening `C:\ProgramData\LolScrollLimiter` / deleting or changing `limiter.db` | Access denied |
| 4.5 | `sc stop LolScrollLimiter` or Services → Stop | Access denied |
| 4.6 | Ending `Limiter.Service.exe` from Task Manager | Access denied |
| 4.7 | Ending `Limiter.NativeHost.exe` from Task Manager | The extension reconnects within a few seconds |
| 4.8 | (As administrator) stopping the service | The tray icon turns red, "!" on the extension, everything is let through; within 5-10 s after starting the service it recovers by itself |
| 4.9 | (As administrator) killing the service's process | Windows restarts it after 5 s; it recovers |
| 4.10 | Breaking `limits.json` (as administrator) | A yellow warning, the previous limit stays |
