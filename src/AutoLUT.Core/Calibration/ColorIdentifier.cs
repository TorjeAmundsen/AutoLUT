using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.Calibration;

/// <summary>
/// Per-shot identification outcome. Arrays are parallel to the input means; a null assignment
/// means the shot could not be matched and Errors holds the reason.
/// </summary>
public sealed record IdentificationOutcome(
    PaletteColor?[] Assignments,
    string?[] Errors,
    IReadOnlyList<string> Warnings,
    string? GlobalError);

/// <summary>
/// Matches captured solid-color means to palette colors with no capture-order assumptions.
/// Design validated by simulation at capture degradation bounds (gain 0.8-1.2, gamma 0.85-1.25,
/// bleed +/-0.03, noise +/-3); naive nearest-neighbor and greedy gray assignment both fail there:
///
/// 1. Black/white by channel-sum min/max - order-safe under any per-channel monotone map.
/// 2. Per-channel pre-normalization against black/white - cancels gain/offset exactly, leaving
///    pure gamma (moves 128 by +/-20 instead of +/-42 against a 127 grid spacing).
/// 3. Anchors: nearest neighbor over the 24 chromatic grid colors only, accept when
///    best &lt;= 85 and second-best (over the full palette) &gt;= 1.4x best.
/// 4. Rough affine fit on anchors (existing fitter, 2-knot curves = straight line).
/// 5. Neutrals: shots with corrected channel-spread under a threshold, assigned to the gray ramp
///    BY RANK of corrected luminance - a monotone capture preserves gray order, while
///    distance-greedy assignment cascades on the affine fit's systematic gamma residual (~17% of
///    runs). Fewer candidates than ramp entries fails fast naming the likely missing grays;
///    blindly taking the 9 lowest spreads instead conscripts a chromatic and silently shifts
///    every brighter gray up one rank (observed in the field with a missing white capture).
///    Near-identical candidates away from the ramp ends are duplicate captures (a monotone
///    chain only flattens the ramp at black crush / highlight clip) and are dropped to the
///    step-6 duplicate error instead of rank-shifting (observed with a re-captured #606060).
/// 6. Chromatics: greedy injective assignment on corrected distance, verified &lt;= 75.
/// </summary>
public static class ColorIdentifier
{
    private const double MinBlackWhiteGap = 60.0;
    private const double ClippingWarnGap = 12.0;
    private const double AnchorMaxDistance = 85.0;
    private const double AnchorRatio = 1.4;
    private const int MinAnchors = 10;
    private const double ChromaticVerifyDistance = 75.0;
    private const double NeutralVerifyDistance = 100.0;
    private const double NeutralTieTolerance = 4.0;

    // Midpoint of the measured corrected-spread gap at the degradation bounds: grays stay under
    // ~30 (opposing per-channel gammas 0.85/1.25 + bleed 0.03), chromatics stay above ~92
    // (WorstBright clipping); a real limited-range field capture measured <= 7.5 vs >= 119.5.
    private const double NeutralSpreadThreshold = 60.0;

    // Raw-mean distance below which two gray candidates count as the same capture. Center-region
    // means average the sensor noise away, so true duplicates land within ~1/255 of each other;
    // adjacent distinct grays stay >= ~20/255 apart except inside end-of-ramp clipping, which the
    // duplicate logic exempts.
    private const double DuplicateGrayTolerance = 4.0;

    public static IdentificationOutcome Identify(IReadOnlyList<Rgb> means, CancellationToken cancellationToken)
    {
        int n = means.Count;
        var assignments = new PaletteColor?[n];
        var errors = new string?[n];
        var warnings = new List<string>();

        IdentificationOutcome Fail(string message) => new(assignments, errors, warnings, message);

        if (n < CalibrationPalette.Neutrals.Count + 2)
        {
            return Fail($"Too few captures to identify ({n}). Capture the calibration palette colors.");
        }

        // 1. Black and white by channel sum; any per-channel monotone capture map preserves them.
        int blackIndex = 0, whiteIndex = 0;
        for (int i = 1; i < n; i++)
        {
            if (Sum(means[i]) < Sum(means[blackIndex]))
            {
                blackIndex = i;
            }

            if (Sum(means[i]) > Sum(means[whiteIndex]))
            {
                whiteIndex = i;
            }
        }

        var black = means[blackIndex];
        var white = means[whiteIndex];
        if ((white.R - black.R) * 255 < MinBlackWhiteGap
            || (white.G - black.G) * 255 < MinBlackWhiteGap
            || (white.B - black.B) * 255 < MinBlackWhiteGap)
        {
            return Fail("The darkest and brightest captures are too close together. Make sure black and white captures are included.");
        }

        double whiteRunnerUpGap = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (i != whiteIndex)
            {
                whiteRunnerUpGap = Math.Min(whiteRunnerUpGap, (Sum(white) - Sum(means[i])) * 255);
            }
        }

