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
  (postfix, non-swallowing) routes Enter→`Confirm`, Tab→`Tab`, digits 1–4→`Number`; a
  `DoFirePerformed` prefix suppresses the game's bare-Ctrl "click" while the map owns input (Ctrl is
  the map's look-ahead modifier).
- `Input/Contexts/*InputContext.cs` — one per screen (thin; delegates to a `Patches/` manager).
- `Input/MapHotkeyPoller.cs`, `Input/CombatHotkeyPoller.cs`, `Input/EventHotkeyPoller.cs`,
  `Input/TownHotkeyPoller.cs`, `Input/CardCraftHotkeyPoller.cs` — `MonoBehaviour`s that poll
  letters the game leaves **unbound** (Alt+letter review keys), since the InputAction system never
  fires for them; each is gated on `InputRouter.IsActive(context)`. The combat/event/town/craft
  pollers also run their manager's per-frame tick *outside* that gate (lifecycle detection —
  town arrival, craft-screen open — must survive a modal owning input).

Registration order in `Plugin.Awake` **is** priority (highest first):
`Tutorial > Settings > Corruption > CardCraft > Combat > Event > TownUpgrade > Town > Map >
Rewards > MainMenu`. A modal thus sits above the screen beneath it. (Matches the game's own
modality order `… Event > Town > Map > Rewards …`; CardCraft sits above Event because event shops
open over the map.)

**Alerts**: `AlertConfirm`/`AlertConfirmDouble` are announced globally (patches in
`SettingsMenuAccessibilityPatch.cs`). The town-family contexts (Town/TownUpgrade/CardCraft) do
NOT suspend while an alert is up — they answer it through `Patches/AlertHelper.cs`
(Enter=`SetConfirmAnswer(true)`, Escape=`CloseAlert()`), because those screens spawn their own
confirm dialogs. Other contexts still exclude alerts in `IsActive` (known gap outside town).

### Screens supported

| Screen | Context | Keys |
|--------|---------|------|
| Main menu / game-mode / save-slot | `MainMenuInputContext` | arrows (game nav, announced), Enter |
| Settings | `SettingsInputContext` | arrows, Enter, Tab, Escape (cancels open dropdown) |
| Tutorial popup | `TutorialInputContext` | Up/Down walk lines, Enter activates (modal focus trap) |
| Map — nodes | `MapInputContext` | ←/→ reachable nodes; Ctrl+↑/↓ descend/ascend look-ahead, Ctrl+←/→ siblings; Enter travels; Alt+T node detail |
| Map — party strip | `MapInputContext` | Tab toggles region; ↑/↓ read heroes; 1–4 jump to slot; Enter (open panel) deferred |
| Map — global | `MapInputContext` + poller | Alt+G gold; Alt+I (and auto on open) position + trackers + tip |
| Corruption prompt | `CorruptionInputContext` | ←/→ choose reward + accept; ↑/↓ toggle accept; Enter confirm |
| Map event (story dialog) | `EventInputContext` + poller | ↑/↓ walk title/text/choices (choices at bottom); Enter select/Continue; Alt+T hover info (probability, blocked reason, card previews, roll explainer); Alt+R repeat; rolls narrated play-by-play |
| Town hub | `TownInputContext` + poller | ↑/↓ hub items (5 buildings, upgrades, Ready, treasures); Enter opens/claims (confirm alerts answered in place); Tab party strip (↑/↓ heroes, 1–4 slots); Alt+T/G/I/R; arrival overview |
| Town services (Altar/Church/Forge/Divination/Armory) | `CardCraftInputContext` + poller | One context for `CardCraftManager` craftType 0–4 (also covers map-event shops). ↑/↓ items with page auto-advance; ←/→ pages (or A/B variant in Altar preview); Enter single-press buy; Tab regions (Forge deck ref; Armory equipped+controls); 1–4 hero; Alt+F filters (Forge); Alt+T full card/item detail; purchases announced via `Hero.*` postfixes |
| Town upgrades window | `TownUpgradeInputContext` | ←/→ building column, ↑/↓ its 6-upgrade chain; states with locked reasons; Enter buys via game confirm alert; Tab grid/sell/exit; sell-supply ↑/↓ quantity sub-mode |
| Rewards screen (post-combat / event / divination) | `RewardsInputContext` + poller | Table: ↑/↓ hero rows (Restart pseudo-row last), ←/→ cards→dust→Deck; Enter takes (Singularity overwrite + MP restart confirms answered in place); Ctrl+↑/↓ card detail drill, Escape exits; Alt+T full detail, Alt+I overview, Alt+R repeat; picks and auto-close announced via `NET_*`/`CheckAllAssigned` postfixes; poller waits out the ~2s row animation before announcing arrival |
| Loot screen (boss/chest item picks, Obelisk-challenge chests) | `LootInputContext` + poller | Arrows walk the loot row (items → gold pile → Restart); Enter takes for the hero whose turn it is (MP restart confirm answered in place); Tab party review (equipped items per hero; Enter reorders picker in SP; 1–4 jump); Ctrl+↑/↓ item detail drill, Escape exits; Alt+T/I/G/R; item announces carry an equipped-slot comparison; picks announced via `Looted`/`LootGold` prefix+postfix pairs; poller's tick detects arrival (active-slot poll), turn changes, and finish |

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
| `BotonGeneric.cs` | `text` (l.17, public TMP_Text) |
| `MenuButton.cs` | `buttonText` (l.8, public TMP_Text) |

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
