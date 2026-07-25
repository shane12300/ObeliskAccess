# TODO — screens & dialogues not yet adapted

Inventory from a full sweep of `../decompiled/` (2026-07-20), checked against current mod coverage.

**Adapted so far:** main menu (+ game-mode select, save slots), settings, tutorial popups,
map (nodes / party strip / gold & position), corruption prompt, combat (navigation,
announcements, review keys), narrative events (text walk, choices, roll narration, Alt+T
hover info), town (hub, all five service screens, upgrades window, treasures), reward
screen (post-combat / event / divination card picks), loot screen, in-combat card-selection
windows, all confirmation alerts (global walkable dialogue), in-combat death popup, the
end-of-run screen, and the hero-selection screen (party building, run options, character
window with all tabs, perk tree).

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
- [ ] **Hero selection — in-game verification** — follow-up for the implemented hero-selection
      accessibility layer (see Completed): the full manual test pass (roster paging, tab
      filters, assign/replace/clear/dice, seed alert, Begin edge, character-window tabs, perk
      take/remove/confirm/save-slots, Obelisk/Weekly/load-game modes, MP echoes) has not yet
      been run in-game.
- [ ] **Filters dialogue (Alt+F) needs rework** — follow-up for the already-implemented card
      shop / craft accessibility layer (`CardCraftManager` service screens — Forge filter
      modal, `CardCraftAccessibilityPatch`; see Completed section). Functional, but not
      displaying as neatly as it should. A complex change: the user is considering a complete
      redesign of the filter menu rather than incremental tweaks. Deferred for now (user
      feedback 2026-07-20).
## Combat improvements — fixes to the already-adapted combat screen

- [ ] **X-cost mechanic doesn't read correctly** — cards with the X mechanic (spend all
      remaining energy) are not announced correctly; fix how their cost/description is spoken.
- [ ] **General exploration of cards in combat** — sweep different card types/keywords to
      ensure each reads correctly and is understandable via speech.
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

- [ ] **Perk tree in town — gate Controls rows on visibility** — follow-up for the completed
      mid-run perk tree (see Completed, "In-run character sheet"): the tree now works in-run
      (incl. town tier 0), but the Controls-region rows should be gated on button visibility —
      the game hides some controls (save slots/import/export) outside the hero-selection
      scene, and the mod's rows may still offer them there. Verify in-game and gate as needed.
- [ ] **Card-inspect screen** — `CardScreenManager`: full card zoom + related cards. Has
      `ControllerMovement`. In combat we deliberately suppress it and speak instead — remaining
      speech gaps (rarity/upgrade state, keywords, related cards now covered by the card detail
      speech enrichment; see Completed section):
  - [ ] **Drill category "Upgrades to"** (Ctrl+↑/↓): each path's name + description + differences
        (`CardItem.ShowDifferences`, `CardItem.cs:4155`; data `CardRealtimeData.UpgradesTo1/2/
        UpgradedFrom/UpgradesToRare` via `Globals.Instance.GetCardData(id, instantiate: false)`).
  - [ ] Outside combat (rewards/shop/sheet), consider letting the real screen open and adapting it.
- [ ] **In-combat selector popups — remaining** — follow-up for the completed card-selection
      windows (`UIDiscardSelector` / `UIDeckCards`; see Completed). Still needing a context:
  - [ ] `UIEnergySelector` — X-cost / energy choice (relates to the X-cost readout item under
        Combat improvements).
- [ ] **Pause menu** — `OptionsManager` (ESC in run): Resign / Settings / Score / Exit, plus
      "can't exit" guards. Has `InputMoveController`.
- [ ] **Map adjuncts** — `ConflictManager` (1-of-3 branch choice, has `ControllerMovement`);
      `PopupNode` (node info popup — we already speak node summaries via Alt+T, verify parity).

## P2 — occasional single-player surfaces

- [ ] **Madness (difficulty) select** — `MadnessManager`. Has `ControllerMovement`. Now
      reachable from the hero-selection run options (Enter opens it and announces it is not
      yet accessible; all hero-selection contexts go inert while it is open). Also covers the
      weekly-modifiers window — the WeeklyModifiers button opens the same madness window.
