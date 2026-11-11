# D3SKCOMMANDER — brand kit

Part of the D3SK family. A keyboard-driven two-panel file manager.

The D3SKCOMMANDER name and these logo files are **not** covered by the MIT license of the code:
see [LICENSE](../LICENSE).

## Logo
Wordmark `D3SKCOMMANDER` in Archivo 800, upper case, tracking −5 %, **outlined** (no font needed).
Symbol `/▮▯`: the family's −16° slash plus two panels slanted at the same angle — the active
panel filled, the target panel outlined. Corner radius 3 units.

## Files
- `d3skcommander-couleur-fond-sombre.svg` / `-fond-clair.svg` / `-noir.svg` / `-blanc.svg` — full
  logo for a dark background, a light background, black, white (layers `wordmark`, `symbole`)
- `d3skcommander-icone.svg` — app icon (anthracite tile, white slash, amber panels)
- `d3skcommander-icone-clair.svg` — light-background variant
- `d3skcommander-icone-petit.svg` — under 48 px: the two panels only, no slash
- `symbole-seul.svg` — the symbol without the wordmark
- `png/` — logos at 800 / 2400 px, icons from 16 to 1024 px

## Which file to use
- **Illustrator / Figma / print** → the `.svg` files
- **Web, docs, slides** → `png/d3skcommander-*-800px.png` (2400 px for high-DPI screens)
- **App icon at large sizes** → `png/icone-1024px.png`
- **Favicon / taskbar** → `png/icone-16px.png` / `icone-32px.png`

## In the app
| Where | Source |
|---|---|
| Windows executable and window icon (`csharp/src/D3skCommander.App/D3skCommander.App.csproj`, `ApplicationIcon`) | `packaging/icons/icon.ico` |
| Packages (`release.json`): Windows setup `icon.ico`, macOS bundle `icon.icns`, Linux desktop entry `256x256.png` | `packaging/icons/` |

`packaging/icons/` is made by `uv run --with pillow python packaging/make_icon.py`, which draws the
`symbole` group of `d3skcommander-icone.svg` (white slash, amber panels, transparent background)
at every size. New symbol → update the shapes in `make_icon.py`, rerun it, commit
`packaging/icons/`.

## Colors
- Amber `#FFB020` — accent, symbol, active panel
- Dark amber `#B86E00` — accent on a light background
- Anthracite `#2A2B30` — tile, icon
- Charcoal `#121216` — main background
- Off-white `#F4F4F7` — wordmark on dark
- Black `#111318` — print

## Typography
Archivo 800 (wordmark) · Space Grotesk (interface) · JetBrains Mono (paths, F1–F10 shortcuts).

## Rules
- The filled panel is always on the left (active), the outlined one on the right (target).
- Under 48 px, use the simplified icon.
