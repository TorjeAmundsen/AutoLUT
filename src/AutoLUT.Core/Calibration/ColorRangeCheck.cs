using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Calibration;

/// <summary>
/// Detects a mismatched color range setting between the capture device and OBS using the known
/// black/white/gray captures. Range mismatch is a digital-capture problem, so the chain is
/// near-neutral and the signatures are clean:
///
/// - Washed out (device sends limited 16-235, OBS treats it as full): black lands near 16 AND
///   white near 235 - both ends pulled inward. Gain alone never lifts black; gamma moves both
///   ends the same direction. The pair is the signature.
/// - Crunched (device sends full 0-255, OBS expands it as limited): the expansion clips, pinning
///   black at 0 while the white-to-gray224 gap collapses (~13 instead of the ~27+ any plausible
///   gain/gamma leaves) and gray32 gets pulled toward black. That gap only holds at exactly
///   unity gain: +0.5% upstream gain clips gray224 toward white (gap under 12) and -3% opens it
///   past 20. So a second signature covers the rest: clipped shadows (BlackCrushCheck) AND
///   clipped white AND the expansion's steep ramp, a 32-to-224 slope near 255/219 = 1.16
///   where real crushing capture devices stay near unity.
/// </summary>
public static class ColorRangeCheck
{
    /// <summary>Lowest 32-to-224 ramp slope read as the limited-range expansion. A crunch at
    /// 0.96-1.03 upstream gain measures 1.12-1.20; real crushing devices measured 0.95-1.02.</summary>
    private const float ExpansionSlope = 1.10f;

    /// <summary>Returns a user-facing range-mismatch warning from the black, white, gray32 and
    /// gray224 capture means, or null. <paramref name="shadowsCrushed"/> is whether
    /// BlackCrushCheck found clipped shadows.</summary>
    public static string? Detect(Rgb black, Rgb white, Rgb gray32, Rgb gray224, bool shadowsCrushed)
    {
        float blackMin = Math.Min(black.R, Math.Min(black.G, black.B)) * 255f;
        float blackAvg = (black.R + black.G + black.B) / 3f * 255f;
        float whiteMin = Math.Min(white.R, Math.Min(white.G, white.B)) * 255f;
        float whiteAvg = (white.R + white.G + white.B) / 3f * 255f;
        float gray224Avg = (gray224.R + gray224.G + gray224.B) / 3f * 255f;
        float gray32Avg = (gray32.R + gray32.G + gray32.B) / 3f * 255f;

        // Limited-range content treated as full: black lifted to ~16 on every channel while
        // white sits low. Analog noise floors stay under ~8, so 12 is a safe lower bound.
        if (blackMin >= 12f && blackAvg <= 48f && whiteAvg <= 245f)
        {
            return "Capture looks washed out (black is lifted, white is dim) - the capture device likely "
                + "outputs limited range (16-235) while OBS treats it as full. In the capture source's "
                + "Properties, set Color Range to 'Partial', then re-capture all colors.";
        }

        // Full-range content expanded as limited: both extremes pinched outward and clipped.
        float topGap = whiteAvg - gray224Avg;
        float bottomGap = gray32Avg - blackAvg;
        float rampSlope = (gray224Avg - gray32Avg) / 192f;
        bool unityGapSignature = topGap > 12f && topGap <= 20f;
        bool clippedExpansionSignature = shadowsCrushed && whiteMin >= 250f && rampSlope >= ExpansionSlope;
        if (blackAvg <= 4f && bottomGap <= 22f && (unityGapSignature || clippedExpansionSignature))
        {
            return "Blacks and highlights look crushed - the capture device likely outputs full range "
                + "(0-255) while OBS treats it as limited. The limited-range expansion clips the darkest "
                + "levels to black and compresses the highlights, so shadow and highlight detail is lost "
                + "before AutoLUT ever sees it - a LUT cannot bring it back. In the capture source's "
                + "Properties, set Color Range to 'Full', then re-capture all colors.";
        }

        return null;
    }
}
