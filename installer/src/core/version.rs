/// Normalize a version string ("v1.0", "1.0.0", "1.0.0.0") to a semver Version,
/// padding or truncating to three components.
pub fn parse_version(s: &str) -> Option<semver::Version> {
    let trimmed = s
        .trim()
        .strip_prefix('v')
        .or_else(|| s.trim().strip_prefix('V'))
        .unwrap_or(s.trim());
    let mut parts = trimmed.split('.');
    let major: u64 = parts.next()?.parse().ok()?;
    let minor: u64 = parts.next().unwrap_or("0").parse().ok()?;
    let patch: u64 = parts.next().unwrap_or("0").parse().ok()?;
    Some(semver::Version::new(major, minor, patch))
}

/// True when the installed version is at least the latest released version.
pub fn is_up_to_date(installed: Option<&str>, latest: &str) -> bool {
    let Some(installed) = installed else {
        return false;
    };
    match (parse_version(installed), parse_version(latest)) {
        (Some(inst), Some(lat)) => inst >= lat,
        _ => installed == latest, // fallback to string comparison
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_all_forms() {
        assert_eq!(parse_version("v1.0.0").unwrap(), semver::Version::new(1, 0, 0));
        assert_eq!(parse_version("1.0").unwrap(), semver::Version::new(1, 0, 0));
        assert_eq!(parse_version("1.2.3.4").unwrap(), semver::Version::new(1, 2, 3));
        assert!(parse_version("garbage").is_none());
    }

    #[test]
    fn up_to_date_comparisons() {
        assert!(is_up_to_date(Some("1.0.0"), "v1.0.0"));
        assert!(is_up_to_date(Some("1.1.0"), "v1.0.0"));
        assert!(!is_up_to_date(Some("1.0.0"), "v1.1.0"));
        assert!(!is_up_to_date(None, "v1.0.0"));
        // Four-part PE version vs three-part tag
        assert!(is_up_to_date(Some("1.0.0.0"), "v1.0.0"));
    }
}
