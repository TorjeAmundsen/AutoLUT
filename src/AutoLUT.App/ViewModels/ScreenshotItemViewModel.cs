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
    private string _statusText = "Ready";

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

    public void SetIdentified(PaletteColor target)
    {
        Target = target;
        StatusText = $"Identified {target.Hex}";
        StatusBrush = Brushes.Green;
        SwatchBrush = new SolidColorBrush(Color.FromRgb(target.R, target.G, target.B));
    }

    public void SetError(string message)
    {
        Target = null;
        StatusText = message;
        StatusBrush = Brushes.Tomato;
        SwatchBrush = null;
    }
}
