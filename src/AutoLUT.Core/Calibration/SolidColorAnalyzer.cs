using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.Calibration;

public sealed record SolidAnalysis(Rgb Mean, float MaxStdDev, bool IsSolid);

/// <summary>
/// Measures the central 30% x 30% of the frame. Calibration sources keep their overlays at the
/// screen edges (the OoT HUD with gz savestates, the corner label in the AutoLUT Palette Wii app),
/// so the fill in the central zone is unobstructed. Solid iff max per-channel stddev is under
/// 10/255 (realistic analog worst case ~4.4/255: noise + interlace + shading tilt); textboxes,
/// menus and gameplay frames blow far past it.
/// </summary>
public static class SolidColorAnalyzer
{
    public const float StdDevThreshold = 10f / 255f;

    public static SolidAnalysis Analyze(RawImage image)
    {
        int x0 = image.Width * 35 / 100;
        int y0 = image.Height * 35 / 100;
        int width = image.Width * 30 / 100;
        int height = image.Height * 30 / 100;

        var (mean, maxStd) = RegionStatistics.Compute(image, x0, y0, width, height);
        return new SolidAnalysis(mean, maxStd, maxStd < StdDevThreshold);
    }
}
