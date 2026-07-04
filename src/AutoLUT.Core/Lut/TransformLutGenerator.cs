using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;

namespace AutoLUT.Core.Lut;

public sealed class TransformLutGenerator : ILutGenerator
{
    public Lut3D Generate(IColorTransform transform)
    {
        var lut = new Lut3D();
        int size = lut.Size;
        float scale = 1f / (size - 1);
        for (int b = 0; b < size; b++)
        for (int g = 0; g < size; g++)
        for (int r = 0; r < size; r++)
        {
            var input = new Rgb(r * scale, g * scale, b * scale);
            lut[r, g, b] = transform.Apply(input).Clamp01();
        }

        return lut;
    }
}
