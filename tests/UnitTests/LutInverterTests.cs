using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;

namespace UnitTests;

public class LutInverterTests
{
    [Test]
    public void IdentityLut_InvertsToSamePixels()
    {
        // Arrange: the identity LUT maps every color to itself, so its inverse must too.
        var inverter = new ObsLutInverter(new ObsLutApplier(TestImages.LoadTemplate()));
        var source = TestImages.Random(32, 24, seed: 11);

        // Act
        var result = inverter.Invert(source);

        // Assert: the baked identity quantizes lattice values, allow 1 LSB like the applier test.
        using (Assert.EnterMultipleScope())
        {
            for (int i = 0; i < source.Pixels.Length; i++)
            {
                int diff = Math.Abs(source.Pixels[i] - result.Pixels[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(1),
                    $"Pixel byte {i}: {source.Pixels[i]} -> {result.Pixels[i]} (diff {diff})");
            }
        }
    }

    [Test]
    public void SmoothLut_ApplyThenInvert_RoundTripsWithinQuantization()
    {
        // Arrange: a channel-scaling LUT (red 60%, green 80%) is smooth and injective, so the
        // inverse should recover the original up to the quantization the forward pass discarded:
        // red loses ~40% of its levels, so up to 2 LSB there.
        var applier = new ObsLutApplier(ScaledLut(0.6f, 0.8f, 1f));
        var inverter = new ObsLutInverter(applier);
        var source = TestImages.Random(32, 24, seed: 22);

        // Act
        var roundTripped = inverter.Invert(applier.Apply(source));

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
        // Arrange: the feature's scenario. Reference images were captured with an old LUT active;
        // reversing the old LUT and applying the new one should match applying the new LUT to the
        // never-corrected original, within the error the old LUT's quantization introduced.
        var oldApplier = new ObsLutApplier(ScaledLut(0.6f, 1f, 0.85f));
        var newApplier = new ObsLutApplier(ScaledLut(1f, 0.75f, 0.9f));
        var inverter = new ObsLutInverter(oldApplier);
        var original = TestImages.Random(32, 24, seed: 33);
        var reference = oldApplier.Apply(original);

        // Act
        var migrated = newApplier.Apply(inverter.Invert(reference));
        var expected = newApplier.Apply(original);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            for (int i = 0; i < expected.Pixels.Length; i++)
            {
                int diff = Math.Abs(expected.Pixels[i] - migrated.Pixels[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(2),
                    $"Pixel byte {i}: expected {expected.Pixels[i]}, got {migrated.Pixels[i]} (diff {diff})");
            }
        }
    }

    [Test]
    public void MigrationPipeline_FloatComposition_MatchesApplyingNewLutToOriginal()
    {
        // Arrange: same scenario as above, but composed in float via InvertThenApply. The only
        // remaining losses are the old LUT's output quantization baked into the reference
        // (irrecoverable) and the final rounding, so this path must stay within 1 LSB where the
        // two-step path is allowed 2.
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
    public void ClippedLut_InvertsToNearestAchievableWithoutDiverging()
    {
        // Arrange: a LUT that clips red above 200 is not injective there; the inverse cannot
        // recover clipped detail but must stay stable and exact in the unclipped range.
        var applier = new ObsLutApplier(ClampedRedLut(200));
        var inverter = new ObsLutInverter(applier);
        var source = TestImages.Random(16, 16, seed: 44);

        // Act
        var roundTripped = inverter.Invert(applier.Apply(source));

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
