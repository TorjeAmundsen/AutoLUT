using AutoLUT.Core.Fitting;

namespace UnitTests;

public class CurveTests
{
    [TestCase(0f)]
    [TestCase(0.1f)]
    [TestCase(0.5f)]
    [TestCase(0.73f)]
    [TestCase(1f)]
    public void Identity_EvaluatesToInput(float x)
    {
        var curve = MonotoneCurve.Identity(13);
        Assert.That(curve.Evaluate(x), Is.EqualTo(x).Within(1e-5f));
    }

    [Test]
    public void Evaluate_InterpolatesBetweenKnots()
    {
        var curve = new MonotoneCurve([0f, 0.5f, 1f]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(curve.Evaluate(0.25f), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(0.5f).Within(1e-5f));
        }
    }

    [Test]
    public void Evaluate_ExtrapolatesLinearlyOutsideRange()
    {
        // Slopes: first segment 0.4/0.5 = 0.8, last segment 0.6/0.5 = 1.2.
        var curve = new MonotoneCurve([0f, 0.4f, 1f]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(curve.Evaluate(-0.1f), Is.EqualTo(-0.08f).Within(1e-5f));
            Assert.That(curve.Evaluate(1.1f), Is.EqualTo(1.12f).Within(1e-5f));
        }
    }

    [Test]
    public void Constructor_RejectsDecreasingKnots()
    {
        Assert.Throws<ArgumentException>(() => new MonotoneCurve([0f, 0.5f, 0.4f]));
    }
}
