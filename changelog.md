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
  row. Every perk announces its state when it has one — selected, locked and why, or how many
  more points you'd need; a perk with no state prefix is available to take — plus choose-one
  groups ("Choice 2 of 3 — taking this replaces the chosen option"), a warning when a
  teammate already has a non-stacking perk, its effect, and its cost. Enter takes or removes the perk and reports the running total — or explains exactly
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
- The in-run character sheet is now fully accessible — including leveling up. Open it with
  Enter on a hero in the map or town party strip (Tab reaches the strip, 1–4 jump to a slot),
  with Alt+C in combat for whichever character you're reviewing, or by mouse as before; it
  announces the hero's name, level, experience, health, and whether a level-up is waiting.
  Tab cycles its tabs — Deck, Level, Items, Stats, and Perks outside combat; Draw pile,
  Discard, Vanished, Items, Stats, and Perks during a fight — and the number keys 1 to 4
  switch to another party member without leaving the sheet.
  - The Deck tab (and the combat pile tabs) reads a header with the card count and average
    energy cost, then each card with Up and Down; injuries and boons sit in their own labelled
    section. Full card text on Alt+T, line-by-line detail with Ctrl+Up/Down, and Alt+I adds a
    breakdown of how many cards sit at each energy cost. The draw pile is read alphabetically,
    exactly as the game shows it — the real draw order stays hidden from everyone.
  - The Level tab walks one row per level: your experience progress, each earned level with
    the trait you chose, and — when a level-up is ready — the two trait choices. Left and
    Right compare them (including the exact card a trait would add, at the upgrade tier you'd
    actually receive), and Enter takes the focused one: the pick, the new level, and the
    health gain are all confirmed out loud. If you can't level yet, Enter explains exactly
    why — not enough experience, mid-combat (the game only allows it on the map or in town),
    or in multiplayer, whose hero it is. Teammates' level-ups are announced as they happen.
  - The Items tab reads all five equipment slots (weapon, armor, jewelry, accessory, pet)
    with full item detail on Alt+T; the Stats tab reads health, energy, speed, cards drawn
    per turn, the damage and healing modifiers, all nine resistances with damage bonuses and
    penalties (Alt+T itemises exactly which item or effect contributes what, like the game's
    hover tooltips), plus current status effects, immunities, and charge bonuses.
  - The Perks tab opens the same accessible perk tree from the hero-selection screen, which
    now also works mid-run everywhere the sheet opens — including the starting town — and
    returns you to the sheet when closed.
  - Right-clicking (or Alt+C on) an enemy in combat reads their sheet too: their stats and
    the cards they have cast so far, newest first.
  - Cards upgraded by story events ("2 cards upgraded") can be reviewed with Left and Right
    before closing, like the end-of-run unlocked-cards popup.
- The story screen between acts (and when entering side dungeons like the Hatch or the Spider
  Lair, or on completing the adventure) is now read aloud: the act title and the full story
  text speak on arrival, Up and Down walk it line by line, Alt+R repeats it, and Enter (or
  Escape) continues from any row. Side-dungeon entrances with no story text announce that the
  game moves on by itself after a few seconds.
- Fixed: choosing a story-event option with Enter always took the first option, no matter
  which one was focused. The game's own hidden Enter handling — an invisible mouse click at
  the last cursor position — was firing alongside the mod's and won the race. The same
  invisible click is now suppressed on every mod-driven screen (map, town, services, upgrades,
  corruption offers, tutorials, settings, and the rest), so Enter always acts on exactly the
  item being read — it could previously travel to the wrong map node, buy the wrong shop item,
  or even silently accept a corruption offer.
- Multiplayer emotes and pings in combat are now fully usable and spoken. The game's own keys
  keep working — R heart, E surprise, W indifference, Q anger — and what you or a partner
  sends is announced by name. The two targeted pings (S, and A for attack) now work from the
  keyboard: press the key, arrow to a character, and press Enter to place the ping (Escape
  cancels); the three-second cooldown is spoken instead of failing silently. Card pings on the
  table are announced ("Bob pings the card Fireball"), and the combat overview now lists the
  emote keys in multiplayer. A new off-by-default option ("Announce partner card aim") speaks
  which character a partner's dragged card is pointing at, on target changes only.
