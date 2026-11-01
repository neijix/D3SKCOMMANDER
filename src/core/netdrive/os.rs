//! The per-OS mount and unmount. Kept thin: everything that can be tested without a
//! server (parsing, scripts, answers) lives in the parent module.

use std::path::{Path, PathBuf};

use super::{Credentials, Share};

/// Mounts `share` and gives the local folder to browse it (the typed sub-folder
/// included). `letter` (`Z:`) maps a drive letter on Windows; elsewhere it is ignored.
/// Blocks until the OS answers: call it from a background job.
pub fn connect(
    share: &Share,
    creds: &Credentials,
    letter: Option<&str>,
) -> Result<PathBuf, String> {
    imp::connect(share, creds, letter).map(|base| share.inside(&base))
}

/// Unmounts the network drive `path` is on; gives what was disconnected.
pub fn disconnect(path: &Path) -> Result<String, String> {
    imp::disconnect(path)
}

/// Runs `program args`, writing `input` to its standard input; its standard output, or
/// its error message (stderr, else stdout) when it fails.
#[cfg(unix)]
fn run(program: &str, args: &[&str], input: &str) -> Result<String, String> {
    use std::io::Write;
    use std::process::{Command, Stdio};
    let mut child = Command::new(program)
        .args(args)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|e| match e.kind() {
            std::io::ErrorKind::NotFound => format!("{program} not found"),
            _ => format!("{program}: {e}"),
        })?;
    if let Some(mut stdin) = child.stdin.take() {
        // A tool that exits before reading everything is not an error here.
        let _ = stdin.write_all(input.as_bytes());
    }
    let out = child
        .wait_with_output()
        .map_err(|e| format!("{program}: {e}"))?;
    let stdout = String::from_utf8_lossy(&out.stdout).into_owned();
    if out.status.success() {
        return Ok(stdout);
    }
    let stderr = String::from_utf8_lossy(&out.stderr);
    let msg = if stderr.trim().is_empty() {
        stdout.trim()
    } else {
        stderr.trim()
    };
    Err(if msg.is_empty() {
        format!("{program} failed ({})", out.status)
    } else {
        msg.to_string()
    })
}

#[cfg(target_os = "linux")]
mod imp {
    use super::*;

    const NO_GIO: &str = "gio not found: install GVfs (package gvfs / gvfs-backends) to mount \
                          network drives, or mount the share yourself and open its folder";

    fn gio(args: &[&str], input: &str) -> Result<String, String> {
        super::run("gio", args, input).map_err(|e| {
            if e == "gio not found" {
                NO_GIO.to_string()
            } else {
                e
            }
        })
    }

    pub fn connect(
        share: &Share,
        creds: &Credentials,
        _letter: Option<&str>,
    ) -> Result<PathBuf, String> {
        let uri = share.uri();
        // "Already mounted" is an error for gio but fine here: the folder is all we need.
        let mounted = gio(&["mount", &uri], &crate::core::netdrive::gio_answers(creds));
        if let Err(e) = &mounted
            && e == NO_GIO
        {
            return Err(e.clone());
        }
        let info = gio(&["info", &uri], "");
        match (
            info.ok()
                .as_deref()
                .and_then(crate::core::netdrive::gio_local_path),
            mounted,
        ) {
            (Some(path), _) => Ok(path),
            (None, Err(e)) => Err(e),
            (None, Ok(_)) => Err(format!(
                "{uri} is mounted but has no local folder (is gvfsd-fuse running?)"
            )),
        }
    }

    pub fn disconnect(path: &Path) -> Result<String, String> {
        let uri = crate::core::netdrive::gvfs_uri(path)
            .ok_or_else(|| format!("{} is not on a network drive", path.display()))?;
        gio(&["mount", "-u", &uri], "")?;
        Ok(uri)
    }
}

#[cfg(target_os = "macos")]
mod imp {
    use super::*;
    use crate::core::netdrive::{mac_mount_containing, mac_mount_point, mac_mount_script};

