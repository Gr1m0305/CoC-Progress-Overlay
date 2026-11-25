# Data notes

What the app reads, where it comes from, and what was confirmed against the live sources
(checked on 25 Nov 2026, the day after the ClashKing data set's last static-data commit).
Anything below that says **confirmed** was checked against the live file, endpoint or
install; anything that says **assumed** could not be checked without a real API key, a real
export or the game running, and the code is written to fail soft if it is wrong.

## 1. Official Clash of Clans API

* Base URL `https://api.clashofclans.com/v1`, header `Authorization: Bearer <key>`.
  **Confirmed**: without a valid key every endpoint answers `403` with a JSON body
  `{"reason": "accessDenied", "message": "Invalid authorization"}` (or `"Missing authorization"`
  with no header). Errors always have this `{reason, message}` shape.
* Tags go in the path URL-encoded: `#` becomes `%23` (`/players/%23ABC123`).
* Keys are locked to IP addresses (`cidrRanges`). A key used from another IP answers `403`
  with `reason` `accessDenied.invalidIp` and a message of the form
  `Invalid authorization: API key does not allow access from IP 203.0.113.7`. The app pulls
  the IP out of the message when it is there and otherwise asks a what-is-my-IP service.
* Other error reasons handled: `notFound` (404, wrong tag), `requestThrottled` (429, honour
  `Retry-After`), `inMaintenance` (503), `accessDenied.privateWarLog` or plain `accessDenied`
  on war endpoints of clans with a private war log (not a key problem).

Endpoints used (fields as used by the code; names confirmed against the maintainer's own
working Discord bot that calls the live API, and against coc.py's models):

| Endpoint | Used for | Main fields |
| --- | --- | --- |
| `GET /players/{tag}` | profile, merge with export | `tag`, `name`, `townHallLevel`, `builderHallLevel`, `expLevel`, `trophies`, `bestTrophies`, `builderBaseTrophies`, `bestBuilderBaseTrophies`, `warStars`, `attackWins`, `defenseWins`, `donations`, `donationsReceived`, `clanCapitalContributions`, `role`, `warPreference`, `leagueTier {id, name, iconUrls}`, `builderBaseLeague {id, name}`, `clan {tag, name, clanLevel, badgeUrls}`, `labels[] {id, name, iconUrls}`, `achievements[] {name, stars, value, target, info, completionInfo, village}`, `troops[]`, `heroes[]`, `spells[]`, `heroEquipment[]` (each `{name, level, maxLevel, village}`) |
| `GET /clans/{tag}` | clan card | `name`, `clanLevel`, `members`, `badgeUrls`, `warLeague`, `capitalLeague`, `warWins`, `warWinStreak`, `isWarLogPublic`, `clanCapital.capitalHallLevel` |
| `GET /clans/{tag}/members` | member count, own rank | `items[] {tag, name, role, clanRank, trophies, donations}` |
| `GET /clans/{tag}/currentwar` | war card | `state` (`notInWar`, `preparation`, `inWar`, `warEnded`), `teamSize`, `attacksPerMember`, `startTime`, `endTime`, `preparationStartTime`, `clan`/`opponent {name, badgeUrls, stars, destructionPercentage, attacks, members[] {tag, attacks[]}}` |
| `GET /clans/{tag}/warlog` | recent results (public logs only) | `items[] {result, endTime, opponent}` |
| `GET /clans/{tag}/currentwar/leaguegroup` | CWL | `state`, `season`, `rounds[] {warTags[]}` (`#0` = not drawn yet) |
| `GET /clanwarleagues/wars/{warTag}` | CWL round | same shape as `currentwar` |
| `GET /clans/{tag}/capitalraidseasons?limit=1` | raid weekend | `items[] {state, startTime, endTime, capitalTotalLoot, raidsCompleted, totalAttacks, members[] {tag, attacks, attackLimit, bonusAttackLimit, capitalResourcesLooted}}` |
| `GET /goldpass/seasons/current` | Gold Pass end | `startTime`, `endTime` |
| `GET /leaguetiers`, `/builderbaseleagues`, `/warleagues`, `/labels/players` | lookups (icons, names) | `items[] {id, name, iconUrls}` |

Times in the API are `yyyyMMddTHHmmss.fffZ` (UTC).

Refresh policy (implemented in `App/Services/RefreshCoordinator`): the player is refreshed on
overlay open when the cached copy is older than 5 minutes; clan, war, CWL and raids every 2
minutes while the overlay is visible; nothing while it is hidden.

Caching (`ClashApiClient` + `ResponseCache`): every good answer is kept on disk with its fetch
time and a max age, and is served without a request while it is fresh. Layout under
`%LOCALAPPDATA%\ClashOverlay\cache\`: `<player tag>\player.json`;
`clan-<clan tag>\{clan,members,currentwar,warlog,leaguegroup,raids}.json`;
`cwl\<war tag>.json`; `global\goldpass.json` (tags without `#`). Max ages: player, clan,
members, league group and raids 5 minutes; current war and CWL wars 2 minutes; war log 30
minutes; an ended CWL war 30 days; Gold Pass 6 hours. A longer `Cache-Control: max-age` from
the API wins. When a call fails, the last good copy comes back marked stale with the error.

Backoff (`BackoffGate`, shared by all endpoints because the API throttles per key): 429 waits
`Retry-After`, else 5 s doubling to 5 minutes; 503 maintenance 1 minute doubling to 10 minutes;
network errors, timeouts and other 5xx 10 s doubling to 5 minutes; a refused key or a wrong IP
pauses 5 minutes, or until the key is changed. `notFound` and a private war log never pause.

Key payload: API keys are JWTs whose payload lists the allowed addresses
(`limits[].cidrs`), so the app can show which IP a key was made for without a request
(`ApiKeyInfo`). The optional developer-site helper (`DeveloperSiteKeyManager`, off by default)
posts to `developer.clashofclans.com/api/login`, `apikey/list`, `apikey/create` and
`apikey/revoke` like coc.py; the login answer's `temporaryAPIToken` carries the public IP the
site sees. It only ever revokes keys named "Clash Tracker", and only at the 10-key limit.

## 2. Village data export

* **Where in the game (assumed from player guides and the maintainer's bot):**
  Settings (gear) → More Settings → scroll to **Data Export** → **Copy** (copies the JSON to the
  clipboard; on Google Play Games for PC it lands in the Windows clipboard). The setup screen
  shows these steps and the app accepts paste, drag-and-drop of a `.json`/`.txt` file, or a
  file picker. If Supercell moves the button the steps text is the only thing to update.
* **Format (confirmed against chiefpansancolt/clash-of-clans-data's parser and mock profile,
  MIT, and the maintainer's bot):**

```json
{"tag": "#ABC123", "timestamp": 1700000000,
 "helpers":   [{"data": 93000000, "lvl": 6, "helper_cooldown": 72981}],
 "guardians": [{"data": 107000000, "lvl": 1}],
 "buildings": [{"data": 1000001, "lvl": 16, "cnt": 1},
               {"data": 1000014, "lvl": 12, "timer": 86400},
               {"data": 1000013, "lvl": 15, "gear_up": 1},
               {"data": 1000010, "lvl": 15, "cnt": 200},
               {"data": 1000097, "types": [{"data": 103000008, "modules": [{"data": 102000024, "lvl": 2}]}]}],
 "traps": [...], "units": [{"data": 4000023, "lvl": 9, "timer": 14400, "helper_recurrent": true}],
 "siege_machines": [...], "heroes": [...], "spells": [...], "pets": [...], "equipment": [...],
 "decos": [...], "obstacles": [...], "skins": [ids], "sceneries": [ids], "emotes": [ids],
 "building_variants": [ids], "boosts": {...},
 "buildings2": [...], "traps2": [...], "units2": [...], "heroes2": [...]}
```

* `data` is the numeric id; it equals `_id` in the static data. `lvl` is the level, `cnt`
  (default 1) groups identical instances, `timer` is seconds left on a running upgrade **at
  export time** (`lvl` is still the old level; `lvl` 0 with a timer is a building under
  construction). `gear_up` marks a geared-up Cannon/Archer Tower/Mortar. `types`/`modules` are
  the Crafting Station's seasonal defenses. `*2` sections are the Builder Base.
* Unknown ids are a logged warning and skipped, never a crash. Unknown sections are ignored.
* The export's `tag` must match the selected account; a mismatch is refused.
* `timestamp` is Unix seconds (UTC). The export's age is shown wherever it drives numbers.

### Merge rule (export + API)

* Buildings, walls, traps, guardians, helpers and all upgrade timers: export only (the API
  has none of them).
* Troops, spells, sieges, heroes, pets and equipment: whichever source is newer. The API copy's
  time is its fetch time; the export's is `timestamp`. When the API is newer and shows a higher
  level than the export, the export's timer for that unit is dropped (the upgrade finished).
  When the levels are equal the export's running timer is kept.
* Account-level facts (name, Town Hall and Builder Hall level, trophies, league, clan, war
  stars, labels, achievements): API.

## 3. Static game data: ClashKing

* Repository `ClashKingInc/ClashKingAssets`, **GPL-3.0** (confirmed), files under
  `assets/static_data/*.json`. The last commit touching `assets/static_data` was
  `9446c022` on 2026-11-23 (the repository itself was last pushed 2026-11-24 for artwork).
  It has Town Hall 19 data.
* Not committed to this repository. Downloaded at first run and on "Check for data update"
  into `%LOCALAPPDATA%\ClashOverlay\data\` from
  `https://raw.githubusercontent.com/ClashKingInc/ClashKingAssets/<commit>/assets/static_data/<file>.json`,
  where `<commit>` is the newest commit touching that folder
  (`GET https://api.github.com/repos/ClashKingInc/ClashKingAssets/commits?path=assets/static_data&per_page=1`).
  If GitHub's API is unavailable (rate limit, offline), the files come from the CDN copy
  `https://assets.clashk.ing/static_data/<file>.json` and are checked against the SHA-256 list
  in `https://assets.clashk.ing/manifest.json` (`data.stats[] {path, sha}`).
  `data\manifest.json` records the source, commit, commit date, download time and each
  file's SHA-256.
* Files used: `buildings`, `traps`, `troops`, `spells`, `heroes`, `pets`, `equipment`,
  `guardians`, `helpers`, `resources`, `league_tiers`, `builder_leagues`, `war_leagues`.
  Each is `{"items": [...]}`.

### Fields (confirmed)

* Item: `_id`, `name`, `TID`, `type`, `village` (`home`/`builderBase`; pets, guardians and
  helpers have none and are home village), `upgrade_resource` (`Gold`, `Elixir`,
  `Dark Elixir`, `Builder Gold`, `Builder Elixir`, `Gems`), `levels[]`.
* Building `type` values: `Defense`, `Army`, `Resource`, `Wall`, `Worker` (Builder's Hut,
  B.O.T.O's Shack), `Worker2` (B.O.B's Hut), `Helper` (Helper Hut), `Town Hall`,
  `BB Builder Hall` (Builder Hall, O.T.T.O's Outpost).
* Building/trap level: `level`, `build_cost`, `build_time` (seconds), `required_townhall`
  (Builder Hall level for Builder Base items), sometimes `alt_upgrade_resource` (walls from
  level 5: gold or elixir), `merge_requirement`, `supercharge`, `variants`. **Cost and time on
  level N are what it takes to reach N** (level 1 = build cost).
* Troop/spell/hero/pet/guardian level: `upgrade_cost`, `upgrade_time`,
  `required_lab_level` / `required_hero_tavern_level` (Hero Hall) / `required_pet_house_level`,
  `required_townhall`. **Cost and time on level N are what it takes to go from N to N+1; the
  top level has 0.** Pinned by tests with in-game values: Barbarian 11 → 12 costs 6,000,000
  Elixir and 3 days (the TH19 update cut it from 8M / 4 days); the top level has 0.
  Requirements on level N are what is needed to *have* level N (Barbarian 14 needs Lab 17 and
  Town Hall 19). The loader normalises everything to "cost/time to reach level N".
* Equipment level: `upgrade_cost` is an ore object `{shiny_ore, glowy_ore, starry_ore}`
  (same "from N to N+1" meaning), `required_blacksmith_level`, `required_townhall`; item has
  `rarity` (`Common`/`Epic`) and `hero`. No upgrade time (instant).
* Troops: `production_building` (`Barracks`, `Dark Barracks`, `Workshop` = siege machines,
  `Squad Barracks` = squads, `Builder Barracks`), `is_seasonal: true` on event troops,
  `super_troop {original_id, original_min_level}` on super troops, `squad {...}` on squads.
  Spells: `is_seasonal` on event spells (Santa's Surprise, Bag of Frostmites).
* Town Hall and Builder Hall levels list `unlocks[] {name, _id, quantity}`: how many of each
  building **and trap** that level adds (incremental, so counts are cumulative sums).
* Merges (`merge_requirement` on a level, sources `{_id, level, geared_up}`):
  Multi-Archer Tower = 2 × Archer Tower 21; Ricochet Cannon = 2 × Cannon 21;
  Multi-Gear Tower = geared Archer Tower 21 + geared Cannon 21; Super Wizard Tower =
  2 × Wizard Tower 17; Firecracker Tower (TH19) = Archer Tower 21 + Scattershot 7; Town Hall
  level 17 = Eagle Artillery 7 (the Eagle goes into the Town Hall).
* Quirks handled by the loader:
  * Firecracker Tower levels have `required_townhall: null`. Such levels get the first Town
    Hall whose cumulative `unlocks` include the item (19), never less than the level before.
  * B.O.T.O's Shack is spelled with a curly apostrophe (U+2019).
  * The Crafting Station has no upgrade levels of its own; its `seasonal_defenses[].modules[]`
    rotate each season and are left out of the totals.
  * Helpers are upgraded with gems; the Prospector has no levels.

### Merge-adjusted counts

chiefpansancolt/clash-of-clans-data (MIT) has `availablePerTownHall[].countAfterMerges`, but its
last push was 2026-11-06, before Town Hall 19, so the app derives the counts from ClashKing
instead: expected standalone count = cumulative `unlocks` − (merged buildings × sources each
consumes). Cross-check against chiefpansancolt for Town Hall 16–18: Archer Tower 8 → 4 at TH16
and 9 → 2 at TH17/18 in both. At TH19 the derived numbers are: Archer Tower 9 → 0 (3 Multi-Archer
Towers, 1 Multi-Gear Tower and 2 Firecracker Towers), Cannon 7 → 0, Scattershot 2 → 0, Wizard
Tower 6 → 2, Eagle Artillery 1 → 0.

### Town Hall 19 values in the data (confirmed)

Town Hall 19 is the top level. New at TH19: Spiky Tower (2 buildings, 2 levels, Dark Elixir),
Firecracker Tower (2 buildings, 2 levels), Guardian Shrine (1, no cost), a 5th Air Defense, a 10th
Spring Trap and 25 more walls (350). Max at TH19: Air Defense 17, Hidden Tesla 18, X-Bow 14,
Multi-Archer Tower 5, Multi-Gear Tower 4, Ricochet Cannon 5, Builder's Hut 9, Clan Castle 15,
Laboratory 17, Hero Hall 13, Squad Barracks 12, Gold and Elixir Storage 20, Dark Elixir Storage 14,
Wall 20, Air Bomb 14, Spring Trap 14, Skeleton Trap 6. Heroes: Barbarian King 115, Archer Queen
115, Minion Prince 100, Grand Warden 90, Royal Champion 60, Dragon Duke 30. Smasher 10, Angry
Jelly 15, Flame Flinger 6, Heroic Spell 4. All of these are unit tests that run against the
downloaded data (`tests/.../Th19ChecksTests.cs`).

## 4. Images: ClashKing asset CDN

* `https://assets.clashk.ing/<path under assets/>`; responses carry `Last-Modified`
  (confirmed). ClashKing asks for credit (README, About).
* Name cleanup (confirmed in their README and against the folder names):
  `lower()`, spaces → `_`, drop `.`, `?`, `\q` and the curly apostrophe `’`. The **straight**
  apostrophe stays: `builder's_hut`, `bob's_hut`, `otto's_outpost`, `santa's_surprise`.
  Hyphens stay: `x-bow`, `multi-archer_tower`. Examples: `P.E.K.K.A` → `pekka`, `L.A.S.S.I` →
  `lassi`, `B.O.T.O’s Shack` → `botos_shack`.
* Paths (confirmed by listing the repository):
  `buildings/home-village/{name}/level_{n}.webp`, `buildings/builder-base/{name}/level_{n}.webp`,
  `traps/home-village/{name}/level_{n}.webp`, `traps/builder-base/{name}/level_{n}.webp`,
  `troops/{name}/icon.webp` (+ `portrait.webp`), `heroes/{name}/icon.webp` and `portrait.webp`,
  `pets/{name}/icon.webp`, `spells/{name}.webp`, `equipment/{name}.webp`,
  `guardians/{name}/icon.webp`, `helpers/{name}.webp`, `resources/{name}.webp` (gold, elixir,
  dark_elixir, shiny_ore, glowy_ore, starry_ore, builder_gold, builder_elixir, gems),
  `leagues/league-tier/{name}.png` (e.g. `electro_league_33.png`, `legend_league_1.png`,
  `unranked.png`: Roman numerals in names become digits), `leagues/builder-base/{name}.png`
  (`wood_league_5.png`, `diamond_league.png`). Town Hall 19 also has
  `buildings/home-village/town_hall/level_19_{barbarian|archer|wizard}.webp`.
* `https://assets.clashk.ing/manifest.json` lists every asset with its SHA-256
  (`assets.<group>[] {path, sha}`). AssetFetch uses it to download only missing or changed
  files with one request; when it is unavailable it falls back to a `HEAD` per file and
  compares `Last-Modified`.
* Files are converted from WebP to PNG with SkiaSharp (WPF's WebP decoding depends on a codec
  that not every Windows install has) and stored under `%LOCALAPPDATA%\ClashOverlay\assets\`.
  Missing images fall back to the nearest lower level, then to a placeholder tile with the
  item's initials. The API's own icon URLs (league, labels, clan badge) are downloaded as-is
  and cached.

## 5. Finding the game window (no game interaction)

* **Google Play Games for PC (confirmed on this machine's install):**
  `C:\Program Files\Google\Play Games\current\` has `client\client.exe` (the launcher UI, a CEF
  app titled "Google Play Games") and `emulator\crosvm.exe` (the VM that hosts each game's
  window). Default rule: process `crosvm.exe`, title `Clash of Clans*`. **Assumed:** the game
  window's title is the game's name; the "Pick game window" button covers any difference.
* **BlueStacks 5:** process `HD-Player.exe`; the window title is the instance name (often
  "BlueStacks App Player"), not the game's, so the rule matches any title. Other emulators
  with the same kind of rule: LDPlayer `dnplayer.exe`, MEmu `MEmu.exe`, Nox `Nox.exe`, MuMu
  `MuMuPlayer.exe` / `MuMuNxDevice.exe`. A last generic rule matches any window titled exactly
  `Clash of Clans`.
* Rules are editable in Settings (process name + title wildcard). Detection uses
  `SetWinEventHook` (create, destroy, show, hide, name change, foreground, minimise) plus a
  3-second fallback poll of top-level windows. Nothing is read from the game process; only its
  window handle, title, process name, rectangle and minimised state.
