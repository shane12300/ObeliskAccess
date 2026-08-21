# ObeliskAccess

ObeliskAccess is an accessibility layer for Across the Obelisk that makes the game playable
through a screen reader.

Across the Obelisk is a co-operative roguelite deckbuilder. You take a party of four heroes
through a branching map of card battles, story events, and towns, building each hero's deck as
you go and starting over when the party falls. Everything is turn based, so you can play at
whatever pace suits you. A run takes an hour or two, and the whole game can be played solo or
with up to three other people.

Speech goes through whatever screen reader you have running — NVDA, JAWS, and others — falling
back to Windows' built-in SAPI voices if none is.

## Installing

Download **`ObeliskAccessInstaller.exe`** from the latest release and run it. Windows will show
a UAC prompt asking for administrator rights — accept it; the installer needs them to write into
the game folder, which is typical under Program Files. Once past that, it's a normal Windows
application with standard controls, fully usable with a screen reader. It finds your Across the
Obelisk install across your Steam libraries (or lets you browse to it), installs BepInEx 5 — the
mod loader — when it isn't there yet, installs or updates the mod, and can uninstall it again
later.

The game runs on Windows through Steam. A screen reader is recommended but not required.

If you'd rather build from the repo or unpack the release zip by hand, see
[CONTRIBUTING.md](CONTRIBUTING.md).

## Getting help

There is no in-game key list yet, so this document is the reference — the next section lists
every key that works across the mod, and each screen section below adds only what is different
about that screen.

If something goes wrong, turn on **Debug mode** (the last option on the Accessibility settings
tab), reproduce the problem, and send `Across the Obelisk\BepInEx\LogOutput.log` with a brief
description of what happened. Debug mode adds troubleshooting detail to the log and doesn't
change how the game plays.

One case is common enough to name here: if the game runs and the log says
`Plugin ObeliskAccess is loaded!` but nothing speaks, `UniversalSpeech.dll` is missing from the
game root or the wrong copy is there. Reinstalling with the installer fixes it.

The game's own "Keyboard shortcuts" setting is required for keyboard play, so the mod forces it
on and keeps it on. Turning it off is undone immediately.

## Keys

These work across most of the mod. Screens that differ say so in their own section.

- **Arrow keys** — move through whatever the screen offers, speaking each item. Most screens
  are a vertical list, with Left/Right used for a second axis where one exists: pages, columns,
  choices
- **Enter** — activate the focused item (numpad Enter too)
- **Escape** — back out, cancel, or close
- **Tab** — switch between a screen's major areas, where it has more than one. Shift+Tab cycles
  backwards on screens with three or more areas; the town-upgrades window only moves forward
- **1–4** — jump to a party slot on the map, in town, and on the loot, shop, character-sheet
  and hero-selection screens. In combat the digits keep their in-game meaning of casting cards
- **Alt+T** — full detail on the focused thing: card text, item text, node details, tooltips
- **Alt+I** — a screen overview; most screens also speak it automatically when they open
- **Alt+G** — your money, on the map, in town and its shops, and on the loot screen
- **Alt+R** — repeat the last line. Not available on the map, the settings menu, the main-menu
  screens, tutorial pop-ups, or the give window — but it does work inside any dialog raised
  from those, and over the multiplayer players panel
- **Ctrl+Up / Ctrl+Down** — step through a card or character one line at a time — name, cost,
  type, description, keyword explanations, related cards — instead of one long utterance.
  Available in combat, the in-combat card windows, rewards, loot, and the character sheet's
  card lists. Plain arrows or Escape leave the drill

In multiplayer, four keys work from anywhere: **Alt+Y** type a chat message, **Alt+M** walk the
chat history, **Alt+P** the players panel, **Ctrl+G** the give window.

### Combat review keys

Each answers a question without moving your focus. Where a key needs a character and nothing is
focused, it falls back to the hero currently acting.

- **Alt+H / Alt+B / Alt+S** — health, block, statuses
- **Alt+E** — energy (heroes only; enemies have none)
- **Alt+V** — the whole battlefield
- **Alt+O** — round and turn order
- **Alt+D** — your draw, hand, discard and exhaust counts
- **Alt+I** — revealed enemy intents
- **Alt+C** — the focused character's sheet
- **Alt+T** — the focused card in full, or a character's resistances and immunities

## Dialogs and text entry

