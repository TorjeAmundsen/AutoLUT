using AutoLUT.Core.Imaging;

namespace UnitTests;

/// <summary>Synthetic game-like scenes and analog-capture degradations for alignment/sampling tests.</summary>
internal static class SyntheticScenes
{
    /// <summary>Gradient background plus seeded random solid rectangles (flat regions with edges between them).</summary>
    public static RawImage Scene(int width, int height, int seed, int rectangles = 24)
    {
        var rng = new Random(seed);
        var image = new RawImage(width, height);
        for (int y = 0; y < height; y++)
        {
            var row = image.Row(y);
            for (int x = 0; x < width; x++)
            {
                row[x * 3] = (byte)(40 + 150 * x / width);
                row[x * 3 + 1] = (byte)(60 + 120 * y / height);
                row[x * 3 + 2] = (byte)(80 + 80 * (x + y) / (width + height));
            }
        }

        for (int i = 0; i < rectangles; i++)
        {
            int rw = rng.Next(20, 60), rh = rng.Next(20, 60);
            int rx = rng.Next(0, width - rw), ry = rng.Next(0, height - rh);
            byte r = (byte)rng.Next(20, 236), g = (byte)rng.Next(20, 236), b = (byte)rng.Next(20, 236);
            FillRect(image, rx, ry, rw, rh, r, g, b);
        }

        return image;
    }

    public static void FillRect(RawImage image, int x, int y, int width, int height, byte r, byte g, byte b)
    {
        for (int row = y; row < y + height; row++)
        {
            var span = image.Row(row);
            for (int col = x; col < x + width; col++)
            {
                span[col * 3] = r;
                span[col * 3 + 1] = g;
                span[col * 3 + 2] = b;
            }
        }
    }

    /// <summary>Shifts content by (dx, dy); exposed border pixels clamp to the edge.</summary>
    public static RawImage Translate(RawImage source, int dx, int dy)
    {
        var result = new RawImage(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            int sx = Math.Clamp(x - dx, 0, source.Width - 1);
            int sy = Math.Clamp(y - dy, 0, source.Height - 1);
            var (r, g, b) = source.GetPixel(sx, sy);
            result.SetPixel(x, y, r, g, b);
        }

        return result;
    }

    /// <summary>3x3 box blur, applied <paramref name="passes"/> times (2 passes approximate a Gaussian).</summary>
    public static RawImage BoxBlur(RawImage source, int passes = 2)
    {
        var current = source;
        for (int pass = 0; pass < passes; pass++)
        {
            var next = new RawImage(current.Width, current.Height);
            for (int y = 0; y < current.Height; y++)
            for (int x = 0; x < current.Width; x++)
            {
                int sumR = 0, sumG = 0, sumB = 0, count = 0;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int sx = x + ox, sy = y + oy;
                    if (sx < 0 || sy < 0 || sx >= current.Width || sy >= current.Height)
                        continue;
                    var (r, g, b) = current.GetPixel(sx, sy);
                    sumR += r;
                    sumG += g;
                    sumB += b;
                    count++;
                }

                next.SetPixel(x, y, (byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
            }

            current = next;
        }

        return current;
    }

    public static RawImage AddNoise(RawImage source, int amplitude, int seed)
    {
        var rng = new Random(seed);
        var result = source.Clone();
        for (int i = 0; i < result.Pixels.Length; i++)
            result.Pixels[i] = (byte)Math.Clamp(result.Pixels[i] + rng.Next(-amplitude, amplitude + 1), 0, 255);
        return result;
    }

    /// <summary>A strong capture-like color shift: per-channel gain + gamma.</summary>
    public static RawImage ColorShift(RawImage source)
    {
        var result = new RawImage(source.Width, source.Height);
        for (int i = 0; i < source.Pixels.Length; i += 3)
        {
            result.Pixels[i] = ShiftChannel(source.Pixels[i], 0.85f, 0.9f);
            result.Pixels[i + 1] = ShiftChannel(source.Pixels[i + 1], 1.05f, 1.1f);
            result.Pixels[i + 2] = ShiftChannel(source.Pixels[i + 2], 0.95f, 1.2f);
        }

        return result;
    }

    private static byte ShiftChannel(byte value, float gain, float gamma) =>
        (byte)Math.Clamp(MathF.Round(255f * MathF.Pow(Math.Clamp(value / 255f * gain, 0f, 1f), gamma)), 0f, 255f);
}
