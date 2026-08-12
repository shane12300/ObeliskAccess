use serde::Deserialize;

use super::paths::{GITHUB_API_URL, GITHUB_RELEASES_URL};

#[derive(Debug, Deserialize, Clone)]
pub struct ReleaseInfo {
    pub tag_name: String,
    #[serde(default)]
    pub body: String,
    #[serde(default)]
    pub prerelease: bool,
    #[serde(default)]
    pub assets: Vec<Asset>,
}

#[derive(Debug, Deserialize, Clone)]
pub struct Asset {
    pub name: String,
    pub browser_download_url: String,
}

fn client() -> Result<reqwest::blocking::Client, String> {
    reqwest::blocking::Client::builder()
        .user_agent("ObeliskAccessInstaller")
        .timeout(std::time::Duration::from_secs(15))
        .build()
        .map_err(|e| format!("Failed to create HTTP client: {}", e))
}

pub fn fetch_latest_release() -> Result<ReleaseInfo, String> {
    let resp = client()?
        .get(GITHUB_API_URL)
        .send()
        .map_err(|e| format!("Failed to connect to GitHub: {}", e))?;

    if !resp.status().is_success() {
        return Err(format!("GitHub API returned status {}", resp.status()));
    }

    resp.json::<ReleaseInfo>()
        .map_err(|e| format!("Failed to parse release info: {}", e))
}

pub fn fetch_all_releases() -> Result<Vec<ReleaseInfo>, String> {
    let resp = client()?
        .get(GITHUB_RELEASES_URL)
        .send()
        .map_err(|e| format!("Failed to connect to GitHub: {}", e))?;

    if !resp.status().is_success() {
        return Err(format!("GitHub API returned status {}", resp.status()));
    }

    resp.json::<Vec<ReleaseInfo>>()
        .map_err(|e| format!("Failed to parse releases: {}", e))
}

/// The mod zip among a release's assets. Releases also carry the installer exe,
/// so match the mod's own naming first and only then fall back to any zip.
pub fn find_mod_zip_asset(assets: &[Asset]) -> Option<&Asset> {
    assets
        .iter()
        .find(|a| a.name.starts_with("ObeliskAccess-") && a.name.ends_with(".zip"))
        .or_else(|| assets.iter().find(|a| a.name.ends_with(".zip")))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn asset(name: &str) -> Asset {
        Asset {
            name: name.to_string(),
            browser_download_url: format!("https://example.com/{}", name),
        }
    }

    #[test]
    fn prefers_mod_zip_over_other_zips() {
        let assets = vec![
            asset("SomethingElse.zip"),
            asset("ObeliskAccessInstaller.exe"),
            asset("ObeliskAccess-v1.0.0.zip"),
        ];
        assert_eq!(find_mod_zip_asset(&assets).unwrap().name, "ObeliskAccess-v1.0.0.zip");
    }

    #[test]
    fn falls_back_to_any_zip() {
        let assets = vec![asset("mod.zip"), asset("installer.exe")];
        assert_eq!(find_mod_zip_asset(&assets).unwrap().name, "mod.zip");
    }

    #[test]
    fn none_when_no_zip() {
        let assets = vec![asset("ObeliskAccessInstaller.exe")];
        assert!(find_mod_zip_asset(&assets).is_none());
    }

    #[test]
    fn deserialize_release_info_missing_optional_fields() {
        let json = r#"{"tag_name": "v1.0.0"}"#;
        let info: ReleaseInfo = serde_json::from_str(json).unwrap();
        assert_eq!(info.tag_name, "v1.0.0");
        assert_eq!(info.body, "");
        assert!(info.assets.is_empty());
    }
}
