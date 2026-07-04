using AutoLUT.Core.Alignment;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;

namespace AutoLUT.Core.Sampling;

/// <summary>
/// Averages each reference region against the aligned user block. Regions whose user block is too
/// noisy (contaminated by particles, HUD flashes, interlace combing) are dropped; accepted regions
/// are down-weighted smoothly with observed noise.
/// </summary>
public sealed class MeanRegionSampler : IRegionSampler
{
    /// <summary>Reject a region when its user-side stddev exceeds max(4 x expected, this floor).</summary>
    private const float RejectionFloor = 8f / 255f;

    /// <summary>Noise scale at which a region's weight halves-ish: weight = 1 / (1 + (std/sigma0)^2).</summary>
    private const float NoiseScale = 4f / 255f;

    public IReadOnlyList<ColorCorrespondence> Sample(RawImage user, ReferenceImage reference, AlignmentResult alignment)
    {
        var correspondences = new List<ColorCorrespondence>(reference.Regions.Count);
        for (int i = 0; i < reference.Regions.Count; i++)
        {
            var region = reference.Regions[i];
            int x = region.X + alignment.Dx;
            int y = region.Y + alignment.Dy;
            if (x < 0 || y < 0 || x + region.Width > user.Width || y + region.Height > user.Height)
                continue;

            var (mean, maxStd) = RegionStatistics.Compute(user, x, y, region.Width, region.Height);
            float threshold = Math.Max((float)region.ExpectedVariance * 4f, RejectionFloor);
            if (maxStd > threshold)
                continue;

            double noise = maxStd / NoiseScale;
            double weight = region.Weight / (1 + noise * noise);
            correspondences.Add(new ColorCorrespondence(mean, reference.RegionMeans[i], weight, maxStd * maxStd));
        }

        return correspondences;
    }
}
