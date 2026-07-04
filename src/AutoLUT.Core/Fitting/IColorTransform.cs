using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Fitting;

/// <summary>A continuous color transform on sRGB-encoded values. Implementations must be thread-safe and deterministic.</summary>
public interface IColorTransform
{
    Rgb Apply(Rgb srgb);
}
