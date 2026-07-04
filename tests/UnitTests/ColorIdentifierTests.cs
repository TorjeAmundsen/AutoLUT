using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;

namespace UnitTests;

public class ColorIdentifierTests
{
    private static void AssertAllAssigned(IdentificationOutcome outcome, IReadOnlyList<PaletteColor> expected)
    {
        Assert.That(outcome.GlobalError, Is.Null);
        for (int i = 0; i < expected.Count; i++)
            Assert.That(outcome.Assignments[i], Is.EqualTo(expected[i]),
                $"Shot {i} (commanded {expected[i].Hex}): got {outcome.Assignments[i]?.Hex ?? "null"} ({outcome.Errors[i]})");
    }

    [Test]
    public void Identify_ExactColors_AssignsAll39()
    {
        // Arrange
        var means = CalibrationPalette.Colors.Select(c => c.ToRgb()).ToList();

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        AssertAllAssigned(outcome, CalibrationPalette.Colors);
    }

    [Test]
    public void Identify_WorstDarkDegradation_AssignsAll39()
    {
        // Arrange: gain 0.8 + gamma 1.25 on all channels + bleed - the dark worst corner.
        var means = SolidCaptures.DegradedMeans(SolidCaptures.Degradation.WorstDark);

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        AssertAllAssigned(outcome, CalibrationPalette.Colors);
    }

    [Test]
    public void Identify_WorstBrightClipping_AssignsAllAndWarns()
    {
        // Arrange: gain 1.2 clips highlights - 224 and 255 grays become indistinguishable.
        var means = SolidCaptures.DegradedMeans(SolidCaptures.Degradation.WorstBright);

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert: everything assigned; the two clipped grays may swap ranks (their captures are identical).
        Assert.That(outcome.GlobalError, Is.Null);
        Assert.That(outcome.Warnings, Is.Not.Empty);
        byte[] clippedGrays = [224, 255];
        for (int i = 0; i < CalibrationPalette.Colors.Count; i++)
        {
            var expected = CalibrationPalette.Colors[i];
            var actual = outcome.Assignments[i];
            Assert.That(actual, Is.Not.Null, $"Shot {i} ({expected.Hex}) unassigned: {outcome.Errors[i]}");
            if (expected.IsNeutral && clippedGrays.Contains(expected.R))
                Assert.That(clippedGrays, Does.Contain(actual!.R), $"Clipped gray {expected.Hex} got {actual.Hex}");
            else
                Assert.That(actual, Is.EqualTo(expected), $"Shot {i} ({expected.Hex}) got {actual!.Hex}");
        }
    }

    [Test]
    public void Identify_ScrambledOrder_FollowsColorsNotOrder()
    {
        // Arrange
        var rng = new Random(42);
        var shuffled = CalibrationPalette.Colors.OrderBy(_ => rng.Next()).ToList();
        var degradation = SolidCaptures.Degradation.Moderate;
        var means = shuffled
            .Select((c, i) => SolidColorAnalyzer.Analyze(SolidCaptures.Capture(c, degradation, 2, 500 + i)).Mean)
            .ToList();

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        AssertAllAssigned(outcome, shuffled);
    }

    [Test]
    public void Identify_MissingChromatics_StillAssignsTheRest()
    {
        // Arrange: all 9 neutrals plus only half the chromatics.
        var subset = CalibrationPalette.Colors
            .Where((c, i) => c.IsNeutral || i % 2 == 0)
            .ToList();
        var degradation = SolidCaptures.Degradation.Moderate;
        var means = subset
            .Select((c, i) => SolidColorAnalyzer.Analyze(SolidCaptures.Capture(c, degradation, 2, 900 + i)).Mean)
            .ToList();

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        AssertAllAssigned(outcome, subset);
    }

    [Test]
    public void Identify_GarbageCapture_IsRejectedWithoutPoisoningOthers()
    {
        // Arrange: full palette plus one capture of a color that is not in the palette.
        var means = SolidCaptures.DegradedMeans(SolidCaptures.Degradation.Moderate);
        means.Add(new Rgb(0.36f, 0.15f, 0.55f));

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        Assert.That(outcome.GlobalError, Is.Null);
        Assert.That(outcome.Assignments[^1], Is.Null);
        Assert.That(outcome.Errors[^1], Does.Contain("could not be matched"));
        for (int i = 0; i < CalibrationPalette.Colors.Count; i++)
            Assert.That(outcome.Assignments[i], Is.EqualTo(CalibrationPalette.Colors[i]));
    }

    [Test]
    public void Identify_DuplicateColorCapture_RejectsTheWorseOne()
    {
        // Arrange: full palette plus a second capture of the same chromatic color.
        var duplicated = CalibrationPalette.Colors.First(c => c.Category == PaletteCategory.Grid);
        var means = SolidCaptures.DegradedMeans(SolidCaptures.Degradation.Moderate);
        means.Add(SolidColorAnalyzer.Analyze(SolidCaptures.Capture(duplicated, SolidCaptures.Degradation.Moderate, 2, 777)).Mean);

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert: exactly one of the two contenders gets the color, the other errors.
        Assert.That(outcome.GlobalError, Is.Null);
        int assignedCount = outcome.Assignments.Count(a => a == duplicated);
        Assert.That(assignedCount, Is.EqualTo(1));
        Assert.That(outcome.Errors.Count(e => e is not null), Is.EqualTo(1));
    }

    [Test]
    public void Identify_TooFewCaptures_FailsGlobally()
    {
        var means = CalibrationPalette.Colors.Take(5).Select(c => c.ToRgb()).ToList();
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);
        Assert.That(outcome.GlobalError, Does.Contain("Too few"));
    }

    [Test]
    public void Identify_NoBlackWhiteContrast_FailsGlobally()
    {
        // Arrange: a dozen near-identical mid-gray captures.
        var means = Enumerable.Range(0, 12).Select(i => new Rgb(0.5f + i * 0.001f, 0.5f, 0.5f)).ToList();

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        Assert.That(outcome.GlobalError, Does.Contain("darkest and brightest"));
    }
}
