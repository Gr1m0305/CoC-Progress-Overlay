using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClashOverlay.Api;
using ClashOverlay.App.ViewModels;
using ClashOverlay.App.Views;
using ClashOverlay.App.Windows;
using ClashOverlay.Core.Calc;
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
        await Scenarios(host, vm, frame, dir, shots);
        vm.OnHidden();

        // Setup, one picture per step.
        var setupVm = new SetupViewModel(host, null);
        var setup = Detach(new SetupWindow(setupVm), 900, 640);
        for (var step = 1; step <= 4; step++)
        {
            setupVm.Step = step;
            await Settle();
            shots.Add(Save(setup, dir, $"setup-{step}.png"));
        }

        // Settings, one picture per page.
        var actions = new SettingsActions { Preview = () => { }, OpenSetup = _ => { }, OpenImport = _ => { }, Confirm = (_, _) => false, OpenFolder = _ => { } };
        using var settingsVm = new SettingsViewModel(host, null, actions);
        var settings = Detach(new SettingsWindow(settingsVm), 960, 700);
        foreach (var section in settingsVm.Sections)
        {
            settingsVm.Section = section;
            await Settle();
            shots.Add(Save(settings, dir, $"settings-{section.Key}.png"));
        }

        host.Log.LogInformation("Screenshots written: {Files}", string.Join(", ", shots.Select(Path.GetFileName)));
    }

    /// <summary>
    /// End-to-end checks on the demo data: progress by time, the second account, importing a newer export, and the
    /// API failing with a saved copy (maintenance, IP change). Each writes a picture and a log line.
    /// </summary>
    private static async Task Scenarios(AppHost host, OverlayViewModel vm, Border frame, string dir, List<string> shots)
    {
        var log = host.Log;
        vm.Village = Village.Home;
        vm.SelectedTab = vm.Tabs.FirstOrDefault();

        host.Settings.ProgressMode = ProgressMode.Time;
        host.SaveSettings();
        await vm.LoadAsync(force: false);
        await Settle();
        log.LogInformation("E2E progress by time: tabs {Tabs}", string.Join(", ", vm.Tabs.Select(t => t.Key)));
        shots.Add(Save(frame, dir, "e2e-overview-time.png"));
        host.Settings.ProgressMode = ProgressMode.Levels;
        host.SaveSettings();

        vm.SelectAccount(1);
        await vm.LoadAsync(force: false);
        await Settle();
        log.LogInformation("E2E second account: {Name}, sync \"{Sync}\", warning {Warning}", vm.AccountName, vm.SyncText, vm.SyncWarning);
        shots.Add(Save(frame, dir, "e2e-second-account.png"));
        vm.SelectAccount(0);
        await vm.LoadAsync(force: false);
        await Settle();

        // A newer export of the main village: the store's Changed event should reload the overlay by itself.
        var before = vm.SyncText;
        var json = System.Text.Json.Nodes.JsonNode.Parse(DemoData.Read("demo_th19.json"))!;
        json["timestamp"] = host.Time.GetUtcNow().AddMinutes(-1).ToUnixTimeSeconds();
        host.Store.Import(json.ToJsonString(), DemoData.MainTag);
        for (var i = 0; i < 5 && vm.SyncText == before; i++)
        {
            await Settle();
        }

        log.LogInformation("E2E import: sync before \"{Before}\", after \"{After}\"", before, vm.SyncText);
        shots.Add(Save(frame, dir, "e2e-after-import.png"));

        if (host.Api is DemoApiClient api)
        {
            api.Fault = ClashApiException.FromResponse(HttpStatusCode.ServiceUnavailable,
                """{"reason":"inMaintenance","message":"API is currently in maintenance, please come back later"}""", null, warEndpoint: false);
            await vm.LoadAsync(force: true);
            await Settle();
            log.LogInformation("E2E maintenance: notice \"{Notice}\"", vm.Notice);
            shots.Add(Save(frame, dir, "e2e-maintenance.png"));

            api.Fault = ClashApiException.FromResponse(HttpStatusCode.Forbidden,
                """{"reason":"accessDenied.invalidIp","message":"Invalid authorization: API key does not allow access from IP 198.51.100.7"}""", null, warEndpoint: false);
            await vm.LoadAsync(force: true);
            await Settle();
            log.LogInformation("E2E invalidIp: notice \"{Notice}\"", vm.Notice);
            shots.Add(Save(frame, dir, "e2e-invalid-ip.png"));

            api.Fault = null;
            await vm.LoadAsync(force: true);
            await Settle();
            log.LogInformation("E2E recovered: notice \"{Notice}\"", vm.Notice ?? "(none)");
        }
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

    /// <summary>
    /// A window's content at the window's size, without showing the window (nothing appears on screen).
    /// </summary>
    private static Border Detach(Window window, double width, double height)
    {
        var content = (UIElement)window.Content;
        var resources = window.Resources;
        var dataContext = window.DataContext;
        window.Content = null;
        var app = Application.Current.Resources;
        var root = new Border
        {
            Width = width,
            Height = height,
            Background = (Brush)app["WindowBrush"],
            BorderBrush = (Brush)app["DividerBrush"],
            BorderThickness = new Thickness(1),
            DataContext = dataContext,
            Child = content,
            UseLayoutRounding = true,
        };
        root.Resources.MergedDictionaries.Add(resources);
        TextElement.SetFontFamily(root, (FontFamily)app["Inter"]);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, (Brush)app["TextBrush"]);
        return root;
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
