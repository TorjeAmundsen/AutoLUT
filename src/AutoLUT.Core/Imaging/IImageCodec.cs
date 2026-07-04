namespace AutoLUT.Core.Imaging;

public interface IImageCodec
{
    /// <summary>Decodes a PNG stream to RGB24. Throws <see cref="InvalidDataException"/> on malformed input.</summary>
    RawImage Decode(Stream stream);

    void EncodePng(RawImage image, Stream stream);
}
