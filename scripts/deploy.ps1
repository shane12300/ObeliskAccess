# deploy.ps1 - build ObeliskAccess from source and install it into the game folder,
# installing BepInEx 5 first if it is not already there.
#
# This is the from-source path, for people working in the git repo. Everyone else uses
# ObeliskAccessInstaller.exe from a release. Between them they do the same things; this one
# builds from your working tree instead of downloading a release.
#
# What it does:
#   1. Resolves and validates the game folder.
#   2. Installs BepInEx 5 (pinned version below) if absent - downloads to a temp folder,
#      extracts into the game folder, deletes the temp folder.
#   3. Builds the mod.
#   4. Copies into <GameDir>\BepInEx\plugins\ObeliskAccess\   ObeliskAccess.dll, UnityAccessibilityLib.dll
#      and into <GameDir>\                                    UniversalSpeech.dll, nvdaControllerClient.dll
#
# The two native speech DLLs are NEVER overwritten unless you pass -ForceNative: a user may have
# put their own (newer, or differently built) copy there deliberately.
#
# BEPINEX PIN: keep $BepInExUrl / $BepInExVersion below in step with
# installer\src\core\paths.rs (BEPINEX_URL / BEPINEX_VERSION). Both must install the same build,
# and it must stay BepInEx 5 - a BepInEx 6 / IL2CPP build does not load this mod.
#
# Keep this file pure ASCII: PowerShell 5.1 reads a BOM-less .ps1 as ANSI, and a stray non-ASCII
# character turns into mojibake that can break parsing.
#
# Usage:
#   .\scripts\deploy.ps1
#   .\scripts\deploy.ps1 -GameDir "D:\Games\Across the Obelisk"
#   .\scripts\deploy.ps1 -Configuration Release -ForceNative
#   .\scripts\deploy.ps1 -SkipBepInEx
#   .\scripts\deploy.ps1 -WhatIf

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # The game's own folder - the one containing AcrossTheObelisk.exe.
    [string] $GameDir,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    # Overwrite UniversalSpeech.dll / nvdaControllerClient.dll in the game root even if present.
    [switch] $ForceNative,

    # Skip the build and just copy what is already in bin\.
    [switch] $NoBuild,

    # Never touch BepInEx: fail instead of installing it when it is missing.
    [switch] $SkipBepInEx
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot

# Pinned BepInEx 5 build - see the BEPINEX PIN note in the header.
$BepInExVersion = '5.4.23.5'
$BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"

function Fail($msg) {
    Write-Host "ERROR: $msg" -ForegroundColor Red
    exit 1
}

function Step($msg) {
    Write-Host $msg -ForegroundColor Cyan
}

# --- Resolve the game folder --------------------------------------------------

if (-not $GameDir) {
    # Same default as the csproj, so a plain "dotnet build" and a plain deploy agree.
    $GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Across the Obelisk'
}

if (-not (Test-Path -LiteralPath $GameDir)) {
    Fail "Game folder not found: $GameDir`n       Pass the right one with -GameDir '<path>'."
}

$gameExe = Join-Path $GameDir 'AcrossTheObelisk.exe'
if (-not (Test-Path -LiteralPath $gameExe)) {
    Fail "That folder has no AcrossTheObelisk.exe: $GameDir`n       -GameDir must be the game's own folder, not the Steam library root."
}

Write-Host "Game folder: $GameDir"

# --- BepInEx ------------------------------------------------------------------

# Same test the Rust installer uses (core/detect.rs::is_bepinex_installed): the loader shim next
# to the exe, plus the core folder. Checking winhttp.dll alone would pass on a half-extracted
# install; checking the BepInEx folder alone would pass on one the game has never run.
function Test-BepInEx {
    param([string] $Root)
    (Test-Path -LiteralPath (Join-Path $Root 'winhttp.dll')) -and
    (Test-Path -LiteralPath (Join-Path $Root 'BepInEx\core') -PathType Container)
}

