using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;
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

/// <summary>A user-supplied capture of a gz solid-color fill: display name plus raw PNG bytes.</summary>
public sealed record ScreenshotInput(string Name, byte[] Data);

public enum PipelineStage
{
    Loading,
    Validating,
    Identifying,
    Fitting,
    GeneratingLut,
    Finished,
}

public sealed record PipelineProgress(PipelineStage Stage, string Message, double? Fraction = null);

public sealed record ScreenshotResult(
    string Name,
    string? Error,
    PaletteColor? Target,
    Rgb? ObservedMean,
    bool IsOutlier = false)
{
    public bool IsValid => Error is null && Target is not null;
}

public sealed record CalibrationResult(
    IReadOnlyList<ScreenshotResult> Screenshots,
    string? Error,
    IReadOnlyList<string> Warnings,
    string? ColorRangeWarning,
    RawImage? LutImage,
    FitDiagnostics? Diagnostics,
    string? ColorSpaceWarning = null)
{
    public bool Success => Error is null && LutImage is not null;
}
