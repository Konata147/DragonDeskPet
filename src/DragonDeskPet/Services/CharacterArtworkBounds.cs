using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragonDeskPet.Services;

public static class CharacterArtworkBounds
{
    public static Rect FindOpaqueNormalizedBounds(BitmapSource bitmap, byte minimumAlpha = 32)
    {
        var source = bitmap.Format is { } format && (format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32)
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        source.CopyPixels(pixels, stride, 0);

        var left = width;
        var top = height;
        var right = 0;
        var bottom = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + x * 4 + 3] < minimumAlpha)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return right <= left || bottom <= top
            ? new Rect(0, 0, 1, 1)
            : new Rect((double)left / width, (double)top / height,
                (double)(right - left) / width, (double)(bottom - top) / height);
    }

    public static Rect FitNormalizedBounds(Rect normalizedBounds, System.Windows.Size elementSize, System.Windows.Size imagePixelSize)
    {
        var fit = Math.Min(elementSize.Width / imagePixelSize.Width, elementSize.Height / imagePixelSize.Height);
        var imageLeft = (elementSize.Width - imagePixelSize.Width * fit) / 2;
        var imageTop = (elementSize.Height - imagePixelSize.Height * fit) / 2;
        return new Rect(
            imageLeft + normalizedBounds.Left * imagePixelSize.Width * fit,
            imageTop + normalizedBounds.Top * imagePixelSize.Height * fit,
            normalizedBounds.Width * imagePixelSize.Width * fit,
            normalizedBounds.Height * imagePixelSize.Height * fit);
    }
}
