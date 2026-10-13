# Technical Details

How AutoLUT turns 39 screenshots into an OBS LUT.

## Data flow

| Stage | Code |
|---|---|
| Validate captures (decode, dedup, solid-field check) | [SolidColorAnalyzer.cs](src/AutoLUT.Core/Calibration/SolidColorAnalyzer.cs) |
| Identify commanded colors (order-free) | [ColorIdentifier.cs](src/AutoLUT.Core/Calibration/ColorIdentifier.cs) |
| Color range mismatch check | [ColorRangeCheck.cs](src/AutoLUT.Core/Calibration/ColorRangeCheck.cs) |
| Fit color transform (robust regression) | [AffineCurvesFitter.cs](src/AutoLUT.Core/Fitting/AffineCurvesFitter.cs) |
| Sample transform into 64³ LUT | [TransformLutGenerator.cs](src/AutoLUT.Core/Lut/TransformLutGenerator.cs) |
| Bake into OBS's original.png layout | [ObsLutWriter.cs](src/AutoLUT.Core/Lut/ObsLutWriter.cs) |
| Preview via an exact OBS filter replica | [ObsLutApplier.cs](src/AutoLUT.Core/Lut/ObsLutApplier.cs) |

Orchestration: [CalibrationPipeline.cs](src/AutoLUT.Core/Pipeline/CalibrationPipeline.cs). Palette definition: [CalibrationPalette.cs](src/AutoLUT.Core/Calibration/CalibrationPalette.cs).

Every capture is a solid-color screen fill produced by a gz savestate, so the ground truth for each measurement is the exact RGB value that was commanded. There is nothing to align or spatially match - one capture yields one data point: *observed mean color → commanded color*.

## Capture analysis

The center 30% × 30% of the frame is measured (mean + per-channel stddev). The OoT HUD (hearts, buttons, counters, minimap) hugs the screen edges and never reaches this zone, so HUD elements in the screenshots are harmless. A capture counts as solid when the max per-channel stddev is below 10/255 - a realistic analog worst case (noise + interlace line alternation + shading tilt) measures around 4.4/255, while textboxes, menus, and gameplay frames blow far past the threshold. Capture resolution is irrelevant; only the relative center region is used.

The center *mean* over ~100k pixels has a standard error around 0.01/255 - the measurements fed to the fitter are essentially exact.

## Color identification

Captures carry no ordering information, so each must be matched to its commanded palette color purely from its (distorted) observed color. The degradation model this must survive, taken from real analog capture chains: per-channel gain 0.8-1.2, per-channel gamma 0.85-1.25, cross-channel bleed ±0.03, noise ±3/255, plus highlight clipping.

Two naive approaches fail at those bounds (verified by simulation, 12k-24k random degradation draws):

- **Raw nearest-neighbor** against the palette: worst-case displacement of a grid color is ~94-108 RGB units against the grid's 127 minimum spacing - misassignments occur, and at the worst corner only 6 of 27 grid colors pass a ratio test.
- **Distance-greedy gray assignment**: an affine fit cannot represent gamma, leaving systematic residuals up to ±22 per channel on the neutral axis. Corrected mid-grays land nearer the *wrong* ramp neighbor and greedy assignment cascades wholesale (~17% of runs).

The working scheme:

1. **Black and white** are the captures with the min/max channel sum. Any per-channel monotone capture map preserves these extremes, so this is order-safe by construction.
2. **Pre-normalization**: every mean is remapped per channel by `(x - black) / (white - black)`. This cancels gain and offset *exactly* - the residual distortion is pure gamma, which moves the mid grid value by ±20 instead of ±42.
3. **Anchors**: nearest-neighbor over the 24 chromatic grid colors only, accepted when the best distance is ≤ 85 and the second-best (over the whole palette) is ≥ 1.4× the best. Simulation: 0 misassignments in 1.08M matches, ≥ 23 anchors in the worst corner.
4. **Rough affine fit** on the anchors, reusing the production fitter with 2-knot curves (a 2-knot monotone curve degenerates to a line).
5. **Grays by rank**: the 9 captures with the lowest corrected channel-spread are the neutrals; they are assigned to the gray ramp *by rank of corrected luminance*, since a monotone capture preserves gray ordering. 0 failures in 24k simulated runs. Clipped highlights (224 and 255 becoming identical) are detected and produce a user warning.
6. **Chromatics** are assigned by greedy injective matching on corrected RGB distance, verified against a 75-unit bound (the largest genuine distance measured was 74.6). Unmatched or duplicate captures are rejected individually.

Coverage requirements: all 9 grays, plus ≥ 20 identified colors total.

## Palette design (39 colors)

- **27-point grid** over {0, 128, 255}³ - coarse coverage of the whole RGB cube, spaced for identification safety.
- **9-entry neutral ramp** (0, 32, ..., 224, 255) - anchors the per-channel tone curves; cut fit error ~25% over grid-only.
- **6 chroma mids** - (192,64,64) and friends; colored support at intermediate levels where bleed behaves differently than on the neutral axis; another ~24% error reduction.

