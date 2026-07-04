using AutoLUT.Core.Alignment;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;

namespace AutoLUT.Core.Sampling;

public interface IRegionSampler
{
    IReadOnlyList<ColorCorrespondence> Sample(RawImage user, ReferenceImage reference, AlignmentResult alignment);
}
