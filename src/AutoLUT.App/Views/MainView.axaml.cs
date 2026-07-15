using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AutoLUT.App.ViewModels;

namespace AutoLUT.App.Views;

public partial class MainView : UserControl
{
    private const string ReadmeUrl = "https://github.com/TorjeAmundsen/AutoLUT#readme";
    private const string ReleasesUrl = "https://github.com/TorjeAmundsen/AutoLUT/releases";

    public MainView()
    {
        InitializeComponent();
        // DragOver/Drop are attached routed events, so they cannot be wired from XAML.
        DragDrop.AddDragOverHandler(HelpGuide, OnGuideDragOver);
        DragDrop.AddDropHandler(HelpGuide, OnGuideDrop);
    }

    private void OnGuideDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnGuideDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (DataContext is not MainWindowViewModel vm || e.DataTransfer.TryGetFiles() is not { } items)
            {
                return;
            }

            var files = new List<(string Name, byte[] Data)>();
            foreach (var item in items)
            {
                if (item is IStorageFile file && file.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    await using var stream = await file.OpenReadAsync();
                    using var memory = new MemoryStream();
                    await stream.CopyToAsync(memory);
                    files.Add((file.Name, memory.ToArray()));
                }
            }

            if (files.Count > 0)
            {
                await vm.AddScreenshotDataAsync(files);
            }
        }
        catch
        {
            // A failed drop must never take the app down.
        }
    }

    private async void OnReadmeLinkClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(ReadmeUrl));
            }
        }
        catch
        {
            // Opening a link must never take the app down.
        }
    }

    private async void OnWiiAppLinkClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // The zip sits next to index.html on the Pages site, same as the savestates.
            if (App.BaseUri is { } baseUri
                && TopLevel.GetTopLevel(this) is { } topLevel)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(baseUri, "AutoLUT-Palette.zip"));
            }
        }
        catch
        {
            // Opening a download must never take the app down.
        }
    }

    private async void OnN64RomLinkClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // The ROM sits next to index.html on the Pages site, same as the savestates.
            if (App.BaseUri is { } baseUri
                && TopLevel.GetTopLevel(this) is { } topLevel)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(baseUri, "autolut-palette.z64"));
            }
        }
        catch
        {
            // Opening a download must never take the app down.
        }
    }

    private async void OnWiiCopyClick(object? sender, RoutedEventArgs e) =>
        // Copying the apps folder itself (not its contents) lets a paste into the SD card root
        // merge with the apps folder that Homebrew Channel users already have.
        await CopyBundleItemsAsync(
            [Path.Combine(AppContext.BaseDirectory, "wii", "apps")],
            areFolders: true,
            "Copied - paste the apps folder into the root of your SD card.");

    private async void OnN64CopyClick(object? sender, RoutedEventArgs e) =>
        await CopyBundleItemsAsync(
            [Path.Combine(AppContext.BaseDirectory, "n64", "autolut-palette.z64")],
            areFolders: false,
            "Copied - paste the ROM onto your flashcart's SD card.");

    private async void OnGzCopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string version })
        {
            return;
        }

        await CopyBundleItemsAsync(
            [Path.Combine(AppContext.BaseDirectory, "savestates", $"lut_gzs_{version}")],
            areFolders: true,
            $"Copied - paste the {version} savestates folder onto your SD card.");
    }

    /// <summary>
    /// Puts bundled artifacts on the clipboard as files for pasting onto an SD card.
    /// Falls back to the GitHub releases page when the artifacts are not next to the
    /// program (e.g. running from a build directory).
    /// </summary>
    private async Task CopyBundleItemsAsync(IReadOnlyList<string> paths, bool areFolders, string successStatus)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
            {
                return;
            }

            var items = new List<IStorageItem>();
            foreach (var path in paths)
            {
                IStorageItem? item = areFolders
                    ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(path))
                    : await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            if (items.Count == 0)
            {
                vm.StatusText = "The bundled files were not found next to the program - opening the GitHub releases page.";
                await topLevel.Launcher.LaunchUriAsync(new Uri(ReleasesUrl));
                return;
            }

            if (topLevel.Clipboard is { } clipboard)
            {
                await clipboard.SetFilesAsync(items);
                vm.StatusText = successStatus;
            }
        }
        catch
        {
            // A failed copy must never take the app down.
        }
    }

    private async void OnBundleFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Control { Tag: string subfolder }
                && TopLevel.GetTopLevel(this) is { } topLevel)
            {
                var folder = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, subfolder));
                if (folder.Exists)
                {
                    await topLevel.Launcher.LaunchDirectoryInfoAsync(folder);
                }
                else
                {
                    // Missing next to the program (e.g. a build directory) - point at releases.
                    await topLevel.Launcher.LaunchUriAsync(new Uri(ReleasesUrl));
                }
            }
        }
        catch
        {
            // Opening a folder must never take the app down.
        }
    }

    private async void OnSavestatesVersionClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Each version's zip sits next to index.html on the Pages site; base URL comes from location.href.
            if (sender is Control { Tag: string version }
                && App.BaseUri is { } baseUri
                && TopLevel.GetTopLevel(this) is { } topLevel)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(baseUri, $"savestates-{version}.zip"));
            }
        }
        catch
        {
            // Opening a download must never take the app down.
        }
    }
}
