using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.Sampling;

public static class RegionStatistics
{
    /// <summary>Per-channel mean and the maximum per-channel stddev of a pixel block, in [0,1] units.</summary>
    public static (Rgb Mean, float MaxStdDev) Compute(RawImage image, int x, int y, int width, int height)
    {
        double sumR = 0, sumG = 0, sumB = 0;
        double sumR2 = 0, sumG2 = 0, sumB2 = 0;
        for (int row = y; row < y + height; row++)
        {
            var span = image.Row(row);
            for (int col = x; col < x + width; col++)
            {
                float r = span[col * 3] / 255f;
                float g = span[col * 3 + 1] / 255f;
                float b = span[col * 3 + 2] / 255f;
                sumR += r;
                sumG += g;
                sumB += b;
                sumR2 += r * r;
                sumG2 += g * g;
                sumB2 += b * b;
            }
        }

        int n = width * height;
        var mean = new Rgb((float)(sumR / n), (float)(sumG / n), (float)(sumB / n));
        double varR = Math.Max(sumR2 / n - mean.R * (double)mean.R, 0);
        double varG = Math.Max(sumG2 / n - mean.G * (double)mean.G, 0);
        double varB = Math.Max(sumB2 / n - mean.B * (double)mean.B, 0);
        float maxStd = (float)Math.Sqrt(Math.Max(varR, Math.Max(varG, varB)));
        return (mean, maxStd);
    }
}
