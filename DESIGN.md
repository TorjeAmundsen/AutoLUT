# Design Specification: Capture Color Calibration Tool

## Overview

The goal of this application is to generate an OBS-compatible LUT image (`LUT.png`) that color-corrects a user's Nintendo Wii capture to closely match the console's true output colors.

The intended audience is the Ocarina of Time speedrunning community and retro capture users. Calibration uses a source that fills the entire screen with any commanded RGB color: gz (the OoT practice tool every speedrunner has installed) or the AutoLUT Palette homebrew app (Wii and N64). Because the commanded color is known exactly, the program has perfect ground truth: each screenshot of a solid-color fill yields one measurement of how the user's capture chain distorts that color. No reference captures, image alignment, or scene matching are required.

The application is built as:

* **.NET 10**
* **Avalonia 12**
* **Native AOT compiled**
* Cross-platform (Windows, Linux, macOS)

---

# Goals

The program should:

* Produce an OBS-compatible LUT image.
* Require minimal user interaction.
* Be deterministic and repeatable.
* Be robust against analog capture imperfections.
* Reject obviously invalid input.
* Give immediate visual feedback through preview images.

The user should never need to manually tweak curves or sliders.

---

# Non-goals

The application is **not** intended to:

* Calibrate monitors
* Reverse analog artifacts
* Sharpen blurry captures
* Support arbitrary games
* Support arbitrary screenshots

It is specifically designed around solid-color calibration fills displayed by a known source (gz savestates or the AutoLUT Palette homebrew app).

---

# Input

The application accepts PNG screenshots of gz solid-color fills, one per palette color.

Screenshots may be captured in any order and with any filenames - each capture is matched to its commanded color automatically.

---

# Calibration Palette

The palette contains **39 colors**:

* A 27-point grid over {0, 128, 255}³ - coarse coverage of the whole RGB cube.
* A 9-entry neutral ramp (0, 32, 64, 96, 128, 160, 192, 224, 255) - anchors the per-channel tone curves.
* 6 chroma mids such as (192, 64, 64) - colored support at intermediate levels, cutting fit error roughly 24%.

The composition was validated by simulation against capture degradation bounds of per-channel gain 0.8-1.2, gamma 0.85-1.25, cross-channel bleed ±0.03 and noise ±3/255. Near-black (16) and near-white (240) entries were deliberately excluded as clipping hazards.

Black, white and all grays are required. At least 20 colors total must be identified for calibration to proceed.

---

# Automatic Validation

Each submitted screenshot is validated before calibration:

## Solid-field check

The center 60% × 60% of the frame (overscan and pillarboxing stay outside) must be uniform: per-channel standard deviation below 10/255 and quadrant means agreeing within 5/255. Gradients, gameplay frames and OSD overlays are rejected.

## Duplicate detection

Byte-identical files are rejected outright. Two captures of the same palette color are detected during identification and the worse one is rejected.

## Clipping detection

If the brightest captures are nearly indistinguishable, the capture chain is clipping highlights; the user is warned that very bright detail is lost before AutoLUT sees it. There is no brightness/contrast advice: those capture controls are digital filters after the ADC and cannot recover analog clipping.

## Color range mismatch detection

A mismatched color range setting between the capture device and OBS is detected from the known black/white/gray captures: limited-range content treated as full lifts black to ~16 and dims white to ~235 (washed out), while full-range content expanded as limited clips both extremes (crushed). Gain and gamma cannot mimic either signature. The user is warned with the exact OBS setting to change; a crushed capture permanently destroys shadow/highlight detail, so fixing it at the source is required for a good calibration base.

---

# Color Identification

Captures are matched to palette colors with no ordering assumptions. The scheme was validated by simulation (12k-24k random degradations at the bounds above; naive nearest-neighbor matching and distance-greedy gray assignment both fail there):

