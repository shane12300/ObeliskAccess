# ObeliskAccess

A screen-reader accessibility mod for **Across the Obelisk**. Adds keyboard navigation and speech
output so the game is playable without sight.

> This is a placeholder README covering installation only. A fuller README (features, usage,
> keybindings) will come later.

## Requirements

- **Across the Obelisk** (Steam)
- **BepInEx 5** (x64) installed into the game folder
- A Windows screen reader (NVDA, JAWS, etc.). Windows SAPI is used as a fallback if none is running.

## Installation

1. **Install BepInEx 5 (x64)** into your Across the Obelisk folder if you haven't already, and run
   the game once so BepInEx generates its folders (`BepInEx/plugins/`, etc.).

2. **Copy the mod** into the plugins folder. From a release, place both DLLs together:

   ```
   Across the Obelisk/BepInEx/plugins/ObeliskAccess/
       ObeliskAccess.dll
       UnityAccessibilityLib.dll
   ```

3. **Copy the speech DLLs** into the **game root** (the folder that contains
   `AcrossTheObelisk.exe`), *not* the plugins folder:

   ```
   Across the Obelisk/
       AcrossTheObelisk.exe
       UniversalSpeech.dll
       nvdaControllerClient.dll
   ```

   These let the mod talk to your screen reader. `nvdaControllerClient.dll` is only needed for NVDA;
   without it, JAWS and the SAPI fallback still work.

4. **Launch the game.** With a screen reader running, the menus should start speaking.

## Building from source

```bash
dotnet build
```

The build copies `ObeliskAccess.dll` + `UnityAccessibilityLib.dll` into the plugins folder, and
copies the bundled `native/UniversalSpeech.dll` + `native/nvdaControllerClient.dll` into the game
root if they aren't already there. Adjust the `GameDir` path in `ObeliskAccess.csproj` if your game
is installed elsewhere.
