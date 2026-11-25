using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClashOverlay.Core.Settings;
using Microsoft.Extensions.Logging;
using static ClashOverlay.App.Native.NativeMethods;

namespace ClashOverlay.App.Overlay;

/// <summary>
/// System-wide hotkeys through <c>RegisterHotKey</c> on a message-only window. <see cref="Apply"/> is called with the
/// set that should be active now (see <see cref="HotkeyPlan"/>); keys that are no longer wanted are released at once,
/// so the game gets them back.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _window;
    private readonly ILogger _log;
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _active = new();
    private readonly HashSet<HotkeyGesture> _failedLogged = [];

    public HotkeyManager(ILogger logger)
    {
        _log = logger;
        var p = new HwndSourceParameters("ClashOverlay.Hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);
    }

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Gestures another program already holds.</summary>
    public IReadOnlyCollection<HotkeyGesture> Unavailable => _failedLogged;

    public void Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> wanted)
    {
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
            else if (_failedLogged.Add(gesture))
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
                Pressed?.Invoke(action);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        ReleaseAll();
        _window.Dispose();
    }
}
