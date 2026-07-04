using AutoLUT.Core.Calibration;
using AutoLUT.Core.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoLUT.App.ViewModels;

public partial class ScreenshotItemViewModel : ObservableObject
{
    public string Name { get; }
    public byte[] Data { get; }
    public RawImage Image { get; }

    /// <summary>Identified palette color, set after a calibration run.</summary>
    public PaletteColor? Target { get; private set; }

    /// <summary>LUT-corrected preview pixels, cached per LUT generation.</summary>
    public RawImage? Corrected { get; set; }

    public int CorrectedGeneration { get; set; } = -1;

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

    public ScreenshotItemViewModel(string name, byte[] data, RawImage image)
    {
        Name = name;
        Data = data;
        Image = image;
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
