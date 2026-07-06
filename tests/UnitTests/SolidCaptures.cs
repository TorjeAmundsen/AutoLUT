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

    /// <summary>Limited-range content treated as full: 0-255 compressed into 16-235 (washed out).</summary>
    public static Rgb Washout(Rgb c) => new(
        16f / 255f + c.R * 219f / 255f,
        16f / 255f + c.G * 219f / 255f,
        16f / 255f + c.B * 219f / 255f);

    /// <summary>Full-range content expanded as limited: (x-16)*255/219, clipping both ends (crushed).</summary>
    public static Rgb Crunch(Rgb c) => new(
        Math.Clamp((c.R * 255f - 16f) * 255f / 219f, 0f, 255f) / 255f,
        Math.Clamp((c.G * 255f - 16f) * 255f / 219f, 0f, 255f) / 255f,
        Math.Clamp((c.B * 255f - 16f) * 255f / 219f, 0f, 255f) / 255f);

    /// <summary>Slightly crushed shadows (matches a real capture device): near-unity gain with a
    /// small NEGATIVE per-channel black offset, clamped at 0. The bottom few code values clip to
    /// black while the rest of the ramp stays linear - the signature BlackCrushCheck detects.</summary>
    public static Rgb CrushDarks(Rgb c) => new(
        Math.Clamp(1.016f * c.R - 13f / 255f, 0f, 1f),
        Math.Clamp(1.017f * c.G - 7.5f / 255f, 0f, 1f),
        Math.Clamp(1.016f * c.B - 8.5f / 255f, 0f, 1f));

    /// <summary>Deeper crush from a second real capture device: sub-unity gain with black offsets
    /// near -14/255, leaving gray32 barely above the clamp. Stresses the gray32 anchor - without
    /// it the robust loop rejects gray32 before the curve learns the toe.</summary>
    public static Rgb CrushDarksDeep(Rgb c) => new(
        Math.Clamp(0.978f * c.R - 11.7f / 255f, 0f, 1f),
        Math.Clamp(0.949f * c.G - 14.5f / 255f, 0f, 1f),
        Math.Clamp(0.958f * c.B - 13.6f / 255f, 0f, 1f));

    /// <summary>
    /// Rec.601-encoded content decoded as Rec.709 (N_A = Decode709 * Encode601), on gamma-encoded RGB.
    /// Rows sum to 1, so grays are untouched and only chromatic colors rotate; saturated colors clip.
    /// </summary>
    public static Rgb Mismatch601as709(Rgb c) => ApplyMatrix(c,
        1.086400f, -0.072354f, -0.014050f,
        0.096547f, 0.845041f, 0.058402f,
        -0.014146f, -0.027694f, 1.041800f);

    /// <summary>Rec.709-encoded content decoded as Rec.601 (N_B = Decode601 * Encode709), on gamma-encoded RGB.</summary>
    public static Rgb Mismatch709as601(Rgb c) => ApplyMatrix(c,
        0.913600f, 0.078477f, 0.007922f,
        -0.105041f, 1.172175f, -0.067126f,
        0.009578f, 0.032122f, 0.958200f);

    private static Rgb ApplyMatrix(Rgb c,
        float m00, float m01, float m02,
        float m10, float m11, float m12,
        float m20, float m21, float m22) => new(
        Math.Clamp(m00 * c.R + m01 * c.G + m02 * c.B, 0f, 1f),
        Math.Clamp(m10 * c.R + m11 * c.G + m12 * c.B, 0f, 1f),
        Math.Clamp(m20 * c.R + m21 * c.G + m22 * c.B, 0f, 1f));

    /// <summary>A solid capture of an arbitrary already-degraded color plus noise.</summary>
    public static RawImage CaptureColor(Rgb value, int noiseAmplitude, int seed, int width = 320, int height = 240)
    {
        byte r = (byte)Math.Clamp(MathF.Round(value.R * 255f), 0f, 255f);
        byte g = (byte)Math.Clamp(MathF.Round(value.G * 255f), 0f, 255f);
        byte b = (byte)Math.Clamp(MathF.Round(value.B * 255f), 0f, 255f);

        var image = Solid(width, height, r, g, b);
        if (noiseAmplitude > 0)
        {
            var rng = new Random(seed);
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                image.Pixels[i] = (byte)Math.Clamp(image.Pixels[i] + rng.Next(-noiseAmplitude, noiseAmplitude + 1), 0, 255);
            }
        }

        return image;
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
            {
                image.Pixels[i] = (byte)Math.Clamp(image.Pixels[i] + rng.Next(-noiseAmplitude, noiseAmplitude + 1), 0, 255);
            }
        }

        return image;
    }

    /// <summary>Center-region means of degraded captures for all palette colors, in palette order.</summary>
    public static List<Rgb> DegradedMeans(Degradation degradation, int noiseAmplitude = 2, int seedBase = 100) =>
        CalibrationPalette.Colors
            .Select((c, i) => SolidColorAnalyzer.Analyze(Capture(c, degradation, noiseAmplitude, seedBase + i)).Mean)
            .ToList();

    /// <summary>Center-region means for all palette colors distorted by an arbitrary chain, in palette order.</summary>
    public static List<Rgb> Means(Func<Rgb, Rgb> chain, int noiseAmplitude = 2, int seedBase = 100) =>
        CalibrationPalette.Colors
            .Select((c, i) => SolidColorAnalyzer.Analyze(CaptureColor(chain(c.ToRgb()), noiseAmplitude, seedBase + i)).Mean)
            .ToList();
}
