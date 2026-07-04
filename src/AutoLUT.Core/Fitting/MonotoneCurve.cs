namespace AutoLUT.Core.Fitting;

/// <summary>
/// Non-decreasing piecewise-linear curve with uniformly spaced knots on [0,1].
/// Outside [0,1] it extrapolates linearly using the slope of the nearest segment.
/// </summary>
public sealed class MonotoneCurve
{
    private readonly float[] _knotValues;
    private readonly float _step;

    public MonotoneCurve(ReadOnlySpan<float> knotValues)
    {
        if (knotValues.Length < 2)
            throw new ArgumentException("At least two knots required.", nameof(knotValues));
        for (int i = 1; i < knotValues.Length; i++)
            if (knotValues[i] < knotValues[i - 1])
                throw new ArgumentException("Knot values must be non-decreasing.", nameof(knotValues));

        _knotValues = knotValues.ToArray();
        _step = 1f / (knotValues.Length - 1);
    }

    public int KnotCount => _knotValues.Length;

    public IReadOnlyList<float> KnotValues => _knotValues;

    public static MonotoneCurve Identity(int knotCount)
    {
        var values = new float[knotCount];
        for (int i = 0; i < knotCount; i++)
            values[i] = i / (float)(knotCount - 1);
        return new MonotoneCurve(values);
    }

    public float Evaluate(float x)
    {
        int last = _knotValues.Length - 1;
        if (x <= 0f)
            return _knotValues[0] + x * (_knotValues[1] - _knotValues[0]) / _step;
        if (x >= 1f)
            return _knotValues[last] + (x - 1f) * (_knotValues[last] - _knotValues[last - 1]) / _step;

        float position = x / _step;
        int segment = Math.Min((int)position, last - 1);
        float t = position - segment;
        return _knotValues[segment] + t * (_knotValues[segment + 1] - _knotValues[segment]);
    }
}
