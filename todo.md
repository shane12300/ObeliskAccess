# TODO

## Combat: surface card-inspect screen info in speech

The game's card-inspect overlay (`CardScreenManager`, opened by right-click / bare Alt / gamepad-north
on a card) shows information our combat layer doesn't yet speak. The Alt-key path is suppressed in
combat (`RouterDoButtonNorthPatch`) because it collided with our Alt hotkeys; mouse right-click still
works, and the overlay is purely visual anyway. Close the gap in speech instead:

- [ ] **Alt+T on a card**: also speak rarity (Common/Uncommon/Rare/Epic/Mythic) and upgrade state
      (not upgraded / blue path A / gold path B / corrupted) — one extra clause.
- [ ] **New drill category — "Upgrades to"** (Ctrl+↑/↓ on a card): each upgrade path's card name +
      description, plus what differs from the base (energy cost, vanish, … — see
      `CardItem.ShowDifferences`, `CardItem.cs:4155`). Data: `CardRealtimeData.UpgradesTo1/UpgradesTo2/
      UpgradedFrom/UpgradesToRare`, resolve via `Globals.Instance.GetCardData(id, instantiate: false)`.
- [ ] **New drill category — "Related cards"**: cards this card creates/references
      (`CardRealtimeData.HaveRelatedCards` / `RelatedCards`), speak each one's name + description.
      Biggest combat-relevant gap ("this card creates Burn — what does Burn do?").

Reference: `CardScreenManager.SetCardDataCo` (decompiled `CardScreenManager.cs:160`) is the canonical
list of what a sighted player sees on that screen.
