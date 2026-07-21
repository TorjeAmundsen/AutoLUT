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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.Transform, Is.Not.Null, "Success must expose the fitted transform.");
            Assert.That(result.Screenshots.All(s => s.IsValid), Is.True);
            Assert.That(result.CrushWarning, Is.Null,
                "Gamma degradation must not trigger the crushed-shadows warning.");
        }

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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.ColorRangeWarning, Does.Contain("washed out"));
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.CrushWarning, Does.Contain("darkest"));
            foreach (var shot in result.Screenshots.Where(s => s.Target is { IsNeutral: true, R: 0 or 32 }))
            {
                Assert.That(shot.IsOutlier, Is.False, $"{shot.Name} must stay an inlier.");
            }
        }

        var applier = new ObsLutApplier(result.LutImage!);
        var capture = SolidCaptures.CaptureColor(SolidCaptures.CrushDarks(new Rgb(0f, 0f, 0f)), 2, 1300);
        var corrected = SolidColorAnalyzer.Analyze(applier.Apply(capture)).Mean;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(corrected.R, Is.LessThan(3f / 255f), "Corrected black R still lifted.");
            Assert.That(corrected.G, Is.LessThan(3f / 255f), "Corrected black G still lifted.");
            Assert.That(corrected.B, Is.LessThan(3f / 255f), "Corrected black B still lifted.");
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            foreach (var shot in result.Screenshots.Where(s => s.Target is { IsNeutral: true, R: 0 or 32 }))
            {
                Assert.That(shot.IsOutlier, Is.False, $"{shot.Name} must stay an inlier.");
            }
        }

        var applier = new ObsLutApplier(result.LutImage!);
        var black = SolidCaptures.CaptureColor(SolidCaptures.CrushDarksDeep(new Rgb(0f, 0f, 0f)), 2, 1500);
        var correctedBlack = SolidColorAnalyzer.Analyze(applier.Apply(black)).Mean;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(correctedBlack.R, Is.LessThan(3f / 255f), "Corrected black R still lifted.");
            Assert.That(correctedBlack.G, Is.LessThan(3f / 255f), "Corrected black G still lifted.");
            Assert.That(correctedBlack.B, Is.LessThan(3f / 255f), "Corrected black B still lifted.");
        }

        var gray32 = SolidCaptures.CaptureColor(SolidCaptures.CrushDarksDeep(new Rgb(32f / 255f, 32f / 255f, 32f / 255f)), 2, 1600);
        var correctedGray32 = SolidColorAnalyzer.Analyze(applier.Apply(gray32)).Mean;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(correctedGray32.R, Is.EqualTo(32f / 255f).Within(3f / 255f), "Corrected gray32 R off target.");
            Assert.That(correctedGray32.G, Is.EqualTo(32f / 255f).Within(3f / 255f), "Corrected gray32 G off target.");
            Assert.That(correctedGray32.B, Is.EqualTo(32f / 255f).Within(3f / 255f), "Corrected gray32 B off target.");
        }
    }

    [Test]
    public async Task EndToEnd_CompressedHighlights_KeepsWhiteInlierAndRecoversIt()
    {
        // Arrange: a knee squeezes 224->255 into ~9 codes (real N64 composite chain). Without the
        // white anchor the robust loop rejects white as an outlier and the LUT extrapolates the
        // top of the range; with it the curve shoulder must learn the knee and correct white back
        // to ~255 while keeping both brightest neutrals inliers.
        var shots = CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png",
                Png(SolidCaptures.CaptureColor(SolidCaptures.CompressHighlights(c.ToRgb()), 2, 1800 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Warnings, Has.Some.Contains("compresses"));
            Assert.That(result.Corrections, Has.Some.Contains("Highlight compression"));
            foreach (var shot in result.Screenshots.Where(s => s.Target is { IsNeutral: true, R: 224 or 255 }))
            {
                Assert.That(shot.IsOutlier, Is.False, $"{shot.Name} must stay an inlier.");
            }
        }

        var applier = new ObsLutApplier(result.LutImage!);
        var white = SolidCaptures.CaptureColor(SolidCaptures.CompressHighlights(new Rgb(1f, 1f, 1f)), 2, 1900);
        var correctedWhite = SolidColorAnalyzer.Analyze(applier.Apply(white)).Mean;
        // ~249+ out of 255: uncorrected white sits ~55/255 low. The anchor weights are tuned on
        // the real knee feed (39/39 inliers there); the synthetic's steepest channel recovers to
        // ~250.8 rather than 252.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(correctedWhite.R, Is.GreaterThan(249f / 255f), "Corrected white R still dim.");
            Assert.That(correctedWhite.G, Is.GreaterThan(249f / 255f), "Corrected white G still dim.");
            Assert.That(correctedWhite.B, Is.GreaterThan(249f / 255f), "Corrected white B still dim.");
        }

        var gray224 = SolidCaptures.CaptureColor(SolidCaptures.CompressHighlights(new Rgb(224f / 255f, 224f / 255f, 224f / 255f)), 2, 2000);
        var correctedGray224 = SolidColorAnalyzer.Analyze(applier.Apply(gray224)).Mean;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(correctedGray224.R, Is.EqualTo(224f / 255f).Within(4f / 255f), "Corrected gray224 R off target.");
            Assert.That(correctedGray224.G, Is.EqualTo(224f / 255f).Within(4f / 255f), "Corrected gray224 G off target.");
            Assert.That(correctedGray224.B, Is.EqualTo(224f / 255f).Within(4f / 255f), "Corrected gray224 B off target.");
        }
    }

    [Test]
    public async Task Run_TintedGrayCapture_FailsInsteadOfShipping()
    {
        // Arrange: one gray capture is identifiable but tinted (+25/255 red on the mid gray).
        // The robust fit excludes it, but a LUT whose fit leaves a gray visibly non-neutral must
        // not ship - the run fails naming the capture so the user re-takes it.
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("tint"));
            Assert.That(result.Error, Does.Contain("#808080.png"));
            Assert.That(result.Screenshots[contaminatedIndex].IsOutlier, Is.True,
                "The failure report should still flag the contaminated capture as the fit outlier.");
        }
    }

    [Test]
    public async Task Run_ShiftedGrayCaptures_FailsWithMissingWhiteAndDuplicate()
    {
        // Arrange: real capture means from a field debug zip where the upper five grays each
        // showed the previous gray (palette/capture desync), #606060 was captured twice and white
        // never captured. Historically this shipped a LUT that visibly tinted grays (corrected
        // #202020 came out #0f181a, spread 11/255); identification must now catch it up front as
        // a duplicate gray plus a missing white.
        string[] observed =
        [
            "000000", "111315", "303533", "515355", "515355", "707573", "929594", "b1b3b3", "d2d3d6",
            "000071", "0100f0", "057001", "087173", "0a70f4", "0fef06", "11f073", "13eff4",
            "6e0000", "6f0072", "7200ef", "717404", "7072f1", "6ef206", "71f374", "71f3f2",
            "e80002", "ea0070", "ec00f3", "ef7405", "f07671", "f171f3", "eef209", "eff475",
            "b03631", "af35b2", "afb535", "3136b1", "31b335", "31b3b2",
        ];
        var shots = observed
            .Select((h, i) => new ScreenshotInput($"shot{i:d2}.png", Png(SolidCaptures.CaptureColor(
                new Rgb(
                    Convert.ToInt32(h[..2], 16) / 255f,
                    Convert.ToInt32(h[2..4], 16) / 255f,
                    Convert.ToInt32(h[4..], 16) / 255f),
                2, 40 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("#ffffff"));
            Assert.That(result.Error, Does.Contain("duplicate"));
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Screenshots[^1].Error, Does.Contain("solid"));
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Screenshots[^1].Error, Does.Contain("duplicate"));
        }
    }

    [Test]
    public async Task Run_FailsWithTooFewCaptures()
    {
        // Arrange
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 300).Take(10).ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("Too few"));
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("gray"));
        }
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
        var reports = new List<PipelineProgress>();
        var progress = Substitute.For<IProgress<PipelineProgress>>();
        progress.When(p => p.Report(Arg.Any<PipelineProgress>()))
            .Do(call => reports.Add(call.Arg<PipelineProgress>()));

        // Act
        var result = await MakePipeline().RunAsync(shots, progress, CancellationToken.None);

        // Assert
        var stages = reports.Select(r => r.Stage).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(stages, Does.Contain(PipelineStage.Validating));
            Assert.That(stages, Does.Contain(PipelineStage.Identifying));
            Assert.That(stages, Does.Contain(PipelineStage.Fitting));
            Assert.That(stages, Does.Contain(PipelineStage.GeneratingLut));
            Assert.That(stages.Last(), Is.EqualTo(PipelineStage.Finished));
        }
    }

    [Test]
    public async Task Run_RangeMismatch_SuppressesRedundantCrushWarning()
    {
        // Arrange: full-range content expanded as limited clips the darks, so BlackCrushCheck
        // fires too - but the range warning already explains the clipping and a second modal
        // would just restate it.
        var shots = CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png",
                Png(SolidCaptures.CaptureColor(SolidCaptures.Crunch(c.ToRgb()), 2, 1900 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.ColorRangeWarning, Does.Contain("crushed"));
            Assert.That(result.ColorRangeWarning, Does.Contain("detail is lost"));
            Assert.That(result.Corrections, Has.Some.Contains("Shadow crush"),
                "Crush detection must still fire so the fit compensates - only the warning is suppressed.");
            Assert.That(result.CrushWarning, Is.Null,
                "Range mismatch already explains the clipping; no second crush warning.");
        }
    }

    [Test]
    public async Task Run_CrushedDarks_ReportsShadowCrushCorrection()
    {
        // Arrange
        var shots = CalibrationPalette.Colors
            .Select((c, i) => new ScreenshotInput($"{c.Hex}.png",
                Png(SolidCaptures.CaptureColor(SolidCaptures.CrushDarks(c.ToRgb()), 2, 1700 + i))))
            .ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Corrections, Has.Some.Contains("Shadow crush"));
        }
    }

    [Test]
    public async Task Run_Success_ExposesFittedTransform()
    {
        // Arrange
        var pipeline = MakePipeline();

        // Act
        var result = await pipeline.RunAsync(PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 900), null, CancellationToken.None);

        // Assert: the exposed transform is the fitted one - recomputing each shot's residual
        // from it (clamped like the baked LUT, as the fitter does) must reproduce the reported
        // per-shot dE.
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Transform, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            foreach (var shot in result.Screenshots.Where(s => s.IsValid))
            {
                float deltaE = Oklab.DeltaESrgb(result.Transform!.Apply(shot.ObservedMean!.Value).Clamp01(), shot.Target!.ToRgb());
                Assert.That(deltaE, Is.EqualTo(shot.DeltaE!.Value).Within(1e-4f),
                    $"{shot.Name}: exposed transform does not reproduce the reported residual.");
            }
        }
    }

    [Test]
    public async Task Run_Failure_DoesNotExposeTransform()
    {
        // Arrange: too few captures - the run fails before fitting. The UI relies on
        // Success == (Transform is not null).
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 1000).Take(10).ToList();

        // Act
        var result = await MakePipeline().RunAsync(shots, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Transform, Is.Null);
        }
    }

    [Test]
    public async Task Run_PopulatesPerShotDeltaE()
    {
        // Arrange: valid captures plus one non-solid gradient that cannot enter the fit.
        var shots = PaletteShots(SolidCaptures.Degradation.Moderate, seedBase: 800);
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            foreach (var shot in result.Screenshots.Where(s => s.IsValid))
            {
                Assert.That(shot.DeltaE, Is.Not.Null, $"{shot.Name} entered the fit and must carry a dE.");
            }

            Assert.That(result.Screenshots[^1].DeltaE, Is.Null, "Invalid captures carry no dE.");
        }
    }
}
