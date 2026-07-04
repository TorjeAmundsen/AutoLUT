using AutoLUT.App.Services;
using AutoLUT.App.ViewModels;
using AutoLUT.App.Views;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Pipeline;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AutoLUT.App;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Hand-wired composition root - no DI container, keeps Native AOT trivially safe.
            var codec = new SkiaImageCodec();
            var window = new MainWindow();
            window.DataContext = new MainWindowViewModel(
                CalibrationPipeline.CreateDefault(codec),
                codec,
                new FilePickerService(window));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
