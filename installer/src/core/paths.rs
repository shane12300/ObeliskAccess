use std::path::{Path, PathBuf};

pub const GITHUB_RELEASES_URL: &str =
    "https://api.github.com/repos/shane12300/ObeliskAccess/releases";

pub const GAME_DIR_NAME: &str = "Across the Obelisk";
pub const GAME_EXE: &str = "AcrossTheObelisk.exe";
pub const GAME_DATA_DIR: &str = "AcrossTheObelisk_Data";

/// BepInEx 5 is pinned: a future BepInEx 6 "latest" must never change what gets installed.
pub const BEPINEX_VERSION: &str = "5.4.23.5";
pub const BEPINEX_URL: &str =
    "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip";

pub const PLUGIN_DIR_NAME: &str = "ObeliskAccess";
pub const MOD_DLL_NAME: &str = "ObeliskAccess.dll";

/// Native speech DLLs that live next to the game executable. Installed only when
/// absent — a user's own (possibly newer) copy is never overwritten.
pub const ROOT_NATIVE_FILES: &[&str] = &["UniversalSpeech.dll", "nvdaControllerClient.dll"];

pub fn plugins_dir(game: &Path) -> PathBuf {
    game.join("BepInEx").join("plugins")
}

pub fn mod_plugin_dir(game: &Path) -> PathBuf {
    plugins_dir(game).join(PLUGIN_DIR_NAME)
}

pub fn mod_dll(game: &Path) -> PathBuf {
    mod_plugin_dir(game).join(MOD_DLL_NAME)
}

/// Candidate Steam root directories, most-authoritative first.
pub fn steam_roots() -> Vec<PathBuf> {
    let mut roots: Vec<PathBuf> = Vec::new();

    #[cfg(windows)]
    {
        use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE};
        use winreg::RegKey;

        if let Ok(key) = RegKey::predef(HKEY_CURRENT_USER).open_subkey("Software\\Valve\\Steam") {
            if let Ok(path) = key.get_value::<String, _>("SteamPath") {
                roots.push(PathBuf::from(path.replace('/', "\\")));
            }
        }
        if let Ok(key) =
            RegKey::predef(HKEY_LOCAL_MACHINE).open_subkey("SOFTWARE\\WOW6432Node\\Valve\\Steam")
        {
            if let Ok(path) = key.get_value::<String, _>("InstallPath") {
                roots.push(PathBuf::from(path));
            }
        }
    }

    roots.push(PathBuf::from("C:\\Program Files (x86)\\Steam"));
    roots.push(PathBuf::from("C:\\Program Files\\Steam"));
    roots.dedup();
    roots
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn steam_roots_not_empty() {
        assert!(!steam_roots().is_empty());
    }

    #[test]
    fn mod_dll_is_under_plugin_dir() {
        let game = Path::new("C:\\game");
        assert!(mod_dll(game).starts_with(mod_plugin_dir(game)));
    }
}
