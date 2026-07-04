using AutoLUT.Core.Alignment;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.Pipeline;

public interface ICalibrationPipeline
{
    Task<CalibrationResult> RunAsync(
        IReadOnlyList<ScreenshotInput> screenshots,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>A user-supplied screenshot: display name plus raw PNG bytes.</summary>
public sealed record ScreenshotInput(string Name, byte[] Data);

public enum PipelineStage
{
    Loading,
    Validating,
    Aligning,
    Sampling,
    Fitting,
    GeneratingLut,
    Finished,
}

public sealed record PipelineProgress(PipelineStage Stage, string Message, double? Fraction = null);

public sealed record ScreenshotResult(
    string Name,
    string? Error,
    string? ReferenceId,
    string? ReferenceName,
    AlignmentResult? Alignment,
    int SampleCount)
{
    public bool IsValid => Error is null;
}

public sealed record CalibrationResult(
    IReadOnlyList<ScreenshotResult> Screenshots,
    string? Error,
    RawImage? LutImage,
    FitDiagnostics? Diagnostics)
{
    public bool Success => Error is null && LutImage is not null;
}

public sealed record PipelineOptions
{
    public static PipelineOptions Default { get; } = new();

    /// <summary>Minimum ZNCC alignment score for a screenshot to be considered a savestate match.</summary>
    public double AlignmentThreshold { get; init; } = 0.55;

    /// <summary>Minimum gradient-map ZNCC (color-invariant structural check).</summary>
    public double StructuralThreshold { get; init; } = 0.5;

    /// <summary>Hard floor on pooled correspondences before fitting is attempted.</summary>
    public int MinimumCorrespondences { get; init; } = 30;

    /// <summary>Required fraction of matched references' regions that must survive sampling.</summary>
    public double MinimumRegionFraction { get; init; } = 0.4;
}
