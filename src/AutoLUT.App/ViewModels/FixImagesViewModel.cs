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
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCustomLutCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadOldLutCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearOldLutCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectImagesCommand))]
    private bool _isBusy;

    private bool NotBusy() => !IsBusy;

    public FixImagesViewModel(IImageCodec codec, IFilePickerService files)
    {
        _codec = codec;
        _files = files;
    }

    public void Open(ObsLutApplier? generatedApplier)
    {
        _generatedApplier = generatedApplier;
        HasGeneratedLut = generatedApplier is not null;
        // Keep the user's LUT-source choice across reopens; only move the selection when the
        // chosen source is not available.
        if (UseGeneratedLut && !HasGeneratedLut)
        {
            UseGeneratedLut = false;
        }
        else if (!UseGeneratedLut && _customApplier is null && HasGeneratedLut)
        {
            UseGeneratedLut = true;
        }

        Feedback = null;
        IsOpen = true;
        ApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Clears everything for the main Reset button; Close keeps state for reopening.</summary>
    public void Reset()
    {
        Images.Clear();
        _generatedApplier = null;
        _customApplier = null;
        _oldLutInverter = null;
        HasGeneratedLut = false;
        UseGeneratedLut = false;
        CustomLutName = null;
        OldLutName = null;
        Feedback = null;
        IsOpen = false;
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(HasOldLut));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void Close() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task LoadCustomLutAsync()
    {
        if (await PickLutAsync("Select OBS LUT PNG") is not { } lut)
        {
            return;
        }

        _customApplier = lut.Applier;
        CustomLutName = lut.Name;
        UseGeneratedLut = false;
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task LoadOldLutAsync()
    {
        if (await PickLutAsync("Select the LUT that was active when the images were taken") is not { } lut)
        {
            return;
        }

        _oldLutInverter = new ObsLutInverter(lut.Applier);
        OldLutName = lut.Name;
        OnPropertyChanged(nameof(HasOldLut));
    }

    private async Task<(string Name, ObsLutApplier Applier)?> PickLutAsync(string title)
    {
        (string Name, byte[] Data)? picked;
        try
        {
            picked = await _files.PickSinglePngAsync(title);
        }
        catch (Exception ex)
        {
            Feedback = $"Could not read the selected file: {ex.Message}";
            return null;
        }

        if (picked is not { } file)
        {
            return null;
        }

        try
        {
            var applier = await Task.Run(() =>
            {
                using var stream = new MemoryStream(file.Data);
                return new ObsLutApplier(_codec.Decode(stream));
            });
            Feedback = null;
            return (file.Name, applier);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Feedback = $"{file.Name} is not a valid OBS LUT: expected a 512x512 PNG.";
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void ClearOldLut()
    {
        _oldLutInverter = null;
        OldLutName = null;
        OnPropertyChanged(nameof(HasOldLut));
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
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

        int failedCount = 0;
        string? failedName = null;
        // Blocks Apply from zipping a half-loaded selection.
        IsBusy = true;
        try
        {
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
                    var existing = Images.FirstOrDefault(loaded => string.Equals(loaded.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        Images.Add(item);
                    }
                    else
                    {
                        Images[Images.IndexOf(existing)] = item;
                    }
                }
                catch (InvalidDataException)
                {
                    failedCount++;
                    failedName = name;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        Feedback = failedCount switch
        {
            0 when picked.Count > 0 => $"{Images.Count} image{(Images.Count == 1 ? "" : "s")} loaded.",
            0 => Feedback,
            1 => $"{failedName} could not be read as a PNG image.",
            _ => $"{failedCount} files could not be read as PNG images.",
        };
        OnPropertyChanged(nameof(HasImages));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private const int InverseChunkPixels = 16384;

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
                // Snapshot before the loop: the awaits below yield to the UI thread, and the
                // whole batch must use one consistent configuration.
                var inverter = _oldLutInverter;
                var items = Images.ToArray();
                await using (stream)
                {
                    // Build the archive in memory first: browser save streams are not seekable,
                    // which ZipArchive needs when writing directly.
                    using var memory = new MemoryStream();
                    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        for (int i = 0; i < items.Length; i++)
                        {
                            var item = items[i];
                            string progress = $"Applying LUT: {i + 1}/{items.Length}";
                            Feedback = progress + "...";
                            RawImage corrected;
                            if (inverter is null)
                            {
                                corrected = await Task.Run(() => applier.Apply(item.Image));
                            }
                            else
                            {
                                // Images taken with an old LUT active are de-corrected first, so
                                // the new LUT lands on the same input OBS will now correct. The
                                // reversal composes with the new LUT in float, quantizing once.
                                var output = new RawImage(item.Image.Width, item.Image.Height);
                                int height = item.Image.Height;
                                int rowsPerChunk = Math.Max(1, InverseChunkPixels / item.Image.Width);
                                for (int firstRow = 0; firstRow < height; firstRow += rowsPerChunk)
                                {
                                    int start = firstRow, rowCount = Math.Min(rowsPerChunk, height - firstRow);
                                    await Task.Run(() => inverter.InvertThenApplyRows(applier, item.Image, output, start, rowCount));
                                    Feedback = $"{progress} ({100 * (start + rowCount) / height}%)...";
                                }

                                corrected = output;
                            }

                            var bytes = await Task.Run(() =>
                            {
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

                Feedback = $"Saved zip with {items.Length} fixed image{(items.Length == 1 ? "" : "s")}.";
            }
            catch (Exception ex)
            {
                Feedback = $"Save failed and left an incomplete zip file. {ex.Message}";
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
