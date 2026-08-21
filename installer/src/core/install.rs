use std::fs;
use std::io::{Cursor, Read};
use std::path::{Component, Path, PathBuf};

use super::paths;

/// What one install pass did, for the log.
#[derive(Default)]
pub struct InstallReport {
    pub installed: Vec<String>,
    /// gameroot/ files left alone because the user already had a copy.
    pub kept_existing: Vec<String>,
    /// zip entries that matched no known layout prefix (release/installer mismatch).
    pub unknown: Vec<String>,
}

pub fn download(url: &str, progress: impl Fn(u32)) -> Result<Vec<u8>, String> {
    let client = reqwest::blocking::Client::builder()
        .user_agent("ObeliskAccessInstaller")
        .timeout(std::time::Duration::from_secs(300))
        .build()
        .map_err(|e| format!("Failed to create HTTP client: {}", e))?;

    let resp = client
        .get(url)
        .send()
        .map_err(|e| format!("Download failed: {}", e))?;

    if !resp.status().is_success() {
        return Err(format!("Download returned status {}", resp.status()));
    }

    let total = resp.content_length().unwrap_or(0);
    let mut reader = resp;
    let mut buffer = Vec::new();
    let mut downloaded: u64 = 0;
    let mut buf = [0u8; 8192];

    loop {
        let n = reader
            .read(&mut buf)
            .map_err(|e| format!("Read error: {}", e))?;
        if n == 0 {
            break;
        }
        buffer.extend_from_slice(&buf[..n]);
        downloaded += n as u64;
        if total > 0 {
            progress((downloaded * 100 / total) as u32);
        }
    }

    Ok(buffer)
}

pub fn download_and_install_mod(
    url: &str,
    game_path: &Path,
    progress: impl Fn(u32),
) -> Result<InstallReport, String> {
    let data = download(url, progress)?;
    install_mod_zip(&data, game_path)
}

pub fn install_mod_from_file(zip_path: &Path, game_path: &Path) -> Result<InstallReport, String> {
    let data = fs::read(zip_path).map_err(|e| format!("Failed to read zip: {}", e))?;
    install_mod_zip(&data, game_path)
}

/// Extract a mod release zip into the game directory per the release layout contract:
///   plugins/**   -> <game>/BepInEx/plugins/**            (always overwritten)
///   gameroot/**  -> <game>/**                            (only when absent)
///   <root docs>  -> <game>/BepInEx/plugins/ObeliskAccess (always overwritten)
/// The contract lives in the zip's paths, so a future release can add files without
/// an installer update.
pub fn install_mod_zip(data: &[u8], game_path: &Path) -> Result<InstallReport, String> {
    let cursor = Cursor::new(data);
    let mut archive =
        zip::ZipArchive::new(cursor).map_err(|e| format!("Failed to open zip: {}", e))?;

    let mut report = InstallReport::default();

    for i in 0..archive.len() {
        let mut file = archive
            .by_index(i)
            .map_err(|e| format!("Failed to read zip entry: {}", e))?;

        let name = file.name().replace('\\', "/");
        if name.ends_with('/') {
            continue; // directory entry
        }
        if !is_safe_relative(&name) {
            return Err(format!("Unsafe path in zip: {}", name));
        }

        let Some((dest, skip_if_exists)) = dest_for_entry(&name, game_path) else {
            report.unknown.push(name);
            continue;
        };

        if skip_if_exists && dest.exists() {
            report.kept_existing.push(display_name(&name));
            continue;
        }

        if let Some(parent) = dest.parent() {
            fs::create_dir_all(parent)
                .map_err(|e| format!("Failed to create directory {}: {}", parent.display(), e))?;
        }
        let mut out_file = fs::File::create(&dest)
            .map_err(|e| format!("Failed to create file {}: {}", dest.display(), e))?;
        std::io::copy(&mut file, &mut out_file)
            .map_err(|e| format!("Failed to write file {}: {}", dest.display(), e))?;
        report.installed.push(display_name(&name));
    }

    if report.installed.is_empty() {
        return Err("The zip contained no installable files — is this a mod release zip?".into());
    }
    Ok(report)
}

fn dest_for_entry(name: &str, game_path: &Path) -> Option<(PathBuf, bool)> {
    if let Some(rest) = name.strip_prefix("plugins/") {
        return Some((paths::plugins_dir(game_path).join(rest), false));
    }
    if let Some(rest) = name.strip_prefix("gameroot/") {
        return Some((game_path.join(rest), true));
    }
    if !name.contains('/') {
        // Docs (README.md, changelog.md) travel to the plugin folder.
        return Some((paths::mod_plugin_dir(game_path).join(name), false));
    }
    None
}

fn display_name(entry: &str) -> String {
    entry.rsplit('/').next().unwrap_or(entry).to_string()
}

fn is_safe_relative(name: &str) -> bool {
    let path = Path::new(name);
    path.components()
        .all(|c| matches!(c, Component::Normal(_)))
}

