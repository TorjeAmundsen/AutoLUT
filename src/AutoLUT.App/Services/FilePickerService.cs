using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AutoLUT.App.Services;

public sealed class FilePickerService : IFilePickerService
{
    private readonly Window _window;

    public FilePickerService(Window window) => _window = window;

    public async Task<IReadOnlyList<(string Name, byte[] Data)>> PickPngScreenshotsAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select calibration screenshots",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImagePng],
        });

        var result = new List<(string, byte[])>(files.Count);
        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            result.Add((file.Name, memory.ToArray()));
        }

        return result;
    }

    public async Task<Stream?> CreateSaveFileAsync(string suggestedName)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save OBS LUT",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
        });

        return file is null ? null : await file.OpenWriteAsync();
    }
}
