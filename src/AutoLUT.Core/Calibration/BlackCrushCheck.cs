using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Calibration;

/// <summary>
/// Detects a capture chain that crushes shadows: near-unity gain with a small negative black
/// offset, so the bottom few code values clamp to 0 at the sensor. The signature, per channel:
/// the neutral ramp above the clip knee is linear with a NEGATIVE extrapolated black intercept.
///
/// A negative intercept alone is not enough - ordinary gamma &gt; 1 captures also extrapolate
/// negative because their toe is convex. The discriminator is gray32: under genuine clipping the
/// ramp stays linear all the way down to the clamp, so gray32 lies ON the trend line; under gamma
/// the convex toe puts gray32 well ABOVE it (~9/255 at the validated degradation bounds vs ~0 for
/// clipping, against a 3/255 tolerance).
/// </summary>
public static class BlackCrushCheck
{
    /// <summary>Ramp levels for the trend fit: clear of the clip toe (0, 32) and the ceiling (255).</summary>
    private static readonly byte[] TrendLevels = [64, 96, 128, 160, 192, 224];

    /// <summary>Extrapolated black must fall at least this far below 0; center-mean noise is ~0.5/255.</summary>
    private const float InterceptThreshold = -4f / 255f;

    /// <summary>How close gray32 must sit to the trend line to count as clipped rather than gamma.</summary>
    private const float Gray32Tolerance = 3f / 255f;

    /// <summary>True if any channel shows clipped shadows. <paramref name="grayMean"/> maps a
    /// commanded neutral level to its captured center-region mean.</summary>
    public static bool Detect(Func<byte, Rgb> grayMean)
    {
        Span<Rgb> means = stackalloc Rgb[TrendLevels.Length];
        for (int k = 0; k < TrendLevels.Length; k++)
        {
            means[k] = grayMean(TrendLevels[k]);
        }

        var gray32 = grayMean(32);

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
            if (intercept >= InterceptThreshold)
            {
                continue;
            }

            float predicted32 = slope * (32f / 255f) + intercept;
            if (Math.Abs(Channel(gray32, channel) - predicted32) < Gray32Tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static float Channel(Rgb c, int channel) => channel switch
    {
        0 => c.R,
        1 => c.G,
        _ => c.B,
    };
}
