use std::io::{self, Write};
use std::path::{Path, PathBuf};

use crate::core::{bepinex, detect, github, install, paths, uninstall, version, winutil};

pub fn run() {
    println!("=== ObeliskAccess Installer ===");
    println!();

    let game_path = match get_game_path() {
        Some(p) => p,
        None => {
            println!("Error: Invalid game directory.");
            return;
        }
    };

    println!();
    show_status(&game_path);
    println!();

    loop {
        println!("Options:");
        println!("  1. Install / Update from GitHub");
        println!("  2. Install from local zip file");
        println!("  3. Uninstall");
        println!("  4. Exit");
        println!();

        let choice = prompt("Choose an option (1-4): ");

        println!();
        match choice.as_str() {
            "1" => install_from_github(&game_path),
            "2" => install_from_file(&game_path),
            "3" => do_uninstall(&game_path),
            "4" => return,
            _ => println!("Invalid option."),
        }
        println!();
    }
}

fn get_game_path() -> Option<PathBuf> {
    if let Some(detected) = detect::detect_game_path() {
        println!("Detected game directory: {}", detected.display());
        let response = prompt("Use this path? (Y/n): ");
        if response != "n" && response != "no" {
            return Some(detected);
        }
    } else {
        println!("Could not auto-detect the Across the Obelisk game directory.");
    }

    let input = prompt("Press B to browse for the game directory, or type the path: ");
    if input.eq_ignore_ascii_case("b") {
        let path = rfd::FileDialog::new()
            .set_title("Select the Across the Obelisk game directory")
            .pick_folder();
        match path {
            Some(p) if detect::validate_game_path(&p) => Some(p),
            Some(_) => {
                println!(
                    "Error: That directory does not contain {} — not a game install.",
                    paths::GAME_EXE
                );
                None
            }
            None => {
                println!("Browse cancelled.");
                None
            }
        }
    } else {
        let path = PathBuf::from(&input);
        if detect::validate_game_path(&path) {
            Some(path)
        } else {
            None
        }
    }
}

fn show_status(game_path: &Path) {
    if detect::is_bepinex_installed(game_path) {
        println!("BepInEx: installed.");
    } else {
        println!(
            "BepInEx: not installed (version {} will be installed with the mod).",
            paths::BEPINEX_VERSION
        );
    }
    if detect::is_mod_installed(game_path) {
        let version = detect::installed_mod_version(game_path)
            .unwrap_or_else(|| "unknown".to_string());
        println!("ObeliskAccess is installed (version: {}).", version);
    } else {
        println!("ObeliskAccess is not currently installed.");
    }
}

/// Shared preflight: refuse while the game runs; offer elevation when the game
/// directory is not writable.
fn ensure_ready(game_path: &Path) -> bool {
    if detect::is_game_running(game_path) {
        println!("Across the Obelisk appears to be running. Close the game and try again.");
        return false;
    }
    if !install::can_write(game_path) {
        println!("The game directory is not writable from this account.");
        let response = prompt(
            "Relaunch the installer as administrator? A new window will open. (y/N): ",
        );
        if response == "y" || response == "yes" {
            if winutil::relaunch_elevated("--cli") {
                std::process::exit(0);
            }
            println!("Could not relaunch elevated.");
        }
        return false;
    }
    true
}

fn ensure_bepinex_interactive(game_path: &Path) -> bool {
    match bepinex::ensure_bepinex(game_path, |pct| {
        print!("\rBepInEx download: {}%   ", pct);
        io::stdout().flush().ok();
    }) {
        Ok(true) => {
            println!();
            println!("Installed BepInEx {}.", paths::BEPINEX_VERSION);
            true
        }
        Ok(false) => true,
        Err(e) => {
            println!();
            println!("Error: {}", e);
            false
        }
    }
}

fn report_install(report: &install::InstallReport) {
    println!("Installed {} file(s).", report.installed.len());
    if !report.kept_existing.is_empty() {
        println!(
            "Kept your existing copies of: {}.",
            report.kept_existing.join(", ")
        );
    }
    if !report.unknown.is_empty() {
        println!(
            "Warning: skipped unrecognized zip entries: {}.",
            report.unknown.join(", ")
        );
    }
}

