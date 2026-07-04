using Avalonia;
using Avalonia.Browser;
using AutoLUT.App;

internal static class Program
{
    private static Task Main(string[] args)
    {
        // main.js passes location.href - needed to build absolute URLs (savestates.zip download).
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var baseUri))
            App.BaseUri = baseUri;

        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .WithInterFont();
}
