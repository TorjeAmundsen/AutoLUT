using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.Lut;

/// <summary>
/// Applies a baked OBS LUT image to an RGB24 image, replicating OBS's color grade filter
/// (plugins/obs-filters/color-grade-filter.c + data/color_grade_filter.effect, technique DrawAlpha3D
/// with amount 1.0 - the default for PNG LUTs) so the preview matches what OBS renders.
///
/// OBS binds the LUT volume texture as sRGB, so the GPU decodes texels to linear light BEFORE
/// trilinear filtering, and the result is re-encoded to sRGB on output. The shader's coordinate math
/// (uvw = nonlinear * 63/64 + 0.5/64, sampled with texel-center offset) reduces to a lattice
/// coordinate of byte/255 * 63. Alpha handling is a no-op for opaque RGB24 input.
/// GPU filtering precision differs slightly from float math, so the match is within ~1 LSB, not bit-exact.
/// </summary>
public sealed class ObsLutApplier
{
    private const int Size = 64;
    private const int TilesPerRow = 8;
    private const int ImageSize = Size * TilesPerRow;

    private readonly float[] _linearLattice; // ((b*64+g)*64+r)*3, sRGB-decoded to linear
    private readonly int[] _index0 = new int[256];
    private readonly float[] _fraction = new float[256];

    public ObsLutApplier(RawImage lutImage)
    {
        if (lutImage.Width != ImageSize || lutImage.Height != ImageSize)
        {
            throw new ArgumentException($"Expected a {ImageSize}x{ImageSize} LUT image, got {lutImage.Width}x{lutImage.Height}.", nameof(lutImage));
        }

        Span<float> srgbToLinear = stackalloc float[256];
        for (int v = 0; v < 256; v++)
        {
            srgbToLinear[v] = ColorSpace.SrgbToLinear(v / 255f);
        }

        _linearLattice = new float[Size * Size * Size * 3];
        for (int b = 0; b < Size; b++)
        {
            int tileX = b % TilesPerRow * Size;
            int tileY = b / TilesPerRow * Size;
            for (int g = 0; g < Size; g++)
            {
                for (int r = 0; r < Size; r++)
                {
                    var (pr, pg, pb) = lutImage.GetPixel(tileX + r, tileY + g);
                    int i = ((b * Size + g) * Size + r) * 3;
                    _linearLattice[i] = srgbToLinear[pr];
                    _linearLattice[i + 1] = srgbToLinear[pg];
                    _linearLattice[i + 2] = srgbToLinear[pb];
                }
            }
        }

        for (int v = 0; v < 256; v++)
        {
            float c = v * 63f / 255f;
            int i0 = Math.Min((int)c, Size - 2);
            _index0[v] = i0;
            _fraction[v] = c - i0;
        }
    }

    public RawImage Apply(RawImage source)
    {
        var output = new RawImage(source.Width, source.Height);
        byte[] src = source.Pixels;
        byte[] dst = output.Pixels;
        for (int i = 0; i < src.Length; i += 3)
        {
            var (r, g, b) = Sample(src[i], src[i + 1], src[i + 2]);
            dst[i] = EncodeByte(r);
            dst[i + 1] = EncodeByte(g);
            dst[i + 2] = EncodeByte(b);
        }

        return output;
    }

    /// <summary>Trilinear sample in linear light; returns linear RGB.</summary>
    private (float R, float G, float B) Sample(byte r, byte g, byte b)
    {
        int r0 = _index0[r], g0 = _index0[g], b0 = _index0[b];
        float fr = _fraction[r], fg = _fraction[g], fb = _fraction[b];

        float outR = 0f, outG = 0f, outB = 0f;
        for (int db = 0; db <= 1; db++)
        {
            float wb = db == 0 ? 1f - fb : fb;
            for (int dg = 0; dg <= 1; dg++)
            {
                float wg = dg == 0 ? 1f - fg : fg;
                for (int dr = 0; dr <= 1; dr++)
                {
                    float w = wb * wg * (dr == 0 ? 1f - fr : fr);
                    int idx = (((b0 + db) * Size + g0 + dg) * Size + r0 + dr) * 3;
                    outR += w * _linearLattice[idx];
                    outG += w * _linearLattice[idx + 1];
                    outB += w * _linearLattice[idx + 2];
                }
            }
        }

        return (outR, outG, outB);
    }

    private static byte EncodeByte(float linear) =>
        (byte)Math.Clamp(MathF.Round(ColorSpace.LinearToSrgb(linear) * 255f), 0f, 255f);
}
