using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ClashOverlay.Core.Display;
using static ClashOverlay.App.Native.NativeMethods;

namespace ClashOverlay.App.Native;

/// <summary>A top-level window as seen from outside: handle, title, owning process name and rectangle.</summary>
internal sealed record WindowInfo(IntPtr Handle, uint ProcessId, string ProcessName, string Title)
{
    public override string ToString() => $"{Title} ({ProcessName})";
}

/// <summary>Helpers over <see cref="NativeMethods"/> for finding, measuring and placing windows.</summary>
internal static class NativeWindows
{
    private static readonly Dictionary<uint, (string Name, long Seen)> ProcessNames = new();

    /// <summary>
    /// Visible, unowned, titled top-level windows of other processes, in z-order. Process names come from the system's
    /// process list (the same list Task Manager shows), so no process is opened.
    /// </summary>
    public static IReadOnlyList<WindowInfo> TopLevel()
    {
        var handles = new List<IntPtr>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            unsafe
            {
                EnumWindows(&Collect, GCHandle.ToIntPtr(gc));
            }
        }
        finally
        {
            gc.Free();
        }

        var own = (uint)Environment.ProcessId;
        var list = new List<WindowInfo>();
        foreach (var h in handles)
        {
            if (!IsCandidate(h))
            {
                continue;
            }

            GetWindowThreadProcessId(h, out var pid);
            if (pid == own)
            {
                continue;
            }

            var title = GetTitle(h);
            if (title.Length == 0)
            {
                continue;
            }

            list.Add(new WindowInfo(h, pid, ProcessName(pid), title));
        }

        return list;
    }

    public static WindowInfo? Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        return new WindowInfo(hwnd, pid, ProcessName(pid), GetTitle(hwnd));
    }

    public static bool IsOwnProcess(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static bool IsCandidate(IntPtr h)
    {
        if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        if ((GetExStyle(h) & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        if (DwmGetWindowAttributeInt(h, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false; // suspended Store apps and windows on other virtual desktops
        }

        var r = Bounds(h);
        return r.Width >= 160 && r.Height >= 120 || IsIconic(h);
    }

    [UnmanagedCallersOnly]
    private static int Collect(IntPtr hwnd, IntPtr state)
    {
        ((List<IntPtr>)GCHandle.FromIntPtr(state).Target!).Add(hwnd);
        return 1;
    }

    /// <summary>The process's name without ".exe", or "" when it has gone.</summary>
    public static string ProcessName(uint pid)
    {
        var now = Environment.TickCount64;
        lock (ProcessNames)
        {
            if (ProcessNames.TryGetValue(pid, out var hit) && now - hit.Seen < 15_000)
            {
                return hit.Name;
            }

            // One snapshot of the process list fills the cache for every process at once.
            ProcessNames.Clear();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    ProcessNames[(uint)p.Id] = (p.ProcessName, now);
                }
            }

            if (ProcessNames.TryGetValue(pid, out hit))
            {
                return hit.Name;
            }

            ProcessNames[pid] = (string.Empty, now);
            return string.Empty;
        }
    }

    /// <summary>The visible frame in physical pixels (without the invisible resize borders).</summary>
    public static PixelRect Bounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Unsafe.SizeOf<RECT>()) != 0)
        {
            GetWindowRect(hwnd, out r);
        }

        return PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static IntPtr MonitorOf(IntPtr hwnd) => MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    public static PixelRect WorkArea(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Unsafe.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return new PixelRect(0, 0, 1920, 1040);
        }

        return PixelRect.FromEdges(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
    }

    /// <summary>1.0 at 96 DPI.</summary>
    public static double DpiScale(IntPtr monitor) =>
        GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var x, out _) == 0 && x > 0 ? x / 96.0 : 1.0;

    /// <summary>Tool window that never takes focus: clicks on it leave the game in the foreground.</summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        var style = GetExStyle(hwnd);
        style = (style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetExStyle(hwnd, style);
    }

    public static void Move(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

    public static void MoveResize(IntPtr hwnd, PixelRect r) =>
        SetWindowPos(hwnd, HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE);
}
