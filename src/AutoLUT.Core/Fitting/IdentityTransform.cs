using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Fitting;

public sealed class IdentityTransform : IColorTransform
{
    public static IdentityTransform Instance { get; } = new();

    public Rgb Apply(Rgb srgb) => srgb;
}
