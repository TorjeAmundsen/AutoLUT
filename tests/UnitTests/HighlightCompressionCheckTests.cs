using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;

namespace UnitTests;

public class HighlightCompressionCheckTests
{
    private static Func<byte, Rgb> Grays(Func<Rgb, Rgb> chain) =>
        level => chain(new Rgb(level / 255f, level / 255f, level / 255f));

    [Test]
    public void Detect_CompressedHighlights_ReportsShortfallOfWorstChannel()
    {
        // The knee squeezes 224->255 into ~9 codes; white lands ~18/255 below the ramp line.
        Assert.That(HighlightCompressionCheck.Detect(Grays(SolidCaptures.CompressHighlights)), Is.EqualTo(18f).Within(3f));
    }

    [Test]
    public void Detect_CleanCapture_ReturnsNull()
    {
        Assert.That(HighlightCompressionCheck.Detect(Grays(c => c)), Is.Null);
    }

    [Test]
    public void Detect_WashedOut_ReturnsNull()
    {
        // Washout is linear end to end - white sits exactly on the ramp's extrapolation.
        Assert.That(HighlightCompressionCheck.Detect(Grays(SolidCaptures.Washout)), Is.Null);
    }

    [Test]
    public void Detect_Crunched_Fires()
    {
        // Full-range-as-limited hard-clips the top of the range - a genuine highlight loss this
        // check is expected to catch; the pipeline lets the range warning own the messaging.
        Assert.That(HighlightCompressionCheck.Detect(Grays(SolidCaptures.Crunch)), Is.Not.Null);
    }

    [Test]
    public void Detect_CrushedDarks_ReturnsNull()
    {
        Assert.That(HighlightCompressionCheck.Detect(Grays(SolidCaptures.CrushDarks)), Is.Null);
    }

    [Test]
    public void Detect_GainGammaDegradations_NoFalsePositive()
    {
        // Gamma < 1 also undershoots the extrapolated ramp at white, but its concave shoulder
        // puts gray224 well off the trend line - it must not masquerade as a knee.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HighlightCompressionCheck.Detect(Grays(SolidCaptures.Degradation.Moderate.Apply)), Is.Null);
            Assert.That(HighlightCompressionCheck.Detect(Grays(SolidCaptures.Degradation.WorstDark.Apply)), Is.Null);
            Assert.That(HighlightCompressionCheck.Detect(Grays(SolidCaptures.Degradation.WorstBright.Apply)), Is.Null);
        }
    }
}
