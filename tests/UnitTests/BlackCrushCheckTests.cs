using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;

namespace UnitTests;

public class BlackCrushCheckTests
{
    private static Func<byte, Rgb> Grays(Func<Rgb, Rgb> chain) =>
        level => chain(new Rgb(level / 255f, level / 255f, level / 255f));

    [Test]
    public void Detect_CrushedDarks_ReportsDepthOfWorstChannel()
    {
        // R channel clips the deepest: 13 / 1.016 = ~12.8 levels.
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.CrushDarks)), Is.EqualTo(12.8f).Within(1.5f));
    }

    [Test]
    public void Detect_DeeplyCrushedDarks_ReportsDepthOfWorstChannel()
    {
        // G channel clips the deepest: 14.5 / 0.949 = ~15.3 levels.
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.CrushDarksDeep)), Is.EqualTo(15.3f).Within(1.5f));
    }

    [Test]
    public void Detect_CleanCapture_ReturnsNull()
    {
        Assert.That(BlackCrushCheck.Detect(Grays(c => c)), Is.Null);
    }

    [Test]
    public void Detect_WashedOut_ReturnsNull()
    {
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Washout)), Is.Null);
    }

    [Test]
    public void Detect_GainGammaDegradations_NoFalsePositive()
    {
        // Gamma > 1 also extrapolates the upper ramp to a negative black, but its convex toe puts
        // gray32 well above the trend line - it must not masquerade as clipping.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Degradation.Moderate.Apply)), Is.Null);
            Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Degradation.WorstDark.Apply)), Is.Null);
            Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Degradation.WorstBright.Apply)), Is.Null);
        }
    }
}
