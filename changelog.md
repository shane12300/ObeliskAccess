# ObeliskAccess Changelog

## Version 1.0 — Initial release

- Speech output through a running screen reader (NVDA, JAWS, etc.), with Windows built-in
  speech as a fallback.
- Keyboard navigation with spoken feedback on all supported screens; the game's own
  "keyboard shortcuts" setting is enabled automatically.
- Main menu, game-mode selection, and save-slot screens made accessible.
- Settings menu made accessible, including dropdowns.
- Tutorial pop-ups read aloud line by line.
- Map made accessible: reachable-node navigation, Ctrl look-ahead along upcoming paths,
  map coordinates for every node, node detail (Alt+T), party strip (Tab), gold (Alt+G),
  and position/tracker readout (Alt+I).
- Corruption offers made accessible.
- Story events made accessible: text and choice walk, narrated dice rolls, and in-depth
  choice detail (Alt+T).
- Combat made accessible: hand/hero/enemy navigation, spoken combat events, and
  battlefield review keys.
- Towns made accessible: arrival overview, town hub, all five services (Altar, Church,
  Forge, Divination, Armory), the town upgrades window, and spoken confirmation prompts
  answerable in place.
- Reward screens (post-combat, story-event, and divination card picks) made accessible:
  arrow-key navigation over each hero's card, dust, and deck-view choices, full card detail
  (Alt+T or Ctrl+Up/Down line by line), a screen overview (Alt+I), spoken pick
  confirmations, and the game's usual hover sounds while moving between choices.
- Loot screens (item pickups after boss and chest fights, and Obelisk-challenge chests) made
  accessible: arrow keys walk the loot — each item, the gold pile, and the restart button —
  and Enter takes the focused pick for the hero whose turn it is, with each turn change
  spoken. Item announcements include what the choosing hero currently has equipped in that
  slot. Tab reviews the party with everyone's equipped items (in single player, Enter there
  lets a different hero pick next; 1–4 jumps straight to a hero). Full item detail on Alt+T
  or line by line with Ctrl+Up/Down, screen overview on Alt+I, and every pick — yours or a
  teammate's — is confirmed out loud.
- Story events with only one available choice no longer select themselves half a second
  after appearing (single player). The event now waits so the full text and the choice can
  be heard and reviewed before pressing Enter. This is the mod's one deliberate change to
  game behaviour — see the README's "Design philosophy" section.
- Fixed: on the loot and reward screens, pressing Enter also triggered the game's hidden
  mouse-click at the last cursor position, which could silently take an item, grab the gold,
  or open the deck window and leave the keyboard unresponsive. The stray click is now
  suppressed, and if the deck window does open (e.g. by mouse), it is announced along with
  how to close it.
- Alt+R repeats the last spoken message on screens that support it.
