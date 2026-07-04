using AutoLUT.Core.ReferenceData;
using AutoLUT.Core.Sampling;

namespace UnitTests;

public class RegionDeriverTests
{
    [Test]
    public void Derive_YieldsFlatNonOverlappingRegionsInsideMargins()
    {
        // Arrange
        var image = SyntheticScenes.Scene(640, 480, seed: 7, rectangles: 32);
        var options = RegionDeriverOptions.Default;

        // Act
        var regions = RegionDeriver.Derive(image, options);

        // Assert
        Assert.That(regions, Has.Count.GreaterThan(50));
        foreach (var region in regions)
        {
            Assert.That(region.X, Is.GreaterThanOrEqualTo(options.Margin));
            Assert.That(region.Y, Is.GreaterThanOrEqualTo(options.Margin));
            Assert.That(region.X + region.Width, Is.LessThanOrEqualTo(image.Width - options.Margin));
            Assert.That(region.Y + region.Height, Is.LessThanOrEqualTo(image.Height - options.Margin));

            var (_, maxStd) = RegionStatistics.Compute(image, region.X, region.Y, region.Width, region.Height);
            Assert.That(maxStd, Is.LessThanOrEqualTo(options.MaxStdDev), $"Region at ({region.X},{region.Y}) is not flat.");
        }

        for (int i = 0; i < regions.Count; i++)
        for (int j = i + 1; j < regions.Count; j++)
        {
            var a = regions[i];
            var b = regions[j];
            bool overlap = a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
            Assert.That(overlap, Is.False, $"Regions {i} and {j} overlap.");
        }
    }

    [Test]
    public void Derive_AvoidsEdges()
    {
        // Arrange: a flat image with one sharp vertical edge down the middle.
        var image = SyntheticScenes.Scene(320, 240, seed: 8, rectangles: 0);
        SyntheticScenes.FillRect(image, 160, 0, 160, 240, 250, 250, 250);

        // Act
        var regions = RegionDeriver.Derive(image);

        // Assert: no region touches the edge column band (dilation-padded).
        foreach (var region in regions)
        {
            bool nearEdge = region.X + region.Width + 4 > 160 && region.X - 4 < 160;
            Assert.That(nearEdge, Is.False, $"Region at ({region.X},{region.Y}) hugs the edge.");
        }
    }
}
