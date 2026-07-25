# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

ObeliskAccess is a BepInEx 5 mod for "Across the Obelisk" that makes the game accessible to screen-reader users via keyboard navigation and speech output.

## Build

```bash
dotnet build
```

The output DLL (`bin/Debug/net46/ObeliskAccess.dll`) must be copied to the game's BepInEx plugins folder to test. There are no automated tests.

## todo.md maintenance

Whenever a feature is completed and the user asks to commit it, check `todo.md`. If the feature
appears there, move its entry to the `## Completed` section at the bottom of the file (mark it
`[x]` and note which priority section it came from). If the entry has attached work that still
needs doing (sub-items, remaining verification, deferred follow-ups), keep that work in its
original priority section as its own `[ ]` item, adding explanatory text that identifies which
system/feature it belongs to, and add a pointer to it from the Completed entry.

## changelog.md maintenance

After every commit, update `changelog.md` with the work done. The changelog is **user-facing**:
describe what changed in the mod from a player's perspective (new screens made accessible, new
keys, behaviour fixes) — not code-level changes. Add entries under the **current** version
heading; **never** change or add a version number unless the user explicitly says to. The
current working version is 1.0, marked as the initial release.

## Architecture

Input handling and speech are split into two concerns: a **central input router** decides which
screen owns the keyboard and translates raw keys into semantic events; per-screen **contexts** and
**announcement patches** implement each screen's behaviour and spoken output.

### Core data flow

1. **`Plugin.cs`** — BepInEx entry point. Registers the `IInputContext`s in priority order, attaches
   the map hotkey poller, then calls `Harmony.PatchAll()` to auto-register all `[HarmonyPatch]` classes.
2. **`Input/` — the input router** (see below). The one place that owns keyboard dispatch.
3. **`SpeechManager`** — Single TTS integration point. Delegates to the `UnityAccessibilityLib`
   NuGet package (net35 build, resolved for our net46 target), which speaks through an active screen
   reader (NVDA/JAWS/…) via `UniversalSpeech`, falling back to Windows SAPI. `Speak(string)`
   interrupts current speech (menus/navigation — last-write-wins); `SpeakQueued(string)` appends
   without interrupting so the screen reader's own queue serialises a burst (combat announcements);
   `RepeatLast()` / `Stop()` are also exposed. `Plugin.Awake` calls `SpeechManager.Initialize(Logger)`.
   **Runtime requirement:** the 64-bit `UniversalSpeech.dll` must be placed in the game's root folder
   (next to the executable). If absent, `Initialize` logs a warning and all speech becomes a silent
   no-op — the mod still runs. The build copies `UnityAccessibilityLib.dll` into the plugin folder
   alongside `ObeliskAccess.dll`.
4. **`Patches/` — per-screen state + announcement.** `AccessibleMenuBase` provides text helpers
   (`StripRichText` is public; `GetMenuItemText`/`AnnounceItem`/`InvokeItemButton` are `protected`).
   Each screen's file holds its open/close/navigation announce patches and (for stateful screens) a
   static manager the context delegates to (e.g. `MapNavigator`, `TutorialPopupManager`).
   Multiplayer text goes through `Patches/MpSpeech.cs` (`IsMp`, `LocalOwns`, `DisplayNick`,
   `OwnerNick`, `OwnershipClause` — all null-safe, all collapse to the SP answer outside MP; the
   older per-screen copies in Loot/Rewards/CharWindow predate it, consolidation is a todo).
   Screen-less MP awareness (ready counts, desync reloads, room join/leave/host-left, alert
   icons, cinematic skip votes) lives in `Patches/MpAmbientAccessibilityPatch.cs`; partner
   turn/cast narration needs no extra patches — MP combat is lock-step simulated on every client,
   so the combat patches fire for partners' actions too, and `OnTurnChanged` just adds an
   owner-nick clause.

### Central input router (`Input/`)

