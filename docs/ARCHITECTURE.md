# Architecture

Clash Tracker is a Windows tray app that draws its own translucent window over Clash of Clans
when the player presses a hotkey. It never touches the game: no injection, no memory reading,
no input automation, no packet capture. Its keyboard hook only looks for the app's own shortcuts. It finds the game's window (title, process name,
rectangle, minimised state) and reads two sanctioned data sources, the official Clash of Clans
API and the village JSON the player exports from the game.

```
               ┌──────────────── ClashOverlay.App (WPF, net8.0-windows) ────────────────┐
  tray icon ──►│ App / TrayIconService / OverlayCoordinator / RefreshCoordinator        │
  hotkeys  ──► │ Native/ (WinEvent + keyboard hooks, RegisterHotKey, styles, monitors)   │
               │ Overlay/ (OverlayWindow, GameDetector, HotkeyManager, KeyboardHook)     │
               │ Views/ + ViewModels/ (tabs, account switcher, setup, settings)          │
               └───────┬──────────────────────────┬─────────────────────────┬───────────┘
                       │                          │                         │
        ┌──────────────▼───────────┐  ┌───────────▼───────────┐  ┌──────────▼──────────┐
        │ ClashOverlay.Core        │  │ ClashOverlay.Api      │  │ tools/AssetFetch    │
        │ (net8.0, no UI)          │  │ (net8.0)              │  │ (net8.0 exe + lib)  │
        │ GameData + IGameDataSrc  │  │ IClashApiClient       │  │ AssetFetcher        │
        │ ClashKing loader/updater │  │ disk cache, backoff   │  │ manifest diff       │
        │ Export parser            │  │ invalidIp handling    │  │ WebP → PNG (Skia)   │
        │ Merge (export + API)     │  │ ApiKeyStore (DPAPI)   │  └─────────────────────┘
        │ Calculations             │  │ PublicIp, DevSiteKeys │
        │ AssetPaths               │  └───────────────────────┘
        └──────────────────────────┘
```

## Projects