1. **Black and white** are found by channel-sum min/max - safe under any per-channel monotone capture map.
2. All means are **pre-normalized** per channel against black/white, cancelling gain and offset exactly; the residual distortion is pure gamma.
3. **Anchor colors** are identified by nearest neighbor over the 24 chromatic grid colors only, with a distance cap and a 1.4× second-best ratio test over the full palette.
4. A **rough affine transform** is fitted to the anchors.
5. **Grays** are assigned by rank of corrected luminance - a monotone capture preserves gray order.
6. **Chromatics** are assigned by greedy injective matching on corrected distance, then verified against a distance bound.

Unidentifiable or duplicate captures are rejected individually with user-friendly errors; calibration continues with the rest.

---

# Sampling

Each identified capture contributes one correspondence:

```
Center-region mean RGB  →  Commanded palette color
```

Correspondences are weighted by measured center-region noise.

---

# Color Model

The application internally estimates a continuous color transform.

The implementation is modular so different fitting algorithms can be evaluated. The default is a 3×4 affine matrix followed by per-channel monotone piecewise-linear curves:

```
output = Curves(M · [r, g, b, 1]ᵀ)
```

The affine matrix captures channel crosstalk and tint; the monotone curves capture gamma and level drift. The model extrapolates sanely across the whole LUT cube and is inherently smooth.

---

# Optimization

The fitting process minimizes perceptual color error where practical and limits outlier influence:

* Weighted samples
* Robust regression (IRLS with Tukey biweight on Oklab residuals)
* Smoothness constraints and monotonicity (PAVA)

Expected quality at full degradation bounds: mean ΔE_Oklab ≈ 0.009.

---

# LUT Generation

After fitting, the transform is sampled into an OBS-compatible 3D LUT.

The LUT is written using OBS's supplied `original.png` LUT layout as the template. Only pixel values are modified; the geometry of the LUT image remains identical to OBS's original.

Output:

```
LUT.png
```

---

# Preview

The UI includes an image preview of the currently selected screenshot.

## Corrected Preview

A toggle switches between:

```
Original Capture
```

and

```
Corrected (LUT Applied)
```

The corrected preview replicates OBS's Apply LUT filter exactly (sRGB-decoded trilinear interpolation in linear light, per the OBS source), so what the preview shows is what OBS renders.

The preview updates instantly after LUT generation.

---

# User Interface

Main window contains:

### Screenshot list

Loaded calibration screenshots, each with an identified-color swatch and status.

### Image preview

Large preview area.

### Toggle

```
□ Show Corrected Image
```

### Generate LUT

Runs calibration.

### Save LUT

```
Save LUT.png
```

### Status area

Displays progress such as:

```
Validating...

Identifying colors...

Fitting transform...

Generating LUT...

Finished
```

### Error display

User-friendly errors. Examples:

```
capture.png is not a solid color capture.

capture.png could not be matched to a palette color.

Captures for all 9 gray palette colors are required.

Too few identified colors (14 of 39; need at least 20).
```

---

# Performance

The application remains responsive. Long-running operations execute asynchronously; the UI never freezes during optimization.

---

# Extensibility

The architecture separates:

```
UI

↓

Calibration Pipeline

↓

Identification

↓

Optimization

↓

LUT Generation
```

Each stage is independently replaceable. Future improvements are possible without rewriting the UI.

---

# File Formats

## Input

PNG

## Output

PNG, using OBS's original LUT layout.

---

# Future Features

Potential future additions include:

* Multiple optimization algorithms
* Calibration quality score
* Per-color error report
* Side-by-side before/after comparison
* Automatic calibration report

---

# Success Criteria

The application will be considered successful if it:

* Generates an OBS-compatible `LUT.png` that can be applied directly in OBS.
* Corrects capture colors to near-imperceptible error across the calibration palette.
* Remains robust against the common characteristics of analog Wii capture, including gain/gamma drift, chroma bleed, noise and highlight clipping.
* Requires no manual tuning beyond supplying the requested solid-color screenshots, in any order.
* Detects and rejects invalid, non-solid or duplicate calibration images instead of generating an unreliable LUT.
* Provides immediate visual feedback through the corrected preview so users can verify the result before saving.
* Is architected so the color fitting algorithm can evolve independently of the user interface.
