namespace AutoLUT.Core.Imaging;

public static class ImageOps
{
    /// <summary>Rec.709 luma of the sRGB-encoded bytes, range [0,1]. Good enough for alignment; no linearization needed.</summary>
    public static GrayImage Luminance(RawImage image)
    {
        var gray = new GrayImage(image.Width, image.Height);
        byte[] rgb = image.Pixels;
        for (int i = 0, p = 0; p < rgb.Length; i++, p += 3)
            gray.Values[i] = (0.2126f * rgb[p] + 0.7152f * rgb[p + 1] + 0.0722f * rgb[p + 2]) / 255f;
        return gray;
    }

    /// <summary>2x box downsample; truncates odd edges.</summary>
    public static GrayImage Downsample2x(GrayImage image)
    {
        var result = new GrayImage(image.Width / 2, image.Height / 2);
        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++)
        {
            result[x, y] = 0.25f * (image[2 * x, 2 * y] + image[2 * x + 1, 2 * y]
                + image[2 * x, 2 * y + 1] + image[2 * x + 1, 2 * y + 1]);
        }

        return result;
    }

    /// <summary>Sobel gradient magnitude; border pixels are zero.</summary>
    public static GrayImage SobelMagnitude(GrayImage image)
    {
        var result = new GrayImage(image.Width, image.Height);
        for (int y = 1; y < image.Height - 1; y++)
        for (int x = 1; x < image.Width - 1; x++)
        {
            float gx = image[x + 1, y - 1] + 2f * image[x + 1, y] + image[x + 1, y + 1]
                - image[x - 1, y - 1] - 2f * image[x - 1, y] - image[x - 1, y + 1];
            float gy = image[x - 1, y + 1] + 2f * image[x, y + 1] + image[x + 1, y + 1]
                - image[x - 1, y - 1] - 2f * image[x, y - 1] - image[x + 1, y - 1];
            result[x, y] = MathF.Sqrt(gx * gx + gy * gy);
        }

        return result;
    }

    /// <summary>
    /// Zero-mean normalized cross-correlation between <paramref name="reference"/> (interior, excluding
    /// <paramref name="margin"/>) and <paramref name="candidate"/> sampled at (x+dx, y+dy).
    /// Mean subtraction makes the score robust to global color/brightness shifts.
    /// </summary>
    public static double Zncc(GrayImage reference, GrayImage candidate, int dx, int dy, int margin)
    {
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
            throw new ArgumentException("Images must have identical dimensions.");
        if (margin < Math.Max(Math.Abs(dx), Math.Abs(dy)))
            throw new ArgumentException("Margin must cover the offset.", nameof(margin));
        if (reference.Width <= 2 * margin || reference.Height <= 2 * margin)
            throw new ArgumentException("Margin leaves no interior.", nameof(margin));

        double sumA = 0, sumB = 0;
        int count = 0;
        for (int y = margin; y < reference.Height - margin; y++)
        for (int x = margin; x < reference.Width - margin; x++)
        {
            sumA += reference[x, y];
            sumB += candidate[x + dx, y + dy];
            count++;
        }

        double meanA = sumA / count, meanB = sumB / count;
        double cross = 0, varA = 0, varB = 0;
        for (int y = margin; y < reference.Height - margin; y++)
        for (int x = margin; x < reference.Width - margin; x++)
        {
            double a = reference[x, y] - meanA;
            double b = candidate[x + dx, y + dy] - meanB;
            cross += a * b;
            varA += a * a;
            varB += b * b;
        }

        double denominator = Math.Sqrt(varA * varB);
        return denominator < 1e-12 ? 0 : cross / denominator;
    }
}
