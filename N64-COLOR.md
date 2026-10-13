# How the N64 framebuffer changes the fill color

AutoLUT currently assumes the color commanded through `Environment_FillScreen()` (see
[FILLSCREEN.md](FILLSCREEN.md)) is the color the console outputs. It is not. The N64 quantizes the
color on its way to the screen, so the digital image leaving the console - and therefore the
emulator reference screenshot and the capture-card input - differs from the commanded RGBA for some
values. Measured example: commanded `#ff8000` displays as `#f98000`.

This document specifies the hardware pipeline, the resulting commanded→displayed mapping, and the
code changes required so AutoLUT fits against what the console actually displays.

## The hardware pipeline

A commanded fill color passes through three stages before it leaves the console digitally:

### 1. RDP draw (8-bit precision)

`Environment_FillScreen` draws full-screen rectangles with the color as an 8-bit-per-channel
primitive color through the RDP color combiner. At this stage the color is still exact.

### 2. Framebuffer write (quantization to 5 bits)

Ocarina of Time renders to a 16-bit **RGBA5551** framebuffer: 5 bits per color channel (32 levels),
1 alpha/coverage bit. On write, the RDP applies its ordered dither ("magic square" pattern) to the
low 3 bits and truncates:

- A channel value divisible by 8 maps to its 5-bit level exactly and uniformly - no dither
  activity, every pixel identical.
- A value not divisible by 8 dithers spatially between the two adjacent 5-bit levels - the
  framebuffer is no longer a uniform field at the LSB scale.
- 255 is already at the top level (31); dither cannot push it higher, so it stores uniformly as 31.

The 8-bit alpha is irrelevant to color: it collapses to the single coverage bit (`FF` = opaque).

### 3. VI scanout (5 bits back to 8, plus filters)

The Video Interface reads the 5-bit framebuffer and produces the 8-bit digital signal fed to the
DAC. Base expansion is `v5 << 3` (so level 31 → 248, not 255). On top of that the VI applies its
filters, all of which OoT leaves enabled:

- **Dither filter (dedither)** - reconstructs sub-LSB precision by averaging the dither pattern
  over a pixel neighborhood. On a dithered mid-value field this approximately recovers the original
  8-bit average; on a uniform field it is nearly a no-op.
- **AA / divot filters** - act on edges and coverage only; a no-op in the flat center region
  AutoLUT samples.
- **VI gamma** - a hardware gamma boost games may enable. **OoT runs with it off**, proven by
  measurement: commanded 128 displays as exactly 128 (gamma would have lifted it far above).

The measured residual on the top level (248 → 249) comes from this VI filter stage. Treat the exact
displayed values as **empirical**, measured from the emulator reference, not derived from a model.

## The commanded → displayed mapping

For the flat screen-center region AutoLUT samples, per channel:

| Commanded value `v` | Displayed value `D(v)` | Status |
|---|---|---|
| `v ≡ 0 (mod 8)` (0, 8, …, 248) | `v` exactly | Verified for 0 and 128 |
| 255 | **249** | Measured (`#ff8000` → `#f98000`) |
| Other values | ≈ `v` (dither + dedither round-trip), ±1-2, spatially non-uniform | Not used by the palette |

Every calibration palette value (`CalibrationPalette`) is a multiple of 8 - the neutral ramp
(0, 32, …, 224), the 128s in the grid, and the 64/192 mids - **except 255**. So exactly one
correction is needed: any commanded 255 channel displays as ~249.

Consequences of ignoring this: the fitter is told capture-observed white corresponds to 255 when
the console only ever emitted ~249. The fitted curve stretches the top of the range by ~6/255,
over-brightening highlights in corrected footage, and every 255-channel palette entry (white plus
all 18 grid colors containing a 255 channel - nearly half the palette) carries the same systematic
reference error into the fit.

## Required code changes

The guiding rule: **the reference side of every correspondence must be the displayed color, not the
commanded color.** The commanded value remains the identity of a palette entry (file naming,
ramp keys, UI labels); the displayed value is what math consumes.

### 1. `CalibrationPalette` / `PaletteColor` - add displayed values

Add a displayed-color accessor alongside the commanded bytes:

```csharp
public sealed record PaletteColor(byte R, byte G, byte B, PaletteCategory Category)
{
    /// <summary>What the N64 actually outputs for a commanded channel value (see N64-COLOR.md).
    /// Valid only for the multiples-of-8-plus-255 values the palette uses.</summary>
    private static byte Displayed(byte v) => v == 255 ? DisplayedWhiteLevel : v;

    /// <summary>Empirical: 255 stores as 5-bit level 31, VI outputs 249. Re-measure against
    /// emulator reference screenshots if the reference emulator or its settings change.</summary>
    private const byte DisplayedWhiteLevel = 249;

    public Rgb ToDisplayedRgb() => new(Displayed(R) / 255f, Displayed(G) / 255f, Displayed(B) / 255f);
}
```

