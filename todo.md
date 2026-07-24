# TODO — screens & dialogues not yet adapted

Inventory from a full sweep of `../decompiled/` (2026-07-20), checked against current mod coverage.

**Adapted so far:** main menu (+ game-mode select, save slots), settings, tutorial popups,
map (nodes / party strip / gold & position), corruption prompt, combat (navigation,
announcements, review keys), narrative events (text walk, choices, roll narration, Alt+T
hover info), town (hub, all five service screens, upgrades window, treasures), reward
screen (post-combat / event / divination card picks).

Most unadapted surfaces expose the same controller contract the mod already piggybacks on:
`ControllerMovement(...)` + `controllerList`/`_controllerList` + an index field + `IsActive()`.
The game's own modality priority order (from `InputController.cs` ~l.818–982) is:
`Keyboard > Alert > Settings > CardScreen > DamageMeter > Tome > MainMenu > PerkTree > Sandbox >
Madness > Lobby > HeroSelection/CharPopup > Give > Intro > Cinematic > Challenge >
CardPlayer(Pairs) > Event > Town > Map > Rewards > Loot > FinishRun > Match` — use it when
slotting new contexts into `Plugin.Awake`.

Priority = how often a player hits it in a normal run × how hard it blocks a blind player.

## P0 — blocks completing a single-player run (core loop, every run)

- [ ] **Narrative events — remaining spot-checks** — follow-up for the already-implemented
      map-event (story dialog) accessibility layer (`EventAccessibilityPatch` +
      `EventInputContext` + `EventHotkeyPoller`; see Completed section). Tentatively verified
      in-game with one event incl. roll + probability; remaining permutations (roll targets,
      blocked options, reward cards, chained events) to be spot-checked during normal play.
- [ ] **Generic confirmation alerts** — `AlertManager`: Yes/No dialogs used everywhere (resign,
      reload, overwrite…). Has `ControllerMovement` + `IsActive()`. Partially closed 2026-07-20:
      `AlertConfirm`/`AlertConfirmDouble` are now spoken globally, and the town-family contexts
      (hub / upgrades / card-craft) stay active during alerts and answer them via `AlertHelper`
      (Enter=`SetConfirmAnswer(true)`, Escape=`CloseAlert()`). Remaining gap: alerts raised on
      screens whose contexts still suspend on `AlertManager.IsActive()` (map, combat, events,
      main menu) — fold those onto `AlertHelper` or add a dedicated top-priority alert context.
- [ ] **Hero selection / run setup** — `HeroSelectionManager` (+ `BoxSelection`, `BoxPlayer`):
      pick party, ready, begin adventure, seed. Has `ControllerMovement`. Without it a run can't
      even be configured (currently only default-party flows work).
- [ ] **Filters dialogue (Alt+F) needs rework** — follow-up for the already-implemented card
      shop / craft accessibility layer (`CardCraftManager` service screens — Forge filter
      modal, `CardCraftAccessibilityPatch`; see Completed section). Functional, but not
      displaying as neatly as it should. A complex change: the user is considering a complete
      redesign of the filter menu rather than incremental tweaks. Deferred for now (user
      feedback 2026-07-20).
- [ ] **End of run** — `FinishRunManager` + `FinishProgression` (+ `ProgressionRow`,
      `UnlockedBar`): victory/defeat summary, unlock reveal, back to menu. Has `ControllerMovement`.

## Combat improvements — fixes to the already-adapted combat screen

- [ ] **X-cost mechanic doesn't read correctly** — cards with the X mechanic (spend all
      remaining energy) are not announced correctly; fix how their cost/description is spoken.
- [ ] **Discover / discard needs fixing** — the discover and discard card-selection flows are
      broken for the accessibility layer (see also the `UIDiscardSelector` / `UIAddcardSelector`
      items under P1 in-combat selector popups).
- [ ] **General exploration of cards in combat** — sweep different card types/keywords to
      ensure each reads correctly and is understandable via speech.
- [ ] **Look-and-discard card effects** — cards whose effects let you look at cards (e.g. peek
      at the top of a deck) and choose which to discard have no accessible flow yet; add
      navigation + speech for that look/peek-then-discard selection. Related to the
      discover/discard item above and the `UIDeckCards` / `UIDiscardSelector` popups under P1.
