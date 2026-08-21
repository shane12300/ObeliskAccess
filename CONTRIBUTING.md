# Building and installing by hand

The [installer](README.md#installing) is the supported way to install ObeliskAccess. This file
covers the two paths that bypass it: building from the repo, and unpacking a release zip
manually.

## Building from source

Prerequisites: a .NET SDK and the game itself (the mod compiles against the game's own DLLs).
BepInEx is **not** a prerequisite — the deploy script installs it if it's missing.

```powershell
.\scripts\deploy.ps1
```

The script checks the game folder; installs BepInEx 5 if it isn't already there (downloading a
pinned version to a temp folder, extracting it into the game folder, then deleting the temp
folder); runs the build; copies `ObeliskAccess.dll` + `UnityAccessibilityLib.dll` into
`BepInEx\plugins\ObeliskAccess\`; and copies the bundled `native\UniversalSpeech.dll` +
`native\nvdaControllerClient.dll` into the game root — printing exactly what it copied and what
it left alone. Unlike a manual BepInEx install, you don't need to launch the game first: the
script creates the plugins folder itself.

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

See `CLAUDE.md` for the mod's architecture, the release process, and the conventions this
codebase follows.

## Installing a release zip manually

1. **Install BepInEx 5.** Download the latest BepInEx 5.4.x release for **Windows x64** — the
   standard build for Unity Mono games, named like `BepInEx_win_x64_5.4.23.5.zip`; do *not* use
   a BepInEx 6 / IL2CPP build. Extract it directly into your Across the Obelisk folder,
   typically `C:\Program Files (x86)\Steam\steamapps\common\Across the Obelisk`, so that
   `winhttp.dll` and the `BepInEx` folder sit next to `AcrossTheObelisk.exe`. Run the game once
   and quit, so BepInEx creates its folders.

2. **Copy the mod into the plugins folder.** Both DLLs together, in their own folder:

   ```
   Across the Obelisk\BepInEx\plugins\ObeliskAccess\
       ObeliskAccess.dll
       UnityAccessibilityLib.dll
   ```

3. **Copy the speech DLLs into the game root** — the folder containing `AcrossTheObelisk.exe`,
   *not* the plugins folder. Both are 64-bit DLLs shipped with the release:

   ```
   Across the Obelisk\
       AcrossTheObelisk.exe
       UniversalSpeech.dll
       nvdaControllerClient.dll
   ```

4. **Launch the game.** With a screen reader running, the main menu should speak as you press
   the arrow keys.

## Checking an install

- BepInEx's log at `Across the Obelisk\BepInEx\LogOutput.log` should contain
  `Plugin ObeliskAccess is loaded!` after a launch. If it doesn't, BepInEx itself isn't loading
  — wrong BepInEx build, or the files are in the wrong folder.
- **Loaded but silent:** `UniversalSpeech.dll` is missing from the game root, or a 32-bit or
  outdated copy is there. The log carries a UniversalSpeech warning in that case. Replace it
  with the 64-bit copy from the release. Note that neither the build nor the deploy script
  overwrites a `UniversalSpeech.dll` already in the game root — replace a stale copy by hand,
  or pass `-ForceNative`.
- The mod's settings live in `BepInEx\config\ObeliskAccess.cfg`, created on first run. You
  never need to edit it; the same options are on the in-game Accessibility settings tab.
