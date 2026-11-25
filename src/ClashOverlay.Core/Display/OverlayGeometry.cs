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

/// <summary>The corner of the overlay being dragged to resize it.</summary>
public enum ResizeCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
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

    /// <summary>
    /// The overlay where the user dragged it: top-left at (<paramref name="left"/>, <paramref name="top"/>), shrunk
    /// to fit the work area if needed, and moved back inside it if part of it is off the screen.
    /// </summary>
    public static (PixelRect Rect, double Fit) PlaceAt(PixelRect workArea, int left, int top, int width, int height)
    {
        var fit = Math.Min(1.0, Math.Min(workArea.Width / (double)Math.Max(1, width), workArea.Height / (double)Math.Max(1, height)));
        fit = Math.Max(0.1, fit);
        var w = (int)Math.Floor(width * fit);
        var h = (int)Math.Floor(height * fit);
        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - w));
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - h));
        return (new PixelRect(left, top, w, h), fit);
    }

    /// <summary>
    /// The rectangle while a corner is dragged by (<paramref name="dx"/>, <paramref name="dy"/>) pixels from
    /// <paramref name="start"/>. The overlay keeps its shape (the content scales, it does not reflow), the opposite
    /// corner stays put, and the width stays between <paramref name="minWidth"/> and <paramref name="maxWidth"/>.
    /// </summary>
    public static PixelRect Resize(PixelRect start, ResizeCorner corner, int dx, int dy, int minWidth, int maxWidth)
    {
        var left = corner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft;
        var top = corner is ResizeCorner.TopLeft or ResizeCorner.TopRight;
        var fromX = start.Width + (left ? -dx : dx);
        var fromY = (start.Height + (top ? -dy : dy)) * BaseWidth / BaseHeight;

        // The drag projected on the diagonal: pulling along either edge, or both, resizes smoothly.
        var width = (int)Math.Round((fromX + fromY) / 2);
        width = Math.Clamp(width, Math.Max(1, minWidth), Math.Max(minWidth, maxWidth));
        var height = (int)Math.Round(width * BaseHeight / BaseWidth);
        var x = left ? start.Right - width : start.Left;
        var y = top ? start.Bottom - height : start.Top;
        return new PixelRect(x, y, width, height);
    }
}
