#!/bin/sh
# Builds autolut-palette.z64 inside the ghcr.io/dragonminded/libdragon container.
# The image ships only the bare mips64-elf toolchain; the libdragon library,
# n64.mk and host tools must be installed into $N64_INST first.
set -e

# Pinned so a given AutoLUT tag always builds the same ROM: libdragon's display_init
# programs the VI (gamma, dither, filters), so an upstream change could silently change
# the colors the palette app scans out. Bump deliberately, together with the image digest.
LIBDRAGON_COMMIT=e356bf3f56f7afbf7e5246329562f145965cfdfc

if [ ! -f "$N64_INST/include/n64.mk" ]; then
    git init -q /tmp/libdragon
    git -C /tmp/libdragon fetch -q --depth 1 https://github.com/DragonMinded/libdragon.git "$LIBDRAGON_COMMIT"
    git -C /tmp/libdragon checkout -q FETCH_HEAD
    make -C /tmp/libdragon install tools-install -j"$(nproc)"
fi

make -C "$(dirname "$0")"
