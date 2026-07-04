using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.Validation;

/// <summary>
/// Crop/border heuristic: compares the width of near-black border bands between the user capture
/// and its reference. Analog captures keep the reference's border geometry; cropping or scaling
/// changes it.
/// </summary>
public static class CropCheck
{
    private const float DarkLuma = 0.06f;
    public const int MaxBandDifference = 6;

    public static bool BandsDiffer(RawImage user, RawImage reference)
    {
        var userBands = MeasureBands(ImageOps.Luminance(user));
        var referenceBands = MeasureBands(ImageOps.Luminance(reference));
        return Math.Abs(userBands.Left - referenceBands.Left) > MaxBandDifference
            || Math.Abs(userBands.Right - referenceBands.Right) > MaxBandDifference
            || Math.Abs(userBands.Top - referenceBands.Top) > MaxBandDifference
            || Math.Abs(userBands.Bottom - referenceBands.Bottom) > MaxBandDifference;
    }

    /// <summary>Width of the near-black band at each edge (rows/columns whose mean luma is below the dark threshold).</summary>
    public static (int Left, int Right, int Top, int Bottom) MeasureBands(GrayImage luma)
    {
        int left = 0;
        while (left < luma.Width && ColumnMean(luma, left) < DarkLuma)
            left++;
        int right = 0;
        while (right < luma.Width - left && ColumnMean(luma, luma.Width - 1 - right) < DarkLuma)
            right++;
        int top = 0;
        while (top < luma.Height && RowMean(luma, top) < DarkLuma)
            top++;
        int bottom = 0;
        while (bottom < luma.Height - top && RowMean(luma, luma.Height - 1 - bottom) < DarkLuma)
            bottom++;
        return (left, right, top, bottom);
    }

    private static float ColumnMean(GrayImage luma, int x)
    {
        float sum = 0;
        for (int y = 0; y < luma.Height; y++)
            sum += luma[x, y];
        return sum / luma.Height;
    }

    private static float RowMean(GrayImage luma, int y)
    {
        float sum = 0;
        for (int x = 0; x < luma.Width; x++)
            sum += luma[x, y];
        return sum / luma.Width;
    }
}