`ToRgb()` stays as-is for anything that genuinely means "commanded".

### 2. `CalibrationPipeline` - fit against displayed

`CalibrationPipeline.cs:215` builds the correspondences; switch the reference:

```csharp
correspondences.Add(new ColorCorrespondence(mean, target.ToDisplayedRgb(), weight, stdDevs[i] * stdDevs[i]));
```

Update the class summary ("Ground truth is the commanded palette color") to say displayed. The
`MeanOf(byte gray)` lookups keep using commanded bytes as keys - they are identities, not colors.

Resulting behavior: the LUT maps the capture to the console's true digital output, i.e. corrected
footage matches the emulator reference pixel-for-pixel, including never reaching 255 on channels the
console itself never reaches. That is the correct target for "look like the console". (A separate
"stretch white back to 255" option would be a deliberate deviation from console output - out of
scope here; note it in the UI docs if ever added.)

### 3. `ColorIdentifier` - identify against displayed

All palette-distance math should use displayed values so the rough affine fit is unbiased:

- `Distance(...)` at `ColorIdentifier.cs:242` compares against `color.R/G/B` - compare against the
  displayed bytes instead.
- The black/white anchor correspondences at `ColorIdentifier.cs:109-110` use
  `CalibrationPalette.Black/White.ToRgb()` - switch to `ToDisplayedRgb()`.

No tolerance changes needed: the shift is ≤ 6/255 against acceptance thresholds of 75-85. This
change is for correctness of the rough fit that steps 5-6 rank against, not to fix a failure.

### 4. `ColorRangeCheck` - re-derive constants from displayed values

The check reasons about absolute white levels; its constants assume white = 255:

- Washed-out branch (`whiteAvg <= 245f`): true displayed white is ~249, so a clean capture still
  fails this guard by only ~4 levels. Keep the threshold but document that it is measured against a
  249 white, or tighten to `<= 243`.
- Crunched branch (`topGap > 12f && topGap <= 20f`): the commanded white−gray224 gap is 31, but the
  *displayed* gap is 249 − 224 = **25**. A clean feed now sits 5 levels from the false-positive
  window instead of 11. Re-derive the window from the displayed gap (e.g. flag `topGap <= 17`) and
  update the comment that quotes "~27+".

### 5. `BlackCrushCheck` - no change

Trend levels (64-224) and gray32 are all multiples of 8, displayed exactly; 255 is already
excluded from the trend fit. Verify the doc comment doesn't cite 255 and leave the logic alone.

### 6. Simulation/tests

Wherever the degradation-bounds simulation or unit tests synthesize "ideal" console output from
palette colors, generate it via `ToDisplayedRgb()` so the validated tolerances keep matching
reality. Add a regression test pinning `Displayed(255) == 249` and `Displayed(v) == v` for the
other palette values, referencing this document.

## Wii VC target

OoT speedruns are mostly run on Wii Virtual Console, which is a different console with a different
video pipeline. The N64 VI described above does not exist there - Nintendo's VC emulator translates
the game's display lists to the Wii GPU (Flipper/Hollywood GX), so there is no RGBA5551
framebuffer, no RDP magic-square dither, no dedither/AA/divot filters, and no 5-bit quantization.
The commanded fill color instead passes through the Wii's own stages (verified against Dolphin's
hardware-tested emulation source, `Source/Core/VideoCommon/`):

1. **EFB render** - the Wii's embedded framebuffer runs in one of two formats chosen by the
   software: `RGB8_Z24` (8 bits/channel, exact) or `RGBA6_Z24` (6 bits/channel with a 2×2 Bayer
   dither - `PixelShaderGen.cpp:1093-1100`). Which one the N64 VC emulator selects must be
   determined empirically (Dolphin can report it at runtime).
2. **EFB→XFB copy: RGB → YCbCr 4:2:2, limited range** - the external framebuffer the Wii actually
   scans out is YUV, not RGB. Dolphin's hardware-tested encoder (`TextureConversionShader.cpp:436`):
   `Y = (66R + 129G + 25B)/256 + 16`, chroma subsampled 2:1 horizontally with rounding. Commanded
   white becomes Y = 235 - the Wii's digital output is **limited range (16-235)** by construction.
   The copy can also apply a hardware gamma (1.0/1.7/2.2); VC presumably uses 1.0 - verify.
