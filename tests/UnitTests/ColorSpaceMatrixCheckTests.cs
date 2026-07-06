using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.Pipeline;
using AutoLUT.Core.Sampling;

namespace UnitTests;

public class ColorSpaceMatrixCheckTests
{
    private static readonly SkiaImageCodec Codec = new();

    /// <summary>Mirrors the pipeline's identify -> correspondences -> fit path and returns the fitted matrix.</summary>
    private static IReadOnlyList<float> FitMatrix(List<Rgb> means)
    {
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);
        Assert.That(outcome.GlobalError, Is.Null, outcome.GlobalError);

        var correspondences = new List<ColorCorrespondence>();
        for (int i = 0; i < means.Count; i++)
        {
            if (outcome.Assignments[i] is { } target)
            {
                correspondences.Add(new ColorCorrespondence(means[i], target.ToRgb(), 1.0, 0.0));
            }
        }

        var fit = new AffineCurvesFitter().Fit(correspondences, new FitOptions { CurveSmoothness = 0.01 }, CancellationToken.None);
        return ((AffineCurvesTransform)fit.Transform).Matrix;
    }

    private static SolidCaptures.Degradation RandomDegradation(Random rng)
    {
        float Gain() => 0.8f + (float)rng.NextDouble() * 0.4f;   // 0.8 - 1.2
        float Gamma() => 0.85f + (float)rng.NextDouble() * 0.4f; // 0.85 - 1.25
        float bleed = (float)rng.NextDouble() * 0.03f;           // 0 - 0.03
        return new SolidCaptures.Degradation(Gain(), Gain(), Gain(), Gamma(), Gamma(), Gamma(), bleed);
    }

    [Test]
    public void Detect_Content601DecodedAs709_AdvisesRec601()
    {
        string? warning = ColorSpaceMatrixCheck.Detect(FitMatrix(SolidCaptures.Means(SolidCaptures.Mismatch601as709)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning, Does.Contain("'Rec. 601'"));
        }
    }

    [Test]
    public void Detect_Content709DecodedAs601_AdvisesRec709()
    {
        string? warning = ColorSpaceMatrixCheck.Detect(FitMatrix(SolidCaptures.Means(SolidCaptures.Mismatch709as601)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning, Does.Contain("'Rec. 709'"));
        }
    }

    [Test]
    public void Detect_CleanAndStandardDegradations_NoWarning()
    {
        // Gain/gamma/bleed within the validated bounds must never masquerade as a matrix mismatch.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ColorSpaceMatrixCheck.Detect(FitMatrix(SolidCaptures.Means(c => c))), Is.Null);
            Assert.That(ColorSpaceMatrixCheck.Detect(FitMatrix(SolidCaptures.DegradedMeans(SolidCaptures.Degradation.Moderate))), Is.Null);
            Assert.That(ColorSpaceMatrixCheck.Detect(FitMatrix(SolidCaptures.DegradedMeans(SolidCaptures.Degradation.WorstDark))), Is.Null);
            Assert.That(ColorSpaceMatrixCheck.Detect(FitMatrix(SolidCaptures.DegradedMeans(SolidCaptures.Degradation.WorstBright))), Is.Null);
        }
    }

    [Test]
    public void Detect_RandomCleanDegradations_NeverFalsePositive()
    {
        var rng = new Random(12345);
        using (Assert.EnterMultipleScope())
        {
            for (int t = 0; t < 200; t++)
            {
                var deg = RandomDegradation(rng);
                var means = SolidCaptures.DegradedMeans(deg, seedBase: t * 40);
                Assert.That(ColorSpaceMatrixCheck.Detect(FitMatrix(means)), Is.Null,
                    $"False positive at draw {t}: {deg}");
            }
        }
    }

    [Test]
    public void Detect_RandomMismatchPlusDegradation_AlwaysDetectsCorrectDirection()
    {
        var rng = new Random(999);
        using (Assert.EnterMultipleScope())
        {
            for (int t = 0; t < 100; t++)
            {
                var deg = RandomDegradation(rng);
                bool content601 = rng.Next(2) == 0; // 601-encoded content decoded as 709
                Func<Rgb, Rgb> chain = content601
                    ? c => SolidCaptures.Mismatch601as709(deg.Apply(c))
                    : c => SolidCaptures.Mismatch709as601(deg.Apply(c));

                var means = SolidCaptures.Means(chain, seedBase: 5000 + t * 40);
                string? warning = ColorSpaceMatrixCheck.Detect(FitMatrix(means));

                Assert.That(warning, Is.Not.Null, $"Missed mismatch at draw {t}: content601={content601}, {deg}");
                Assert.That(warning, Does.Contain(content601 ? "'Rec. 601'" : "'Rec. 709'"),
                    $"Wrong direction at draw {t}: content601={content601}");
            }
        }
    }

    [Test]
    public async Task Run_MismatchCaptures_WarnsAboutColorSpaceButStillCorrects()
    {
        // A 601/709 matrix mismatch is a cross-channel rotation the LUT can fix, but the user should
        // fix the source Color Space setting because saturated colors clip.
        var shots = CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png",
                Png(SolidCaptures.CaptureColor(SolidCaptures.Mismatch601as709(c.ToRgb()), 2, 3000 + i))))
            .ToList();

        var pipeline = new CalibrationPipeline(Codec, new AffineCurvesFitter(), new TransformLutGenerator(),
            new ObsLutWriter(), TestImages.LoadTemplate());
        var result = await pipeline.RunAsync(shots, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.ColorSpaceWarning, Is.Not.Null);
            Assert.That(result.ColorSpaceWarning, Does.Contain("'Rec. 601'"));
        }
    }

    private static byte[] Png(RawImage image)
    {
        using var stream = new MemoryStream();
        Codec.EncodePng(image, stream);
        return stream.ToArray();
    }
}
