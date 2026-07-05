#!/usr/bin/env python3
"""Generate one gz savestate (.gzs) per PALETTE.txt entry from a template.

One template exists per supported OoT version (deadbeef-<version>.gzs); outputs
land in lut_gzs_<version>/. Template must have envCtx.screenFillColor set to a
unique sentinel, preceded by the fillScreen enable byte (0x01). We locate
`01 <sentinel>` (5 bytes, unique), then overwrite only the 4 RGBA bytes that
follow the 0x01. File length is never changed, so the state_meta `size` field
stays valid and no checksum exists to fix.
"""

import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PALETTE = os.path.join(HERE, "PALETTE.txt")
SENTINEL = bytes([0x01, 0xDE, 0xAD, 0xBE, 0xEF])  # fillScreen=01 + RGBA sentinel
VERSIONS = ["1.0", "1.2"]


def read_palette():
    entries = []
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
            entries.append((hex_rgba, name, rgba))
    return entries


def generate(version, entries):
    template = os.path.join(HERE, f"deadbeef-{version}.gzs")
    data = open(template, "rb").read()

    # locate the authoritative envCtx.screenFillColor via the 5-byte pattern
    i = data.find(SENTINEL)
    if i == -1:
        sys.exit(f"sentinel {SENTINEL.hex()} not found in {template}")
    if data.find(SENTINEL, i + 1) != -1:
        sys.exit("sentinel not unique - pick a rarer value and re-export template")
    color_off = i + 1  # skip the fillScreen 0x01; RGBA is the next 4 bytes
    print(f"{version}: template {len(data)} bytes; fillScreen@0x{i:X}, RGBA@0x{color_off:X}")

    outdir = os.path.join(HERE, f"lut_gzs_{version}")
    os.makedirs(outdir, exist_ok=True)

    width = len(str(len(entries)))
    for idx, (hex_rgba, name, rgba) in enumerate(entries, 1):
        out = bytearray(data)
        out[color_off : color_off + 4] = rgba  # overwrite RGBA only
        assert len(out) == len(data)  # length unchanged -> size field valid
        fname = f"{idx:0{width}d} {name}.gzs"
        with open(os.path.join(outdir, fname), "wb") as of:
            of.write(out)
        print(f"  {hex_rgba} -> lut_gzs_{version}/{fname}")

    return len(entries)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "-v", "--version", choices=VERSIONS,
        help="generate only this OoT version (default: all)",
    )
    args = parser.parse_args()

    versions = [args.version] if args.version else VERSIONS
    entries = read_palette()

    made = sum(generate(version, entries) for version in versions)
    print(f"done: {made} savestates written")


if __name__ == "__main__":
    main()