- [ ] **Enemy intents** — match the sighted flow: without the Sight effect, sighted players
      still see how many actions an enemy intends, so always announce the action count; when
      Sight is active, speak the full card detail of each intended action instead of the
      current revealed-only summary.

## Map improvements — fixes to the already-adapted map screen

- [ ] **Objective readout parity** — the sighted map view shows more objective information than
      the mod currently speaks; expand the spoken objective readout (Alt+I / node detail) to
      match what sighted players see.

## Cross-screen improvements

- [ ] **Alt+G full currency readout** — on every screen that offers it, Alt+G should speak all
      currencies the player holds (gold, dust, supplies, …), not just gold.

## P1 — frequent; run is possible but seriously degraded without these

- [ ] **Character sheet** — `CharacterWindowUI` + sub-panels `DeckWindowUI` / `ItemsWindowUI` /
      `PerksWindowUI` / `StatsWindowUI` / `LevelWindowUI`. Host has `ControllerMovement`;
      sub-panels don't (need our own focus walk). Also unblocks the map party strip's deferred
      "Enter opens panel".
- [ ] **Perk tree** — `PerkTree` (+ `PerksManager`): spend perk points on level-up, confirm/reset.
      Has `ControllerMovement` + `IsActive()`. Level-ups happen every run.
- [ ] **Card-inspect screen** — `CardScreenManager`: full card zoom + related cards. Has
      `ControllerMovement`. In combat we deliberately suppress it and speak instead — remaining
      speech gaps (rarity/upgrade state, keywords, related cards now covered by the card detail
      speech enrichment; see Completed section):
  - [ ] **Drill category "Upgrades to"** (Ctrl+↑/↓): each path's name + description + differences
        (`CardItem.ShowDifferences`, `CardItem.cs:4155`; data `CardRealtimeData.UpgradesTo1/2/
        UpgradedFrom/UpgradesToRare` via `Globals.Instance.GetCardData(id, instantiate: false)`).
  - [ ] Outside combat (rewards/shop/sheet), consider letting the real screen open and adapting it.
- [ ] **In-combat selector popups** — navigated via `MatchManager.ControllerMovement` branches,
      not their own contexts:
  - [ ] `UIDiscardSelector` — "choose a card to discard" (many card effects).
  - [ ] `UIDeckCards` — draw/discard-pile viewer.
  - [ ] `UIAddcardSelector` — "add a card" chooser.
  - [ ] `UIEnergySelector` — X-cost / energy choice.
  - [ ] `UICombatDeath` / `MatchManager.ShowDeathScreen` — hero death / defeat screen.
- [ ] **Pause menu** — `OptionsManager` (ESC in run): Resign / Settings / Score / Exit, plus
      "can't exit" guards. Has `InputMoveController`.
- [ ] **Map adjuncts** — `ConflictManager` (1-of-3 branch choice, has `ControllerMovement`);
      `PopupNode` (node info popup — we already speak node summaries via Alt+T, verify parity).
- [ ] **Hero detail popup** — `CharPopup` (+ `CharPopupMini`): stats/rank/perks/skins tabs inside
      hero selection. Has `ControllerMovement`. Needed for informed party picks.

## P2 — occasional single-player surfaces

- [ ] **Madness (difficulty) select** — `MadnessManager`. Has `ControllerMovement`.
- [ ] **Obelisk challenge setup** — `ChallengeSelectionManager`/`2` (nav routed through
      `CardCraftManager.ControllerMovement`): reroll, perks, packs, ready.
- [ ] **Weekly modifiers** — `WeeklySelector` (draw/reveal).
- [ ] **Sandbox modifiers** — `SandboxManager`. Has `ControllerMovement` + `IsActive()`.
- [ ] **Divination minigames** — `CardPlayerManager` (pick-a-card), `CardPlayerPairsManager`
      (memory pairs). Both have `ControllerMovement`.
- [ ] **Intro & cinematics** — `IntroNewGameManager`, `CinematicManager`: speak story text,
      Enter/Escape to skip. Both have `ControllerMovement`.
