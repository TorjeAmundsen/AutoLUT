using AutoLUT.Core;
using AutoLUT.Core.Imaging;

namespace UnitTests;

internal static class TestImages
{
    public static RawImage LoadTemplate()
    {
        using var stream = EmbeddedAssets.OpenOriginalLutTemplate();
        return new SkiaImageCodec().Decode(stream);
    }

    /// <summary>Deterministic pseudo-random RGB image.</summary>
    public static RawImage Random(int width, int height, int seed)
    {
        var rng = new Random(seed);
        var image = new RawImage(width, height);
        rng.NextBytes(image.Pixels);
        return image;
    }
}
