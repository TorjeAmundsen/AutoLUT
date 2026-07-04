namespace AutoLUT.Core.ColorScience;

public static class ColorSpace
{
    /// <summary>sRGB EOTF: nonlinear [0,1] to linear light. Matches the inverse of OBS's srgb_linear_to_nonlinear shader function.</summary>
    public static float SrgbToLinear(float u) =>
        u <= 0.04045f ? u / 12.92f : MathF.Pow((u + 0.055f) / 1.055f, 2.4f);

    /// <summary>sRGB inverse EOTF: linear light to nonlinear [0,1].</summary>
    public static float LinearToSrgb(float u) =>
        u <= 0.0031308f ? 12.92f * u : 1.055f * MathF.Pow(u, 1f / 2.4f) - 0.055f;

    public static Rgb SrgbToLinear(Rgb c) => new(SrgbToLinear(c.R), SrgbToLinear(c.G), SrgbToLinear(c.B));

    public static Rgb LinearToSrgb(Rgb c) => new(LinearToSrgb(c.R), LinearToSrgb(c.G), LinearToSrgb(c.B));
}
