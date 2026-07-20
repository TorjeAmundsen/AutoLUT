using System.Text;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Pipeline;
using Avalonia;
using Avalonia.Media;

namespace AutoLUT.App.ViewModels;

/// <summary>
/// Immutable projection of a CalibrationResult for the "Show details" overlay - built once
/// per run, so plain properties instead of ObservableObject.
/// </summary>
public sealed class CalibrationDetailsViewModel
{
    public string? Error { get; }

    public bool HasError => Error is not null;

    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;

    public string FitSummary { get; }

    public IReadOnlyList<string> AppliedCorrections { get; }

    public bool HasCorrections => AppliedCorrections.Count > 0;

    public IReadOnlyList<DetailRow> Rows { get; }

    /// <summary>The exact captures the run was generated from, for the debug zip.</summary>
    public IReadOnlyList<ScreenshotInput> Inputs { get; }

    /// <summary>True when a fitted transform exists (success only) - gates the visualization sections.</summary>
    public bool HasVisualizations { get; }

    /// <summary>Neutral-target tiles, dark to light.</summary>
    public IReadOnlyList<SwatchTile> NeutralSwatches { get; }

    /// <summary>Chromatic-target tiles, worst dE first.</summary>
    public IReadOnlyList<SwatchTile> ChromaSwatches { get; }

    /// <summary>Curve plot edge length in pixels; the XAML canvas size must match.</summary>
    public const double CurvePlotSize = 220;

    /// <summary>
    /// Binding fallback for the curve Polylines: when LastDetails is null (e.g. after Reset)
    /// the broken binding path would otherwise push null into Polyline.Points, which crashes
    /// Avalonia's renderer.
    /// </summary>
    public static IList<Point> EmptyPoints { get; } = [];

    public IList<Point> CurvePointsR { get; }

    public IList<Point> CurvePointsG { get; }

    public IList<Point> CurvePointsB { get; }

    public sealed record DetailRow(string Name, string Target, string Observed, string DeltaE, string Status);

    /// <summary>One palette tile: captured mean, transform-corrected color, and ground-truth target.</summary>
    public sealed record SwatchTile(
        string Name,
        IBrush ObservedBrush,
        IBrush CorrectedBrush,
        IBrush TargetBrush,
        IBrush TileBorderBrush,
        string Tooltip,
        bool IsOutlier);

    private CalibrationDetailsViewModel(
        string? error, IReadOnlyList<string> warnings, string fitSummary,
        IReadOnlyList<string> appliedCorrections, IReadOnlyList<DetailRow> rows,
        IReadOnlyList<ScreenshotInput> inputs,
        IReadOnlyList<SwatchTile> neutralSwatches, IReadOnlyList<SwatchTile> chromaSwatches,
        IList<Point> curvePointsR, IList<Point> curvePointsG, IList<Point> curvePointsB)
    {
        Error = error;
        Warnings = warnings;
        FitSummary = fitSummary;
        AppliedCorrections = appliedCorrections;
        Rows = rows;
        Inputs = inputs;
        HasVisualizations = curvePointsR.Count > 0;
        NeutralSwatches = neutralSwatches;
        ChromaSwatches = chromaSwatches;
        CurvePointsR = curvePointsR;
        CurvePointsG = curvePointsG;
        CurvePointsB = curvePointsB;
    }

    public static CalibrationDetailsViewModel From(CalibrationResult result, IReadOnlyList<ScreenshotInput> inputs)
    {
        var warnings = new List<string>();
        if (result.ColorRangeWarning is { } rangeWarning)
        {
            warnings.Add(rangeWarning);
        }

        if (result.ColorSpaceWarning is { } colorSpaceWarning)
        {
            warnings.Add(colorSpaceWarning);
        }

        if (result.CrushWarning is { } crushWarning)
        {
            warnings.Add(crushWarning);
        }

        warnings.AddRange(result.Warnings);

        string fitSummary = result.Diagnostics is { } d
            ? $"Mean dE {d.MeanDeltaE:F4}, median {d.MedianDeltaE:F4}, p95 {d.P95DeltaE:F4} - {d.InlierCount}/{d.TotalCount} inliers."
            : "No fit was produced.";

        var rows = result.Screenshots
            .Select(s => new
            {
                Shot = s,
                Row = new DetailRow(
                    s.Name,
                    s.Target?.Hex ?? "-",
                    s.ObservedMean is { } mean ? ToHex(mean) : "-",
                    s.DeltaE is { } deltaE ? deltaE.ToString("F4") : "-",
                    s.Error is { } error ? error : s.IsOutlier ? "outlier (excluded from fit)" : "ok"),
            })
            // Errors first, then outliers, then by target hex - stable order for comparing reports.
            .OrderByDescending(x => x.Shot.Error is not null)
            .ThenByDescending(x => x.Shot.IsOutlier)
            .ThenBy(x => x.Shot.Target?.Hex ?? "", StringComparer.Ordinal)
            .Select(x => x.Row)
            .ToList();

        var (neutralSwatches, chromaSwatches) = BuildSwatches(result);
        return new CalibrationDetailsViewModel(
            result.Error, warnings, fitSummary, result.Corrections, rows, inputs,
            neutralSwatches, chromaSwatches,
            BuildCurvePoints(result.Transform, rgb => rgb.R),
            BuildCurvePoints(result.Transform, rgb => rgb.G),
            BuildCurvePoints(result.Transform, rgb => rgb.B));
    }

