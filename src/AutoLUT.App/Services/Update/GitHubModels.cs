using System.Text.Json.Serialization;

namespace AutoLUT.App.Services.Update;

/// <summary>Minimal shape of a GitHub release from the /releases/latest API.</summary>
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl);

// Source-generated (JsonSerializerContext) so serialization stays reflection-free and
// Native-AOT/trim safe under TreatWarningsAsErrors. One context covers the GitHub
// payload and the local settings file.
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(UpdateSettings))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
