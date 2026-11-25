using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClashOverlay.App.Native;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;
using static ClashOverlay.App.Native.NativeMethods;

namespace ClashOverlay.App.Overlay;

/// <summary>
/// The overlay: borderless, transparent, always on top, not in the taskbar or Alt+Tab, and never activated, so the
/// game keeps the keyboard focus even when the overlay is clicked. It fades in and out over about 150 ms. Dragging an
/// empty part moves it and dragging a corner resizes it (the content scales with it); <see cref="Arranged"/> reports
/// where it ended up.
/// </summary>
internal partial class OverlayWindow : Window
{
    public static readonly Duration FadeTime = new(TimeSpan.FromMilliseconds(150));

    /// <summary>Pixels the pointer must travel before a press on the overlay becomes a move, so clicks stay clicks.</summary>
    private const int DragThreshold = 5;

    private bool _hiding;
    private DragMode _drag;
    private ResizeCorner _corner;
    private POINT _dragStart;
    private PixelRect _dragOrigin;

    public OverlayWindow(FrameworkElement content)
    {
        InitializeComponent();
        Host.Content = content;
        Opacity = 0;
        GripTopLeft.Tag = ResizeCorner.TopLeft;
        GripTopRight.Tag = ResizeCorner.TopRight;
        GripBottomLeft.Tag = ResizeCorner.BottomLeft;
        GripBottomRight.Tag = ResizeCorner.BottomRight;
        PreviewMouseLeftButtonDown += OnPressed;
        PreviewMouseMove += OnPointerMoved;
        PreviewMouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => EndDrag();
    }

    private enum DragMode
    {
        None,
        Pending,
        Move,
        Resize,
    }

    public IntPtr Handle { get; private set; }

    public bool IsShown => IsVisible && !_hiding;

    /// <summary>The user finished moving or resizing the overlay; the new bounds in physical pixels.</summary>
    public event EventHandler<PixelRect>? Arranged;

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

    /// <summary>
    /// Sizes and places the window on the monitor of <paramref name="anchor"/> (the game window, or any window), or at
    /// <paramref name="position"/> (top-left, physical pixels) when the user dragged it there.
    /// </summary>
    public void Place(IntPtr anchor, PixelRect? game, OverlayPlacement placement, double scale, (int Left, int Top)? position = null)
    {
        if (_drag is DragMode.Move or DragMode.Resize)
        {
            return; // the user is arranging it right now
        }

        var monitor = position is { } p
            ? MonitorFromPoint(new POINT { X = p.Left + 40, Y = p.Top + 40 }, MONITOR_DEFAULTTONEAREST)
            : NativeWindows.MonitorOf(anchor);
        var dpi = NativeWindows.DpiScale(monitor);
        var work = NativeWindows.WorkArea(monitor);
        var wanted = ((int)Math.Round(OverlayGeometry.BaseWidth * scale * dpi), (int)Math.Round(OverlayGeometry.BaseHeight * scale * dpi));
        var (rect, fit) = position is { } at
            ? OverlayGeometry.PlaceAt(work, at.Left, at.Top, wanted.Item1, wanted.Item2)
            : OverlayGeometry.Place(work, game, placement, wanted.Item1, wanted.Item2);

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

    /// <summary>The overlay's scale for a width in pixels on the monitor it is on.</summary>
    public double ScaleFor(PixelRect bounds)
    {
        var dpi = NativeWindows.DpiScale(NativeWindows.MonitorOf(Handle));
        return bounds.Width / (OverlayGeometry.BaseWidth * dpi);
    }

    // ----- moving and resizing -----

    private void OnPressed(object sender, MouseButtonEventArgs e)
    {
        if (Handle == IntPtr.Zero || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        GetCursorPos(out _dragStart);
        _dragOrigin = NativeWindows.Bounds(Handle);
        if (Find<Border>(source, b => b.Tag is ResizeCorner) is { Tag: ResizeCorner corner })
        {
            _corner = corner;
            _drag = DragMode.Resize;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // The overlay never has the keyboard focus, and a list item only selects itself when it can take the focus,
        // so tabs and rows are selected here, on the press.
        if (Find<ListBoxItem>(source, _ => true) is { IsSelected: false } item
            && ItemsControl.ItemsControlFromItemContainer(item) is ListBox { SelectionMode: SelectionMode.Single })
        {
            item.IsSelected = true;
        }

        _drag = IsControl(source) ? DragMode.None : DragMode.Pending;
    }

    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        if (_drag == DragMode.None)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        GetCursorPos(out var now);
        var dx = now.X - _dragStart.X;
        var dy = now.Y - _dragStart.Y;
        switch (_drag)
        {
            case DragMode.Pending:
                if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
                {
                    return;
                }

                _drag = DragMode.Move;
                CaptureMouse();
                NativeWindows.Move(Handle, _dragOrigin.Left + dx, _dragOrigin.Top + dy);
                break;
            case DragMode.Move:
                NativeWindows.Move(Handle, _dragOrigin.Left + dx, _dragOrigin.Top + dy);
                break;
            case DragMode.Resize:
                var monitor = NativeWindows.MonitorOf(Handle);
                var dpi = NativeWindows.DpiScale(monitor);
                var work = NativeWindows.WorkArea(monitor);
                var min = (int)Math.Round(OverlayGeometry.BaseWidth * AppSettings.MinScale * dpi);
                var max = (int)Math.Min(OverlayGeometry.BaseWidth * AppSettings.MaxScale * dpi,
                    Math.Min(work.Width, work.Height * OverlayGeometry.BaseWidth / OverlayGeometry.BaseHeight));
                NativeWindows.MoveResize(Handle, OverlayGeometry.Resize(_dragOrigin, _corner, dx, dy, min, max));
                break;
        }
    }

    private void EndDrag()
    {
        var was = _drag;
        _drag = DragMode.None;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (was is DragMode.Move or DragMode.Resize)
        {
            Arranged?.Invoke(this, NativeWindows.Bounds(Handle));
        }
    }

    /// <summary>Buttons, scroll bars, links and other controls keep their clicks; a press on one never moves the overlay.</summary>
    private static bool IsControl(DependencyObject source) =>
        Find<DependencyObject>(source, d => d is ButtonBase or Thumb or ScrollBar or Slider or TextBoxBase or ComboBox or Hyperlink) is not null;

    private static T? Find<T>(DependencyObject? d, Func<T, bool> match)
        where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t && match(t))
            {
                return t;
            }

            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return null;
    }

    // ----- showing and hiding -----

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

        EndDrag();
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
