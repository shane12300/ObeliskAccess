# ObeliskAccess Changelog

## Version 1.0 — Initial release

- Speech output through a running screen reader (NVDA, JAWS, etc.), with Windows built-in
  speech as a fallback.
- Keyboard navigation with spoken feedback on all supported screens; the game's own
  "keyboard shortcuts" setting is enabled automatically.
- Main menu, game-mode selection, and save-slot screens made accessible. The game-mode
  screen is walked with Up/Down (Left/Right work too) as one list — Main Menu button first,
  then the four modes — instead of the game's sideways layout. Each mode announces its
  proper name and description; modes still chained off (no rank-3 character yet) speak the
  game's own requirement text, and — matching the game itself — can still be entered.
  Pressing Enter now activates a menu item exactly once (previously the game's own hidden
  Enter handling could double-press, which sometimes skipped the mode-selection screen
  entirely and jumped straight to a save-slot window).
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
- New Accessibility tab in the settings menu (Tab/Shift+Tab reaches it after Gameplay) holding
  the mod's own options, remembered between sessions. Alt+T on any option speaks a tooltip
  explaining exactly what it does. The first four options tune combat narration verbosity:
  combine enemy turn announcements into their card plays (off by default), short status
  phrasing — "Poison 7" instead of "gains Poison 3, total 7" (on by default), skip the routine
  1-point end-of-turn status decay while still announcing statuses running out and larger
  losses (on by default), and skip enemy status changes entirely, keeping damage, heals, card
  plays and deaths (off by default).
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
- In-combat card-selection windows made accessible: cards that make you discard from hand
  (including put-on-deck and vanish variants), cards that look at the top of your deck and let
  you discard or vanish some of them, discover-style "choose a card to add" picks, and the
  draw/discard pile viewers. Left and Right arrows walk the candidate cards (already-picked
  ones are announced as "selected"), Enter selects or deselects, and Space confirms — or tells
  you how many more cards you must pick. When exactly one card must be chosen, Enter takes it
  and confirms in one press. The game's own number keys still work and now speak their result.
  Full card detail stays available with Alt+T or line by line with Ctrl+Up/Down, and Alt+R
  repeats. Pure "look only" peeks and the pile viewers are read-only: browse with the arrows,
  continue with Space or close with Escape.
- Fixed: cards with these deck effects could not be cast with the keyboard at all — after
  picking the card up, pressing Enter on the target hero silently did nothing (the game's
  deck icons that appear over each hero were swallowing the click). Enter on a character now
  casts reliably, and picking a card up is announced ("Expert Tracker picked up. Move to a
  target and press Enter.") instead of being silent.
- New "Debug mode" option at the bottom of the Accessibility settings tab (off by default).
  When on, the mod writes extra troubleshooting detail to its log file to help diagnose
  problems; normal play is unaffected either way.
- All of the game's pop-up dialogs (confirmations, warnings, text-entry boxes, and the
  import/export boxes) are now read as a walkable dialogue on every screen: the dialog text is
  read when it opens along with a summary of the available options, then Up and Down move
  through the text and the option buttons, and Enter presses the focused button. Enter on a
  text line never accepts anything — you must move down to a button — so a destructive
  confirmation (deleting a save, resigning a run) can't be triggered by accident. Escape still
  cancels or dismisses. Text-entry dialogs (profile names, lobby codes, deck names, seeds) let
  you type normally, read back the current value as you review, and submit from the accept
  button. Answers given by mouse or by another player are spoken too. Alt+R repeats. This also
  makes previously unanswerable dialogs work, including the "do you want to retry?" question
  after a party wipe in combat.
- The screen that appears when a hero dies in combat (while the party fights on) is now
  accessible. Its announcement waits politely in the speech queue rather than talking over the
  combat narration; Up and Down read it line by line — who died, how they return after the
  fight, and the Death's Door curse added to their deck (full curse detail on Alt+T) — and
  Enter continues. In multiplayer, if the fallen hero belongs to someone else, the mod says who
  everyone is waiting for.
