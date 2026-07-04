using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Imaging;

namespace UnitTests;

/// <summary>Synthetic gz solid-color captures with analog-style degradation for identifier/pipeline tests.</summary>
internal static class SolidCaptures
{
    /// <summary>Per-channel gain + gamma with optional cross-channel bleed, matching the validated degradation bounds.</summary>
    public sealed record Degradation(
        float GainR, float GainG, float GainB,
        float GammaR, float GammaG, float GammaB,
        float Bleed = 0f)
    {
        public static Degradation Moderate { get; } = new(0.85f, 1.05f, 0.95f, 0.9f, 1.1f, 1.2f, 0.02f);

        /// <summary>Worst dark corner: everything dimmer and crushed.</summary>
        public static Degradation WorstDark { get; } = new(0.8f, 0.8f, 0.8f, 1.25f, 1.25f, 1.25f, 0.03f);

        /// <summary>Worst bright corner: clips highlights (224/255 become indistinguishable).</summary>
        public static Degradation WorstBright { get; } = new(1.2f, 1.2f, 1.2f, 0.85f, 0.85f, 0.85f, 0.03f);

        public Rgb Apply(Rgb c)
        {
            float r = GainR * c.R + Bleed * c.G - Bleed * c.B;
            float g = -Bleed * c.R + GainG * c.G + Bleed * c.B;
            float b = Bleed * c.R - Bleed * c.G + GainB * c.B;
            return new Rgb(
                MathF.Pow(Math.Clamp(r, 0f, 1f), GammaR),
                MathF.Pow(Math.Clamp(g, 0f, 1f), GammaG),
                MathF.Pow(Math.Clamp(b, 0f, 1f), GammaB));
        }
    }

    public static void FillRect(RawImage image, int x, int y, int width, int height, byte r, byte g, byte b)
    {
        for (int row = y; row < y + height; row++)
        {
            var span = image.Row(row);
            for (int col = x; col < x + width; col++)
            {
                span[col * 3] = r;
                span[col * 3 + 1] = g;
                span[col * 3 + 2] = b;
            }
        }
    }

    public static RawImage Solid(int width, int height, byte r, byte g, byte b)
    {
        var image = new RawImage(width, height);
        for (int i = 0; i < image.Pixels.Length; i += 3)
        {
            image.Pixels[i] = r;
            image.Pixels[i + 1] = g;
            image.Pixels[i + 2] = b;
        }

        return image;
    }

    /// <summary>A degraded capture of one commanded palette color: solid degraded color plus noise.</summary>
    public static RawImage Capture(PaletteColor color, Degradation degradation, int noiseAmplitude, int seed,
        int width = 320, int height = 240)
    {
        var degraded = degradation.Apply(color.ToRgb());
        byte r = (byte)Math.Clamp(MathF.Round(degraded.R * 255f), 0f, 255f);
        byte g = (byte)Math.Clamp(MathF.Round(degraded.G * 255f), 0f, 255f);
        byte b = (byte)Math.Clamp(MathF.Round(degraded.B * 255f), 0f, 255f);

        var image = Solid(width, height, r, g, b);
        if (noiseAmplitude > 0)
        {
            var rng = new Random(seed);
            for (int i = 0; i < image.Pixels.Length; i++)
                image.Pixels[i] = (byte)Math.Clamp(image.Pixels[i] + rng.Next(-noiseAmplitude, noiseAmplitude + 1), 0, 255);
        }

        return image;
    }

    /// <summary>Center-region means of degraded captures for all palette colors, in palette order.</summary>
    public static List<Rgb> DegradedMeans(Degradation degradation, int noiseAmplitude = 2, int seedBase = 100) =>
        CalibrationPalette.Colors
            .Select((c, i) => SolidColorAnalyzer.Analyze(Capture(c, degradation, noiseAmplitude, seedBase + i)).Mean)
            .ToList();
}
