using AutoLUT.Core.Calibration;

namespace UnitTests;

public class SolidColorAnalyzerTests
{
    [Test]
    public void Analyze_NoisySolid_IsAccepted()
    {
        // Arrange: solid with realistic analog noise.
        var image = SolidCaptures.Capture(CalibrationPalette.Colors[12], SolidCaptures.Degradation.Moderate,
            noiseAmplitude: 4, seed: 1);

        // Act
        var analysis = SolidColorAnalyzer.Analyze(image);

        // Assert
        Assert.That(analysis.IsSolid, Is.True);
        Assert.That(analysis.MaxStdDev, Is.LessThan(SolidColorAnalyzer.StdDevThreshold));
    }

    [Test]
    public void Analyze_HudAtScreenEdges_DoesNotAffectResult()
    {
        // Arrange: gz fill with HUD-like overlays where the OoT HUD actually sits - hearts
        // top-left, buttons top-right, counters bottom-left, minimap bottom-right. All outside
        // the central 30% zone.
        var image = SolidCaptures.Solid(320, 240, 220, 6, 8);
        SolidCaptures.FillRect(image, 20, 15, 45, 15, 230, 60, 60);    // hearts
        SolidCaptures.FillRect(image, 220, 12, 80, 45, 40, 130, 50);   // buttons
        SolidCaptures.FillRect(image, 20, 200, 50, 25, 240, 240, 240); // rupee counter
        SolidCaptures.FillRect(image, 210, 165, 95, 65, 90, 90, 100);  // minimap
        SolidCaptures.FillRect(image, 270, 70, 40, 20, 250, 250, 250); // timer/counter right side

        // Act
        var analysis = SolidColorAnalyzer.Analyze(image);

        // Assert
        Assert.That(analysis.IsSolid, Is.True);
        Assert.That(analysis.Mean.R, Is.EqualTo(220f / 255f).Within(0.002f));
        Assert.That(analysis.Mean.G, Is.EqualTo(6f / 255f).Within(0.002f));
        Assert.That(analysis.Mean.B, Is.EqualTo(8f / 255f).Within(0.002f));
    }

    [Test]
    public void Analyze_ObstructionInCenter_IsRejected()
    {
        // Arrange: textbox-like overlay covering part of the central zone.
        var image = SolidCaptures.Solid(320, 240, 220, 6, 8);
        SolidCaptures.FillRect(image, 120, 100, 90, 50, 30, 30, 30);

        // Act
        var analysis = SolidColorAnalyzer.Analyze(image);

        // Assert
        Assert.That(analysis.IsSolid, Is.False);
    }

    [Test]
    public void Analyze_Gradient_IsRejected()
    {
        // Arrange: horizontal gradient across the whole frame.
        var image = SolidCaptures.Solid(320, 240, 0, 0, 0);
        for (int y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);
            for (int x = 0; x < image.Width; x++)
            {
                row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = (byte)(255 * x / image.Width);
            }
        }

        // Act
        var analysis = SolidColorAnalyzer.Analyze(image);

        // Assert
        Assert.That(analysis.IsSolid, Is.False);
    }

    [Test]
    public void Analyze_GameplayLikeFrame_IsRejected()
    {
        // Arrange: four large distinct color blocks meeting in the center.
        var image = SolidCaptures.Solid(320, 240, 40, 90, 30);
        SolidCaptures.FillRect(image, 0, 0, 160, 120, 120, 100, 80);
        SolidCaptures.FillRect(image, 160, 0, 160, 120, 70, 130, 200);
        SolidCaptures.FillRect(image, 0, 120, 160, 120, 200, 180, 90);

        // Act
        var analysis = SolidColorAnalyzer.Analyze(image);

        // Assert
        Assert.That(analysis.IsSolid, Is.False);
    }
}
