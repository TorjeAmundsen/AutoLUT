using AutoLUT.Core.ColorScience;

namespace AutoLUT.Core.Fitting;

/// <summary>output = Curves(M · [r, g, b, 1]) on sRGB-encoded values.</summary>
public sealed class AffineCurvesTransform : IColorTransform
{
    private readonly float[] _matrix; // row-major 3x4
    private readonly MonotoneCurve _curveR;
    private readonly MonotoneCurve _curveG;
    private readonly MonotoneCurve _curveB;

    public AffineCurvesTransform(ReadOnlySpan<float> matrix3x4, MonotoneCurve curveR, MonotoneCurve curveG, MonotoneCurve curveB)
    {
        if (matrix3x4.Length != 12)
        {
            throw new ArgumentException("Expected a row-major 3x4 matrix (12 values).", nameof(matrix3x4));
        }

        _matrix = matrix3x4.ToArray();
        _curveR = curveR;
        _curveG = curveG;
        _curveB = curveB;
    }

    public IReadOnlyList<float> Matrix => _matrix;

    public Rgb Apply(Rgb srgb)
    {
        float r = _matrix[0] * srgb.R + _matrix[1] * srgb.G + _matrix[2] * srgb.B + _matrix[3];
        float g = _matrix[4] * srgb.R + _matrix[5] * srgb.G + _matrix[6] * srgb.B + _matrix[7];
        float b = _matrix[8] * srgb.R + _matrix[9] * srgb.G + _matrix[10] * srgb.B + _matrix[11];
        return new Rgb(_curveR.Evaluate(r), _curveG.Evaluate(g), _curveB.Evaluate(b));
    }
}
