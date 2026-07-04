namespace AutoLUT.App.Services;

public interface IFilePickerService
{
    /// <summary>Lets the user pick PNG screenshots; returns name + raw bytes per file.</summary>
    Task<IReadOnlyList<(string Name, byte[] Data)>> PickPngScreenshotsAsync();

    /// <summary>Opens a save dialog; returns a writable stream or null if cancelled.</summary>
    Task<Stream?> CreateSaveFileAsync(string suggestedName);
}
