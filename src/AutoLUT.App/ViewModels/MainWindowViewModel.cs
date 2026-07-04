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

    private RawImage? _lutImage;
    private ObsLutApplier? _lutApplier;
    private int _lutGeneration;
    private int _previewVersion;

    public ObservableCollection<ScreenshotItemViewModel> Screenshots { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private ScreenshotItemViewModel? _selectedScreenshot;

    [ObservableProperty]
    private bool _showCorrected;

    [ObservableProperty]
    private string _statusText = "Load calibration screenshots to begin.";

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLutCommand))]
    private bool _hasLut;

    public MainWindowViewModel(ICalibrationPipeline pipeline, IImageCodec codec, IFilePickerService files)
    {
        _pipeline = pipeline;
        _codec = codec;
        _files = files;
    }

    partial void OnSelectedScreenshotChanged(ScreenshotItemViewModel? value) => _ = UpdatePreviewAsync();

    partial void OnShowCorrectedChanged(bool value) => _ = UpdatePreviewAsync();

    [RelayCommand]
    private async Task AddScreenshotsAsync()
    {
        var picked = await _files.PickPngScreenshotsAsync();
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
        SelectedScreenshot ??= Screenshots.FirstOrDefault();
    }

    private bool CanRemoveSelected() => SelectedScreenshot is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        if (SelectedScreenshot is { } selected)
        {
            int index = Screenshots.IndexOf(selected);
            Screenshots.Remove(selected);
            SelectedScreenshot = Screenshots.Count > 0 ? Screenshots[Math.Min(index, Screenshots.Count - 1)] : null;
            GenerateCommand.NotifyCanExecuteChanged();
        }
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
            if (shot.IsValid)
                items[i].SetOk($"Matched {shot.ReferenceName} ({shot.SampleCount} regions)");
            else
                items[i].SetError($"{shot.Name} {shot.Error}");
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
        StatusText = result.Diagnostics is { } d
            ? $"Finished - mean ΔE {d.MeanDeltaE:F4}, p95 {d.P95DeltaE:F4}, {d.InlierCount}/{d.TotalCount} inliers."
            : "Finished";
        await UpdatePreviewAsync();
    }

    private bool CanSaveLut() => HasLut;

    [RelayCommand(CanExecute = nameof(CanSaveLut))]
    private async Task SaveLutAsync()
    {
        if (_lutImage is null)
            return;

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
