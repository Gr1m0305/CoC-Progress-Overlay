using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClashOverlay.App.Native;

namespace ClashOverlay.App.Windows;

/// <summary>
/// A normal, focusable window in the app's dark style (setup, settings, import, about), with a dark title bar.
/// </summary>
internal class AppWindow : Window
{
    public AppWindow()
    {
        var res = Application.Current.Resources;
        Background = (Brush)res["WindowBrush"];
        Foreground = (Brush)res["TextBrush"];
        FontFamily = (FontFamily)res["Inter"];
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        }
        catch (Exception e) when (e is IOException or NotSupportedException)
        {
        }

        SourceInitialized += (_, _) =>
        {
            var on = 1;
            _ = NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        };
    }

    /// <summary>Shows the window, or brings it back to the front when it is already open.</summary>
    public void ShowOrActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }
}
