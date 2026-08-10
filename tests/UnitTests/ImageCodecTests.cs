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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded.Width, Is.EqualTo(original.Width));
            Assert.That(decoded.Height, Is.EqualTo(original.Height));
            Assert.That(decoded.Pixels, Is.EqualTo(original.Pixels));
        }
    }

    [Test]
    public void EncodeDecode_WithAlpha_RoundTripsRgbAndAlphaExactly()
    {
        // Arrange
        var codec = new SkiaImageCodec();
        var original = TestImages.Random(33, 17, seed: 7);
        var alpha = new byte[33 * 17];
        new Random(8).NextBytes(alpha);
        alpha[0] = 0;    // fully transparent (AutoSplit mask region)
        alpha[1] = 255;  // fully opaque

        // Act
        using var stream = new MemoryStream();
        codec.EncodePng(original, alpha, stream);
        stream.Position = 0;
        var (decoded, decodedAlpha) = codec.DecodeWithAlpha(stream);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded.Pixels, Is.EqualTo(original.Pixels));
            Assert.That(decodedAlpha, Is.EqualTo(alpha));
        }
    }

    [Test]
    public void DecodeWithAlpha_OpaqueImage_ReturnsNullAlpha()
    {
        // Arrange
        var codec = new SkiaImageCodec();
        var original = TestImages.Random(9, 5, seed: 3);

        // Act
        using var stream = new MemoryStream();
        codec.EncodePng(original, null, stream);
        stream.Position = 0;
        var (decoded, decodedAlpha) = codec.DecodeWithAlpha(stream);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded.Pixels, Is.EqualTo(original.Pixels));
            Assert.That(decodedAlpha, Is.Null);
        }
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
