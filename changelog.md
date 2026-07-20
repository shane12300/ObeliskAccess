# ObeliskAccess Changelog

ObeliskAccess makes Across the Obelisk playable with a screen reader: full keyboard
navigation with spoken feedback through NVDA, JAWS, or other screen readers (falling back
to Windows built-in speech if none is running).

## Version 1.0 — Initial release

### Getting around
- Arrow keys navigate every supported screen, with each item spoken as you move.
- Enter activates the focused item; Escape backs out or cancels.
- Alt+R repeats the last spoken message on screens that support it.

### Main menu and setup
- Main menu, game-mode selection, and save-slot screens are fully navigable and spoken.
- The settings menu works with arrows, Enter, and Tab, including dropdowns (Escape cancels
  an open dropdown).
- Tutorial pop-ups are read aloud: Up/Down walks through the text line by line, Enter
  activates the buttons, and the pop-up keeps keyboard focus until you close it.

### The map
- Left/Right moves between the nodes you can travel to, announcing each one. Enter travels.
- Hold Ctrl with the arrow keys to look ahead along the road before committing: Ctrl+Down
  descends into upcoming nodes, Ctrl+Up backs out, and Ctrl+Left/Right compares branching
  paths.
- Every node is announced with map coordinates: the first number is its position left to
  right (1 is leftmost), the second is how far into the map it sits (1 is the first group
  of nodes you can travel to, 2 the group after, and so on).
- Alt+T reads full detail about the focused node.
- Tab switches to the party strip: Up/Down reads each hero's condition, and 1–4 jumps
  straight to a party slot.
- Alt+G reads your gold; Alt+I reads your current position, quest trackers, and a travel tip
  (also spoken automatically when the map opens).
- Corruption offers are fully accessible: Left/Right picks a reward, Up/Down toggles
  accept/decline, Enter confirms.

### Story events
- When a story event opens, Up/Down walks through the title, the event text, and your
  choices (choices sit at the bottom of the walk). Enter picks a choice or continues.
- Dice rolls are narrated play-by-play, including your chance of success.
- Alt+T on a choice explains it in depth: success probability, why an option is blocked,
  and previews of any cards a choice would give you.

### Combat
- Arrow keys move through your hand, your heroes, and the enemies, with each card and
  character announced.
- Combat events — damage, healing, status effects, turns starting and ending — are spoken
  as they happen, queued so nothing talks over anything else.
- Review keys let you check the battlefield at any time without losing your place.

### Towns
- Arriving in town gives a spoken overview of what's available.
- Up/Down moves through everything in town: the five buildings, the upgrades window,
  ready-up, and any treasures waiting to be claimed. Enter opens or claims, and
  confirmation prompts are spoken and answerable in place (Enter for yes, Escape for no).
- Tab switches to the party strip, same as on the map.
- All five town services are fully accessible:
  - **Altar** — browse cards to bless, with A/B upgrade previews (Left/Right compares them).
  - **Church** — heal and remove cards, with a spoken confirmation before removal.
  - **Forge** — craft and upgrade cards. Up/Down moves through the stock and automatically
    turns the page at the end; Left/Right jumps a whole page. Alt+F opens the card filters.
  - **Divination** — browse the reading tiers and their prices.
  - **Armory** — buy equipment, with Tab to review what each hero already has equipped.
  - In every shop, 1–4 switches which hero you're shopping for, Alt+T reads the full card
    or item description, and every purchase is confirmed out loud.
- The town upgrades window is fully accessible: Left/Right picks a building, Up/Down walks
  its upgrade chain, and each upgrade reports whether you own it, can afford it, or why
  it's locked. Selling supplies has its own quantity picker (Up/Down adjusts the amount).

### Under the hood (but you'll notice)
- Keyboard navigation works even if the game's own "keyboard shortcuts" setting is off —
  the mod enables it for you.
- Speech goes through your running screen reader when one is active, so it uses the voice
  and speed you're used to.