3. **VC emulator's own processing** - Nintendo's emulator may apply its own brightness/gamma
   adjustments (N64 VC titles are commonly reported darker than real N64). Not derivable from
   Dolphin; captured automatically by empirical measurement.
4. **Wii VI/DAC** - timing and analog output only; no N64-style filter stages.

Chroma subsampling is invisible on solid fills (uniform chroma), so the palette measurement is
unaffected; it is still part of authentic VC output on real content.

### What to correct toward - the compatibility toggle

Two defensible targets, and they are different products:

- **"Accurate Wii VC"** (recommended default for VC runners): the LUT removes only the capture
  chain's error, restoring the console's true digital output. Reference = VC displayed palette.
- **"Make VC look like N64"**: a deliberate cross-console re-grade, not a correction. Achievable
  with the same machinery by fitting VC captures against the *N64* displayed palette, but it should
  be a clearly labeled separate mode, not the silent default.

Implementation: make the displayed palette a function of a console target instead of a constant.

```csharp
public enum ConsoleTarget { N64, WiiVC }
```

- `PaletteColor.ToDisplayedRgb(ConsoleTarget)` selects between two per-entry displayed tables:
  the N64 table above (mod-8 exact, 255 → 249) and a Wii VC table measured from Dolphin.
- `CalibrationPipeline` and `ColorIdentifier` take the target and thread it through; nothing else
  in the fit changes. The UI exposes it as a console toggle next to the savestate-set choice.
- Do not assume the VC table equals the commanded values: EFB format (possible 6-bit dither) and
  the YUV round-trip both quantize, and any VC-emulator gamma shifts everything. The table is
  empirical, same policy as `DisplayedWhiteLevel`.
- `ColorRangeCheck` interaction: the Wii outputs limited-range video natively. A capture chain that
  digitizes it as limited and flags it correctly in OBS lands at full-range values (the decode
  expands 16-235 back out); one that mislabels it triggers the existing washed-out warning. That
  logic already exists - but re-validate its thresholds against the measured VC white, which may
  not decode to exactly the N64 target's 249.

### Measuring the Wii VC displayed palette (Dolphin)

Reference screenshots come from the VC WAD running in Dolphin with the gz VC fill savestates.
Settings that matter for color fidelity:

1. **Force True Color: OFF** - it disables the RGBA6 quantization and dither Dolphin would
   otherwise emulate faithfully (`PixelShaderGen.cpp:178`), hiding a real quantization stage.
2. **XFB emulation: real** ("Store XFB Copies to Texture Only" off / XFB copy to RAM on,
   Immediate XFB off) - forces the actual RGB→YUV→RGB round-trip so its quantization lands in the
   screenshot. Skipping it presents the EFB directly and bypasses stage 2 entirely.
3. Native (1x) internal resolution, no anti-aliasing/anisotropy, no post-processing shaders.
4. Dolphin's internal screenshot function, not OS capture.

Note Dolphin's YUV→RGB decode (`TextureDecoder_Common.cpp:633`) uses the standard BT.601 full-swing
expansion (1.164 (Y−16), …) - the screenshot is full-range RGB, directly comparable to a correctly
range-configured OBS capture. Then run the same procedure as below: assert stability across
screenshots, and record the per-entry means as the `WiiVC` displayed table.

## Verification procedure

The `DisplayedWhiteLevel` constant is empirical. Validate (and re-validate after any emulator or
setting change):

1. Configure the reference emulator per the reference-capture setup: Parallel Launcher, ParaLLEl
   graphics plugin (LLE), 1x upscaling, Anti-Aliasing checkbox on, divot/dither/gamma-dither core
   options enabled, windowed mode, F12 internal screenshots.
2. Load each of the 39 palette savestates and screenshot.
3. Measure the screen-center region mean of each screenshot.
4. Assert: every commanded multiple-of-8 channel reads back exactly; every commanded 255 channel
   reads back at a single consistent value. That value **is** `DisplayedWhiteLevel`.
5. If step 4 shows anything else (e.g. per-color variation of the top level), replace the
   single-constant model with a measured lookup table keyed by palette entry - the code structure
   above localizes that change to `PaletteColor`.

Console cross-check (optional but recommended once): the same quantization happens in silicon, so a
raw digital capture of a real console (e.g. via an HDMI mod, bypassing analog) should show the same
displayed values. Agreement confirms the emulator reference is a valid stand-in for the console's
digital output.
