using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Sampling;

/// <summary>
/// One observed-to-reference color pair produced by a sampling region. Colors are sRGB-encoded [0,1].
/// <paramref name="IsAnchor"/> marks an explicit anchor that the fitter never rejects as an outlier.
/// </summary>
public readonly record struct ColorCorrespondence(
    Rgb Observed, Rgb Reference, double Weight, double ObservedVariance, bool IsAnchor = false);
