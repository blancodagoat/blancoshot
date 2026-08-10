#!/usr/bin/env python3
"""Generates the multi-resolution .ico files in assets/.

Pure stdlib: rasterises the mark by supersampling, encodes each size as a PNG
and packs them into an ICO. PNG-compressed ICO entries are understood by
Windows Vista and later, so every size can use the same encoder.

Usage: python3 tools/make-icons.py
"""

import os
import struct
import zlib

SIZES = (16, 32, 48, 256)
SS = 4  # supersampling factor per axis

BG = (0x12, 0x10, 0x0E)
TEXT = (0xF2, 0xEC, 0xE2)
ACCENT = (0xE0, 0x91, 0x3F)


def _rect(x0, y0, x1, y1):
    return lambda x, y: x0 <= x <= x1 and y0 <= y <= y1


def _circle(cx, cy, r):
    return lambda x, y: (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def _rounded(x0, y0, x1, y1, r):
    def inside(x, y):
        if not (x0 <= x <= x1 and y0 <= y <= y1):
            return False
        cx = min(max(x, x0 + r), x1 - r)
        cy = min(max(y, y0 + r), y1 - r)
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r
    return inside


def shapes(size, with_background, dot_colour):
    """Corner crop-marks plus a centre dot, in unit space.

    Arm thickness and dot radius have a floor expressed in device pixels so the
    mark stays legible at 16px instead of dissolving into grey.
    """
    t = max(0.085, 2.0 / size)      # bracket thickness
    arm = max(0.30, 5.0 / size)     # bracket arm length
    i = 0.13                        # inset from the edge
    dot = max(0.115, 1.7 / size)

    layers = []
    if with_background:
        layers.append((_rounded(0.02, 0.02, 0.98, 0.98, 0.20), BG))

    j = 1.0 - i
    for shape in (
        _rect(i, i, i + arm, i + t),                 # top-left
        _rect(i, i, i + t, i + arm),
        _rect(j - arm, i, j, i + t),                 # top-right
        _rect(j - t, i, j, i + arm),
        _rect(i, j - t, i + arm, j),                 # bottom-left
        _rect(i, j - arm, i + t, j),
        _rect(j - arm, j - t, j, j),                 # bottom-right
        _rect(j - t, j - arm, j, j),
    ):
        layers.append((shape, ACCENT))

    layers.append((_circle(0.5, 0.5, dot), dot_colour))
    return layers


def render(size, layers):
    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                y = (py + (sy + 0.5) / SS) / size
                for sx in range(SS):
                    x = (px + (sx + 0.5) / SS) / size
                    hit = None
                    for shape, colour in layers:
                        if shape(x, y):
                            hit = colour
                    if hit is not None:
                        acc[0] += hit[0]
                        acc[1] += hit[1]
                        acc[2] += hit[2]
                        acc[3] += 255.0
            n = SS * SS
            a = acc[3] / n
            if a <= 0.0:
                row += b"\x00\x00\x00\x00"
                continue
            covered = acc[3] / 255.0
            row += bytes((
                int(round(acc[0] / covered)),
                int(round(acc[1] / covered)),
                int(round(acc[2] / covered)),
                int(round(a)),
            ))
        rows.append(bytes(row))
    return rows


def png(size, rows):
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def ico(images):
    count = len(images)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries, blobs = b"", b""
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    return header + entries + blobs


def build(path, with_background, dot_colour):
    images = [(s, png(s, render(s, shapes(s, with_background, dot_colour)))) for s in SIZES]
    with open(path, "wb") as fh:
        fh.write(ico(images))
    print("wrote %s (%d bytes)" % (path, os.path.getsize(path)))


def main():
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir, "assets")
    root = os.path.normpath(root)
    os.makedirs(root, exist_ok=True)
    # Tray variants are named for the theme they are drawn *for*: the light
    # variant is the pale mark that sits on a dark taskbar, and vice versa.
    build(os.path.join(root, "tray-light.ico"), False, TEXT)
    build(os.path.join(root, "tray-dark.ico"), False, BG)
    build(os.path.join(root, "app.ico"), True, TEXT)


if __name__ == "__main__":
    main()
