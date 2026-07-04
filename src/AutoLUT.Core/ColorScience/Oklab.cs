namespace AutoLUT.Core.ColorScience;

public readonly record struct Oklab(float L, float A, float B)
{
    /// <summary>Converts an sRGB-encoded color (nominal [0,1]) to Oklab. Out-of-range inputs are handled via linear extension of the EOTF and Cbrt's odd extension.</summary>
    public static Oklab FromSrgb(Rgb srgb)
    {
        var c = ColorSpace.SrgbToLinear(srgb.Clamp01());
        float l = 0.4122214708f * c.R + 0.5363325363f * c.G + 0.0514459929f * c.B;
        float m = 0.2119034982f * c.R + 0.6806995451f * c.G + 0.1073969566f * c.B;
        float s = 0.0883024619f * c.R + 0.2817188376f * c.G + 0.6299787005f * c.B;

        float l2 = MathF.Cbrt(l);
        float m2 = MathF.Cbrt(m);
        float s2 = MathF.Cbrt(s);

        return new Oklab(
            0.2104542553f * l2 + 0.7936177850f * m2 - 0.0040720468f * s2,
            1.9779984951f * l2 - 2.4285922050f * m2 + 0.4505937099f * s2,
            0.0259040371f * l2 + 0.7827717662f * m2 - 0.8086757660f * s2);
    }

    public static float DeltaE(Oklab x, Oklab y)
    {
        float dl = x.L - y.L, da = x.A - y.A, db = x.B - y.B;
        return MathF.Sqrt(dl * dl + da * da + db * db);
    }

    public static float DeltaESrgb(Rgb x, Rgb y) => DeltaE(FromSrgb(x), FromSrgb(y));
}
