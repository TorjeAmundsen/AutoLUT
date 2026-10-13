# Download

Download the [latest release](/../../releases/latest) for your platform. The builds are self-contained Native AOT executables - no .NET runtime required.

## Or use it in your browser

AutoLUT also runs entirely in your browser (WebAssembly) - nothing to install: **https://TorjeAmundsen.github.io/AutoLUT/**

Same app, same results. Notes for the web version:

- The calibration apps or savestates are downloadable from the page itself (**Downloads** button or via the guide).
- First load fetches ~12 MB (cached afterwards).

# AutoLUT

<img width="960" height="240" alt="autolut" src="https://github.com/user-attachments/assets/982f0f37-fa42-47ba-85a4-d0a3550d3c33" />

AutoLUT generates an OBS-compatible `LUT.png` that color-corrects your Wii or N64 capture to match the console's true output colors. Apply it with OBS's built-in **Apply LUT** filter and your stream/recording colors match what the game actually outputs - no manual tweaking of curves or sliders.

Calibration works by displaying 39 known colors on your console and screenshotting each one. Because AutoLUT knows exactly which color is being displayed, it can measure precisely how your capture chain distorts colors and compute the correction. Three ways to display the colors:

- **AutoLUT Palette** (Wii Homebrew Channel): a homebrew app that displays the colors fullscreen - no game required. Download `AutoLUT-Palette-Wii-<version>.zip` from [releases](/../../releases/latest) and extract it to the root of your SD card.
- **AutoLUT Palette** (N64 with a flashcart): a [libdragon](https://libdragon.dev) ROM that displays the colors fullscreen from a 32-bit framebuffer with no dithering or VI filtering, so none of the RGBA5551 quantization a game's renderer goes through. The N64's video DAC takes 7 bits per channel, which puts every palette and setup-check level within one 8-bit code of its value. Download `AutoLUT-Palette-N64-<version>.z64` from [releases](/../../releases/latest) and boot it from your flashcart.
- **gz savestates** (legacy, not recommended): [gz](https://github.com/glankk/gz) savestates for Ocarina of Time on N64 or Wii VC that fill the entire screen with each color. If you can run gz, you can run AutoLUT Palette: gz on Wii VC already needs the Homebrew Channel, and gz on N64 already needs a flashcart. Use AutoLUT Palette instead. It's quicker to step through, more accurate on N64, and has a color range check screen. The savestates stay available for anyone who still needs them.

## How to use

Follow the step-by-step guide in [TUTORIAL.md](TUTORIAL.md): [Wii](TUTORIAL.md#wii) or [N64](TUTORIAL.md#n64). The same steps are in the app itself under **Open guide**.

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

# Format the Wii/N64 C sources (clang-format, style in .clang-format)
./build.ps1 --format

# Check C formatting without changing files (what CI runs)
./build.ps1 --check-format
```

The Linux cross-compilation, the Wii homebrew build, the N64 ROM build and the C formatter all require Docker to be running.

Run the tests with `dotnet test`. The calibration savestates are generated from per-version templates by `savestates/generate_states.py` (pass `-v 1.0` or `-v 1.2` to regenerate a single set).
