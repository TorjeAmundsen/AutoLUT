using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Calibration;

public enum PaletteCategory
{
    Neutral,
    Grid,
    Mid,
}

public sealed record PaletteColor(byte R, byte G, byte B, PaletteCategory Category)
{
    public string Hex => $"#{R:x2}{G:x2}{B:x2}";

    public bool IsNeutral => Category == PaletteCategory.Neutral;

    public Rgb ToRgb() => new(R / 255f, G / 255f, B / 255f);
}

/// <summary>
/// The 39-color gz calibration palette. Composition validated by
/// simulation against capture degradation bounds (per-channel gain 0.8-1.2, gamma 0.85-1.25,
/// bleed +/-0.03, noise +/-3): 27-point grid {0,128,255}^3, a 9-entry neutral ramp (grid neutrals
/// plus 32..224), and 6 chroma mids that cut fit error ~24%. Near-black (16) and near-white (240)
/// were rejected as clipping hazards. Mids are never anchor targets during identification.
/// </summary>
public static class CalibrationPalette
{
    public static IReadOnlyList<PaletteColor> Colors { get; } = Build();

    /// <summary>The 9 neutral entries, dark to light (0, 32, 64, 96, 128, 160, 192, 224, 255).</summary>
    public static IReadOnlyList<PaletteColor> Neutrals { get; } =
        [.. Colors.Where(c => c.IsNeutral).OrderBy(c => c.R)];

    /// <summary>The 24 chromatic grid colors - the only valid anchor targets.</summary>
    public static IReadOnlyList<PaletteColor> ChromaticGrid { get; } =
        [.. Colors.Where(c => c.Category == PaletteCategory.Grid)];

    public static PaletteColor Black { get; } = Colors.First(c => c is { R: 0, G: 0, B: 0 });

    public static PaletteColor White { get; } = Colors.First(c => c is { R: 255, G: 255, B: 255 });

    private static PaletteColor[] Build()
    {
        var colors = new List<PaletteColor>(39);

        byte[] ramp = [0, 32, 64, 96, 128, 160, 192, 224, 255];
        foreach (byte v in ramp)
        {
            colors.Add(new PaletteColor(v, v, v, PaletteCategory.Neutral));
        }

        byte[] grid = [0, 128, 255];
        foreach (byte r in grid)
        {
            foreach (byte g in grid)
            {
                foreach (byte b in grid)
                {
                    if (r == g && g == b)
                    {
                        continue; // grid neutrals are in the ramp
                    }

                    colors.Add(new PaletteColor(r, g, b, PaletteCategory.Grid));
                }
            }
        }

        colors.Add(new PaletteColor(192, 64, 64, PaletteCategory.Mid));
        colors.Add(new PaletteColor(64, 192, 64, PaletteCategory.Mid));
        colors.Add(new PaletteColor(64, 64, 192, PaletteCategory.Mid));
        colors.Add(new PaletteColor(192, 192, 64, PaletteCategory.Mid));
        colors.Add(new PaletteColor(192, 64, 192, PaletteCategory.Mid));
        colors.Add(new PaletteColor(64, 192, 192, PaletteCategory.Mid));

        return [.. colors];
    }
}
