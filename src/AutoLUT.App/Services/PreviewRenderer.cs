using System.Runtime.InteropServices;
using AutoLUT.Core.Imaging;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AutoLUT.App.Services;

public static class PreviewRenderer
{
    /// <summary>Returns a copy with the right half filled with the commanded target color, for corrected-vs-target comparison.</summary>
    public static RawImage ComposeWithTarget(RawImage image, byte r, byte g, byte b)
    {
        var composed = image.Clone();
        int half = composed.Width / 2;
        for (int y = 0; y < composed.Height; y++)
        {
            var row = composed.Row(y);
            for (int x = half; x < composed.Width; x++)
            {
                row[x * 3] = r;
                row[x * 3 + 1] = g;
                row[x * 3 + 2] = b;
            }
        }

        return composed;
    }

    /// <summary>Converts an RGB24 RawImage to an Avalonia bitmap for display.</summary>
    public static WriteableBitmap ToBitmap(RawImage image)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var buffer = bitmap.Lock();
        var row = new byte[image.Width * 4];
        for (int y = 0; y < image.Height; y++)
        {
            var source = image.Row(y);
            for (int x = 0; x < image.Width; x++)
            {
                row[x * 4] = source[x * 3 + 2];     // B
                row[x * 4 + 1] = source[x * 3 + 1]; // G
                row[x * 4 + 2] = source[x * 3];     // R
                row[x * 4 + 3] = 255;
            }

            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
        }

        return bitmap;
    }
}
