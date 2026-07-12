using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Calibration;

/// <summary>
/// Detects a capture chain that compresses highlights: the neutral ramp is linear (or nearly so)
/// all the way up to gray224, then a knee squeezes the top of the range so white lands far below
/// the ramp's extrapolation. Observed on a real N64 composite chain: 192->224 kept a ~25/255 step
/// while 224->255 collapsed to ~9/255.
///
/// A large white shortfall alone is not enough - ordinary gamma &lt; 1 captures also undershoot the
/// extrapolation because their shoulder is concave. The discriminator is gray224: under a genuine
/// knee the ramp stays linear through 224, so gray224 lies ON the trend line; under gamma the
/// global curvature puts it well off it (~5-16/255 at the validated degradation bounds vs ~0-2.5
/// for the real knee chain, against a 3/255 tolerance). A full-range-as-limited mismatch also
/// trips this check - correctly, since it hard-clips the same region - but its own warning takes
/// precedence for messaging.
/// </summary>
public static class HighlightCompressionCheck
{
    /// <summary>Ramp levels for the trend fit: clear of any dark clip toe (0, 32) and the knee (224, 255).</summary>
    private static readonly byte[] TrendLevels = [64, 96, 128, 160, 192];

    /// <summary>How close gray224 must sit to the trend line to count as a knee rather than gamma.</summary>
    private const float Gray224Tolerance = 3f / 255f;

    /// <summary>White must fall at least this far below the extrapolated line; the strongest
    /// in-bounds gamma shoulder reaches ~9/255 and the real knee chain ~18/255.</summary>
    private const float ShortfallThreshold = 12f / 255f;

    /// <summary>If any channel shows a highlight knee, returns the shortfall - how far below the
    /// linear ramp's extrapolation white lands (out of 255) on the worst channel; otherwise null.
    /// <paramref name="grayMean"/> maps a commanded neutral level to its captured center-region mean.</summary>
    public static float? Detect(Func<byte, Rgb> grayMean)
    {
        Span<Rgb> means = stackalloc Rgb[TrendLevels.Length];
        for (int k = 0; k < TrendLevels.Length; k++)
        {
            means[k] = grayMean(TrendLevels[k]);
        }

        var gray224 = grayMean(224);
        var white = grayMean(255);

        float? worstShortfall = null;
        for (int channel = 0; channel < 3; channel++)
        {
            // Least-squares line obs = slope * (level/255) + intercept over the trend levels.
            int n = TrendLevels.Length;
            float sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int k = 0; k < n; k++)
            {
                float x = TrendLevels[k] / 255f;
                float y = Channel(means[k], channel);
                sx += x;
                sy += y;
                sxx += x * x;
                sxy += x * y;
            }

            float slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
            float intercept = (sy - slope * sx) / n;
            if (slope <= 0)
            {
                continue;
            }

            float predicted224 = slope * (224f / 255f) + intercept;
            if (Math.Abs(Channel(gray224, channel) - predicted224) >= Gray224Tolerance)
            {
                continue;
            }

            float shortfall = (slope + intercept) - Channel(white, channel);
            if (shortfall > ShortfallThreshold)
            {
                worstShortfall = Math.Max(worstShortfall ?? 0f, shortfall * 255f);
            }
        }

        return worstShortfall;
    }

    private static float Channel(Rgb c, int channel) => channel switch
    {
        0 => c.R,
        1 => c.G,
        _ => c.B,
    };
}