Every pop-up the game raises — confirmations, warnings, text-entry boxes, import/export boxes —
is read as a walkable dialogue. Up/Down move through the text lines and then the option
buttons; Enter presses the focused **button**, and Enter on a text line only reminds you to
arrow down, so a destructive confirmation can't fire by accident. Escape cancels or dismisses.
A buttonless dialog, like multiplayer's waiting notices, answers "No options, waiting". Answers
given by mouse or gamepad are spoken too, as is a dialog the game closes on its own.

**Text entry** — run seeds, profile names, deck and perk-build names, room codes, import codes,
and the lobby's room-name and password rows — uses an explicit edit mode. Arrow to the field
and press Enter; the dialog says "Editing" and reads any current text. Type normally, then
press Enter or Escape to finish. What you typed is kept and read back, and the arrows return to
walking the dialog so you can reach the accept button.

**Tutorial pop-ups** work the same way, with one difference: Enter on a text line presses
Continue rather than hinting. They hold keyboard focus until closed.

## Settings

Up/Down move through the options, Enter activates a toggle, button, or dropdown, and Left/Right
adjust the focused slider, spoken as a percentage. Tab cycles the tabs: Graphics, Audio,
Gameplay, and a mod-added **Accessibility** tab. Dropdowns are spoken as you arrow through
them; Escape cancels an open one. Alt+T reads a description of the focused Accessibility option
— the game's own settings have no description text.

The Accessibility tab holds the mod's options, remembered between sessions:

- **Combine enemy turn announcements** into their card plays (off)
- **Short status phrasing** — "Poison 7" instead of "gains Poison 3, total 7" (on)
- **Skip routine status decay** — the 1-point end-of-turn tick stays silent; statuses running
  out and larger losses are still announced (on)
- **Skip enemy status changes** entirely, keeping damage, heals, card plays and deaths (off)
- **Speak chat messages** in multiplayer (on)
- **Announce partner card aim** in multiplayer (off)
- **Debug mode** — extra troubleshooting detail in the log (off)

## Setting up a run

### Main menu, game modes, save slots

The menu speaks as you arrow through it, and each screen announces itself. On the game-mode
screen the mod replaces the game's sideways layout with a simple list: Up/Down (or Left/Right)
walk the buttons in order — Main Menu and multiplayer, the four modes, then the Paradox-account
buttons when shown. Each mode announces its name, its requirement line where one can be locked,
and its description; as in the base game the lock is advisory and the mode still opens.

Save slots announce their position. An empty slot says "Create new game"; a used one reads the
save's own summary and its madness marker. A slot's delete button appears right after it once
focused. Enter on a save the current game version can't load says "Save incompatible". The Alt
review keys don't work on the main-menu screens.

### Hero selection

The screen opens with the game mode, the madness or New Game Plus level, the seed if the mode
has one (spelled letter by letter), how many party slots are filled, and a note when the party
can't be changed. On your first adventure the game picks the party for you and says so. Tab
cycles three areas; Escape from Party or Run options returns to the roster.

**Roster** — Up/Down walk the heroes, turning pages automatically; Left/Right switch the class
filter tabs, each announced with its hero count. Every hero announces name, subclass and
classes, rank and unspent perk points, whether they're already in the party, and for locked
heroes exactly what unlocks them.

- **Enter** — add to your first free slot; a locked or already-picked hero explains why instead
- **1–4** — put the hero in a specific slot, replacing whoever was there
- **Alt+T** — full hero sheet: description, health, energy, speed, resistances, traits, rank
- **Alt+C** — the hero's character window

**Party** — Up/Down read the active slots. Enter clears a slot you own, or rolls the
random-hero dice on an empty one in single player.

**Run options** — Up/Down move between rows, most reporting their current setting: madness /
New Game Plus, weekly modifiers, sandbox, the seed, and Begin Adventure. Enter on madness,
weekly modifiers or sandbox opens the game's own panel, which isn't accessible yet — it says so,
and Escape closes it. Enter on the seed opens the text-entry dialog, or explains why the seed is
fixed. Begin Adventure explains why it's unavailable — how many more heroes are needed, or that
it's waiting on other players — and announces the moment it becomes ready.

### The character window

Alt+C on a hero opens their character window — the right-click equivalent, and it works on
locked heroes where right-click doesn't. Tab moves through its tabs: Stats, Perks, Rank, Skins,
Card Backs, and Singularity Cards. Tabs that don't apply are skipped, and the window says which
are missing and why when it opens.