- More partner actions are echoed in multiplayer: a hero changing hands ("Magnus is now
  controlled by Bob"), a declined shop purchase ("Purchase failed — already sold or not enough
  gold"), and partners crafting, upgrading, or removing cards at the Forge and Altar.
- Multiplayer chat is now spoken: incoming messages (and the joined/kicked notices that arrive
  through chat) are read aloud as they appear — toggleable on the Accessibility tab. Alt+Y
  types a message in place: everything you type goes only to the chat box, Enter sends it, and
  Escape cancels without opening the pause menu. Alt+M steps back through the last twenty
  messages, newest first.
- Players panel (Alt+P, or the game's own button): each player's name, host tag, platform,
  ping, ready state, current heroes, and mute state are read; Enter mutes or unmutes them
  (muting hides their chat messages and pings), Escape closes.
- Give window (Ctrl+G on the map or in town): pick the receiving player with Left and Right
  (their heroes are read too), set the amount with Up and Down — hold Control for steps of 20,
  Shift for 100, both for 1000 — Tab switches between gold and dust, Enter sends. Receiving a
  gift is announced with the giver's name and your new balance.
- Multiplayer lobby made accessible: region selection (crossplay toggle with its locked reason,
  quick Europe/US/Asia buttons, and the full 13-region list), connection status spoken as it
  changes, the room browser (each room reads its name, creator, player count, password lock,
  looking-for-more flag and version; Enter joins, and the list announces its refreshes), join
  by room code, room setup (room name and password typed in place, player count,
  looking-for-more), and the room screen itself — every player slot with version and host
  tags, the room code spelled out letter by letter, Steam invites, kicking (Enter twice, so a
  slip can't kick anyone), and Launch with its availability spoken ("need at least 2
  players" / "waiting for the host"). Players joining or leaving the room and the launch
  becoming available are announced live; Escape backs out of each panel; the join-by-code,
  password, kick-received and exit prompts all use the existing spoken dialogs. Alt+I repeats
  the current panel overview, Alt+R the last line.
- Multiplayer map travel made accessible: pressing Enter on a node now casts your travel vote
  ("Voting to travel to…"), with clear refusals when your vote is already locked or when
  follow-the-leader gives the host the choice. Partners' votes, the running tally, and the
  final unanimous departure are all announced — the game itself only shows small colored
  markers — and the map overview mentions follow-the-leader whenever it is on. When votes
  disagree, the card-flip conflict screen is fully narrated: why it opened, who picks the
  rule, the three rules (reviewed with Up/Down or 1–3, chosen with Enter when it's your
  pick), every card flip with its cost, ties and re-flips, each round's results, eliminations,
  and the winner. Alt+R repeats the last line.
- Fixed: in multiplayer the keyboard could randomly go completely dead until Escape was
  pressed. The game's hidden Tab handling could silently move focus into the chat box, after
  which every key was treated as chat typing. Tab can no longer land in the chat box, and the
  chat box no longer takes focus from any keyboard navigation — clicking it or Alt+Y still work.
- Fixed: when a teammate crafted, upgraded, or removed a card while you had a shop open, you
  heard a bogus local purchase line ("Crafted. N dust remaining") on top of the teammate
  announcement. Only the teammate line is spoken now.
- Changed: closing an event shop on the map with Escape in multiplayer now works like the
  game's own exit button: it marks you ready to leave ("Ready to leave. Waiting for the other
  players…") and everyone leaves together when all players are ready; pressing Escape again
  lets you keep shopping. Previously Escape closed the shop for you alone, leaving you stuck
  on a silent black screen until the others finished — and could hang the whole party.
- Multiplayer ambient awareness: ready counts are spoken as partners ready up on the shared
  screens ("Players ready: 1 of 2. Waiting for Bob"); combat turn announcements name the
  owning player when a partner's hero acts; combat desync reloads, resign votes, and
  leave-game prompts are announced; players joining or leaving the room — including the host
  leaving and what happens next — are spoken as they occur; a co-op divination invitation is
  read aloud and can be joined from a new "Join divination" item at the top of the town list;
  and cinematic skip votes are counted out loud.
- Fixed: in multiplayer the corruption offer could speak the wrong rewards — the previous
  offer's text, or a leftover "heal" placeholder shown as both options — because the
  announcement raced the network sync that fills the real labels in. The offer is now only
  spoken once the actual rewards for the current node have arrived, so what you hear always
  matches what your partners see.
- Fixed: in multiplayer, the leftmost card of a full hand could be silently unplayable — no
  hover sound when focusing it, and Enter did nothing — until some other card was played. The
  chat window's invisible click area sits over that corner of the screen and was swallowing
  the keyboard's card activation. Pressing Enter on a card in your hand now always plays (or
  picks up) that exact card, and the usual card hover sound plays when focusing it even while
  the chat area covers it.
- Debug mode (Accessibility tab) now records more detail for troubleshooting: what each
  combat Enter press actually lands on, and any speech call slow enough to hitch the game —
  useful evidence to attach when reporting multiplayer slowdowns or dead keys.
- Fixed: in a multiplayer room where someone had left and someone else joined, the two-step
  kick could announce the wrong player's name (and mark "that's you" on the wrong slot). The
  kick itself always removed the player shown on the slot — but the spoken name could
  disagree with it. Kick announcements now always name exactly the player rendered on that
  slot.
- Fixed: after a player left mid-run, the players panel (Alt+P) could read a stale row for
  the departed player while making a later player unreachable. The panel now lists exactly
  the players still present, and mute/unmute always lands on the player you hear.
- The give window is safer around disconnects: switching currency with Tab now closes the
  window with an announcement when no other players remain (previously this could freeze the
  game), sending refuses cleanly if the chosen seat has been vacated, and Ctrl+G no longer
  opens the window after the connection has dropped.
- Fixed: reviewing the room panel while being removed from a room could throw errors and eat
  a keypress; it now says "Leaving the room." instead.
- Fixed: a joining player was sometimes announced by their internal network name; the join is
  now spoken with their real display name as soon as it arrives.
- Fixed: switching lobby panels while typing a room name or password no longer speaks a
  leftover "Room name set to…" over the new panel's announcement.
- Hardened the multiplayer vote-conflict narration so an unexpected speech error can never
  interrupt the game's card-flip sequence mid-round.
- Fixed: opening the give window from the Forge's gold button now correctly takes over the
  keyboard — previously Enter kept buying shop items behind the give window, and on a
  travelling shop Escape could cast a ready-vote instead of closing it.
- Fixed: cancelling the room-password prompt with Escape no longer announces "Wrong
  password" — that is only spoken when a password was actually submitted and rejected.
- Fixed: after submitting text in a dialog (run seed, room password, perk import), the next
  dialog answered by mouse or controller could close silently; answers are spoken again.
- Fixed: pressing Escape on a text-entry dialog could speak a leftover button label from an
  earlier dialog (for example "Keep deck"); it now says "Cancelled." when no cancel button
  is on screen.
- Fixed: submitting text in a dialog no longer announces "Alert closed." ahead of the
  "Submitted…" confirmation — the submit is now the only line spoken, in the right order.
- Fixed: pressing Ctrl on a shop or service screen, in the multiplayer lobby, or on a story
  event no longer triggers a hidden click — previously it could open the game's on-screen
  keyboard over the shop search bar or silently press an event reply.
- If the game's on-screen keyboard does open (for example from a controller), the arrow keys
  and Enter now drive the keyboard itself instead of secretly acting on the screen beneath
  it; Escape closes it as before.
- Picking up a card in combat with Enter now speaks the card's name ("Fireball picked up…")
  instead of the generic "Card picked up".
- The in-combat card-selection windows (discard, look-at-deck, discover) now queue their
  opening and closing announcements behind the ongoing combat narration instead of cutting
  it off, refuse cleanly with "Not ready yet" while their cards are still appearing, and
  refuse further Enter presses with "Already confirmed — resolving" once you have confirmed
  in multiplayer (previously they could silently corrupt the shared selection).
- The death popup now handles a second hero falling while it is open ("X also fell…")
  instead of mixing the two heroes' details, no longer skips its first line when the popup
  has no title, and reads stylised hero names without markup.
- Multiplayer pings aimed at a character who just died are no longer announced as delivered
  — matching the game, which ignores them.
- The hover sound fallback for keyboard-focused cards now also covers cards hidden behind
  other windows (for example the chat overlay), and a partner highlighting a card no longer
  silences it.
- After a party wipe on round 1 followed by a retry (and after a multiplayer desync reload),
  the rebuilt combat announces its round, battlefield overview, and emote keys again —
  previously the retried combat started silent.
