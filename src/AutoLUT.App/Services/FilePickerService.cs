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

    public async Task<IReadOnlyList<(string Name, byte[] Data)>> PickPngImagesAsync(string title)
    {
        var files = await OpenPngPickerAsync(title, allowMultiple: true);
        var result = new List<(string, byte[])>(files.Count);
        foreach (var file in files)
        {
            result.Add(await ReadFileAsync(file));
        }

        return result;
    }

    public async Task<(string Name, byte[] Data)?> PickSinglePngAsync(string title)
    {
        var files = await OpenPngPickerAsync(title, allowMultiple: false);
        return files.Count == 0 ? null : await ReadFileAsync(files[0]);
    }

    private async Task<IReadOnlyList<IStorageFile>> OpenPngPickerAsync(string title, bool allowMultiple) =>
        await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = [FilePickerFileTypes.ImagePng],
        });

    private static async Task<(string Name, byte[] Data)> ReadFileAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return (file.Name, memory.ToArray());
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
