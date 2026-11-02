"""Builds the d3skcommander app icons the installers need from the brand kit in
`d3skcommander_logo/png/`: the full icon (anthracite tile, white slash, amber panels) from
48 px up, the simplified two-panel icon below 48 px (the kit's rule). Dev-only:
`python3 packaging/make_icon.py` (needs Pillow 10.1+); the outputs are committed, so building
or packaging the app never runs this."""

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
KIT = ROOT / "d3skcommander_logo" / "png"
OUT = Path(__file__).resolve().parent / "icons"
SMALL_BELOW = 48


def load(name):
    return Image.open(KIT / name).convert("RGBA")


def icon(size, full, small):
    """The icon at `size` px: a kit PNG when it has that size, else a LANCZOS reduction."""
    kit = KIT / f"icone-{size}px.png"
    if kit.exists():
        return load(kit.name)
    source = small if size < SMALL_BELOW else full
    return source.resize((size, size), Image.LANCZOS)


def main():
    OUT.mkdir(exist_ok=True)
    full, small = load("icone-1024px.png"), load("icone-32px.png")

    for size in (32, 64, 128, 256, 512, 1024):
        icon(size, full, small).save(OUT / f"{size}x{size}.png")

    ico = [icon(s, full, small) for s in (16, 24, 32, 48, 64, 128, 256)]
    ico[-1].save(OUT / "icon.ico", sizes=[i.size for i in ico], append_images=ico[:-1])

    icns = [icon(s, full, small) for s in (16, 32, 64, 128, 256, 512)]
    full.save(OUT / "icon.icns", append_images=icns)


if __name__ == "__main__":
    main()
