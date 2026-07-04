using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.Lut;

public interface ILutWriter
{
    /// <summary>Bakes the LUT into a copy of the template image, replacing pixel values only.</summary>
    RawImage Bake(Lut3D lut, RawImage template);
}
