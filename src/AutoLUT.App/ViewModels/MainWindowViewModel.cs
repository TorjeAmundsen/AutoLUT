using System.Collections.ObjectModel;
using System.IO.Compression;
using AutoLUT.App.Services;
using AutoLUT.App.Services.Update;
using AutoLUT.Core.Calibration;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.Pipeline;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoLUT.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ICalibrationPipeline _pipeline;
    private readonly IImageCodec _codec;
    private readonly IFilePickerService _files;
    private readonly IDialogService _dialogs;
    private readonly Func<TopLevel?> _topLevel;

    private RawImage? _lutImage;
    private ObsLutApplier? _lutApplier;
    private int _lutGeneration;
    private int _previewVersion;

    // Only the selected screenshot is ever held decoded: its source pixels, and its LUT-corrected
    // pixels for the current LUT generation. Selecting another screenshot replaces both.
    private (ScreenshotItemViewModel Item, RawImage Image)? _previewSource;
    private (ScreenshotItemViewModel Item, int Generation, RawImage Image)? _previewCorrected;

    public ObservableCollection<ScreenshotItemViewModel> Screenshots { get; } = [];

    [ObservableProperty]
    private ScreenshotItemViewModel? _selectedScreenshot;

    [ObservableProperty]
    private bool _showCorrected;

    [ObservableProperty]
    private string _statusText = "Capture the 39 calibration colors, then add the screenshots here.";

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLutCommand))]
    private bool _hasLut;

    /// <summary>True while screenshots load or a LUT generates. The screenshot list is locked
    /// meanwhile: a run maps its results back onto the items by position, and a load in flight
    /// would add items that Reset had already cleared.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddScreenshotsCommand), nameof(RemoveScreenshotCommand), nameof(ResetCommand), nameof(GenerateCommand))]
    private bool _isBusy;

    /// <summary>Overlay dialog state for MainView's modal layer; null when dialogs are real windows (desktop).</summary>
    public OverlayDialogService? Overlay => _dialogs as OverlayDialogService;

    /// <summary>In the browser the savestates are not bundled next to an executable - offer them as a download.</summary>
    public bool ShowSavestatesLink => OperatingSystem.IsBrowser();

    /// <summary>The update checker is desktop-only; the button is hidden in the browser build.</summary>
    public bool IsDesktop => !OperatingSystem.IsBrowser();

    /// <summary>Step-through "How to use" guide shown in place of the preview pane.</summary>
    public HelpWizardViewModel Help { get; } = new();

    [RelayCommand]
    private void ToggleHelp() => Help.IsOpen = !Help.IsOpen;

    [ObservableProperty]
    private CalibrationDetailsViewModel? _lastDetails;

    [ObservableProperty]
    private bool _isDetailsOpen;

    [ObservableProperty]
    private string? _detailsFeedback;

    [RelayCommand]
    private void OpenDetails()
    {
        DetailsFeedback = null;
        IsDetailsOpen = true;
    }

    [RelayCommand]
    private void CloseDetails() => IsDetailsOpen = false;

    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        if (LastDetails is null)
        {
            return;
        }

        var clipboard = _topLevel()?.Clipboard;
        if (clipboard is null)
        {
            DetailsFeedback = "Clipboard is not available.";
            return;
        }

        await clipboard.SetTextAsync(LastDetails.BuildReportText());
        DetailsFeedback = "Copied to clipboard.";
    }

    [RelayCommand]
    private async Task SaveDetailsZipAsync()
    {
        if (LastDetails is null)
        {
            return;
        }

        try
        {
            var stream = await _files.CreateSaveZipAsync("autolut-debug.zip");
            if (stream is null)
            {
                return;
            }

            await using (stream)
            {
                // Build the archive in memory first: browser save streams are not seekable,
                // which ZipArchive needs when writing directly.
                using var memory = new MemoryStream();
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var detailsEntry = zip.CreateEntry("details.txt");
                    await using (var writer = new StreamWriter(detailsEntry.Open()))
                    {
                        await writer.WriteAsync(LastDetails.BuildReportText());
                    }

                    var inputs = LastDetails.Inputs;
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        // Index prefix keeps entries unique even if two loaded files share a name.
                        var entry = zip.CreateEntry($"screenshots/{i + 1:D2}_{inputs[i].Name}");
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(inputs[i].Data);
                    }
                }

                memory.Position = 0;
                await memory.CopyToAsync(stream);
            }

            DetailsFeedback = "Debug zip saved.";
        }
        catch (Exception ex)
        {
            DetailsFeedback = $"Save failed: {ex.Message}";
        }
    }

    public MainWindowViewModel(ICalibrationPipeline pipeline, IImageCodec codec, IFilePickerService files, IDialogService dialogs, Func<TopLevel?> topLevel)
    {
        _pipeline = pipeline;
        _codec = codec;
        _files = files;
        _dialogs = dialogs;
        _topLevel = topLevel;
    }

    partial void OnSelectedScreenshotChanged(ScreenshotItemViewModel? value) => _ = UpdatePreviewAsync();

    partial void OnShowCorrectedChanged(bool value) => _ = UpdatePreviewAsync();

    private bool CanEditScreenshots() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEditScreenshots))]
    private async Task AddScreenshotsAsync()
    {
        IReadOnlyList<(string Name, byte[] Data)> picked;
        try
        {
            picked = await _files.PickPngScreenshotsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not read the selected files: {ex.Message}";
            return;
        }

        await AddScreenshotDataAsync(picked);
    }

    /// <summary>Decodes and adds screenshots; shared by the file picker and drag-and-drop.
    /// Each file is decoded once, to measure it; only its bytes and the measurement are kept.
    /// Files identical to one already loaded are skipped.</summary>
    public async Task AddScreenshotDataAsync(IReadOnlyList<(string Name, byte[] Data)> picked)
    {
        if (IsBusy)
        {
            StatusText = "Wait for the current task to finish before adding screenshots.";
            return;
        }

        var duplicateNames = new List<string>();
        var files = new List<(string Name, byte[] Data)>();
        foreach (var file in picked)
        {
            // SequenceEqual compares lengths first, so this stays cheap for distinct captures.
            bool isDuplicate = Screenshots.Any(s => s.Data.AsSpan().SequenceEqual(file.Data))
                || files.Any(f => f.Data.AsSpan().SequenceEqual(file.Data));
            if (isDuplicate)
            {
                duplicateNames.Add(file.Name);
            }
            else
            {
                files.Add(file);
            }
        }

        string previousStatus = StatusText;
        IsBusy = true;
        try
        {
            SolidAnalysis?[] analyses;
            if (OperatingSystem.IsBrowser())
            {
                // No worker threads in the browser: decode one file at a time and let the page
                // paint between files, so the tab shows progress instead of freezing.
                analyses = new SolidAnalysis?[files.Count];
                for (int i = 0; i < files.Count; i++)
                {
                    StatusText = $"Loading {files[i].Name} ({i + 1}/{files.Count})...";
                    await BrowserYield.ForFrameAsync();
                    analyses[i] = TryAnalyze(files[i].Data);
                }
            }
            else
            {
                analyses = await Task.WhenAll(files.Select(file => Task.Run(() => TryAnalyze(file.Data))));
            }

            StatusText = previousStatus;
            var failedNames = new List<string>();
            int addedCount = 0;
            for (int i = 0; i < files.Count; i++)
            {
                if (analyses[i] is { } analysis)
                {
                    Screenshots.Add(new ScreenshotItemViewModel(files[i].Name, files[i].Data, analysis));
                    addedCount++;
                }
                else
                {
                    failedNames.Add(files[i].Name);
                }
            }

            if (addedCount > 0)
            {
                InvalidateResults();
            }

            var problems = new List<string>();
            if (failedNames.Count == 1)
            {
                problems.Add($"{failedNames[0]} could not be read as an image.");
            }
            else if (failedNames.Count > 1)
            {
                problems.Add($"{failedNames.Count} files could not be read as images: {string.Join(", ", failedNames)}.");
            }

            if (duplicateNames.Count == 1)
            {
                problems.Add($"Skipped {duplicateNames[0]}: the same image is already loaded.");
            }
            else if (duplicateNames.Count > 1)
            {
                problems.Add($"Skipped {duplicateNames.Count} images that are already loaded: {string.Join(", ", duplicateNames)}.");
            }

            if (problems.Count > 0)
            {
                StatusText = string.Join(" ", problems);
            }
        }
        finally
        {
            IsBusy = false;
        }

        GenerateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        SelectedScreenshot ??= Screenshots.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanEditScreenshots))]
    private void RemoveScreenshot(ScreenshotItemViewModel? item)
    {
        int index = item is null ? -1 : Screenshots.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        if (Screenshots.Count == 1)
        {
            ResetCore();
            return;
        }

        bool wasSelected = SelectedScreenshot == item;
        Screenshots.RemoveAt(index);
        if (_previewSource?.Item == item)
        {
            _previewSource = null;
            _previewCorrected = null;
        }

        if (wasSelected)
        {
            SelectedScreenshot = Screenshots[Math.Min(index, Screenshots.Count - 1)];
        }

        InvalidateResults();
        GenerateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Drops a finished run's LUT, details and per-screenshot statuses once the loaded
    /// screenshots change, so nothing on screen describes a set of captures that is gone.</summary>
    private void InvalidateResults()
    {
        if (!HasLut && LastDetails is null)
        {
            return;
        }

        _lutImage = null;
        _lutApplier = null;
        HasLut = false;
        ShowCorrected = false;
        LastDetails = null;
        IsDetailsOpen = false;
        foreach (var screenshot in Screenshots)
        {
            screenshot.ClearStatus();
        }

        StatusText = "The screenshots changed. Click Generate LUT to make a new LUT.";
    }

    /// <summary>Center-region analysis of an image file, or null if it cannot be decoded.</summary>
    private SolidAnalysis? TryAnalyze(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            return SolidColorAnalyzer.Analyze(_codec.Decode(stream));
        }
        catch (Exception)
        {
            // Not an image, truncated, or an unsupported format: reported by name, never a crash.
            return null;
        }
    }

    private bool CanReset() => Screenshots.Count > 0 && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset() => ResetCore();

    private void ResetCore()
    {
        Screenshots.Clear();
        SelectedScreenshot = null;
        _lutImage = null;
        _lutApplier = null;
        HasLut = false;
        ShowCorrected = false;
        _previewSource = null;
        _previewCorrected = null;
        SetPreview(null);
        StatusText = "Capture the 39 calibration colors, then add the screenshots here.";
        LastDetails = null;
        IsDetailsOpen = false;
        GenerateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (_topLevel() is Window window)
        {
            await new UpdateService().CheckManuallyAsync(window, _dialogs);
        }
    }

    private bool CanGenerate() => Screenshots.Count > 0 && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        IsBusy = true;
        try
        {
            await GenerateCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GenerateCoreAsync()
    {
        var inputs = Screenshots.Select(s => new ScreenshotInput(s.Name, s.Data, s.Analysis)).ToList();
        var items = Screenshots.ToList();
        var progress = new Progress<PipelineProgress>(p => StatusText = p.Message);

        LastDetails = null;
        IsDetailsOpen = false;

        // Drop the previous run's LUT up front: if this run fails, Save and the corrected
        // preview must not keep offering a LUT that no longer matches the loaded screenshots.
        _lutImage = null;
        _lutApplier = null;
        HasLut = false;
        ShowCorrected = false;

        CalibrationResult result;
        try
        {
            result = await _pipeline.RunAsync(inputs, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText = $"Calibration failed: {ex.Message}";
            return;
        }

        LastDetails = CalibrationDetailsViewModel.From(result, inputs);

        for (int i = 0; i < items.Count && i < result.Screenshots.Count; i++)
        {
            var shot = result.Screenshots[i];
            if (shot is { IsValid: true, Target: { } target })
            {
                if (shot.IsOutlier)
                {
                    items[i].SetOutlier(target);
                }
                else
                {
                    items[i].SetIdentified(target);
                }
            }
            else
            {
                items[i].SetError($"{shot.Name} {shot.Error ?? "was not identified."}");
            }
        }

        // A color range mismatch silently ruins every capture, so it gets a modal dialog
        // instead of just a status-bar line.
        if (result.ColorRangeWarning is { } rangeWarning)
        {
            await _dialogs.ShowWarningAsync("Color range mismatch detected", rangeWarning);
        }

        // A color space mismatch distorts every chromatic capture, so it also gets a modal.
        if (result.ColorSpaceWarning is { } colorSpaceWarning)
        {
            await _dialogs.ShowWarningAsync("Color space mismatch detected", colorSpaceWarning);
        }

        // Crushed shadows lose detail the LUT cannot bring back, so the user should know even
        // though there is no setting to fix - the loss is permanent in the capture chain.
        if (result.CrushWarning is { } crushWarning)
        {
            await _dialogs.ShowWarningAsync("Crushed shadows detected", crushWarning);
        }

        // Remaining warnings also get a modal at generate time; afterwards they stay
        // reachable via the details button instead of bloating the status bar.
        if (result.Warnings.Count > 0)
        {
            await _dialogs.ShowWarningAsync("Warning", string.Join("\n\n", result.Warnings));
        }

        if (!result.Success)
        {
            StatusText = result.Error ?? "Calibration failed.";
            return;
        }

        var lutImage = result.LutImage!;
        var applier = await Task.Run(() => new ObsLutApplier(lutImage));
        _lutImage = lutImage;
        _lutApplier = applier;
        _lutGeneration++;
        HasLut = true;
        ShowCorrected = true;
        // Both warnings mean the captures need redoing, so pointing the user at saving would be wrong.
        bool mustRecapture = result.ColorRangeWarning is not null || result.ColorSpaceWarning is not null;
        if (!mustRecapture)
        {
            Help.MovePastGenerateStep();
        }

        string next = mustRecapture ? "" : " Next: click Save LUT.png.";
        StatusText = result.Diagnostics is { } d
            ? $"Finished - mean dE {d.MeanDeltaE:F4}, p95 {d.P95DeltaE:F4}, {d.InlierCount}/{d.TotalCount} inliers.{next}"
            : $"Finished.{next}";
        await UpdatePreviewAsync();
    }

    private bool CanSaveLut() => HasLut;

    [RelayCommand(CanExecute = nameof(CanSaveLut))]
    private async Task SaveLutAsync()
    {
        if (_lutImage is null)
        {
            return;
        }

        try
        {
            var stream = await _files.CreateSaveFileAsync("LUT.png");
            if (stream is null)
            {
                return;
            }

            await using (stream)
            {
                var lutImage = _lutImage;
                using var memory = new MemoryStream();
                await Task.Run(() => _codec.EncodePng(lutImage, memory));
                memory.Position = 0;
                await memory.CopyToAsync(stream);
            }

            StatusText = "LUT.png saved. In OBS: Filters > Apply LUT > select the file.";
        }
        catch (Exception ex)
        {
            // Writing into protected locations (e.g. OBS's own LUTs folder under Program Files)
            // throws UnauthorizedAccessException without elevation - report instead of crashing.
            await _dialogs.ShowWarningAsync("Save failed",
                $"Could not save the LUT: {ex.Message}\n\nPick a folder you have write access to, such as "
                + "Documents or Desktop. OBS can load LUT.png from any location - it does not need to be "
                + "in the OBS program folder.");
            StatusText = "Save failed.";
        }
    }

    private async Task UpdatePreviewAsync()
    {
        int version = ++_previewVersion;
        var item = SelectedScreenshot;
        if (item is null)
        {
            SetPreview(null);
            return;
        }

        RawImage source;
        if (_previewSource is { } cachedSource && cachedSource.Item == item)
        {
            source = cachedSource.Image;
        }
        else
        {
            source = await Task.Run(() =>
            {
                using var stream = new MemoryStream(item.Data);
                return _codec.Decode(stream);
            });
            if (version != _previewVersion)
            {
                return; // superseded by a newer preview request
            }

            _previewSource = (item, source);
            _previewCorrected = null;
        }

        if (ShowCorrected && _lutApplier is { } applier)
        {
            if (_previewCorrected is { } cachedCorrected && cachedCorrected.Item == item && cachedCorrected.Generation == _lutGeneration)
            {
                source = cachedCorrected.Image;
            }
            else
            {
                var uncorrected = source;
                source = await Task.Run(() => applier.Apply(uncorrected));
                if (version != _previewVersion)
                {
                    return; // superseded by a newer preview request
                }

                _previewCorrected = (item, _lutGeneration, source);
            }
        }

        SetPreview(PreviewRenderer.ToBitmap(source));
    }

    /// <summary>Swaps the preview bitmap and frees the old one's native pixel buffer right away
    /// instead of leaving it to the finalizer; the binding has already moved to the new one.</summary>
    private void SetPreview(Bitmap? bitmap)
    {
        var old = PreviewImage;
        PreviewImage = bitmap;
        old?.Dispose();
    }
}
