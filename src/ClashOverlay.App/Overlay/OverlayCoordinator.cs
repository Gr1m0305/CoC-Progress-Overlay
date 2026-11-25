using System.Windows;
using System.Windows.Threading;
using ClashOverlay.App.Native;
using ClashOverlay.App.Services;
using ClashOverlay.App.ViewModels;
using ClashOverlay.App.Views;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Overlay;

/// <summary>
/// Ties the game detector, the hotkeys and the overlay window together: shows and hides the overlay, keeps it placed,
/// hides it when the game is minimised or closed, and registers hotkeys only while the game is in front or the
/// overlay is showing.
/// </summary>
internal sealed class OverlayCoordinator : IDisposable
{
    private readonly AppHost _host;
    private readonly GameDetector _detector;
    private readonly HotkeyManager _hotkeys;
    private readonly OverlayViewModel _vm;
    private readonly ILogger _log;
    private readonly DispatcherTimer _moveDebounce;
    private OverlayWindow? _window;

    public OverlayCoordinator(AppHost host, GameDetector detector, HotkeyManager hotkeys, OverlayViewModel vm)
    {
        _host = host;
        _detector = detector;
        _hotkeys = hotkeys;
        _vm = vm;
        _log = host.Loggers.CreateLogger("Overlay");
        _moveDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _moveDebounce.Tick += (_, _) =>
        {
            _moveDebounce.Stop();
            if (IsVisible)
            {
                Place();
            }
        };

        _detector.Changed += (_, _) => OnGameChanged();
        _detector.GameMoved += (_, _) =>
        {
            if (IsVisible && _host.Settings.Placement == OverlayPlacement.OverGameWindow)
            {
                _moveDebounce.Start();
            }
        };
        _hotkeys.Pressed += OnHotkey;
        _vm.CloseRequested += (_, _) =>
        {
            _log.LogDebug("Close button");
            Hide();
        };
        _host.SettingsChanged += (_, _) =>
        {
            UpdateHotkeys();
            if (IsVisible)
            {
                Place();
            }
        };
    }

    public bool IsVisible => _window?.IsShown == true;

    /// <summary>The user moved or resized the overlay (the new place and scale are saved in settings).</summary>
    public event EventHandler? Arranged;

    public void Start()
    {
        _detector.Start();
        UpdateHotkeys();
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        if (_window is null)
        {
            _window = new OverlayWindow(new OverlayView { DataContext = _vm });
            _window.Arranged += (_, bounds) => OnArranged(bounds);
        }

        Place();
        _window.FadeIn();
        _vm.OnShown();
        UpdateHotkeys();
        _log.LogDebug("Overlay shown");
    }

    public void Hide(bool instant = false)
    {
        if (_window is null || !_window.IsVisible)
        {
            return;
        }

        _window.FadeOut(instant);
        _vm.OnHidden();
        UpdateHotkeys();
        _log.LogDebug("Overlay hidden");
    }

    private void Place()
    {
        var s = _host.Settings;
        var game = _detector.Game?.Window.Handle ?? IntPtr.Zero;
        var anchor = game != IntPtr.Zero ? game : NativeMethods.GetForegroundWindow();
        var position = s.OverlayLeft is { } left && s.OverlayTop is { } top ? (left, top) : ((int, int)?)null;
        _window!.Place(anchor, _detector.GameBounds, s.Placement, s.Scale, position);
    }

    /// <summary>Keeps where the user dragged the overlay, and the size a corner drag gave it.</summary>
    private void OnArranged(PixelRect bounds)
    {
        var s = _host.Settings;
        s.OverlayLeft = bounds.Left;
        s.OverlayTop = bounds.Top;
        s.Scale = Math.Clamp(_window!.ScaleFor(bounds), AppSettings.MinScale, AppSettings.MaxScale);
        _log.LogDebug("Overlay arranged at {Bounds}, scale {Scale:0.00}", bounds, s.Scale);
        _host.SaveSettings();
        Arranged?.Invoke(this, EventArgs.Empty);
    }

    private void OnGameChanged()
    {
        if (IsVisible && !_host.IsDemo && _host.Settings.HideWhenGameMinimised && (_detector.Game is null || _detector.IsGameMinimised))
        {
            Hide(instant: _detector.Game is null);
        }

        UpdateHotkeys();
    }

    private void UpdateHotkeys()
    {
        var gameInFront = _detector.IsGameForeground || _host.IsDemo;
        var wanted = HotkeyPlan.Wanted(_host.Settings.Hotkeys, gameInFront, IsVisible, _host.Settings.Accounts.Count);
        _hotkeys.Apply(wanted);
    }

    private void OnHotkey(HotkeyAction action)
    {
        _log.LogDebug("Hotkey {Action}", action);
        switch (action)
        {
            case HotkeyAction.Toggle:
                Toggle();
                break;
            case HotkeyAction.Close:
                Hide();
                break;
            case HotkeyAction.SwitchVillage:
                _vm.SwitchVillage();
                break;
            case HotkeyAction.NextAccount:
                _vm.NextAccount();
                break;
            case >= HotkeyAction.Account1 and <= HotkeyAction.Account9:
                _vm.SelectAccount(action - HotkeyAction.Account1);
                break;
        }
    }

    public void Dispose()
    {
        _moveDebounce.Stop();
        _hotkeys.ReleaseAll();
        _window?.Close();
    }
}
