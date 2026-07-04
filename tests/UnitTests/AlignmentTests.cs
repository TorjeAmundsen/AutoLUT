using AutoLUT.Core.Alignment;
using AutoLUT.Core.ReferenceData;

namespace UnitTests;

public class AlignmentTests
{
    private static ReferenceImage MakeReference(int seed) =>
        new("test", "Test", SyntheticScenes.Scene(320, 240, seed), []);

    [Test]
    public void RecoversKnownShifts_UnderBlurNoiseAndColorShift()
    {
        // Arrange
        var reference = MakeReference(seed: 11);
        var aligner = new TranslationSearchAligner();

        int[] offsets = [-10, -7, -3, 0, 3, 7, 10];
        foreach (int dy in offsets)
        foreach (int dx in offsets)
        {
            var user = SyntheticScenes.Translate(reference.Image, dx, dy);
            user = SyntheticScenes.ColorShift(user);
            user = SyntheticScenes.BoxBlur(user);
            user = SyntheticScenes.AddNoise(user, amplitude: 3, seed: dx * 100 + dy);

            // Act
            var result = aligner.Align(user, reference);

            // Assert
            Assert.That((result.Dx, result.Dy), Is.EqualTo((dx, dy)),
                $"Expected ({dx},{dy}), got ({result.Dx},{result.Dy}) score {result.Score:F3}");
        }
    }

    [Test]
    public void IdenticalImages_AlignAtZeroWithPerfectScore()
    {
        // Arrange
        var reference = MakeReference(seed: 12);

        // Act
        var result = new TranslationSearchAligner().Align(reference.Image.Clone(), reference);

        // Assert
        Assert.That(result.Dx, Is.Zero);
        Assert.That(result.Dy, Is.Zero);
        Assert.That(result.Score, Is.GreaterThan(0.999));
        Assert.That(result.AtSearchBoundary, Is.False);
    }

    [Test]
    public void MaximumShift_SetsBoundaryFlag()
    {
        // Arrange
        var reference = MakeReference(seed: 13);
        var user = SyntheticScenes.Translate(reference.Image, 10, 0);

        // Act
        var result = new TranslationSearchAligner().Align(user, reference);

        // Assert
        Assert.That(result.Dx, Is.EqualTo(10));
        Assert.That(result.AtSearchBoundary, Is.True);
    }

    [Test]
    public void MismatchedDimensions_Throw()
    {
        var reference = MakeReference(seed: 14);
        var user = SyntheticScenes.Scene(160, 120, seed: 14);
        Assert.Throws<ArgumentException>(() => new TranslationSearchAligner().Align(user, reference));
    }
}
