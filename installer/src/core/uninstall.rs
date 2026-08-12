use std::fs;
use std::path::Path;

use super::paths;

/// Remove the mod's plugin folder. BepInEx and the native speech DLLs are left
/// alone (the latter are offered separately via remove_native_speech).
/// Returns (removed, errors). The error list matters: a locked DLL (the game is still running)
/// or a permissions failure must not be reported as a clean uninstall, or the user is told the
/// mod is gone while it is still fully installed and still loading.
pub fn uninstall_mod(game_path: &Path) -> (Vec<String>, Vec<String>) {
    let mut removed = Vec::new();
    let mut errors = Vec::new();
    let plugin_dir = paths::mod_plugin_dir(game_path);
    if plugin_dir.is_dir() {
        match fs::remove_dir_all(&plugin_dir) {
            Ok(_) => removed.push(format!("BepInEx\\plugins\\{}", paths::PLUGIN_DIR_NAME)),
            Err(e) => errors.push(format!("{}: {}", plugin_dir.display(), e)),
        }
    }
    (removed, errors)
}

/// Remove the native speech DLLs from the game root. Separate opt-in step:
/// the user may have placed their own copies, or use them for another mod.
pub fn remove_native_speech(game_path: &Path) -> (Vec<String>, Vec<String>) {
    let mut removed = Vec::new();
    let mut errors = Vec::new();
    for name in paths::ROOT_NATIVE_FILES {
        let f = game_path.join(name);
        if f.exists() {
            match fs::remove_file(&f) {
                Ok(_) => removed.push((*name).to_string()),
                Err(e) => errors.push(format!("{}: {}", name, e)),
            }
        }
    }
    (removed, errors)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn uninstall_removes_plugin_dir_only() {
        let dir = tempfile::tempdir().unwrap();
        let plugin_dir = paths::mod_plugin_dir(dir.path());
        fs::create_dir_all(&plugin_dir).unwrap();
        fs::write(plugin_dir.join("ObeliskAccess.dll"), "").unwrap();
        fs::create_dir_all(dir.path().join("BepInEx").join("core")).unwrap();
        fs::write(dir.path().join("UniversalSpeech.dll"), "").unwrap();

        let (removed, errors) = uninstall_mod(dir.path());
        assert!(errors.is_empty());
        assert_eq!(removed.len(), 1);
        assert!(!plugin_dir.exists());
        assert!(dir.path().join("BepInEx").join("core").exists());
        assert!(dir.path().join("UniversalSpeech.dll").exists());
    }

    #[test]
    fn uninstall_nothing_installed() {
        let dir = tempfile::tempdir().unwrap();
        let (removed, errors) = uninstall_mod(dir.path());
        assert!(removed.is_empty());
        assert!(errors.is_empty());
    }

    #[test]
    fn remove_native_speech_removes_both() {
        let dir = tempfile::tempdir().unwrap();
        for f in paths::ROOT_NATIVE_FILES {
            fs::write(dir.path().join(f), "").unwrap();
        }
        let (removed, errors) = remove_native_speech(dir.path());
        assert_eq!(removed.len(), 2);
        assert!(errors.is_empty());
    }
}
