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

## Build

You need a Rust toolchain (1.88 or newer, from [rustup](https://rustup.rs)).

```sh
cargo build --release
./target/release/d3skcommander
```

- **Linux**: install the usual GUI development packages first. On Debian or Ubuntu:
  `sudo apt install libxkbcommon-dev libwayland-dev libgl1-mesa-dev libx11-dev libxcursor-dev libxrandr-dev libxi-dev`.
- **macOS**: Xcode command-line tools (`xcode-select --install`).
- **Windows**: the MSVC build tools (rustup offers to install them).

Run the gate (format, lint, tests) with `./check.sh`.

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

What is not done yet: [docs/ROADMAP.md](docs/ROADMAP.md).

## License

MIT, see [LICENSE](LICENSE).