        if (whiteRunnerUpGap < ClippingWarnGap)
        {
            warnings.Add("Capture appears to be clipping highlights - consider lowering capture brightness/contrast.");
        }

        // 2. Pre-normalize per channel; residual degradation is pure gamma.
        var normalized = new Rgb[n];
        for (int i = 0; i < n; i++)
        {
            normalized[i] = new Rgb(
                (means[i].R - black.R) / (white.R - black.R),
                (means[i].G - black.G) / (white.G - black.G),
                (means[i].B - black.B) / (white.B - black.B));
        }

        // 3. Anchors among the chromatic grid colors.
        var anchorSamples = new List<ColorCorrespondence>
        {
            new(means[blackIndex], CalibrationPalette.Black.ToRgb(), 1.0, 0.0),
            new(means[whiteIndex], CalibrationPalette.White.ToRgb(), 1.0, 0.0),
        };
        for (int i = 0; i < n; i++)
        {
            if (i == blackIndex || i == whiteIndex)
            {
                continue;
            }

            PaletteColor? best = null;
            double bestDistance = double.MaxValue;
            foreach (var candidate in CalibrationPalette.ChromaticGrid)
            {
                double d = Distance(normalized[i], candidate);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }

            double secondBest = double.MaxValue;
            foreach (var candidate in CalibrationPalette.Colors)
            {
                if (candidate == best)
                {
                    continue;
                }

                secondBest = Math.Min(secondBest, Distance(normalized[i], candidate));
            }

            if (best is not null && bestDistance <= AnchorMaxDistance && secondBest >= AnchorRatio * bestDistance)
            {
                anchorSamples.Add(new ColorCorrespondence(means[i], best.ToRgb(), 1.0, 0.0));
            }
        }

        if (anchorSamples.Count < MinAnchors)
        {
            return Fail("Captured colors could not be identified. Are these calibration palette captures?");
        }

        // 4. Rough affine fit (2-knot curves degenerate to a line; smoothing no-ops).
        var roughOptions = new FitOptions { CurveKnots = 2, RobustIterations = 2 };
        var rough = new AffineCurvesFitter().Fit(anchorSamples, roughOptions, cancellationToken).Transform;

        var corrected = new Rgb[n];
        for (int i = 0; i < n; i++)
        {
            corrected[i] = rough.Apply(means[i]);
        }

        // 5. Neutrals by rank of corrected luminance, among low-spread candidates only.
        var neutralRamp = CalibrationPalette.Neutrals;
        var neutralCandidates = Enumerable.Range(0, n)
            .Where(i => Spread(corrected[i]) * 255 <= NeutralSpreadThreshold)
            .OrderBy(i => Spread(corrected[i]))
            .ToList();

        // Duplicate gray captures: a monotone capture chain can only flatten the gray ramp at its
        // ends (black crush, highlight clipping), so near-identical candidates AWAY from both ends
        // are duplicate captures, not clipping. Rank assignment would slot a duplicate into the
        // next ramp entry and shift every brighter gray up one (seen in the field with a
        // re-captured #606060), so keep the lowest-spread shot of each interior group and drop the
        // rest - they fall through to step 6 and pick up the duplicate error there.
        var kept = neutralCandidates;
        int duplicatesDropped = 0;
        if (neutralCandidates.Count >= 2)
        {
            int darkest = neutralCandidates.MinBy(i => Sum(means[i]));
            int brightest = neutralCandidates.MaxBy(i => Sum(means[i]));
            bool NearIdentical(int a, int b) => DistanceRaw255(means[a], means[b]) < DuplicateGrayTolerance;

            kept = [];
            foreach (int i in neutralCandidates)
            {
                if (!NearIdentical(i, darkest) && !NearIdentical(i, brightest) && kept.Any(j => NearIdentical(i, j)))
                {
                    duplicatesDropped++;
                }
                else
                {
                    kept.Add(i);
                }
            }

            // End ties are legitimate clipping only while the count fits the ramp; with more
            // candidates than ramp entries the extras are duplicates of the clipped level -
            // drop worst spread first.
            for (int k = kept.Count - 1; k > 0 && kept.Count > neutralRamp.Count; k--)
            {
                if (kept.Take(k).Any(j => NearIdentical(kept[k], j)))
                {
                    kept.RemoveAt(k);
                    duplicatesDropped++;
                }
            }
        }

        if (kept.Count > neutralRamp.Count)
        {
            return Fail($"Found {kept.Count} gray-looking captures for the {neutralRamp.Count} gray palette "
                + "colors. Remove extra or duplicate gray captures and generate again.");
        }

