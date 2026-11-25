# Clash Tracker

A self-hosted Clash of Clans progress overlay for Windows. It waits in the system tray, notices
when Clash of Clans is running, and on **Ctrl+R** shows a large translucent dashboard over the
game: how close your village is to max for its Town Hall, what is rushed and by how much, the
longest builder and laboratory upgrades, heroes, pets, equipment and ores, your profile, clan,
war and Clan War League, for the Main Village and the Builder Base, for as many accounts as you
like.

> Work in progress. See the sections below as they are filled in.

## How it works

* **It never touches the game.** No injection, no memory reading, no input automation, no packet
  capture. The app finds the game's window and draws its own window on top, nothing more.
* **Two sanctioned data sources:** the official Clash of Clans API (with your own key) and the
  village data export you copy from the game's settings.
* **Game data from a maintained data set** (ClashKing), downloaded on first run and updated
  with one click. Nothing is typed into the code.

## Credits

* Game data and artwork: [ClashKing](https://github.com/ClashKingInc/ClashKingAssets)
  (static data and the asset CDN at assets.clashk.ing). Downloaded at run time, never bundled.
* Export parser reference: [chiefpansancolt/clash-of-clans-data](https://github.com/chiefpansancolt/clash-of-clans-data) (MIT).
* Key automation approach: [coc.py](https://github.com/mathsman5133/coc.py) (MIT).
* Font: [Inter](https://rsms.me/inter/) (SIL Open Font License 1.1).

This material is unofficial and is not endorsed by Supercell. For more information see
Supercell's Fan Content Policy: <https://supercell.com/en/fan-content-policy/>.
Clash Tracker is not affiliated with, endorsed, sponsored or approved by Supercell.
