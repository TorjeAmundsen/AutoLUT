namespace AutoLUT.Core.Imaging;

public interface IImageCodec
{
    /// <summary>Decodes an image stream to RGB24. Throws <see cref="InvalidDataException"/> on malformed input.</summary>
    RawImage Decode(Stream stream);

    void EncodePng(RawImage image, Stream stream);
}
