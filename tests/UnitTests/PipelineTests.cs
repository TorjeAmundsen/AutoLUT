using AutoLUT.Core.Alignment;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.Pipeline;
using AutoLUT.Core.ReferenceData;
using AutoLUT.Core.Sampling;

namespace UnitTests;

public class PipelineTests
{
    private static readonly SkiaImageCodec Codec = new();

    private static ReferenceSet MakeReferences(params int[] seeds) =>
        new(seeds.Select(seed =>
        {
            var image = SyntheticScenes.Scene(640, 480, seed, rectangles: 32);
            return new ReferenceImage($"ref{seed}", $"Scene {seed}", image, RegionDeriver.Derive(image));
        }).ToList());

    private static CalibrationPipeline MakePipeline(
        ReferenceSet references, PipelineOptions? options = null, IAligner? aligner = null) =>
        new(
            references,
            Codec,
            aligner ?? new TranslationSearchAligner(),
            new MeanRegionSampler(),
            new AffineCurvesFitter(),
            new TransformLutGenerator(),
            new ObsLutWriter(),
            TestImages.LoadTemplate(),
            options: options);

    private static byte[] Png(RawImage image)
    {
        using var stream = new MemoryStream();
        Codec.EncodePng(image, stream);
        return stream.ToArray();
    }

    private static ScreenshotInput Shot(string name, RawImage image) => new(name, Png(image));

    /// <summary>Analog-capture-like degradation: shift, color shift, blur, noise.</summary>
    private static RawImage Degrade(RawImage reference, int dx, int dy, int seed) =>
        SyntheticScenes.AddNoise(
            SyntheticScenes.BoxBlur(
                SyntheticScenes.ColorShift(
                    SyntheticScenes.Translate(reference, dx, dy))),
            amplitude: 2, seed: seed);

    private static float MeanRegionDeltaE(RawImage capture, ReferenceImage reference, int dx, int dy)
    {
        float total = 0;
        int count = 0;
        for (int i = 0; i < reference.Regions.Count; i++)
        {
            var region = reference.Regions[i];
            var (mean, _) = RegionStatistics.Compute(capture, region.X + dx, region.Y + dy, region.Width, region.Height);
            total += Oklab.DeltaESrgb(mean, reference.RegionMeans[i]);
            count++;
        }

        return total / count;
    }

    [Test]
    public async Task EndToEnd_CorrectsColorShiftedCaptures()
    {
        // Arrange
        var references = MakeReferences(101, 202);
        var pipeline = MakePipeline(references);
        var capture1 = Degrade(references.References[0].Image, 4, -3, seed: 1);
        var capture2 = Degrade(references.References[1].Image, -5, 2, seed: 2);

        // Act
        var result = await pipeline.RunAsync(
            [Shot("cap1.png", capture1), Shot("cap2.png", capture2)], null, CancellationToken.None);

        // Assert
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Screenshots.All(s => s.IsValid), Is.True);
        Assert.That(result.Screenshots[0].ReferenceId, Is.EqualTo("ref101"));
        Assert.That(result.Screenshots[1].ReferenceId, Is.EqualTo("ref202"));

