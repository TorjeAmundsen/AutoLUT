using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.Pipeline;

namespace UnitTests;

public class PipelineTests
{
    private static readonly SkiaImageCodec Codec = new();

    private static CalibrationPipeline MakePipeline() =>
        new(Codec, new AffineCurvesFitter(), new TransformLutGenerator(), new ObsLutWriter(), TestImages.LoadTemplate());

    private static byte[] Png(RawImage image)
    {
        using var stream = new MemoryStream();
        Codec.EncodePng(image, stream);
        return stream.ToArray();
    }

    /// <summary>One capture per palette color, degraded, in palette order.</summary>
    private static List<ScreenshotInput> PaletteShots(SolidCaptures.Degradation degradation, int seedBase = 0) =>
        CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png", Png(SolidCaptures.Capture(c, degradation, 2, seedBase + i))))
            .ToList();

    [Test]
    public async Task EndToEnd_CorrectsDegradedCaptures()
    {
        // Arrange
        var degradation = SolidCaptures.Degradation.Moderate;
        var pipeline = MakePipeline();

        // Act
        var result = await pipeline.RunAsync(PaletteShots(degradation), null, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Screenshots.All(s => s.IsValid), Is.True);
        Assert.That(result.CrushWarning, Is.Null,
            "Gamma degradation must not trigger the crushed-shadows warning.");

        // Applying the LUT to each degraded capture must bring its color back to the commanded value.
        var applier = new ObsLutApplier(result.LutImage!);
        float total = 0;
        foreach (var color in CalibrationPalette.Colors)
        {
            var capture = SolidCaptures.Capture(color, degradation, 2, 1000 + color.R + color.G * 3 + color.B * 7);
            var corrected = SolidColorAnalyzer.Analyze(applier.Apply(capture));
            total += Oklab.DeltaESrgb(corrected.Mean, color.ToRgb());
        }

        float meanDeltaE = total / CalibrationPalette.Colors.Count;
        Assert.That(meanDeltaE, Is.LessThan(0.015f), $"Mean dE {meanDeltaE} too high after correction.");
    }

    [Test]
    public async Task EndToEnd_WorstDarkCorner_StillCorrects()
    {
        // Arrange
        var degradation = SolidCaptures.Degradation.WorstDark;
        var pipeline = MakePipeline();

        // Act
        var result = await pipeline.RunAsync(PaletteShots(degradation, seedBase: 50), null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True, result.Error);

        var applier = new ObsLutApplier(result.LutImage!);
        float total = 0;
        foreach (var color in CalibrationPalette.Colors)
        {
            var capture = SolidCaptures.Capture(color, degradation, 2, 2000 + color.R + color.G * 3 + color.B * 7);
            total += Oklab.DeltaESrgb(SolidColorAnalyzer.Analyze(applier.Apply(capture)).Mean, color.ToRgb());
        }

        float meanDeltaE = total / CalibrationPalette.Colors.Count;
        Assert.That(meanDeltaE, Is.LessThan(0.02f), $"Mean dE {meanDeltaE} too high after correction.");
    }

