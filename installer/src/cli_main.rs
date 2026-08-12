//! Console-subsystem twin of the installer, exposing the same text-mode flow as
//! `ObeliskAccessInstaller.exe --cli`.
//!
//! Why a second binary rather than a flag on the first: the GUI binary is built with
//! `#![windows_subsystem = "windows"]`, and cmd.exe / PowerShell do not WAIT for a GUI-subsystem
//! process. `ObeliskAccessInstaller.exe --cli` therefore returns to the prompt immediately, the
//! shell takes back the console's stdin, and the prompt loop reads EOF and exits after printing
//! its banner — the text mode appears to do nothing. `AttachConsole` cannot fix that; only a
//! console-subsystem image makes the shell wait. This binary has no `windows_subsystem` attribute,
//! so it is a console image and behaves like any other command-line tool.
//!
//! The modules are shared with the GUI binary by path (`core` is self-contained and `cli` depends
//! only on `core`), so there is exactly one implementation of the install logic.

#[path = "cli.rs"]
mod cli;
#[path = "core/mod.rs"]
mod core;

fn main() {
    cli::run();
}