* **`src/ClashOverlay.Core`**: everything that is game logic and has no UI.
  * `Data/`: `GameData` (items keyed by export id), `ItemDef`/`LevelDef` (normalised so every
    level's cost/time is "to reach this level"), `IGameDataSource` and `ClashKingDataSource`
    (reads the downloaded JSON), `StaticDataUpdater` (download + manifest); `GameData` also
    holds the per-hall unlock counts and which merges consume which buildings.
  * `Export/`: `ExportParser` → `VillageExport` (unknown ids become warnings).
  * `State/`: `VillageState` (what the player has, after merging the export with the API and
    advancing timers by the time since export), built by `VillageStateBuilder` (the merge rule).
  * `Calc/`: `ProgressCalculator` (per category and overall, levels or time weighted, costs and
    times to max), `RushCalculator` (below the previous Town Hall's max), `UpgradePlanner`
    (longest available builder, lab and pet upgrades, active upgrades), all for both villages.
  * `Assets/AssetPaths`: ClashKing CDN path for any item/level; the local PNG path.
  * `Settings/`: `AppSettings` and friends (JSON in `%LOCALAPPDATA%\ClashOverlay\settings.json`),
    `HotkeyGesture`, `GameWindowRule`.
  * `Updates/`: `ReleaseChecker` (one call to GitHub's `releases/latest`, a version compare, and
    the release's setup and `SHA256SUMS.txt` assets, read only when their links are this
    repository's release downloads) and `UpdateDownloader` (downloads the setup into `updates\`
    and keeps it only if it matches both the digest GitHub reports and the checksum list).
* **`src/ClashOverlay.Api`**: `IClashApiClient` / `ClashApiClient` (HttpClient, typed DTOs,
  per-account disk cache with timestamps, 429/5xx backoff, typed errors including
  `InvalidIp`), `ApiKeyStore` (DPAPI, CurrentUser), `PublicIpService`, and the optional
  `DeveloperSiteKeyManager` (log in to developer.clashofclans.com and create a key for the
  current IP, like coc.py; off by default).
* **`tools/AssetFetch`**: builds the list of every image the static data implies, diffs it
  against ClashKing's `manifest.json` (or `HEAD` + `Last-Modified`), downloads what is missing
  or changed, converts WebP to PNG with SkiaSharp and writes `assets\manifest.json`. A console
  app, also referenced by the App so the tray's "Check for data update" runs the same code.
* **`src/ClashOverlay.App`**: the WPF app. Win32 interop lives in `Native/` only.
* **`tests/ClashOverlay.Tests`**: xUnit. Two kinds of tests:
  * deterministic ones against small hand-built data sets and anonymised fixtures
    (`tests/fixtures/`), for the calculations, parser, merge, API client and asset paths;
  * data-driven ones against the **downloaded** ClashKing data (the Town Hall 19 checks, merge
    counts, upgrade-time semantics, the demo fixture loading without unknown ids), so a bad
    data update fails loudly. They use `CLASHOVERLAY_DATA_DIR`, else
    `%LOCALAPPDATA%\ClashOverlay\data`, else download into `%TEMP%\ClashOverlayTestData`.

## Runtime folders

Everything the app writes lives in `%LOCALAPPDATA%\ClashOverlay\`:

| Path | What |
| --- | --- |
| `settings.json` | settings and the account list |
| `apikey.bin` | the API key, DPAPI-protected for the current Windows user |
| `devsite.bin` | optional developer-site login, DPAPI-protected |
| `data\` | ClashKing static data + `manifest.json` |
| `assets\` | PNG artwork + `manifest.json` |
| `accounts\<tag>\export.json` | the account's latest village export |
| `cache\<tag>\`, `cache\clan-<tag>\`, `cache\cwl\`, `cache\global\` | cached API responses with fetch time and max age |
| `cache\icons\` | league, label and clan badge icons from the API |
| `updates\` | the next version's setup while it is verified and installed |
| `logs\` | rolling log files (`demo-*.log` for demo copies, `update-install.log` from the last update's setup; the API key is never logged) |

## Flow

1. **Start**: single-instance mutex `Local\ClashOverlay.SingleInstance` (`Local\ClashOverlay.Demo`
   for a demo copy). The installer looks for these mutexes and sets the event
   `Local\ClashOverlay.Exit` to close every running copy before it replaces the exe. Settings
   load, logging starts, the tray icon appears. If no key is stored, first-run setup opens (but
   not when Windows starts the app at sign-in with `--startup`). If no static data is present it
   downloads in the background.
2. **Detect**: `GameDetector` listens to `SetWinEventHook` events and polls every 3 s. It tries
   the window in front first, then every top-level window, so when several windows match, the
   one the player is using wins. Google Play Games titles its window "Clash of Clans - <name>",
   so its default rule is `Clash of Clans*` on `crosvm.exe`; rules saved by 1.0 are widened on
   load. The game counts as in front when the foreground window, its root owner or its process
   is the game's. There is no notification.
3. **Hotkeys**: `HotkeyPlan` decides what is wanted: the toggle (Ctrl+R) while the game is in
   front, and Ctrl+B, Ctrl+Tab, Ctrl+1…9 and Esc while the overlay is open. `HotkeyManager`
   watches for them two ways at once. `KeyboardHook` is a `WH_KEYBOARD_LL` hook on its own
   thread, installed only while some shortcut is wanted; it swallows only a matching key press
   and passes every other key on untouched. It is needed because emulators such as Google Play
   Games stop `RegisterHotKey` shortcuts from firing while they are focused. The keys are also
   registered with `RegisterHotKey`, which still works when the game runs as administrator. A
   150 ms guard drops a second report of the same press. All rebindable.
4. **Show**: `OverlayCoordinator` positions the overlay (centre of the game's monitor, or over
   the game window), fades it in, and asks `RefreshCoordinator` to refresh stale API data.
   The overlay is `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` and answers `WM_MOUSEACTIVATE` with
   `MA_NOACTIVATE`, so clicking it never takes focus from the game. Because it never takes
   focus, list items (the section tabs) are selected on mouse press by the window itself.
   Dragging anywhere that is not a control moves it; the four corner grips resize it with the
   1260:760 shape kept (`OverlayGeometry.Resize`). The position and scale are saved in
   `OverlayLeft`/`OverlayTop`/`Scale` and replace the placement setting until reset.
5. **Compute**: `AccountSession` merges the export with the cached API player into a
   `VillageState`, then the calculators produce the view models for every tab.
6. **Hide**: on the hotkey, Esc, the game being minimised or closed.

## Releases and updates

* **Version**: one property, `ClashVersion` in `Directory.Build.props` (`0.0.0-dev` unless a build
  passes `-p:ClashVersion=X.Y.Z`). The release workflow takes it from the `vX.Y.Z` tag, and it
  ends up in the assembly, file and product versions, the Settings sidebar and the installer.
* **Publish**: `Properties/PublishProfiles/win-x64.pubxml` makes one self-contained
  `publish/ClashOverlay.exe`, with the Inter licence beside it in `licenses\`.
* **Installer**: `installer/ClashOverlay.iss` (Inno Setup 6) installs per user into
  `%LOCALAPPDATA%\Programs\ClashOverlay` with no admin prompt. It writes the same `HKCU\…\Run`
  value as the app's Start with Windows switch (`StartupRegistration`). A silent upgrade closes
  the running copy through `Local\ClashOverlay.Exit` and starts the new one with `--startup`.
  On uninstall it asks
  whether to delete `%LOCALAPPDATA%\ClashOverlay`. A silent uninstall keeps the folder unless
  `/DELETEDATA` is passed.
* **Workflows**: `.github/workflows/ci.yml` builds, tests and compiles the installer on every
  push and pull request. `.github/workflows/release.yml` runs on `v*` tags: it tests, publishes,
  signs if secrets are set, builds the installer, zips the portable build, writes
  `SHA256SUMS.txt` and creates the release with the notes from `CHANGELOG.md`.
* **Updates**: `UpdateWatcher` asks GitHub 15 s after every start (retrying after 2, 10 and
  30 minutes if the network is not up yet at sign-in) and when the player presses Check now; it
  never checks in a `0.0.0-dev` build. For an installed copy with **Install updates
  automatically** on (the default), it downloads and verifies the setup with `UpdateDownloader`,
  waits while the overlay is open, and runs it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
  A tag that was tried in the last day is not retried automatically. A portable copy only shows
  a "Download vX.Y.Z…" tray item that opens the release page. Nothing pops up; state shows in the
  tray menu and Settings → Data and updates.

## Decisions

* Calculations treat every level uniformly as "cost/time to reach level N"; the loader does the
  shift for units (whose data stores "from N to N+1").
* Merge-adjusted counts are derived, not looked up (see DATA_NOTES).
* Builder's Huts and B.O.B's Hut count what the player has (they are bought with gems or come
  from the Builder Base), not what the Town Hall would allow.
* Excluded from max/rushed totals: seasonal troops and spells, super troops, squads, the
  Crafting Station's seasonal modules, helpers (gems), gear-ups, supercharges, the Town Hall
  and Builder Hall themselves, decorations, obstacles.
* Rush score = levels behind the previous Town Hall's max, summed, walls and equipment shown
  but not scored.
