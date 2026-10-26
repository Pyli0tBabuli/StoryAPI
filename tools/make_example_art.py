"""Generates the placeholder art used by the StoryAPI example mod.

Written with the standard library only so the example stays reproducible without
any image tooling installed. Replace these with real artwork; the point is only
that StoryAPI accepts any PNG of any size.
"""

import math
import struct
import sys
import zlib
from pathlib import Path


def write_png(path, width, height, pixel, alpha=False):
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter type: none
        for x in range(width):
            color = pixel(x, y, width, height)
            raw += bytes(color[:3])
            if alpha:
                raw.append(color[3] if len(color) > 3 else 255)

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    color_type = 6 if alpha else 2
    header = struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", header)
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))

    Path(path).write_bytes(png)
    print(f"{path}  {width}x{height}  {len(png)} bytes")


def classroom(x, y, w, h):
    """A warm room gradient with a lighter band where a wall meets the floor."""
    wall = y / h
    floor = 0.62

    if wall < floor:
        t = wall / floor
        r = int(236 - 26 * t)
        g = int(222 - 18 * t)
        b = int(198 - 10 * t)
    else:
        t = (wall - floor) / (1 - floor)
        r = int(150 - 40 * t)
        g = int(126 - 30 * t)
        b = int(104 - 22 * t)

    # Soft vignette towards the edges.
    dx = (x / w - 0.5) * 2
    dy = (y / h - 0.5) * 2
    vignette = 1.0 - 0.18 * min(1.0, math.hypot(dx, dy))

    # Window light from the upper left.
    glow = max(0.0, 1.0 - math.hypot(x / w - 0.18, y / h - 0.2) * 1.5) * 26

    return (
        max(0, min(255, int(r * vignette + glow))),
        max(0, min(255, int(g * vignette + glow))),
        max(0, min(255, int(b * vignette + glow))),
    )


def june(x, y, w, h):
    """A simple flat-shaded figure, drawn as an ellipse plus a head.

    The background is transparent; saving this as RGB gives a white rectangle.
    """
    cx, cy = w / 2, h * 0.86
    rx, ry = w * 0.42, h * 0.40

    if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
        return (236, 118, 168, 255)      # hair

    hx, hy, hr = w / 2, h * 0.20, min(w, h) * 0.13
    if ((x - hx) / hr) ** 2 + ((y - hy) / (hr * 1.25)) ** 2 <= 1.0:
        return (250, 226, 205, 255)      # face

    if hx - hr * 0.45 <= x <= hx + hr * 0.45 and hy - hr * 0.15 <= y <= hy + hr * 0.1:
        return (40, 44, 62, 255)         # eyes

    return (0, 0, 0, 0)                  # transparent


if __name__ == "__main__":
    target = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    target.mkdir(parents=True, exist_ok=True)
    write_png(target / "classroom.png", 1920, 1080, classroom)
    write_png(target / "june.png", 512, 1024, june, alpha=True)