//! Launch external programs on files: "open selected with" (once per file or all at once),
//! the default application, and shell command lines. Children are spawned detached and
//! never waited on by the caller.

use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum LaunchMode {
    OncePerFile,
    AllAtOnce,
}

/// Split a command line on whitespace; `"double quotes"` group words (and are removed),
/// `""` gives an empty argument.
pub fn split_command_line(cmd: &str) -> Vec<String> {
    let mut args = Vec::new();
    let mut cur = String::new();
    let mut in_token = false;
    let mut quoted = false;
    for c in cmd.chars() {
        match c {
            '"' => {
                quoted = !quoted;
                in_token = true;
            }
            c if c.is_whitespace() && !quoted => {
                if in_token {
                    args.push(std::mem::take(&mut cur));
                    in_token = false;
                }
            }
            c => {
                cur.push(c);
                in_token = true;
            }
        }
    }
    if in_token {
        args.push(cur);
    }
    args
}

/// Programs and arguments to run. `%s` inside an argument is replaced by the file(s):
/// an argument containing it is repeated once per file (so `%s` alone becomes one argument
/// per file). Without `%s` the files are appended. `OncePerFile` gives one command per
/// file, `AllAtOnce` a single command. The program (first word) is never substituted.
pub fn build_commands(
    command: &str,
    files: &[PathBuf],
    mode: LaunchMode,
) -> Vec<(String, Vec<String>)> {
    let mut words = split_command_line(command);
    if words.is_empty() || files.is_empty() {
        return Vec::new();
    }
    let program = words.remove(0);
    let has_placeholder = words.iter().any(|w| w.contains("%s"));
    let names: Vec<String> = files
        .iter()
        .map(|f| f.to_string_lossy().into_owned())
        .collect();
    let groups: Vec<&[String]> = match mode {
        LaunchMode::OncePerFile => names.chunks(1).collect(),
        LaunchMode::AllAtOnce => vec![names.as_slice()],
    };
    groups
        .into_iter()
        .map(|group| {
            let mut args = Vec::new();
            for w in &words {
                if w.contains("%s") {
                    args.extend(group.iter().map(|n| w.replace("%s", n)));
                } else {
                    args.push(w.clone());
                }
            }
            if !has_placeholder {
                args.extend(group.iter().cloned());
            }
            (program.clone(), args)
        })
        .collect()
}

/// Run a command detached: no stdio, own process group on Unix, reaped by a helper thread
/// so no zombie is left behind. The caller never blocks.
fn spawn_detached(mut cmd: Command, cwd: &Path) -> std::io::Result<()> {
    cmd.current_dir(cwd)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    #[cfg(unix)]
    {
        use std::os::unix::process::CommandExt;
        cmd.process_group(0);
    }
    let mut child = cmd.spawn()?;
    std::thread::spawn(move || {
        let _ = child.wait();
    });
    Ok(())
}

/// Spawn the commands of [`build_commands`] in `cwd`; never waits for them.
/// Returns how many were started.
pub fn launch(
    command: &str,
    files: &[PathBuf],
    mode: LaunchMode,
    cwd: &Path,
) -> Result<usize, String> {
    let commands = build_commands(command, files, mode);
    if commands.is_empty() {
        return Err("nothing to launch: empty command or no files".to_string());
    }
    let mut started = 0;
    for (program, args) in commands {
        let mut cmd = Command::new(&program);
        cmd.args(&args);
        spawn_detached(cmd, cwd).map_err(|e| format!("{program}: {e}"))?;
        started += 1;
    }
    Ok(started)
}

/// Open a file or folder with the system's default application (detached).
pub fn open_with_default(path: &Path) -> Result<(), String> {
    open::that_detached(path).map_err(|e| format!("{}: {e}", path.display()))
}

/// Run a command line through the shell (`cmd /C` on Windows, `sh -c` elsewhere), detached.
pub fn run_in_shell(command_line: &str, cwd: &Path) -> Result<(), String> {
    if command_line.trim().is_empty() {
        return Err("empty command line".to_string());
    }
    let cmd = shell_command(command_line);
    spawn_detached(cmd, cwd).map_err(|e| format!("{command_line}: {e}"))
}

#[cfg(windows)]
fn shell_command(command_line: &str) -> Command {
    use std::os::windows::process::CommandExt;
    let mut cmd = Command::new("cmd");
    // the line goes to cmd verbatim, no re-quoting of embedded quotes
    cmd.arg("/C").raw_arg(command_line);
    cmd
}

#[cfg(not(windows))]
fn shell_command(command_line: &str) -> Command {
    let mut cmd = Command::new("sh");
    cmd.arg("-c").arg(command_line);
    cmd
}

#[cfg(test)]
mod tests {
    use super::*;

    fn files(names: &[&str]) -> Vec<PathBuf> {
        names.iter().map(PathBuf::from).collect()
    }

    #[test]
    fn split_honours_quotes() {
        assert_eq!(
            split_command_line(r#"viewer --opt "a b" c"#),
            ["viewer", "--opt", "a b", "c"]
        );
        assert_eq!(
            split_command_line(r#""C:\Program Files\x.exe" %s"#),
            [r"C:\Program Files\x.exe", "%s"]
        );
        assert_eq!(split_command_line(r#"a "" b"#), ["a", "", "b"]);
        assert_eq!(split_command_line(r#"--f="x y""#), ["--f=x y"]);
        assert!(split_command_line("   ").is_empty());
    }

    #[test]
    fn once_per_file_with_placeholder() {
        let cmds = build_commands(
            "edit -f %s -q",
            &files(&["a", "b"]),
            LaunchMode::OncePerFile,
        );
        assert_eq!(cmds.len(), 2);
        assert_eq!(
            cmds[0],
            (
                "edit".to_string(),
                vec!["-f".into(), "a".into(), "-q".into()]
            )
        );
        assert_eq!(cmds[1].1, ["-f", "b", "-q"]);
    }

    #[test]
    fn all_at_once_expands_placeholder_to_separate_args() {
        let cmds = build_commands("tool %s", &files(&["a b", "c"]), LaunchMode::AllAtOnce);
        assert_eq!(
            cmds,
            vec![("tool".to_string(), vec!["a b".to_string(), "c".to_string()])]
        );
    }

    #[test]
    fn no_placeholder_appends_files() {
        let cmds = build_commands("tool --x", &files(&["a", "b"]), LaunchMode::AllAtOnce);
        assert_eq!(cmds[0].1, ["--x", "a", "b"]);
        let cmds = build_commands("tool", &files(&["a", "b"]), LaunchMode::OncePerFile);
        assert_eq!(cmds.len(), 2);
        assert_eq!(cmds[1].1, ["b"]);
    }

    #[test]
    fn placeholder_inside_an_argument() {
        let cmds = build_commands("t --file=%s", &files(&["a", "b"]), LaunchMode::AllAtOnce);
        assert_eq!(cmds[0].1, ["--file=a", "--file=b"]);
    }

    #[test]
    fn empty_inputs_give_no_commands() {
        assert!(build_commands("", &files(&["a"]), LaunchMode::AllAtOnce).is_empty());
        assert!(build_commands("tool", &[], LaunchMode::OncePerFile).is_empty());
    }
}
