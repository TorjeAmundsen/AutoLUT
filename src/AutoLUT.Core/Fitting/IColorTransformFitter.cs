using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.Fitting;

public interface IColorTransformFitter
{
    FitResult Fit(IReadOnlyList<ColorCorrespondence> samples, FitOptions options, CancellationToken cancellationToken);
}

public sealed record FitOptions
{
    public static FitOptions Default { get; } = new();

    /// <summary>Knot count for the per-channel curves.</summary>
    public int CurveKnots { get; init; } = 13;

    /// <summary>Outer IRLS iterations.</summary>
    public int RobustIterations { get; init; } = 4;

    /// <summary>Ridge strength pulling the affine matrix toward identity, relative to total sample weight.</summary>
    public double MatrixRidge { get; init; } = 1e-3;

    /// <summary>Second-difference smoothness penalty on curve knots, relative to total sample weight.</summary>
    public double CurveSmoothness { get; init; } = 0.05;

    /// <summary>Identity pull on curve knots (regularizes knots with no data support), relative to total sample weight.</summary>
    public double CurveIdentityPull { get; init; } = 1e-3;
}

public sealed record FitResult(IColorTransform Transform, FitDiagnostics Diagnostics);

public sealed record FitDiagnostics(
    float MeanDeltaE,
    float MedianDeltaE,
    float P95DeltaE,
    int InlierCount,
    int TotalCount,
    IReadOnlyList<float> Residuals,
    IReadOnlyList<bool> Inliers);
