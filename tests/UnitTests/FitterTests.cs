using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Sampling;

namespace UnitTests;

public class FitterTests
{
    // Gentle capture-like ground truth: cross-channel mixing + offsets + per-channel gamma.
    private static Rgb GroundTruth(Rgb x)
    {
        float r = 0.85f * x.R + 0.08f * x.G + 0.03f * x.B + 0.02f;
        float g = 0.05f * x.R + 0.88f * x.G + 0.04f * x.B + 0.01f;
        float b = 0.03f * x.R + 0.06f * x.G + 0.86f * x.B + 0.03f;
        return new Rgb(MathF.Pow(r, 0.92f), MathF.Pow(g, 1.08f), MathF.Pow(b, 1.0f));
    }

    private static List<ColorCorrespondence> MakeSamples(int count, int seed, Func<Rgb, Rgb>? corrupt = null, double noise = 0)
    {
        var rng = new Random(seed);
        var samples = new List<ColorCorrespondence>(count);
        for (int i = 0; i < count; i++)
        {
            var observed = new Rgb(rng.NextSingle(), rng.NextSingle(), rng.NextSingle());
            var reference = GroundTruth(observed);
            if (noise > 0)
            {
                reference = new Rgb(
                    reference.R + (float)((rng.NextDouble() - 0.5) * 2 * noise),
                    reference.G + (float)((rng.NextDouble() - 0.5) * 2 * noise),
                    reference.B + (float)((rng.NextDouble() - 0.5) * 2 * noise)).Clamp01();
            }

            if (corrupt is not null)
                reference = corrupt(reference);
            samples.Add(new ColorCorrespondence(observed, reference, Weight: 1.0, ObservedVariance: 0.0));
        }

        return samples;
    }

    private static float MeanDeltaEAgainstTruth(IColorTransform transform, int seed)
    {
        var rng = new Random(seed);
        float total = 0;
        const int count = 500;
        for (int i = 0; i < count; i++)
        {
            var x = new Rgb(rng.NextSingle(), rng.NextSingle(), rng.NextSingle());
            total += Oklab.DeltaESrgb(transform.Apply(x).Clamp01(), GroundTruth(x));
        }

        return total / count;
    }

    [Test]
    public void Fit_RecoversCleanSyntheticTransform()
    {
        // Arrange
        var samples = MakeSamples(400, seed: 1);

        // Act
        var result = new AffineCurvesFitter().Fit(samples, FitOptions.Default, CancellationToken.None);

        // Assert
        float meanDeltaE = MeanDeltaEAgainstTruth(result.Transform, seed: 2);
        Assert.That(meanDeltaE, Is.LessThan(0.01f), "Mean dE too high for clean fit.");
    }

    [Test]
    public void Fit_RecoversNoisySyntheticTransform()
    {
        // Arrange
        var samples = MakeSamples(400, seed: 3, noise: 0.01);

        // Act
        var result = new AffineCurvesFitter().Fit(samples, FitOptions.Default, CancellationToken.None);

        // Assert
        float meanDeltaE = MeanDeltaEAgainstTruth(result.Transform, seed: 4);
        Assert.That(meanDeltaE, Is.LessThan(0.02f), "Mean dE too high for noisy fit.");
    }

    [Test]
    public void Fit_ResistsGrossOutliers()
    {
        // Arrange: replace 10% of references with random garbage.
        var rng = new Random(5);
        int index = 0;
        var samples = MakeSamples(400, seed: 6, corrupt: reference =>
        {
            bool outlier = index++ % 10 == 0;
            return outlier ? new Rgb(rng.NextSingle(), rng.NextSingle(), rng.NextSingle()) : reference;
        });

        // Act
        var result = new AffineCurvesFitter().Fit(samples, FitOptions.Default, CancellationToken.None);

        // Assert
        float meanDeltaE = MeanDeltaEAgainstTruth(result.Transform, seed: 7);
        Assert.That(meanDeltaE, Is.LessThan(0.02f), "Mean dE too high with outliers.");
        Assert.That(result.Diagnostics.InlierCount, Is.LessThan(result.Diagnostics.TotalCount),
            "Expected some samples to be down-weighted as outliers.");
    }

    [Test]
    public void Fit_DegenerateSamples_ProducesFiniteNearIdentityTransform()
    {
        // Arrange: all samples the same color - everything but that point is unconstrained; the
        // ridge and identity pulls must keep the transform finite and sane.
        var observed = new Rgb(0.5f, 0.5f, 0.5f);
        var reference = new Rgb(0.55f, 0.5f, 0.45f);
        var samples = Enumerable.Repeat(new ColorCorrespondence(observed, reference, 1.0, 0.0), 50).ToList();

        // Act
        var result = new AffineCurvesFitter().Fit(samples, FitOptions.Default, CancellationToken.None);

        // Assert
        foreach (float x in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            var output = result.Transform.Apply(new Rgb(x, x, x));
            Assert.That(float.IsFinite(output.R) && float.IsFinite(output.G) && float.IsFinite(output.B), Is.True);
            Assert.That(output.R, Is.InRange(-0.5f, 1.5f));
        }
    }

    [Test]
    public void Fit_RejectsTooFewSamples()
    {
        var samples = MakeSamples(4, seed: 8);
        Assert.Throws<ArgumentException>(() =>
            new AffineCurvesFitter().Fit(samples, FitOptions.Default, CancellationToken.None));
    }

    [Test]
    public void Fit_HonorsCancellation()
    {
        // Arrange
        var samples = MakeSamples(400, seed: 9);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert
        Assert.Throws<OperationCanceledException>(() =>
            new AffineCurvesFitter().Fit(samples, FitOptions.Default, cts.Token));
    }
}