/// Extract a zip verbatim into `dest` (used for the BepInEx package, whose zip
/// already mirrors the game root).
pub fn extract_zip_plain(data: &[u8], dest: &Path) -> Result<(), String> {
    let cursor = Cursor::new(data);
    let mut archive =
        zip::ZipArchive::new(cursor).map_err(|e| format!("Failed to open zip: {}", e))?;

    for i in 0..archive.len() {
        let mut file = archive
            .by_index(i)
            .map_err(|e| format!("Failed to read zip entry: {}", e))?;

        let name = file.name().replace('\\', "/");
        if !is_safe_relative(name.trim_end_matches('/')) {
            return Err(format!("Unsafe path in zip: {}", name));
        }
        let out_path = dest.join(&name);
        if name.ends_with('/') {
            fs::create_dir_all(&out_path)
                .map_err(|e| format!("Failed to create dir {}: {}", name, e))?;
            continue;
        }
        if let Some(parent) = out_path.parent() {
            fs::create_dir_all(parent)
                .map_err(|e| format!("Failed to create parent dir: {}", e))?;
        }
        let mut out_file = fs::File::create(&out_path)
            .map_err(|e| format!("Failed to create file {}: {}", name, e))?;
        std::io::copy(&mut file, &mut out_file)
            .map_err(|e| format!("Failed to write file {}: {}", name, e))?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn make_zip(entries: &[(&str, &[u8])]) -> Vec<u8> {
        let mut zip_buf = Vec::new();
        {
            let cursor = Cursor::new(&mut zip_buf);
            let mut writer = zip::ZipWriter::new(cursor);
            let options = zip::write::SimpleFileOptions::default();
            for (name, content) in entries {
                writer.start_file(*name, options).unwrap();
                writer.write_all(content).unwrap();
            }
            writer.finish().unwrap();
        }
        zip_buf
    }

    #[test]
    fn layout_contract_routes_files() {
        let dir = tempfile::tempdir().unwrap();
        let zip = make_zip(&[
            ("plugins/ObeliskAccess/ObeliskAccess.dll", b"mod"),
            ("plugins/ObeliskAccess/UnityAccessibilityLib.dll", b"lib"),
            ("gameroot/UniversalSpeech.dll", b"native"),
            ("README.md", b"docs"),
        ]);

        let report = install_mod_zip(&zip, dir.path()).unwrap();
        assert_eq!(report.installed.len(), 4);
        assert!(report.kept_existing.is_empty());

        let plugin_dir = dir.path().join("BepInEx").join("plugins").join("ObeliskAccess");
        assert!(plugin_dir.join("ObeliskAccess.dll").exists());
        assert!(plugin_dir.join("UnityAccessibilityLib.dll").exists());
        assert!(plugin_dir.join("README.md").exists());
        assert!(dir.path().join("UniversalSpeech.dll").exists());
    }

    #[test]
    fn gameroot_files_never_overwritten() {
        let dir = tempfile::tempdir().unwrap();
        fs::write(dir.path().join("UniversalSpeech.dll"), "users own copy").unwrap();
        let zip = make_zip(&[
            ("plugins/ObeliskAccess/ObeliskAccess.dll", b"mod"),
            ("gameroot/UniversalSpeech.dll", b"shipped"),
        ]);

        let report = install_mod_zip(&zip, dir.path()).unwrap();
        assert_eq!(report.kept_existing, vec!["UniversalSpeech.dll"]);
        let content = fs::read_to_string(dir.path().join("UniversalSpeech.dll")).unwrap();
        assert_eq!(content, "users own copy");
    }

    #[test]
    fn plugin_files_always_overwritten() {
        let dir = tempfile::tempdir().unwrap();
        let dll = dir
            .path()
            .join("BepInEx")
            .join("plugins")
            .join("ObeliskAccess")
            .join("ObeliskAccess.dll");
        fs::create_dir_all(dll.parent().unwrap()).unwrap();
        fs::write(&dll, "old version").unwrap();

        let zip = make_zip(&[("plugins/ObeliskAccess/ObeliskAccess.dll", b"new version")]);
        install_mod_zip(&zip, dir.path()).unwrap();
        assert_eq!(fs::read_to_string(&dll).unwrap(), "new version");
    }

    #[test]
    fn unknown_entries_are_skipped_not_extracted() {
        let dir = tempfile::tempdir().unwrap();
        let zip = make_zip(&[
            ("plugins/ObeliskAccess/ObeliskAccess.dll", b"mod"),
            ("mystery/whatever.txt", b"?"),
        ]);
        let report = install_mod_zip(&zip, dir.path()).unwrap();
        assert_eq!(report.unknown, vec!["mystery/whatever.txt"]);
        assert!(!dir.path().join("mystery").exists());
    }

    #[test]
    fn zip_slip_rejected() {
        let dir = tempfile::tempdir().unwrap();
        let zip = make_zip(&[("plugins/../../evil.dll", b"evil")]);
        assert!(install_mod_zip(&zip, dir.path()).is_err());
    }

    #[test]
    fn empty_zip_is_an_error() {
        let dir = tempfile::tempdir().unwrap();
        let zip = make_zip(&[]);
        assert!(install_mod_zip(&zip, dir.path()).is_err());
    }

    #[test]
    fn extract_plain_creates_nested_dirs() {
        let dir = tempfile::tempdir().unwrap();
        let zip = make_zip(&[("BepInEx/core/BepInEx.dll", b"core"), ("winhttp.dll", b"shim")]);
        extract_zip_plain(&zip, dir.path()).unwrap();
        assert!(dir.path().join("BepInEx").join("core").join("BepInEx.dll").exists());
        assert!(dir.path().join("winhttp.dll").exists());
    }
}
