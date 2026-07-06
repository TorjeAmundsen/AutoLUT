using AutoLUT.Core.ColorScience;

namespace UnitTests;

public class ColorScienceTests
{
    [Test]
    public void SrgbLinearRoundTrip()
    {
        using (Assert.EnterMultipleScope())
        {
            for (int v = 0; v <= 255; v++)
            {
                float srgb = v / 255f;
                float back = ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(srgb));
                Assert.That(back, Is.EqualTo(srgb).Within(1e-5f), $"Round trip failed for {v}");
            }
        }
    }

    [Test]
    public void Oklab_WhiteAndBlack()
    {
        // Act
        var white = Oklab.FromSrgb(new Rgb(1f, 1f, 1f));
        var black = Oklab.FromSrgb(new Rgb(0f, 0f, 0f));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(white.L, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(white.A, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(white.B, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(black.L, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(black.A, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(black.B, Is.EqualTo(0f).Within(1e-3f));
        }
    }

    [Test]
    public void Oklab_SrgbRed_MatchesPublishedValues()
    {
        // Reference values from Björn Ottosson's Oklab publication.
        var red = Oklab.FromSrgb(new Rgb(1f, 0f, 0f));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(red.L, Is.EqualTo(0.628f).Within(0.005f));
            Assert.That(red.A, Is.EqualTo(0.225f).Within(0.005f));
            Assert.That(red.B, Is.EqualTo(0.126f).Within(0.005f));
        }
    }

    [Test]
    public void DeltaE_ZeroForIdenticalColors()
    {
        var c = new Rgb(0.3f, 0.6f, 0.9f);
        Assert.That(Oklab.DeltaESrgb(c, c), Is.Zero);
    }
}
