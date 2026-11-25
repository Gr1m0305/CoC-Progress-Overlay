# Changelog

All notable changes to Clash Tracker are listed here. Versions follow
[Semantic Versioning](https://semver.org/). Each release on GitHub takes its notes from the
matching section below.

## [1.0.0] - 2026-11-26

The first release.

### Overlay

- Waits in the notification area, notices Clash of Clans in Google Play Games for PC, BlueStacks,
  LDPlayer, MEmu, NoxPlayer or MuMu Player, and shows a translucent dashboard over it on Ctrl+R.
- Main Village tabs: Overview, Rushed, Builder upgrades, Laboratory, Heroes & Pets, Equipment,
  Profile & War.
- Builder Base tabs: Overview, Builder upgrades, Star Laboratory, Profile.
- Progress against the caps of your Town Hall, up to Town Hall 19, counted by levels or by upgrade
  time. Also shows rushed items, the cost and time left, ores, your clan, wars and Clan War League.
- Any number of accounts: switch with Ctrl+Tab or Ctrl+1 to Ctrl+9, and between villages with
  Ctrl+B. Every hotkey can be changed.
- Mark finished upgrades with +1 between exports, and undo them one by one.

### Data

- Reads the official Clash of Clans API with your own key, saved encrypted for your Windows
  account. Answers are cached, and the overlay says why data is old during maintenance or after an
  IP address change.
- Imports the game's village data export by paste, drag and drop, or file, and checks it against
  the player tag.
- Downloads ClashKing game data and images at run time, converts the images to PNG, and checks for
  new ones once a week.
- Optional: makes a new API key on the developer site when your IP address changes. It is off by
  default.

### Setup and updates

- First-run setup in four steps, a Settings window, and a tray alert when the IP address changes.
- Per-user installer with a Start with Windows option and a keep-or-delete-data choice on
  uninstall. A portable zip is also available.
- Checks GitHub once a day for a new version and shows a tray note and an "Update available" menu
  item. Nothing is downloaded or installed for you.
