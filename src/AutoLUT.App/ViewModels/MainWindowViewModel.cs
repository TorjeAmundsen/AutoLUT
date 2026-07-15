using System.Collections.ObjectModel;
using System.IO.Compression;
using AutoLUT.App.Services;
using AutoLUT.App.Services.Update;
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

    /// <summary>Overlay dialog state for MainView's modal layer; null when dialogs are real windows (desktop).</summary>
    public OverlayDialogService? Overlay => _dialogs as OverlayDialogService;

    /// <summary>In the browser the savestates are not bundled next to an executable - offer them as a download.</summary>
    public bool ShowSavestatesLink => OperatingSystem.IsBrowser();

    /// <summary>The update checker is desktop-only; the button is hidden in the browser build.</summary>
    public bool IsDesktop => !OperatingSystem.IsBrowser();

    /// <summary>Step-through "How to use" guide shown in place of the preview pane.</summary>
    public HelpWizardViewModel Help { get; } = new();

    // Starting the guide clears any loaded work so the user follows it from a clean slate.
    [RelayCommand]
    private void ToggleHelp()
    {
        if (Help.IsOpen)
        {
            Help.IsOpen = false;
        }
        else
        {
            ResetCore();
            Help.Open();
        }
    }

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

    [RelayCommand]
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

    /// <summary>Decodes and adds screenshots; shared by the file picker and the guide's drag-and-drop.</summary>
    public async Task AddScreenshotDataAsync(IReadOnlyList<(string Name, byte[] Data)> picked)
    {
        foreach (var (name, data) in picked)
        {
            try
            {
                var image = await Task.Run(() =>
                {
                    using var stream = new MemoryStream(data);
                    return _codec.Decode(stream);
                });
                Screenshots.Add(new ScreenshotItemViewModel(name, data, image));
            }
            catch (InvalidDataException)
            {
                StatusText = $"{name} could not be read as a PNG image.";
            }
        }

        GenerateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        SelectedScreenshot ??= Screenshots.FirstOrDefault();
    }

    private bool CanReset() => Screenshots.Count > 0;

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
        PreviewImage = null;
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

    private bool CanGenerate() => Screenshots.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var inputs = Screenshots.Select(s => new ScreenshotInput(s.Name, s.Data)).ToList();
        var items = Screenshots.ToList();
        var progress = new Progress<PipelineProgress>(p => StatusText = p.Message);

        LastDetails = null;
        IsDetailsOpen = false;

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

        string warningSuffix = result.Warnings.Count > 0 ? $" Warning: {string.Join(" ", result.Warnings)}" : "";
        if (!result.Success)
        {
            StatusText = (result.Error ?? "Calibration failed.") + warningSuffix;
            return;
        }

        var lutImage = result.LutImage!;
        var applier = await Task.Run(() => new ObsLutApplier(lutImage));
        _lutImage = lutImage;
        _lutApplier = applier;
        _lutGeneration++;
        HasLut = true;
        ShowCorrected = true;
        // The guide occupies the preview pane; close it so the corrected preview is visible.
        Help.IsOpen = false;
        StatusText = (result.Diagnostics is { } d
            ? $"Finished - mean ΔE {d.MeanDeltaE:F4}, p95 {d.P95DeltaE:F4}, {d.InlierCount}/{d.TotalCount} inliers."
            : "Finished") + warningSuffix;
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

            StatusText = "LUT.png saved. In OBS: Filters → Apply LUT → select the file.";
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
            PreviewImage = null;
            return;
        }

        var source = item.Image;
        if (ShowCorrected && _lutApplier is { } applier)
        {
            if (item.Corrected is null || item.CorrectedGeneration != _lutGeneration)
            {
                var corrected = await Task.Run(() => applier.Apply(item.Image));
                if (version != _previewVersion)
                {
                    return; // superseded by a newer preview request
                }

                item.Corrected = corrected;
                item.CorrectedGeneration = _lutGeneration;
            }

            source = item.Corrected;
        }

        PreviewImage = PreviewRenderer.ToBitmap(source);
    }
}
