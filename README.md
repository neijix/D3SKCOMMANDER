# d3skcommander

A cross-platform two-panel (orthodox) file manager written in Rust with
[egui](https://github.com/emilk/egui). It runs on Linux, macOS and Windows from a single binary.
The tools other file managers ship as add-ons are built in.

## Features

- **Two panels** with tabs, back/forward history, a hotlist (favorite folders), drive buttons
  showing free space, an editable path bar, and full, brief or thumbnail views with sortable,
  configurable columns.
- **File operations** (copy, move, rename, delete to the trash or permanently, new folder or
  file) run as background jobs with progress. An overwrite dialog compares both files and
  offers overwrite, skip, rename the new one, overwrite all older, and apply to all.
- **Selection**: by wildcard group, by the same extension, invert, Ins/Space. A **quick filter**
  (Ctrl+S) and a **branch view** (Ctrl+B: every file below the folder, flat) narrow the list.
- **Quick view** (Ctrl+Q) shows the file under the cursor in the other panel: text, images, hex.
- **Lister** (F3): text, hex, images and DBF tables, with search and a text cursor.
  **Editor** (F4): a built-in editor with find and replace, unless you set your own editor.
- **Archives**: you browse, extract and pack ZIP, 7Z, TAR and TAR.GZ natively. RAR, ARJ, LZH,
  CAB, ACE, UC2, GZ, BZ2, XZ, ISO and others are read through an installed `7z` or `unrar`, if one
  is on `PATH`.
- **Search** (Alt+F7): by name (wildcards or regex), by content (text or regex), by size and by
  date, inside archives too. Results can go to a panel.
- **Compare**: compare two files side by side, then edit and save them (compare editor).
  Compare panels marks the files that differ between the two sides.
- **Synchronize directories**: symmetric or one-way, by date, size or content, including
  empty folders. You review and change each action before it runs.
- **Multi-rename** (Ctrl+M): name and extension masks (`[N]`, `[E]`, `[C]` counter, date
  fields), search and replace with regex, case changes, a live preview, and undo.
- **Network drives** (Net menu, or the 💾 drive list): connect a shared folder —
  `\\server\share`, `smb://server/share`, and on Linux and macOS also `sftp://`, `ftp://`,
  `dav(s)://`, `afp://`, `nfs://` — with a user, domain and password, then browse it in the
  panel. The operating system does the mount (a mapped drive letter on Windows, Finder's
  "Connect to Server" on macOS, GVFS / `gio mount` on Linux), so the share works in other
  programs too. The password is never saved nor put on a command line; recent addresses are
  remembered. On Linux this needs GVFS (`gvfs-backends` on Debian/Ubuntu, usually installed
  with the desktop); connected shares also show in the drive list.
- **Folder picker**: the 📂 button of each path bar, the drive list and every folder field
  (copy/move target, extract, split, encrypt, search, synchronize) open the system's folder
  chooser (the XDG desktop portal on Linux).
- **FTP / FTPS** (Ctrl+N): connection manager, transfers in both directions, and a password
  manager encrypted with a master password.
- **Built-in tools**:
  - split and combine files (`.001`, `.002`, … with a CRC file);
  - create and verify checksums (CRC32/SFV, MD5, SHA-1, SHA-256);
  - back up the selection to a dated ZIP;
  - shift file dates (AddTime), re-date files in a sequence (ReDate), set a fixed date (touch);
  - a disk catalog, searchable offline;
  - encrypt and decrypt files with a passphrase ([age](https://age-encryption.org));
  - open with…, user commands in the Tools menu, a terminal here, and a command line.
- **Operations log**, an English or French interface, and a **portable mode**.
- **Updates itself** (Help → Check for updates…, and a quiet check at start-up that can be
  turned off in Configuration): it downloads the new version for the way it was installed,
  checks its SHA-256, installs it and restarts.

## Install

Download the file for your system from the
[releases page](https://github.com/neijix/d3skcommander/releases):

| System | File | Install |
|---|---|---|
| Linux (Debian, Ubuntu…) | `…-linux-x86_64.deb` | `sudo apt install ./d3skcommander-*.deb` |
| Linux (any) | `…-linux-x86_64.AppImage` | `chmod +x` it and run it |
| macOS (Apple silicon) | `…-macos-aarch64.dmg` | open it, drag the app to Applications |
| Windows | `…-windows-x86_64-setup.exe` | run it (no administrator rights needed) |
| Any, no installer | `…-linux-x86_64.tar.gz`, `…-windows-x86_64.zip` | unpack and run the program |

The macOS app and the Windows setup are not signed yet: macOS says the app "cannot be
opened" — right-click it, then Open (or `xattr -dr com.apple.quarantine
/Applications/d3skcommander.app`); Windows SmartScreen asks once — More info, then Run anyway.

How each install updates itself: the `.deb` reinstalls the new package with administrator
rights (`pkexec`), the AppImage, the macOS app and a plain program replace themselves, and the
Windows setup runs the new setup silently. A build from source does not update itself
(`git pull`, then build again).

## Build

You need a Rust toolchain (1.95 or newer, from [rustup](https://rustup.rs)).

```sh
cargo build --release
./target/release/d3skcommander
```

- **Linux**: install the usual GUI development packages first. On Debian or Ubuntu:
  `sudo apt install libxkbcommon-dev libwayland-dev libgl1-mesa-dev libx11-dev libxcursor-dev libxrandr-dev libxi-dev`.
- **macOS**: Xcode command-line tools (`xcode-select --install`).
- **Windows**: the MSVC build tools (rustup offers to install them).

Or build and start it in one step: `./start.sh` (macOS, Linux) or `start.bat` (Windows).

Run the gate (format, lint, tests) with `./check.sh`.

### Packages

The installers are built by [cargo-packager](https://github.com/crabnebula-dev/cargo-packager)
(`cargo install cargo-packager --locked`), configured in `Cargo.toml`
(`[package.metadata.packager]`):

```sh
cargo packager --release --formats deb,appimage   # Linux
cargo packager --release --formats app,dmg        # macOS
cargo packager --release --formats nsis           # Windows
```

Files land in `target/packages/`. Pushing a tag `v<version>` (the version in `Cargo.toml`) runs
`.github/workflows/release.yml`, which builds all of them, names them
`d3skcommander-<version>-<os>-<arch>…` (the names the updater looks for) and publishes the
release. The app icon is drawn by `packaging/make_icon.py` (Python with Pillow).

## Configuration

Settings live in `d3skcommander.json`:

- **Portable mode**: if a `d3skcommander.json` file sits next to the executable, everything
  (settings, passwords, log) is kept in that folder. Create an empty `{}` file there to turn
  it on.
- Otherwise the file is in the user config folder: `~/.config/d3skcommander` on Linux,
  `~/Library/Application Support/d3skcommander` on macOS, `%APPDATA%\d3skcommander` on Windows.

## Keyboard

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
| Ctrl+M | Multi-rename | Ctrl+N | FTP |
| Ctrl+U | Swap panels | Ctrl+H | Hidden files |
| Ctrl+F1 / F2 / Ctrl+Shift+F1 | Brief / Full / Thumbnails | Ctrl+F3…F6 | Sort by name, ext, date, size |
| + / - / * | Select / Unselect group / Invert | Ins / Space | Mark |

F1 in the app lists every binding.

## Development notes

- `D3SK_SCREENSHOT=/tmp/shot.png` makes the app save a screenshot after two seconds and quit.
  `D3SK_SCREENSHOT_CMD=Sync` runs a command first (any command name, such as `Ftp`,
  `Settings` or `Search`).
- On some Linux Wayland compositors a window that is not on screen gets no frames. To take
  such a screenshot, run it through XWayland: `WAYLAND_DISPLAY= D3SK_SCREENSHOT=… d3skcommander`.

- `tests/ui_layout.rs` runs the real app headless (no GPU, on every CI OS) and checks from
  its accessibility tree that panels, bars, tool windows and dialogs stay on screen at
  several window sizes and scales.
- The network mount is tested against a real share only on demand:
  `D3SK_TEST_SHARE=smb://server/share D3SK_TEST_USER=… D3SK_TEST_PASSWORD=… cargo test connects_to_a_real_share -- --ignored`
  (also `D3SK_TEST_DOMAIN`).

What is not done yet: [docs/ROADMAP.md](docs/ROADMAP.md).

## License

MIT, see [LICENSE](LICENSE). Icons: [Material Symbols](https://fonts.google.com/icons)
(Apache-2.0), through the `egui_material_icons` crate.
