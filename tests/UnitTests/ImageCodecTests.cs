using AutoLUT.Core.Imaging;

namespace UnitTests;

public class ImageCodecTests
{
    [Test]
    public void EncodeDecode_RoundTripsPixelsExactly()
    {
        // Arrange
        var codec = new SkiaImageCodec();
        var original = TestImages.Random(33, 17, seed: 99);

        // Act
        using var stream = new MemoryStream();
        codec.EncodePng(original, stream);
        stream.Position = 0;
        var decoded = codec.Decode(stream);

        // Assert
        Assert.That(decoded.Width, Is.EqualTo(original.Width));
        Assert.That(decoded.Height, Is.EqualTo(original.Height));
        Assert.That(decoded.Pixels, Is.EqualTo(original.Pixels));
    }

    [Test]
    public void Decode_RejectsGarbage()
    {
        // Arrange
        var codec = new SkiaImageCodec();
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        // Act + Assert
        Assert.Throws<InvalidDataException>(() => codec.Decode(stream));
    }
}