- `IInputContext.cs` — the interface + `InputContextBase` no-op base. A context exposes `IsActive`
  (queried live from game state) and handlers `OnMove(Vector2)`, `OnConfirm()`, `OnCancel()`,
  `OnTab(bool)`, `OnNumber(int)`. A handler returning `true` consumes the event.
- `InputRouter.cs` — priority-ordered context registry. Each event goes to the single
  highest-priority context whose `IsActive` is true (no fall-through). Also the home of raw-key
  helpers: `IsKeyboard`, `IsEnter`, `IsTab`, `IsDigit`, and the modifier reads
  `ShiftHeld` / `CtrlHeld` / `AltHeld`. `Controller` holds the in-flight `InputController` for
  contexts that call back into the game; `IsActive(context)` lets a component act only while its
  screen owns input.
- `RouterInputPatches.cs` — **the ONLY patches on `InputController`.** `DoMovement` (prefix,
  swallows arrows iff handled) → `Move`; `DoEscape` (prefix, swallowable) → `Cancel`; `DoKeyBinding`
  postfix routes Enter→`Confirm`, Tab→`Tab`, digits 1–4→`Number`, plus a prefix that swallows the
  game's own handling of keys the mod repurposes. **The game DOES wire Enter**: with
  `ConfigKeyboardShortcuts` on (which `ForceKeyboardShortcutsPatch` forces), `DoKeyBinding` maps
  Enter (and bare Ctrl) → `DoFirePerformed`, a synthetic click at the cursor — so any context whose
  `OnConfirm` performs its own activation must be in the prefix's Enter-swallow list or every Enter
  fires twice (this once skipped the mode-selection screen: the second fire's physics fallback hit
  the just-activated mode collider under the stale cursor, and later made event Enter "always pick
  option 1" — the stale click hit the first reply before the context's own select ran). As of the
  2026-07-25 audit **every context is in the list except Combat**, which deliberately stays out:
  in combat Enter maps to `BattleKeyboard.KeyboardEnter` (inert in plain combat, but the only
  working Enter for the not-yet-covered energy-transfer selector `UIEnergySelector`), and the
  combat context's Confirm rides the game's warped-cursor click path by design. Any new
  self-activating context MUST be added to the list. A `DoFirePerformed` prefix likewise
  suppresses the bare-Ctrl "click" on screens where Ctrl is a modifier (map look-ahead, detail
  drills), and a `DoButtonNorth` prefix suppresses the bare-Alt synthetic right-click on screens
  where Alt is the review modifier (combat and the hero-selection family — on HeroSelection a
  bare Alt would right-click the portrait under the cursor and pop the character window).
- `Input/Contexts/*InputContext.cs` — one per screen (thin; delegates to a `Patches/` manager).
- `Input/MapHotkeyPoller.cs`, `Input/CombatHotkeyPoller.cs`, `Input/EventHotkeyPoller.cs`,
  `Input/TownHotkeyPoller.cs`, `Input/CardCraftHotkeyPoller.cs`, `Input/AlertHotkeyPoller.cs`,
  `Input/FinishRunHotkeyPoller.cs`, `Input/CharWindowHotkeyPoller.cs`, `Input/IntroHotkeyPoller.cs` — `MonoBehaviour`s that poll letters the game leaves
  **unbound** (Alt+letter review keys), since the InputAction system never fires for them; each is
  gated on `InputRouter.IsActive(context)`. The combat/event/town/craft/finish-run pollers also
  run their manager's per-frame tick *outside* that gate (lifecycle detection — town arrival,
  craft-screen open, finish-run arrival — must survive a modal owning input).

