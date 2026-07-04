using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;

namespace AutoLUT.Core.Alignment;

/// <summary>
/// Estimates a global translation (±10 px) between a user capture and its reference by exhaustive
/// ZNCC search on downsampled luminance, refined ±1 px at full resolution. ZNCC's mean subtraction
/// makes it robust to the very color shift the calibration is meant to fix.
/// </summary>
public sealed class TranslationSearchAligner : IAligner
{
    public const int MaxOffset = 10;

    private const int CoarseMargin = 16;
    private const int FineMargin = 24;

    public AlignmentResult Align(RawImage user, ReferenceImage reference)
    {
        if (user.Width != reference.Image.Width || user.Height != reference.Image.Height)
            throw new ArgumentException("User image dimensions must match the reference.", nameof(user));

        var lumaUser = ImageOps.Luminance(user);
        var lumaReference = ImageOps.Luminance(reference.Image);
        var halfUser = ImageOps.Downsample2x(lumaUser);
        var halfReference = ImageOps.Downsample2x(lumaReference);

        int coarseRange = MaxOffset / 2;
        (int dx, int dy, double score) best = (0, 0, double.NegativeInfinity);
        for (int dy = -coarseRange; dy <= coarseRange; dy++)
        for (int dx = -coarseRange; dx <= coarseRange; dx++)
        {
            double score = ImageOps.Zncc(halfReference, halfUser, dx, dy, CoarseMargin);
            if (score > best.score)
                best = (dx, dy, score);
        }

        (int dx, int dy, double score) fine = (0, 0, double.NegativeInfinity);
        for (int dy = 2 * best.dy - 2; dy <= 2 * best.dy + 2; dy++)
        for (int dx = 2 * best.dx - 2; dx <= 2 * best.dx + 2; dx++)
        {
            int cx = Math.Clamp(dx, -MaxOffset, MaxOffset);
            int cy = Math.Clamp(dy, -MaxOffset, MaxOffset);
            double score = ImageOps.Zncc(lumaReference, lumaUser, cx, cy, FineMargin);
            if (score > fine.score)
                fine = (cx, cy, score);
        }

        bool atBoundary = Math.Abs(fine.dx) == MaxOffset || Math.Abs(fine.dy) == MaxOffset;
        return new AlignmentResult(fine.dx, fine.dy, fine.score, atBoundary);
    }
}
