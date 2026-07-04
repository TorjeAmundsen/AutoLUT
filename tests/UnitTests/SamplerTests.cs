using AutoLUT.Core.Alignment;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;
using AutoLUT.Core.Sampling;

namespace UnitTests;

public class SamplerTests
{
    private static (ReferenceImage Reference, RawImage Base) MakeFlatScene()
    {
        var image = SyntheticScenes.Scene(320, 240, seed: 31, rectangles: 0);
        SyntheticScenes.FillRect(image, 40, 40, 40, 40, 200, 50, 60);
        SyntheticScenes.FillRect(image, 150, 60, 40, 40, 30, 180, 90);
        SyntheticScenes.FillRect(image, 220, 150, 40, 40, 90, 80, 220);

        RegionDefinition[] regions =
        [
            new(48, 48, 24, 24),
            new(158, 68, 24, 24),
            new(228, 158, 24, 24),
        ];
        return (new ReferenceImage("flat", "Flat", image, regions), image);
    }

    [Test]
    public void Sample_TranslatedImage_ReproducesReferenceMeans()
    {
        // Arrange
        var (reference, baseImage) = MakeFlatScene();
        var user = SyntheticScenes.Translate(baseImage, 6, -4);

        // Act
        var samples = new MeanRegionSampler().Sample(user, reference, new AlignmentResult(6, -4, 1, false));

        // Assert
        Assert.That(samples, Has.Count.EqualTo(3));
        for (int i = 0; i < samples.Count; i++)
        {
            Assert.That(samples[i].Observed.R, Is.EqualTo(reference.RegionMeans[i].R).Within(0.005f));
            Assert.That(samples[i].Observed.G, Is.EqualTo(reference.RegionMeans[i].G).Within(0.005f));
            Assert.That(samples[i].Observed.B, Is.EqualTo(reference.RegionMeans[i].B).Within(0.005f));
        }
    }

    [Test]
    public void Sample_ColorShiftedImage_MapsObservedToReference()
    {
        // Arrange
        var (reference, baseImage) = MakeFlatScene();
        var user = SyntheticScenes.ColorShift(baseImage);

        // Act
        var samples = new MeanRegionSampler().Sample(user, reference, new AlignmentResult(0, 0, 1, false));

        // Assert: reference side keeps the original color; observed side carries the shift.
        Assert.That(samples, Has.Count.EqualTo(3));
        Assert.That(samples[0].Reference.R, Is.EqualTo(reference.RegionMeans[0].R).Within(5e-5f));
        Assert.That(samples[0].Observed.R, Is.Not.EqualTo(samples[0].Reference.R).Within(0.005f));
    }

    [Test]
    public void Sample_DropsContaminatedRegion()
    {
        // Arrange: fill half of region 1's user block with a wildly different color (particle/HUD flash).
        var (reference, baseImage) = MakeFlatScene();
        var user = baseImage.Clone();
        SyntheticScenes.FillRect(user, 158, 68, 24, 12, 255, 255, 255);

        // Act
        var samples = new MeanRegionSampler().Sample(user, reference, new AlignmentResult(0, 0, 1, false));

        // Assert
        Assert.That(samples, Has.Count.EqualTo(2));
    }

    [Test]
    public void Sample_SkipsRegionShiftedOutOfBounds()
    {
        // Arrange
        var image = SyntheticScenes.Scene(320, 240, seed: 32, rectangles: 0);
        var reference = new ReferenceImage("edge", "Edge", image, [new RegionDefinition(300, 100, 16, 16)]);

        // Act
        var samples = new MeanRegionSampler().Sample(image.Clone(), reference, new AlignmentResult(8, 0, 1, false));

        // Assert
        Assert.That(samples, Is.Empty);
    }

    [Test]
    public void Sample_DownWeightsNoisyRegions()
    {
        // Arrange
        var (reference, baseImage) = MakeFlatScene();
        var noisy = SyntheticScenes.AddNoise(baseImage, amplitude: 4, seed: 33);

        // Act
        var clean = new MeanRegionSampler().Sample(baseImage.Clone(), reference, new AlignmentResult(0, 0, 1, false));
        var degraded = new MeanRegionSampler().Sample(noisy, reference, new AlignmentResult(0, 0, 1, false));

        // Assert
        Assert.That(degraded, Has.Count.EqualTo(clean.Count));
        for (int i = 0; i < clean.Count; i++)
            Assert.That(degraded[i].Weight, Is.LessThan(clean[i].Weight), $"Region {i}");
    }

    [Test]
    public void ReferenceImage_RejectsOutOfBoundsRegion()
    {
        var image = SyntheticScenes.Scene(64, 64, seed: 34, rectangles: 0);
        Assert.Throws<ArgumentException>(() =>
            new ReferenceImage("bad", "Bad", image, [new RegionDefinition(60, 60, 16, 16)]));
    }
}
