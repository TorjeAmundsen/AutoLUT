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

    /// <summary>Label for the details button; mentions warnings when there are any.</summary>
    public string DetailsButtonText => HasWarnings ? "Show details/warnings" : "Show details";

    /// <summary>Hover tooltip for the details button: all warnings, blank line between them.</summary>
    public string WarningsTooltip => string.Join("\n\n", Warnings);

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

    /// <summary>Vectorscope plot edge length in pixels; the XAML canvas size must match.</summary>
    public const double ScopePlotSize = 220;

    // Oklab a/b extent of the plot. The sRGB gamut reaches about |a| 0.28, |b| 0.31 (blue),
    // so 0.35 keeps every palette color inside the canvas with a small margin.
    private const float ScopeAbMax = 0.35f;

    private const double ScopeDotRadius = 3;

    /// <summary>
    /// Shift lines, one segment per capture from observed to corrected chroma. Parsed lazily:
    /// Geometry.Parse requires the render platform, which does not exist until the app is up -
    /// eager parsing would make this VM unconstructible in headless contexts. Bindings evaluate
    /// well after platform init.
    /// </summary>
    public Geometry? ScopeShifts => _scopeShiftsData is null ? null : _scopeShifts ??= Geometry.Parse(_scopeShiftsData);

    private readonly string? _scopeShiftsData;
    private Geometry? _scopeShifts;

    /// <summary>Target dots on the vectorscope, in canvas coordinates (already offset by the dot radius).</summary>
    public IReadOnlyList<ScopeDot> ScopeDots { get; }

    /// <summary>One vectorscope target marker, colored as the target color.</summary>
    public sealed record ScopeDot(double X, double Y, IBrush Brush, string Tooltip);

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
        IList<Point> curvePointsR, IList<Point> curvePointsG, IList<Point> curvePointsB,
        IReadOnlyList<ScopeDot> scopeDots, string? scopeShiftsData)
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
        ScopeDots = scopeDots;
        _scopeShiftsData = scopeShiftsData;
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
        var (scopeDots, scopeShiftsData) = BuildScope(result);
        return new CalibrationDetailsViewModel(
            result.Error, warnings, fitSummary, result.Corrections, rows, inputs,
            neutralSwatches, chromaSwatches,
            BuildCurvePoints(result.Transform, rgb => rgb.R),
            BuildCurvePoints(result.Transform, rgb => rgb.G),
            BuildCurvePoints(result.Transform, rgb => rgb.B),
            scopeDots, scopeShiftsData);
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

    private static SwatchTile BuildTile(ScreenshotResult shot, Rgb corrected) =>
        new(
            shot.Name,
            ToBrush(shot.ObservedMean!.Value),
            ToBrush(corrected),
            ToBrush(shot.Target!.ToRgb()),
            shot.IsOutlier ? Brushes.Orange : TileBorder,
            BuildTooltip(shot, corrected),
            shot.IsOutlier);

    private static string BuildTooltip(ScreenshotResult shot, Rgb corrected)
    {
        // Line order mirrors the tile's stripes: observed, corrected, target.
        var tooltip = new StringBuilder()
            .AppendLine(shot.Name)
            .AppendLine($"observed  {ToHex(shot.ObservedMean!.Value)}")
            .AppendLine($"corrected {ToHex(corrected)}")
            .AppendLine($"target    {shot.Target!.Hex}")
            .Append($"dE {(shot.DeltaE is { } deltaE ? deltaE.ToString("F4") : "-")}");
        if (shot.IsOutlier)
        {
            tooltip.AppendLine().Append("outlier (excluded from fit)");
        }

        return tooltip.ToString();
    }

    /// <summary>Projects a color's Oklab chroma (a right, b up, lightness dropped) onto the scope canvas.</summary>
    private static Point ScopeProject(Rgb srgb)
    {
        var lab = Oklab.FromSrgb(srgb);
        double half = ScopePlotSize / 2;
        return new Point(
            half + Math.Clamp(lab.A / ScopeAbMax, -1f, 1f) * half,
            half - Math.Clamp(lab.B / ScopeAbMax, -1f, 1f) * half);
    }

    private static (IReadOnlyList<ScopeDot> Dots, string? ShiftsData) BuildScope(CalibrationResult result)
    {
        if (result.Transform is not { } transform)
        {
            return ([], null);
        }

        var dots = new List<ScopeDot>();
        var path = new StringBuilder();
        foreach (var shot in result.Screenshots.Where(s => s is { Target: not null, ObservedMean: not null }))
        {
            var correctedRgb = transform.Apply(shot.ObservedMean!.Value).Clamp01();
            var observed = ScopeProject(shot.ObservedMean!.Value);
            var corrected = ScopeProject(correctedRgb);
            var target = ScopeProject(shot.Target!.ToRgb());
            // Invariant culture: a comma decimal separator would corrupt the path syntax.
            path.Append(FormattableString.Invariant(
                $"M {observed.X:F1},{observed.Y:F1} L {corrected.X:F1},{corrected.Y:F1} "));
            dots.Add(new ScopeDot(
                target.X - ScopeDotRadius, target.Y - ScopeDotRadius,
                ToBrush(shot.Target.ToRgb()), BuildTooltip(shot, correctedRgb)));
        }

        return dots.Count > 0 ? (dots, path.ToString()) : ([], null);
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
