using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AutoLUT.App.Services;
using AutoLUT.App.ViewModels;

namespace AutoLUT.App.Views;

public partial class MainView : UserControl
{
    private const string TutorialUrl = "https://github.com/TorjeAmundsen/AutoLUT/blob/main/TUTORIAL.md";
    private const string ReleasesUrl = "https://github.com/TorjeAmundsen/AutoLUT/releases";

    /// <summary>What had focus before an overlay opened, to give it back when the last one closes.</summary>
    private IInputElement? _focusBeforeOverlay;

    public MainView()
    {
        InitializeComponent();
        // DragOver/Drop are attached routed events, so they cannot be wired from XAML. Handling
        // them on the view itself makes the whole window a drop target: drops on any child
        // bubble up here, and the view's own background keeps empty areas hit-testable.
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);

        ScreenshotList.KeyDown += OnScreenshotListKeyDown;

        // The view model ignores the cleared selection from a Ctrl+click, but the picker still
        // shows nothing selected until it is given the current step again.
        StepPicker.SelectionChanged += (_, _) =>
        {
            if (StepPicker.SelectedItem is null && DataContext is MainWindowViewModel { Help.CurrentStep: { } step })
            {
                Dispatcher.UIThread.Post(() => StepPicker.SelectedItem = step);
            }
        };

        // The scroll viewer is shared by every step, so without this a new step opens at the
        // previous step's scroll offset instead of its own top.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Help.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(HelpWizardViewModel.CurrentStep))
                    {
                        GuideScroll.ScrollToHome();
                    }
                };

                vm.PropertyChanged += OnOverlayStateChanged;
                if (vm.Overlay is { } overlay)
                {
                    overlay.PropertyChanged += OnOverlayStateChanged;
                }
            }
        };

        // Escape closes the topmost overlay. Enter needs no handler: focus moves onto the
        // overlay's button when it opens, and a focused button clicks on Enter.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (vm.Overlay is { IsOpen: true } overlay)
            {
                overlay.DismissCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsDetailsOpen)
            {
                vm.CloseDetailsCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private void OnOverlayStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsDetailsOpen) or nameof(OverlayDialogService.IsOpen))
        {
            UpdateOverlayFocus();
        }
    }

    /// <summary>
    /// Keeps keyboard focus inside the topmost overlay: everything behind it is disabled, so
    /// Tab cannot wander there, and focus starts on the overlay's button. When the last overlay
    /// closes, focus returns to where it was before the first one opened.
    /// </summary>
    private void UpdateOverlayFocus()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        bool warningOpen = vm.Overlay?.IsOpen == true;
        bool detailsOpen = vm.IsDetailsOpen;
        bool anyOpen = warningOpen || detailsOpen;

        if (anyOpen && MainContent.IsEnabled)
        {
            _focusBeforeOverlay = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }

        MainContent.IsEnabled = !anyOpen;
        DetailsCard.IsEnabled = !warningOpen;

        // Posted: the overlay's IsVisible binding may not have updated yet, and an invisible
        // control refuses focus.
        Dispatcher.UIThread.Post(() =>
        {
            if (warningOpen)
            {
                OverlayOkButton.Focus(NavigationMethod.Tab);
            }
            else if (detailsOpen)
            {
                DetailsCloseButton.Focus(NavigationMethod.Tab);
            }
            else if (_focusBeforeOverlay is { } previous)
            {
                _focusBeforeOverlay = null;
                previous.Focus();
            }
        });
    }

    private void OnScreenshotListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete
            && DataContext is MainWindowViewModel { SelectedScreenshot: { } selected } vm
            && vm.RemoveScreenshotCommand.CanExecute(selected))
        {
            vm.RemoveScreenshotCommand.Execute(selected);
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        try
        {
            if (e.DataTransfer.TryGetFiles() is not { } items)
            {
                return;
            }

            // Dropped files go to the decoder whatever their extension, so a wrong file is
            // reported by name. A dropped folder contributes only its PNGs: it usually holds
            // other files too, and those are not mistakes worth reporting.
            var files = new List<(string Name, byte[] Data)>();
            foreach (var item in items)
            {
                if (item is IStorageFile file)
                {
                    files.Add((file.Name, await ReadAllBytesAsync(file)));
                }
                else if (item is IStorageFolder folder)
                {
                    await foreach (var child in folder.GetItemsAsync())
                    {
                        if (child is IStorageFile childFile && childFile.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            files.Add((childFile.Name, await ReadAllBytesAsync(childFile)));
                        }
                    }
                }
            }

            if (files.Count == 0)
            {
                vm.StatusText = "Nothing to load: drop PNG screenshots, or a folder that contains them.";
                return;
            }

            await vm.AddScreenshotDataAsync(files);
        }
        catch (Exception ex)
        {
            // A failed drop must never take the app down, but it must not fail silently either.
            vm.StatusText = $"Could not read the dropped files: {ex.Message}";
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private async void OnTutorialLinkClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(TutorialUrl));
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
                await topLevel.Launcher.LaunchUriAsync(new Uri(baseUri, "AutoLUT-Palette-Wii.zip"));
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
                await topLevel.Launcher.LaunchUriAsync(new Uri(baseUri, "AutoLUT-Palette-N64.z64"));
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
