using ClashOverlay.Core.Settings;

namespace ClashOverlay.Core.Display;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);
}

public static class OverlayGeometry
{
    /// <summary>The overlay's size at 100% scale, in device-independent pixels.</summary>
    public const double BaseWidth = 1260;

    public const double BaseHeight = 760;

    /// <summary>Space kept free around the overlay when it is shrunk to fit.</summary>
    public const int Margin = 16;

    /// <summary>
    /// Where the overlay goes and how much it must shrink to fit the work area (1 when it fits).
    /// <paramref name="width"/> and <paramref name="height"/> are the wanted size in pixels on that monitor.
    /// </summary>
    public static (PixelRect Rect, double Fit) Place(PixelRect workArea, PixelRect? game, OverlayPlacement placement, int width, int height)
    {
        var fit = Math.Min(1.0, Math.Min(
            (workArea.Width - 2.0 * Margin) / Math.Max(1, width),
            (workArea.Height - 2.0 * Margin) / Math.Max(1, height)));
        fit = Math.Max(0.1, fit);
        var w = (int)Math.Floor(width * fit);
        var h = (int)Math.Floor(height * fit);

        double cx, cy;
        if (placement == OverlayPlacement.OverGameWindow && game is { IsEmpty: false } g)
        {
            cx = g.Left + g.Width / 2.0;
            cy = g.Top + g.Height / 2.0;
        }
        else
        {
            cx = workArea.Left + workArea.Width / 2.0;
            cy = workArea.Top + workArea.Height / 2.0;
        }

        var left = (int)Math.Round(cx - w / 2.0);
        var top = (int)Math.Round(cy - h / 2.0);
        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - w));
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - h));
        return (new PixelRect(left, top, w, h), fit);
    }
}
