#!/bin/sh
# Builds autolut-palette.z64 inside the ghcr.io/dragonminded/libdragon container.
# The image ships only the bare mips64-elf toolchain; the libdragon library,
# n64.mk and host tools must be installed into $N64_INST first.
set -e

if [ ! -f "$N64_INST/include/n64.mk" ]; then
    git clone --depth 1 --branch trunk https://github.com/DragonMinded/libdragon.git /tmp/libdragon
    make -C /tmp/libdragon install tools-install -j"$(nproc)"
fi

make -C "$(dirname "$0")"