- [ ] **Damage meter** — `DamageMeterManager`. Has `ControllerMovement` + `IsActive()`.
- [ ] **Combat tool / log** — `CombatToolManager` (+ `PopupNodeCombatTool`): detailed combat
      math; a speech "combat log review" may serve better than adapting the panel.
- [ ] **Score panel** — `Score` (via pause menu).
- [ ] **Character stat-sheet popup** — `PopupSheet` (hover-driven; likely fold into existing
      review keys rather than adapt directly).
- [ ] **Tome of Knowledge** — `TomeManager` (+ scene/sub-widgets): codex of cards/heroes/runs,
      search. Has `ControllerMovement` + `IsActive()`.
- [ ] **Team management** — `TeamManagement`: saved builds from main menu.
- [ ] **Map legend** — `MapLegend` (could be a spoken summary instead).

## P3 — multiplayer & rare

- [ ] **MP lobby** — `LobbyManager` (+ `RoomList`): create/join room, regions. Has
      `ControllerMovement`.
- [ ] **Virtual keyboard** — `KeyboardManager`: controller text entry (chat, seeds, names). Has
      `ControllerMovement`; highest modality priority. (Physical-keyboard users may bypass it —
      verify text fields accept real typing first.)
- [ ] **Give gold/dust** — `GiveManager`. Has `ControllerMovement` + `IsActive()`.
- [ ] **Chat** — `ChatManager` (+ `ChatController`): speak incoming messages, navigate player list.
- [ ] **Emotes / pings** — `EmoteManager`.
- [ ] **Connection alerts** — `NetworkManager` (mostly surfaces through `AlertManager` — P0 item
      likely covers it; verify).
- [ ] **Cardback picker** — `CardBackSelectionPanel` (cosmetic).
- [ ] **Profiles / credits / DLC popup** — `MainMenuManager` sub-panels not yet announced
      (profile list, credits, DLC info).

## Non-goals / covered another way

- **Tooltips** (`PopupManager`, `Popup`, `PopupAuraCurse`, `PopupHPBar`, `PopupText`) —
  hover-driven and non-navigable; the mod speaks the same data directly from its review keys
  instead of adapting them.
- **HUD overlays** (`PlayerUIManager`, `SideCharacters`, `PlayerStatusManager`) — persistent
  visuals; surfaced via Alt+G/Alt+I-style summaries per screen, not focus navigation.
- **`LogosManager` / `TrailerManager`** — boot splash/attract mode; nothing to navigate.

## Completed

Finished features, moved here from the priority sections above. Any follow-up work a completed
feature still requires stays in its original priority section (with a note pointing back here).

- [x] **Narrative events** (was P0) — `EventManager` (+ `Reply`): event text, reply options with
      requirement/probability dice, continue button. Implemented 2026-07-20
      (`EventAccessibilityPatch` + `EventInputContext` + `EventHotkeyPoller`): tutorial-style
      text walk with choices at the bottom, play-by-play roll narration, Alt+T hover-info
      mirror. *Remaining work — spot-checks of untested permutations — tracked under P0.*
- [x] **Town hub** (was P0) — `TownManager` (+ `TownBuilding`): enter Forge/Church/Altar/Cart/
      Armory, ready-up, treasures, supply. Implemented 2026-07-20 (`TownAccessibilityPatch` +
      `TownInputContext` + `TownHotkeyPoller`): Up/Down hub items incl. treasures, Tab party
      strip, tutorial-step alerts spoken and answerable, arrival overview, Alt+T/G/I/R.
      **Verified in-game 2026-07-20 — works correctly.**
- [x] **Card shop / craft** (was P0) — `CardCraftManager`: buy/upgrade/craft cards, filters.
      Implemented 2026-07-20 for craftType 0–4 (`CardCraftAccessibilityPatch` +
      `CardCraftInputContext` + `CardCraftHotkeyPoller`): Altar A/B preview, Church
      confirm-remove, Forge/Armory grids with page auto-advance (Down past last item) and
      Left/Right paging, Divination tiers, Alt+F filter modal (Forge), hero switch 1–4,
      purchase announces via `Hero.*` postfixes. NOT covered: obelisk-challenge setup nav
      (craftType 5, stays a P2 item), corruption flows (6/7 — the existing corruption handling
      owns those), post-divination reward screen (since completed — see the rewards entry
      below), deck save/load slots
      at the starting town. **Verified in-game 2026-07-20 — all five service screens work
      correctly.** *Remaining work — Alt+F filter menu redesign — tracked under P0.*
