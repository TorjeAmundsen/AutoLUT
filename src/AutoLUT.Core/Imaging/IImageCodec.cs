namespace AutoLUT.Core.Imaging;

public interface IImageCodec
{
    /// <summary>Decodes an image stream to RGB24. Throws <see cref="InvalidDataException"/> on malformed input.</summary>
    RawImage Decode(Stream stream);

    /// <summary>Decodes to RGB24 plus a straight (unpremultiplied) alpha plane; Alpha is null when fully opaque.</summary>
    (RawImage Image, byte[]? Alpha) DecodeWithAlpha(Stream stream);

    void EncodePng(RawImage image, Stream stream);

    /// <summary>Encodes RGB24 with an optional straight alpha plane; null alpha encodes opaque.</summary>
    void EncodePng(RawImage image, byte[]? alpha, Stream stream);
}
