using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClashOverlay.Core.Settings;
using Microsoft.Extensions.Logging;
using static ClashOverlay.App.Native.NativeMethods;

namespace ClashOverlay.App.Overlay;

/// <summary>
/// The app's shortcuts. <see cref="Apply"/> is called with the set that should be active now (see
/// <see cref="HotkeyPlan"/>); keys that are no longer wanted are released at once, so the game gets them back.
/// Two things watch for them together. A <see cref="KeyboardHook"/> sees the keys first, which matters because games
/// and emulators such as Google Play Games keep <c>RegisterHotKey</c> shortcuts from firing while they are focused.
/// The keys are also registered with <c>RegisterHotKey</c> on a message-only window, which still works when the game
/// runs as administrator (Windows does not show a normal app's hook the keys typed into an elevated window). A key
/// the hook handled never reaches the registered hotkey; a short guard drops a second report of the same press anyway.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _window;
    private readonly ILogger _log;
    private readonly KeyboardHook? _hook;
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _active = new();
    private readonly HashSet<HotkeyGesture> _failedLogged = [];
    private readonly Dictionary<HotkeyAction, long> _lastPressed = new();
    private bool _useHook;

    /// <summary>The same shortcut reported again within this time is one press seen twice.</summary>
    private const long RepeatGuardMs = 150;

    public HotkeyManager(ILogger logger, bool useHook = true)
    {
        _log = logger;
        var p = new HwndSourceParameters("ClashOverlay.Hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);
        if (useHook)
        {
            var dispatcher = _window.Dispatcher;
            _hook = new KeyboardHook(action => dispatcher.BeginInvoke(() => Raise(action)), logger);
            _useHook = true;
        }
    }

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Gestures another program already registered (the keyboard hook still sees them).</summary>
    public IReadOnlyCollection<HotkeyGesture> Unavailable => _failedLogged;

    public void Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> wanted)
    {
        if (_useHook && _hook is not null && !_hook.Set(wanted))
        {
            _log.LogWarning("The keyboard hook is not available; using registered hotkeys only");
            _useHook = false;
            _hook.Set(new Dictionary<HotkeyAction, HotkeyGesture>());
        }

        foreach (var (action, gesture) in _active.ToList())
        {
            if (!wanted.TryGetValue(action, out var w) || w != gesture)
            {
                UnregisterHotKey(_window.Handle, Id(action));
                _active.Remove(action);
            }
        }

        foreach (var (action, gesture) in wanted)
        {
            if (_active.ContainsKey(action))
            {
                continue;
            }

            if (RegisterHotKey(_window.Handle, Id(action), (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.Key))
            {
                _active[action] = gesture;
                _failedLogged.Remove(gesture);
            }
            else if (_failedLogged.Add(gesture) && !_useHook)
            {
                _log.LogWarning("Hotkey {Gesture} for {Action} is in use by another program (error {Error})", gesture, action, Marshal.GetLastPInvokeError());
            }
        }
    }

    public void ReleaseAll() => Apply(new Dictionary<HotkeyAction, HotkeyGesture>());

    private static int Id(HotkeyAction action) => 0x4C00 + (int)action;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var action = (HotkeyAction)(wParam.ToInt32() - 0x4C00);
            if (Enum.IsDefined(action))
            {
                handled = true;
                Raise(action);
            }
        }

        return IntPtr.Zero;
    }

    private void Raise(HotkeyAction action)
    {
        var now = Environment.TickCount64;
        if (_lastPressed.TryGetValue(action, out var last) && now - last < RepeatGuardMs)
        {
            return;
        }

        _lastPressed[action] = now;
        Pressed?.Invoke(action);
    }

    public void Dispose()
    {
        ReleaseAll();
        _hook?.Dispose();
        _window.Dispose();
    }
}
