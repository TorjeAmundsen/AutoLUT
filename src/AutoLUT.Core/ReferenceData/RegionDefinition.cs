namespace AutoLUT.Core.ReferenceData;

/// <summary>A flat sampling region within a reference image. ExpectedVariance is the max-channel stddev in [0,1] units.</summary>
public sealed record RegionDefinition(int X, int Y, int Width, int Height, double Weight = 1.0, double ExpectedVariance = 0.01);
