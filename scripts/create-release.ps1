<#
.SYNOPSIS
    Package ObeliskAccess and publish a GitHub release.

.DESCRIPTION
    Reads the top section of changelog.md as the single source of truth for the
    release version, title, and notes. Builds the mod with that version stamped
    into the DLL, packages the release zip per the layout contract the installer
    expects, builds the Rust installer, shows the full release form, and - after
    an explicit confirmation - creates the GitHub release as a DRAFT with both
    assets. Review it on GitHub and publish from there; the git tag is only
    created when the draft is published.

    NOTE: this file must stay pure ASCII. PowerShell 5.1 reads BOM-less .ps1
    files in the ANSI codepage, and a literal em dash mojibakes into a smart
    quote that breaks parsing. The em dash is built from [char]0x2014 instead.

    Zip layout contract (consumed by installer/src/core/install.rs):
        plugins/ObeliskAccess/ObeliskAccess.dll
        plugins/ObeliskAccess/UnityAccessibilityLib.dll
        gameroot/UniversalSpeech.dll
        gameroot/nvdaControllerClient.dll
        README.md
        changelog.md

.PARAMETER DryRun
    Do everything except create the GitHub release (no tag is pushed).

.PARAMETER AllowDirty
    Skip the clean-working-tree check.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Fail($msg) {
    Write-Host "ERROR: $msg" -ForegroundColor Red
    exit 1
}

# Run a native command with stderr tolerated. PS 5.1 + ErrorActionPreference Stop
# turns any native stderr line (cargo/dotnet progress) into a terminating error;
# failures are detected via $LASTEXITCODE instead.
function Invoke-Native([scriptblock]$Command) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command 2>&1 | ForEach-Object { "$_" }
    } finally {
        $ErrorActionPreference = $old
    }
}

# --- Preflight: tools ---------------------------------------------------------

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail "GitHub CLI (gh) not found on PATH." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail "dotnet not found on PATH." }
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    $cargoBin = Join-Path $env:USERPROFILE ".cargo\bin"
    if (Test-Path (Join-Path $cargoBin "cargo.exe")) {
        $env:Path = "$cargoBin;$env:Path"
    } else {
        Fail "cargo not found. Install Rust from https://rustup.rs/."
    }
}

# wxdragon's build needs libclang (bindgen) and ninja (cmake generator).
if (-not $env:LIBCLANG_PATH) {
    $llvm = "C:\Program Files\LLVM\bin"
    if (Test-Path (Join-Path $llvm "libclang.dll")) { $env:LIBCLANG_PATH = $llvm }
}
if (-not (Get-Command ninja -ErrorAction SilentlyContinue)) {
    $vsNinja = "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja"
    if (Test-Path (Join-Path $vsNinja "ninja.exe")) { $env:Path = "$vsNinja;$env:Path" }
}

Invoke-Native { gh auth status } | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "gh is not authenticated. Run: gh auth login" }

# --- Preflight: git state -----------------------------------------------------

