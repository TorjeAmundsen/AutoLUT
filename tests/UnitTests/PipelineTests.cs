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
                row[x * 3] = (byte)(255 * x / gradient.Width);
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
