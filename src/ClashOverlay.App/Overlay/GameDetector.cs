using System.Runtime.InteropServices;
using System.Windows.Threading;
using ClashOverlay.App.Native;
using ClashOverlay.Core.Display;
using ClashOverlay.Core.Settings;
using Microsoft.Extensions.Logging;
using static ClashOverlay.App.Native.NativeMethods;

namespace ClashOverlay.App.Overlay;

internal sealed record GameWindow(WindowInfo Window, string RuleName);

/// <summary>
/// Finds the game's window by the rules in settings. Window events (foreground changes, minimise and restore, and,
/// once the game is found, its window moving or closing) arrive through <c>SetWinEventHook</c>; a 3 s timer covers
/// anything the events miss. Only window titles, process names and rectangles are read.
/// </summary>
internal sealed class GameDetector : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly Func<IReadOnlyList<GameWindowRule>> _rules;
    private readonly ILogger _log;
    private readonly DispatcherTimer _poll;
    private readonly WinEventProc _callback;
    private readonly IntPtr _callbackPtr;
    private readonly List<IntPtr> _globalHooks = [];
    private readonly List<IntPtr> _gameHooks = [];
    private IntPtr _pinned;

    public GameDetector(Func<IReadOnlyList<GameWindowRule>> rules, ILogger logger)
    {
        _rules = rules;
        _log = logger;
        _callback = OnWinEvent;
        _callbackPtr = Marshal.GetFunctionPointerForDelegate(_callback);
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _poll.Tick += (_, _) => Poll();
    }

    public GameWindow? Game { get; private set; }

    public IntPtr Foreground { get; private set; }

    public bool IsGameForeground => Game is not null && Foreground == Game.Window.Handle;

    public bool IsGameMinimised => Game is not null && IsIconic(Game.Window.Handle);

    /// <summary>The game appeared, went away, gained or lost the foreground, or was minimised or restored.</summary>
    public event EventHandler? Changed;

    public event EventHandler<GameWindow>? GameFound;

    /// <summary>The game window moved or changed size.</summary>
    public event EventHandler? GameMoved;

    public PixelRect? GameBounds => Game is null ? null : NativeWindows.Bounds(Game.Window.Handle);

    public void Start()
    {
        Hook(_globalHooks, EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0);
        Hook(_globalHooks, EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, 0);
        Foreground = GetForegroundWindow();
        Poll();
        _poll.Start();
    }

    /// <summary>Use this window from now on (the "Pick game window" button), until it closes.</summary>
    public void Pin(IntPtr hwnd)
    {
        _pinned = hwnd;
        SetGame(null);
        Poll();
    }

    /// <summary>Rules changed: look again.</summary>
    public void Rescan()
    {
        SetGame(null);
        Poll();
    }

    public string? MatchRule(WindowInfo w)
    {
        if (w.Handle == _pinned && _pinned != IntPtr.Zero)
        {
            return "Picked window";
        }

        foreach (var rule in _rules())
        {
            if (rule.Matches(w.ProcessName, w.Title))
            {
                return rule.Name;
            }
        }

        return null;
    }

    private void Poll()
    {
        try
        {
            if (Game is { } g)
            {
                var still = IsWindow(g.Window.Handle) ? NativeWindows.Describe(g.Window.Handle) : null;
                if (still is null || MatchRule(still) is null)
                {
                    SetGame(null);
                }
            }

            if (Game is null)
            {
                if (_pinned != IntPtr.Zero && !IsWindow(_pinned))
                {
                    _pinned = IntPtr.Zero;
                }

                var fg = NativeWindows.Describe(Foreground);
                if (fg is not null && !NativeWindows.IsOwnProcess(fg.Handle) && MatchRule(fg) is { } rule)
                {
                    SetGame(new GameWindow(fg, rule));
                }
                else
                {
                    foreach (var w in NativeWindows.TopLevel())
                    {
                        if (MatchRule(w) is { } r)
                        {
                            SetGame(new GameWindow(w, r));
                            break;
                        }
                    }
                }
            }

            var foreground = GetForegroundWindow();
            if (foreground != Foreground)
            {
                Foreground = foreground;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Game window check failed");
        }
    }

    private void SetGame(GameWindow? game)
    {
        if (Game?.Window.Handle == game?.Window.Handle)
        {
            return;
        }

        Unhook(_gameHooks);
        var old = Game;
        Game = game;
        if (game is null)
        {
            if (old is not null)
            {
                _log.LogInformation("Game window gone: {Window}", old.Window);
            }
        }
        else
        {
            _log.LogInformation("Game window found: {Window} by rule {Rule}", game.Window, game.RuleName);
            Hook(_gameHooks, EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, game.Window.ProcessId);
            Hook(_gameHooks, EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_NAMECHANGE, game.Window.ProcessId);
            GameFound?.Invoke(this, game);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0)
        {
            return;
        }

        switch (eventType)
        {
            case EVENT_SYSTEM_FOREGROUND:
                Foreground = hwnd;
                if (Game is null && !NativeWindows.IsOwnProcess(hwnd) && NativeWindows.Describe(hwnd) is { } w && MatchRule(w) is { } rule)
                {
                    SetGame(new GameWindow(w, rule));
                }
                else
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }

                break;
            case EVENT_SYSTEM_MINIMIZESTART or EVENT_SYSTEM_MINIMIZEEND when Game?.Window.Handle == hwnd:
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case EVENT_OBJECT_DESTROY when Game?.Window.Handle == hwnd:
                SetGame(null);
                break;
            case EVENT_OBJECT_LOCATIONCHANGE when Game?.Window.Handle == hwnd:
                GameMoved?.Invoke(this, EventArgs.Empty);
                break;
            case EVENT_OBJECT_NAMECHANGE when Game?.Window.Handle == hwnd:
                Poll(); // a launcher reusing its window for another game
                break;
        }
    }

    private void Hook(List<IntPtr> into, uint min, uint max, uint processId)
    {
        var h = SetWinEventHook(min, max, IntPtr.Zero, _callbackPtr, processId, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h == IntPtr.Zero)
        {
            _log.LogWarning("SetWinEventHook {Min:X}-{Max:X} failed; the 3 s check still runs", min, max);
        }
        else
        {
            into.Add(h);
        }
    }

    private static void Unhook(List<IntPtr> hooks)
    {
        foreach (var h in hooks)
        {
            UnhookWinEvent(h);
        }

        hooks.Clear();
    }

    public void Dispose()
    {
        _poll.Stop();
        Unhook(_gameHooks);
        Unhook(_globalHooks);
        GC.KeepAlive(_callback);
    }
}
