# ObeliskAccess Changelog

## Version 1.0 — Initial release

First release: full keyboard navigation and screen-reader speech across the game, in
single player and multiplayer. See the README for the key reference.

### Added

- Speech output through a running screen reader (NVDA, JAWS, etc.), with Windows speech
  as a fallback.
- An installer (`ObeliskAccessInstaller.exe`) that installs BepInEx 5 and the mod,
  updates, and uninstalls — screen-reader friendly. If you would rather work from a
  terminal, the source repository has a `scripts\deploy.ps1` that does the same from
  a git checkout: it installs BepInEx if needed, builds the mod, and copies everything
  into place in one command.
- Main menu, game-mode selection, and save slots.
- Settings menu, plus a new Accessibility tab holding the mod's own options.
- Tutorial pop-ups.
- Map navigation, including look-ahead along upcoming paths and the party strip.
- Story events, with narrated dice rolls.
- Corruption offers.
- Combat: hand, hero, and enemy navigation, spoken combat events, and review keys.
- In-combat card-selection windows (discard, look-at-deck, discover, pile viewers).
- The in-combat death popup.
- Towns: the hub, all five services, and the upgrades window.
- Reward and loot screens.
- Hero selection and run setup.
- The character window, the in-run character sheet, and leveling up.
- The perk tree.
- Act-transition story screens.
- The end-of-run screen.
- All of the game's pop-up dialogs, including text entry.
- Multiplayer: chat, players panel, give window, the lobby, travel votes and the
  card-flip conflict screen, emotes and pings, and ambient announcements for partner
  actions.

### Changed

- Story events with only one available choice no longer select themselves automatically
  in single player, so the text can be read first. See the README's "Story events".
- Escape on a multiplayer map shop now casts the ready-to-leave vote instead of closing
  the shop for you alone.