Push-Location $RepoRoot
try {
    if (-not $AllowDirty) {
        $dirty = git status --porcelain
        if ($dirty) {
            Write-Host "Working tree is not clean:" -ForegroundColor Yellow
            $dirty | ForEach-Object { Write-Host "  $_" }
            Fail "Commit or stash first (or pass -AllowDirty)."
        }
    }

    # HEAD must be pushed. "gh release create --draft" is called without --target, so on publish
    # GitHub creates the tag at the REMOTE default branch's HEAD - with unpushed commits that is
    # an older tree than the DLL we are about to build, and the release binaries would not match
    # the tagged source.
    Invoke-Native { git fetch origin } | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "Could not reach origin to check the branch state." }
    $localHead = (Invoke-Native { git rev-parse HEAD }).Trim()
    $remoteHead = (Invoke-Native { git rev-parse origin/master }).Trim()
    if ($LASTEXITCODE -ne 0) { Fail "Could not resolve origin/master." }
    if ($localHead -ne $remoteHead) {
        Fail "HEAD is not pushed to origin/master (local $($localHead.Substring(0,8)), origin $($remoteHead.Substring(0,8))). Push first."
    }

    # --- Parse changelog ------------------------------------------------------

    $changelogPath = Join-Path $RepoRoot "changelog.md"
    if (-not (Test-Path $changelogPath)) { Fail "changelog.md not found." }
    # Explicit UTF-8: PS 5.1 misreads the em dash in the heading otherwise.
    $changelog = [System.IO.File]::ReadAllText($changelogPath, [System.Text.Encoding]::UTF8)

    $emDash = [char]0x2014
    $headingRe = [regex]('(?m)^## Version\s+(\d+(?:\.\d+){0,3})\s*(?:[' + $emDash + '\-:]+\s*)?(.*)$')
    $m = $headingRe.Match($changelog)
    if (-not $m.Success) { Fail "No '## Version X.Y - Title' heading found in changelog.md." }

    $rawVersion = $m.Groups[1].Value
    $title = $m.Groups[2].Value.Trim()

    # Normalize to three components: 1.0 -> 1.0.0
    $parts = $rawVersion.Split('.')
    while ($parts.Count -lt 3) { $parts += '0' }
    $version = ($parts | Select-Object -First 3) -join '.'
    $tag = "v$version"

    # Notes = everything from after the heading line to the next '## ' or EOF.
    $bodyStart = $m.Index + $m.Length
    $rest = $changelog.Substring($bodyStart)
    $next = [regex]::Match($rest, '(?m)^## ')
    if ($next.Success) { $notes = $rest.Substring(0, $next.Index) } else { $notes = $rest }
    $notes = $notes.Trim()
    if (-not $notes) { Fail "The changelog section for version $rawVersion is empty." }

    # --- Preflight: tag/release must not exist --------------------------------

    $localTag = git tag -l $tag
    if ($localTag) { Fail "Tag $tag already exists locally." }
    $remoteTag = Invoke-Native { git ls-remote --tags origin "refs/tags/$tag" }
    if ($LASTEXITCODE -ne 0) { Fail "Could not reach origin to check tags." }
    if ($remoteTag) { Fail "Tag $tag already exists on origin." }
    # A DRAFT release creates no git tag, so neither check above can see one. Without this, a
    # re-run after an abandoned or half-failed draft silently produces a SECOND draft with the
    # same tag and identical asset names, and publishing the wrong one ships stale binaries.
    Invoke-Native { gh release view $tag } | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Fail "A release or draft for $tag already exists on GitHub. Delete it first: gh release delete $tag"
    }

    # Informational: csproj <Version> is overridden by -p:Version, but drift is worth knowing.
    $csproj = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "ObeliskAccess.csproj"), [System.Text.Encoding]::UTF8)
    $csprojVer = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
    if ($csprojVer -and $csprojVer -ne $version) {
        Write-Host "Note: csproj <Version> is $csprojVer; building with $version from the changelog." -ForegroundColor Yellow
    }

    # --- Build the mod --------------------------------------------------------

    Write-Host "Building ObeliskAccess $version (Release)..." -ForegroundColor Cyan
    # Point GameDir at a throwaway staging dir: the csproj's CopyToPlugins / CopyNativeSpeech
    # targets otherwise fire against the hardcoded Program Files install, which litters (or, without
    # admin, fails) on any machine where the game lives elsewhere.
    $buildStage = Join-Path $env:TEMP "ObeliskAccess-build-$tag"
    if (Test-Path $buildStage) { Remove-Item -Recurse -Force $buildStage }
    New-Item -ItemType Directory -Force $buildStage | Out-Null
    Invoke-Native { dotnet build -c Release -p:Version=$version -p:GameDir=$buildStage }
    if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed." }

    $outDir = Join-Path $RepoRoot "bin\Release\net46"
    $modDll = Join-Path $outDir "ObeliskAccess.dll"
    $speechDll = Join-Path $outDir "UnityAccessibilityLib.dll"
    foreach ($f in @($modDll, $speechDll)) {
        if (-not (Test-Path $f)) { Fail "Expected build output missing: $f" }
    }
    $nativeSpeech = Join-Path $RepoRoot "native\UniversalSpeech.dll"
    $nativeNvda = Join-Path $RepoRoot "native\nvdaControllerClient.dll"
    foreach ($f in @($nativeSpeech, $nativeNvda)) {
        if (-not (Test-Path $f)) { Fail "Native speech DLL missing: $f" }
    }

    # --- Build the installer --------------------------------------------------

    Write-Host "Building the installer (cargo build --release)..." -ForegroundColor Cyan
    Push-Location (Join-Path $RepoRoot "installer")
    try {
        Invoke-Native { cargo build --release }
        if ($LASTEXITCODE -ne 0) { Fail "cargo build failed." }
    } finally {
        Pop-Location
    }
    $installerExe = Join-Path $RepoRoot "installer\target\release\ObeliskAccessInstaller.exe"
    if (-not (Test-Path $installerExe)) { Fail "Installer build output missing: $installerExe" }
    # Console-subsystem twin: shells do not wait for the GUI image, so its --cli mode is unusable
    # from an interactive prompt. Both are shipped; the README points terminal users at this one.
    $installerCli = Join-Path $RepoRoot "installer\target\release\ObeliskAccessInstaller-cli.exe"
    if (-not (Test-Path $installerCli)) { Fail "CLI installer build output missing: $installerCli" }

    # --- Package the zip per the layout contract ------------------------------

    $stage = Join-Path $env:TEMP "ObeliskAccess-release-$tag"
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    $pluginStage = Join-Path $stage "plugins\ObeliskAccess"
    $rootStage = Join-Path $stage "gameroot"
    New-Item -ItemType Directory -Force $pluginStage | Out-Null
    New-Item -ItemType Directory -Force $rootStage | Out-Null

    Copy-Item $modDll $pluginStage
    Copy-Item $speechDll $pluginStage
    Copy-Item $nativeSpeech $rootStage
    Copy-Item $nativeNvda $rootStage
    Copy-Item (Join-Path $RepoRoot "README.md") $stage
    Copy-Item $changelogPath $stage

    $zipName = "ObeliskAccess-$tag.zip"
    $zipPath = Join-Path $env:TEMP $zipName
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath

    $notesFile = Join-Path $env:TEMP "ObeliskAccess-release-notes-$tag.md"
    [System.IO.File]::WriteAllText($notesFile, $notes, (New-Object System.Text.UTF8Encoding $false))

    # --- Show the release form and confirm ------------------------------------

    $releaseTitle = "ObeliskAccess $version $emDash $title"
    $zipSize = "{0:N1} MB" -f ((Get-Item $zipPath).Length / 1MB)
    $exeSize = "{0:N1} MB" -f ((Get-Item $installerExe).Length / 1MB)
    $cliSize = "{0:N1} MB" -f ((Get-Item $installerCli).Length / 1MB)

    Write-Host ""
    Write-Host "================ RELEASE FORM ================" -ForegroundColor Green
    Write-Host "Tag:       $tag"
    Write-Host "Title:     $releaseTitle"
    Write-Host "Assets:"
    Write-Host "  $zipName ($zipSize)"
    Write-Host "  ObeliskAccessInstaller.exe ($exeSize)"
    Write-Host "  ObeliskAccessInstaller-cli.exe ($cliSize)"
    Write-Host ""
    Write-Host "Release notes:" -ForegroundColor Green
    Write-Host "----------------------------------------------"
    Write-Host $notes
    Write-Host "=============================================="
    Write-Host ""

    if ($DryRun) {
        Write-Host "Dry run: stopping before release creation. Staged assets:" -ForegroundColor Yellow
        Write-Host "  $zipPath"
        Write-Host "  $installerExe"
        Write-Host "  $installerCli"
        Write-Host "(The mod and installer WERE built; only the GitHub release was skipped.)"
        exit 0
    }

    $answer = Read-Host "Create this release as a draft on GitHub? (y/N)"
    if ($answer -ne 'y' -and $answer -ne 'yes') {
        Write-Host "Aborted. Nothing was pushed or published."
        exit 0
    }

    # --- Create draft ---------------------------------------------------------

    Write-Host "Creating draft release $tag..." -ForegroundColor Cyan
    Invoke-Native { gh release create $tag --draft --title $releaseTitle --notes-file $notesFile $zipPath $installerExe $installerCli }
    if ($LASTEXITCODE -ne 0) { Fail "gh release create failed." }

    Write-Host "Draft release $tag created (URL above)." -ForegroundColor Green
    Write-Host "Review it on GitHub and press Publish there; the $tag git tag is created at that point."
} finally {
    # Temp staging: the zip and notes file are already uploaded by this point, and a leftover
    # stage dir would be silently reused by the next run. Skipped on -DryRun, whose whole point is
    # to leave the built artifacts on disk for inspection (PowerShell runs finally on "exit" too).
    if (-not $DryRun) {
        foreach ($tmp in @($stage, $buildStage, $zipPath, $notesFile)) {
            if ($tmp -and (Test-Path $tmp)) { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
        }
    }
    Pop-Location
}