Up/Down read each tab row by row: the hero's description and strengths, the stat block and
resistances, every trait with its unlock tier, the signature item and each starting card, the
classic-variant toggle, rank progress with every reward and its locked state, and the
use-supplies level-up button with the exact reason when it can't be used. Alt+T gives full text
on a trait or card. Alt+I re-reads the window's headline.

Skins and card backs are equipped with Enter; locked ones say what unlocks them, and the one
you're wearing reads as "equipped". Card backs are organised into categories switched with
Left/Right. Escape closes the window — except on Card Backs, where the first Escape returns you
to Stats.

> **No confirmation on use supplies.** Neither in the mod nor in the base game — the first
> Enter spends a supply.

### The perk tree

Opens from the character window's Perks tab, from the perk badge on a portrait, and mid-run from
the character sheet's Perks tab, including in the starting town. It announces the hero and
available points, and says "review only" when perks can't be changed right now.

- **Tab** — cycle the four perk categories, each reporting points spent, plus a Controls area
- **Up / Down** — rows, announcing each row's unlock threshold
- **Left / Right** — perks within a row
- **Enter** — take or remove the perk, reporting the running total, or explain exactly why not:
  not enough points, a selected perk requires it, or a higher row would fall below its
  requirement
- **Space** — save the build. An overspent build is refused until you remove a perk
- **Alt+T** — the game's full tooltip, including which party members already have the perk
- **Alt+I** — available, total and spent points, the split across categories, and a reminder if
  you have unsaved changes

Every perk announces its state — selected, locked and why, or how many more points you'd need —
plus choose-one groups ("Choice 2 of 3"), a "does not stack" note naming any teammate who
already has one, its effect, and its cost.

The Controls area holds whatever the game offers on the current screen: Confirm, Reset, Import,
Export, your perk-build save slots, and Exit. A filled slot reads its name and point count and
offers load, with a separate delete row after it; an empty slot offers saving the current build.
Closing with unsaved changes asks first.

## The map

- **Left / Right** — move between the nodes you can travel to
- **Enter** — travel
- **Ctrl+Up / Ctrl+Down** — descend into upcoming nodes, or step back the way you came
- **Ctrl+Left / Ctrl+Right** — compare branching paths
- **Tab** — the party strip: Up/Down read each hero's condition — health, level, experience
  below level 5, deck size, injuries, a pending level-up — and Enter opens their character sheet
- **1–4** — jump to a party slot from anywhere on the map
- **Alt+T** — node detail; **Alt+G** gold; **Alt+I** position, quest trackers, and a travel tip

Ctrl with the arrows looks ahead along the roads without committing. Escape abandons the whole
look-ahead and drops you back where you started it, and Enter inside one says "Not reachable
yet", so you can't travel by accident. Escape when you aren't looking ahead opens the pause menu
as usual.

Nodes are announced with map coordinates. The first number is the node's position left to right
among nodes at the same depth, 1 being leftmost; the second is how far into the map it sits,
counted from where you entered. Both are fixed for the whole map, so the second climbs as you
travel. The occasional node the mod can't trace a road to is spoken without coordinates.

The map has no Alt+R.

### Corruption offers

When a corruption is offered at a node, the prompt announces the difficulty, the corruption card
and what it does, the enemies waiting, both rewards, and the score accepting is worth.

Up/Down walk the rows: header, the corruption card, the enemies, reward A, reward B, the free
card a "hero card" reward would grant, whether you've accepted, and Continue. Left/Right walk
sub-items — on the enemies row they step through the line-up one monster at a time, on either
reward row they hop to the other. Each row reports its current state as you pass it, so you can
check where the offer stands at any time.

**Arrow keys never change anything.** Enter acts on the focused row: choose that reward, toggle
acceptance, or continue. **1** and **2** choose reward A and B from anywhere. Choosing a reward
also accepts the corruption, which is the game's own behaviour and is announced when it happens;
Enter on the accept row switches acceptance off again and clears the reward choice. Continuing
without accepting declines the offer; continuing with it accepted but no reward chosen is
refused.

Alt+T reads the focused row in full — the corruption's complete rules text, the walked enemy's
stats and resistances, or the free card.

### Story events

Up/Down walk the event: title, the story text line by line, a note about requirements you don't
meet, then your choices — each read as "Choice 2 of 3" with its roll, whether it's blocked, and
any DLC note — with a hide/show-map button at the bottom. Enter picks the focused choice. Once
the event resolves the same walk becomes the title, the story text, "Chosen: *your option*", the
outcome line by line, and Continue, with focus landing on the first line of the outcome. Any
card an event gives you, injuries included, is read by name.

