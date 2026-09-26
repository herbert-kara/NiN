"""Generate readable status icons, retaining the existing NI monogram.

Run explicitly after changing the palette; Pillow is needed only by artwork tooling.
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
COLORS = [(51, 153, 204), (147, 51, 234), (220, 38, 38), (22, 128, 61)]
SIZES = [(s, s) for s in (16, 24, 32, 48, 64, 128, 256)]


def generate():
    with Image.open(ROOT / 'v2rayN/v2rayN/Resources/NotifyIcon1.ico') as source:
        base = source.convert('RGBA')
    for number, color in enumerate(COLORS, 1):
        im = base.copy()
        for y in range(im.height):
            for x in range(im.width):
                r, g, b, a = base.getpixel((x, y))
                # Preserve the high-contrast white monogram and its antialiasing.
                whiteness = max(0.0, min(1.0, (min(r, g, b) - 165) / 80))
                im.putpixel((x, y), (*[round(c * (1 - whiteness) + 245 * whiteness) for c in color], a))
        for folder in ('v2rayN/v2rayN/Resources', 'v2rayN/v2rayN.Desktop/Assets'):
            im.save(ROOT / folder / f'NotifyIcon{number}.ico', sizes=SIZES)


if __name__ == '__main__':
    generate()
