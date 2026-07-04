using System.Collections.ObjectModel;
using AutoLUT.App.Services;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using AutoLUT.Core.Pipeline;
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
    private string _statusText = "Capture the 39 gz calibration colors, then add the screenshots here.";

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLutCommand))]
    private bool _hasLut;

    public MainWindowViewModel(ICalibrationPipeline pipeline, IImageCodec codec, IFilePickerService files, IDialogService dialogs)
    {
        _pipeline = pipeline;
        _codec = codec;
        _files = files;
        _dialogs = dialogs;
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
    private void Reset()
    {
        Screenshots.Clear();
        SelectedScreenshot = null;
        _lutImage = null;
        _lutApplier = null;
        HasLut = false;
        ShowCorrected = false;
        PreviewImage = null;
        StatusText = "Capture the 39 gz calibration colors, then add the screenshots here.";
        GenerateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private bool CanGenerate() => Screenshots.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var inputs = Screenshots.Select(s => new ScreenshotInput(s.Name, s.Data)).ToList();
        var items = Screenshots.ToList();
        var progress = new Progress<PipelineProgress>(p => StatusText = p.Message);

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

        for (int i = 0; i < items.Count && i < result.Screenshots.Count; i++)
        {
            var shot = result.Screenshots[i];
            if (shot is { IsValid: true, Target: { } target })
            {
                if (shot.IsOutlier)
                    items[i].SetOutlier(target);
                else
                    items[i].SetIdentified(target);
            }
            else
            {
                items[i].SetError($"{shot.Name} {shot.Error ?? "was not identified."}");
            }
        }

        // A color range mismatch silently ruins every capture, so it gets a modal dialog
        // instead of just a status-bar line.
        if (result.ColorRangeWarning is { } rangeWarning)
            await _dialogs.ShowWarningAsync("Color range mismatch detected", rangeWarning);

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
            return;

        try
        {
            var stream = await _files.CreateSaveFileAsync("LUT.png");
            if (stream is null)
                return;

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
                    return; // superseded by a newer preview request
                item.Corrected = corrected;
                item.CorrectedGeneration = _lutGeneration;
            }

            source = item.Corrected;
        }

        PreviewImage = PreviewRenderer.ToBitmap(source);
    }
}
