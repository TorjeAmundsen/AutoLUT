using AutoLUT.Core.Calibration;
using AutoLUT.Core.ColorScience;

namespace UnitTests;

public class ColorIdentifierTests
{
    private static void AssertAllAssigned(IdentificationOutcome outcome, IReadOnlyList<PaletteColor> expected)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Is.Null);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(outcome.Assignments[i], Is.EqualTo(expected[i]),
                    $"Shot {i} (commanded {expected[i].Hex}): got {outcome.Assignments[i]?.Hex ?? "null"} ({outcome.Errors[i]})");
            }
        }
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
        byte[] clippedGrays = [224, 255];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Is.Null);
            Assert.That(outcome.Warnings, Is.Not.Empty);
            for (int i = 0; i < CalibrationPalette.Colors.Count; i++)
            {
                var expected = CalibrationPalette.Colors[i];
                var actual = outcome.Assignments[i];
                Assert.That(actual, Is.Not.Null, $"Shot {i} ({expected.Hex}) unassigned: {outcome.Errors[i]}");
                if (expected.IsNeutral && clippedGrays.Contains(expected.R))
                {
                    Assert.That(clippedGrays, Does.Contain(actual!.R), $"Clipped gray {expected.Hex} got {actual.Hex}");
                }
                else
                {
                    Assert.That(actual, Is.EqualTo(expected), $"Shot {i} ({expected.Hex}) got {actual!.Hex}");
                }
            }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Is.Null);
            Assert.That(outcome.Assignments[^1], Is.Null);
            Assert.That(outcome.Errors[^1], Does.Contain("could not be matched"));
            for (int i = 0; i < CalibrationPalette.Colors.Count; i++)
            {
                Assert.That(outcome.Assignments[i], Is.EqualTo(CalibrationPalette.Colors[i]));
            }
        }
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
        int assignedCount = outcome.Assignments.Count(a => a == duplicated);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Is.Null);
            Assert.That(assignedCount, Is.EqualTo(1));
            Assert.That(outcome.Errors.Count(e => e is not null), Is.EqualTo(1));
        }
    }

    [Test]
    public void Identify_DuplicateGrayCapture_RejectsTheDuplicateWithoutCascading()
    {
        // Arrange: full palette plus a second capture of #606060. Rank assignment must not slot
        // the duplicate into #808080 and shift every brighter gray up one.
        var duplicated = CalibrationPalette.Neutrals.First(c => c.R == 96);
        var means = SolidCaptures.DegradedMeans(SolidCaptures.Degradation.Moderate);
        means.Add(SolidColorAnalyzer.Analyze(SolidCaptures.Capture(duplicated, SolidCaptures.Degradation.Moderate, 2, 777)).Mean);

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert: exactly one of the two contenders gets #606060, the other errors as duplicate,
        // and every other palette color keeps its own assignment.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Is.Null);
            Assert.That(outcome.Assignments.Count(a => a == duplicated), Is.EqualTo(1));
            Assert.That(outcome.Errors.Count(e => e is not null), Is.EqualTo(1));
            Assert.That(outcome.Errors.Single(e => e is not null), Does.Contain("duplicate"));
            for (int i = 0; i < CalibrationPalette.Colors.Count; i++)
            {
                if (CalibrationPalette.Colors[i] != duplicated)
                {
                    Assert.That(outcome.Assignments[i], Is.EqualTo(CalibrationPalette.Colors[i]),
                        $"Shot {i} ({CalibrationPalette.Colors[i].Hex}) got {outcome.Assignments[i]?.Hex ?? "null"}");
                }
            }
        }
    }

    [Test]
    public void Identify_DuplicateGrayAndMissingWhite_FailsNamingMissingGray()
    {
        // Arrange: the real field shape - #606060 captured twice, white never captured, so the
        // candidate count still looks like 9. Must fail naming the missing white, not cascade.
        var duplicated = CalibrationPalette.Neutrals.First(c => c.R == 96);
        var palette = CalibrationPalette.Colors.Where(c => c != CalibrationPalette.White).ToList();
        var degradation = SolidCaptures.Degradation.Moderate;
        var means = palette
            .Select((c, i) => SolidColorAnalyzer.Analyze(SolidCaptures.Capture(c, degradation, 2, 600 + i)).Mean)
            .ToList();
        means.Add(SolidColorAnalyzer.Analyze(SolidCaptures.Capture(duplicated, degradation, 2, 778)).Mean);

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Does.Contain("8 of the 9 gray"));
            Assert.That(outcome.GlobalError, Does.Contain("#ffffff"));
            Assert.That(outcome.GlobalError, Does.Contain("duplicate"));
        }
    }

    [Test]
    public void Identify_ExtraUnknownGrayCapture_FailsGlobally()
    {
        // Arrange: full palette plus a capture of a gray that is not in the palette - ten
        // distinct gray candidates cannot be ranked onto nine ramp entries.
        var means = SolidCaptures.DegradedMeans(SolidCaptures.Degradation.Moderate);
        means.Add(new Rgb(0.42f, 0.42f, 0.42f));

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        Assert.That(outcome.GlobalError, Does.Contain("gray-looking"));
    }

    [Test]
    public void Identify_MissingWhite_FailsNamingMissingGray()
    {
        // Arrange: full degraded palette minus the white capture.
        var palette = CalibrationPalette.Colors.Where(c => c != CalibrationPalette.White).ToList();
        var degradation = SolidCaptures.Degradation.Moderate;
        var means = palette
            .Select((c, i) => SolidColorAnalyzer.Analyze(SolidCaptures.Capture(c, degradation, 2, 300 + i)).Mean)
            .ToList();

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Does.Contain("8 of the 9 gray"));
            Assert.That(outcome.GlobalError, Does.Contain("#ffffff"));
        }
    }

    [Test]
    public void Identify_MissingMidGray_FailsNamingMissingGray()
    {
        // Arrange: full degraded palette minus the #808080 capture.
        var palette = CalibrationPalette.Colors.Where(c => c.Hex != "#808080").ToList();
        var degradation = SolidCaptures.Degradation.Moderate;
        var means = palette
            .Select((c, i) => SolidColorAnalyzer.Analyze(SolidCaptures.Capture(c, degradation, 2, 400 + i)).Mean)
            .ToList();

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Does.Contain("8 of the 9 gray"));
            Assert.That(outcome.GlobalError, Does.Contain("#808080"));
        }
    }

    [Test]
    public void Identify_LimitedRangeMissingWhite_Regression()
    {
        // Arrange: real capture means from a field debug zip (limited-range capture card, white
        // never captured). The old blind take-9-lowest-spreads conscripted the #008080 capture as
        // a gray and silently shifted #606060..#e0e0e0 one ramp step up.
        string[] observed =
        [
            "000000", "000071", "0100f0", "057001", "087173", "0a70f4", "0fef06", "11f073",
            "13eff4", "111315", "303533", "3136b1", "31b335", "31b3b2", "515355", "6e0000",
            "6f0072", "7200ef", "717404", "707573", "7072f1", "6ef206", "71f374", "71f3f2",
            "929594", "b03631", "af35b2", "afb535", "b1b3b3", "d2d3d6", "e80002", "ea0070",
            "ec00f3", "ef7405", "f07671", "f171f3", "eef209", "eff475",
        ];
        var means = observed
            .Select(h => new Rgb(
                Convert.ToInt32(h[..2], 16) / 255f,
                Convert.ToInt32(h[2..4], 16) / 255f,
                Convert.ToInt32(h[4..], 16) / 255f))
            .ToList();

        // Act
        var outcome = ColorIdentifier.Identify(means, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GlobalError, Does.Contain("8 of the 9 gray"));
            Assert.That(outcome.GlobalError, Does.Contain("#ffffff"));
        }
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
