namespace AutoLUT.Core.Imaging;

/// <summary>Single-channel float image, row-major.</summary>
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }
    public float[] Values { get; }

    public GrayImage(int width, int height)
    {
        Width = width;
        Height = height;
        Values = new float[width * height];
    }

    public float this[int x, int y]
    {
        get => Values[y * Width + x];
        set => Values[y * Width + x] = value;
    }
}
