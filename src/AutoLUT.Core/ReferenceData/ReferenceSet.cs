namespace AutoLUT.Core.ReferenceData;

public sealed class ReferenceSet
{
    public IReadOnlyList<ReferenceImage> References { get; }

    public ReferenceSet(IReadOnlyList<ReferenceImage> references)
    {
        if (references.Count == 0)
            throw new ArgumentException("Reference set cannot be empty.", nameof(references));
        References = references;
    }

    public IEnumerable<ReferenceImage> WithDimensions(int width, int height) =>
        References.Where(r => r.Image.Width == width && r.Image.Height == height);
}
