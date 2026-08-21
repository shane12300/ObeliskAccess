use std::cell::RefCell;
use std::path::{Path, PathBuf};
use std::rc::Rc;

use wxdragon::prelude::*;

use crate::core::{bepinex, detect, github, install, paths, uninstall, version};

struct State {
    release: Option<github::ReleaseInfo>,
    all_releases: Vec<github::ReleaseInfo>,
}

pub fn run() {
    wxdragon::main(|_app| {
        let frame = Frame::builder()
            .with_title("ObeliskAccess Installer")
            .with_size(Size::new(650, 500))
            .build();

        let panel = Panel::builder(&frame).build();
        let main_sizer = BoxSizer::builder(Orientation::Vertical).build();

        // Status text
        let status = StaticText::builder(&panel)
            .with_label("Detecting game directory...")
            .build();

        // Game path row
        let path_sizer = BoxSizer::builder(Orientation::Horizontal).build();
        let path_label = StaticText::builder(&panel)
            .with_label("Game directory:")
            .build();
        let path_input = TextCtrl::builder(&panel).build();
        let browse_btn = Button::builder(&panel).with_label("Browse...").build();

        path_sizer.add(&path_label, 0, SizerFlag::All, 4);
        path_sizer.add(&path_input, 1, SizerFlag::Expand | SizerFlag::All, 4);
        path_sizer.add(&browse_btn, 0, SizerFlag::All, 4);

        // Log area
        let log = TextCtrl::builder(&panel)
            .with_style(TextCtrlStyle::MultiLine | TextCtrlStyle::ReadOnly | TextCtrlStyle::WordWrap)
            .build();

        // Button row
        let btn_sizer = BoxSizer::builder(Orientation::Horizontal).build();
        let install_btn = Button::builder(&panel).with_label("Install").build();
        let install_file_btn = Button::builder(&panel)
            .with_label("Install from file...")
            .build();
        let uninstall_btn = Button::builder(&panel).with_label("Uninstall").build();

        btn_sizer.add_stretch_spacer(1);
        btn_sizer.add(&install_btn, 0, SizerFlag::All, 4);
        btn_sizer.add(&install_file_btn, 0, SizerFlag::All, 4);
        btn_sizer.add(&uninstall_btn, 0, SizerFlag::All, 4);

        // Layout
        main_sizer.add(&status, 0, SizerFlag::Expand | SizerFlag::All, 8);
        main_sizer.add_sizer(&path_sizer, 0, SizerFlag::Expand | SizerFlag::Left | SizerFlag::Right, 8);
        main_sizer.add(&log, 1, SizerFlag::Expand | SizerFlag::All, 8);
        main_sizer.add_sizer(&btn_sizer, 0, SizerFlag::Expand | SizerFlag::All, 4);

        panel.set_sizer(main_sizer, true);

        // Disable action buttons until detection finishes
        install_btn.enable(false);
        install_file_btn.enable(false);
        uninstall_btn.enable(false);

        // Shared state
        let state = Rc::new(RefCell::new(State {
            release: None,
            all_releases: Vec::new(),
        }));

        // Auto-detect game path
        if let Some(detected) = detect::detect_game_path() {
            path_input.set_value(&detected.to_string_lossy());
            log_append(&log, &format!("Game directory: {}", detected.display()));
            update_state(&status, &install_btn, &install_file_btn, &uninstall_btn, &detected, &state, &log);
        } else {
            status.set_label("Game directory not found. Please browse to select it.");
            log_append(&log, "Could not auto-detect the Across the Obelisk game directory.");
        }

        // Fetch release info (before showing window, so no visible delay)
        match github::fetch_all_releases() {
            Ok(releases) => {
                let latest = releases.iter().find(|r| !r.prerelease).cloned();
                if let Some(ref info) = latest {
                    log_append(&log, &format!("Latest version: {}", info.tag_name));
                }
                {
                    let mut s = state.borrow_mut();
                    s.all_releases = releases;
                    s.release = latest;
                }
                let path = PathBuf::from(path_input.get_value());
                if detect::validate_game_path(&path) {
                    update_state(&status, &install_btn, &install_file_btn, &uninstall_btn, &path, &state, &log);
                }
            }
            Err(e) => {
                log_append(&log, &format!("Failed to check for updates: {}", e));
                status.set_label("Could not connect to GitHub. Only 'Install from file' is available.");
                let path = PathBuf::from(path_input.get_value());
                install_file_btn.enable(detect::validate_game_path(&path));
                uninstall_btn.enable(detect::is_mod_installed(&path));
            }
        }

        // Browse button
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();

            browse_btn.on_click(move |_| {
                let dialog =
                    DirDialog::builder(&frame_c, "Select the Across the Obelisk game directory", "")
                        .build();
                if dialog.show_modal() == ID_OK {
                    if let Some(path_str) = dialog.get_path() {
                        let path = PathBuf::from(&path_str);
                        if !detect::validate_game_path(&path) {
                            log_append(&log_c, &format!("Invalid game directory: {}", path.display()));
                            status_c.set_label(&format!(
                                "That folder does not contain {}. Please select the game's install folder.",
                                paths::GAME_EXE
                            ));
                            return;
                        }
                        path_input_c.set_value(&path.to_string_lossy());
                        log_append(&log_c, &format!("Game directory: {}", path.display()));
                        update_state(&status_c, &install_btn_c, &install_file_btn_c, &uninstall_btn_c, &path, &state_c, &log_c);
                    }
                }
            });
        }

        // Install / Update button
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let browse_btn_c = browse_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();

            install_btn.on_click(move |_| {
                let game_path = PathBuf::from(path_input_c.get_value());
                if !ensure_ready(&frame_c, &game_path) {
                    return;
                }

                let borrow = state_c.borrow();
                if borrow.all_releases.is_empty() {
                    return;
                }

                // Build version list for picker
                let choices: Vec<String> = borrow
                    .all_releases
                    .iter()
                    .map(|r| {
                        if r.prerelease {
                            format!("{} (pre-release)", r.tag_name)
                        } else {
                            r.tag_name.clone()
                        }
                    })
                    .collect();
                let choice_refs: Vec<&str> = choices.iter().map(|s| s.as_str()).collect();

                drop(borrow);

                let dialog = SingleChoiceDialog::builder(
                    &frame_c,
                    "Select a version to install:",
                    "Choose Version",
                    &choice_refs,
                )
                .build();

                if dialog.show_modal() != ID_OK {
                    return;
                }
                let selection = dialog.get_selection();
                if selection < 0 {
                    return;
                }

                let borrow = state_c.borrow();
                let info = &borrow.all_releases[selection as usize];

                if !info.body.is_empty() {
                    let dialog = MessageDialog::builder(
                        &frame_c,
                        &format!("Release notes for {}:\n\n{}\n\nProceed?", info.tag_name, info.body),
                        &format!("Install {}", info.tag_name),
                    )
                    .with_style(MessageDialogStyle::YesNo | MessageDialogStyle::IconQuestion)
                    .build();
                    if dialog.show_modal() != ID_YES {
                        return;
                    }
                }

                let Some(asset) = github::find_mod_zip_asset(&info.assets) else {
                    log_append(&log_c, "Error: No mod .zip asset found in the release.");
                    return;
                };

                let url = asset.browser_download_url.clone();
                let version = info.tag_name.clone();
                drop(borrow);

                // Disable buttons during download
                install_btn_c.enable(false);
                install_file_btn_c.enable(false);
                uninstall_btn_c.enable(false);
                browse_btn_c.enable(false);

                let result = run_full_install(&game_path, &url, &log_c, &status_c);

                install_file_btn_c.enable(true);
                uninstall_btn_c.enable(true);
                browse_btn_c.enable(true);

                match result {
                    Ok(()) => {
                        log_append(&log_c, &format!("Successfully installed version {}.", version));
                        update_state(&status_c, &install_btn_c, &install_file_btn_c, &uninstall_btn_c, &game_path, &state_c, &log_c);

                        MessageDialog::builder(
                            &frame_c,
                            &format!("ObeliskAccess version {} installed successfully!", version),
                            "Installation Complete",
                        )
                        .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconInformation)
                        .build()
                        .show_modal();
                    }
                    Err(e) => {
                        install_btn_c.enable(true);
                        log_append(&log_c, &format!("Error: {}", e));
                        MessageDialog::builder(&frame_c, &e, "Installation Failed")
                            .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconError)
                            .build()
                            .show_modal();
                    }
                }
            });
        }

        // Install from file button
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();

            install_file_btn.on_click(move |_| {
                let game_path = PathBuf::from(path_input_c.get_value());
                if !ensure_ready(&frame_c, &game_path) {
                    return;
                }

                let dialog = FileDialog::builder(&frame_c)
                    .with_message("Select mod zip file")
                    .with_wildcard("Zip files (*.zip)|*.zip")
                    .with_style(FileDialogStyle::Open | FileDialogStyle::FileMustExist)
                    .build();

                if dialog.show_modal() != ID_OK {
                    return;
                }
                let Some(zip_path_str) = dialog.get_path() else { return };
                let zip_path = PathBuf::from(&zip_path_str);

                let result = ensure_bepinex_logged(&game_path, &log_c, &status_c)
                    .and_then(|_| install::install_mod_from_file(&zip_path, &game_path));

                match result {
                    Ok(report) => {
                        log_install_report(&log_c, &report);
                        log_append(
                            &log_c,
                            &format!(
                                "Installed from {}.",
                                zip_path.file_name().unwrap_or_default().to_string_lossy()
                            ),
                        );
                        update_state(&status_c, &install_btn_c, &install_file_btn_c, &uninstall_btn_c, &game_path, &state_c, &log_c);

                        MessageDialog::builder(
                            &frame_c,
                            "ObeliskAccess installed successfully from file!",
                            "Installation Complete",
                        )
                        .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconInformation)
                        .build()
                        .show_modal();
                    }
                    Err(e) => {
                        log_append(&log_c, &format!("Error: {}", e));
                        MessageDialog::builder(&frame_c, &e, "Error")
                            .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconError)
                            .build()
                            .show_modal();
                    }
                }
            });
        }

        // Uninstall button
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();

            uninstall_btn.on_click(move |_| {
                let game_path = PathBuf::from(path_input_c.get_value());
                if !ensure_ready(&frame_c, &game_path) {
                    return;
                }

                let dialog = MessageDialog::builder(
                    &frame_c,
                    "Remove ObeliskAccess mod files?\n\nBepInEx will be left in place.",
                    "Confirm Uninstall",
                )
                .with_style(MessageDialogStyle::YesNo | MessageDialogStyle::IconQuestion)
                .build();

                if dialog.show_modal() != ID_YES {
                    return;
                }

                let (removed, errors) = uninstall::uninstall_mod(&game_path);
                if !errors.is_empty() {
                    log_append(&log_c, &format!("Could not remove: {}", errors.join("; ")));
                    log_append(&log_c, "The mod is still installed. Close the game and try again.");
                } else if removed.is_empty() {
                    log_append(&log_c, "No mod files found to remove.");
                } else {
                    log_append(&log_c, &format!("Removed: {}", removed.join(", ")));
                }

                let remove_speech = MessageDialog::builder(
                    &frame_c,
                    "Also remove the speech DLLs (UniversalSpeech, nvdaControllerClient) from the game folder?\n\nChoose No if you placed your own copies there.",
                    "Remove Speech DLLs",
                )
                .with_style(MessageDialogStyle::YesNo | MessageDialogStyle::IconQuestion)
                .build()
                .show_modal();

                if remove_speech == ID_YES {
                    let (removed, errors) = uninstall::remove_native_speech(&game_path);
                    if !removed.is_empty() {
                        log_append(&log_c, &format!("Removed: {}", removed.join(", ")));
                    }
                    if !errors.is_empty() {
                        log_append(&log_c, &format!("Errors: {}", errors.join("; ")));
                    }
                }

                log_append(&log_c, "Uninstall complete.");
                update_state(&status_c, &install_btn_c, &install_file_btn_c, &uninstall_btn_c, &game_path, &state_c, &log_c);

                MessageDialog::builder(
                    &frame_c,
                    "ObeliskAccess has been uninstalled.",
                    "Uninstall Complete",
                )
                .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconInformation)
                .build()
                .show_modal();
            });
        }

        frame.show(true);
    })
    .expect("Failed to start application");
}

