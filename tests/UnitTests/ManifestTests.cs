using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;

namespace UnitTests;

public class ManifestTests
{
    [Test]
    public void LoadEmbedded_ReturnsPlaceholderReferences()
    {
        // Act
        var set = ManifestLoader.LoadEmbedded(new SkiaImageCodec());

        // Assert
        Assert.That(set.References, Has.Count.EqualTo(2));
        foreach (var reference in set.References)
        {
            Assert.That(reference.Image.Width, Is.EqualTo(640));
            Assert.That(reference.Image.Height, Is.EqualTo(480));
            Assert.That(reference.Regions, Has.Count.GreaterThan(100));
            Assert.That(reference.RegionMeans, Has.Count.EqualTo(reference.Regions.Count));
        }
    }

    [Test]
    public void Load_RejectsDimensionMismatch()
    {
        // Arrange: manifest declares 640x480 but the actual image is 320x240.
        var codec = new SkiaImageCodec();
        var manifest = new ReferenceManifest(1, [new ReferenceEntry("bad", "Bad", "bad.png", 640, 480, [])]);
        byte[] png;
        using (var stream = new MemoryStream())
        {
            codec.EncodePng(SyntheticScenes.Scene(320, 240, seed: 1), stream);
            png = stream.ToArray();
        }

        // Act + Assert
        Assert.Throws<InvalidDataException>(() =>
            ManifestLoader.Load(manifest, _ => new MemoryStream(png), codec));
    }
}
