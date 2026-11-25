# Clash Tracker

A self-hosted Clash of Clans progress overlay for Windows. It waits in the system tray, notices
when Clash of Clans is running, and on **Ctrl+R** shows a large translucent dashboard over the
game. The dashboard covers:

* how close your village is to max for its Town Hall
* what is rushed, and by how much
* the longest builder and laboratory upgrades
* heroes, pets, equipment and ores
* your profile, clan, war and Clan War League

It does this for the Main Village and the Builder Base, for as many accounts as you like.

![Main Village overview](docs/screenshots/mv-overview.png)

## How it works

* **It never touches the game.** No injection, no memory reading, no input automation, no packet
  capture. The app finds the game's window by its title and process name and draws its own window
  on top. It reads nothing else from the game.
* **Two sanctioned data sources:**
  * The official Clash of Clans API, with a key you make yourself. It gives heroes, troops,
    spells, pets, equipment, your profile, your clan and wars.
  * The village data export you copy from the game's settings. It gives buildings, traps, walls
    and running timers, which the API leaves out.
* **Game data from a maintained data set** ([ClashKing](https://github.com/ClashKingInc/ClashKingAssets)):
  upgrade costs, times, Town Hall caps and images. It is downloaded on first run and refreshed
  once a week, so nothing is typed into the code. Images are converted from WebP to PNG on your
  PC and kept in `%LOCALAPPDATA%\ClashOverlay\assets`.
* Between exports the overlay counts running timers down. You can mark an upgrade as done with
  **+1** on its row instead of exporting again.

## Using it

| Key | What it does |
| --- | --- |
| **Ctrl+R** | Show or hide the overlay (only while the game is the window in front) |
| **Ctrl+B** | Switch between Main Village and Builder Base |
| **Ctrl+Tab** | Next account |
| **Ctrl+1** … **Ctrl+9** | Jump to an account |
| **Esc** | Close the overlay |

All keys can be changed in Settings → Hotkeys. While the overlay is hidden, Clash Tracker
registers only the toggle key, and only while the game is in front, so every other key goes to
the game as usual.

Main Village tabs:

* Overview
* Rushed
* Builder upgrades
* Laboratory
* Heroes & Pets
* Equipment
* Profile & War

Builder Base tabs:

* Overview
* Builder upgrades
* Star Laboratory
* Profile

Settings → Overlay switches progress between **levels** (each level counts the same) and
**time** (weighted by upgrade time).

## Getting an API key

Clash Tracker reads your profile from the official API with a key that only you hold. It is free.

1. Open <https://developer.clashofclans.com> and sign in, or create an account. A developer account
   is separate from your Supercell ID.
2. Go to **My Account** → **Create New Key**. Any name and description will do.
3. Under **Allowed IP addresses**, enter your PC's public IP address. The setup window shows it,
   with a Copy button.
4. Create the key, open it, and copy the long token shown under **Key**.
5. Paste it into Clash Tracker. Setup does this in step 2, or later in Settings → API key.
   Clash Tracker makes one real call to check it.

The key is saved encrypted for your Windows account (DPAPI, current user). It is never written to
`settings.json` and never logged.

**When your IP address changes**, the key stops working. Clash Tracker notices the
`accessDenied.invalidIp` answer, or the new address at start-up, and shows a tray notification.
Clicking the notification opens the key steps with the new address ready to copy. You can also
turn on **Make keys automatically** in setup step 2. Clash Tracker then signs in to the developer
site with your login when the address changes and creates a key for the new address, the same way
the open-source [coc.py](https://github.com/mathsman5133/coc.py) library does. This is off unless
you turn it on, and the login is saved encrypted with DPAPI like the key.

## Importing your village

The API has no building, trap or wall levels, so those come from the game's own data export:

1. In Clash of Clans open **Settings** → **More Settings** → **Data Export** and tap **Copy**.
2. In Clash Tracker choose **Import village export…** from the tray menu, or use setup step 4.
3. Paste the text, drop a saved file on the box, or choose the file.

Each export is checked against the account's player tag. Import again after a round of upgrades.
In between, the overlay counts running timers down, and you can mark finished upgrades by hand.
The overlay shows how old the export is, and turns the dot next to it orange when the export is
older than three days or from before your last Town Hall upgrade.

## Supported launchers

Clash Tracker looks for these windows by default:

| Launcher | Process | Window title |
| --- | --- | --- |
| Google Play Games for PC | `crosvm.exe` | `Clash of Clans` |
| BlueStacks | `HD-Player.exe` | any |
| LDPlayer | `dnplayer.exe` | any |
| MEmu | `MEmu.exe` | any |
| NoxPlayer | `Nox.exe` | any |
| MuMu Player | `MuMuPlayer.exe` | any |
| MuMu Player 12 | `MuMuNxDevice.exe` | any |
| Anything else | any | `Clash of Clans` |

If your launcher is not found, open Settings → Game detection and press **Pick game window**.
Click the game within 15 seconds and Clash Tracker adds a rule for it. You can also edit the rules
by hand. A title can use `*` and `?` wildcards.

## Where things are kept

Everything lives in `%LOCALAPPDATA%\ClashOverlay`:

| Path | Contents |
| --- | --- |
| `settings.json` | Settings (no key, no login) |
| `apikey.bin`, `devsite.bin` | The API key and the optional developer login, DPAPI-encrypted |
| `accounts\<tag>\` | Your exports, manual changes and the last API answers |
| `data\`, `assets\` | ClashKing game data and images |
| `logs\` | Logs, kept for 14 days |

Nothing is sent anywhere except to the Clash of Clans API, the developer site if you turn on
automatic keys, a public-IP lookup (ipify, with Amazon and icanhazip as fallbacks), and the
ClashKing download servers.

## Trying it without a key

`ClashOverlay.exe --demo` runs with a built-in TH19 account and a rushed TH13 account. It needs no
key and leaves your own settings and accounts alone. **Preview with demo data** in the tray menu
opens the same preview while the app is running.

## Building from source

You need the .NET 8 SDK or newer on Windows.

```powershell
dotnet build ClashOverlay.sln
```

```powershell
dotnet test ClashOverlay.sln
```

```powershell
dotnet publish src/ClashOverlay.App/ClashOverlay.App.csproj -p:PublishProfile=win-x64
```

The publish step writes one self-contained `publish/ClashOverlay.exe`. Add
`-p:ClashVersion=1.2.3` to stamp a version; without it the version is `0.0.0-dev`.

`tools/AssetFetch` is the downloader the app uses for game data and images. Run it on its own with
`dotnet run --project tools/AssetFetch -- --out <folder>`. Downloaded data and images must never
be committed; `.gitignore` keeps them out.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the code is laid out and
[docs/DATA_NOTES.md](docs/DATA_NOTES.md) for the data sources and the Town Hall 19 checks.

## Credits

* Game data and artwork: [ClashKing](https://github.com/ClashKingInc/ClashKingAssets): static data
  and the asset CDN at assets.clashk.ing, under GPL-3.0. They are downloaded at run time, never
  bundled.
* Export parser reference: [chiefpansancolt/clash-of-clans-data](https://github.com/chiefpansancolt/clash-of-clans-data) (MIT).
* Key automation approach: [coc.py](https://github.com/mathsman5133/coc.py) (MIT).
* Font: [Inter](https://rsms.me/inter/) (SIL Open Font License 1.1). The licence ships next to
  the font in `src/ClashOverlay.App/Assets/Fonts`.

## Licence

The code in this repository is under the [MIT licence](LICENSE). Game data and artwork belong to
their owners and are not part of this repository.

## Supercell notice

This material is unofficial and is not endorsed by Supercell. For more information see
Supercell's Fan Content Policy: <https://supercell.com/en/fan-content-policy/>.

Clash Tracker is not affiliated with, endorsed, sponsored or approved by Supercell.
