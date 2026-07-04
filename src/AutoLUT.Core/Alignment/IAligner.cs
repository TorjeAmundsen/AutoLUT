using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;

namespace AutoLUT.Core.Alignment;

public interface IAligner
{
    AlignmentResult Align(RawImage user, ReferenceImage reference);
}

/// <summary>User pixel (x+Dx, y+Dy) corresponds to reference pixel (x, y). Score is ZNCC in [-1,1].</summary>
public readonly record struct AlignmentResult(int Dx, int Dy, double Score, bool AtSearchBoundary);