Alt+T on a choice explains it in depth: your chance of success, the roll type, why an option is
blocked, and previews of any cards it would give you.

Dice rolls are narrated play-by-play — which card each hero draws, ties and re-rolls, each
hero's success or failure, then the outcome. The keys go quiet while the roll animation plays
and narration resumes with the outcome.

In single player, events with only one choice wait for you instead of auto-selecting after half
a second as the base game does. Mouse, gamepad and multiplayer selection are untouched.

## Combat

Arrow keys move through the battlefield, announcing each stop: your hand (unplayable cards read
as "unplayable"), your heroes, the enemies, the draw and discard piles with their counts,
equipment icons, the initiative strip, and the End turn button. Crossing into a region announces
it.

- **Enter** — play the focused card, or cast a held one on the focused target
- **Escape** — put a held card back. With a pending ping or an open detail drill, Escape cancels
  those first
- **Space** — end your turn
- **Digits** — the game's own casting: the card's number, then a number for its target, heroes
  first. The mod doesn't narrate this sequence, so Enter is the spoken route
- **Enter** on a pile — open the character sheet on that pile's tab

A card that needs a target plays in two steps: Enter picks it up — "*card name*. Select target."
— then arrow to a target and press Enter. A card that needs no target plays on the first Enter.

Everything that happens is spoken as it happens: damage, healing, statuses, whose turn it is,
round changes, deaths, and the closing "Victory" or "Defeat" — queued so nothing talks over
anything else, with a full battlefield overview right after the first turn announcement of every
fight. Characters sharing a name are numbered ("Warden 1", "Warden 2") and keep their number all
fight. How much of this is spoken is tunable on the Accessibility settings tab.

