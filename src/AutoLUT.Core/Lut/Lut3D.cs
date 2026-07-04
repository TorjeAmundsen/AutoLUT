using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Lut;

/// <summary>
/// A cubic 3D LUT lattice of sRGB-encoded values in [0,1].
/// Storage order matches OBS's volume texture: index = ((b * Size + g) * Size + r).
/// </summary>
public sealed class Lut3D
{
    public const int DefaultSize = 64;

    public int Size { get; }

    private readonly float[] _values; // 3 floats per lattice point

    public Lut3D(int size = DefaultSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        Size = size;
        _values = new float[size * size * size * 3];
    }

    public Rgb this[int r, int g, int b]
    {
        get
        {
            int i = Index(r, g, b);
            return new Rgb(_values[i], _values[i + 1], _values[i + 2]);
        }
        set
        {
            int i = Index(r, g, b);
            _values[i] = value.R;
            _values[i + 1] = value.G;
            _values[i + 2] = value.B;
        }
    }

    private int Index(int r, int g, int b) => ((b * Size + g) * Size + r) * 3;
}
