using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AutoLUT.App.Views;

public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private async void OnSavestatesLinkClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // The zip sits next to index.html on the Pages site; base URL comes from location.href.
            if (App.BaseUri is { } baseUri && TopLevel.GetTopLevel(this) is { } topLevel)
                await topLevel.Launcher.LaunchUriAsync(new Uri(baseUri, "savestates.zip"));
        }
        catch
        {
            // Opening a download must never take the app down.
        }
    }
}
