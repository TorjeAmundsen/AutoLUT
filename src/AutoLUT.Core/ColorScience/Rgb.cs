namespace AutoLUT.Core.ColorScience;

/// <summary>An RGB triplet with nominal range [0, 1] per channel. Encoding (sRGB or linear) depends on context.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public Rgb Clamp01() => new(Math.Clamp(R, 0f, 1f), Math.Clamp(G, 0f, 1f), Math.Clamp(B, 0f, 1f));
}
