using System.Security.Cryptography;
using AutoLUT.Core.Alignment;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.ReferenceData;
using AutoLUT.Core.Sampling;
using AutoLUT.Core.Validation;

namespace AutoLUT.Core.Pipeline;

/// <summary>
/// Orchestrates validate → align → sample → fit → generate. Per-screenshot failures mark that
/// screenshot invalid without aborting the run; the run fails only when nothing usable remains.
/// Every stage is constructor-injected and independently replaceable.
/// </summary>
public sealed class CalibrationPipeline : ICalibrationPipeline
{
    private readonly ReferenceSet _references;
    private readonly IImageCodec _codec;
    private readonly IAligner _aligner;
    private readonly IRegionSampler _sampler;
    private readonly IColorTransformFitter _fitter;
    private readonly ILutGenerator _lutGenerator;
    private readonly ILutWriter _lutWriter;
    private readonly RawImage _lutTemplate;
    private readonly FitOptions _fitOptions;
    private readonly PipelineOptions _options;

    public CalibrationPipeline(
        ReferenceSet references,
        IImageCodec codec,
        IAligner aligner,
        IRegionSampler sampler,
        IColorTransformFitter fitter,
        ILutGenerator lutGenerator,
        ILutWriter lutWriter,
        RawImage lutTemplate,
        FitOptions? fitOptions = null,
        PipelineOptions? options = null)
    {
        _references = references;
        _codec = codec;
        _aligner = aligner;
        _sampler = sampler;
        _fitter = fitter;
        _lutGenerator = lutGenerator;
        _lutWriter = lutWriter;
        _lutTemplate = lutTemplate;
        _fitOptions = fitOptions ?? FitOptions.Default;
        _options = options ?? PipelineOptions.Default;
    }

    /// <summary>Default wiring against the embedded reference dataset and OBS LUT template.</summary>
    public static CalibrationPipeline CreateDefault(IImageCodec codec)
    {
        using var templateStream = EmbeddedAssets.OpenOriginalLutTemplate();
        return new CalibrationPipeline(
            ManifestLoader.LoadEmbedded(codec),
            codec,
            new TranslationSearchAligner(),
            new MeanRegionSampler(),
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
        var results = new List<ScreenshotResult>(screenshots.Count);
        var seenHashes = new HashSet<string>();
        var assignedReferences = new Dictionary<string, string>(); // reference id -> screenshot name
        var correspondences = new List<ColorCorrespondence>();
        int matchedRegionTotal = 0;

        for (int i = 0; i < screenshots.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var shot = screenshots[i];
            double fraction = (i + 1) / (double)screenshots.Count;
            progress?.Report(new PipelineProgress(PipelineStage.Validating, $"Validating {shot.Name}...", fraction));

            var result = ProcessScreenshot(shot, seenHashes, assignedReferences, progress, fraction);
            results.Add(result.Result);
            if (result.Samples is not null)
            {
                correspondences.AddRange(result.Samples);
                matchedRegionTotal += result.MatchedRegionCount;
            }
        }

        if (results.All(r => !r.IsValid))
            return new CalibrationResult(results, "No valid screenshots. Fix the reported problems and try again.", null, null);

        int required = Math.Max(_options.MinimumCorrespondences, (int)(matchedRegionTotal * _options.MinimumRegionFraction));
        if (correspondences.Count < required)
            return new CalibrationResult(results, "Insufficient valid calibration regions.", null, null);

        progress?.Report(new PipelineProgress(PipelineStage.Fitting, "Fitting transform..."));
        FitResult fit;
        try
        {
            fit = _fitter.Fit(correspondences, _fitOptions, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CalibrationResult(results, $"Fitting failed: {ex.Message}", null, null);
        }

        progress?.Report(new PipelineProgress(PipelineStage.GeneratingLut, "Generating LUT..."));
        var lut = _lutGenerator.Generate(fit.Transform);
        var lutImage = _lutWriter.Bake(lut, _lutTemplate);

        progress?.Report(new PipelineProgress(PipelineStage.Finished, "Finished"));
        return new CalibrationResult(results, null, lutImage, fit.Diagnostics);
    }

    private (ScreenshotResult Result, IReadOnlyList<ColorCorrespondence>? Samples, int MatchedRegionCount) ProcessScreenshot(
        ScreenshotInput shot,
        HashSet<string> seenHashes,
        Dictionary<string, string> assignedReferences,
        IProgress<PipelineProgress>? progress,
        double fraction)
    {
        ScreenshotResult Fail(string error) => new(shot.Name, error, null, null, null, 0);

        RawImage image;
        try
        {
            using var stream = new MemoryStream(shot.Data);
            image = _codec.Decode(stream);
        }
        catch (InvalidDataException)
        {
            return (Fail("could not be read as a PNG image."), null, 0);
        }

        if (!seenHashes.Add(Convert.ToHexString(SHA256.HashData(shot.Data))))
            return (Fail("is a duplicate of another loaded screenshot."), null, 0);

        var candidates = _references.WithDimensions(image.Width, image.Height).ToList();
        if (candidates.Count == 0)
        {
            double ratio = image.Width / (double)image.Height;
            bool ratioMatchesSomeReference = _references.References
                .Any(r => Math.Abs(r.Image.Width / (double)r.Image.Height - ratio) < 0.01);
            return ratioMatchesSomeReference
                ? (Fail($"has incorrect dimensions ({image.Width}x{image.Height}). Screenshots must be unscaled captures."), null, 0)
                : (Fail("has an unexpected aspect ratio. The capture appears stretched or cropped."), null, 0);
        }

        progress?.Report(new PipelineProgress(PipelineStage.Aligning, $"Computing alignment for {shot.Name}...", fraction));
        ReferenceImage? bestReference = null;
        AlignmentResult bestAlignment = default;
        foreach (var candidate in candidates)
        {
            var alignment = _aligner.Align(image, candidate);
            if (bestReference is null || alignment.Score > bestAlignment.Score)
            {
                bestReference = candidate;
                bestAlignment = alignment;
            }
        }

        if (bestAlignment.Score < _options.AlignmentThreshold)
            return (Fail("does not match any expected savestate."), null, 0);

        if (StructuralMatcher.Score(image, bestReference!, bestAlignment) < _options.StructuralThreshold)
            return (Fail("does not match the expected savestate."), null, 0);

        if (bestAlignment.AtSearchBoundary || CropCheck.BandsDiffer(image, bestReference!.Image))
            return (Fail("appears cropped or misaligned."), null, 0);

        if (!assignedReferences.TryAdd(bestReference.Id, shot.Name))
            return (Fail($"shows the same savestate as '{assignedReferences[bestReference.Id]}'."), null, 0);

        progress?.Report(new PipelineProgress(PipelineStage.Sampling, $"Collecting samples from {shot.Name}...", fraction));
        var samples = _sampler.Sample(image, bestReference, bestAlignment);
        if (samples.Count == 0)
            return (Fail("has no usable calibration regions."), null, 0);

        var result = new ScreenshotResult(shot.Name, null, bestReference.Id, bestReference.DisplayName, bestAlignment, samples.Count);
        return (result, samples, bestReference.Regions.Count);
    }
}
