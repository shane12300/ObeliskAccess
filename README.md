# ObeliskAccess

A screen-reader accessibility mod for **Across the Obelisk**. It adds keyboard navigation and
speech output so the game can be played without sight. Speech goes through your running screen
reader (NVDA, JAWS, and others), falling back to Windows' built-in SAPI voices if no screen
reader is running.

This guide covers installation, the controls the mod adds, and a
walkthrough of every screen in a run — first the conventions that work everywhere, then only
what is different about each screen.

## Requirements

- **Across the Obelisk** on Windows (Steam).
- **BepInEx 5** (64-bit) installed into the game folder — the mod loader. See installation below.
- A screen reader (NVDA, JAWS, etc.) is recommended. If none is running, the mod speaks through
  Windows' built-in SAPI voices instead. 
## Installation

### With the installer (recommended)

Download **`ObeliskAccessInstaller.exe`** from the mod's latest GitHub release and run it. It
is a normal Windows application with standard controls, fully usable with a screen reader. The
installer:

- finds your Across the Obelisk install automatically across Steam libraries, or lets you
  browse to it;
- installs BepInEx 5 for you when it isn't there yet;
- installs the mod, or — when a version is already installed — tells you your version and the
  latest release and offers an update;
- never overwrites a `UniversalSpeech.dll` you placed in the game folder yourself;
- can uninstall the mod again (BepInEx is left in place).

If the game folder needs administrator rights to write to (typical for installs under Program
Files), the installer offers to relaunch itself elevated.

**Prefer a terminal?** Work from the git repo instead — `scripts\deploy.ps1` builds the mod and
installs it in one command. See [Building from source](#building-from-source).

### Manual installation

1. **Install BepInEx 5.** Download the latest BepInEx 5.4.x release for **Windows x64**
   (the standard build for Unity Mono games — the file is named like
   `BepInEx_win_x64_5.4.23.5.zip`; do *not* use a BepInEx 6 / IL2CPP build) from the BepInEx
   GitHub releases page. Extract it directly into your Across the Obelisk folder — typically
   `C:\Program Files (x86)\Steam\steamapps\common\Across the Obelisk` — so that `winhttp.dll`
   and the `BepInEx` folder sit next to `AcrossTheObelisk.exe`. Run the game once and quit;
   this lets BepInEx create its folders (`BepInEx\plugins`, `BepInEx\config`, and so on).

2. **Copy the mod into the plugins folder.** From the mod release, place both DLLs together in
   their own folder under plugins:

   ```
   Across the Obelisk\BepInEx\plugins\ObeliskAccess\
       ObeliskAccess.dll
       UnityAccessibilityLib.dll
   ```

3. **Copy the speech DLLs into the game root** — the folder that contains
   `AcrossTheObelisk.exe`, *not* the plugins folder:

   ```
   Across the Obelisk\
       AcrossTheObelisk.exe
       UniversalSpeech.dll
       nvdaControllerClient.dll
   ```

   Both are 64-bit DLLs shipped with the mod release.

4. **Launch the game.** With a screen reader running (or SAPI as fallback), the main menu
   should start speaking as you press the arrow keys.

### Checking the install and troubleshooting

- The mod writes to BepInEx's log at `Across the Obelisk\BepInEx\LogOutput.log`. After a
  launch, that file should contain the line `Plugin ObeliskAccess is loaded!`. If it doesn't,
  BepInEx itself isn't loading (wrong BepInEx build, or the files are in the wrong folder).
- **Game runs, mod is loaded, but nothing speaks:** this almost always means
  `UniversalSpeech.dll` is missing from the game root, or a wrong (32-bit or outdated) copy is
  there. The log will contain a warning about UniversalSpeech in that case. Replace the DLL in
  the game root with the 64-bit copy from the mod release. Note that if you build from source,
  the build never overwrites a `UniversalSpeech.dll` already present in the game root — replace
  a stale copy by hand.
- The mod's settings are saved to `BepInEx\config\ObeliskAccess.cfg` (created on the first run
  with the mod installed). You normally never need to touch it — the same options are on the
  in-game Accessibility settings tab.
- The game's own "Keyboard shortcuts" setting is required for keyboard play, so the mod forces
  it on and keeps it on; its settings row is labelled accordingly, and turning it off is undone
  immediately.

### Reporting problems

Turn on **Debug mode** (the last option on the Accessibility settings tab), reproduce the
problem, and attach `BepInEx\LogOutput.log` to your report. Debug mode adds detailed
troubleshooting output and does not change normal play.

## Everyday controls

These conventions hold on most supported screens; each screen section below describes what
that screen adds or does differently.

- **Arrow keys** move through whatever the screen offers, speaking each item as you land on it.
  Most screens are a vertical list (Up/Down), with Left/Right used for a second axis where one
  exists (pages, columns, choices).
- **Enter** activates the focused item (the numpad Enter works too). **Escape** backs out,
  cancels, or closes.
