#!/usr/bin/env python3
"""Generate the coloured reputation flags used by the per-config flagged column.

Three small flags are embedded into ServiceLib so both UIs can show the verdict
of the anti-fraud check without depending on emoji rendering:

  nin_flag_clean.png    green   - IP looks like an ordinary connection
  nin_flag_flagged.png  red     - IP listed as proxy / VPN / hosting
  nin_flag_unknown.png  grey    - no verdict yet, or the check could not run

Colours are chosen to stay legible on both the light WPF theme and the dark
Avalonia theme: a saturated border, a pale body, and a dark masthead.
"""

from __future__ import annotations

import struct
import zlib
from pathlib import Path

WIDTH = 20
HEIGHT = 14
# 1px transparent margin so the flag never touches neighbouring column content.
BORDER = 1

STATES = {
    "clean": ((34, 120, 60), (198, 240, 206), (16, 74, 36)),
    "flagged": ((176, 32, 32), (248, 206, 206), (104, 18, 18)),
    "unknown": ((96, 100, 108), (226, 228, 232), (58, 62, 68)),
}

OUT_DIR = Path(__file__).resolve().parents[2] / "v2rayN" / "ServiceLib" / "Resources" / "Flags"


def blank_rgba() -> list[list[tuple[int, int, int, int]]]:
    return [[(0, 0, 0, 0)] * WIDTH for _ in range(HEIGHT)]


def draw_flag(border: tuple[int, int, int], body: tuple[int, int, int], masthead: tuple[int, int, int]):
    """Draw a flag: solid masthead on the left, pale body, 1px border."""
    pixels = blank_rgba()
    right = WIDTH - BORDER
    bottom = HEIGHT - BORDER
    masthead_end = max(BORDER + 1, WIDTH // 5)

    for y in range(BORDER, bottom):
        for x in range(BORDER, right):
            if y < BORDER + 2:
                pixels[y][x] = (*masthead, 255)
            elif x < masthead_end:
                pixels[y][x] = (*masthead, 255)
            else:
                pixels[y][x] = (*body, 255)

    # Border last so it always wins over the fills.
    for x in range(BORDER, right):
        pixels[BORDER][x] = (*border, 255)
        pixels[bottom - 1][x] = (*border, 255)
    for y in range(BORDER, bottom):
        pixels[y][BORDER] = (*border, 255)
        pixels[y][right - 1] = (*border, 255)

    return pixels


def write_png(path: Path, pixels) -> None:
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter type 0 (None)
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    header = struct.pack(">2I5B", WIDTH, HEIGHT, 8, 6, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for state, colours in STATES.items():
        out = OUT_DIR / f"nin_flag_{state}.png"
        write_png(out, draw_flag(*colours))
        print(f"wrote {out.relative_to(OUT_DIR.parents[3])}")


if __name__ == "__main__":
    main()