        // The LUT must pull the degraded capture's region colors close to the reference.
        var applier = new ObsLutApplier(result.LutImage!);
        float before = MeanRegionDeltaE(capture1, references.References[0], 4, -3);
        float after = MeanRegionDeltaE(applier.Apply(capture1), references.References[0], 4, -3);
        Assert.That(before, Is.GreaterThan(0.03f), "Degradation should be significant before correction.");
        Assert.That(after, Is.LessThan(0.015f), $"Corrected error {after} too high (was {before}).");
    }

    [Test]
    public async Task Run_IsolatesPerScreenshotFailures()
    {
        // Arrange
        var references = MakeReferences(101, 202);
        var pipeline = MakePipeline(references);
        var shots = new[]
        {
            new ScreenshotInput("garbage.png", [1, 2, 3, 4]),
            Shot("wrong-scene.png", SyntheticScenes.Scene(640, 480, seed: 999, rectangles: 32)),
            Shot("good.png", Degrade(references.References[0].Image, 3, 1, seed: 3)),
        };

        // Act
        var result = await pipeline.RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Screenshots[0].Error, Does.Contain("PNG"));
        Assert.That(result.Screenshots[1].Error, Does.Contain("savestate"));
        Assert.That(result.Screenshots[2].IsValid, Is.True);
    }

    [Test]
    public async Task Run_RejectsExactDuplicates()
    {
        // Arrange
        var references = MakeReferences(101);
        var pipeline = MakePipeline(references);
        var shot = Shot("cap.png", Degrade(references.References[0].Image, 0, 0, seed: 4));

        // Act
        var result = await pipeline.RunAsync([shot, shot with { Name = "copy.png" }], null, CancellationToken.None);

        // Assert
        Assert.That(result.Screenshots[0].IsValid, Is.True);
        Assert.That(result.Screenshots[1].Error, Does.Contain("duplicate"));
    }

    [Test]
    public async Task Run_RejectsSecondCaptureOfSameSavestate()
    {
        // Arrange: two different captures (different noise) of the same reference scene.
        var references = MakeReferences(101);
        var pipeline = MakePipeline(references);
        var shots = new[]
        {
            Shot("first.png", Degrade(references.References[0].Image, 2, 2, seed: 5)),
            Shot("second.png", Degrade(references.References[0].Image, -2, 1, seed: 6)),
        };

        // Act
        var result = await pipeline.RunAsync(shots, null, CancellationToken.None);

        // Assert
        Assert.That(result.Screenshots[0].IsValid, Is.True);
        Assert.That(result.Screenshots[1].Error, Does.Contain("same savestate"));
    }

    [Test]
    public async Task Run_RejectsWrongDimensions()
    {
        // Arrange: same aspect ratio, half resolution - reads as a scaled capture.
        var references = MakeReferences(101);
        var pipeline = MakePipeline(references);

        // Act
        var result = await pipeline.RunAsync(
            [Shot("small.png", SyntheticScenes.Scene(320, 240, seed: 1))], null, CancellationToken.None);

        // Assert
        Assert.That(result.Screenshots[0].Error, Does.Contain("dimensions"));
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task Run_ReportsCropWhenAlignmentHitsSearchBoundary()
    {
        // Arrange: substitute aligner claims a perfect score at the search boundary.
        var references = MakeReferences(101);
        var aligner = Substitute.For<IAligner>();
        aligner.Align(Arg.Any<RawImage>(), Arg.Any<ReferenceImage>())
            .Returns(new AlignmentResult(0, 0, 0.95, AtSearchBoundary: true));
        var pipeline = MakePipeline(references, aligner: aligner);

        // Act
        var result = await pipeline.RunAsync(
            [Shot("cap.png", references.References[0].Image.Clone())], null, CancellationToken.None);

        // Assert
        Assert.That(result.Screenshots[0].Error, Does.Contain("cropped"));
    }

    [Test]
    public async Task Run_FailsWhenCorrespondencesInsufficient()
    {
        // Arrange
        var references = MakeReferences(101);
        var pipeline = MakePipeline(references, new PipelineOptions { MinimumCorrespondences = 100_000 });

        // Act
        var result = await pipeline.RunAsync(
            [Shot("cap.png", Degrade(references.References[0].Image, 0, 0, seed: 7))], null, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Insufficient"));
    }

    [Test]
    public void Run_HonorsCancellation()
    {
        // Arrange
        var references = MakeReferences(101);
        var pipeline = MakePipeline(references);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert
        Assert.CatchAsync<OperationCanceledException>(() => pipeline.RunAsync(
            [Shot("cap.png", references.References[0].Image.Clone())], null, cts.Token));
    }

    [Test]
    public async Task Run_ReportsProgressThroughAllStages()
    {
        // Arrange
        var references = MakeReferences(101);
        var pipeline = MakePipeline(references);
        var stages = new List<PipelineStage>();
        var progress = Substitute.For<IProgress<PipelineProgress>>();
        progress.When(p => p.Report(Arg.Any<PipelineProgress>()))
            .Do(call => stages.Add(call.Arg<PipelineProgress>().Stage));

        // Act
        var result = await pipeline.RunAsync(
            [Shot("cap.png", Degrade(references.References[0].Image, 1, 1, seed: 8))], progress, CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(stages, Does.Contain(PipelineStage.Validating));
        Assert.That(stages, Does.Contain(PipelineStage.Aligning));
        Assert.That(stages, Does.Contain(PipelineStage.Sampling));
        Assert.That(stages, Does.Contain(PipelineStage.Fitting));
        Assert.That(stages, Does.Contain(PipelineStage.GeneratingLut));
        Assert.That(stages.Last(), Is.EqualTo(PipelineStage.Finished));
    }
}
