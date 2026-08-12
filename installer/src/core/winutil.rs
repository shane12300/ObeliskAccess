/// Relaunch this executable with administrator rights (UAC prompt), passing
/// `args` through. Returns true when the elevated process was started.
#[cfg(windows)]
pub fn relaunch_elevated(args: &str) -> bool {
    use std::os::windows::ffi::OsStrExt;

    let Ok(exe) = std::env::current_exe() else {
        return false;
    };
    let exe_w: Vec<u16> = exe
        .as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    let verb_w: Vec<u16> = "runas".encode_utf16().chain(std::iter::once(0)).collect();
    let args_w: Vec<u16> = args.encode_utf16().chain(std::iter::once(0)).collect();

    let result = unsafe {
        windows_sys::Win32::UI::Shell::ShellExecuteW(
            std::ptr::null_mut(),
            verb_w.as_ptr(),
            exe_w.as_ptr(),
            args_w.as_ptr(),
            std::ptr::null(),
            1, // SW_SHOWNORMAL
        )
    };
    result as usize > 32 // per ShellExecute docs, values > 32 mean success
}

#[cfg(not(windows))]
pub fn relaunch_elevated(_args: &str) -> bool {
    false
}