    private static (IReadOnlyList<SwatchTile> Neutrals, IReadOnlyList<SwatchTile> Chroma) BuildSwatches(
        CalibrationResult result)
    {
        if (result.Transform is not { } transform)
        {
            return ([], []);
        }

        var identified = result.Screenshots
            .Where(s => s is { Target: not null, ObservedMean: not null })
            .Select(s => new
            {
                Shot = s,
                Tile = BuildTile(s, transform.Apply(s.ObservedMean!.Value).Clamp01()),
            })
            .ToList();

        return (
            identified.Where(x => x.Shot.Target!.IsNeutral)
                .OrderBy(x => x.Shot.Target!.R)
                .Select(x => x.Tile)
                .ToList(),
            identified.Where(x => !x.Shot.Target!.IsNeutral)
                .OrderByDescending(x => x.Shot.DeltaE ?? float.MinValue)
                .ThenBy(x => x.Shot.Target!.Hex, StringComparer.Ordinal)
                .Select(x => x.Tile)
                .ToList());
    }

    private static SwatchTile BuildTile(ScreenshotResult shot, Rgb corrected)
    {
        var tooltip = new StringBuilder()
            .AppendLine(shot.Name)
            .AppendLine($"target    {shot.Target!.Hex}")
            .AppendLine($"observed  {ToHex(shot.ObservedMean!.Value)}")
            .AppendLine($"corrected {ToHex(corrected)}")
            .Append($"dE {(shot.DeltaE is { } deltaE ? deltaE.ToString("F4") : "-")}");
        if (shot.IsOutlier)
        {
            tooltip.AppendLine().Append("outlier (excluded from fit)");
        }

        return new SwatchTile(
            shot.Name,
            ToBrush(shot.ObservedMean!.Value),
            ToBrush(corrected),
            ToBrush(shot.Target.ToRgb()),
            shot.IsOutlier ? Brushes.Orange : TileBorder,
            tooltip.ToString(),
            shot.IsOutlier);
    }

    /// <summary>
    /// Samples the fitted transform along the gray axis into plot coordinates (y down, output
    /// bottom-to-top). Values are clamped to [0,1] like the baked LUT clips them, so the plot
    /// shows the shipped behavior and flat edge segments honestly mark clipping.
    /// </summary>
    private static List<Point> BuildCurvePoints(IColorTransform? transform, Func<Rgb, float> channel)
    {
        if (transform is null)
        {
            return [];
        }

        const int samples = 64;
        var points = new List<Point>(samples + 1);
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float y = Math.Clamp(channel(transform.Apply(new Rgb(t, t, t))), 0f, 1f);
            points.Add(new Point(t * CurvePlotSize, (1 - y) * CurvePlotSize));
        }

        return points;
    }

    /// <summary>Plain-text rendering of everything the overlay shows, for clipboard and details.txt.</summary>
    public string BuildReportText()
    {
        var version = typeof(CalibrationDetailsViewModel).Assembly.GetName().Version;
        var report = new StringBuilder();
        report.AppendLine($"AutoLUT debug report (v{version?.ToString(3) ?? "unknown"})");
        report.AppendLine();

        if (Error is not null)
        {
            report.AppendLine($"Error: {Error}");
            report.AppendLine();
        }

        if (HasWarnings)
        {
            report.AppendLine("Warnings:");
            foreach (var warning in Warnings)
            {
                report.AppendLine($"- {warning}");
            }

            report.AppendLine();
        }

        report.AppendLine($"Fit: {FitSummary}");
        report.AppendLine();

        report.AppendLine("Additional corrections (beyond the LUT fit itself):");
        if (HasCorrections)
        {
            foreach (var correction in AppliedCorrections)
            {
                report.AppendLine($"- {correction}");
            }
        }
        else
        {
            report.AppendLine("- none; no capture problems needed special handling");
        }

        report.AppendLine();
        report.AppendLine("Captures (worst first):");
        int nameWidth = Math.Max("Capture".Length, Rows.Count > 0 ? Rows.Max(r => r.Name.Length) : 0);
        report.AppendLine($"{"Capture".PadRight(nameWidth)}  {"Target",-8}  {"Observed",-8}  {"dE",-7}  Status");
        foreach (var row in Rows)
        {
            report.AppendLine($"{row.Name.PadRight(nameWidth)}  {row.Target,-8}  {row.Observed,-8}  {row.DeltaE,-7}  {row.Status}");
        }

        return report.ToString();
    }

    private static readonly IBrush TileBorder = new SolidColorBrush(Color.Parse("#484848"));

    private static string ToHex(Rgb rgb)
    {
        var clamped = rgb.Clamp01();
        return $"#{ToByte(clamped.R):x2}{ToByte(clamped.G):x2}{ToByte(clamped.B):x2}";
    }

    private static byte ToByte(float channel) => (byte)Math.Round(channel * 255f);

    private static IBrush ToBrush(Rgb rgb)
    {
        var clamped = rgb.Clamp01();
        return new SolidColorBrush(Color.FromRgb(ToByte(clamped.R), ToByte(clamped.G), ToByte(clamped.B)));
    }
}
