using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ClashOverlay.Core.Settings;
using Microsoft.Extensions.Logging;
using static ClashOverlay.App.Native.NativeMethods;

namespace ClashOverlay.App.Overlay;

/// <summary>
/// Looks for this app's own shortcuts with a low-level keyboard hook (<c>WH_KEYBOARD_LL</c>). Games and emulators
/// such as Google Play Games take over the keyboard while they are focused, so <c>RegisterHotKey</c> shortcuts never
/// reach the app; a low-level hook still sees the key first. The hook runs in this process on its own thread (nothing
/// is injected anywhere), is only installed while a shortcut is wanted (the game is in front or the overlay is
/// showing), and only acts on the wanted shortcuts: those keys are kept from the game, like a registered hotkey, and
/// every other key passes straight through. No key is recorded or sent.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private const uint WorkMessage = WM_APP + 1;

    // Left and right modifier keys as the hook reports them, and the generic codes some keyboards send.
    private static readonly (int Vk, KeyModifiers Mod)[] ModifierKeys =
    [
        (0xA2, KeyModifiers.Ctrl), (0xA3, KeyModifiers.Ctrl), (0x11, KeyModifiers.Ctrl),
        (0xA4, KeyModifiers.Alt), (0xA5, KeyModifiers.Alt), (0x12, KeyModifiers.Alt),
        (0xA0, KeyModifiers.Shift), (0xA1, KeyModifiers.Shift), (0x10, KeyModifiers.Shift),
        (0x5B, KeyModifiers.Win), (0x5C, KeyModifiers.Win),
    ];

    private readonly Action<HotkeyAction> _pressed;
    private readonly ILogger _log;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly LowLevelKeyboardProc _proc;
    private readonly IntPtr _procPtr;

    // Only touched on the hook thread.
    private readonly HashSet<int> _modifiersDown = [];
    private readonly HashSet<int> _held = [];
    private IntPtr _hook;
    private uint _threadId;

    private volatile Dictionary<HotkeyGesture, HotkeyAction> _wanted = new();

    /// <param name="pressed">Called on the hook's thread; keep it short (post to the UI thread).</param>
    public KeyboardHook(Action<HotkeyAction> pressed, ILogger logger)
    {
        _pressed = pressed;
        _log = logger;
        _proc = OnKey;
        _procPtr = Marshal.GetFunctionPointerForDelegate(_proc);
        _thread = new Thread(Loop) { IsBackground = true, Name = "ClashOverlay.KeyboardHook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Watches for <paramref name="wanted"/> from now on; an empty set removes the hook. Going from nothing to
    /// something installs the hook afresh, so it is the newest one and sees keys before any the game installed.
    /// Returns false when the hook could not be installed.
    /// </summary>
    public bool Set(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> wanted)
    {
        var map = new Dictionary<HotkeyGesture, HotkeyAction>();
        foreach (var (action, gesture) in wanted)
        {
            map.TryAdd(gesture, action);
        }

        var wasEmpty = _wanted.Count == 0;
        _wanted = map;
        if (map.Count == 0)
        {
            Invoke(Remove);
            return true;
        }

        return !wasEmpty && _hook != IntPtr.Zero || Invoke(Install);
    }

    private bool Invoke(Func<bool> action)
    {
        if (_threadId == 0)
        {
            return false;
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() => done.TrySetResult(action()));
        if (!PostThreadMessage(_threadId, WorkMessage, UIntPtr.Zero, IntPtr.Zero))
        {
            return false;
        }

        return done.Task.Wait(TimeSpan.FromSeconds(2)) && done.Task.Result;
    }

    private void Loop()
    {
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // gives the thread its message queue
        _threadId = GetCurrentThreadId();
        _started.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WorkMessage)
            {
                while (_work.TryDequeue(out var work))
                {
                    work();
                }
            }
        }

        Remove();
    }

    private bool Install()
    {
        Remove();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _procPtr, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            _log.LogWarning("Keyboard hook could not be installed (error {Error})", Marshal.GetLastPInvokeError());
            return false;
        }

        // Modifiers already held when the hook goes in.
        _modifiersDown.Clear();
        _held.Clear();
        foreach (var (vk, _) in ModifierKeys)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                _modifiersDown.Add(vk);
            }
        }

        return true;
    }

    private bool Remove()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _held.Clear();
        return true;
    }

    private KeyModifiers Modifiers()
    {
        var mods = KeyModifiers.None;
        foreach (var (vk, mod) in ModifierKeys)
        {
            if (_modifiersDown.Contains(vk))
            {
                mods |= mod;
            }
        }

        return mods;
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            var down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            var up = message is WM_KEYUP or WM_SYSKEYUP;
            var vk = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode;
            if (Array.Exists(ModifierKeys, m => m.Vk == vk))
            {
                if (down)
                {
                    _modifiersDown.Add(vk);
                }
                else if (up)
                {
                    _modifiersDown.Remove(vk);
                }
            }
            else if (down)
            {
                if (_held.Contains(vk))
                {
                    return new IntPtr(1); // key repeat of a shortcut that already fired
                }

                if (_wanted.TryGetValue(new HotkeyGesture(Modifiers(), vk), out var action))
                {
                    _held.Add(vk);
                    _pressed(action);
                    return new IntPtr(1);
                }
            }
            else if (up && _held.Remove(vk))
            {
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        _wanted = new Dictionary<HotkeyGesture, HotkeyAction>();
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, UIntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _started.Dispose();
        GC.KeepAlive(_proc);
    }
}