Registration order in `Plugin.Awake` **is** priority (highest first):
`Alert > Tutorial > Settings > Corruption > CardCraft > DeathScreen > CombatSelector > PerkTree >
CharWindow > Combat > Conflict > Event > TownUpgrade > Town > Map > Rewards > Loot > FinishRun > Intro >
CharPopup > HeroSelection > MainMenu`. A modal thus sits above the screen beneath it. (Matches the game's own
modality order `… Event > Town > Map > Rewards …`; CardCraft sits above Event because event shops
open over the map; Conflict is the MP vote tie-breaker, modal over both the event book and the
map per the game's own `characterWindow > Conflict > Event > Map` dispatch chain (it never
coexists with Combat — different scenes); DeathScreen and CombatSelector are the in-combat modals over Combat; CharWindow
is the in-run character sheet, a modal over Combat/Event/Town/Map/Rewards/Loot — all of which
also gate themselves inert on `characterWindow.IsActive()`; PerkTree sits directly above it
because the sheet's Perks tab opens the tree mid-run (this also covers the town-tier-0 tree),
while still outranking CharPopup/HeroSelection per the game's
`PerkTree > … > HeroSelection/CharPopup` order; Alert outranks everything — the game's confirm
dialogs are modal over every screen.) All three hero-selection contexts go inert while the
madness or sandbox panel is open (deferred interiors — the game's own controller nav drives
those).

**Alerts**: one global top-priority `AlertInputContext` + `AlertDialogueManager`
(`Patches/AlertAccessibilityPatch.cs`) covers every `AlertManager` dialog — confirm single/double,
text input, copy/paste, and the buttonless MP "waiting" variant. The alert is a walkable dialogue:
body lines first, then one row per **visible** option button (rebuilt live each read); ↑/↓ move,
Enter activates only on an option row (a text-row Enter just hints — no accidental accepts on
destructive confirms), Escape follows `CloseAlert` semantics (`SetConfirmAnswer(false)` /
dismiss / "no options, waiting"). Open/answer/close are announced from postfixes on the five
`Alert*` methods, `SetConfirmAnswer` (labels captured in a prefix — the postfix runs after
`HideAlert` wipes them) and `HideAlert`; `popupT` is checked so the MP `ShowPlayers` panel is not
treated as an alert. "Press" = `SetConfirmAnswer(bool)`; never call `OnButtonClick` (null-deref
when no delegate). No per-screen alert handling remains (`AlertHelper.cs` is gone); the letter
pollers fall silent automatically because they gate on their own contexts, and Alt+R under an
alert comes from `AlertHotkeyPoller`.

### Screens supported

