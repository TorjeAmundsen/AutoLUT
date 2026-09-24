using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;

namespace UnitTests;

public class LutInverterTests
{
    [Test]
    public void IdentityLut_InvertThenReapply_ReproducesSourceExactly()
    {
        // Arrange: every color is reachable through the identity LUT, and the solve tolerance
        // is far below the final rounding step.
        var identity = new ObsLutApplier(TestImages.LoadTemplate());
        var inverter = new ObsLutInverter(identity);
        var source = TestImages.Random(32, 24, seed: 11);

        // Act
        var result = inverter.InvertThenApply(identity, source);

        // Assert
        Assert.That(result.Pixels, Is.EqualTo(source.Pixels));
    }

    [Test]
    public void SmoothLut_ApplyThenInvert_RoundTripsWithinQuantization()
    {
        // Arrange: a channel-scaling LUT (red 60%, green 80%) is smooth and injective, so the
        // inverse should recover the original up to the quantization the forward pass discarded.
        // Red loses about 40% of its levels, so up to 2 LSB there.
        var applier = new ObsLutApplier(ScaledLut(0.6f, 0.8f, 1f));
        var inverter = new ObsLutInverter(applier);
        var identity = new ObsLutApplier(TestImages.LoadTemplate());
        var source = TestImages.Random(32, 24, seed: 22);

        // Act
        var roundTripped = inverter.InvertThenApply(identity, applier.Apply(source));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            for (int i = 0; i < source.Pixels.Length; i++)
            {
                int diff = Math.Abs(source.Pixels[i] - roundTripped.Pixels[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(2),
                    $"Pixel byte {i}: {source.Pixels[i]} -> {roundTripped.Pixels[i]} (diff {diff})");
            }
        }
    }

    [Test]
    public void MigrationPipeline_MatchesApplyingNewLutToOriginal()
    {
        // Arrange: reference images were captured with an old LUT active. Reversing it and
        // applying the new LUT should match applying the new LUT to the never-corrected original.
        // The only losses are the old LUT's output quantization, baked into the reference, and
        // the final rounding.
        var oldApplier = new ObsLutApplier(ScaledLut(0.6f, 1f, 0.85f));
        var newApplier = new ObsLutApplier(ScaledLut(1f, 0.75f, 0.9f));
        var inverter = new ObsLutInverter(oldApplier);
        var original = TestImages.Random(32, 24, seed: 33);
        var reference = oldApplier.Apply(original);

        // Act
        var migrated = inverter.InvertThenApply(newApplier, reference);
        var expected = newApplier.Apply(original);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            for (int i = 0; i < expected.Pixels.Length; i++)
            {
                int diff = Math.Abs(expected.Pixels[i] - migrated.Pixels[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(1),
                    $"Pixel byte {i}: expected {expected.Pixels[i]}, got {migrated.Pixels[i]} (diff {diff})");
            }
        }
    }

    [Test]
    public void InvertThenApplyRows_InBands_MatchesWholeImage()
    {
        // Arrange: 23 rows in bands of 5 leaves a short final band.
        var oldApplier = new ObsLutApplier(ScaledLut(0.6f, 1f, 0.85f));
        var newApplier = new ObsLutApplier(ScaledLut(1f, 0.75f, 0.9f));
        var source = TestImages.Random(19, 23, seed: 55);
        var expected = new ObsLutInverter(oldApplier).InvertThenApply(newApplier, source);

        // Act
        var inverter = new ObsLutInverter(oldApplier);
        var banded = new RawImage(source.Width, source.Height);
        for (int row = 0; row < source.Height; row += 5)
        {
            inverter.InvertThenApplyRows(newApplier, source, banded, row, Math.Min(5, source.Height - row));
        }

        // Assert
        Assert.That(banded.Pixels, Is.EqualTo(expected.Pixels));
    }

    [Test]
    public void ClippedLut_InvertsToNearestAchievableWithoutDiverging()
    {
        // Arrange: a LUT that clips red above 200 is not injective there; the inverse cannot
        // recover clipped detail but must stay stable and exact in the unclipped range.
        var applier = new ObsLutApplier(ClampedRedLut(200));
        var inverter = new ObsLutInverter(applier);
        var identity = new ObsLutApplier(TestImages.LoadTemplate());
        var source = TestImages.Random(16, 16, seed: 44);

        // Act
        var roundTripped = inverter.InvertThenApply(identity, applier.Apply(source));

        // Assert: green and blue are untouched by this LUT; red must round-trip below the clip.
        using (Assert.EnterMultipleScope())
        {
            for (int i = 0; i < source.Pixels.Length; i += 3)
            {
                if (source.Pixels[i] < 190)
                {
                    int diff = Math.Abs(source.Pixels[i] - roundTripped.Pixels[i]);
                    Assert.That(diff, Is.LessThanOrEqualTo(2), $"Red at pixel {i / 3} (diff {diff})");
                }

                Assert.That(Math.Abs(source.Pixels[i + 1] - roundTripped.Pixels[i + 1]), Is.LessThanOrEqualTo(1), $"Green at pixel {i / 3}");
                Assert.That(Math.Abs(source.Pixels[i + 2] - roundTripped.Pixels[i + 2]), Is.LessThanOrEqualTo(1), $"Blue at pixel {i / 3}");
            }
        }
    }

    /// <summary>Bakes a per-channel scaling LUT from the identity template.</summary>
    private static RawImage ScaledLut(float r, float g, float b)
    {
        var lut = TestImages.LoadTemplate();
        byte[] pixels = lut.Pixels;
        for (int i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = (byte)MathF.Round(pixels[i] * r);
            pixels[i + 1] = (byte)MathF.Round(pixels[i + 1] * g);
            pixels[i + 2] = (byte)MathF.Round(pixels[i + 2] * b);
        }

        return lut;
    }

    /// <summary>Identity LUT with the red channel clamped to a maximum.</summary>
    private static RawImage ClampedRedLut(byte maxRed)
    {
        var lut = TestImages.LoadTemplate();
        byte[] pixels = lut.Pixels;
        for (int i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = Math.Min(pixels[i], maxRed);
        }

        return lut;
    }
}
