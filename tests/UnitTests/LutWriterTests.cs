using AutoLUT.Core.ColorScience;
using AutoLUT.Core.Fitting;
using AutoLUT.Core.Lut;

namespace UnitTests;

public class LutWriterTests
{
    [Test]
    public void Template_HasExpectedGeometryAndIdentityValues()
    {
        // Arrange + Act
        var template = TestImages.LoadTemplate();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(template.Width, Is.EqualTo(512));
            Assert.That(template.Height, Is.EqualTo(512));

            // Corners of the identity mapping round(i * 255 / 63).
            Assert.That(template.GetPixel(0, 0), Is.EqualTo(((byte)0, (byte)0, (byte)0)));
            Assert.That(template.GetPixel(63, 0), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
            Assert.That(template.GetPixel(0, 63), Is.EqualTo(((byte)0, (byte)255, (byte)0)));
            Assert.That(template.GetPixel(448, 448), Is.EqualTo(((byte)0, (byte)0, (byte)255))); // blue slice 63 tile
            Assert.That(template.GetPixel(511, 511), Is.EqualTo(((byte)255, (byte)255, (byte)255)));

            // Ramp values prove the mapping is round(i * 255 / 63), not i * 4.
            Assert.That(template.GetPixel(1, 0), Is.EqualTo(((byte)4, (byte)0, (byte)0)));
            Assert.That(template.GetPixel(32, 0), Is.EqualTo(((byte)130, (byte)0, (byte)0)));
            Assert.That(template.GetPixel(62, 0), Is.EqualTo(((byte)251, (byte)0, (byte)0)));
        }
    }

    [Test]
    public void IdentityTransform_BakesPixelIdenticalToTemplate()
    {
        // Arrange
        var template = TestImages.LoadTemplate();
        var lut = new TransformLutGenerator().Generate(IdentityTransform.Instance);

        // Act
        var baked = new ObsLutWriter().Bake(lut, template);

        // Assert
        Assert.That(baked.Pixels, Is.EqualTo(template.Pixels));
    }

    [Test]
    public void Bake_WritesTransformValuesAtCorrectPixels()
    {
        // Arrange: a transform that swaps red and blue, so lattice (r,g,b) must land at
        // pixel ((b%8)*64+r, (b/8)*64+g) holding (blueRamp, greenRamp, redRamp).
        var template = TestImages.LoadTemplate();
        var lut = new TransformLutGenerator().Generate(new SwapRedBlueTransform());

        // Act
        var baked = new ObsLutWriter().Bake(lut, template);

        byte ramp10 = (byte)MathF.Round(10 * 255f / 63f);
        byte ramp20 = (byte)MathF.Round(20 * 255f / 63f);
        byte ramp30 = (byte)MathF.Round(30 * 255f / 63f);
        using (Assert.EnterMultipleScope())
        {
            // Lattice point (r=63, g=0, b=0) -> swapped -> (0, 0, 255) at pixel (63, 0).
            Assert.That(baked.GetPixel(63, 0), Is.EqualTo(((byte)0, (byte)0, (byte)255)));
            // Lattice point (r=0, g=0, b=63) -> swapped -> (255, 0, 0) at tile 63 origin (448, 448).
            Assert.That(baked.GetPixel(448, 448), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
            // Lattice point (r=10, g=20, b=30): tile (30%8, 30/8) origin (384, 192) -> pixel (394, 212).
            Assert.That(baked.GetPixel(384 + 10, 192 + 20), Is.EqualTo((ramp30, ramp20, ramp10)));
        }
    }

    [Test]
    public void Bake_RejectsWrongTemplateSize()
    {
        // Arrange
        var lut = new TransformLutGenerator().Generate(IdentityTransform.Instance);

        // Act + Assert
        Assert.Throws<ArgumentException>(() => new ObsLutWriter().Bake(lut, TestImages.Random(64, 64, 1)));
    }

    private sealed class SwapRedBlueTransform : IColorTransform
    {
        public Rgb Apply(Rgb srgb) => new(srgb.B, srgb.G, srgb.R);
    }
}
