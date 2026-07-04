using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Sampling;

/// <summary>One observed→reference color pair produced by a sampling region. Colors are sRGB-encoded [0,1].</summary>
public readonly record struct ColorCorrespondence(Rgb Observed, Rgb Reference, double Weight, double ObservedVariance);