| Screen | Context | Keys |
|--------|---------|------|
| Main menu / game-mode / save-slot | `MainMenuInputContext` | arrows (game nav, announced), Enter; on the game-mode screen the context takes over movement: ↑/↓ (←/→ synonyms) walk a linear list (Main Menu button, the four modes, PDX buttons) instead of the game's spatial row. Mode announce = name + (if chained) the `obeliskNeedCharacter` advisory + the mode's description blurb; the chains "lock" is advisory only — the game never enforces it, so Enter opens the save screen either way, matching vanilla mouse behaviour |
| Settings | `SettingsInputContext` + poller | arrows, Enter, Tab (4 tabs: Graphics/Audio/Gameplay/Accessibility — the last is a mod-owned virtual tab of `AccessibilityOptions` combat-narration toggles + a debug-logging toggle), Alt+T option tooltip, Escape (cancels open dropdown) |
| Tutorial popup | `TutorialInputContext` | Up/Down walk lines, Enter activates (modal focus trap) |
| Map — nodes | `MapInputContext` | ←/→ reachable nodes; Ctrl+↑/↓ descend/ascend look-ahead, Ctrl+←/→ siblings; Enter travels (in MP: casts the travel vote, with locked-vote / follow-the-leader refusals explained; partners' votes + tally + unanimous departure announced from a `NET_SharePlayerSelectedNode` postfix — re-parsed with `JsonHelper`, never the possibly-stale dict; follow state appended to Alt+I); Alt+T node detail |
| Map — party strip | `MapInputContext` | Tab toggles region; ↑/↓ read heroes; 1–4 jump to slot; Enter opens the character sheet (via `OverCharacter.Clicked()`) |
| Map — global | `MapInputContext` + poller | Alt+G gold; Alt+I (and auto on open) position + trackers + tip |
| Corruption prompt | `CorruptionInputContext` | ←/→ choose reward + accept; ↑/↓ toggle accept; Enter confirm |
| MP vote conflict (card-flip tie-breaker over map/event) | `ConflictInputContext` + poller (state in `ConflictScreenManager`, `Patches/ConflictAccessibilityPatch.cs`; instance at `MapManager.Instance.Conflict`, MP-only) | Narrated play-by-play from postfixes on the game's roll methods (deterministic on every client): open reason, chooser (`EnableButtonsForPlayerChoosing`), flips with card + cost (`DoCard`; cost via Traverse `charRollResult`), tie re-flips, standings (`RollResult` — fires at coroutine *creation*, values already final; eliminations/winner deliberately hang off `TurnOffCharacter`/`FinalResolution` instead), winner. ↑/↓ or 1–3 review rules, Enter chooses via public `MapManager.ConflictSelection` (guarded on `botonConflict[n].IsEnabled()` — buttons enabled only for the choosing hero's owner); Escape left to the game (no cancel exists); Alt+R via poller, whose Tick (outside the gate) detects teardown under a modal |
| Map event (story dialog) | `EventInputContext` + poller | ↑/↓ walk title/text/choices (choices at bottom); Enter select/Continue; Alt+T hover info (probability, blocked reason, card previews, roll explainer); Alt+R repeat; rolls narrated play-by-play |
| Alert dialogs (global) | `AlertInputContext` (top priority; `AlertDialogueManager`, `Patches/AlertAccessibilityPatch.cs`) | Covers all `AlertManager` shapes (confirm single/double, input, copy/paste, buttonless MP "waiting"). ↑/↓ walk body lines then visible option-button rows; Enter activates option rows only (text rows hint); Escape = cancel/dismiss ("No options, waiting" when buttonless); input alerts use an explicit edit mode — the game's auto-focus of the TMP field is undone on open (poller Tick), Enter on the field row starts editing (`ActivateInputField`), Enter/Escape end it keeping the typed text (an `_editCache` frame-mirror restores TMP's Escape revert; Tick also catches TMP self-deactivating), accept row submits; answers by mouse/gamepad announced too; Alt+R + the edit-mode Tick via `AlertHotkeyPoller` |
| Town hub | `TownInputContext` + poller | ↑/↓ hub items (5 buildings, upgrades, Ready, treasures); Enter opens/claims (confirm alerts via the global alert dialogue); Tab party strip (↑/↓ heroes, 1–4 slots); Alt+T/G/I/R; arrival overview |
| Town services (Altar/Church/Forge/Divination/Armory) | `CardCraftInputContext` + poller | One context for `CardCraftManager` craftType 0–4 (also covers map-event shops). ↑/↓ items with page auto-advance; ←/→ pages (or A/B variant in Altar preview); Enter single-press buy; Tab regions (Forge deck ref; Armory equipped+controls); 1–4 hero; Alt+F filters (Forge); Alt+T full card/item detail; purchases announced via `Hero.*` postfixes |
| Town upgrades window | `TownUpgradeInputContext` | ←/→ building column, ↑/↓ its 6-upgrade chain; states with locked reasons; Enter buys via game confirm alert; Tab grid/sell/exit; sell-supply ↑/↓ quantity sub-mode |
| Rewards screen (post-combat / event / divination) | `RewardsInputContext` + poller | Table: ↑/↓ hero rows (Restart pseudo-row last), ←/→ cards→dust→Deck; Enter takes (Singularity overwrite + MP restart confirms via the global alert dialogue); Ctrl+↑/↓ card detail drill, Escape exits; Alt+T full detail, Alt+I overview, Alt+R repeat; picks and auto-close announced via `NET_*`/`CheckAllAssigned` postfixes; poller waits out the ~2s row animation before announcing arrival |
| Loot screen (boss/chest item picks, Obelisk-challenge chests) | `LootInputContext` + poller | Arrows walk the loot row (items → gold pile → Restart); Enter takes for the hero whose turn it is (MP restart confirm via the global alert dialogue); Tab party review (equipped items per hero; Enter reorders picker in SP; 1–4 jump); Ctrl+↑/↓ item detail drill, Escape exits; Alt+T/I/G/R; item announces carry an equipped-slot comparison; picks announced via `Looted`/`LootGold` prefix+postfix pairs; poller's tick detects arrival (active-slot poll), turn changes, and finish |
| In-combat card-selection windows (discard-from-hand / look-at-deck / discover / pile viewers) | `CombatSelectorInputContext` (above Combat; state in `CombatSelectorManager`, `Patches/CombatSelectorAccessibilityPatch.cs`) | Covers `UIDiscardSelector` + `UIDeckCards` types 0–3 (`UIAddcardSelector` is dead code — discover runs through `UIDeckCards` type 3). ←/→ review cards ("selected" flagged); Enter toggles via `SelectCardToDiscard/Addcard` (mandatory single picks auto-confirm; toggles announced from prefix/postfix snapshot pairs, so digits/mouse/MP echoes speak too); Space confirms via `AssignDiscardAction`/`AssignLookDiscardAction` (or speaks "select N more"); game's Enter (window confirm) + Space (end turn) suppressed in the `DoKeyBinding` prefix while active; Ctrl+↑/↓ drill, Alt+T/R via `CombatHotkeyPoller`, whose tick also announces the async card spawn-in. Deck-effect cast fix: with a held card, Enter on a character casts via `ControllerExecute()` directly (the `DeckInHero` overlay colliders eclipse the hero and eat the synthetic click); pickup announced |
| In-run character sheet (`CharacterWindowUI` — map/town/combat/rewards/loot) | `CharWindowInputContext` + poller (state in `CharWindowScreenManager`, `Patches/CharWindowAccessibilityPatch.cs`; above Combat, below PerkTree — the screens beneath all gate on `characterWindow.IsActive()`) | Open: party-strip Enter on map/town (`OverCharacter.Clicked()`), Alt+C in combat (focused character via `CombatNavigator.FocusedTransform` → Traverse `_hero`/`_npc`, else active hero — enemies get their stats/cards-cast view), or any mouse path — all announced from the `Show` postfix (`_element` "" resolves via the private `activeTab`; "perks" is skipped — the tree owns it; FinishRun scene skipped entirely). Tab cycles tabs (Deck/Level/Items/Stats/Perks out of combat; Draw/Discard/Vanish/Items/Stats/Perks in combat; Cards cast/Stats for NPCs; Perks is a **virtual** tab — Enter opens the tree via the scene wrapper); tab switches go through the scene's `ShowCharacterWindow` wrapper. ↑/↓ rows **rebuilt from data, never the spawned CardItems** (combat piles spawn 1/frame in a coroutine): deck = `hero.Cards` split injuries/boons + sorted like `SetDecks`, draw pile sorted (order stays hidden), discard newest-first, NPC casted via `GetNPCCardsCastedList` reversed; header rows carry count + DeckEnergy-style average cost (histogram on Alt+I). Level tab: header + 5 level rows from `SubClassData` traits + `hero.Traits` (never the `TraitLevel` components), ←/→ compare choice A/B, Enter commits via `hero.LevelUp(id)` / `HeroLevelUpMP` (map/town only — `TraitLevel.OnMouseUp` can't be called, its `ClickedThisTransform` guard needs the real cursor); refusals diagnosed (XP short / wrong scene / MP non-owner / already chosen); outcome announced **queued** from a level-guarded `Hero.LevelUp(string)` postfix (covers mouse + MP echoes — the RPC runs on all clients). Items: 5 slot rows. Stats: rows from `Character` getters + `StatsWindowUI` public TMPs; damage-type rows' Alt+T reads the game's prebuilt breakdown pops (private arrays → Traverse); buff/immunity/charge-modifier rows mirror `DoStats`' merges. 1–4 switch hero (`OverCharacter.Clicked()`); Ctrl+↑/↓ card drill (plain arrows exit it); Alt+T/I/R via poller; Escape closes (`Hide()`; guarded postfix announces — also fires when the hero dies under us). Mid-run upgraded-cards popup (`ShowUpgradedCards` postfix): ←/→ review, Enter/Escape close. In combat the game blocks its own Enter/Space/digits itself (`CharacterWindowBlocksCombatInput`); Enter/Ctrl/Alt suppressed in the router prefixes |
| In-combat death popup (hero dies, party survives) | `DeathScreenInputContext` (above CombatSelector; state in `DeathScreenPopupManager`, `Patches/DeathScreenAccessibilityPatch.cs`) | `UICombatDeath` via `MatchManager.ShowDeathScreen` postfix. Open announce is **queued** (never interrupts turn narration); ↑/↓ walk title / body lines / Death's Door note / Continue row (Continue tracked live — hidden in MP for non-owners, "Waiting for {owner}"; host auto-closes after 30s); Enter = `TurnOffFromButton()` from any row; game's Enter suppressed in the `DoKeyBinding` prefix; Alt+T Death's Door card detail + Alt+R via `CombatHotkeyPoller`; close announced from a guarded `TurnOff` postfix |
| Hero selection / run setup (scene "HeroSelection") | `HeroSelectionInputContext` + poller (state in `HeroSelectionScreenManager`, `Patches/HeroSelectionAccessibilityPatch.cs`; shared text builders in `Patches/HeroSpeech.cs`) | Tab cycles Roster → Party → Run options. Roster: ↑/↓ heroes with 16-per-page auto-advance, ←/→ filter tabs (game order: All/Warriors/Scouts/Mages/Healers/Multiclass/Locked), Enter assigns to first empty own slot (via the dice's `PickHero(true)`+`PickStop(slot)` sequence — never re-assign an already-picked hero, the random path corrupts box state), 1–4 assign to a specific slot (displacement announced), Alt+T hero sheet (stats computed from `SubClassData`+perk bonuses — `subClassData`/`spriteSR` are **internal**, use `HeroSpeech.ScdOf`/Traverse), Alt+C character window (`RightClick()`; direct `Init`+`Show` for locked heroes). Party: ↑/↓ active slots, Enter clears own filled slot (`ClearBox`) / rolls dice on empty (`SetRandomHero`, SP only). Run options: ↑/↓ madness/sandbox (deferred interiors — open+announce), seed (spelled letter-by-letter; Enter → game's input alert), weekly modifiers, MP Ready/Follow, Begin (disabled reason spoken; enable edge announced from Tick). Arrival overview from poller Tick (`charPopupGO != null` = settle signal; first-game auto-start announced instead); MP echoes queued via `NET_*` postfixes; weekly/loading modes make mutating keys explain instead |
| Character window (CharPopup, over hero selection) | `CharPopupInputContext` (state in `CharPopupScreenManager`, `Patches/CharPopupAccessibilityPatch.cs`) | Tab/Shift+Tab cycle Stats/Perks/Rank/Skins/Card Backs/Singularity (unavailable skipped: Perks+Rank in Obelisk or locked hero; Singularity only in that mode); ↑/↓ rows (Stats: description, stats, resists, per-trait rows with Alt+T detail, per-card rows via `_CardParent` CardItems with Alt+T `CardSpeech.FullDetail`, classic-variant toggle; Rank: progress, reward rows, use-supplies with exact disabled reason — NO game confirm, spends immediately; Skins/Card Backs: Enter equips via `OnMouseUp()` — internally guarded; Card Backs ←/→ categories, page auto-flip); Escape closes (Card Backs tab first hops to Stats). Open detect: `Show` postfix guarded `IsOpened()` — `CharPopupMini` silently primes the popup (`Init(showNothing:true)`+`ShowStats`+`Close`) on every hero click, so never key on `Init`/`ShowStats` without that guard; tab resync postfixes use a self-switch flag |
| Perk tree (PerkTree overlay, topmost of the family) | `PerkTreeInputContext` (state in `PerkTreeScreenManager`, `Patches/PerkTreeAccessibilityPatch.cs`) | Opens from the Perks tab or the portrait perk-badge (`BotHeroChar` → `PerkTree.Show` directly, no CharPopup beneath). Tab cycles the 4 categories + Controls region; ↑/↓ rows (threshold state first), ←/→ nodes; choose-one clusters expanded in place (child `PND.Perk` id+node passed to `SelectPerk`); Enter toggles with outcome + running points (refusals diagnosed: dependent perk / row threshold), Space = `PerksAssignConfirm`, Alt+T = full `NewPerkDescription` (public; side effect: disables node hover at 0 points — same as the game's own hover), Alt+I points summary. Controls: Confirm/Reset/Import/Export/save-slots (load+delete rows per filled slot)/Exit — all slot dialogs global-alert-owned. Escape → `Hide()` (game raises the unsaved confirm itself; dirty flag = `buttonConfirm.buttonEnabled`). Scope: everywhere the tree opens — hero selection AND mid-run from the character sheet's Perks tab (map, town incl. tier 0, combat, rewards, loot); registered above CharWindow, and its close announce says "Back to character sheet" when the sheet is beneath. Space needs no router suppression (inert outside combat) |
| Act-transition screen (scene "IntroNewGame", between acts / entering sub-dungeons / adventure-complete farewell) | `IntroInputContext` + poller (state in `IntroScreenManager`, `Patches/IntroAccessibilityPatch.cs`) | Open announce (title + full story body + Continue hint) from a `GameManager.SceneLoaded` postfix gated on the scene name — both `DoIntro` and `DoFinishGame` call it after the TMPs are set (`TextFade` only animates alpha, the text is complete immediately); the run-start cinematic path and mid-act redirect never call it, so they stay silent. ↑/↓ walk title / body lines / Continue row; Enter = `SkipIntro()` from any row (the only, harmless action — game's Enter suppressed in the `DoKeyBinding` prefix); Escape left to the game (its default on this scene is the same skip); Alt+R via `IntroHotkeyPoller`. Sub-dungeon variant (empty body) announces "Continuing automatically" — the game's `FadeOut` auto-skips after 4s. Close (any path, incl. auto-fade) detected via a `SkipIntro` postfix; no close announce — map/finish-run arrival takes over |
| End-of-run screen (scene "FinishRun") + unlocked-cards popup | `FinishRunInputContext` + poller (state in `FinishRunScreenManager`, `Patches/FinishRunAccessibilityPatch.cs`) | ↑/↓ rows read live from `FinishRunManager` TMP fields: header, six score rows, adventure-completed, final score (+madness/best/time), reward + retention + total (sprite icons → words via `CardSpeech.CleanFlat`), per-hero `FinishProgression` rows (animate upward), Main Menu button last (Enter = `ControllerMovement()` warp + `DoFirePerformed`; "Still tallying" while disabled — "Main menu available" announced on enable). Unlocked-cards popup sub-mode (`CharacterWindowUI.ShowUnlockedCards` postfix, `ShowInTome`-filtered): ←/→ cards, Alt+T detail, Enter/Escape → `characterWindow.Hide()`; arrival overview waits it out; Alt+I overview, Alt+R repeat |

### Extensibility pattern

- **Input**: add `Input/Contexts/XyzInputContext.cs : InputContextBase` and register it in
  `Plugin.Awake` at the right priority. Do **not** add new `InputController` patches — route through
  the context. For a modifier key use `InputRouter.CtrlHeld/AltHeld`; for an unbound letter, extend
  the poller.
- **Announce/state**: add `Patches/XyzAccessibilityPatch.cs` with the open/close + navigation
  announce patches (may inherit `AccessibleMenuBase` for text helpers), and a static manager if the
  screen has focus state.

### Key gotchas

**`ForceKeyboardShortcutsPatch`** is essential — without it, `InputController.DoMovement` silently
drops all keyboard arrow-key input because `GameManager.Instance.ConfigKeyboardShortcuts` defaults
to `false`. The patch postfixes `SettingsManager.LoadPrefs` to force it `true`.

**Public vs private member access** — many game members the mod uses are public (call directly).
Private members patched/read by string name must go through `Traverse.Create(...).Field<T>(...)` /
`.Method(...)`, not raw reflection. When a member won't compile though `../decompiled/` shows it,
the decompile is stale — reflect the live DLL or re-decompile (see "Game code reference").

**Harmony003 warning** — the analyzer incorrectly flags `_context` struct-field reads in
`RouterInputPatches` (DoMovement/DoKeyBinding) as modifications. These ~2 warnings are safe to ignore.

## Game code reference

Decompiled game source is at `../decompiled/`. Key files (line numbers as of the 2026-07-19 re-decompile):

| File | Relevant members |
|------|-----------------|
| `MainMenuManager.cs` | `ControllerMovement()` (l.1567), `controllerHorizontalIndex` (l.352), `controllerList` (l.350, private) |
| `InputController.cs` | `DoKeyBinding()` (l.387, private), `DoFirePerformed()` (l.667, private) |
| `BotonGeneric.cs` | `text` (l.17, public TMP_Text), `IsEnabled()` (l.334) |
| `MenuButton.cs` | `buttonText` (l.8, public TMP_Text) |
| `AlertManager.cs` | `popupT` (l.14; false for the MP `ShowPlayers` panel), `alertText` (l.20), button labels `alertTextSingleButton`/`alertTextLeftButton`/`alertTextRightButton` (l.40-44; button GO = `label.transform.parent`), `SetConfirmAnswer` (l.189, the "press" call), `CloseAlert` (l.175, no-op for buttonless alerts), `AlertInputSuccess` (l.207) |
| `UICombatDeath.cs` | `textCharDeath`/`textInstructions` (l.8-10), `button` (l.18; MP owner-only), `TurnOffFromButton()` (l.82), `TurnOff()` (l.88, also called defensively at combat start) |
| `FinishRunManager.cs` | all score/reward TMP fields public (set synchronously in `CalculateFinishRunReward`), `mainMenuButton` (l.22), `characterWindow` (l.102), `fp0-3` progression blocks, `ControllerMovement()` (l.839, warps to the lone Main Menu button) |
| `CharacterWindowUI.cs` | `ShowUnlockedCards(List<string>)` (l.151, FinishRun-only caller), `Hide()` (public; releases FinishRun's spin-wait) |

**Keeping `../decompiled/` current** — it is generated from the live `Assembly-CSharp.dll` and can
fall behind after a game patch. If a member is present in `../decompiled/` but won't compile against
the game DLL (or you otherwise suspect drift), the decompile is stale: **delete the folder and
regenerate it** from the current DLL — do not decompile into the existing folder (a re-export
overwrites matching files but leaves orphaned stale files for removed/renamed types). Regenerate
with the ILSpy CLI:

```bash
# one-time (the latest ilspycmd package is broken, so pin a working version):
dotnet tool install -g ilspycmd --version 8.2.0.7535
MANAGED="C:\Program Files (x86)\Steam\steamapps\common\Across the Obelisk\AcrossTheObelisk_Data\Managed"
ilspycmd -p -o ../decompiled_new -r "$MANAGED" "$MANAGED/Assembly-CSharp.dll"   # then delete ../decompiled and rename ../decompiled_new to it
```

`-p` produces a compilable per-type project matching the existing layout; `-r` resolves Unity deps.
To confirm one symbol's real signature without a full re-decompile, reflect the DLL instead
(`[Reflection.Assembly]::LoadFrom(...).GetType("X").GetMethods(...)`). Re-decompiling shifts the
line numbers above, so refresh this table afterward.

## csproj — DLL reference rules

**Do not** add `UnityEngine.Modules` via NuGet. It ships the old monolithic `UnityEngine.dll`, which causes CS0433 type ambiguity against the game's split module DLLs. Always reference the game's own DLLs directly via `<HintPath>` pointing to:

```
C:\Program Files (x86)\Steam\steamapps\common\Across the Obelisk\AcrossTheObelisk_Data\Managed\
```

When a new Unity type is needed, find which split-module DLL exports it and add a `<Reference>` with `<Private>false</Private>`.
