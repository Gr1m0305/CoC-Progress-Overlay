# Architecture

Clash Tracker is a Windows tray app that draws its own translucent window over Clash of Clans
when the player presses a hotkey. It never touches the game: no injection, no memory reading,
no input automation, no packet capture. It finds the game's window (title, process name,
rectangle, minimised state) and reads two sanctioned data sources, the official Clash of Clans
API and the village JSON the player exports from the game.

```
               ┌──────────────── ClashOverlay.App (WPF, net8.0-windows) ────────────────┐
  tray icon ──►│ App / TrayIconService / OverlayCoordinator / RefreshCoordinator        │
  hotkeys  ──► │ Native/ (SetWinEventHook, RegisterHotKey, window styles, monitors)      │
               │ Overlay/ (OverlayWindow, GameDetector, HotkeyManager)                   │
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
  * `Updates/ReleaseChecker`: one call to GitHub's `releases/latest` and a version compare, for
    the daily new-version check.
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
| `logs\` | rolling log files (the API key is never logged) |

## Flow

1. **Start**: single-instance mutex `Local\ClashOverlay.SingleInstance` (`Local\ClashOverlay.Demo`
   for a demo copy). The installer looks for these mutexes and sets the event
   `Local\ClashOverlay.Exit` to close every running copy before it replaces the exe. Settings
   load, logging starts, the tray icon appears. If no key is stored, first-run setup opens. If no static data is present it downloads in the background.
2. **Detect**: `GameDetector` listens to `SetWinEventHook` events and polls every 3 s. When a
   rule matches it raises `GameAppeared` (tray toast once per session: "Clash of Clans
   detected, press Ctrl+R"). Foreground changes decide whether hotkeys are registered.
3. **Hotkeys**: `HotkeyManager` registers Ctrl+R (toggle), Ctrl+B (village), Ctrl+Tab (next
   account), Ctrl+1…9 (account) only while the game is the foreground window; Esc is added
   while the overlay is open. All rebindable.
4. **Show**: `OverlayCoordinator` positions the overlay (centre of the game's monitor, or over
   the game window), fades it in, and asks `RefreshCoordinator` to refresh stale API data.
   The overlay is `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` and answers `WM_MOUSEACTIVATE` with
   `MA_NOACTIVATE`, so clicking it never takes focus from the game.
5. **Compute**: `AccountSession` merges the export with the cached API player into a
   `VillageState`, then the calculators produce the view models for every tab.
6. **Hide**: on the hotkey, Esc, the game being minimised or closed.

## Releases and updates

* **Version**: one property, `ClashVersion` in `Directory.Build.props` (`0.0.0-dev` unless a build
  passes `-p:ClashVersion=X.Y.Z`). The release workflow takes it from the `vX.Y.Z` tag, and it
  ends up in the assembly, file and product versions, the About page and the installer.
* **Publish**: `Properties/PublishProfiles/win-x64.pubxml` makes one self-contained
  `publish/ClashOverlay.exe`, with the Inter licence beside it in `licenses\`.
* **Installer**: `installer/ClashOverlay.iss` (Inno Setup 6) installs per user into
  `%LOCALAPPDATA%\Programs\ClashOverlay` with no admin prompt. It writes the same `HKCU\…\Run`
  value as the app's Start with Windows switch (`StartupRegistration`). On uninstall it asks
  whether to delete `%LOCALAPPDATA%\ClashOverlay`. A silent uninstall keeps the folder unless
  `/DELETEDATA` is passed.
* **Workflows**: `.github/workflows/ci.yml` builds, tests and compiles the installer on every
  push and pull request. `.github/workflows/release.yml` runs on `v*` tags: it tests, publishes,
  signs if secrets are set, builds the installer, zips the portable build, writes
  `SHA256SUMS.txt` and creates the release with the notes from `CHANGELOG.md`.
* **New-version check**: `UpdateWatcher` asks GitHub at most once a day, when the setting is on
  and the build is not `0.0.0-dev`. A new version shows one tray note and an "Update available"
  menu item, and both only open the release page.

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
