using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class AnimationFrameCleaner
{
    // A sprite-sheet cell may contain a clipped fragment from the neighboring cell.
    // Keep the connected character silhouette and discard detached specks/icons.
    public static void KeepMainFigure(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var area = width * height;
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            var labels = new int[area];
            var queue = new int[area];
            var sizes = new int[area + 1];
            var component = 0;
            var largest = 0;
            for (var i = 0; i < area; i++)
            {
                if (labels[i] != 0 || pixels[(i / width * data.Stride) + (i % width * 4) + 3] < 16)
                    continue;
                component++;
                var head = 0;
                var tail = 0;
                queue[tail++] = i;
                labels[i] = component;
                while (head < tail)
                {
                    var current = queue[head++];
                    var x = current % width;
                    var y = current / width;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        var next = ny * width + nx;
                        if (labels[next] != 0 || pixels[ny * data.Stride + nx * 4 + 3] < 16) continue;
                        labels[next] = component;
                        queue[tail++] = next;
                    }
                }
                sizes[component] = tail;
                if (tail > sizes[largest]) largest = component;
            }
            if (largest == 0 || sizes[largest] < 20000)
                throw new InvalidOperationException("No complete character silhouette in animation cell.");
            for (var i = 0; i < area; i++)
                if (labels[i] != largest)
                    pixels[(i / width * data.Stride) + (i % width * 4) + 3] = 0;
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
