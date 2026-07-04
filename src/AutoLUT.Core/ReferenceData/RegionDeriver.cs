using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.ReferenceData;

public sealed record RegionDeriverOptions
{
    public static RegionDeriverOptions Default { get; } = new();

    public int BlockSize { get; init; } = 16;
    public int Stride { get; init; } = 8;

    /// <summary>Excluded border, covering alignment slack plus analog border junk.</summary>
    public int Margin { get; init; } = 24;

    /// <summary>Max per-channel stddev ([0,1] units) for a block to count as flat.</summary>
    public double MaxStdDev { get; init; } = 2.5 / 255.0;

    /// <summary>Sobel magnitude above this anywhere near a block disqualifies it (chroma-bleed guard).</summary>
    public float EdgeThreshold { get; init; } = 0.12f;

    /// <summary>How far (px) around a block edges are checked.</summary>
    public int EdgeDilation { get; init; } = 4;

    /// <summary>Cap per quantized-Oklab color bin so sky/walls cannot dominate the fit.</summary>
    public int MaxPerColorBin { get; init; } = 12;

    public int MaxRegions { get; init; } = 500;
}

/// <summary>
/// Automatically derives flat sampling regions from a reference image: candidate blocks on a grid,
/// filtered by flatness and edge proximity, greedily selected without overlap with a per-color cap.
/// Avoids text, edges, dithering and effects by construction (all raise local variance/gradient).
/// </summary>
public static class RegionDeriver
{
    public static List<RegionDefinition> Derive(RawImage image, RegionDeriverOptions? options = null)
    {
        var opt = options ?? RegionDeriverOptions.Default;
        var sobel = ImageOps.SobelMagnitude(ImageOps.Luminance(image));

        var candidates = new List<(RegionDefinition Region, Rgb Mean, double Score)>();
        for (int y = opt.Margin; y + opt.BlockSize <= image.Height - opt.Margin; y += opt.Stride)
        for (int x = opt.Margin; x + opt.BlockSize <= image.Width - opt.Margin; x += opt.Stride)
        {
            var (mean, maxStd) = RegionStatistics.Compute(image, x, y, opt.BlockSize, opt.BlockSize);
            if (maxStd > opt.MaxStdDev)
                continue;
            if (MaxSobelAround(sobel, x, y, opt) > opt.EdgeThreshold)
                continue;

            double score = 1.0 / (1.0 + maxStd * 255.0);
            candidates.Add((new RegionDefinition(x, y, opt.BlockSize, opt.BlockSize, score, maxStd), mean, score));
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var selected = new List<RegionDefinition>();
        var binCounts = new Dictionary<(int, int, int), int>();
        foreach (var (region, mean, _) in candidates)
        {
            if (selected.Count >= opt.MaxRegions)
                break;
            if (selected.Any(s => Overlaps(s, region)))
                continue;

            var lab = Oklab.FromSrgb(mean);
            var bin = ((int)MathF.Round(lab.L * 8f), (int)MathF.Round(lab.A * 20f), (int)MathF.Round(lab.B * 20f));
            int count = binCounts.GetValueOrDefault(bin);
            if (count >= opt.MaxPerColorBin)
                continue;

            binCounts[bin] = count + 1;
            selected.Add(region);
        }

        return selected;
    }

    private static float MaxSobelAround(GrayImage sobel, int x, int y, RegionDeriverOptions opt)
    {
        int x0 = Math.Max(x - opt.EdgeDilation, 0);
        int y0 = Math.Max(y - opt.EdgeDilation, 0);
        int x1 = Math.Min(x + opt.BlockSize + opt.EdgeDilation, sobel.Width);
        int y1 = Math.Min(y + opt.BlockSize + opt.EdgeDilation, sobel.Height);

        float max = 0f;
        for (int row = y0; row < y1; row++)
        for (int col = x0; col < x1; col++)
            max = Math.Max(max, sobel[col, row]);
        return max;
    }

    private static bool Overlaps(RegionDefinition a, RegionDefinition b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