if (Test-BepInEx -Root $GameDir) {
    Write-Host "BepInEx: already installed."
} elseif ($SkipBepInEx) {
    Fail "BepInEx 5 is not installed in $GameDir and -SkipBepInEx was passed."
} else {
    Step "BepInEx 5 not found - installing $BepInExVersion..."

    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ObeliskAccess-bepinex-" + [guid]::NewGuid().ToString('N'))
    $zipPath = Join-Path $tempDir "BepInEx_win_x64_$BepInExVersion.zip"

    if ($PSCmdlet.ShouldProcess($GameDir, "Install BepInEx $BepInExVersion")) {
        try {
            New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

            # PowerShell 5.1 does not negotiate TLS 1.2 by default on older Windows builds, and
            # github.com refuses anything less - without this the download fails with an
            # unhelpful "connection closed" error.
            try {
                [Net.ServicePointManager]::SecurityProtocol =
                    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            } catch { }

            Write-Host "  downloading $BepInExUrl"
            # Invoke-WebRequest's progress bar makes large downloads crawl in PS 5.1, and it is
            # noise for a screen reader; suppress it just for this call.
            $oldProgress = $ProgressPreference
            $ProgressPreference = 'SilentlyContinue'
            try {
                Invoke-WebRequest -Uri $BepInExUrl -OutFile $zipPath -UseBasicParsing
            } finally {
                $ProgressPreference = $oldProgress
            }

            if (-not (Test-Path -LiteralPath $zipPath)) {
                Fail "BepInEx download produced no file."
            }

            # Sanity-check the archive before unpacking it into the game folder: a redirect to an
            # HTML error page would otherwise be "extracted" as junk next to the exe.
            $extractDir = Join-Path $tempDir 'extracted'
            New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
            try {
                Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force
            } catch {
                Fail "Downloaded BepInEx archive could not be extracted: $($_.Exception.Message)"
            }

            if (-not (Test-Path -LiteralPath (Join-Path $extractDir 'winhttp.dll'))) {
                Fail "The downloaded archive does not look like a BepInEx win_x64 build (no winhttp.dll)."
            }

            Write-Host "  extracting into the game folder"
            Copy-Item -Path (Join-Path $extractDir '*') -Destination $GameDir -Recurse -Force

            # BepInEx creates these on its first run, but the plugin copy below needs the folder
            # now - otherwise you would have to launch the game once before deploying.
            New-Item -ItemType Directory -Force -Path (Join-Path $GameDir 'BepInEx\plugins') | Out-Null

            if (-not (Test-BepInEx -Root $GameDir)) {
                Fail "BepInEx install finished but the game folder still fails the check."
            }
            Write-Host "  BepInEx $BepInExVersion installed."
        } finally {
            # Always clear the temp folder, including on failure.
            if (Test-Path -LiteralPath $tempDir) {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# --- Build --------------------------------------------------------------------

if ($NoBuild) {
    Step "Skipping build (-NoBuild)."
} else {
    Step "Building ObeliskAccess ($Configuration)..."
    # GameDir is passed through so the compile resolves the game's own DLLs, and so the csproj's
    # own copy targets land in the same place this script is about to verify.
    & dotnet build -c $Configuration -p:GameDir=$GameDir
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet build failed. If the errors are missing references, -GameDir is probably wrong."
    }
}

$outDir = Join-Path $RepoRoot "bin\$Configuration\net46"
$modDll = Join-Path $outDir 'ObeliskAccess.dll'
$speechDll = Join-Path $outDir 'UnityAccessibilityLib.dll'

foreach ($f in @($modDll, $speechDll)) {
    if (-not (Test-Path -LiteralPath $f)) {
        Fail "Build output missing: $f`n       Run without -NoBuild, or check the build succeeded."
    }
}

# --- Copy the plugin DLLs -----------------------------------------------------

$pluginDir = Join-Path $GameDir 'BepInEx\plugins\ObeliskAccess'
Step "Installing plugin to: $pluginDir"

if (-not (Test-Path -LiteralPath $pluginDir)) {
    if ($PSCmdlet.ShouldProcess($pluginDir, 'Create plugin folder')) {
        New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    }
}

foreach ($src in @($modDll, $speechDll)) {
    $name = Split-Path -Leaf $src
    # "copied" is printed inside the guard so -WhatIf reports only what it would do.
    if ($PSCmdlet.ShouldProcess((Join-Path $pluginDir $name), 'Copy')) {
        Copy-Item -LiteralPath $src -Destination $pluginDir -Force
        Write-Host "  copied  $name"
    }
}

# --- Copy the native speech DLLs (only when absent) ---------------------------

$nativeDir = Join-Path $RepoRoot 'native'
$nativeNames = @('UniversalSpeech.dll', 'nvdaControllerClient.dll')

Step "Checking native speech DLLs in the game root..."
foreach ($name in $nativeNames) {
    $src = Join-Path $nativeDir $name
    $dst = Join-Path $GameDir $name

    if (-not (Test-Path -LiteralPath $src)) {
        Write-Host "  MISSING in repo: native\$name - speech will be a silent no-op" -ForegroundColor Yellow
        continue
    }

    if ((Test-Path -LiteralPath $dst) -and -not $ForceNative) {
        Write-Host "  kept    $name (already present; pass -ForceNative to overwrite)"
        continue
    }

    if ($PSCmdlet.ShouldProcess($dst, 'Copy')) {
        Copy-Item -LiteralPath $src -Destination $dst -Force
        Write-Host "  copied  $name"
    }
}

# --- Report -------------------------------------------------------------------

$installedDll = Join-Path $pluginDir 'ObeliskAccess.dll'
$version = ''
if (Test-Path -LiteralPath $installedDll) {
    $version = (Get-Item -LiteralPath $installedDll).VersionInfo.FileVersion
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
if ($version) { Write-Host "Installed ObeliskAccess $version ($Configuration)." }
Write-Host "Start the game with a screen reader running; the main menu should speak."
Write-Host "If nothing speaks, check $GameDir\BepInEx\LogOutput.log for 'Plugin ObeliskAccess is loaded!'."
