# TODO — screens & dialogues not yet adapted

Inventory from a full sweep of `../decompiled/` (2026-07-20), checked against current mod coverage.

**Adapted so far:** main menu (+ game-mode select, save slots), settings, tutorial popups,
map (nodes / party strip / gold & position), corruption prompt, combat (navigation,
announcements, review keys).

Most unadapted surfaces expose the same controller contract the mod already piggybacks on:
`ControllerMovement(...)` + `controllerList`/`_controllerList` + an index field + `IsActive()`.
The game's own modality priority order (from `InputController.cs` ~l.818–982) is:
`Keyboard > Alert > Settings > CardScreen > DamageMeter > Tome > MainMenu > PerkTree > Sandbox >
Madness > Lobby > HeroSelection/CharPopup > Give > Intro > Cinematic > Challenge >
CardPlayer(Pairs) > Event > Town > Map > Rewards > Loot > FinishRun > Match` — use it when
slotting new contexts into `Plugin.Awake`.

Priority = how often a player hits it in a normal run × how hard it blocks a blind player.

## P0 — blocks completing a single-player run (core loop, every run)

- [ ] **Narrative events** — `EventManager` (+ `Reply`): event text, reply options with
      requirement/probability dice, continue button. Has `ControllerMovement`. The single biggest
      gap: events are unreadable and unanswerable today.
- [ ] **Post-combat card rewards** — `RewardsManager` (+ `CharacterReward`): per-hero reward card
      picks, dust. Has `ControllerMovement`. After nearly every fight.
- [ ] **Loot / chest screen** — `LootManager` (+ `CharacterLoot`, `LootItem`): gold + item pickups
      per hero. Has `ControllerMovement`.
- [ ] **Generic confirmation alerts** — `AlertManager`: Yes/No dialogs used everywhere (resign,
      reload, overwrite…). Has `ControllerMovement` + `IsActive()`. Already a known router gap
      (alerts outside settings have no accessible Enter/Escape). Highest modality priority after
      the virtual keyboard — register near the top.
- [ ] **Hero selection / run setup** — `HeroSelectionManager` (+ `BoxSelection`, `BoxPlayer`):
      pick party, ready, begin adventure, seed. Has `ControllerMovement`. Without it a run can't
      even be configured (currently only default-party flows work).
- [ ] **Town hub** — `TownManager` (+ `TownBuilding`): enter Forge/Church/Altar/Cart/Armory,
      ready-up, treasures, supply. Has `ControllerMovement`. Between-act core loop.
- [ ] **Card shop / craft** — `CardCraftManager`: buy/upgrade/craft cards, filters; also drives
      obelisk-challenge setup nav. Has `ControllerMovement`. The main gold sink each town visit.
- [ ] **End of run** — `FinishRunManager` + `FinishProgression` (+ `ProgressionRow`,
      `UnlockedBar`): victory/defeat summary, unlock reveal, back to menu. Has `ControllerMovement`.

## P1 — frequent; run is possible but seriously degraded without these

- [ ] **Character sheet** — `CharacterWindowUI` + sub-panels `DeckWindowUI` / `ItemsWindowUI` /
      `PerksWindowUI` / `StatsWindowUI` / `LevelWindowUI`. Host has `ControllerMovement`;
      sub-panels don't (need our own focus walk). Also unblocks the map party strip's deferred
      "Enter opens panel".
- [ ] **Perk tree** — `PerkTree` (+ `PerksManager`): spend perk points on level-up, confirm/reset.
      Has `ControllerMovement` + `IsActive()`. Level-ups happen every run.
- [ ] **Card-inspect screen** — `CardScreenManager`: full card zoom + related cards. Has
      `ControllerMovement`. In combat we deliberately suppress it and speak instead — remaining
      speech gaps:
  - [ ] **Alt+T on a card**: also speak rarity and upgrade state (not upgraded / blue A / gold B /
        corrupted) — one extra clause.
  - [ ] **Drill category "Upgrades to"** (Ctrl+↑/↓): each path's name + description + differences
        (`CardItem.ShowDifferences`, `CardItem.cs:4155`; data `CardRealtimeData.UpgradesTo1/2/
        UpgradedFrom/UpgradesToRare` via `Globals.Instance.GetCardData(id, instantiate: false)`).
  - [ ] **Drill category "Related cards"** (`CardRealtimeData.HaveRelatedCards`/`RelatedCards`):
        name + description of each ("this card creates Burn — what does Burn do?").
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
- [ ] **Town upgrades / sell supply** — `TownUpgradeWindow`. Has `ControllerMovement`.
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
