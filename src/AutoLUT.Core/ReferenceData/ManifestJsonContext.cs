using System.Text.Json.Serialization;

namespace AutoLUT.Core.ReferenceData;

/// <summary>Source-generated JSON context - no reflection, Native AOT safe.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ReferenceManifest))]
public sealed partial class ManifestJsonContext : JsonSerializerContext;