fn install_from_github(game_path: &Path) {
    if !ensure_ready(game_path) {
        return;
    }

    println!("Checking for latest release...");

    let release = match github::fetch_latest_release() {
        Ok(r) => r,
        Err(e) => {
            println!("Error: {}", e);
            return;
        }
    };

    println!("Latest version: {}", release.tag_name);

    let installed = detect::installed_mod_version(game_path);
    if detect::is_mod_installed(game_path)
        && version::is_up_to_date(installed.as_deref(), &release.tag_name)
    {
        let response = prompt("You already have the latest version. Reinstall anyway? (y/N): ");
        if response != "y" && response != "yes" {
            return;
        }
    } else if let Some(installed) = installed.as_deref() {
        println!("Installed version: {}", installed);
    }

    if !release.body.is_empty() {
        println!();
        println!("Release notes:");
        println!("{}", release.body);
        println!();
    }

    let confirm = prompt("Proceed with installation? (Y/n): ");
    if confirm == "n" || confirm == "no" {
        return;
    }

    let asset = match github::find_mod_zip_asset(&release.assets) {
        Some(a) => a,
        None => {
            println!("Error: No mod .zip asset found in the release.");
            return;
        }
    };

    if !ensure_bepinex_interactive(game_path) {
        return;
    }

    println!("Downloading {}...", asset.name);

    match install::download_and_install_mod(&asset.browser_download_url, game_path, |pct| {
        print!("\rProgress: {}%   ", pct);
        io::stdout().flush().ok();
    }) {
        Ok(report) => {
            println!();
            report_install(&report);
            println!("Successfully installed version {}.", release.tag_name);
        }
        Err(e) => {
            println!();
            println!("Error: {}", e);
        }
    }
}

fn install_from_file(game_path: &Path) {
    if !ensure_ready(game_path) {
        return;
    }

    let input = prompt("Press B to browse for the zip file, or type the path: ");
    let zip_path = if input.eq_ignore_ascii_case("b") {
        match rfd::FileDialog::new()
            .set_title("Select mod zip file")
            .add_filter("Zip files", &["zip"])
            .pick_file()
        {
            Some(p) => p,
            None => {
                println!("Browse cancelled.");
                return;
            }
        }
    } else {
        PathBuf::from(&input)
    };

    if !zip_path.exists() {
        println!("Error: File not found.");
        return;
    }

    if !ensure_bepinex_interactive(game_path) {
        return;
    }

    match install::install_mod_from_file(&zip_path, game_path) {
        Ok(report) => {
            report_install(&report);
            println!(
                "Installed from {}.",
                zip_path.file_name().unwrap_or_default().to_string_lossy()
            );
        }
        Err(e) => println!("Error: {}", e),
    }
}

fn do_uninstall(game_path: &Path) {
    if !ensure_ready(game_path) {
        return;
    }

    let confirm = prompt("Remove ObeliskAccess mod files? (y/N): ");
    if confirm != "y" && confirm != "yes" {
        return;
    }

    let (removed, errors) = uninstall::uninstall_mod(game_path);
    if !errors.is_empty() {
        println!("Could not remove: {}", errors.join("; "));
        println!("The mod is still installed. Close the game and try again.");
    } else if removed.is_empty() {
        println!("No mod files found to remove.");
    } else {
        println!("Removed: {}", removed.join(", "));
    }

    let remove_speech = prompt(
        "Also remove the speech DLLs (UniversalSpeech, nvdaControllerClient) from the game folder? (y/N): ",
    );
    if remove_speech == "y" || remove_speech == "yes" {
        let (removed, errors) = uninstall::remove_native_speech(game_path);
        if !removed.is_empty() {
            println!("Removed: {}", removed.join(", "));
        }
        if !errors.is_empty() {
            println!("Errors: {}", errors.join("; "));
        }
    }

    println!("Uninstall complete. BepInEx was left in place.");
}

fn prompt(msg: &str) -> String {
    print!("{}", msg);
    io::stdout().flush().ok();
    let mut input = String::new();
    match io::stdin().read_line(&mut input) {
        Ok(0) | Err(_) => {
            // EOF (redirected input exhausted) — never loop on an unanswerable prompt.
            println!();
            std::process::exit(0);
        }
        Ok(_) => {}
    }
    input.trim().to_lowercase()
}