    [Test]
    public async Task Run_WashedOutCaptures_WarnsAboutColorRangeButStillCorrects()
    {
        // Arrange: limited-range content treated as full - an affine distortion the LUT can fix,
        // but the user should fix the range setting at the source.
        var shots = CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png",
                Png(SolidCaptures.CaptureColor(SolidCaptures.Washout(c.ToRgb()), 2, 800 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.ColorRangeWarning, Does.Contain("washed out"));
    }

    [Test]
    public async Task EndToEnd_CrushedDarks_MapsBlackToZeroAndKeepsDarkGraysInliers()
    {
        // Arrange: shadows clipped at the sensor (negative black offset, clamped at 0). The
        // crush path must anchor black so the LUT maps captured black to (0,0,0) instead of a
        // slight gray, and the darkest grays must not be rejected as outliers.
        var shots = CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png",
                Png(SolidCaptures.CaptureColor(SolidCaptures.CrushDarks(c.ToRgb()), 2, 1100 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.CrushWarning, Does.Contain("darkest"));
        foreach (var shot in result.Screenshots.Where(s => s.Target is { IsNeutral: true, R: 0 or 32 }))
        {
            Assert.That(shot.IsOutlier, Is.False, $"{shot.Name} must stay an inlier.");
        }

        var applier = new ObsLutApplier(result.LutImage!);
        var capture = SolidCaptures.CaptureColor(SolidCaptures.CrushDarks(new Rgb(0f, 0f, 0f)), 2, 1300);
        var corrected = SolidColorAnalyzer.Analyze(applier.Apply(capture)).Mean;
        Assert.That(corrected.R, Is.LessThan(3f / 255f), "Corrected black R still lifted.");
        Assert.That(corrected.G, Is.LessThan(3f / 255f), "Corrected black G still lifted.");
        Assert.That(corrected.B, Is.LessThan(3f / 255f), "Corrected black B still lifted.");
    }

    [Test]
    public async Task EndToEnd_DeeplyCrushedDarks_RecoversGray32AndKeepsItInlier()
    {
        // Arrange: deeper crush leaves gray32 barely above the clamp (~16/255 observed). The
        // gray32 anchor must keep it alive through the robust loop so the toe recovers it to ~32
        // instead of rejecting it and correcting it to ~15.
        var shots = CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png",
                Png(SolidCaptures.CaptureColor(SolidCaptures.CrushDarksDeep(c.ToRgb()), 2, 1400 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True, result.Error);
        foreach (var shot in result.Screenshots.Where(s => s.Target is { IsNeutral: true, R: 0 or 32 }))
        {
            Assert.That(shot.IsOutlier, Is.False, $"{shot.Name} must stay an inlier.");
        }

        var applier = new ObsLutApplier(result.LutImage!);
        var black = SolidCaptures.CaptureColor(SolidCaptures.CrushDarksDeep(new Rgb(0f, 0f, 0f)), 2, 1500);
        var correctedBlack = SolidColorAnalyzer.Analyze(applier.Apply(black)).Mean;
        Assert.That(correctedBlack.R, Is.LessThan(3f / 255f), "Corrected black R still lifted.");
        Assert.That(correctedBlack.G, Is.LessThan(3f / 255f), "Corrected black G still lifted.");
        Assert.That(correctedBlack.B, Is.LessThan(3f / 255f), "Corrected black B still lifted.");

        var gray32 = SolidCaptures.CaptureColor(SolidCaptures.CrushDarksDeep(new Rgb(32f / 255f, 32f / 255f, 32f / 255f)), 2, 1600);
        var correctedGray32 = SolidColorAnalyzer.Analyze(applier.Apply(gray32)).Mean;
        Assert.That(correctedGray32.R, Is.EqualTo(32f / 255f).Within(3f / 255f), "Corrected gray32 R off target.");
        Assert.That(correctedGray32.G, Is.EqualTo(32f / 255f).Within(3f / 255f), "Corrected gray32 G off target.");
        Assert.That(correctedGray32.B, Is.EqualTo(32f / 255f).Within(3f / 255f), "Corrected gray32 B off target.");
    }

    [Test]
    public async Task Run_FlagsInconsistentCaptureAsOutlier()
    {
        // Arrange: one capture is identifiable but its color disagrees with the transform that
        // explains every other capture (+25/255 red on the mid gray) - robust fitting must
        // exclude it and the result must say which one.
        var degradation = SolidCaptures.Degradation.Moderate;
        var shots = new List<ScreenshotInput>();
        int contaminatedIndex = -1;
        for (int i = 0; i < CalibrationPalette.Colors.Count; i++)
        {
            var color = CalibrationPalette.Colors[i];
            var degraded = degradation.Apply(color.ToRgb());
            if (color is { IsNeutral: true, R: 128 })
            {
                degraded = new Rgb(degraded.R + 25f / 255f, degraded.G, degraded.B);
                contaminatedIndex = i;
            }

            shots.Add(new ScreenshotInput($"{color.Hex}.png", Png(SolidCaptures.CaptureColor(degraded, 2, 900 + i))));
        }

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Screenshots[contaminatedIndex].IsValid, Is.True);
        Assert.That(result.Screenshots[contaminatedIndex].IsOutlier, Is.True);
        Assert.That(result.Screenshots.Count(s => s.IsOutlier), Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task Run_IsolatesNonSolidCapture()
    {
        // Arrange: one gradient capture among the valid ones.
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 100);
        var gradient = SolidCaptures.Solid(320, 240, 0, 0, 0);
        for (int y = 0; y < gradient.Height; y++)
        {
            var row = gradient.Row(y);
            for (int x = 0; x < gradient.Width; x++)
            {
                row[x * 3] = (byte)(255 * x / gradient.Width);
            }
        }

        shots.Add(new ScreenshotInput("gradient.png", Png(gradient)));

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Screenshots[^1].Error, Does.Contain("solid"));
    }

    [Test]
    public async Task Run_RejectsExactDuplicates()
    {
        // Arrange
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 200);
        shots.Add(shots[0] with { Name = "copy.png" });

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Screenshots[^1].Error, Does.Contain("duplicate"));
    }

    [Test]
    public async Task Run_FailsWithTooFewCaptures()
    {
        // Arrange
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 300).Take(10).ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Too few"));
    }

    [Test]
    public async Task Run_FailsWhenAGrayIsMissing()
    {
        // Arrange: all captures except one mid gray.
        var degradation = SolidCaptures.Degradation.Moderate;
        var shots = CalibrationPalette.Colors
            .Where(c => c is not { IsNeutral: true, R: 96 })
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png", Png(SolidCaptures.Capture(c, degradation, 2, 400 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("gray"));
    }

    [Test]
    public void Run_HonorsCancellation()
    {
        // Arrange
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 600);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert
        Assert.CatchAsync<OperationCanceledException>(() => MakePipeline().RunAsync(shots, null, cts.Token));
    }

    [Test]
    public async Task Run_ReportsProgressThroughAllStages()
    {
        // Arrange
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 700);
        var stages = new List<PipelineStage>();
        var progress = Substitute.For<IProgress<PipelineProgress>>();
        progress.When(p => p.Report(Arg.Any<PipelineProgress>()))
            .Do(call => stages.Add(call.Arg<PipelineProgress>().Stage));

        // Act
        var result = await MakePipeline().RunAsync(shots, progress, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(stages, Does.Contain(PipelineStage.Validating));
        Assert.That(stages, Does.Contain(PipelineStage.Identifying));
        Assert.That(stages, Does.Contain(PipelineStage.Fitting));
        Assert.That(stages, Does.Contain(PipelineStage.GeneratingLut));
        Assert.That(stages.Last(), Is.EqualTo(PipelineStage.Finished));
    }
}
