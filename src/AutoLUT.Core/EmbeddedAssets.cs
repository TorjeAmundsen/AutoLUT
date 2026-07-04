namespace AutoLUT.Core;

public static class EmbeddedAssets
{
    /// <summary>Opens OBS's pristine original.png LUT template (embedded resource).</summary>
    public static Stream OpenOriginalLutTemplate() => Open("Assets.original.png");

    internal static Stream Open(string relativeName)
    {
        string fullName = $"AutoLUT.Core.{relativeName}";
        return typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded resource '{fullName}' not found.");
    }
}
