using System.Reflection;
using Avalonia.Controls;

namespace AutoLUT.App.Views;

public partial class MainWindow : Window
{
    public static readonly string AppVersion =
        typeof(MainWindow).Assembly
                          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion ?? "unknown";

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AutoLUT v{AppVersion} - OoT Capture Color Calibration";
    }
}
