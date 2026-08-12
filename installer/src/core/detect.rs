use std::path::{Path, PathBuf};

use regex::Regex;

use super::paths::{self, GAME_DATA_DIR, GAME_DIR_NAME, GAME_EXE};

pub fn detect_game_path() -> Option<PathBuf> {
    for steam_dir in paths::steam_roots() {
        // Every Steam library (including ones on other drives) is listed in the
        // root install's libraryfolders.vdf.
        let vdf_path = steam_dir.join("steamapps").join("libraryfolders.vdf");
        if vdf_path.exists() {
            if let Ok(content) = std::fs::read_to_string(&vdf_path) {
                for lib_path in parse_vdf_library_paths(&content) {
                    let game_path = lib_path.join("steamapps").join("common").join(GAME_DIR_NAME);
                    if validate_game_path(&game_path) {
                        return Some(game_path);
                    }
                }
            }
        }

        let default_path = steam_dir.join("steamapps").join("common").join(GAME_DIR_NAME);
        if validate_game_path(&default_path) {
            return Some(default_path);
        }
    }
    None
}

pub fn parse_vdf_library_paths(content: &str) -> Vec<PathBuf> {
    let re = Regex::new(r#""path"\s+"([^"]+)""#).unwrap();
    re.captures_iter(content)
        .map(|cap| {
            let raw = cap[1].replace("\\\\", "\\");
            PathBuf::from(raw)
        })
        .collect()
}

pub fn validate_game_path(path: &Path) -> bool {
    path.join(GAME_EXE).exists() && path.join(GAME_DATA_DIR).is_dir()
}

pub fn is_bepinex_installed(game_path: &Path) -> bool {
    game_path.join("winhttp.dll").exists() && game_path.join("BepInEx").join("core").is_dir()
}

pub fn is_mod_installed(game_path: &Path) -> bool {
    paths::mod_dll(game_path).exists()
}

/// Version of the installed mod, read from ObeliskAccess.dll's PE version resource.
/// Works for manual installs too — no side-channel version file needed.
pub fn installed_mod_version(game_path: &Path) -> Option<String> {
    let dll = paths::mod_dll(game_path);
    pe_file_version(&dll)
}

pub fn pe_file_version(dll: &Path) -> Option<String> {
    let map = pelite::FileMap::open(dll).ok()?;
    let file = pelite::PeFile::from_bytes(&map).ok()?;
    let resources = file.resources().ok()?;
    let version_info = resources.version_info().ok()?;
    let fixed = version_info.fixed()?;
    let v = fixed.dwFileVersion;
    Some(format!("{}.{}.{}", v.Major, v.Minor, v.Patch))
}

/// A running executable is locked against writing on Windows — cheap "is the game
/// open" check without walking the process list.
pub fn is_game_running(game_path: &Path) -> bool {
    let exe = game_path.join(GAME_EXE);
    if !exe.exists() {
        return false;
    }
    match std::fs::OpenOptions::new().write(true).open(&exe) {
        Ok(_) => false,
        Err(e) => e.raw_os_error() == Some(32), // ERROR_SHARING_VIOLATION
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    #[test]
    fn parse_vdf_empty_content() {
        assert!(parse_vdf_library_paths("").is_empty());
    }

    #[test]
    fn parse_vdf_multiple_paths() {
        let content = r#"
        "0"
        {
            "path"		"C:\\Program Files (x86)\\Steam"
        }
        "1"
        {
            "path"		"D:\\SteamLibrary"
        }
        "#;
        let paths = parse_vdf_library_paths(content);
        assert_eq!(paths.len(), 2);
        assert_eq!(paths[0], PathBuf::from("C:\\Program Files (x86)\\Steam"));
        assert_eq!(paths[1], PathBuf::from("D:\\SteamLibrary"));
    }

    #[test]
    fn validate_game_path_needs_exe_and_data() {
        let dir = tempfile::tempdir().unwrap();
        assert!(!validate_game_path(dir.path()));
        fs::write(dir.path().join(GAME_EXE), "").unwrap();
        assert!(!validate_game_path(dir.path()));
        fs::create_dir(dir.path().join(GAME_DATA_DIR)).unwrap();
        assert!(validate_game_path(dir.path()));
    }

    #[test]
    fn bepinex_detection() {
        let dir = tempfile::tempdir().unwrap();
        assert!(!is_bepinex_installed(dir.path()));
        fs::write(dir.path().join("winhttp.dll"), "").unwrap();
        assert!(!is_bepinex_installed(dir.path()));
        fs::create_dir_all(dir.path().join("BepInEx").join("core")).unwrap();
        assert!(is_bepinex_installed(dir.path()));
    }

    #[test]
    fn mod_detection() {
        let dir = tempfile::tempdir().unwrap();
        assert!(!is_mod_installed(dir.path()));
        let plugin_dir = paths::mod_plugin_dir(dir.path());
        fs::create_dir_all(&plugin_dir).unwrap();
        fs::write(plugin_dir.join(paths::MOD_DLL_NAME), "").unwrap();
        assert!(is_mod_installed(dir.path()));
    }

    #[test]
    fn version_of_non_pe_file_is_none() {
        let dir = tempfile::tempdir().unwrap();
        let f = dir.path().join("fake.dll");
        fs::write(&f, "not a PE file").unwrap();
        assert!(pe_file_version(&f).is_none());
    }
}