/// BepInEx (when missing) + mod download + extract, with log/status updates.
fn run_full_install(
    game_path: &Path,
    url: &str,
    log: &TextCtrl,
    status: &StaticText,
) -> Result<(), String> {
    ensure_bepinex_logged(game_path, log, status)?;
    log_append(log, "Downloading ObeliskAccess...");
    status.set_label("Downloading ObeliskAccess...");
    let report = install::download_and_install_mod(url, game_path, |_pct| {})?;
    log_install_report(log, &report);
    Ok(())
}

fn ensure_bepinex_logged(
    game_path: &Path,
    log: &TextCtrl,
    status: &StaticText,
) -> Result<(), String> {
    if !detect::is_bepinex_installed(game_path) {
        log_append(log, &format!("Installing BepInEx {}...", paths::BEPINEX_VERSION));
        status.set_label("Downloading BepInEx...");
    }
    match bepinex::ensure_bepinex(game_path, |_pct| {})? {
        true => log_append(log, &format!("Installed BepInEx {}.", paths::BEPINEX_VERSION)),
        false => {}
    }
    Ok(())
}

fn log_install_report(log: &TextCtrl, report: &install::InstallReport) {
    log_append(log, &format!("Installed {} file(s).", report.installed.len()));
    if !report.kept_existing.is_empty() {
        log_append(
            log,
            &format!("Kept your existing copies of: {}.", report.kept_existing.join(", ")),
        );
    }
    if !report.unknown.is_empty() {
        log_append(
            log,
            &format!(
                "Warning: skipped unrecognized zip entries: {}.",
                report.unknown.join(", ")
            ),
        );
    }
}

