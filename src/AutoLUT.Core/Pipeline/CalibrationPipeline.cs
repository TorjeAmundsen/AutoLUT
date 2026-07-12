using System.Security.Cryptography;
using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.Pipeline;

/// <summary>
/// Orchestrates validate -> identify -> fit -> generate for solid-color calibration captures.
/// Ground truth is the commanded palette color, so there is nothing to align against;
/// each capture contributes one correspondence (center-region mean -> commanded color).
/// Per-screenshot failures mark that screenshot invalid without aborting the run.
/// </summary>
public sealed class CalibrationPipeline : ICalibrationPipeline
{
    /// <summary>Minimum identified colors before fitting is attempted (palette has 39).</summary>
    public const int MinimumIdentified = 20;

    private const float NoiseScale = 4f / 255f;

    // Crushed-shadow captures (BlackCrushCheck) get a finer curve plus hard anchors on the two
    // darkest neutrals, which carry the entire toe shape: black pins the clamp to (0, 0, 0) and
    // gray32 pins the first unclipped step. The gray32 anchor matters on deeper crushes - without
    // it the initial toe misfit gets gray32 rejected by the robust loop before the curve learns
    // the toe, and once rejected the fit never recovers it. Values validated against two real
    // crushed feeds and the degradation-bounds simulation: black lands within ~1/255 of 0, gray32
    // within ~1/255 of 32, all samples under the outlier cutoff. Gated because unconditional
    // anchoring costs ~2-3/255 shadow accuracy on gamma-curved feeds.
    private const int CrushCurveKnots = 21;
    private const double CrushCurveSmoothness = 0.003;
    private const double BlackAnchorWeight = 20.0;
    private const double Gray32AnchorWeight = 10.0;

    // Compressed-highlight captures (HighlightCompressionCheck) mirror the crush handling at the
    // top of the range: the knee squeezes 224->255 into a few codes, and without anchoring the
    // robust loop rejects white as an outlier instead of learning the knee - the shoulder shape
    // rides entirely on the two brightest neutrals. Unlike the crush pair (where black is pinned
    // to an exact clamp), both shoulder points start equally misfit, so they need EQUAL weights:
    // on the real knee feed 20/10 gets gray224 rejected by the white pull, and 20/20 shrinks the
    // robust scale enough to reject three mid-dark grays. 10/10 keeps all 39 samples inliers
    // (mean dE 0.0043, corrected white ~252) - validated against the real feed and the
    // degradation-bounds simulation.
    private const double WhiteAnchorWeight = 10.0;
    private const double Gray224AnchorWeight = 10.0;

    private readonly IImageCodec _codec;
    private readonly IColorTransformFitter _fitter;
    private readonly ILutGenerator _lutGenerator;
    private readonly ILutWriter _lutWriter;
    private readonly RawImage _lutTemplate;
    private readonly FitOptions _fitOptions;

    public CalibrationPipeline(
        IImageCodec codec,
        IColorTransformFitter fitter,
        ILutGenerator lutGenerator,
        ILutWriter lutWriter,
        RawImage lutTemplate,
        FitOptions? fitOptions = null)
    {
        _codec = codec;
        _fitter = fitter;
        _lutGenerator = lutGenerator;
        _lutWriter = lutWriter;
        _lutTemplate = lutTemplate;
        // v2 correspondences are near-exact (center-mean noise ~0.01/255), so much less curve
        // smoothing than FitOptions.Default: 0.05 flattens the gamma curvature we are fitting
        // when mid-ramp knots carry only ~1 sample each.
        _fitOptions = fitOptions ?? new FitOptions { CurveSmoothness = 0.01 };
    }

    /// <summary>Default wiring against the embedded OBS LUT template.</summary>
    public static CalibrationPipeline CreateDefault(IImageCodec codec)
    {
        using var templateStream = EmbeddedAssets.OpenOriginalLutTemplate();
        return new CalibrationPipeline(
            codec,
            new AffineCurvesFitter(),
            new TransformLutGenerator(),
            new ObsLutWriter(),
            codec.Decode(templateStream));
    }

    public Task<CalibrationResult> RunAsync(
        IReadOnlyList<ScreenshotInput> screenshots,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => Run(screenshots, progress, cancellationToken), cancellationToken);

    private CalibrationResult Run(
        IReadOnlyList<ScreenshotInput> screenshots,
        IProgress<PipelineProgress>? progress,
        CancellationToken ct)
    {
        int n = screenshots.Count;
        var names = screenshots.Select(s => s.Name).ToArray();
        var errors = new string?[n];
        var means = new Rgb?[n];
        var stdDevs = new float[n];
        var seenHashes = new HashSet<string>();

        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new PipelineProgress(PipelineStage.Validating, $"Validating {names[i]}...", (i + 1) / (double)n));

