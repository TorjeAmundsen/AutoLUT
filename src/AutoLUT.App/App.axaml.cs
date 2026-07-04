using AutoLUT.App.Services;
using AutoLUT.App.ViewModels;
using AutoLUT.App.Views;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Pipeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AutoLUT.App;

public class App : Application
{
    /// <summary>Site base URL when running in the browser (set by the browser head from location.href).</summary>
    public static Uri? BaseUri { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Hand-wired composition root - no DI container, keeps Native AOT trivially safe.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = CreateViewModel(() => window, new DialogService(window));
            desktop.MainWindow = window;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Browser: no windows - single view with the overlay-based dialog service.
            var view = new MainView();
            view.DataContext = CreateViewModel(() => TopLevel.GetTopLevel(view), new OverlayDialogService());
            singleView.MainView = view;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindowViewModel CreateViewModel(Func<TopLevel?> topLevel, IDialogService dialogs)
    {
        var codec = new SkiaImageCodec();
        return new MainWindowViewModel(
            CalibrationPipeline.CreateDefault(codec),
            codec,
            new FilePickerService(topLevel),
            dialogs);
    }
}
