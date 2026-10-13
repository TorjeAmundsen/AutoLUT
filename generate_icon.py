#!/usr/bin/env python3
"""Generate the AutoLUT icon: a triforce with red, green and blue triangles.

Renders a supersampled master, box-downsamples to all needed sizes, and writes:
  src/AutoLUT.App/Assets/icon.png   (256x256, Avalonia window icon)
  src/AutoLUT.App/Assets/app.ico    (16..256 multi-size, Windows exe icon)
"""

import math
import os
import struct
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
OUTDIR = os.path.join(HERE, "src", "AutoLUT.App", "Assets")

MASTER = 2048          # supersampled render size
ICON_PNG_SIZE = 256
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]

RED = (255, 40, 40)
GREEN = (40, 220, 40)
BLUE = (40, 90, 255)


def triforce_triangles(size):
    """Vertices of the three sub-triangles, sized to fill the canvas with a small margin."""
    margin = size * 0.04
    side = size - 2 * margin
    height = side * math.sqrt(3) / 2
    top_y = (size - height) / 2
    cx = size / 2

    apex = (cx, top_y)
    left = (cx - side / 2, top_y + height)
    right = (cx + side / 2, top_y + height)

    def mid(a, b):
        return ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)

    m_tl, m_tr, m_b = mid(apex, left), mid(apex, right), mid(left, right)
    return [
        ((apex, m_tl, m_tr), RED),
        ((m_tl, left, m_b), GREEN),
        ((m_tr, m_b, right), BLUE),
    ]


def rasterize(size):
    """Scanline-fill the triangles into an RGBA byte buffer (transparent background)."""
    buffer = bytearray(size * size * 4)
    for vertices, color in triforce_triangles(size):
        edges = [(vertices[i], vertices[(i + 1) % 3]) for i in range(3)]
        min_y = max(int(min(v[1] for v in vertices)), 0)
        max_y = min(int(max(v[1] for v in vertices)) + 1, size)
        for y in range(min_y, max_y):
            cy = y + 0.5
            xs = []
            for (x0, y0), (x1, y1) in edges:
                if (y0 <= cy < y1) or (y1 <= cy < y0):
                    xs.append(x0 + (cy - y0) * (x1 - x0) / (y1 - y0))
            if len(xs) < 2:
                continue
            start = max(int(math.ceil(min(xs) - 0.5)), 0)
            end = min(int(math.floor(max(xs) - 0.5)), size - 1)
            for x in range(start, end + 1):
                i = (y * size + x) * 4
                buffer[i] = color[0]
                buffer[i + 1] = color[1]
                buffer[i + 2] = color[2]
                buffer[i + 3] = 255
    return buffer


def downsample(source, source_size, target_size):
    """Box-filter RGBA with premultiplied-alpha averaging (clean edges on transparency)."""
    factor = source_size // target_size
    result = bytearray(target_size * target_size * 4)
    area = factor * factor
    for ty in range(target_size):
        for tx in range(target_size):
            r = g = b = a = 0
            for sy in range(ty * factor, (ty + 1) * factor):
                base = (sy * source_size + tx * factor) * 4
                for i in range(base, base + factor * 4, 4):
                    alpha = source[i + 3]
                    r += source[i] * alpha
                    g += source[i + 1] * alpha
                    b += source[i + 2] * alpha
                    a += alpha
            j = (ty * target_size + tx) * 4
            if a > 0:
                result[j] = round(r / a)
                result[j + 1] = round(g / a)
                result[j + 2] = round(b / a)
            result[j + 3] = round(a / area)
    return result


def write_png(path, size, rgba):
    def chunk(typ, data):
        c = struct.pack(">I", len(data)) + typ + data
        return c + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF)

    raw = b""
    stride = size * 4
    for y in range(size):
        raw += b"\x00" + bytes(rgba[y * stride:(y + 1) * stride])
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as fp:
        fp.write(png)
    return png


def png_bytes(size, rgba):
    import io
    path = io.BytesIO()

    def chunk(typ, data):
        c = struct.pack(">I", len(data)) + typ + data
        return c + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF)

    raw = b""
    stride = size * 4
    for y in range(size):
        raw += b"\x00" + bytes(rgba[y * stride:(y + 1) * stride])
    path.write(b"\x89PNG\r\n\x1a\n")
    path.write(chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)))
    path.write(chunk(b"IDAT", zlib.compress(raw, 9)))
    path.write(chunk(b"IEND", b""))
    return path.getvalue()


def write_ico(path, images):
    """images: list of (size, png_bytes). Windows accepts PNG-compressed ICO entries."""
    header = struct.pack("<HHH", 0, 1, len(images))
    entries = b""
    blobs = b""
    offset = len(header) + 16 * len(images)
    for size, blob in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        blobs += blob
        offset += len(blob)
    with open(path, "wb") as fp:
        fp.write(header + entries + blobs)


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    print(f"rendering {MASTER}x{MASTER} master...")
    master = rasterize(MASTER)

    ico_images = []
    for size in ICO_SIZES:
        rgba = downsample(master, MASTER, size)
        ico_images.append((size, png_bytes(size, rgba)))
        if size == ICON_PNG_SIZE:
            write_png(os.path.join(OUTDIR, "icon.png"), size, rgba)
            print(f"wrote icon.png ({size}x{size})")

    write_ico(os.path.join(OUTDIR, "app.ico"), ico_images)
    print(f"wrote app.ico ({', '.join(str(s) for s in ICO_SIZES)})")


if __name__ == "__main__":
    main()