Rejected by simulation: near-black (16,16,16) - no measurable benefit and hardest to identify; near-white (240,240,240) - clips into white under gain ≥ 1.07 and *beat* white's channel sum in 4% of runs, which would break identification step 1.

## Color model and fitting

The transform is `output = Curves(M · [r, g, b, 1]ᵀ)` on sRGB-encoded values in [0,1]:

- **M** is a 3×4 affine matrix - captures channel crosstalk, tint, and offsets. Fitted in closed form (per-row weighted least squares, 4×4 normal equations) with a small ridge pulling toward identity so degenerate sample sets stay sane.
- **Curves** are per-channel monotone piecewise-linear curves with 13 knots - capture gamma and level drift. Fitted by weighted least squares with a second-difference smoothness penalty, then projected to monotonicity with the pool-adjacent-violators algorithm (PAVA). A tiny identity pull anchors knots with no data support.

This model was chosen over polynomials (wild extrapolation - the LUT must cover the whole cube, including colors far from any sample) and direct 3D-LUT fitting (39 samples cannot constrain 262,144 lattice values). Affine + monotone curves extrapolates sanely and is smooth by construction.

Fitting alternates matrix → curves inside an IRLS loop (4 iterations) with Tukey biweight robust weights, so contaminated captures lose influence instead of bending the result. The curve smoothness weight is far lower than a noisy-data default would be (0.01 vs 0.05) because the correspondences are near-exact; heavier smoothing measurably flattens the gamma curvature being fitted.

### Outlier decisions: RGB error with a perceptual gate

Robust weighting and the outlier labels use **RGB residuals**, not perceptual ones, with a floor of 5/255 ("within capture noise = consistent by definition"). Two failure modes force this split:

- Oklab's cube-root lightness amplifies a 1.5/255 error at black into ΔE ≈ 0.03 - dark captures would be chronically rejected for invisible errors.
- Clipped saturated primaries produce real sensor-space error with near-zero perceptual error - numerically large, visually nothing.

A capture is excluded only when its error is both numerically large (RGB, beyond the Tukey cutoff) *and* visible (Oklab ΔE ≥ 0.01). Reported quality diagnostics (mean/p95 ΔE) remain in Oklab, where they mean something perceptual.

## Color range mismatch detection

A mismatched full/limited color range setting between capture device and OBS has clean signatures on the known gray captures, and gain/gamma cannot mimic either (gamma moves both ends the same direction; gain never lifts black):

- **Limited treated as full** (washed out): black lifted to ~16 *and* white lowered to ~235 - both ends pulled inward.
- **Full expanded as limited** (crushed): the 16-235 → 0-255 expansion clips both extremes - black pinned at 0, the white-to-gray224 gap collapsed to 12-20 (any plausible gain/gamma leaves ≥ ~26), gray32 pulled toward black.

Washed-out captures are affine distortions the LUT can actually correct, but the user is warned to fix the setting anyway; crushed captures permanently destroy shadow/highlight detail, so fixing the source is the only real remedy.

## LUT generation

OBS's `original.png` template is a 512×512 image holding a 64³ lattice as an 8×8 grid of 64×64 tiles: tile index (row-major) = blue, x within tile = red, y = green. The identity value mapping is `round(i · 255 / 63)` - **not** `i · 4` (the ramp ends ..., 247, 251, 255). Baking evaluates the fitted transform at every lattice point `(r/63, g/63, b/63)` and writes `round(255 · f(...))` at the corresponding pixel. Only pixel values change; geometry is untouched, and an identity transform reproduces the template pixel-for-pixel (locked by a round-trip test).

## OBS-exact preview

The corrected preview replicates OBS's Apply LUT filter ([color-grade-filter.c](https://github.com/obsproject/obs-studio/blob/master/plugins/obs-filters/color-grade-filter.c), technique `DrawAlpha3D` in [color_grade_filter.effect](https://github.com/obsproject/obs-studio/blob/master/plugins/obs-filters/data/color_grade_filter.effect)) rather than approximating it:

- OBS reshapes the PNG into a 64³ volume texture and binds it **as sRGB**, so the GPU decodes texels to linear light *before* trilinear filtering, then re-encodes on output.
- The shader's coordinate math (`uvw = nonlinear · 63/64 + 0.5/64`, sampled at texel centers) reduces exactly to a lattice coordinate of `byte/255 · 63`.

So the CPU replica: sRGB-decode the baked LUT's lattice to linear, trilinear-interpolate in linear light at `byte/255 · 63`, re-encode to sRGB. Interpolating in linear light is the load-bearing detail - naive interpolation of the raw sRGB bytes visibly diverges from what OBS renders. The preview also consumes the *baked 8-bit LUT*, not the analytic transform, so it includes the same quantization OBS sees. GPU filtering precision makes the match ~1 LSB, not bit-exact.

## Expected quality

At the full degradation bounds, simulation and end-to-end tests put the corrected error at mean ΔE(Oklab) ≈ 0.003-0.009 with p95 ≈ 0.03 - well under a just-noticeable difference for the mean, with the p95 dominated by near-black measurements where Oklab exaggerates sub-LSB residuals.
