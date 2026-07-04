namespace AutoLUT.Core.ReferenceData;

public sealed record ReferenceManifest(int Version, IReadOnlyList<ReferenceEntry> References);

public sealed record ReferenceEntry(
    string Id,
    string DisplayName,
    string File,
    int Width,
    int Height,
    IReadOnlyList<RegionDefinition> Regions);
