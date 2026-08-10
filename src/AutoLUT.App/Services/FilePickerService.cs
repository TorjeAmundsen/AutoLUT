using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AutoLUT.App.Services;

public sealed class FilePickerService : IFilePickerService
{
    private readonly Func<TopLevel?> _topLevel;

    /// <summary>
    /// Resolves the TopLevel lazily: on desktop it is the main window; in the browser the
    /// single view is only attached to its TopLevel after layout, so it cannot be captured
    /// at composition time.
    /// </summary>
    public FilePickerService(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    private IStorageProvider StorageProvider =>
        (_topLevel() ?? throw new InvalidOperationException("View is not attached yet.")).StorageProvider;

    public Task<IReadOnlyList<(string Name, byte[] Data)>> PickPngScreenshotsAsync() =>
        PickPngImagesAsync("Select calibration screenshots");

    public async Task<IReadOnlyList<(string Name, byte[] Data)>> PickPngImagesAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
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

    public async Task<(string Name, byte[] Data)?> PickSinglePngAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImagePng],
        });

        if (files.Count == 0)
        {
            return null;
        }

        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return (files[0].Name, memory.ToArray());
    }

    public async Task<Stream?> CreateSaveFileAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save OBS LUT",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
        });

        return file is null ? null : await file.OpenWriteAsync();
    }

    public async Task<Stream?> CreateSaveZipAsync(string suggestedName, string dialogTitle)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = dialogTitle,
            SuggestedFileName = suggestedName,
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"], MimeTypes = ["application/zip"] }],
        });

        return file is null ? null : await file.OpenWriteAsync();
    }
}