- [ ] **Obelisk challenge setup** — `ChallengeSelectionManager`/`2` (nav routed through
      `CardCraftManager.ControllerMovement`): reroll, perks, packs, ready.
- [ ] **Weekly modifiers** — `WeeklySelector` (draw/reveal).
- [ ] **Sandbox modifiers** — `SandboxManager`. Has `ControllerMovement` + `IsActive()`. Now
      reachable from the hero-selection run options (open + announce only, like madness).
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
- [x] **In-combat card-selection windows** (was P1 "In-combat selector popups" —
      `UIDiscardSelector`, `UIDeckCards`, `UIAddcardSelector` — plus the Combat-improvements
      items "Discover / discard needs fixing" and "Look-and-discard card effects") — implemented
      2026-07-24 (`CombatSelectorAccessibilityPatch` + `CombatSelectorInputContext`, registered
      above Combat): covers discard-from-hand (discard / top-deck / vanish), look-at-top-of-deck
      windows (incl. pure peeks), the discover "choose a card to add" window, and the
      draw/discard pile viewers. Left/Right review cards ("selected" flagged), Enter toggles
      (mandatory single picks auto-confirm), Space confirms (or speaks "select N more"), digits
      still toggle natively with spoken results, Ctrl+↑/↓ drill, Alt+T/R; game's Enter/Space
      suppressed under the windows. `UIAddcardSelector` is dead code in the current game build —
      discover runs through `UIDeckCards` type 3. Root-caused and fixed the cast blocker: on
      deck-effect cards the hero drop-target is eclipsed by `DeckInHero` overlay colliders that
      swallow the synthetic Enter click, so with a held card Enter on a character now casts via
      `ControllerExecute()` directly; card pickup is also announced now. Added a Debug mode
      toggle (Accessibility tab, default off) gating the troubleshooting logs added during this
      work. **Verified in-game 2026-07-24 — works correctly.** *Remaining work —
      `UIEnergySelector` — tracked under P1 (the death screen has since been completed; see
      below).*
- [x] **Post-combat card rewards** (was P0) — `RewardsManager` (+ `CharacterReward`): per-hero
      reward card picks, dust. Implemented 2026-07-21 (`RewardsAccessibilityPatch` +
      `RewardsInputContext` + `RewardsHotkeyPoller`): table navigation — Up/Down hero rows with
      Restart as a final pseudo-row, Left/Right across cards → dust → Deck button; Enter takes
      the focused choice (Singularity overwrite and multiplayer restart confirms via the global
      alert dialogue); Ctrl+↑/↓ card-detail drill; Alt+T full detail, Alt+I overview,
      Alt+R repeat; arrival overview waits out the row animation; picks and the auto-close
      announced; the game's hover sounds mirrored on focus moves. Also covers map-event rewards
      and town Divination rounds (same scene). The Deck button opens the game's character
      window (since completed — see the "In-run character sheet" entry below).
      **Verified in-game 2026-07-21 — works correctly.**
- [x] **Generic confirmation alerts** (was P0) — `AlertManager`, all five dialog shapes (confirm
      single/double incl. the buttonless MP "waiting" variant, text input, copy/paste).
      Completed 2026-07-24 as a full redesign (`AlertAccessibilityPatch` + top-priority
      `AlertInputContext` + `AlertHotkeyPoller`): every alert is now a walkable dialogue — body
      lines then one row per visible option button, Enter activates only on an option row,
      Escape follows `CloseAlert` semantics. Replaced the old split model entirely (`AlertHelper`
      deleted, per-context alert answering/exclusions removed, Settings' inline alert branches
      removed); this also closed the old "remaining gap" — alerts on map/combat/event/main-menu
      screens (e.g. the party-wipe retry dialog) are now answerable. **Verified in-game
      2026-07-24 — works correctly.**
