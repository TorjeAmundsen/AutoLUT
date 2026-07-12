using System.Text;
using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Pipeline;

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

    public sealed record DetailRow(string Name, string Target, string Observed, string DeltaE, string Status);

    private CalibrationDetailsViewModel(
        string? error, IReadOnlyList<string> warnings, string fitSummary,
        IReadOnlyList<string> appliedCorrections, IReadOnlyList<DetailRow> rows,
        IReadOnlyList<ScreenshotInput> inputs)
    {
        Error = error;
        Warnings = warnings;
        FitSummary = fitSummary;
        AppliedCorrections = appliedCorrections;
        Rows = rows;
        Inputs = inputs;
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

        return new CalibrationDetailsViewModel(result.Error, warnings, fitSummary, result.Corrections, rows, inputs);
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

    private static string ToHex(Rgb rgb)
    {
        var clamped = rgb.Clamp01();
        return $"#{ToByte(clamped.R):x2}{ToByte(clamped.G):x2}{ToByte(clamped.B):x2}";
    }

    private static byte ToByte(float channel) => (byte)Math.Round(channel * 255f);
}