The review keys are listed [above](#combat-review-keys).

### Card-selection windows

Cards that make you discard from hand, look at the top of your deck, or discover a card to add
open a selection window. Its cards appear over a few frames, so the window announces itself once
ready.

- **Left / Right** — walk the candidates; already-picked ones read as "selected"
- **Enter** — select or deselect. When exactly one card must be chosen, Enter takes it outright
- **Space** — confirm, or tell you how many more you must pick
- **Escape** — restates what's still needed; it won't back out of a window that requires a choice

The game's number keys still work and speak their result. "Look only" peeks are read-only:
browse with Left/Right, dismiss with Space. To browse a pile at leisure, open the character
sheet instead.

### When a hero dies

If a hero falls but the party fights on, the death popup is read, queued behind the ongoing
combat narration. Up/Down walk it: who died, how they return after the fight, and the Death's
Door curse added to their deck, with Alt+T for the curse card in full. Enter continues from any
row. A second hero falling while the popup is open is announced too.

## Between fights

### Rewards

After combat, some events, and each divination round, rewards are laid out one row per hero.
Up/Down move between hero rows, with a Restart row last while available; Left/Right move across
that hero's choices — the offered cards, the dust option, and the Deck button, which opens that
hero's character sheet. Enter takes the focused choice. Every pick is confirmed out loud, and
the screen announces when everyone has chosen. Restart undoes the party's picks and redisplays
the screen.

### Loot

After boss and chest fights, and at Obelisk-challenge boss, chest and draft nodes, heroes take
turns picking items. The arrows walk the loot row — each item, the gold pile, and Restart — and
Enter takes the focused pick for the hero whose turn it is. Each turn change is spoken, and
already-taken items stay in the row and say who took them. Item announcements include what the
choosing hero currently has equipped in that slot. Tab reviews the party with everyone's
equipped items, and 1–4 jump to a hero from anywhere on the screen; in single player, Enter on a
hero in that review lets them pick next.

### Towns

Arriving in town speaks an overview. Up/Down move through everything: the buildings, the
upgrades window, Ready (leave town), and any treasures waiting. Enter opens or claims, and
refusals explain themselves. Claiming a treasure asks for confirmation, then reads what you
gained. During the town tutorial the overview names the step you're on, and picking the wrong
building explains why nothing happened. Tab switches to the party strip, exactly as on the map.

### The five town services

All five services, and the travelling shops at map events, share one shape:

- **Up / Down** — move through the stock, turning the page automatically where the shop has
  pages (Forge, Armory)
- **Left / Right** — jump a whole page on those same two
- **Enter** — buy. One press at the Forge, Divination and Armory; at the Altar and Church it
  opens a preview or confirmation that a second Enter completes
- **1–4** — switch which hero you're shopping for
- **Alt+T** full card or item text, **Alt+G** currencies, **Alt+I** overview, **Alt+R** repeat

Every purchase is confirmed out loud, with refusals explained. Enter before you've arrowed onto
anything answers "Nothing focused", so a shop can never be bought from blind. Escape leaves a
shop; while a purchase is settling, keys answer "Please wait".

- **Altar** — upgrade cards. Enter previews; Left/Right compare the A and B versions, Enter
  buys, Escape cancels the preview
- **Church** — remove cards from a deck, with a spoken confirmation and the game's real removal
  rules explained when something can't be removed
- **Forge** — craft and upgrade cards. Tab shows a reference view of the current hero's deck;
  Alt+F opens the card filters
- **Divination** — browse the reading tiers and prices; the card-pick rounds play out on the
  rewards screen. Divination serves the whole party, so 1–4 doesn't apply
- **Armory** — buy equipment. Alt+T adds what the hero has in that slot, and a purchase says
  what it replaced. Tab cycles the shop, the hero's equipped items, and the shop controls: the
  item and pet shops, the reroll button, and the shady deal

### Town upgrades

Left/Right pick a building column, Up/Down walk its upgrade chain. Each upgrade reports whether
you own it, can afford it, or why it's locked, and Enter buys through the game's confirmation
dialog. Tab cycles the upgrade grid, Sell Supply once that late-game option is unlocked, and
Exit. Selling supplies has its own quantity picker on Up/Down, with the sale spoken.

### The character sheet

Open it with Enter on a hero in the map or town party strip, with Alt+C in combat — for whoever
you're focused on, enemies included — or by mouse. It announces the hero's name, level,
experience until level 5, health, and whether a level-up is waiting. Tab cycles the tabs: Deck,
Level, Items, Stats and Perks outside combat; Draw pile, Discard, Vanished, Items, Stats and
Perks during a fight. 1–4 switch to another party member without leaving the sheet.

- **Deck and pile tabs** — a header with the card count and average energy cost, then each card,
  with injuries and boons in their own labelled section. Alt+I adds a breakdown of how many
  cards sit at each energy cost. The draw pile is read alphabetically, the same order the game
  shows it; the discard pile reads newest first
- **Level tab** — one row per level: experience progress, the innate trait, each earned level
  with the trait you chose, and, when a level-up is ready, the two choices. Left/Right compare
  them; Alt+T or the drill adds the exact card a trait would give you, at the tier you'd receive.
  Enter takes the focused one and confirms the pick, the new level, your new maximum health, and
  any card added. If you can't level, Enter explains why
- **Items tab** — the five equipment slots
- **Stats tab** — health, energy, speed, cards drawn per turn, the damage and healing modifiers,
  and all nine damage types with resistances, bonuses and penalties. Alt+T on a modifier row
  itemises exactly which item or effect contributes what. Then current statuses, immunities and
  charge bonuses
- **Perks tab** — reads how many perk points are available; Enter opens the perk tree. In
  Obelisk challenges the game disables perks, so the tab isn't there

An enemy's sheet shows their stats and the cards they've cast so far, newest first. When a story
event upgrades cards mid-run, the popup can be reviewed with Left/Right, Alt+T for full text,
and closed with Enter or Escape.

## Between acts

The story screen between acts — and when entering side dungeons like the Hatch or the Spider
Lair, or on completing the adventure — reads its act title and full story text on arrival.
Up/Down walk it line by line; Enter or Escape continues from any row. Side-dungeon entrances with
no story text announce that the game moves on by itself after a few seconds.

## End of run

If you unlocked new cards, their popup comes first: it announces how many, Left/Right review
them with Alt+T for detail, and Enter or Escape closes it.

The screen then announces how the run ended, your final score, time played, the total reward,
and whether progression is still tallying. Up/Down walk every row: how the run ended, the score
breakdown, the final score with any madness bonus, best-score notice and time played, the run
reward in gold and dust, the supply-retention bonus, the total, and each hero's rank progress as
the bars fill. The Main Menu button is the last row — it reports "still tallying" until the bars
finish, announces "Main menu available" when ready, and Enter leaves.

## Multiplayer

Everything above works in multiplayer. Partners' actions are spoken as they happen: ready counts
on shared screens, whose hero is acting in combat, players joining or leaving, the host leaving
and what follows, desync reloads, resign and reload votes, heroes changing hands, denied
purchases, partners crafting at the Forge and Altar, and cinematic skip votes. Where the host
owns a decision, your keys refuse with the host's name and tell you who you're waiting for.

### Lobby

- **Region panel** — the crossplay toggle with its locked reason, quick Europe/US/Asia buttons,
  and the full region list read as a spoken option walk. Connection status is spoken as it
  changes
- **Room browser** — each room reads its name, creator, player count, password lock,
  looking-for-more flag, whether it's full, and its version. Enter joins; a password prompt
  appears as a normal text-entry dialog, and a wrong password says so. A change in the number of
  listed rooms is announced. Tab switches between the room list and the browser's controls: join
  by room code, set up a new game, and disconnect from the region when offered
- **Create room** — room name and password typed in place, plus player count and
  looking-for-more
- **Room screen** — every player slot with host and version tags, the room description, the room
  code and any password spelled letter by letter, the waiting status, Steam invites, kicking
  (Enter twice, so a slip can't kick anyone), Launch with its availability spoken, and Exit room.
  Players joining or leaving and the launch becoming available are announced live

Escape cancels whatever is in progress — a dropdown walk, an armed kick, a text edit — goes back
to the room list from the create panel, and leaves the room from a room.

If the room you had focused disappears before you press Enter, the mod says "That room is gone"
and moves focus rather than joining whichever room slid into its place.

### Chat

Incoming messages are read as they arrive, toggleable on the Accessibility tab. **Alt+Y** types
a message in place: everything you type goes only to the chat box, Enter sends it and returns
the keyboard to the screen you were on, and Escape cancels without opening the pause menu.
**Alt+M** steps back through the last twenty messages, newest first.

### Players panel

**Alt+P** opens the players panel; in a solo room it just says "No other players". Each row reads
a player's name and host tag, "that's you" on your own row, their platform, whether they're
ready, their mute state, their current heroes, and their ping. Enter mutes or unmutes a partner,
which hides their chat messages and pings; your own row can't be muted. Escape closes.

### Giving gold and dust

**Ctrl+G** opens the give window from the map or the town hub — not from inside a shop, the
upgrades window, or a character sheet; back out first.

- **Left / Right** — pick the receiving player, whose heroes are read too
- **Up / Down** — set the amount in steps of 1; hold Ctrl for 20, Shift for 100, both for 1000
- **Tab** — switch between gold and dust, which resets the amount and target
- **Enter** — send. **Escape** — close

Receiving a gift is announced with the giver's name and your new balance.

### Travel votes and the conflict screen

On the multiplayer map, Enter casts your travel vote rather than travelling, with clear refusals
when your vote is locked or when follow-the-leader gives the host the choice. Partners' votes,
the running tally, and the final unanimous departure are announced — the game itself only shows
small coloured markers — and the map overview mentions follow-the-leader whenever it's on. Event
choices are votes in the same way, and Continue works as a ready toggle.

When votes disagree, the card-flip conflict screen is narrated play-by-play: why it opened, who
picks the rule, the three rules — reviewed with Up/Down or 1–3, chosen with Enter when the pick
is yours — every card flip with its cost, ties and re-flips, each round's standings,
eliminations, and the winner.

### Emotes and pings

The game's emote keys keep working in multiplayer combat — R heart, E surprise, W indifference,
Q anger — and what you or a partner sends is announced, named by the hero and their owner. The
two targeted pings, **S** and **A** for attack, work from the keyboard: press the key, arrow to
a character, Enter places the ping, Escape cancels. While the emote cooldown is running you hear
so instead of the key failing silently. Card pings are announced too. An off-by-default setting,
Announce partner card aim, speaks which character a partner's dragged card is pointing at.

## Not yet accessible

The mod covers a full run, but some screens aren't adapted yet:

- The **pause menu** during a run: Resign, Settings, Score, Exit
- The **madness / New Game Plus**, **sandbox** and **weekly-modifier** setup windows — they open
  and announce themselves, but their interiors aren't navigable
- The **Obelisk challenge setup** screen
- The **divination minigames** 
- The **Tome of Knowledge**, **damage meter**, **combat log**, **score panel**, saved **team
  management**, **map legend**, and the **profiles / credits / DLC** panels
- **X-cost cards** — their cost and description can be misleading, and the pop-up asking how
  much energy to spend isn't covered
- **Enemy intents** speak revealed actions only; parity with the sighted display is planned
- **Map objectives** — the node detail and map overview say less than the sighted map shows
- **Upgrade previews outside the Altar** — the card drill doesn't yet list what a card can
  upgrade into
- Typing with a **gamepad**, via the game's on-screen keyboard — use a physical keyboard for
  seeds, names and chat
