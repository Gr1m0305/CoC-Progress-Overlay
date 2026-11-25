using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClashOverlay.App.ViewModels;
using ClashOverlay.App.Views;
using ClashOverlay.Core.Data;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>
/// <c>--demo --screenshots DIR</c>: renders every overlay tab in both villages (and the other windows) with the demo
/// data to PNG files, then exits. The overlay is drawn over a neutral dark backdrop so its translucency shows.
/// </summary>
internal static class ScreenshotRunner
{
    private const int Pad = 32;

    public static async Task RunAsync(AppHost host, string dir)
    {
        Directory.CreateDirectory(dir);
        var vm = new OverlayViewModel(host);
        vm.OnShown();
        await vm.LoadAsync(force: false);
        await Settle();

        var frame = Frame(new OverlayView { DataContext = vm });
        var shots = new List<string>();
        foreach (var village in new[] { Village.Home, Village.BuilderBase })
        {
            vm.Village = village;
            await Settle();
            foreach (var tab in vm.Tabs.ToList())
            {
                vm.SelectedTab = tab;
                await Settle();
                var prefix = village == Village.Home ? "mv" : "bb";
                shots.Add(Save(frame, dir, $"{prefix}-{tab.Key}.png"));
            }
        }

        vm.Village = Village.Home;
        vm.SelectedTab = vm.Tabs.FirstOrDefault();
        vm.IsAccountMenuOpen = true;
        await Settle();
        shots.Add(Save(frame, dir, "account-switcher.png"));
        vm.IsAccountMenuOpen = false;
        vm.OnHidden();

        host.Log.LogInformation("Screenshots written: {Files}", string.Join(", ", shots.Select(Path.GetFileName)));
    }

    /// <summary>Lets bindings, layout and async loads finish.</summary>
    public static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(30);
        }
    }

    /// <summary>The element on a dark, game-neutral backdrop with some margin.</summary>
    public static Border Frame(FrameworkElement content)
    {
        var bg = new LinearGradientBrush(Color.FromRgb(0x34, 0x45, 0x37), Color.FromRgb(0x1B, 0x28, 0x3B), 35);
        bg.GradientStops.Insert(1, new GradientStop(Color.FromRgb(0x2A, 0x33, 0x40), 0.55));
        return new Border { Background = bg, Padding = new Thickness(Pad), Child = content, UseLayoutRounding = true };
    }

    public static string Save(FrameworkElement frame, string dir, string name)
    {
        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = frame.DesiredSize;
        frame.Arrange(new Rect(size));
        frame.UpdateLayout();

        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(frame);
        var path = Path.Combine(dir, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(path))
        {
            encoder.Save(f);
        }

        return path;
    }
}
