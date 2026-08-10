using SkiaSharp;

namespace AutoLUT.Core.Imaging;

public sealed class SkiaImageCodec : IImageCodec
{
    public RawImage Decode(Stream stream) => DecodeWithAlpha(stream).Image;

    public (RawImage Image, byte[]? Alpha) DecodeWithAlpha(Stream stream)
    {
        using var data = SKData.Create(stream) ?? throw new InvalidDataException("Failed to read image data.");
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Not a valid image file.");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            throw new InvalidDataException("Failed to decode image pixels.");
        }

        var image = new RawImage(info.Width, info.Height);
        ReadOnlySpan<byte> rgba = bitmap.GetPixelSpan();
        byte[] rgb = image.Pixels;
        var alpha = new byte[info.Width * info.Height];
        bool opaque = true;
        for (int src = 0, dst = 0; dst < rgb.Length; src += 4, dst += 3)
        {
            rgb[dst] = rgba[src];
            rgb[dst + 1] = rgba[src + 1];
            rgb[dst + 2] = rgba[src + 2];
            byte alphaValue = rgba[src + 3];
            alpha[dst / 3] = alphaValue;
            opaque &= alphaValue == 255;
        }

        return (image, opaque ? null : alpha);
    }

    public void EncodePng(RawImage image, Stream stream) => EncodePng(image, null, stream);

    public void EncodePng(RawImage image, byte[]? alpha, Stream stream)
    {
        var alphaType = alpha is null ? SKAlphaType.Opaque : SKAlphaType.Unpremul;
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, alphaType);
        using var bitmap = new SKBitmap(info);
        Span<byte> rgba = bitmap.GetPixelSpan();
        byte[] rgb = image.Pixels;
        for (int src = 0, dst = 0; src < rgb.Length; src += 3, dst += 4)
        {
            rgba[dst] = rgb[src];
            rgba[dst + 1] = rgb[src + 1];
            rgba[dst + 2] = rgb[src + 2];
            rgba[dst + 3] = alpha?[src / 3] ?? 255;
        }

        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        encoded.SaveTo(stream);
    }
}