- **Tab** switches between a screen's major areas where it has more than one (for example a
  list and a party strip); Shift+Tab cycles backwards on screens with more than two areas
  (except the town-upgrades window, where Tab only moves forward).
- **Number keys 1–4** (top row or numpad) jump to a party slot on the map, in town, and on the
  loot, shop, character-sheet, and hero-selection screens. In combat the digits keep their
  normal in-game meaning of casting cards. (On hero selection they work on the screen itself,
  not while the character window or the perk tree is open over it.)
- **Alt+T** reads full detail about the focused item wherever the game has extra detail to
  show — full card text, item text, node details, tooltips.
- **Alt+I** reads a screen overview on most screens — where you are and what's on offer — and
  most screens also speak it automatically when they open. (In combat, Alt+I instead reads the
  revealed enemy intent; the battlefield overview there is Alt+V.)
- **Alt+G** reads your money on the map, in town and its shops, and on the loot screen.
- **Alt+R** repeats the last spoken line on most screens. It is not available on the map, the
  settings menu, the main-menu screens, tutorial pop-ups, or the give
  window — but it does work inside any pop-up dialog raised from those screens, and over the
  multiplayer players panel.
- **Ctrl+Up/Down** opens a line-by-line detail drill on cards and characters where they appear
  (combat, the in-combat card-choice and pile windows, rewards, loot, and the character
  sheet's card lists): instead of one long utterance, each press steps through name, cost,
  type, description, keyword explanations, and related cards. Plain arrow keys or Escape leave
  the drill.
### Pop-up dialogs

All of the game's pop-up dialogs — confirmations, warnings, text-entry boxes, import/export
boxes, on every screen — are read as a walkable dialogue. When one opens you hear its text and
a summary of the options. Up/Down move through the text lines and then the option buttons;
Enter presses the focused **button** — Enter on a text line only reminds you to arrow down to
the options, so a destructive confirmation (deleting a save, resigning a run) can never fire by
accident (a buttonless dialog, like multiplayer's "waiting" notices, answers "No options,
waiting"). Escape cancels or dismisses. Answers you give with the mouse or a gamepad are
spoken too, and a dialog the game closes on its own — for example when a partner answers in
multiplayer — is announced as closed.

**Text entry** (run seeds, profile names, deck and perk-build names, room codes, import codes
— and the lobby's room-name and password rows, which use the same edit mode) works through an
explicit edit mode: arrow to the text field row and press Enter — the dialog says
"Editing" and reads any current text. Type normally, then press Enter or Escape to finish;
what you typed is kept and read back, and the arrows return to walking the dialog so you can
reach the accept button.

---

## A run, screen by screen

### Main menu, game modes, and save slots

The main menu speaks as you arrow through it, and each screen announces itself as it opens
("Select game mode", "Select save slot"). On the game-mode screen the mod replaces the game's
sideways layout with a simple list: Up/Down (Left/Right also work) walk the screen's buttons
in order — the Main Menu and multiplayer buttons, then the four modes, then the
Paradox-account buttons when shown. Each mode announces its name, the game's requirement line
where a mode can be locked (as in the base game, the lock is advisory — the mode still opens),
and its description. Save slots announce their position on the page ("Slot 1"); an empty slot
then says "Create new game", and a used slot reads the save's own summary — the run
description and, when the run has one, the game's own short madness marker ("M7"). A
slot's delete button appears in the list right after the slot once it is focused and reads
"Delete save slot N"; pressing it asks the game's own confirmation, read as a normal dialog.
Enter on a save the current game version can't load says "Save incompatible" instead of
loading it. The Alt review keys are not available on the main-menu screens.

### Settings

Up/Down move through the options; Enter activates a toggle, button, or dropdown; Left/Right
adjust the focused slider, spoken as a percentage. Tab cycles the tabs: Graphics, Audio,
Gameplay, and a mod-added **Accessibility** tab. Dropdowns are spoken as you arrow through
them; Escape cancels an open dropdown. Alt+T reads a description of the focused Accessibility
option (the game's own settings have no description text).

The Accessibility tab holds the mod's own options, remembered between sessions:

- **Combine enemy turn announcements** into their card plays (off by default).
- **Short status phrasing** — "Poison 7" instead of "gains Poison 3, total 7" (on by default).
- **Skip routine status decay** — the 1-point end-of-turn tick stays silent; statuses running
  out and larger losses are still announced (on by default).
- **Skip enemy status changes** entirely, keeping damage, heals, card plays, and deaths (off
  by default).
- **Speak chat messages** in multiplayer (on by default).
- **Announce partner card aim** in multiplayer — which character a partner's dragged card is
  pointing at (off by default).
- **Debug mode** — extra troubleshooting detail in the log file (off by default).

### Tutorial pop-ups

When a tutorial pop-up appears you hear its title and first line; Up/Down walk the rest of the
text and then its buttons. Enter presses the focused button — and, unlike other dialogs, Enter
on a text line presses Continue. Escape also closes. The pop-up keeps keyboard focus until it
closes, and the close is announced.

### Hero selection (run setup)

At the start of every run you hear the game mode, the madness or New Game Plus level when one
is set, the seed if the mode has one (spelled out letter by letter), how many of the available
party slots are filled, and a note when the party can't be changed (weekly challenge, or a
loaded save). On your very first adventure the game picks the party for you — the screen
announces that the run starts automatically. Tab cycles three areas; Escape from the Party or
Run-options areas jumps back to the roster:

- **Roster** — Up/Down walk the heroes, turning pages automatically; Left/Right switch the
  class filter tabs (All, Warriors, Scouts, Mages, Healers, Multiclass, Locked), each announced
  with its hero count. Every hero announces name, subclass and classes, rank and any unspent
  perk points, whether they are already in the party (and whose slot it is, in multiplayer),
  and — for locked heroes — exactly what unlocks them. Enter adds the focused hero to your
  first free slot (in multiplayer, only slots you own count); a locked or already-picked hero
  explains why instead. 1–4 put the hero in a specific slot, replacing whoever was there (the
  swap is spoken). Alt+T reads a full hero sheet (description, health, energy, speed,
  resistances, traits, rank progress); Alt+C opens the hero's character window (below).
- **Party** — Up/Down read the active slots; in multiplayer each slot also names its owner,
  whether they are ready, and whether it is yours to control. Enter clears a slot you own, or
  rolls the random-hero dice on an empty one (single player). From here and from Run options,
  1–4 jump straight to a party slot.
- **Run options** — Up/Down move between the rows, most reporting their current setting: the
  madness / New Game Plus level, weekly modifiers, sandbox mode, the seed, in multiplayer a
  Ready toggle and a Follow-the-leader toggle, and Begin Adventure. Enter on the madness,
  weekly-modifiers, or sandbox row opens the game's own panel, which is not accessible yet —
  it says so, and Escape closes it. Enter on the seed opens the text-entry dialog when the
  seed can be changed, or explains why it is fixed (weekly challenge, loaded save, host-only,
  or your first adventure). Begin Adventure explains why it is unavailable — how many more heroes the party
  needs, or that it is waiting for all players to be ready — and announces the moment it
  becomes ready.

Alt+I repeats the overview. In multiplayer, teammates joining, picking or removing heroes,
readying up, and host changes to madness or seed are all spoken as they happen.

### The character window (from hero selection)

Alt+C on a hero — in the roster or in a party slot — opens their character window (the mouse
right-click equivalent; it works on locked heroes too, where right-click doesn't). Tab moves
through its tabs — Stats, Perks, Rank, Skins, Card Backs, and Singularity Cards — skipping any
that don't apply (Singularity Cards only exists in Singularity runs; Perks, Rank, and Card
Backs drop out for locked heroes and in the weekly challenge, and Perks and Rank in Obelisk
challenges — the window says which are missing and why when it opens). Up/Down read each tab row by row: the hero's
description and strengths, the full stat block and resistances, every trait with its unlock
tier (Alt+T for the trait's description), the signature item and each starting card (Alt+T for
the full text), the classic-variant toggle where the game offers one, rank progress with every
rank reward and its locked state, and the use-supplies level-up button with the exact reason
when it can't be used — note that this button has **no confirmation step**, in the mod or in the
base game: the first Enter spends a supply. Skins and card backs are equipped with Enter — locked ones say what
unlocks them (a rank — or, for skins, a named DLC), and the one you're wearing reads as
"equipped"; card
backs are organised into categories, switched with Left/Right. The Singularity Cards tab lists
that run's singularity cards to read, with Alt+T for full text. Alt+I re-reads the window's
headline (hero, tab, and what the tab holds). Escape closes the window —
except on the Card Backs tab, where the first Escape closes the card-backs panel (landing you
on Stats) and a second one closes the window.

### The perk tree

Opens from the character window's Perks tab or the perk badge on a portrait — and mid-run from
the character sheet's Perks tab. It announces the hero and available points, and says "review
only" when perks can't be changed right now. Alt+I re-reads the points summary and Alt+R
repeats the last line. In multiplayer, editing a hero that isn't yours
is refused when you press Enter or Space, and in
the starting town some perk types read "Locked in town" — changeable only before a run. Tab
cycles the four perk categories — each reporting how many points are spent in it —
plus a Controls area. Up/Down move between the tree's rows, announcing each row's unlock
threshold; Left/Right move between the perks of a row. Every perk announces its state —
selected, locked and why, or how many more points you'd need (no prefix means it's available)
— plus choose-one groups ("Choice 2 of 3", where the other options add "taking this replaces
the chosen option" once you've picked one of the group), a "does not stack" note on
non-stacking perks naming any teammate who already has one, its effect, and its cost. Enter
takes or removes the perk and reports the running total,
or explains exactly why it can't — not enough points, "a selected perk requires it", or a
higher row would fall below its point requirement. **Space saves the build** (with nothing to
save it says so; an overspent build is refused until you remove a perk). The Controls area
holds whatever the game offers on the current screen — Confirm (the same save as Space),
Reset, Import, Export, and your perk-build save slots — plus Exit; a filled slot reads its
name and point count and offers load, with a separate delete row after it, while an empty slot
offers saving the current build, named in the dialog that follows. Closing with unsaved
changes asks first, and the close announces the return to the character sheet or window when
one is beneath. Alt+T reads the game's full
tooltip for a perk, including which party members already have it; Alt+I reads available,
total, and spent points, the split across the four categories, and a reminder if you have
unsaved changes.

### The map

- **Left/Right** move between the nodes you can travel to. **Enter** travels (in multiplayer
  it casts your travel vote — see the multiplayer section).
- **Ctrl with the arrows looks ahead** along the roads before committing: Ctrl+Up descends
  into upcoming nodes, Ctrl+Down steps back one node the way you came, Ctrl+Left/Right
  compare branching paths. Escape abandons the whole look-ahead at once and drops you back on
  the reachable node you started it from — and Enter inside a look-ahead just says "Not
  reachable yet", so you can't travel by accident. (Escape when you aren't looking ahead opens
  the game's pause menu as usual.)
- Nodes are announced with map coordinates: the first number is the node's position left to
  right among the nodes at the same depth (1 is leftmost); the second is how far into the map
  it sits, counted from where you entered the map. The numbers are fixed for the whole map, so
  they climb as you travel forward. The occasional node the mod can't trace a road to is
  spoken without coordinates.
- **Tab** switches to the party strip: Up/Down read each hero's condition (health, level,
  experience while below level 5, deck size, any injuries, a pending level-up), and Enter
  opens that hero's character sheet. **1–4** jump straight to a party slot from anywhere on
  the map, no Tab needed.
- Alt+T node detail; Alt+G gold; Alt+I position, quest trackers, and a travel tip (also spoken
  automatically when the map opens). The map has no Alt+R.

### Corruption offers

When a corruption is offered at a node, the prompt announces itself: the difficulty, the
corruption card and what it does, the enemies waiting in the fight, both rewards, and the
score accepting is worth.

Up/Down walk the offer's rows — the header, the corruption card, the enemies, reward A,
reward B, the free card a "hero card" reward would grant, whether you have accepted, and
Continue. Left/Right walk sub-items: on the enemies row they step through the line-up one at
a time (each monster's name, its position in the line, health, speed, and the aura a champion
is immune to); on either reward row they hop to the other one. Any other row just re-reads
itself. **Arrow keys never change anything** — they are safe to explore with.

Each row reports its current state as you pass it: the reward rows say whether they are the
chosen one, and the accept row says whether you have accepted and what the score bonus is. So
you can check where the offer stands at any point without altering it.

Enter acts on the focused row: it chooses that reward, toggles acceptance, or continues.
**1** and **2** choose reward A and B from anywhere. Note that choosing a reward also accepts
the corruption (that is the game's own behaviour) — it is announced when it happens. The
accept row is the way back out: Enter there switches acceptance off again (which also clears
the reward choice), and the row says so as you pass it.
Continuing with the corruption not accepted declines the offer, while continuing with it
accepted but no reward chosen is refused — the game's alert tells you to pick a reward first.
Once the choice locks in, the prompt says so and travel proceeds.

Alt+T reads the focused row in full — the corruption's complete rules text, the walked
enemy's full stats and resistances, or the free card. Alt+I repeats the whole offer, Alt+R
repeats the last line. Escape is left to the game (there is nothing to cancel — the only way
on is Continue).

In multiplayer only the host decides. Everyone else can still read every row, hears the
host's picks as they happen, and is told who they are waiting for if they try to act.

### Story events

Up/Down walk the event: title, the story text line by line, a note about any requirements you
don't meet (when the event has locked options), then your choices — each read as "Choice 2 of
3" with its roll, whether it's blocked, and any DLC note — with a hide/show-map button at the
very bottom. When the choices appear you hear how many there are. Enter picks the focused
choice; on a text line it tells you to press Down to reach the choices, and on the hide/show
button it toggles the map peek. Once the event resolves, the same walk becomes the title, the
story text, "Chosen: *your option*", the outcome line by line, and Continue — focus lands on
the first line of the outcome for you. Any card an event gives you, including injuries, is
read by name.

Dice rolls are narrated play-by-play: which card each hero draws, ties and re-rolls, each
hero's success or failure, then the outcome. While the roll animation plays, the keys go quiet
on purpose — exactly as a sighted player watches it — and narration resumes with the outcome.
Alt+T on a choice explains it in depth: your chance of success, the roll type, why an option
is blocked, and previews of any cards the choice would give you.

In single player, events with only one choice wait for you instead of auto-selecting. This is
the mod's one deliberate change to the game: the base game picks a lone choice for you half a
second after the event appears, which cut the text off mid-sentence and made the option
impossible to review. Mouse, gamepad and multiplayer selection are untouched. In multiplayer, event choices are votes like map travel — disagreements go
to the conflict screen (see Multiplayer) — Continue works as a ready toggle, and with
follow-the-leader on, picks are announced as "Following the host".

### Combat

Arrow keys move through the battlefield, announcing each stop: your hand (unplayable cards
read as "unplayable"), your heroes, the enemies, the draw and discard piles (spoken with their
counts — Enter on a pile opens the character sheet on that pile's tab), equipment icons, the initiative strip, and the End
turn button; crossing into a region announces it ("Hand", "Heroes", "Enemies"). A card that
needs a target plays in two steps: Enter picks it up (you hear "*card name*. Select target."),
arrow to a target, Enter casts. A card that needs no target plays on the first Enter. Escape
puts a held card back — silently, as the game gives no cue there either; with a pending
multiplayer ping or a detail drill open, Escape cancels those first and the next press drops
the card. The game's own combat keys keep working — Space ends your turn, and the
digits still play cards: press the card's number, then a second number for its target (heroes
first, then enemies). The mod doesn't narrate that digit sequence, so Enter is the spoken
route.

Everything that happens is spoken as it happens — damage, healing, statuses, whose turn it is,
round changes, deaths, and the closing "Victory" or "Defeat" — queued so nothing talks over
anything else, with a full battlefield overview right after the first turn announcement of
every fight.
Characters sharing a name are numbered ("Warden 1", "Warden 2") and keep their number all
fight. The narration verbosity is tunable on the Accessibility settings tab.

**Review keys** answer questions at any time without losing your place. Alt+H health, Alt+B
block, Alt+S statuses — each for the focused character, or, while a hero is acting, that hero
if nothing is focused; Alt+E reports energy for the focused hero, or for the acting hero when
an enemy (or nothing) is focused — enemies have no energy of their own. Alt+V reads
the whole battlefield, Alt+O the round and turn order, Alt+D your draw, hand, discard, and
exhaust counts, Alt+I revealed enemy intents, and Alt+C opens the focused character's sheet
(see below). Alt+T reads the focused card in full, or a character's resistances and
immunities — and, when you have drilled onto one of their buffs, curses, or traits, that
effect's full description. Ctrl+Up/Down drill into the focused card or character line by
line.

### In-combat card-selection windows

Cards that make you discard from hand (including put-on-deck and vanish variants), look at the
top of your deck, or discover a card to add open a selection window. Its cards appear over a few frames, so the window announces itself once
ready ("N cards. 1 of N: …"). Left/Right walk the candidate cards (already-picked ones read
as "selected"), Enter selects or deselects, and **Space confirms** — or tells you how many
more you must pick. Escape doesn't back out of a window that requires a choice; it restates
what is still needed. When exactly one card must be chosen, Enter takes it in one press. The
game's number keys still work and speak their result. In multiplayer, a partner's selection
says "Waiting for another player", and pressing confirm again answers "Already confirmed —
resolving". Pure "look only" peeks are read-only: browse with Left/Right, then dismiss with
Space. (To browse a pile at leisure, open the character sheet instead — Enter on the draw or
discard pile in combat, or Alt+C.)

### When a hero dies

If a hero falls but the party fights on, the death popup is read — politely queued behind the
ongoing combat narration. Up/Down walk it: who died, how they return after the fight, and the
Death's Door curse added to their deck (Alt+T reads the curse card in full). Enter continues
from any row, the close is announced, and a second hero falling while the popup is open is
announced too. In multiplayer, only the fallen hero's owner can continue — everyone else's
Enter says who they are waiting for, and the host dismisses the popup automatically after
about thirty seconds.

### Towns

Arriving in town speaks an overview. Up/Down move through everything in town: the buildings,
the upgrades window, Ready (leave town), and any treasures waiting to be claimed; Enter opens
or claims, and refusals explain themselves ("Unavailable in this game mode", "Busy, try again
in a moment"). Claiming a treasure asks for confirmation, then reads what you gained. During
the town tutorial the overview names the step you're on, and picking the wrong building
explains why nothing happened. Tab switches to the party strip, exactly as on the map.

In multiplayer, Ready is a vote — "Ready to leave town — waiting for the other players" — and
because the game silently drops your ready flag whenever you open a service, the mod tells you
("No longer ready to leave town"). A co-op divination invitation appears as its own "Join
divination" item.

### The five town services

All five services (and the travelling shops at map events) share one shape: Up/Down move
through the stock — turning the page automatically at the end of a page where the shop has
pages (Forge, Armory) — Left/Right jump a whole page on those same two, 1–4 switch which hero
you're shopping for, Alt+T reads the full card or item text, Alt+G your currencies, Alt+I a
screen overview and Alt+R a repeat; every purchase is confirmed out loud, with refusals
explained ("not enough gold", "no uses left at this shop"). Enter buys in a single press at the
Forge, Divination and Armory; at the Altar and Church it opens a preview or a confirmation that
a second Enter completes. Pressing Enter before you have arrowed onto anything answers
"Nothing focused", so a shop can never be bought from blind. Per service:

- **Altar** — upgrade cards. Enter previews the upgrade: Left/Right compare the A and B
  versions (an already-upgraded card has only its sibling path to transmute to, and a card
  with no upgrade path says so), Enter buys, Escape cancels the preview.
- **Church** — remove cards from a hero's deck, with a spoken confirmation before removal and
  the game's real removal rules explained when something can't be removed.
- **Forge** — craft and upgrade cards. Tab shows a reference view of the current hero's deck;
  Alt+F opens the card filters (functional, though its menu is still rough).
- **Divination** — browse the reading tiers and prices. The card-pick rounds that follow play
  out on the rewards screen (below). Divination serves the whole party, so 1–4 hero switching
  doesn't apply here, and in multiplayer choosing a tier enters a spoken waiting state until
  every player is in.
- **Armory** — buy equipment. Alt+T on an item adds what the hero currently has in that slot,
  and a purchase says what it replaced. Tab cycles the shop, the hero's equipped items, and
  the shop controls: switching between the item and pet shops, the reroll button (which
  explains when no rerolls remain), and the shady deal, which reports what it traded.

Escape leaves a shop; while a purchase is still settling, keys answer "Please wait" (at most
once every second or so, so a quick second press is simply silent). One multiplayer difference:
at a **map-event shop**, Escape instead marks you ready to leave ("Ready to leave. Waiting for the other
players…") and everyone leaves together when all players are ready — press Escape again to
keep shopping.

### Town upgrades

Left/Right pick a building column, Up/Down walk its upgrade chain; each upgrade reports
whether you own it, can afford it, or why it's locked, and Enter buys through the game's
confirmation dialog. Tab cycles the upgrade grid, Sell Supply (once that late-game option is
unlocked), and Exit. Alt+T reads the focused upgrade in full, Alt+G your currencies, Alt+I a
screen overview and Alt+R a repeat. Selling supplies has its own quantity picker (Up/Down adjust, with the
sale spoken).

### Rewards screen

After combat, some events, and each divination round, the party's rewards are laid out one row
per hero. Up/Down move between hero rows (a Restart row sits last while available), Left/Right
move across that hero's choices: the offered cards, the dust option, and the Deck button
(which opens that hero's character sheet). Enter takes the focused choice. Every pick — yours
or a teammate's — is confirmed out loud, and the screen announces when everyone has chosen.
Restart undoes the party's picks and redisplays the screen; in multiplayer only the host
restarts directly — anyone else's press sends a request the host has to confirm.

### Loot screen

After boss and chest fights (and at Obelisk-challenge boss, chest, and draft nodes), heroes
take turns picking items. The arrows walk the loot row — each item, the gold pile (every hero
who takes it gets the full amount, and you hear how many already have), and the Restart
button, which works as on the rewards screen — and Enter takes the focused pick for the hero
whose turn it is. Each turn change is spoken, and already-taken items stay in the row and say
who took them. Item announcements include what the choosing hero currently has equipped in
that slot. Tab reviews the party with everyone's equipped items, and 1–4 jump straight to a
hero there from anywhere on the screen. In single player, Enter on a hero in that review lets
them pick next; in multiplayer the pick order is fixed.

### The character sheet (during a run)

Open it with Enter on a hero in the map or town party strip, with Alt+C in combat (for
whichever character you're focused on — enemies included; with nothing focused it opens the
hero whose turn it is), or by mouse. It announces the hero's name, level, experience (until
level 5), health, and whether a level-up is waiting. Tab cycles its
tabs — Deck, Level, Items, Stats, and Perks outside combat; Draw pile, Discard, Vanished,
Items, Stats, and Perks during a fight — and 1–4 switch to another party member without
leaving the sheet. Escape closes.

- **Deck and pile tabs** — a header with the card count and average energy cost, then each
  card; injuries and boons sit in their own labelled section. Alt+I adds a breakdown of how
  many cards sit at each energy cost. The draw pile is read alphabetically, the same order the
  game shows it — the real draw order stays hidden from everyone; the discard pile reads
  newest first.
- **Level tab** — one row per level: experience progress, the hero's innate trait, each earned
  level with the trait you chose, and — when a level-up is ready — the two trait choices.
  Left/Right compare them (name, description, and whether it was taken); Alt+T or the
  Ctrl+Up/Down drill adds the exact card a trait would give you, at the upgrade tier you'd
  actually receive. Enter takes the focused one: the pick, the new level, your new maximum
  health, and any card the trait adds are confirmed out loud. If you can't level, Enter
  explains why — not enough experience, a level you haven't reached, a level whose trait you
  already chose, being somewhere other than the map or town (combat, rewards, and loot all
  refuse), or, in multiplayer, whose hero it is.
- **Items tab** — the five equipment slots (weapon, armor, jewelry, accessory, pet).
- **Stats tab** — health, energy, speed, cards drawn per turn, then the damage and healing
  modifiers and all nine damage types with resistances, bonuses, and penalties — Alt+T on any
  of those modifier rows itemises exactly which item or effect contributes what, like the
  game's hover tooltips (or says "No modifiers") — plus current statuses (Alt+T for the full
  status text), immunities, and charge bonuses.
- **Perks tab** — reads how many perk points are available; Enter opens the accessible perk
  tree (described above), which works mid-run everywhere the sheet opens, including the
  starting town, and announces your return to the sheet when it closes. In Obelisk challenges
  (including the weekly), the game disables perks, so the sheet has no Perks tab.

An enemy's sheet shows their stats and the cards they have cast so far, newest first. When a
story event upgrades cards mid-run ("2 cards upgraded"), the popup can be reviewed with
Left/Right (Alt+T for full text) and closed with Enter or Escape.

### Between acts

The story screen between acts — and when entering side dungeons like the Hatch or the Spider
Lair, or on completing the adventure — reads its act title and full story text on arrival.
Up/Down walk it line by line; Enter (or Escape) continues from any row. Side-dungeon entrances
with no story text announce that the game moves on by itself after a few seconds.

### End of run

If you unlocked new cards this run, their popup comes first: it announces how many, Left/Right
review them (Alt+T for full detail), and Enter or Escape closes it. The screen then announces
how the run ended, your final score, time played, the total reward, and whether progression is
still tallying. Up/Down walk every row: how the run ended, the score breakdown (places
visited, combat expertise, hero deaths, experience, bosses, corruptions, adventure-completed
bonus), the final score with any madness bonus, best-score notice and time played, the run
reward in gold and dust, the supply-retention bonus, the total reward, and each hero's rank
progress as the bars fill. The Main Menu button is the last row — Enter on any other row
reminds you to press Down to reach it. It reports "still tallying" until the progression bars
finish, announces "Main menu available" when ready, and Enter leaves.

---

## Multiplayer

Everything above works in multiplayer, with partners' actions spoken as they happen: ready
counts on shared screens ("Players ready: 1 of 2. Waiting for Bob"), whose hero is acting in
combat ("Magnus, Bob's turn"), players joining or leaving — including the host leaving and
what happens next — desync reloads, the resign-vote, player-leaving, and pending-reload
alerts, a hero changing hands, denied purchases, partners crafting, upgrading, or removing
cards at the Forge and Altar, and cinematic skip votes.

### Lobby

The multiplayer lobby is fully navigable:

- **Region panel** — the crossplay toggle (with its locked reason), quick Europe/US/Asia
  buttons, and the full region list read as a spoken option walk. Connection status is spoken
  as it changes.
- **Room browser** — each room reads its name, creator, player count, password lock,
  looking-for-more flag, whether it is full, and its version; Enter joins — a password prompt
  appears as a normal text-entry dialog, and a wrong password says so (the base game fails
  silently). A change in the number of listed rooms is announced ("Room list updated: N
  rooms"). Tab switches between the
  room list and the browser's controls: join by room code (the usual text-entry dialog), set
  up a new game, and — when the game offers it — disconnect from the region.
- **Create room** — room name and password are typed in place (the password row appears when
  the password toggle is on), plus player count and looking-for-more.
- **Room screen** — every player slot with host and version tags, the room description, the
  room code and any password spelled out letter by letter, the waiting status, Steam invites,
  kicking (Enter twice, so a slip can't kick anyone), Launch with its availability spoken
  ("need at least 2 players" / "waiting for the host"), and Exit room. Players joining or
  leaving and the launch becoming available are announced live.

Escape cancels whatever is in progress — a dropdown walk, an armed kick, or a text edit; on
the create panel it goes back to the room list, and in a room it leaves the room (with the
game's confirm). Alt+I repeats the current panel overview and Alt+R the last line.

If the room you had focused disappears from the browser before you press Enter (rooms fill and
close constantly), the mod says "That room is gone" and moves focus rather than joining
whichever room slid into its place.

### Chat

Incoming chat messages are read as they arrive (toggleable on the Accessibility tab). **Alt+Y**
types a message in place: everything you type goes only to the chat box, Enter sends it and
returns the keyboard to the screen you were on (press Alt+Y again for another message), and
Escape cancels without opening the pause menu. **Alt+M** steps back through the last twenty
messages, newest first, wrapping back around to the newest.

### Players panel

**Alt+P** opens the players panel — in a solo room it just says "No other players"; the game's
own button opens it either way. Each row reads a player's name and host tag, then "that's you" on your own
row, then their platform, whether they are ready, their mute state, their current heroes, and
finally their ping. Your own row can't be muted. Enter mutes or unmutes a partner (muting hides their chat messages and pings);
Escape closes.

### Giving gold and dust

**Ctrl+G** opens the give window from the map or the town hub (not from inside a shop, the
upgrades window, or a character sheet — back out to the map or hub first): Left/Right pick the
receiving player (their heroes are read too), Up/Down set the amount in steps of 1 — hold Ctrl
for 20, Shift for 100, both for 1000 — Tab switches between gold and dust, which resets the
amount to zero and the target back to the first player, Enter sends, Escape closes. Receiving a
gift is announced with the giver's name and your new balance; the giver hears only their own
confirmation as they send.

### Travel votes and the conflict screen

On the multiplayer map, Enter casts your travel vote ("Voting to travel to…" — as the host
under follow-the-leader it says "Leading the party to…", and a following client hears
"Following Bob to…"), with clear refusals when your vote is already locked or when
follow-the-leader gives the host the choice.
Partners' votes, the running tally, and the final unanimous departure are announced — the game
itself only shows small colored markers — and the map overview mentions follow-the-leader
whenever it is on.

When votes disagree, the card-flip conflict screen is narrated play-by-play: why it opened,
who picks the rule, the three rules (reviewed with Up/Down or 1–3, chosen with Enter when the
pick is yours), every card flip with its cost, ties and re-flips, each round's standings,
eliminations, and the winner.

### Emotes and pings (combat)

The game's own emote keys keep working in multiplayer combat — R heart, E surprise, W
indifference, Q anger — and what you or a partner sends is announced, named by the hero and
their owner ("Bob's Magnus emotes a heart"). The two targeted
pings — S, and A for attack — work from the keyboard: press the key, arrow to a character,
Enter places the ping (Escape cancels); when the emote cooldown is still running you hear
"Emotes are on cooldown for a few seconds" instead of the key failing silently. Card pings are
announced too ("Bob pings the card Fireball"). An off-by-default
option ("Announce partner card aim") speaks which character a partner's dragged card is
pointing at ("Partner aims Fireball at Warden 1" — it names the card and target, not the
partner).

---

## Not yet accessible

The mod covers a full run, but some screens are not adapted yet:

- **The pause menu** (Escape during a run: Resign, Settings, Score, Exit) is not yet spoken.
- The **madness/New Game Plus**, **sandbox**, and **weekly-modifier** setup windows open and
  announce themselves, but their interiors aren't navigable yet.
- The **Obelisk challenge setup** screen (rerolls, perks, card packs, ready) is not adapted at
  all.
- The **divination minigames** (pick-a-card and memory pairs) and the **run-start cinematic**.
- The **Tome of Knowledge**, **damage meter**, **combat log**, **score panel**, saved **team
  management**, **map legend**, and the **profiles/credits/DLC** panels on the main menu.
- **X-cost cards** (spend all your energy) are not read correctly yet — their cost and
  description can be misleading, and the pop-up that asks how much energy to spend isn't
  covered.
- **Enemy intents** currently speak revealed actions only; parity with the sighted intent
  display (action counts, Sight detail) is planned.
- **Map objectives** — the spoken node detail and map overview say less about your current
  objectives than the sighted map shows.
- **Upgrade previews outside the Altar** — the card detail drill doesn't yet list what a card
  can upgrade into; at the Altar those previews are spoken.
- Typing with a **gamepad** (the game's on-screen keyboard) isn't spoken — use a physical
  keyboard for seeds, names, and chat.

## Building from source

This is also the route to take if you would rather install from a terminal than use the
graphical installer.

Prerequisites: a .NET SDK and the game itself (the mod compiles against the game's own DLLs).
BepInEx is **not** a prerequisite here — the deploy script installs it for you if it's missing.

The deploy script does everything in one step:

```powershell
.\scripts\deploy.ps1
```

It checks the game folder; installs BepInEx 5 if it isn't already there (downloading a pinned
version to a temp folder, extracting it into the game folder, then deleting the temp folder);
runs the build; copies `ObeliskAccess.dll` + `UnityAccessibilityLib.dll` into
`BepInEx\plugins\ObeliskAccess\`; and copies the bundled `native\UniversalSpeech.dll` +
`native\nvdaControllerClient.dll` into the game root — printing exactly what it copied and what
it left alone. Unlike a manual BepInEx install, you don't need to launch the game first: the
script creates the plugins folder itself. Useful switches:

```powershell
.\scripts\deploy.ps1 -GameDir "D:\Games\Across the Obelisk"   # non-default install location
.\scripts\deploy.ps1 -Configuration Release                   # release build
.\scripts\deploy.ps1 -ForceNative                             # overwrite the speech DLLs too
.\scripts\deploy.ps1 -NoBuild                                 # copy what is already built
.\scripts\deploy.ps1 -SkipBepInEx                             # never touch BepInEx; fail if absent
.\scripts\deploy.ps1 -WhatIf                                  # list what it would do, change nothing
```

The speech DLLs in the game root are **never overwritten** unless you pass `-ForceNative`: a
copy you placed there yourself always wins.

Plain `dotnet build` also works and does the same two copies (the csproj carries build targets
for them). Pass `-p:GameDir="D:\Games\Across the Obelisk"` if your game isn't in the default
Steam location; `GameDir` must be the game's own folder, the one containing
`AcrossTheObelisk.exe`, and a wrong one shows up as missing-reference compile errors. Either
way, packages restore from nuget.org and the BepInEx feed (`nuget.bepinex.dev`).
