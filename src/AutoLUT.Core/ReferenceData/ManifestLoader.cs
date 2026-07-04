using System.Text.Json;
using AutoLUT.Core.Imaging;

namespace AutoLUT.Core.ReferenceData;

public static class ManifestLoader
{
    /// <summary>Loads the reference dataset embedded in this assembly (Assets/References).</summary>
    public static ReferenceSet LoadEmbedded(IImageCodec codec)
    {
        using var manifestStream = EmbeddedAssets.Open("Assets.References.manifest.json");
        var manifest = ParseManifest(manifestStream);
        return Load(manifest, file => EmbeddedAssets.Open($"Assets.References.{file}"), codec);
    }

    public static ReferenceManifest ParseManifest(Stream json) =>
        JsonSerializer.Deserialize(json, ManifestJsonContext.Default.ReferenceManifest)
            ?? throw new InvalidDataException("Reference manifest is empty.");

    public static ReferenceSet Load(ReferenceManifest manifest, Func<string, Stream> openFile, IImageCodec codec)
    {
        var references = new List<ReferenceImage>(manifest.References.Count);
        foreach (var entry in manifest.References)
        {
            using var stream = openFile(entry.File);
            var image = codec.Decode(stream);
            if (image.Width != entry.Width || image.Height != entry.Height)
                throw new InvalidDataException(
                    $"Reference '{entry.Id}': image is {image.Width}x{image.Height} but manifest declares {entry.Width}x{entry.Height}.");
            references.Add(new ReferenceImage(entry.Id, entry.DisplayName, image, entry.Regions));
        }

        return new ReferenceSet(references);
    }
}