- The hero-selection screen (start of every run) is now fully accessible. On arrival you hear
  the game mode, the madness level, the seed (spelled out letter by letter), and how many
  party slots are filled. Tab cycles three areas: the hero roster, the party slots, and the
  run options. In the roster, Up and Down walk the heroes (pages turn automatically) and Left
  and Right switch the class filter tabs, each announced with its hero count; every hero
  announces their name, class, rank, unspent perk points, and — for locked heroes — exactly
  what unlocks them. Enter adds the focused hero to the first free slot; the number keys 1 to
  4 put them in a specific slot, replacing whoever was there (the swap is spoken). In the
  party area, Enter clears a slot or rolls the random-hero dice on an empty one. The run
  options read and operate the madness / New Game Plus level, sandbox mode, the game seed
  (Enter opens the usual accessible text-entry box), weekly modifiers, and Begin Adventure —
  which explains why it is unavailable ("party incomplete") and announces the moment it
  becomes ready. Alt+T reads a full hero sheet (description, health, energy, speed,
  resistances, traits, rank progress) and Alt+I repeats the overview. In multiplayer,
  teammates joining, picking or removing heroes, readying up, and host changes to madness or
  seed are all spoken as they happen.
- Every hero's character window is accessible from that screen with Alt+C (the mouse
  right-click equivalent). Tab moves through its tabs — Stats, Perks, Rank, Skins, Card
  Backs, and Singularity Cards — skipping any that don't apply, and Up and Down read each tab
  row by row: the full stat block and resistances, every trait with its unlock tier (Alt+T
  for the trait's description), each starting card (Alt+T for the full card text), the
  classic-variant toggle where the game offers one, rank progress with every rank reward and
  its locked state, and the use-supplies level-up button with the exact reason when it can't
  be used. Skins and card backs can be browsed and equipped with Enter, card backs across
  their three categories with Left and Right.
- The perk tree is fully accessible — perk points can now be spent from the keyboard. It
  opens from the character window's Perks tab (or the perk badge on a portrait) and announces
  the hero and available points. Tab cycles the four perk categories — each reporting how
  many points are spent in it — plus a controls area. Up and Down move between the tree's
  rows, announcing each row's unlock threshold; Left and Right move between the perks of a
  row. Every perk announces its state (selected, available, locked and why, or how many more
  points you'd need), choose-one groups ("Choice 2 of 3 — taking this replaces the chosen
  option"), a warning when a teammate already has a non-stacking perk, its effect, and its
  cost. Enter takes or removes the perk and reports the running total — or explains exactly
  why it can't ("a selected perk requires it"). Space saves the build. The controls area
  holds Confirm, Reset, Import, Export, the ten named save slots (load, save, and delete,
  all through the usual accessible dialogs), and Exit. Closing with unsaved changes asks
  first, through the standard walkable confirmation. Alt+T reads the game's full tooltip for
  a perk, including which party members already have it; Alt+I summarises your points.
- The hero-selection screen, character window, and perk tree now play the game's own hover
  sounds as you move with the keyboard: the character flourish on roster portraits, the perk
  pop on tree nodes, the card sound on card rows, and the standard button sound everywhere
  else — the same audio a sighted player hears when the mouse passes over each element.
- Text-entry dialogs now use a clear edit mode instead of relying on the box grabbing your
  keystrokes by itself (which could silently stop working). Arrow to the text field row and
  press Enter to start typing — the dialog says "Editing" and reads any current text. Type
  your text, then press Enter or Escape to finish; what you typed is kept and read back, and
  the arrows go back to walking the dialog so you can reach the accept button. This applies
  everywhere text is entered: perk-build names, game seeds, profile names, and import codes.
- The end-of-run screen is now fully accessible. On arrival you hear where the run ended and
  your final score, then Up and Down walk every row: the score breakdown (places visited,
  combat expertise, hero deaths, experience, bosses, corruptions, adventure-completed bonus),
  the final score with any madness bonus, best-score notice, and time played, the gold and dust
  reward with the supply-retention bonus, and each hero's rank progress as the bars fill. The
  Main Menu button is the last row — it reports "still tallying" until the progression bars
  finish, announces "Main menu available" when ready, and Enter leaves for the menu. Newly
  unlocked cards shown on arrival can be reviewed with Left and Right (full detail on Alt+T)
  before continuing. Alt+I gives a screen overview at any time.