- [x] **In-combat death popup** (was the P1 selector-popups sub-item `UICombatDeath`) —
      hero dies but the party survives. Implemented 2026-07-24
      (`DeathScreenAccessibilityPatch` + `DeathScreenInputContext`, registered above
      CombatSelector): opening announcement is queued so it never interrupts turn narration;
      Up/Down walk title / body / Death's Door note / Continue row; Enter continues (owner/host
      only in MP — others hear "Waiting for {owner}"); Alt+T reads the Death's Door curse,
      Alt+R repeats; close announced on button, 30s timeout, or MP RPC. **Verified in-game
      2026-07-24 — works correctly.**
- [x] **Hero selection / run setup** (was P0), **Hero detail popup** (was P1), **Perk tree**
      (was P1, hero-selection scope) and **Cardback picker** (was P3) — `HeroSelectionManager`
      (+ `BoxSelection`), `CharPopup` (+ `CardBackSelectionPanel`), `PerkTree`. Implemented
      2026-07-24 (`HeroSelectionAccessibilityPatch` + `CharPopupAccessibilityPatch` +
      `PerkTreeAccessibilityPatch` + `HeroSpeech` + three contexts + one poller): Tab regions
      Roster/Party/Run options with paged roster, filter tabs, assign/replace/clear/random
      dice, spelled seed with the accessible input alert, madness/sandbox open-and-announce,
      MP ready/follow/echoes, Begin edge announce; Alt+C character window with all six tabs
      (stats/traits/cards, perk-points row, rank + use-supplies, skins, card backs incl.
      categories and pages, singularity cards); perk tree with category+Controls Tab stops,
      row thresholds, choose-one groups, take/remove diagnosis, Space confirm, save slots via
      the global alert dialogue. Router: Enter-swallow + bare-Alt suppression extended to all
      three contexts. *Remaining work — in-game verification pass — tracked under P0;
      madness/sandbox/weekly interiors stay P2. The town-tier-0 perk tree re-prioritisation
      has since been completed — see the "In-run character sheet" entry below.*
- [x] **In-run character sheet** (was P1 "Character sheet") and **Perk tree in town / mid-run**
      (was P1 "Perk tree in town (tier 0)", the re-prioritisation half) — `CharacterWindowUI`
      on all five screens it opens over (map, town, combat, rewards, loot). Implemented
      2026-07-25 (`CharWindowAccessibilityPatch` + `CharWindowInputContext` +
      `CharWindowHotkeyPoller`, registered above Combat and below PerkTree — which moved up
      and lost its HeroSelection scene gate): opens from party-strip Enter on map/town
      (closing that deferred item), Alt+C in combat (focused character; enemies get their
      stats/cards-cast view), or any mouse path. Tab cycles the tabs (Deck/Level/Items/Stats/
      Perks; combat piles in combat), rows rebuilt from game data (never the spawned
      CardItems), 1–4 hero switch, Ctrl+↑/↓ drill, Alt+T/I/R. Level tab: ←/→ compare the two
      trait choices (with the granted card at its real tier), Enter levels up via
      `hero.LevelUp`/`HeroLevelUpMP` with diagnosed refusals; outcomes announced queued from a
      `Hero.LevelUp` postfix (mouse + MP echoes included). Stats tab reads the game's own
      per-source breakdown popups on Alt+T. Mid-run upgraded-cards popup covered. The Perks
      tab opens the (now scene-agnostic) accessible perk tree, covering the town-tier-0 tree.
      **Verified in-game 2026-07-25 — works correctly.** *Remaining work — gating the perk
      tree's Controls rows on button visibility in town — tracked under P1.*
- [x] **End of run** (was P0) — `FinishRunManager` + `FinishProgression`. Implemented 2026-07-24
      (`FinishRunAccessibilityPatch` + `FinishRunInputContext` + `FinishRunHotkeyPoller`):
      arrival overview, Up/Down rows read live (header, score breakdown, final score with
      madness/best/time, reward + retention + total, per-hero progression bars as they animate),
      Main Menu button last ("Still tallying" until the bars finish, "Main menu available"
      announced on enable, Enter exits). The unlocked-cards popup on arrival is a sub-mode:
      Left/Right review, Alt+T full detail, Enter/Escape close. Alt+I overview, Alt+R repeat.
      **Verified in-game 2026-07-24 — works correctly.**
