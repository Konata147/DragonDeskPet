using System.Drawing;

namespace DragonDeskPet.Core;

public static class PetPlacement
{
    // characterOffset is the transformed pet bounds relative to the native window.
    // The window may extend beyond a monitor; at least the requested share of
    // the visible character must remain on the chosen screen.
    public static Point ClampWindowTopLeft(
        Point proposed, Rectangle characterOffset, Rectangle screenBounds, int margin,
        double minimumVisibleFraction = 1)
    {
        if (!double.IsFinite(minimumVisibleFraction) || minimumVisibleFraction is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumVisibleFraction));
        }

        margin = Math.Max(0, margin);
        var minimumX = (int)Math.Ceiling(screenBounds.Left + margin - characterOffset.Right
            + characterOffset.Width * minimumVisibleFraction);
        var maximumX = (int)Math.Floor(screenBounds.Right - margin - characterOffset.Left
            - characterOffset.Width * minimumVisibleFraction);
        var minimumY = (int)Math.Ceiling(screenBounds.Top + margin - characterOffset.Bottom
            + characterOffset.Height * minimumVisibleFraction);
        var maximumY = (int)Math.Floor(screenBounds.Bottom - margin - characterOffset.Top
            - characterOffset.Height * minimumVisibleFraction);

        return new Point(
            ClampAxis(proposed.X, minimumX, maximumX),
            ClampAxis(proposed.Y, minimumY, maximumY));
    }

    private static int ClampAxis(int proposed, int minimum, int maximum) => minimum <= maximum
        ? Math.Clamp(proposed, minimum, maximum)
        : minimum + (maximum - minimum) / 2;
}