- [x] **Town upgrades / sell supply** (was P2) — `TownUpgradeWindow`. Implemented 2026-07-20
      (`TownUpgradeAccessibilityPatch` + `TownUpgradeInputContext`): Left/Right column,
      Up/Down chain, owned/available/locked reasons, buy via the game's confirm alert,
      sell-supply quantity sub-mode. **Verified in-game 2026-07-20 — works correctly.**
- [x] **Card detail speech enrichment** (was P1, two sub-items of "Card-inspect screen") —
      Alt+T and the Ctrl+↑/↓ drill now speak everything the game shows on and around a hovered
      card. Implemented 2026-07-21 (`CardSpeech.DetailLines`/`FullDetail`, shared by combat and
      town/shop screens): brief line incl. rarity + upgrade state, card type + target,
      description (incl. the "X equals ..." explainer), require line ("Requires Stanza"),
      cost-modification reasons (reductions / until-discarded / Exhaustion), per-keyword
      glossary, and related-card previews. Combat drill walks these as discrete lines; Alt+T
      speaks them as one interruptible utterance with the live in-combat cost.
      **Verified in-game 2026-07-21 — user reports it much better.** *Remaining work —
      "Upgrades to" drill category and outside-combat card-inspect adaptation — tracked
      under P1.*
- [x] **Loot / chest screen** (was P0) — `LootManager` (+ `CharacterLoot`, `LootItem`): gold +
      item pickups per hero after boss/chest fights and on Obelisk-challenge nodes. Implemented
      2026-07-24 (`LootAccessibilityPatch` + `LootInputContext` + `LootHotkeyPoller`): arrows walk
      the loot row (items → gold pile → Restart), Enter takes for the hero whose turn it is, turn
      changes announced from a per-frame poll, item announces carry an equipped-slot comparison,
      Tab party review with equipped items (Enter reorders the picker in SP, 1–4 jump), Ctrl+↑/↓
      detail drill, Alt+T/I/G/R. Fixed same-day: the game's Enter fallthrough fired a synthetic
      click at the stale mouse position (could open the deck window and strand input) — the
      router now swallows the game's Enter on loot/rewards, suppresses the bare-Ctrl click on
      loot, and announces character-window open/close. **Verified in-game 2026-07-24 — works
      correctly.**
- [x] **Single-choice event auto-select suppressed** — the game auto-picks a lone event option
      0.5s after showing it (single player only), which outran speech and made the option
      unreviewable. Suppressed via a gated `Reply.SelectThisOption` prefix (2026-07-24); the
      mod's one deliberate behaviour deviation, documented in the README under "Design
      philosophy". Mouse/gamepad/multiplayer selection paths unaffected. **Verified in-game
      2026-07-24.**
- [x] **Post-combat card rewards** (was P0) — `RewardsManager` (+ `CharacterReward`): per-hero
      reward card picks, dust. Implemented 2026-07-21 (`RewardsAccessibilityPatch` +
      `RewardsInputContext` + `RewardsHotkeyPoller`): table navigation — Up/Down hero rows with
      Restart as a final pseudo-row, Left/Right across cards → dust → Deck button; Enter takes
      the focused choice (Singularity overwrite and multiplayer restart confirms answered in
      place via `AlertHelper`); Ctrl+↑/↓ card-detail drill; Alt+T full detail, Alt+I overview,
      Alt+R repeat; arrival overview waits out the row animation; picks and the auto-close
      announced; the game's hover sounds mirrored on focus moves. Also covers map-event rewards
      and town Divination rounds (same scene). The Deck button opens the game's character
      window, which is not yet accessible — that's the P1 "Character sheet" item.
      **Verified in-game 2026-07-21 — works correctly.**
