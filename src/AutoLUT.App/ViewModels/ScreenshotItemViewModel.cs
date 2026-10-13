using AutoLUT.Core.Calibration;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoLUT.App.ViewModels;

public partial class ScreenshotItemViewModel : ObservableObject
{
    public string Name { get; }

    /// <summary>The PNG as loaded. Only this compressed form is kept per screenshot (a decoded
    /// 1440x1080 capture is ~4.7 MB, 39 of them ~180 MB); the preview decodes on demand.</summary>
    public byte[] Data { get; }

    /// <summary>Center-region measurement, taken once at load so calibration never decodes again.</summary>
    public SolidAnalysis Analysis { get; }

    /// <summary>Identified palette color, set after a calibration run.</summary>
    public PaletteColor? Target { get; private set; }

    [ObservableProperty]
    private string _statusPrefix = "Ready";

    /// <summary>Hex part of the status, rendered in a monospace font.</summary>
    [ObservableProperty]
    private string _statusHex = "";

    [ObservableProperty]
    private string _statusSuffix = "";

    [ObservableProperty]
    private IBrush _statusBrush = Brushes.Gray;

    [ObservableProperty]
    private IBrush? _swatchBrush;

    public ScreenshotItemViewModel(string name, byte[] data, SolidAnalysis analysis)
    {
        Name = name;
        Data = data;
        Analysis = analysis;
    }

    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#22CC22"));

    public void SetIdentified(PaletteColor target)
    {
        Target = target;
        StatusPrefix = "Identified ";
        StatusHex = target.Hex;
        StatusSuffix = "";
        StatusBrush = SuccessBrush;
        SwatchBrush = new SolidColorBrush(Color.FromRgb(target.R, target.G, target.B));
    }

    public void SetOutlier(PaletteColor target)
    {
        Target = target;
        StatusPrefix = "Identified ";
        StatusHex = target.Hex;
        StatusSuffix = " - excluded as outlier (inconsistent with the other captures; consider re-capturing)";
        StatusBrush = Brushes.Orange;
        SwatchBrush = new SolidColorBrush(Color.FromRgb(target.R, target.G, target.B));
    }

    /// <summary>Back to the freshly loaded state, for when a run's results no longer apply.</summary>
    public void ClearStatus()
    {
        Target = null;
        StatusPrefix = "Ready";
        StatusHex = "";
        StatusSuffix = "";
        StatusBrush = Brushes.Gray;
        SwatchBrush = null;
    }

    public void SetError(string message)
    {
        Target = null;
        StatusPrefix = message;
        StatusHex = "";
        StatusSuffix = "";
        StatusBrush = Brushes.Tomato;
        SwatchBrush = null;
    }
}
