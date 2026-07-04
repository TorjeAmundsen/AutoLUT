# Download

Download the [latest release](/../../releases/latest) for your platform. The builds are self-contained Native AOT executables - no .NET runtime required.

## Or use it in your browser

AutoLUT also runs entirely in your browser (WebAssembly) - nothing to install: **https://TorjeAmundsen.github.io/AutoLUT/**

Same app, same results. Notes for the web version:

- The calibration savestates are downloadable from the page itself (**Download savestates** button).
- On Firefox/Safari, **Save LUT.png** delivers the file as a regular browser download instead of a save dialog.
- First load fetches ~10 MB (cached afterwards).

# AutoLUT

<img width="960" height="240" alt="autolut" src="https://github.com/user-attachments/assets/982f0f37-fa42-47ba-85a4-d0a3550d3c33" />

AutoLUT generates an OBS-compatible `LUT.png` that color-corrects your Wii capture to match the console's true output colors. Apply it with OBS's built-in **Apply LUT** filter and your stream/recording colors match what the game actually outputs - no manual tweaking of curves or sliders.

Calibration uses [gz](https://github.com/glankk/gz) savestates that fill the entire screen with known colors. Because AutoLUT knows exactly which color each savestate displays, it can measure precisely how your capture chain distorts colors and compute the correction.

## How to Use

> When screenshotting, screenshot the **raw capture source**, not your processed output. In OBS, right-click your capture source and use **Screenshot (Source)** with **all filters turned off**. Do not edit or re-encode the files afterwards.

1. Get the calibration savestates: they are bundled in the `savestates/` folder next to the executable (also downloadable as a separate zip from releases). Copy the folder matching your game version - `lut_gzs_1.0` or `lut_gzs_1.2` - to your SD card.
2. Load each savestate in gz and screenshot the solid-color fill it displays. There are 39 colors; capture them in any order with any filenames - AutoLUT detects which color each screenshot shows automatically. The game HUD (hearts, buttons, counters, minimap) is fine, but keep the center of the screen clear: no watches or other overlays.
3. Open AutoLUT, click **Add Screenshots...** and select all your screenshots.
4. Click **Generate LUT**. Each screenshot gets matched to its color (shown as a swatch in the list); problems are reported per screenshot.
5. Check the result with **Show Corrected Image** - the corrected preview replicates OBS's Apply LUT filter exactly, so what you see is what OBS will render.
6. Click **Save LUT.png**.
7. In OBS: right-click your capture source, **Filters**, add **Apply LUT**, and select your saved `LUT.png`.

### Capture requirements

- All 9 gray savestates (including black and white) are required; at least 20 of the 39 colors total must be identified. More colors = better correction.
- Any capture resolution works.
- Each color should be captured exactly once - duplicates are rejected.
- If AutoLUT warns about washed-out or crushed colors, your capture device and OBS disagree on color range (full vs limited). Fix it in the capture source's **Properties** - set **Color Range** to 'Partial' for washed-out captures or 'Full' for crushed ones - then re-capture. Calibrating on a crushed capture loses shadow/highlight detail permanently, so always fix this first.
- A capture marked **excluded as outlier** (orange) was identified but disagreed with what all your other captures say about your capture chain, so it did not influence the LUT. One or two are harmless; re-capture them for maximum quality. Many outliers means something changed mid-capture (settings, input, lighting) - re-capture the whole set.

## Building from Source

Requires .NET 10 SDK. Release builds use Native AOT compilation.

```sh
# Build for your current OS
./build.ps1

# Build all platforms (uses Docker for cross-OS AOT)
./build.ps1 --all

# Zip only the calibration savestates
./build.ps1 --savestates
```

The Linux cross-compilation requires Docker to be running.

Run the tests with `dotnet test`. The calibration savestates are generated from per-version templates by `savestates/generate_states.py` (pass `-v 1.0` or `-v 1.2` to regenerate a single set).
