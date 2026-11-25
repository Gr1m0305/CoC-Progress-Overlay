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
        await TabClick(host, vm, frame, dir, shots);
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
        var actions = new SettingsActions { OpenSetup = _ => { }, OpenImport = _ => { }, Confirm = (_, _) => false, OpenFolder = _ => { } };
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
    /// E2E: a press on a tab in the real overlay window selects it, although the overlay never has the keyboard focus.
    /// The window is shown far off-screen and fully transparent, so nothing appears.
    /// </summary>
    private static async Task TabClick(AppHost host, OverlayViewModel vm, Border frame, string dir, List<string> shots)
    {
        var view = new OverlayView { DataContext = vm };
        var window = new Overlay.OverlayWindow(view) { Left = -32000, Top = -32000, Width = 1260, Height = 760 };
        window.Show();
        try
        {
            vm.SelectedTab = vm.Tabs.FirstOrDefault();
            await Settle();
            var tabs = Descendants<ListBoxItem>(view).Where(i => i.DataContext is TabViewModel).ToList();
            var target = tabs.LastOrDefault();
            if (target is null)
            {
                host.Log.LogWarning("E2E tab click: no tabs found");
                return;
            }

            var press = Press(target);
            await Settle();
            var ok = ReferenceEquals(vm.SelectedTab, target.DataContext);
            var list = ItemsControl.ItemsControlFromItemContainer(target) as ListBox;
            host.Log.Log(ok ? LogLevel.Information : LogLevel.Warning, "E2E tab click: pressed {Tab}, selected {Selected} (item selected {ItemSelected}, list has {ListSelected}, handled {Handled}): {Result}",
                ((TabViewModel)target.DataContext).Title, vm.SelectedTab?.Title, target.IsSelected, (list?.SelectedItem as TabViewModel)?.Title, press.Handled, ok ? "ok" : "FAILED");

            await FilterChecks(host, vm, view, frame, dir, shots);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// E2E: a press on a filter chip filters the Rushed list; the sort chips order the builder list; a hero selected in
    /// the builder list keeps the list on screen (its level strip used to push the list away).
    /// </summary>
    private static async Task FilterChecks(AppHost host, OverlayViewModel vm, OverlayView view, Border frame, string dir, List<string> shots)
    {
        var log = host.Log;
        if (vm.Tabs.OfType<RushedTab>().FirstOrDefault() is { } rushed && rushed.Filters.Count > 1)
        {
            vm.SelectedTab = rushed;
            await Settle();
            var chip = Descendants<ListBoxItem>(view).FirstOrDefault(i => i.DataContext is Choice c && c.Key != "all" && rushed.Filters.Contains(c));
            if (chip is not null)
            {
                Press(chip);
                await Settle();
                var picked = (Choice)chip.DataContext;
                var ok = rushed.Filter == picked && rushed.Rows.Count > 0 && rushed.Rows.All(r => r.Category.ToString() == picked.Key);
                log.Log(ok ? LogLevel.Information : LogLevel.Warning, "E2E rushed filter: pressed {Chip}, {Rows} rows: {Result}", picked.Label, rushed.Rows.Count, ok ? "ok" : "FAILED");
                shots.Add(Save(frame, dir, "e2e-rushed-filter.png"));
                rushed.Filter = rushed.Filters[0];
            }
        }

        if (vm.Tabs.OfType<UpgradesTab>().FirstOrDefault(t => t.IsBuilder) is { } builder)
        {
            vm.SelectedTab = builder;
            await Settle();
            if (Descendants<ListBoxItem>(view).FirstOrDefault(i => i.DataContext is Choice { Key: "cheapest" }) is { } cheapest)
            {
                Press(cheapest);
                await Settle();
                builder.ResourceFilter = builder.ResourceFilters.FirstOrDefault(c => c.Key == nameof(Resource.Gold)) ?? builder.ResourceFilter;
                await Settle();
                var golds = builder.Rows.Where(r => !r.IsLocked).Select(r => r.CostTotals[Resource.Gold] + r.CostTotals.GoldOrElixir).ToList();
                var ok = builder.Sort?.Key == "cheapest" && golds.Count > 1 && golds.Zip(golds.Skip(1)).All(p => p.First <= p.Second);
                log.Log(ok ? LogLevel.Information : LogLevel.Warning, "E2E builder sort: {Sort} / {Resource}, {Rows} rows, first {First}, last {Last}: {Result}",
                    builder.Sort?.Label, builder.ResourceFilter?.Label, builder.Rows.Count, golds.FirstOrDefault(), golds.LastOrDefault(), ok ? "ok" : "FAILED");
                shots.Add(Save(frame, dir, "e2e-builder-cheapest-gold.png"));
                builder.Sort = builder.Sorts[0];
                builder.ResourceFilter = builder.ResourceFilters.FirstOrDefault();
            }

            if (builder.Rows.FirstOrDefault(r => r.Item.Kind == ItemKind.Hero) is { } hero)
            {
                builder.SelectedRow = hero;
                await Settle();
                view.UpdateLayout();
                var list = Descendants<ListBox>(view).FirstOrDefault(l => ReferenceEquals(l.ItemsSource, builder.Rows));
                var height = list?.ActualHeight ?? 0;
                var ok = height >= 140 && builder.Strip.Count <= UpgradesTab.StripCells && builder.Strip.Any(c => c.IsOwned);
                log.Log(ok ? LogLevel.Information : LogLevel.Warning, "E2E hero selected: {Hero}, list {Height:0} px high, strip {Cells} levels ({Detail}): {Result}",
                    hero.Name, height, builder.Strip.Count, builder.StripDetail, ok ? "ok" : "FAILED");
                shots.Add(Save(frame, dir, "e2e-hero-selected.png"));
            }
        }

        if (vm.Tabs.OfType<EquipmentTab>().FirstOrDefault() is { } gear)
        {
            vm.SelectedTab = gear;
            gear.Sort = gear.Sorts.First(c => c.Key == "cheapest");
            await Settle();
            var shiny = gear.Flat.Select(g => g.NextTotals[Resource.ShinyOre]).ToList();
            var ok = !gear.IsByHero && shiny.Count > 0 && shiny.Zip(shiny.Skip(1)).All(p => p.First <= p.Second) && gear.Flat.All(g => g.CanUpgrade);
            log.Log(ok ? LogLevel.Information : LogLevel.Warning, "E2E equipment sort: {Sort}, {Count} pieces, shiny {First} to {Last}: {Result}",
                gear.Sort?.Label, gear.Flat.Count, shiny.FirstOrDefault(), shiny.LastOrDefault(), ok ? "ok" : "FAILED");
            shots.Add(Save(frame, dir, "e2e-equipment-cheapest.png"));
            gear.Sort = gear.Sorts[0];
        }

        if (vm.Tabs.OfType<MaxTab>().FirstOrDefault() is { } max)
        {
            vm.SelectedTab = max;
            await Settle();
            var groups = max.Groups.Count;
            max.Filter = max.Filters.FirstOrDefault(c => c.Key == nameof(Category.Heroes)) ?? max.Filter;
            await Settle();
            var ok = groups > 1 && max.Rows.Any() && max.Rows.All(r => r.Category == Category.Heroes);
            log.Log(ok ? LogLevel.Information : LogLevel.Warning, "E2E to max: {Groups} types, heroes filter {Rows} lines: {Result}", groups, max.Rows.Count(), ok ? "ok" : "FAILED");
            shots.Add(Save(frame, dir, "e2e-tomax-heroes.png"));
            max.Filter = max.Filters[0];
        }
    }

    /// <summary>A left-button press and release on <paramref name="target"/>, raised the way real input arrives.</summary>
    private static System.Windows.Input.MouseButtonEventArgs Press(UIElement target)
    {
        var press = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseDownEvent, // WPF raises the left-button events from it
        };
        target.RaiseEvent(press);
        target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseUpEvent,
        });
        return press;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t)
            {
                yield return t;
            }

            foreach (var d in Descendants<T>(child))
            {
                yield return d;
            }
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
