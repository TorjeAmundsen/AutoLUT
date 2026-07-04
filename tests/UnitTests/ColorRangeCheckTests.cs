using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;

namespace UnitTests;

public class ColorRangeCheckTests
{
    private static readonly Rgb Black = new(0f, 0f, 0f);
    private static readonly Rgb White = new(1f, 1f, 1f);
    private static readonly Rgb Gray32 = new(32f / 255f, 32f / 255f, 32f / 255f);
    private static readonly Rgb Gray224 = new(224f / 255f, 224f / 255f, 224f / 255f);

    private static string? Detect(Func<Rgb, Rgb> chain) =>
        ColorRangeCheck.Detect(chain(Black), chain(White), chain(Gray32), chain(Gray224));

    [Test]
    public void Detect_LimitedAsFull_WarnsWashedOut()
    {
        string? warning = Detect(SolidCaptures.Washout);

        Assert.That(warning, Does.Contain("washed out"));
        Assert.That(warning, Does.Contain("'Partial'"));
    }

    [Test]
    public void Detect_FullAsLimited_WarnsCrushed()
    {
        string? warning = Detect(SolidCaptures.Crunch);

        Assert.That(warning, Does.Contain("crushed"));
        Assert.That(warning, Does.Contain("'Full'"));
    }

    [Test]
    public void Detect_CleanCapture_NoWarning()
    {
        Assert.That(Detect(c => c), Is.Null);
    }

    [Test]
    public void Detect_GainGammaDegradations_NoFalsePositive()
    {
        // Gamma moves both ends the same direction and gain never lifts black - neither may
        // masquerade as a range mismatch.
        Assert.That(Detect(SolidCaptures.Degradation.Moderate.Apply), Is.Null);
        Assert.That(Detect(SolidCaptures.Degradation.WorstDark.Apply), Is.Null);
        Assert.That(Detect(SolidCaptures.Degradation.WorstBright.Apply), Is.Null);
    }
}
