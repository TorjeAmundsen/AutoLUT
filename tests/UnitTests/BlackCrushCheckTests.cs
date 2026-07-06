using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;

namespace UnitTests;

public class BlackCrushCheckTests
{
    private static Func<byte, Rgb> Grays(Func<Rgb, Rgb> chain) =>
        level => chain(new Rgb(level / 255f, level / 255f, level / 255f));

    [Test]
    public void Detect_CrushedDarks_ReturnsTrue()
    {
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.CrushDarks)), Is.True);
    }

    [Test]
    public void Detect_CleanCapture_ReturnsFalse()
    {
        Assert.That(BlackCrushCheck.Detect(Grays(c => c)), Is.False);
    }

    [Test]
    public void Detect_WashedOut_ReturnsFalse()
    {
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Washout)), Is.False);
    }

    [Test]
    public void Detect_GainGammaDegradations_NoFalsePositive()
    {
        // Gamma > 1 also extrapolates the upper ramp to a negative black, but its convex toe puts
        // gray32 well above the trend line - it must not masquerade as clipping.
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Degradation.Moderate.Apply)), Is.False);
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Degradation.WorstDark.Apply)), Is.False);
        Assert.That(BlackCrushCheck.Detect(Grays(SolidCaptures.Degradation.WorstBright.Apply)), Is.False);
    }
}