    pub fn connect(
        share: &Share,
        creds: &Credentials,
        _letter: Option<&str>,
    ) -> Result<PathBuf, String> {
        if !matches!(
            share.scheme.as_str(),
            "smb" | "afp" | "nfs" | "ftp" | "dav" | "davs"
        ) {
            return Err(format!("{}:// is not supported by macOS", share.scheme));
        }
        // The script (and the password in it) goes on stdin, never on the command line.
        super::run("osascript", &["-"], &mac_mount_script(share, creds))?;
        let mounts = super::run("mount", &[], "")?;
        mac_mount_point(&mounts, share)
            .ok_or_else(|| format!("{} was mounted but its folder was not found", share.uri()))
    }

    pub fn disconnect(path: &Path) -> Result<String, String> {
        let mounts = super::run("mount", &[], "")?;
        let point = mac_mount_containing(&mounts, path)
            .ok_or_else(|| format!("{} is not on a network drive", path.display()))?;
        let point_text = point.to_string_lossy().into_owned();
        super::run("diskutil", &["unmount", &point_text], "")?;
        Ok(point_text)
    }
}

#[cfg(windows)]
mod imp {
    use super::*;
    use windows_sys::Win32::NetworkManagement::WNet::{
        CONNECT_TEMPORARY, NETRESOURCEW, RESOURCETYPE_DISK, WNetAddConnection2W,
        WNetCancelConnection2W,
    };

    fn wide(text: &str) -> Vec<u16> {
        text.encode_utf16().chain(std::iter::once(0)).collect()
    }

    fn error_text(code: u32) -> String {
        std::io::Error::from_raw_os_error(code as i32).to_string()
    }

    pub fn connect(
        share: &Share,
        creds: &Credentials,
        letter: Option<&str>,
    ) -> Result<PathBuf, String> {
        if share.scheme != "smb" {
            return Err(format!(
                "{}:// is not supported on Windows: use \\\\server\\share",
                share.scheme
            ));
        }
        let mut remote = wide(&share.unc());
        let mut local = letter.map(wide);
        let user = if creds.user.is_empty() {
            None
        } else if creds.domain.is_empty() {
            Some(wide(&creds.user))
        } else {
            Some(wide(&format!("{}\\{}", creds.domain, creds.user)))
        };
        let password = (!creds.password.is_empty()).then(|| wide(&creds.password));
        let resource = NETRESOURCEW {
            dwType: RESOURCETYPE_DISK,
            lpLocalName: local
                .as_mut()
                .map_or(std::ptr::null_mut(), |l| l.as_mut_ptr()),
            lpRemoteName: remote.as_mut_ptr(),
            ..Default::default()
        };
        let ptr = |v: &Option<Vec<u16>>| v.as_ref().map_or(std::ptr::null(), |v| v.as_ptr());
        // SAFETY: every pointer is a NUL-terminated UTF-16 buffer (or null) that outlives the call.
        let code = unsafe {
            WNetAddConnection2W(&resource, ptr(&password), ptr(&user), CONNECT_TEMPORARY)
        };
        // 1219: already connected to this server with these credentials (or others) — the
        // share is reachable, which is what matters for browsing it.
        if code != 0 && !(code == 1219 && letter.is_none()) {
            return Err(error_text(code));
        }
        Ok(match letter {
            Some(l) => PathBuf::from(format!("{l}\\")),
            None => PathBuf::from(format!("{}\\", share.unc())),
        })
    }

    pub fn disconnect(path: &Path) -> Result<String, String> {
        let root = crate::core::netdrive::windows_root(&path.to_string_lossy())
            .ok_or_else(|| format!("{} is not on a network drive", path.display()))?;
        let name = wide(&root);
        // SAFETY: `name` is a NUL-terminated UTF-16 buffer that outlives the call.
        let code = unsafe { WNetCancelConnection2W(name.as_ptr(), 0, 0) };
        if code != 0 {
            return Err(error_text(code));
        }
        Ok(root)
    }
}

#[cfg(not(any(target_os = "linux", target_os = "macos", windows)))]
mod imp {
    use super::*;

    pub fn connect(_: &Share, _: &Credentials, _: Option<&str>) -> Result<PathBuf, String> {
        Err("network drives are not supported on this system".into())
    }

    pub fn disconnect(_: &Path) -> Result<String, String> {
        Err("network drives are not supported on this system".into())
    }
}
