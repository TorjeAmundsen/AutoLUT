using System.Text.Json;

namespace AutoLUT.App.Services.Update;

/// <summary>
/// Persisted updater preferences. Stored outside the install directory (which the update
/// swap replaces wholesale and may be read-only): %AppData%\AutoLUT\settings.json on
/// Windows, ~/.config/AutoLUT/settings.json on Linux.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>"Don't ask again" - suppresses the automatic on-launch check entirely.</summary>
    public bool AutoCheckDisabled { get; set; }

    /// <summary>"Skip this version" - the release tag the user chose not to be prompted for again.</summary>
    public string? SkippedVersion { get; set; }

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoLUT",
            "settings.json");

    public static UpdateSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new UpdateSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, UpdateJsonContext.Default.UpdateSettings)
                   ?? new UpdateSettings();
        }
        catch
        {
            // Missing or corrupt settings must never block launch - fall back to defaults.
            return new UpdateSettings();
        }
    }

    public void Save()
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonSerializer.Serialize(this, UpdateJsonContext.Default.UpdateSettings);

            // Write-and-rename so a crash mid-write can't leave a truncated file.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Best-effort; a failure to persist a preference is not worth surfacing.
        }
    }
}
