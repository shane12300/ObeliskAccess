# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

ObeliskAccess is a BepInEx 5 mod for "Across the Obelisk" that makes the game accessible to screen-reader users via keyboard navigation and speech output.

## Build

```bash
dotnet build
```

The output DLL (`bin/Debug/net46/ObeliskAccess.dll`) must be copied to the game's BepInEx plugins folder to test. There are no automated tests.

## Architecture

### Core data flow

1. **`Plugin.cs`** — BepInEx entry point; calls `Harmony.PatchAll()` to auto-register all `[HarmonyPatch]` classes.
2. **`SpeechManager.Speak(string)`** — Single TTS integration point. Currently writes to clipboard (`GUIUtility.systemCopyBuffer`) for use with external screen readers. 
3. **`Patches/AccessibleMenuBase.cs`** — Abstract base class all menu patches inherit from. Provides shared helpers: `GetMenuItemText`, `AnnounceItem`, `InvokeItemButton`.
4. **`Patches/*AccessibilityPatch.cs`** — Harmony postfix patches. Each menu screen gets one file inheriting `AccessibleMenuBase`.

### Extensibility pattern

To add accessibility support for a new menu screen:
1. Create `Patches/<MenuName>AccessibilityPatch.cs`
2. Inherit `AccessibleMenuBase`
3. Apply `[HarmonyPatch]` to the method that fires when the controller selection changes (analogous to `MainMenuManager.ControllerMovement`)
4. Call `AnnounceItem(selectedTransform)` in the postfix

### Key gotchas

**`ForceKeyboardShortcutsPatch`** is essential — without it, `InputController.DoMovement` silently drops all keyboard arrow-key input because `GameManager.Instance.ConfigKeyboardShortcuts` defaults to `false`. The patch postfixes `SettingsManager.LoadPrefs` to force it `true`.

**Private member access** — Game members patched by string name (e.g. `"DoKeyBinding"`, `"DoFirePerformed"`, `"controllerList"`) must be accessed via `Traverse.Create(...).Field<T>(...)` or `.Method(...)`. Do not use reflection directly.

**Harmony003 warning** — The analyzer incorrectly flags `_context.control` struct-field reads as modifications. This warning is safe to ignore.

## Game code reference

Decompiled game source is at `../decompiled/`. Key files:

| File | Relevant members |
|------|-----------------|
| `MainMenuManager.cs` | `ControllerMovement()` (l.1563), `controllerHorizontalIndex` (l.344), `controllerList` (l.342, private) |
| `InputController.cs` | `DoKeyBinding()` (l.310, private), `DoFirePerformed()` (l.733, private) |
| `BotonGeneric.cs` | `text` (l.18, public TMP_Text) |
| `MenuButton.cs` | `buttonText` (l.8, public TMP_Text) |

## csproj — DLL reference rules

**Do not** add `UnityEngine.Modules` via NuGet. It ships the old monolithic `UnityEngine.dll`, which causes CS0433 type ambiguity against the game's split module DLLs. Always reference the game's own DLLs directly via `<HintPath>` pointing to:

```
C:\Program Files (x86)\Steam\steamapps\common\Across the Obelisk\AcrossTheObelisk_Data\Managed\
```

When a new Unity type is needed, find which split-module DLL exports it and add a `<Reference>` with `<Private>false</Private>`.
