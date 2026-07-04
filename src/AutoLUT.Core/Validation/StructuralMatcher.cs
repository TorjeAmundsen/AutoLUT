using AutoLUT.Core.Alignment;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;

namespace AutoLUT.Core.Validation;

/// <summary>
/// Color-invariant wrong-savestate detection: ZNCC of Sobel gradient-magnitude maps on
/// 4x-downsampled luminance. Gradient structure survives monotone color shifts, blur and chroma
/// bleed, while a different scene decorrelates.
/// </summary>
public static class StructuralMatcher
{
    public const double DefaultThreshold = 0.5;

    public static double Score(RawImage user, ReferenceImage reference, AlignmentResult alignment)
    {
        var userQuarter = ImageOps.Downsample2x(ImageOps.Downsample2x(ImageOps.Luminance(user)));
        var referenceQuarter = ImageOps.Downsample2x(ImageOps.Downsample2x(ImageOps.Luminance(reference.Image)));

        var userEdges = ImageOps.SobelMagnitude(userQuarter);
        var referenceEdges = ImageOps.SobelMagnitude(referenceQuarter);

        int dx = (int)Math.Round(alignment.Dx / 4.0);
        int dy = (int)Math.Round(alignment.Dy / 4.0);
        return ImageOps.Zncc(referenceEdges, userEdges, dx, dy, margin: 8);
    }
}
