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
        ColorRangeCheck.Detect(chain(Black), chain(White), chain(Gray32), chain(Gray224),
            BlackCrushCheck.Detect(level => chain(new Rgb(level / 255f, level / 255f, level / 255f))) is not null);

    /// <summary>Full-range content expanded as limited after a small upstream gain error.</summary>
    private static Func<Rgb, Rgb> CrunchWithGain(float gain) => c =>
        SolidCaptures.Crunch(new Rgb(c.R * gain, c.G * gain, c.B * gain));

    [Test]
    public void Detect_LimitedAsFull_WarnsWashedOut()
    {
        string? warning = Detect(SolidCaptures.Washout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warning, Does.Contain("washed out"));
            Assert.That(warning, Does.Contain("'Partial'"));
        }
    }

    [Test]
    public void Detect_FullAsLimited_WarnsCrushed()
    {
        string? warning = Detect(SolidCaptures.Crunch);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warning, Does.Contain("crushed"));
            Assert.That(warning, Does.Contain("'Full'"));
        }
    }

    [TestCase(0.96f)]
    [TestCase(0.97f)]
    [TestCase(1.005f)]
    [TestCase(1.01f)]
    [TestCase(1.03f)]
    public void Detect_FullAsLimitedWithGainError_WarnsCrushed(float gain)
    {
        // Off unity gain the white-to-gray224 gap leaves its 12-20 window; the clipped-expansion
        // signature must still name the range setting instead of leaving only generic warnings.
        string? warning = Detect(CrunchWithGain(gain));

        Assert.That(warning, Does.Contain("'Full'"));
    }

    [Test]
    public void Detect_CrushingDevices_NoFalsePositive()
    {
        // Real crushing devices clip shadows at near-unity gain; even with white clipped by extra
        // gain, the ramp stays far shallower than the 255/219 limited-range expansion.
        static Rgb BrightCrush(Rgb c) => new(
            Math.Clamp(1.06f * c.R - 13f / 255f, 0f, 1f),
            Math.Clamp(1.06f * c.G - 13f / 255f, 0f, 1f),
            Math.Clamp(1.06f * c.B - 13f / 255f, 0f, 1f));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Detect(SolidCaptures.CrushDarks), Is.Null);
            Assert.That(Detect(SolidCaptures.CrushDarksDeep), Is.Null);
            Assert.That(Detect(BrightCrush), Is.Null);
            Assert.That(Detect(SolidCaptures.CompressHighlights), Is.Null);
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Detect(SolidCaptures.Degradation.Moderate.Apply), Is.Null);
            Assert.That(Detect(SolidCaptures.Degradation.WorstDark.Apply), Is.Null);
            Assert.That(Detect(SolidCaptures.Degradation.WorstBright.Apply), Is.Null);
        }
    }
}
