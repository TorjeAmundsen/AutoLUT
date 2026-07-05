using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;

namespace UnitTests;

public class LutApplierTests
{
    [Test]
    public void IdentityLut_IsNearNoOp()
    {
        // Arrange
        var applier = new ObsLutApplier(TestImages.LoadTemplate());
        var source = TestImages.Random(64, 48, seed: 42);

        // Act
        var result = applier.Apply(source);

        // Assert: the baked identity LUT quantizes lattice values to 8 bits, so allow 1 LSB.
        for (int i = 0; i < source.Pixels.Length; i++)
        {
            int diff = Math.Abs(source.Pixels[i] - result.Pixels[i]);
            Assert.That(diff, Is.LessThanOrEqualTo(1),
                $"Pixel byte {i}: {source.Pixels[i]} -> {result.Pixels[i]} (diff {diff})");
        }
    }

    [Test]
    public void ConstantLut_MapsEverythingToThatColor()
    {
        // Arrange
        var template = TestImages.LoadTemplate();
        var lut = new TransformLutGenerator().Generate(new ConstantTransform(new Rgb(0.25f, 0.5f, 0.75f)));
        var baked = new ObsLutWriter().Bake(lut, template);
        var applier = new ObsLutApplier(baked);

        // Act
        var result = applier.Apply(TestImages.Random(16, 16, seed: 7));

        // Assert
        byte r = (byte)MathF.Round(0.25f * 255f);
        byte g = (byte)MathF.Round(0.5f * 255f);
        byte b = (byte)MathF.Round(0.75f * 255f);
        for (int i = 0; i < result.Pixels.Length; i += 3)
        {
            Assert.That(result.Pixels[i], Is.EqualTo(r));
            Assert.That(result.Pixels[i + 1], Is.EqualTo(g));
            Assert.That(result.Pixels[i + 2], Is.EqualTo(b));
        }
    }

    [Test]
    public void Apply_MatchesNaiveLinearSpaceTrilinearReference()
    {
        // Arrange: an independent re-implementation of OBS's sampling math (sRGB-decode, trilinear
        // in linear light, re-encode) computed in double precision, compared against the applier.
        var lutImage = TestImages.Random(512, 512, seed: 123);
        var applier = new ObsLutApplier(lutImage);
        var source = TestImages.Random(32, 32, seed: 456);

        // Act
        var result = applier.Apply(source);

        // Assert
        for (int i = 0; i < source.Pixels.Length; i += 3)
        {
            byte[] expected = ReferenceSample(lutImage, source.Pixels[i], source.Pixels[i + 1], source.Pixels[i + 2]);
            for (int c = 0; c < 3; c++)
            {
                int diff = Math.Abs(expected[c] - result.Pixels[i + c]);
                Assert.That(diff, Is.LessThanOrEqualTo(1),
                    $"Pixel {i / 3} channel {c}: expected {expected[c]}, got {result.Pixels[i + c]}");
            }
        }
    }

    [Test]
    public void Applier_RejectsWrongLutImageSize()
    {
        Assert.Throws<ArgumentException>(() => new ObsLutApplier(TestImages.Random(512, 256, 1)));
    }

    private static byte[] ReferenceSample(RawImage lutImage, byte r, byte g, byte b)
    {
        double cr = r * 63.0 / 255.0, cg = g * 63.0 / 255.0, cb = b * 63.0 / 255.0;
        int r0 = Math.Min((int)cr, 62), g0 = Math.Min((int)cg, 62), b0 = Math.Min((int)cb, 62);
        double fr = cr - r0, fg = cg - g0, fb = cb - b0;

        var acc = new double[3];
        for (int db = 0; db <= 1; db++)
        {
            for (int dg = 0; dg <= 1; dg++)
            {
                for (int dr = 0; dr <= 1; dr++)
                {
                    double w = (db == 0 ? 1 - fb : fb) * (dg == 0 ? 1 - fg : fg) * (dr == 0 ? 1 - fr : fr);
                    int slice = b0 + db;
                    var (pr, pg, pb) = lutImage.GetPixel(slice % 8 * 64 + r0 + dr, slice / 8 * 64 + g0 + dg);
                    acc[0] += w * SrgbToLinear(pr / 255.0);
                    acc[1] += w * SrgbToLinear(pg / 255.0);
                    acc[2] += w * SrgbToLinear(pb / 255.0);
                }
            }
        }

        return [EncodeByte(acc[0]), EncodeByte(acc[1]), EncodeByte(acc[2])];
    }

    private static double SrgbToLinear(double u) => u <= 0.04045 ? u / 12.92 : Math.Pow((u + 0.055) / 1.055, 2.4);

    private static double LinearToSrgb(double u) => u <= 0.0031308 ? 12.92 * u : 1.055 * Math.Pow(u, 1 / 2.4) - 0.055;

    private static byte EncodeByte(double linear) => (byte)Math.Clamp(Math.Round(LinearToSrgb(linear) * 255.0), 0, 255);

    private sealed record ConstantTransform(Rgb Color) : IColorTransform
    {
        public Rgb Apply(Rgb srgb) => Color;
    }
}