/// Shared preflight: refuse while the game runs, or if no valid game directory
/// is selected. The manifest requests administrator rights up front (Windows
/// shows the UAC prompt before this process even starts), so no runtime
/// writability probe or elevated relaunch is needed here.
fn ensure_ready(parent: &Frame, game_path: &Path) -> bool {
    if !detect::validate_game_path(game_path) {
        MessageDialog::builder(
            parent,
            "Please select a valid Across the Obelisk game directory first.",
            "Game Directory Needed",
        )
        .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconWarning)
        .build()
        .show_modal();
        return false;
    }

    if detect::is_game_running(game_path) {
        MessageDialog::builder(
            parent,
            "Across the Obelisk appears to be running.\n\nClose the game, then try again.",
            "Game Is Running",
        )
        .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconWarning)
        .build()
        .show_modal();
        return false;
    }

    true
}

fn update_state(
    status: &StaticText,
    install_btn: &Button,
    install_file_btn: &Button,
    uninstall_btn: &Button,
    game_path: &Path,
    state: &Rc<RefCell<State>>,
    log: &TextCtrl,
) {
    let has_valid_path = detect::validate_game_path(game_path);
    let mod_installed = detect::is_mod_installed(game_path);
    let installed_version = detect::installed_mod_version(game_path);

    install_file_btn.enable(has_valid_path);
    uninstall_btn.enable(has_valid_path && mod_installed);

    if mod_installed {
        log_append(
            log,
            &format!(
                "Installed version: {}",
                installed_version.as_deref().unwrap_or("unknown")
            ),
        );
    }
    if has_valid_path && !detect::is_bepinex_installed(game_path) {
        log_append(
            log,
            &format!(
                "BepInEx is not installed — version {} will be installed with the mod.",
                paths::BEPINEX_VERSION
            ),
        );
    }

    let borrow = state.borrow();
    if let Some(info) = borrow.release.as_ref() {
        let latest = &info.tag_name;
        if !has_valid_path {
            install_btn.enable(false);
            return;
        }
        if !mod_installed {
            install_btn.set_label("Install");
            install_btn.enable(true);
            status.set_label(&format!("Ready to install version {}.", latest));
        } else if version::is_up_to_date(installed_version.as_deref(), latest) {
            install_btn.set_label("Reinstall");
            install_btn.enable(true);
            status.set_label(&format!("ObeliskAccess is up to date (version {}).", latest));
        } else {
            install_btn.set_label("Update");
            install_btn.enable(true);
            status.set_label(&format!(
                "Update available: {} → {}",
                installed_version.as_deref().unwrap_or("unknown"),
                latest
            ));
        }
    }
}

fn log_append(log: &TextCtrl, msg: &str) {
    let current = log.get_value();
    if current.is_empty() {
        log.set_value(msg);
    } else {
        log.set_value(&format!("{}\n{}", current, msg));
    }
}
