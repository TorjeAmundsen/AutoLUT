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

Calibration works by displaying 39 known colors on your console and screenshotting each one. Because AutoLUT knows exactly which color is being displayed, it can measure precisely how your capture chain distorts colors and compute the correction. Three ways to display the colors:

- **AutoLUT Palette** (Wii Homebrew Channel): a homebrew app that displays the colors fullscreen - no game required. Download `AutoLUT-Palette-<version>.zip` from [releases](/../../releases/latest) and extract it to the root of your SD card.
- **AutoLUT Palette** (N64 with a flashcart): a [libdragon](https://libdragon.dev) ROM that displays the colors fullscreen with exact 8-bit output (32-bit framebuffer, no dithering or VI filtering - none of the RGBA5551 quantization a game's renderer goes through). Download `AutoLUT-Palette-<version>.z64` from [releases](/../../releases/latest) and boot it from your flashcart.
- **gz savestates** (Ocarina of Time on N64 or Wii VC): [gz](https://github.com/glankk/gz) savestates that fill the entire screen with each color.

## How to Use

> When screenshotting, screenshot the **raw capture source**, not your processed output. In OBS, right-click your capture source and use **Screenshot (Source)** with **all filters turned off**. Scaling/crop filters are okay, just make sure no color altering filters are enabled. Do not edit or re-encode the files afterwards.
>
> Strongly recommended: bind a hotkey to **Screenshot Selected Source** (OBS Settings → Hotkeys) and keep your capture source selected - 39 screenshots through the right-click menu is a good way to lose your mind.

1. Get the calibration colors onto your console:
   - **AutoLUT Palette (Wii)**: extract `AutoLUT-Palette-<version>.zip` from [releases](/../../releases/latest) to the root of your SD card and launch it from the Homebrew Channel.
   - **AutoLUT Palette (N64)**: put `AutoLUT-Palette-<version>.z64` from [releases](/../../releases/latest) on your flashcart's SD card and boot it.
   - **gz**: the savestates are bundled in the `savestates/` folder next to the executable (also downloadable as a separate zip from releases). Copy the folder matching your game version - `lut_gzs_1.0` or `lut_gzs_1.2` - to your SD card. The savestates require [gz](https://github.com/glankk/gz) **0.3.7 or newer**.
2. Display each color and screenshot it. With gz, load each savestate; with AutoLUT Palette, step through the colors with LEFT/RIGHT (A also advances, HOME/Start exits). There are 39 colors; capture them in any order with any filenames - AutoLUT detects which color each screenshot shows automatically. The game HUD or the palette app's corner label is fine, but keep the center of the screen clear: no watches or other overlays.
3. Open AutoLUT, click **Load images...** and select all your screenshots.
4. Click **Generate LUT**. Each screenshot gets matched to its color (shown as a swatch in the list); problems are reported per screenshot.
5. Check the result with **Show Corrected Image** - the corrected preview replicates OBS's Apply LUT filter exactly, so what you see is what OBS will render.
6. Click **Save LUT.png**.
7. In OBS: right-click your capture source, **Filters**, add **Apply LUT**, and select your saved `LUT.png`.

### Capture requirements

- Set up OBS before capturing - mismatched settings distort the capture before AutoLUT ever sees it, and incorrect settings **will** force you to re-take all captures if you want optimal and accurate results, so follow these closely:
  - **Settings -> Advanced -> Video**: set **Color Space** to **Rec. 709** and **Color Range** to **Limited**, since this is what modern streaming sites expect.
  - Capture source **Properties**: set **Color Space** to **Rec. 601** if that option exists, since this is the color space the Wii and N64 output.
  - Capture source **Properties**: set **Resolution/FPS Type** to **Custom** and **Resolution** to **720x480**. Some capture card drivers (Elgato, for example) otherwise force their own color range conversion on top of OBS's, doubling any range mismatch. 720x480 is correct even for the N64: NTSC signal timings are fixed, so capture cards digitize any NTSC source to 720x480 regardless of the console's internal resolution.
- All 9 gray colors (including black and white) are required; at least 20 of the 39 colors total must be identified. More colors = better correction. If any colors are outliers, it's most likely a settings issue. In most cases a correct setup will match all 39, even on fairly messed up capture feeds.
- AutoLUT accepts any capture resolution - the Custom 720x480 setting above is about keeping the driver's hands off the color range, not about what AutoLUT needs.
- If AutoLUT warns about washed-out or crushed colors, your capture device and OBS disagree on color range (full vs limited). Fix it in the capture source's **Properties** - set **Color Range** to 'Partial' for washed-out captures or 'Full' for crushed ones - then re-capture. Calibrating on a crushed capture loses shadow/highlight detail permanently, so always fix this first. It's possible for your capture to simply be crushed without it being a color range issue, but people mismatch their color range settings fairly often.
- A capture marked **excluded as outlier** (orange) was identified but disagreed with what all your other captures say about your capture chain, so it did not influence the LUT. One or two are harmless; re-capture them for maximum quality. Many outliers means something changed mid-capture (settings, input, lighting) - re-capture the whole set.
- Grays are held to a stricter standard: if the fit would leave any gray capture visibly tinted, AutoLUT refuses to generate the LUT. This almost always means an OBS or capture device settings mismatch (see above) - fix the settings and re-capture everything.

### Cropping and scaling your game (optional)

Not relevant to AutoLUT itself, but recommended regardless: to crop and scale a 4:3 game optimally, never use OBS' transform features (drag to scale, alt-drag to crop) - use filters for everything, ordered:

1. **Apply LUT**
2. **Crop/Pad**
3. **Scaling/Aspect Ratio**

Set **Crop/Pad** per game with the game running, since games render at different resolutions (basically none use 640x480 or 320x240).

Set **Scaling/Aspect Ratio** to the 4:3 resolution that fills your canvas vertically - 1440x1080 on a 1920x1080 canvas - not just "4:3", with scale filtering on **Area**. Point also works for a really pixelated/harsh look, at the cost of uneven pixel row/column widths; never use the other scale filtering options here.

## How the color fill savestates work

If you're curious about how the gz calibration savestates produce a known screen-fill color, see [FILLSCREEN.md](FILLSCREEN.md).

## Building from Source

Requires .NET 10 SDK. Release builds use Native AOT compilation.

```sh
# Build for your current OS
./build.ps1

# Build all platforms (uses Docker for cross-OS AOT)
./build.ps1 --all

# Zip only the calibration savestates
./build.ps1 --savestates

# Build the AutoLUT Palette Wii homebrew (uses Docker with devkitPro)
./build.ps1 --wii

# Build the AutoLUT Palette N64 ROM (uses Docker with libdragon)
./build.ps1 --n64
```

The Linux cross-compilation, the Wii homebrew build, and N64 ROM build all require Docker to be running.

Run the tests with `dotnet test`. The calibration savestates are generated from per-version templates by `savestates/generate_states.py` (pass `-v 1.0` or `-v 1.2` to regenerate a single set).
