using System.Security.Cryptography;
using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.Pipeline;

/// <summary>
/// Orchestrates validate -> identify -> fit -> generate for gz solid-color captures.
/// Ground truth is the commanded palette color, so there is nothing to align against;
/// each capture contributes one correspondence (center-region mean -> commanded color).
/// Per-screenshot failures mark that screenshot invalid without aborting the run.
/// </summary>
public sealed class CalibrationPipeline : ICalibrationPipeline
{
    /// <summary>Minimum identified colors before fitting is attempted (palette has 39).</summary>
    public const int MinimumIdentified = 20;

    private const float NoiseScale = 4f / 255f;

    // Crushed-shadow captures (BlackCrushCheck) get a finer curve plus a hard black anchor so the
    // toe bends captured black back to (0, 0, 0) instead of trading it against the clean upper
    // ramp. Values validated against real crushed captures and the degradation-bounds simulation:
    // black lands within 1/255 of 0, gray32 within ~2/255, all samples under the outlier cutoff.
    // Gated because unconditional anchoring costs ~2-3/255 shadow accuracy on gamma-curved feeds.
    private const int CrushCurveKnots = 21;
    private const double CrushCurveSmoothness = 0.003;
    private const double BlackAnchorWeight = 20.0;

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

        bool darksCrushed = BlackCrushCheck.Detect(MeanOf);
        var fitOptions = darksCrushed
            ? _fitOptions with { CurveKnots = CrushCurveKnots, CurveSmoothness = CrushCurveSmoothness }
            : _fitOptions;

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
            if (darksCrushed && target is { R: 0, G: 0, B: 0 })
            {
                weight *= BlackAnchorWeight;
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
        for (int k = 0; k < correspondenceShotIndex.Count; k++)
        {
            outliers[correspondenceShotIndex[k]] = !fit.Diagnostics.Inliers[k];
        }

        progress?.Report(new PipelineProgress(PipelineStage.Finished, "Finished"));
        var screenshotsOut = BuildScreenshots(names, errors, targets, means, outliers);
        return new CalibrationResult(screenshotsOut, null, warnings, rangeWarning, lutImage, fit.Diagnostics, colorSpaceWarning);
    }

    private static CalibrationResult BuildResult(
        string[] names, string?[] errors, PaletteColor?[] targets, Rgb?[] means,
        IReadOnlyList<string> warnings, string globalError, string? rangeWarning = null) =>
        new(BuildScreenshots(names, errors, targets, means, null), globalError, warnings, rangeWarning, null, null);

    private static ScreenshotResult[] BuildScreenshots(
        string[] names, string?[] errors, PaletteColor?[] targets, Rgb?[] means, bool[]? outliers)
    {
        var results = new ScreenshotResult[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            results[i] = new ScreenshotResult(names[i], errors[i], targets[i], means[i], outliers?[i] ?? false);
        }

        return results;
    }
}