            RawImage image;
            try
            {
                using var stream = new MemoryStream(screenshots[i].Data);
                image = _codec.Decode(stream);
            }
            catch (InvalidDataException)
            {
                errors[i] = "could not be read as a PNG image.";
                continue;
            }

            if (!seenHashes.Add(Convert.ToHexString(SHA256.HashData(screenshots[i].Data))))
            {
                errors[i] = "is a duplicate of another loaded screenshot.";
                continue;
            }

            var analysis = SolidColorAnalyzer.Analyze(image);
            if (!analysis.IsSolid)
            {
                errors[i] = "is not a solid color capture.";
                continue;
            }

            means[i] = analysis.Mean;
            stdDevs[i] = analysis.MaxStdDev;
        }

        var validIndices = Enumerable.Range(0, n).Where(i => means[i] is not null).ToArray();
        var warnings = new List<string>();
        var targets = new PaletteColor?[n];

        if (validIndices.Length < MinimumIdentified)
        {
            return BuildResult(names, errors, targets, means, warnings,
                $"Too few valid captures ({validIndices.Length}); need at least {MinimumIdentified} of the {CalibrationPalette.Colors.Count} palette colors.");
        }

        progress?.Report(new PipelineProgress(PipelineStage.Identifying, "Identifying colors..."));
        var outcome = ColorIdentifier.Identify(validIndices.Select(i => means[i]!.Value).ToArray(), ct);
        warnings.AddRange(outcome.Warnings);
        if (outcome.GlobalError is not null)
        {
            return BuildResult(names, errors, targets, means, warnings, outcome.GlobalError);
        }

        for (int v = 0; v < validIndices.Length; v++)
        {
            int i = validIndices[v];
            targets[i] = outcome.Assignments[v];
            errors[i] ??= outcome.Errors[v];
        }

        int identified = targets.Count(t => t is not null);
        var missingNeutrals = CalibrationPalette.Neutrals.Where(c => !targets.Contains(c)).ToList();
        if (missingNeutrals.Count > 0)
        {
            return BuildResult(names, errors, targets, means, warnings,
                $"Captures for all 9 gray palette colors are required (missing: {string.Join(", ", missingNeutrals.Select(c => c.Hex))}).");
        }

        if (identified < MinimumIdentified)
        {
            return BuildResult(names, errors, targets, means, warnings,
                $"Too few identified colors ({identified} of {CalibrationPalette.Colors.Count}; need at least {MinimumIdentified}).");
        }

        // All 9 neutrals are guaranteed identified at this point.
        Rgb MeanOf(byte gray) => means[Array.IndexOf(targets, CalibrationPalette.Neutrals.First(c => c.R == gray))]!.Value;
        string? rangeWarning = ColorRangeCheck.Detect(MeanOf(0), MeanOf(255), MeanOf(32), MeanOf(224));

        float? crushDepth = BlackCrushCheck.Detect(MeanOf);
        bool darksCrushed = crushDepth is not null;
        // A range mismatch is itself what crushes the darks, so its warning already explains the
        // clipping - a second crushed-shadows warning would just restate it. The crush fit
        // compensation below still applies either way.
        string? crushWarning = crushDepth is { } depth && rangeWarning is null
            ? $"Something in the capture chain (capture device, splitter, cabling, ...) clips "
                + $"roughly the darkest {Math.Round(depth)} of 255 levels to black, so some detail "
                + "in very dark areas is lost and cannot be recovered by the LUT. Calibration "
                + "compensates for the rest of the range."
            : null;

        float? highlightShortfall = HighlightCompressionCheck.Detect(MeanOf);
        bool highlightsCompressed = highlightShortfall is not null;
        // A full-range-as-limited mismatch hard-clips the same region, so its warning already
        // explains the loss - the compensation below still applies either way.
        if (highlightShortfall is { } shortfall && rangeWarning is null)
        {
            warnings.Add(
                $"Something in the capture chain (capture device, splitter, cabling, ...) compresses "
                + $"the brightest levels - white lands roughly {Math.Round(shortfall)} of 255 below where "
                + "the rest of the ramp points, so some detail in very bright areas is flattened and "
                + "cannot be fully recovered by the LUT. Calibration compensates for the rest of the range.");
        }

        var fitOptions = darksCrushed || highlightsCompressed
            ? _fitOptions with { CurveKnots = CrushCurveKnots, CurveSmoothness = CrushCurveSmoothness }
            : _fitOptions;

        var corrections = new List<string>();
        if (darksCrushed)
        {
            corrections.Add(
                $"Shadow crush compensation: fitting a finer {CrushCurveKnots}-knot tone curve and anchoring "
                + $"black ({BlackAnchorWeight}x weight) and gray 32 ({Gray32AnchorWeight}x) so the curve toe "
                + "follows the clipped shadows.");
        }

        if (highlightsCompressed)
        {
            corrections.Add(
                $"Highlight compression compensation: fitting a finer {CrushCurveKnots}-knot tone curve and "
                + $"anchoring white ({WhiteAnchorWeight}x weight) and gray 224 ({Gray224AnchorWeight}x) so the "
                + "curve shoulder follows the compressed highlights.");
        }

        var correspondences = new List<ColorCorrespondence>(identified);
        var correspondenceShotIndex = new List<int>(identified);
        for (int i = 0; i < n; i++)
        {
            if (targets[i] is not { } target || means[i] is not { } mean)
            {
                continue;
            }

            double noise = stdDevs[i] / NoiseScale;
            double weight = 1.0 / (1.0 + noise * noise);
            if (darksCrushed && target is { IsNeutral: true, R: 0 })
            {
                weight *= BlackAnchorWeight;
            }
            else if (darksCrushed && target is { IsNeutral: true, R: 32 })
            {
                weight *= Gray32AnchorWeight;
            }
            else if (highlightsCompressed && target is { IsNeutral: true, R: 255 })
            {
                weight *= WhiteAnchorWeight;
            }
            else if (highlightsCompressed && target is { IsNeutral: true, R: 224 })
            {
                weight *= Gray224AnchorWeight;
            }

            correspondences.Add(new ColorCorrespondence(mean, target.ToRgb(), weight, stdDevs[i] * stdDevs[i]));
            correspondenceShotIndex.Add(i);
        }

        progress?.Report(new PipelineProgress(PipelineStage.Fitting, "Fitting transform..."));
        FitResult fit;
        try
        {
            fit = _fitter.Fit(correspondences, fitOptions, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildResult(names, errors, targets, means, warnings, $"Fitting failed: {ex.Message}", rangeWarning);
        }

        // A 601/709 matrix mismatch is a gray-preserving cross-channel rotation the affine matrix
        // absorbs, so it never shows on the grays ColorRangeCheck watches - read it off the fitted
        // matrix instead. Advisory only: the LUT corrects the recoverable part.
        string? colorSpaceWarning = fit.Transform is AffineCurvesTransform affine
            ? ColorSpaceMatrixCheck.Detect(affine.Matrix)
            : null;

        progress?.Report(new PipelineProgress(PipelineStage.GeneratingLut, "Generating LUT..."));
        var lut = _lutGenerator.Generate(fit.Transform);
        var lutImage = _lutWriter.Bake(lut, _lutTemplate);

        var outliers = new bool[n];
        var deltaEs = new float?[n];
        for (int k = 0; k < correspondenceShotIndex.Count; k++)
        {
            outliers[correspondenceShotIndex[k]] = !fit.Diagnostics.Inliers[k];
            deltaEs[correspondenceShotIndex[k]] = fit.Diagnostics.Residuals[k];
        }

        var outlierNames = Enumerable.Range(0, n).Where(i => outliers[i]).Select(i => names[i]).ToList();
        if (outlierNames.Count > 0)
        {
            corrections.Add(
                $"Excluded {outlierNames.Count} capture(s) from the fit as outliers (observed color too far "
                + $"from the fitted model): {string.Join(", ", outlierNames)}.");
        }

        progress?.Report(new PipelineProgress(PipelineStage.Finished, "Finished"));
        var screenshotsOut = BuildScreenshots(names, errors, targets, means, outliers, deltaEs);
        return new CalibrationResult(
            screenshotsOut, null, warnings, rangeWarning, lutImage, fit.Diagnostics,
            colorSpaceWarning, crushWarning, corrections);
    }

    private static CalibrationResult BuildResult(
        string[] names, string?[] errors, PaletteColor?[] targets, Rgb?[] means,
        IReadOnlyList<string> warnings, string globalError, string? rangeWarning = null) =>
        new(BuildScreenshots(names, errors, targets, means, null), globalError, warnings, rangeWarning, null, null);

    private static ScreenshotResult[] BuildScreenshots(
        string[] names, string?[] errors, PaletteColor?[] targets, Rgb?[] means, bool[]? outliers,
        float?[]? deltaEs = null)
    {
        var results = new ScreenshotResult[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            results[i] = new ScreenshotResult(names[i], errors[i], targets[i], means[i], outliers?[i] ?? false, deltaEs?[i]);
        }

        return results;
    }
}
