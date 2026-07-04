using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Sampling;

namespace AutoLUT.Core.ReferenceData;

/// <summary>A bundled reference capture with its sampling regions and precomputed region means.</summary>
public sealed class ReferenceImage
{
    public string Id { get; }
    public string DisplayName { get; }
    public RawImage Image { get; }
    public IReadOnlyList<RegionDefinition> Regions { get; }
    public IReadOnlyList<Rgb> RegionMeans { get; }

    public ReferenceImage(string id, string displayName, RawImage image, IReadOnlyList<RegionDefinition> regions)
    {
        Id = id;
        DisplayName = displayName;
        Image = image;
        Regions = regions;

        var means = new Rgb[regions.Count];
        for (int i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.X < 0 || region.Y < 0 || region.X + region.Width > image.Width || region.Y + region.Height > image.Height)
                throw new ArgumentException($"Region {i} of reference '{id}' is outside the image.", nameof(regions));
            means[i] = RegionStatistics.Compute(image, region.X, region.Y, region.Width, region.Height).Mean;
        }

        RegionMeans = means;
    }
}
