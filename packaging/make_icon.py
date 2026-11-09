"""Builds the d3skcommander app icons (window, taskbar, tray, installers) from the brand symbol:
the white slash and the two amber panels on a transparent background, at every size (no tile:
the owner wants the slash visible and no dark square). The slash carries a thin anthracite edge
so it stays visible on a light taskbar. The shapes are the `symbole` group of
`d3skcommander_logo/d3skcommander-icone.svg`, drawn 8x larger then reduced.
Dev-only: `uv run --with pillow python packaging/make_icon.py`; the outputs are committed, so
building or packaging the app never runs this."""

from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent / "icons"
WHITE, AMBER, EDGE = (255, 255, 255, 255), (255, 176, 32, 255), (42, 43, 48, 255)
CLEAR = (0, 0, 0, 0)

# Symbol units (the SVG group before its transform): x from -5 to 128, y from 15 to 100.
# The slash is wider than in the logo (26 units, not 16) so it stays visible at 16 px.
SLASH = [(19.38, 15), (45.38, 15), (21.0, 100), (-5.0, 100)]
LEFT = [(50.0, 15), (84.0, 15), (59.62, 100), (25.62, 100)]
RIGHT = [(94.0, 15), (128.0, 15), (103.62, 100), (69.62, 100)]
RIGHT_HOLE = [(100.5, 25), (115.76, 25), (97.12, 90), (81.86, 90)]


def render(size):
    big = size * 8
    scale = big * 0.9 / 133
    dx = (big - 133 * scale) / 2 + 5 * scale
    dy = (big - 85 * scale) / 2 - 15 * scale

    def at(points):
        return [(dx + x * scale, dy + y * scale) for x, y in points]

    image = Image.new("RGBA", (big, big), CLEAR)
    draw = ImageDraw.Draw(image)
    edge = max(4, round(big * 0.018))
    draw.polygon(at(SLASH), fill=WHITE, outline=EDGE, width=edge)
    draw.polygon(at(LEFT), fill=AMBER)
    draw.polygon(at(RIGHT), fill=AMBER)
    draw.polygon(at(RIGHT_HOLE), fill=CLEAR)
    return image.resize((size, size), Image.LANCZOS)


def main():
    OUT.mkdir(exist_ok=True)
    for size in (32, 64, 128, 256, 512, 1024):
        render(size).save(OUT / f"{size}x{size}.png")

    ico = [render(s) for s in (16, 24, 32, 48, 64, 128, 256)]
    ico[-1].save(OUT / "icon.ico", sizes=[i.size for i in ico], append_images=ico[:-1])

    icns = [render(s) for s in (16, 32, 64, 128, 256, 512)]
    render(1024).save(OUT / "icon.icns", append_images=icns)


if __name__ == "__main__":
    main()
