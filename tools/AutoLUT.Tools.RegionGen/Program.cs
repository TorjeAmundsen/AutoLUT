using System.Text.Json;
using AutoLUT.Core.Imaging;
using AutoLUT.Core.ReferenceData;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: RegionGen <output-manifest.json> <reference1.png> [reference2.png ...]");
    Console.Error.WriteLine("Derives flat sampling regions for each reference PNG and writes a complete manifest.");
    return 1;
}

var codec = new SkiaImageCodec();
var entries = new List<ReferenceEntry>();
foreach (string path in args.Skip(1))
{
    using var stream = File.OpenRead(path);
    var image = codec.Decode(stream);
    var regions = RegionDeriver.Derive(image);
    string id = Path.GetFileNameWithoutExtension(path);
    entries.Add(new ReferenceEntry(id, id, Path.GetFileName(path), image.Width, image.Height, regions));
    Console.WriteLine($"{path}: {image.Width}x{image.Height}, {regions.Count} regions");
}

var manifest = new ReferenceManifest(Version: 1, entries);
using var output = File.Create(args[0]);
JsonSerializer.Serialize(output, manifest, ManifestJsonContext.Default.ReferenceManifest);
Console.WriteLine($"Wrote {args[0]}");
return 0;
