use std::path::Path;

use super::detect;
use super::install;
use super::paths::BEPINEX_URL;

/// Make sure BepInEx 5 is present in the game directory, downloading the pinned
/// release when it isn't. Returns true when BepInEx was freshly installed.
pub fn ensure_bepinex(game_path: &Path, progress: impl Fn(u32)) -> Result<bool, String> {
    if detect::is_bepinex_installed(game_path) {
        return Ok(false);
    }
    let data = install::download(BEPINEX_URL, progress)
        .map_err(|e| format!("BepInEx download failed: {}", e))?;
    install::extract_zip_plain(&data, game_path)
        .map_err(|e| format!("BepInEx install failed: {}", e))?;
    Ok(true)
}