        if (kept.Count < neutralRamp.Count)
        {
            var missing = GuessMissingNeutrals(kept.Select(i => corrected[i]).ToArray(), neutralRamp);
            string duplicateNote = duplicatesDropped > 0
                ? $" Note: {duplicatesDropped} duplicate gray capture(s) were ignored."
                : "";
            return Fail($"Only {kept.Count} of the {neutralRamp.Count} gray palette captures were found"
                + $" (likely missing: {string.Join(", ", missing.Select(m => m.Hex))})."
                + " Capture all 9 grays, including black and white." + duplicateNote);
        }

        int[] neutralIndices = kept.OrderBy(i => Sum(corrected[i])).ToArray();

        for (int rank = 0; rank < neutralIndices.Length; rank++)
        {
            // Clipped highlights make adjacent grays indistinguishable; ranks among raw-identical
            // shots are arbitrary, which is fine - their commanded values are what clipped together.
            int index = neutralIndices[rank];
            if (rank > 0 && Math.Abs(Sum(means[index]) - Sum(means[neutralIndices[rank - 1]])) * 255 < NeutralTieTolerance * 3)
            {
                warnings.Add($"Gray captures are nearly identical near {neutralRamp[rank].Hex} - highlights may be clipping.");
            }

            assignments[index] = neutralRamp[rank];
        }

        if (assignments[blackIndex] != CalibrationPalette.Black || assignments[whiteIndex] != CalibrationPalette.White)
        {
            return Fail("The gray captures could not be identified reliably. Make sure all 9 gray palette colors are captured exactly once.");
        }

        // 6. Chromatics: greedy injective assignment on corrected distance.
        var chromaticPalette = CalibrationPalette.Colors.Where(c => !c.IsNeutral).ToList();
        var freeShots = Enumerable.Range(0, n).Where(i => assignments[i] is null).ToList();
        var pairs = new List<(int Shot, PaletteColor Color, double Distance)>();
        foreach (int i in freeShots)
        {
            foreach (var candidate in chromaticPalette)
            {
                pairs.Add((i, candidate, Distance255(corrected[i], candidate)));
            }
        }

        pairs.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var takenColors = new HashSet<PaletteColor>();
        foreach (var (shot, color, distance) in pairs)
        {
            if (assignments[shot] is not null || takenColors.Contains(color) || distance > ChromaticVerifyDistance)
            {
                continue;
            }

            assignments[shot] = color;
            takenColors.Add(color);
        }

        // 7. Verify.
        for (int i = 0; i < n; i++)
        {
            if (assignments[i] is null)
            {
                errors[i] = "could not be matched to a palette color (unexpected or duplicate color).";
            }
            else if (assignments[i]!.IsNeutral && Distance255(corrected[i], assignments[i]!) > NeutralVerifyDistance)
            {
                errors[i] = $"does not look like its expected gray value {assignments[i]!.Hex}.";
                assignments[i] = null;
            }
        }

        return new IdentificationOutcome(assignments, errors, warnings, null);
    }

    private static double Sum(Rgb c) => c.R + c.G + c.B;

    /// <summary>Ramp entries left over after greedily matching candidates to the ramp by
    /// corrected luminance - the best guess at which grays were not captured.</summary>
    private static List<PaletteColor> GuessMissingNeutrals(IReadOnlyList<Rgb> candidates, IReadOnlyList<PaletteColor> ramp)
    {
        var pairs = new List<(int Candidate, int Ramp, double Difference)>();
        for (int i = 0; i < candidates.Count; i++)
        {
            for (int j = 0; j < ramp.Count; j++)
            {
                pairs.Add((i, j, Math.Abs(Sum(candidates[i]) - Sum(ramp[j].ToRgb()))));
            }
        }

        pairs.Sort((a, b) => a.Difference.CompareTo(b.Difference));

        var takenCandidates = new HashSet<int>();
        var takenRamp = new HashSet<int>();
        foreach (var (candidate, rampIndex, _) in pairs)
        {
            if (!takenCandidates.Contains(candidate) && !takenRamp.Contains(rampIndex))
            {
                takenCandidates.Add(candidate);
                takenRamp.Add(rampIndex);
            }
        }

        return ramp.Where((_, j) => !takenRamp.Contains(j)).ToList();
    }

    private static double Spread(Rgb c)
    {
        float max = Math.Max(c.R, Math.Max(c.G, c.B));
        float min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max - min;
    }

    /// <summary>Euclidean distance in 0-255 units between a normalized [0,1] color and a palette color.</summary>
    private static double Distance(Rgb normalized, PaletteColor color)
    {
        double dr = normalized.R * 255 - color.R;
        double dg = normalized.G * 255 - color.G;
        double db = normalized.B * 255 - color.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static double Distance255(Rgb corrected, PaletteColor color) =>
        Distance(corrected, color);

    /// <summary>Euclidean distance in 0-255 units between two raw capture means.</summary>
    private static double DistanceRaw255(Rgb a, Rgb b)
    {
        double dr = (a.R - b.R) * 255;
        double dg = (a.G - b.G) * 255;
        double db = (a.B - b.B) * 255;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}
