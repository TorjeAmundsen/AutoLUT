using AutoLUT.Core.Alignment;
using AutoLUT.Core.ReferenceData;
using AutoLUT.Core.Validation;

namespace UnitTests;

public class StructuralMatcherTests
{
    [Test]
    public void SameScene_HeavilyColorShifted_ScoresHigh()
    {
        // Arrange
        var reference = new ReferenceImage("r", "R", SyntheticScenes.Scene(320, 240, seed: 21), []);
        var user = SyntheticScenes.BoxBlur(SyntheticScenes.ColorShift(reference.Image));

        // Act
        double score = StructuralMatcher.Score(user, reference, new AlignmentResult(0, 0, 1, false));

        // Assert
        Assert.That(score, Is.GreaterThan(StructuralMatcher.DefaultThreshold));
    }

    [Test]
    public void DifferentScene_ScoresLow()
    {
        // Arrange
        var reference = new ReferenceImage("r", "R", SyntheticScenes.Scene(320, 240, seed: 22), []);
        var user = SyntheticScenes.Scene(320, 240, seed: 99);

        // Act
        double score = StructuralMatcher.Score(user, reference, new AlignmentResult(0, 0, 0.2, false));

        // Assert
        Assert.That(score, Is.LessThan(StructuralMatcher.DefaultThreshold));
    }

    [Test]
    public void ShiftedSameScene_UsesAlignmentOffset()
    {
        // Arrange
        var reference = new ReferenceImage("r", "R", SyntheticScenes.Scene(320, 240, seed: 23), []);
        var user = SyntheticScenes.Translate(reference.Image, 8, -6);

        // Act
        double score = StructuralMatcher.Score(user, reference, new AlignmentResult(8, -6, 1, false));

        // Assert
        Assert.That(score, Is.GreaterThan(StructuralMatcher.DefaultThreshold));
    }
}
