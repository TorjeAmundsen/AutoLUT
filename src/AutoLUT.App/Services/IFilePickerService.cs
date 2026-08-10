namespace AutoLUT.App.Services;

public interface IFilePickerService
{
    /// <summary>Lets the user pick PNG screenshots; returns name + raw bytes per file.</summary>
    Task<IReadOnlyList<(string Name, byte[] Data)>> PickPngScreenshotsAsync();

    /// <summary>Lets the user pick multiple PNG images; returns name + raw bytes per file.</summary>
    Task<IReadOnlyList<(string Name, byte[] Data)>> PickPngImagesAsync(string title);

    /// <summary>Lets the user pick a single PNG file; returns null if cancelled.</summary>
    Task<(string Name, byte[] Data)?> PickSinglePngAsync(string title);

    /// <summary>Opens a save dialog; returns a writable stream or null if cancelled.</summary>
    Task<Stream?> CreateSaveFileAsync(string suggestedName);

    /// <summary>Opens a save dialog for a zip archive; returns a writable stream or null if cancelled.</summary>
    Task<Stream?> CreateSaveZipAsync(string suggestedName, string dialogTitle);
}
