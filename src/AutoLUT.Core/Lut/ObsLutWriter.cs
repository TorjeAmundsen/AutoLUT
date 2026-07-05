using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.Lut;

/// <summary>
/// Writes a 64^3 LUT into OBS's original.png layout: an 8x8 grid of 64x64 tiles,
/// tile index (row-major) = blue, x within tile = red, y = green.
/// OBS reads this back in make_clut_texture_png (plugins/obs-filters/color-grade-filter.c).
/// </summary>
public sealed class ObsLutWriter : ILutWriter
{
    private const int TileSize = 64;
    private const int TilesPerRow = 8;
    private const int ImageSize = TileSize * TilesPerRow;

    public RawImage Bake(Lut3D lut, RawImage template)
    {
        if (lut.Size != TileSize)
        {
            throw new ArgumentException($"Expected a {TileSize}^3 LUT, got {lut.Size}^3.", nameof(lut));
        }

        if (template.Width != ImageSize || template.Height != ImageSize)
        {
            throw new ArgumentException($"Expected a {ImageSize}x{ImageSize} template, got {template.Width}x{template.Height}.", nameof(template));
        }

        var output = template.Clone();
        for (int b = 0; b < TileSize; b++)
        {
            int tileX = b % TilesPerRow * TileSize;
            int tileY = b / TilesPerRow * TileSize;
            for (int g = 0; g < TileSize; g++)
            {
                for (int r = 0; r < TileSize; r++)
                {
                    var v = lut[r, g, b];
                    output.SetPixel(tileX + r, tileY + g, ToByte(v.R), ToByte(v.G), ToByte(v.B));
                }
            }
        }

        return output;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
}
