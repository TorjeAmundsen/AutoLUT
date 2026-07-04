using AutoLUT.Core.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoLUT.App.ViewModels;

public partial class ScreenshotItemViewModel : ObservableObject
{
    public string Name { get; }
    public byte[] Data { get; }
    public RawImage Image { get; }

    /// <summary>LUT-corrected pixels, cached per LUT generation.</summary>
    public RawImage? Corrected { get; set; }

    public int CorrectedGeneration { get; set; } = -1;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private IBrush _statusBrush = Brushes.Gray;

    public ScreenshotItemViewModel(string name, byte[] data, RawImage image)
    {
        Name = name;
        Data = data;
        Image = image;
    }

    public void SetOk(string message)
    {
        StatusText = message;
        StatusBrush = Brushes.Green;
    }

    public void SetError(string message)
    {
        StatusText = message;
        StatusBrush = Brushes.Tomato;
    }
}
