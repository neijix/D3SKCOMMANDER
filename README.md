# d3skcommander

A cross-platform two-panel (orthodox) file manager for Linux, macOS and Windows, written in C#
on .NET 10 with [Avalonia](https://avaloniaui.net) 12 and its Fluent theme, which follows the
operating system's light or dark setting.

## Status

The app is being rebuilt in C# (version 0.3.0, in progress). The first version, written in
Rust, is no longer in this tree; its last release is v0.1.x on the releases page and its code
stays in the git history.

What works today:

- the main window: the menu bar, the function-key bar and the status line;
- both panels list a local folder, with a path bar, a folder chooser, keyboard navigation,
  tabs, history, the hotlist, sorting, the full, brief and thumbnail views, folder and
  file-type icons, and the last session (tabs, folders, sort, view) reopened at start;
- selection by pattern, quick filter, branch view, names and paths copied to the clipboard;
- folder sizes: Space or Ctrl+L measures a folder, an optional setting measures every folder in
  the background, and the panel status shows marked and total bytes and the free space;
- disk usage (Shift+Alt+L): a folder's children by size with a ring chart you click to descend;
- copy, move, new folder, rename in place, delete and permanent delete (F5–F8) run as background
  jobs, with a progress line, a conflict dialog and a failure report;
- Enter on a file opens it with its default application; Open with, a terminal here, the
  command line under the panels, and your own commands (Settings) in the Tools menu;
- a drive bar above each panel (Show menu to hide it) and a drive menu (Alt+F1, Alt+F2) with
  usage bars, your usual folders, network shares and, on Linux, disks to mount or eject
  (`udisksctl`); Properties (Alt+Enter), and files dropped from the system copied into a panel;
- quick view (Ctrl+Q) of the file under the cursor in the other panel; videos play there, muted at
  first, with the [VLC](https://www.videolan.org/vlc/) already installed on the computer
  (optional: without it the preview says to install it; on Linux, the `libvlc` package);
- a Home view above the drives (the house button, or Up from a drive root): drives with how full they are, network drives and favorites in one read-only list;
- column sets: named sets of columns (name, extension, size, modified, created, accessed,
  attributes, owner, group, type) with their order and widths, chosen per tab from the header's
  right-click menu or cycled with Ctrl+F7, and edited in Settings;
- a toolbar under the menu that you fill in Settings (right-click it to customize): built-in
  commands, your own commands or programs, with separators;
- a folder tree beside each panel (Ctrl+Shift+T) that follows the panel's folder;
- favorite tabs: both panels' tabs saved under a name (Ctrl+Alt+S) and reopened from
  Show > Favorite tabs, replacing or adding to the open tabs;
- file associations: per name mask, the programs that open, view (F3) or edit (F4) a file,
  plus extra actions in the panel's right-click menu (Settings);
- search (Alt+F7) with several masks, excluded files and folders, field history, saved
  templates, a duplicate finder, and results opened as a panel tab you can copy, move or delete
  from; quick find (Ctrl+Shift+F) filters a name index of your drives as you type;
- synchronize folders, compare, multi-rename, split and join, checksums, file times,
  archives (pack, unpack, browse), encryption and the password vault, FTP and FTPS sites,
  network drives, the catalog, the operations log, the settings window (F9) and update checks;
- an English or French interface;
- the viewer (F3: text, hex and image modes) and the editor (F4, Shift+F4 for a new file),
  which asks Save, Don't save or Cancel before closing with unsaved changes.

What is planned beyond that: [docs/ROADMAP.md](docs/ROADMAP.md).

## Install

Take the file for your system from the releases page (the packages are built on the owner's
machine, see [Packages](#packages); nothing is built on GitHub). The .NET runtime is included.

| System | File |
|---|---|
| Windows (x64), setup | `d3skcommander-<version>-windows-x86_64-setup.exe` |
| Windows (x64), portable | `d3skcommander-<version>-windows-x86_64.zip` |
| macOS (Apple silicon), disk image | `d3skcommander-<version>-macos-aarch64.dmg` |
| macOS (Apple silicon), archive | `d3skcommander-<version>-macos-aarch64.tar.gz` (holds `D3skCommander.app`) |
| Linux (x64) | `d3skcommander-<version>-linux-x86_64.tar.gz` (holds `install.sh`) |

Every release also lists `SHA256SUMS` and `SHA256SUMS.sig`; the app checks that signature itself
before it installs an update.

- **Windows setup**: a wizard with a folder page: you choose where the program goes (the default
  is per user, no administrator; a folder that needs administrator rights, such as
  `Program Files`, makes Windows ask) and where settings are kept (proposed: `config` inside the
  install folder, written to `config-location.txt`; an upgrade leaves an existing choice alone).
  The zip needs no install: unpack it and run `D3skCommander.exe`. Windows SmartScreen asks once
  (More info, then Run anyway).
- **macOS**: open the `.dmg` and drag `D3skCommander.app` to any folder you like. The app is
  ad-hoc signed, not notarized, so the first launch is blocked: open System Settings > Privacy &
  Security and press Open Anyway (on macOS 14 and older, right-click the app, then Open).
- **Linux**: unpack the archive and run `./install.sh <folder>` (without a folder it asks, default
  `~/.local/share/d3skcommander`). It copies the program there and adds a menu entry and an icon;
  `uninstall.sh` removes them again.

**From v0.2.1:** that version cannot update itself (its release feed is private), so reinstall
v0.3.0 or later by hand once; from then on Update (below) works in place.

### Update and sign-in

The Update window (also checked at start unless `D3SK_NO_UPDATE_CHECK=1`) looks for a newer
release, downloads it and installs it in the folder the program already lives in. The repository
is public, so that download needs no sign-in. The release signature (`SHA256SUMS.sig`) is verified
against the public key built into the app; a bad signature refuses the update. A private release
feed still asks you to sign in once: press Sign in, the window shows a device code, you enter it
at `https://github.com/login/device`, and the token is kept in the system keychain (never in a
file). The sign-in uses the shared "D3SK Updates" GitHub App; set `D3SKCOMMANDER_GITHUB_CLIENT_ID`
to use another client ID.

If the window stays black, flickers or crashes at start (a broken GPU driver, a virtual machine,
a remote desktop), start it with `D3SK_RENDERING=software` in the environment: the window is then
drawn by the CPU, with no GPU or OpenGL needed.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). On Linux, the window also
needs an X11 session (Wayland desktops use XWayland) and these shared libraries, which any
desktop install has: `libX11`, `libXi`, `libXrandr`, `libXcursor`, `libXext`, `libICE`, `libSM`
(loaded by Avalonia) and `libfontconfig` with `libfreetype` (needed by the bundled Skia).
No GPU or OpenGL is required.

```sh
./start.sh          # build (release) and start; start.bat on Windows
./check.sh          # the gate: format check, build with warnings as errors, tests
```

`dotnet` is found on `PATH`, else in `$DOTNET_ROOT`. An optional, git-ignored `.env` at the root
can set `DOTNET_ROOT`, `DOTNET_CLI_HOME`, `NUGET_PACKAGES` and a `PATH` prefix
(`.env.example`). `./check.sh --full` adds the slow tests.

Building needs a GitHub Packages read token, see [Build token](#build-token).

The sources are in `csharp/`: `src/D3skCommander.Core` holds every rule and has no UI
dependency; `src/D3skCommander.App` is the Avalonia window layer; each has its test project under
`csharp/tests/`.

### Packages

```sh
./release.sh                  # gate, then every package into dist/ (release.bat, release.command: one click)
./release.sh --dry-run        # list the steps and artifacts, write nothing, skip the gate
./release.sh --no-setup       # skip the Windows setup;  --no-dmg skips the .dmg
./release.sh --verbose        # show each external command;  --json: machine-readable output
csharp/packaging/github-release.sh --dry-run   # publish: tag, draft upload, verify the signature, publish
```

Both scripts are thin wrappers on `d3sk-release`, the private .NET tool of the D3sk.Update
repository (restored with the [build token](#build-token)); `release.json` at the root holds the
app's settings. Other options: `--no-check` (skip the gate), `--version V` (default: `<Version>` in
`csharp/Directory.Build.props`), `--out DIR` (default `dist/`). Nothing is built or uploaded on
GitHub: `github-release.sh` is the owner's step (needs `gh` signed in) and refuses to publish when
the signature does not match the public key in `release.json`.

`dist/` (git-ignored) gets the Windows zip and setup, the macOS `.dmg` and `.tar.gz`, the Linux
`.tar.gz`, `SHA256SUMS` and `SHA256SUMS.sig`. The signing key is the PEM file named by
`D3SKCOMMANDER_RELEASE_KEY_FILE`, else `.release-keys/release-signing-key.pem` in the checkout (git-ignored;
`csharp/packaging/restore-release-key.sh` puts it back from its Bitwarden secure note, item name in
`D3SKCOMMANDER_RELEASE_KEY_ITEM`; a key that git tracks or does not ignore is refused); without
either the build stops and says so. A missing tool also stops it with its install command.
Building on Windows needs, besides the .NET SDK:

- NSIS 3: `winget install NSIS.NSIS --location <folder>`, then set `NSISDIR` to that folder.
- WSL with Ubuntu, for the `.dmg`: `wsl --install -d Ubuntu --location <folder>`, then
  `wsl -d Ubuntu -u root -- apt-get install -y build-essential cmake git zlib1g-dev libbz2-dev libssl-dev hfsprogs`.
  The libdmg-hfsplus tool is built into `.tools/libdmg-hfsplus` (git-ignored), or the folder in
  `D3SKCOMMANDER_DMG_TOOLS_DIR`.
- `rcodesign` (apple-codesign): unpack its release zip into a folder of your choice and set
  `RCODESIGN` to the full path of the executable (else it is found on `PATH`).

`release.command` / `release.bat` run `./release.sh` with the same options, open `dist/` when it
worked and wait for a key. `release.bat` needs Git for Windows, whose bash runs the script.
The logo and app icon come from `d3skcommander_logo/`; `packaging/make_icon.py` (Python with
Pillow) draws its symbol into `packaging/icons/`.

### CI

`./check.sh` runs locally before every commit; there is no per-push CI. The same gate runs on
Linux, Windows and macOS on demand, on the standard GitHub-hosted runners (`ubuntu-latest`,
`windows-latest`, `macos-latest`): `gh workflow run ci.yml`, or `-f os='["Windows"]'` to pick
the OSes, then `gh run watch`. The repository is public, so those runners are free. The workflow
stays on `workflow_dispatch` (write access required) and has no `pull_request` trigger. Packages
are never built by a workflow: `./release.sh` builds them locally.

### Build token

The shared `D3sk.*` libraries are private packages on GitHub Packages (`nuget.config` at the root
maps `D3sk.*` to `https://nuget.pkg.github.com/neijix/index.json`), and GitHub Packages asks for a
token even to read. `./check.sh` and `./release.sh` look for a token with `read:packages` once a
`D3sk.*` package is referenced, in this order, and use the first that answers (for that process only;
the token is never printed or written anywhere):

1. The environment variable `NuGetPackageSourceCredentials_github`, set to `Username=<login>;Password=<token>`.
2. The Bitwarden item `d3skhand-github-packages-read-token` in the `DEV_KEYS` folder, read with
   `bw-auto get-pass` (`$AI_CODE_ROOT/BitwardenAuto/bw-auto`).
3. The first line of the file `~/.d3skcommander-release/github-packages-read-token` (mode 0600 on
   Linux and macOS; on Windows the profile folder's own permissions protect it).
4. The token of the GitHub CLI, when it has the scope: `gh auth refresh -h github.com -s read:packages`.

The GitHub login comes from `D3SKCOMMANDER_PACKAGES_USER`, else the Bitwarden item's username, else
`gh api user`. In PowerShell, to set the first way for one session:
`$env:NuGetPackageSourceCredentials_github = "Username=<login>;Password=<token>"`.
`ci.yml` passes the repository secret `PACKAGES_READ_TOKEN` (`read:packages`) the same way, for the gate step only. The job's own token cannot read the private packages.


## Keyboard

The default keys of the first version are kept:

| Key | Action | Key | Action |
|---|---|---|---|
| Tab | Switch panel | F1 | Key list |
| F2 / Shift+F6 | Rename | F3 / F4 | View / Edit |
| Shift+F4 | New file | F5 / F6 | Copy / Move |
| F7 / F8 | New folder / Delete | Shift+F8 | Delete permanently |
| F9 | Settings | Alt+F7 | Search |
| Alt+F5 / Alt+F9 | Pack / Unpack | Alt+F1 / Alt+F2 | Drives left / right |
| Alt+Enter | Properties | Alt+← / Alt+→ | Back / Forward |
| Ctrl+D | Hotlist | Alt+↓ | History |
| Ctrl+T / Ctrl+W | New / Close tab | Ctrl+Tab | Next tab |
| Ctrl+Q | Quick view | Ctrl+S | Quick filter |
| Ctrl+B | Branch view | Ctrl+L | Folder sizes |
| Shift+Alt+L | Disk usage | Ctrl+Shift+F | Quick find |
| Ctrl+M | Multi-rename | Ctrl+N | FTP |
| Ctrl+U | Swap panels | Ctrl+H | Hidden files |
| Ctrl+F1 / F2 / Ctrl+Shift+F1 | Brief / Full / Thumbnails | Ctrl+F3…F6 | Sort by name, ext, date, size |
| Ctrl+G | Go to the file's folder | Space | Mark (a folder: measure it) |
| + / - / * | Select / Unselect group / Invert | Ins | Mark |
| Ctrl+F7 | Next column set | Ctrl+Shift+T | Folder tree |
| Ctrl+Alt+S | Save favorite tabs | Ctrl+Alt+1 | Open the first favorite tabs |

A command whose feature is not built yet shows in its menu, disabled.

## Development notes

- `D3SK_SCREENSHOT=/tmp/shot.png` makes the app save a screenshot after two seconds and quit.
  `D3SK_SCREENSHOT_CMD=Settings` runs a command first (any command name); it shoots the window or dialog that command opened.
- The App tests run on Avalonia.Headless (no window, no GPU). One test starts the real program
  with `D3SK_RENDERING=software` under `xvfb-run` (Linux; skipped when Xvfb is not installed, and
  on Windows/macOS outside CI).
- The first version of the window used an immediate-mode GUI (ImGui); Avalonia replaced it
  before the port reached parity.

## License

The code is MIT, see [LICENSE](LICENSE). The D3SKCOMMANDER name and logo
(`d3skcommander_logo/`, `packaging/icons/`) are not covered by it: ship them unchanged only with
the unmodified app, and use your own name and icon for a fork.
