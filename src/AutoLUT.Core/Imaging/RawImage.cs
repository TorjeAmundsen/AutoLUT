namespace AutoLUT.Core.Imaging;

/// <summary>Uncompressed RGB24 image, row-major, no padding.</summary>
public sealed class RawImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RawImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        if (pixels.Length != width * height * 3)
        {
            throw new ArgumentException($"Pixel buffer length {pixels.Length} does not match {width}x{height} RGB24.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public RawImage(int width, int height)
        : this(width, height, new byte[width * height * 3])
    {
    }

    public Span<byte> Row(int y) => Pixels.AsSpan(y * Width * 3, Width * 3);

    public (byte R, byte G, byte B) GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 3;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        int i = (y * Width + x) * 3;
        Pixels[i] = r;
        Pixels[i + 1] = g;
        Pixels[i + 2] = b;
    }

    public RawImage Clone() => new(Width, Height, (byte[])Pixels.Clone());
}
