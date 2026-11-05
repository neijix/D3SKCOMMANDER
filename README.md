# d3skcommander

A cross-platform two-panel (orthodox) file manager for Linux, macOS and Windows, written in C#
on .NET 10 with [Avalonia](https://avaloniaui.net) 12 and its Fluent theme, which follows the
operating system's light or dark setting.

## Status

The app is being rebuilt in C# (version 0.2.1, in progress). The first version, written in
Rust, is no longer in this tree; its last release is v0.1.x on the releases page and its code
stays in the git history.

What works today:

- the main window: the menu bar, the function-key bar and the status line;
- both panels list a local folder, with a path bar, a folder chooser, and keyboard navigation
  (Enter opens a folder, Backspace or Ctrl+PgUp goes up, Tab switches panel);
- copy, move, new folder and delete (F5–F8) run as background jobs, with a progress line,
  a conflict dialog and a failure report;
- an English or French interface (from the system language);
- dialogs and tool windows, the system folder chooser;
- the viewer (F3: text, hex and image modes) and the editor (F4, Shift+F4 for a new file),
  which asks Save, Don't save or Cancel before closing with unsaved changes;
- in the core library, with tests: local file listing and sorting, wildcard masks, the panel and
  tab model (history, selection, columns), background jobs with progress, cancel and retry,
  and file operations (copy, move, delete, new folder; an overwrite writes through a temporary
  file beside the target and swaps it in, so a failed or cancelled copy keeps the old file).

Not wired into the window yet: search, and the other add-ons (archives, synchronize, FTP, network drives, the remaining tools).
What is planned beyond that: [docs/ROADMAP.md](docs/ROADMAP.md).

## Install

Download the archive for your system from the
[releases page](https://github.com/neijix/D3SKCOMMANDER/releases), unpack it and run the
program inside (`D3skCommander.exe` on Windows, `D3skCommander` elsewhere). Nothing else is
needed: the .NET runtime is included.

| System | File |
|---|---|
| Windows (x64), portable | `d3skcommander-<version>-windows-x86_64.zip` |
| Windows (x64), setup | `d3skcommander-<version>-windows-x86_64-setup.exe` |
| Linux (x64) | `d3skcommander-<version>-linux-x86_64.tar.gz` |
| macOS (Apple silicon) | `d3skcommander-<version>-macos-aarch64.tar.gz` |

The setup installs for the current user (no administrator) and asks where to keep settings.
The proposed folder is `config` inside the install folder. That choice is written to
`config-location.txt` next to the program; an upgrade leaves an existing choice alone.
The programs are not signed yet: Windows SmartScreen asks once (More info, then Run anyway);
on macOS, right-click the program, then Open. Linux and macOS installers (.deb, AppImage, .dmg)
are not built yet.

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

The sources are in `csharp/`: `src/D3skCommander.Core` holds every rule and has no UI
dependency; `src/D3skCommander.App` is the Avalonia window layer; each has its test project under
`csharp/tests/`.

### Packages

```sh
csharp/packaging/publish.sh win-x64     # dist/d3skcommander-<version>-windows-x86_64.zip
csharp/packaging/publish.sh linux-x64   # dist/d3skcommander-<version>-linux-x86_64.tar.gz
csharp/packaging/publish.sh osx-arm64   # dist/d3skcommander-<version>-macos-aarch64.tar.gz
csharp/packaging/setup-windows.sh       # dist/d3skcommander-<version>-windows-x86_64-setup.exe
```

Each archive is a self-contained build: one executable with Avalonia's native Skia and HarfBuzz
libraries beside it, plus a `.sha256` file. Any of them builds from any OS. The setup needs NSIS 3
(`makensis`) and packs the Windows publish; it builds that publish first when the folder is
not already there. The version is in `csharp/Directory.Build.props`.

Pushing a tag `v<version>` (that version) runs `.github/workflows/release.yml`, which runs the
gate, builds the three archives and the Windows setup, and publishes the release.
`gh workflow run release.yml --ref master` builds them without publishing. The logo and app icon come from the brand kit in
`d3skcommander_logo/`; `packaging/make_icon.py` (Python with Pillow) turns its PNGs into
`packaging/icons/`.

### CI

There is no per-push CI: `./check.sh` runs locally before every commit. The same gate on macOS
and Windows runs on GitHub on demand (`gh workflow run check-desktop.yml`). Both workflows have
a daily cap (`.github/ci-quota.sh`, UTC day): at most 2 macOS jobs a day across workflows and 4
runs a day per workflow. Change the caps with repository variables
(`gh variable set CI_MAX_MACOS_PER_DAY --body 3`, `CI_MAX_RUNS_PER_DAY`); skip them once with
`-f force=true`.

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
| Ctrl+M | Multi-rename | Ctrl+N | FTP |
| Ctrl+U | Swap panels | Ctrl+H | Hidden files |
| Ctrl+F1 / F2 / Ctrl+Shift+F1 | Brief / Full / Thumbnails | Ctrl+F3…F6 | Sort by name, ext, date, size |
| + / - / * | Select / Unselect group / Invert | Ins / Space | Mark |

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

MIT, see [LICENSE](LICENSE).
