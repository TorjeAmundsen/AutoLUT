using AutoLUT.Core.Fitting;

namespace AutoLUT.Core.Lut;

public interface ILutGenerator
{
    Lut3D Generate(IColorTransform transform);
}
