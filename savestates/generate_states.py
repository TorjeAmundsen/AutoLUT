#!/usr/bin/env python3
"""Generate one gz savestate (.gzs) per PALETTE.txt entry from a template.

Template must have envCtx.screenFillColor set to a unique sentinel, preceded by
the fillScreen enable byte (0x01). We locate `01 <sentinel>` (5 bytes, unique),
then overwrite only the 4 RGBA bytes that follow the 0x01. File length is never
changed, so the state_meta `size` field stays valid and no checksum exists to fix.
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = os.path.join(HERE, "deadbeef.gzs")
PALETTE = os.path.join(HERE, "PALETTE.txt")
OUTDIR = os.path.join(HERE, "lut_gzs")
SENTINEL = bytes([0x01, 0xDE, 0xAD, 0xBE, 0xEF])  # fillScreen=01 + RGBA sentinel


def main():
    data = open(TEMPLATE, "rb").read()

    # locate the authoritative envCtx.screenFillColor via the 5-byte pattern
    i = data.find(SENTINEL)
    if i == -1:
        sys.exit(f"sentinel {SENTINEL.hex()} not found in {TEMPLATE}")
    if data.find(SENTINEL, i + 1) != -1:
        sys.exit("sentinel not unique - pick a rarer value and re-export template")
    color_off = i + 1  # skip the fillScreen 0x01; RGBA is the next 4 bytes
    print(f"template: {len(data)} bytes; fillScreen@0x{i:X}, RGBA@0x{color_off:X}")

    os.makedirs(OUTDIR, exist_ok=True)

    made = 0
    with open(PALETTE, "r") as fp:
        for lineno, raw in enumerate(fp, 1):
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split()
            if len(parts) != 2:
                sys.exit(
                    f"PALETTE.txt line {lineno}: expected 'RRGGBBAA name', got {line!r}"
                )
            hex_rgba, name = parts
            if len(hex_rgba) != 8:
                sys.exit(
                    f"PALETTE.txt line {lineno}: RGBA must be 8 hex digits, got {hex_rgba!r}"
                )
            try:
                rgba = bytes.fromhex(hex_rgba)
            except ValueError:
                sys.exit(f"PALETTE.txt line {lineno}: bad hex {hex_rgba!r}")

            out = bytearray(data)
            out[color_off : color_off + 4] = rgba  # overwrite RGBA only
            assert len(out) == len(data)  # length unchanged -> size field valid
            out_path = os.path.join(OUTDIR, name + ".gzs")
            with open(out_path, "wb") as of:
                of.write(out)
            print(f"  {hex_rgba} -> {name}.gzs")
            made += 1

    print(f"done: {made} savestates written")


if __name__ == "__main__":
    main()
