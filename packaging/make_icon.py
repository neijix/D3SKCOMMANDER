"""Draws the d3skcommander app icon (two panels on a gradient tile) at every size the
installers need. Dev-only: `python3 packaging/make_icon.py` (needs Pillow); the PNGs are
committed, so building or packaging the app never runs this."""

from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).parent / "icons"
BIG = 1024
TOP, BOTTOM = (64, 92, 230), (22, 180, 170)


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def master():
    s = BIG * 2  # supersampled, scaled down at the end for smooth edges
    grad = Image.new("RGBA", (s, s))
    d = ImageDraw.Draw(grad)
    for y in range(s):
        d.line([(0, y), (s, y)], fill=lerp(TOP, BOTTOM, y / s) + (255,))
    mask = Image.new("L", (s, s), 0)
    m = s * 0.06
    ImageDraw.Draw(mask).rounded_rectangle([m, m, s - m, s - m], radius=s * 0.2, fill=255)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    pad, gap, top, bottom = s * 0.2, s * 0.05, s * 0.26, s * 0.74
    w = (s - 2 * pad - gap) / 2
    for i in range(2):
        x0 = pad + i * (w + gap)
        fill = (255, 255, 255, 245) if i == 0 else (255, 255, 255, 150)
        d.rounded_rectangle([x0, top, x0 + w, bottom], radius=s * 0.045, fill=fill)
        line = TOP + (255,) if i == 0 else (255, 255, 255, 255)
        for k in range(4):
            y = top + s * 0.07 + k * s * 0.09
            length = w * (0.62 if k % 2 else 0.78)
            if i == 0 and k == 1:  # the cursor row
                d.rounded_rectangle(
                    [x0 + s * 0.025, y - s * 0.03, x0 + w - s * 0.025, y + s * 0.03],
                    radius=s * 0.02,
                    fill=BOTTOM + (255,),
                )
                continue
            d.rounded_rectangle(
                [x0 + s * 0.05, y - s * 0.012, x0 + s * 0.05 + length - s * 0.1, y + s * 0.012],
                radius=s * 0.012,
                fill=line if i == 0 else (64, 92, 230, 140),
            )
    return img.resize((BIG, BIG), Image.LANCZOS)


def main():
    OUT.mkdir(exist_ok=True)
    img = master()
    for size in (32, 64, 128, 256, 512, 1024):
        img.resize((size, size), Image.LANCZOS).save(OUT / f"{size}x{size}.png")
    img.save(OUT / "icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    img.save(OUT / "icon.icns")


if __name__ == "__main__":
    main()
