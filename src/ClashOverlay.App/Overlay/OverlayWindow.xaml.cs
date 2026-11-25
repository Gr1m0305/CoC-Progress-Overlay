using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using ClashOverlay.App.Native;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;
using static ClashOverlay.App.Native.NativeMethods;

namespace ClashOverlay.App.Overlay;

/// <summary>
/// The overlay: borderless, transparent, always on top, not in the taskbar or Alt+Tab, and never activated, so the
/// game keeps the keyboard focus even when the overlay is clicked. It fades in and out over about 150 ms.
/// </summary>
internal partial class OverlayWindow : Window
{
    public static readonly Duration FadeTime = new(TimeSpan.FromMilliseconds(150));

    private bool _hiding;

    public OverlayWindow(FrameworkElement content)
    {
        InitializeComponent();
        Host.Content = content;
        Opacity = 0;
    }

    public IntPtr Handle { get; private set; }

    public bool IsShown => IsVisible && !_hiding;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        NativeWindows.MakeNoActivate(Handle);
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    /// <summary>Sizes and places the window on the monitor of <paramref name="anchor"/> (the game window, or any window).</summary>
    public void Place(IntPtr anchor, PixelRect? game, OverlayPlacement placement, double scale)
    {
        var monitor = NativeWindows.MonitorOf(anchor);
        var dpi = NativeWindows.DpiScale(monitor);
        var work = NativeWindows.WorkArea(monitor);
        var wanted = ((int)Math.Round(OverlayGeometry.BaseWidth * scale * dpi), (int)Math.Round(OverlayGeometry.BaseHeight * scale * dpi));
        var (rect, fit) = OverlayGeometry.Place(work, game, placement, wanted.Item1, wanted.Item2);

        if (Handle == IntPtr.Zero)
        {
            // First show: create the window off-screen at the right size, then move it.
            Left = -32000;
            Top = -32000;
            Width = OverlayGeometry.BaseWidth * scale * fit;
            Height = OverlayGeometry.BaseHeight * scale * fit;
            Show();
        }

        // Physical pixels, so mixed-DPI setups land exactly; WPF follows the DPI of the monitor it ends up on.
        NativeWindows.MoveResize(Handle, rect);
        var actual = NativeWindows.Bounds(Handle);
        if (actual.Width != rect.Width || actual.Height != rect.Height || actual.Left != rect.Left || actual.Top != rect.Top)
        {
            NativeWindows.MoveResize(Handle, rect); // second pass after a DPI change between monitors
        }
    }

    public void FadeIn()
    {
        _hiding = false;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, FadeTime) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    public void FadeOut(bool instant = false)
    {
        if (!IsVisible)
        {
            return;
        }

        _hiding = true;
        if (instant)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Hide();
            return;
        }

        var fade = new DoubleAnimation(0, FadeTime) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (_hiding)
            {
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
