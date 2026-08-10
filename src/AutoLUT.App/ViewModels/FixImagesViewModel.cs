using System.Collections.ObjectModel;
using System.IO.Compression;
using AutoLUT.App.Services;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.Lut;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoLUT.App.ViewModels;

/// <summary>
/// Overlay for re-applying a LUT to existing AutoSplit reference images, so the user does not
/// have to re-capture them after correcting their capture with the generated LUT.
/// </summary>
public partial class FixImagesViewModel : ObservableObject
{
    private readonly IImageCodec _codec;
    private readonly IFilePickerService _files;
    private ObsLutApplier? _generatedApplier;
    private ObsLutApplier? _customApplier;
    private ObsLutInverter? _oldLutInverter;

    /// <summary>The alpha plane is kept separately: AutoSplit uses PNG transparency as comparison masks.</summary>
    public sealed record FixImageItem(string Name, RawImage Image, byte[]? Alpha);

    public ObservableCollection<FixImageItem> Images { get; } = [];

    public bool HasImages => Images.Count > 0;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _useGeneratedLut;

    [ObservableProperty]
    private bool _hasGeneratedLut;

    [ObservableProperty]
    private string? _customLutName;

    [ObservableProperty]
    private string? _oldLutName;

    public bool HasOldLut => _oldLutInverter is not null;

    [ObservableProperty]
    private string? _feedback;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    public FixImagesViewModel(IImageCodec codec, IFilePickerService files)
    {
        _codec = codec;
        _files = files;
    }

    public void Open(ObsLutApplier? generatedApplier)
    {
        _generatedApplier = generatedApplier;
        HasGeneratedLut = generatedApplier is not null;
        UseGeneratedLut = HasGeneratedLut;
        Feedback = null;
        IsOpen = true;
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task LoadCustomLutAsync()
    {
        (string Name, byte[] Data)? picked;
        try
        {
            picked = await _files.PickSinglePngAsync("Select OBS LUT PNG");
        }
        catch (Exception ex)
        {
            Feedback = $"Could not read the selected file: {ex.Message}";
            return;
        }

        if (picked is not { } file)
        {
            return;
        }

        try
        {
            var applier = await Task.Run(() =>
            {
                using var stream = new MemoryStream(file.Data);
                return new ObsLutApplier(_codec.Decode(stream));
            });
            _customApplier = applier;
            CustomLutName = file.Name;
            UseGeneratedLut = false;
            Feedback = null;
            ApplyCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Feedback = $"{file.Name} is not a valid OBS LUT: expected a 512x512 PNG.";
        }
    }

    [RelayCommand]
    private async Task LoadOldLutAsync()
    {
        (string Name, byte[] Data)? picked;
        try
        {
            picked = await _files.PickSinglePngAsync("Select the LUT that was active when the images were taken");
        }
        catch (Exception ex)
        {
            Feedback = $"Could not read the selected file: {ex.Message}";
            return;
        }

        if (picked is not { } file)
        {
            return;
        }

        try
        {
            var inverter = await Task.Run(() =>
            {
                using var stream = new MemoryStream(file.Data);
                return new ObsLutInverter(new ObsLutApplier(_codec.Decode(stream)));
            });
            _oldLutInverter = inverter;
            OldLutName = file.Name;
            Feedback = null;
            OnPropertyChanged(nameof(HasOldLut));
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Feedback = $"{file.Name} is not a valid OBS LUT: expected a 512x512 PNG.";
        }
    }

    [RelayCommand]
    private void ClearOldLut()
    {
        _oldLutInverter = null;
        OldLutName = null;
        OnPropertyChanged(nameof(HasOldLut));
    }

    [RelayCommand]
    private async Task SelectImagesAsync()
    {
        IReadOnlyList<(string Name, byte[] Data)> picked;
        try
        {
            picked = await _files.PickPngImagesAsync("Select AutoSplit images");
        }
        catch (Exception ex)
        {
            Feedback = $"Could not read the selected files: {ex.Message}";
            return;
        }

        string? error = null;
        foreach (var (name, data) in picked)
        {
            try
            {
                var (image, alpha) = await Task.Run(() =>
                {
                    using var stream = new MemoryStream(data);
                    return _codec.DecodeWithAlpha(stream);
                });

                var item = new FixImageItem(name, image, alpha);
                // Picking a file with the same name again replaces it, so zip entry names
                // stay unique without renaming (AutoSplit parses semantics out of filenames).
                int existing = -1;
                for (int i = 0; i < Images.Count; i++)
                {
                    if (string.Equals(Images[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = i;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    Images[existing] = item;
                }
                else
                {
                    Images.Add(item);
                }
            }
            catch (InvalidDataException)
            {
                error = $"{name} could not be read as a PNG image.";
            }
        }

        Feedback = error ?? (picked.Count > 0 ? $"{Images.Count} image{(Images.Count == 1 ? "" : "s")} loaded." : Feedback);
        OnPropertyChanged(nameof(HasImages));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private ObsLutApplier? EffectiveApplier => UseGeneratedLut ? _generatedApplier : _customApplier;

    private bool CanApply() => !IsBusy && Images.Count > 0 && EffectiveApplier is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (EffectiveApplier is not { } applier)
        {
            return;
        }

        try
        {
            var stream = await _files.CreateSaveZipAsync("autosplit-fixed.zip", "Save fixed AutoSplit images");
            if (stream is null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await using (stream)
                {
                    // Build the archive in memory first: browser save streams are not seekable,
                    // which ZipArchive needs when writing directly.
                    using var memory = new MemoryStream();
                    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        for (int i = 0; i < Images.Count; i++)
                        {
                            var item = Images[i];
                            Feedback = $"Applying LUT: {i + 1}/{Images.Count}...";
                            var inverter = _oldLutInverter;
                            var bytes = await Task.Run(() =>
                            {
                                // Images taken with an old LUT active are de-corrected first, so
                                // the new LUT lands on the same input OBS will now correct. The
                                // reversal composes with the new LUT in float, quantizing once.
                                var corrected = inverter is null
                                    ? applier.Apply(item.Image)
                                    : inverter.InvertThenApply(applier, item.Image);
                                using var png = new MemoryStream();
                                _codec.EncodePng(corrected, item.Alpha, png);
                                return png.ToArray();
                            });
                            var entry = zip.CreateEntry(item.Name);
                            await using var entryStream = entry.Open();
                            await entryStream.WriteAsync(bytes);
                        }
                    }

                    memory.Position = 0;
                    await memory.CopyToAsync(stream);
                }

                Feedback = $"Saved zip with {Images.Count} fixed image{(Images.Count == 1 ? "" : "s")}.";
            }
            finally
            {
                IsBusy = false;
            }
        }
        catch (Exception ex)
        {
            Feedback = $"Save failed: {ex.Message}";
        }
    }
}
