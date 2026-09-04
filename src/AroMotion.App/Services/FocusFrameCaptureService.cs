using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

/// <summary>
/// Captures best-effort native HWND rectangles for clicks and focus changes.
/// It intentionally uses normal user32 APIs only, so it works from a standard
/// Windows user account. UWP/Chromium custom-drawn controls may fall back to
/// the containing window rectangle.
/// </summary>
public sealed class FocusFrameCaptureService : IAsyncDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const uint EventObjectFocus = 0x8005;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const uint CwpSkipInvisible = 0x0001;
    private const uint CwpSkipDisabled = 0x0002;

    private readonly Stopwatch _clock = new();
    private readonly MouseProc _mouseProc;
    private readonly WinEventProc _focusProc;
    private StreamWriter? _writer;
    private IntPtr _mouseHook;
    private IntPtr _focusHook;
    private long _lastFocusMs = -1000;

    public FocusFrameCaptureService()
    {
        _mouseProc = MouseCallback;
        _focusProc = FocusCallback;
    }

    public bool IsRunning => _clock.IsRunning;

    public Task StartAsync(string path)
    {
        if (IsRunning) throw new InvalidOperationException("Focus-frame capture is already running.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        _clock.Restart();
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
        _focusHook = SetWinEventHook(EventObjectFocus, EventObjectFocus, IntPtr.Zero, _focusProc, 0, 0,
            WineventOutofcontext | WineventSkipownprocess);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _clock.Stop();
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_focusHook != IntPtr.Zero) { UnhookWinEvent(_focusHook); _focusHook = IntPtr.Zero; }
        if (_writer is not null)
        {
            await _writer.FlushAsync();
            _writer.Dispose();
            _writer = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsRunning && unchecked((int)(long)wParam) == WmLButtonDown)
        {
            var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            WriteFrame("focus_frame", data.Point.X, data.Point.Y, WindowFromPoint(data.Point));
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void FocusCallback(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (!IsRunning || hwnd == IntPtr.Zero) return;
        var now = _clock.ElapsedMilliseconds;
        if (now - _lastFocusMs < 180) return;
        _lastFocusMs = now;
        if (GetWindowRect(hwnd, out var rect))
        {
            WriteFrame("focus_change", rect.Left + rect.Width / 2, rect.Top + rect.Height / 2, hwnd);
        }
    }

    private void WriteFrame(string type, int x, int y, IntPtr hwnd)
    {
        if (_writer is null || hwnd == IntPtr.Zero) return;
        hwnd = FindDeepestChildAtPoint(hwnd, x, y);
        if (!GetWindowRect(hwnd, out var rect) || rect.Width < 8 || rect.Height < 8) return;
        var title = GetTitle(GetAncestor(hwnd, 2));
        var evt = new CaptureEvent(
            _clock.ElapsedMilliseconds,
            type,
            x,
            y,
            FrameX: rect.Left,
            FrameY: rect.Top,
            FrameWidth: rect.Width,
            FrameHeight: rect.Height,
            WindowTitle: title);
        _writer.WriteLine(JsonSerializer.Serialize(evt));
    }

    private static IntPtr FindDeepestChildAtPoint(IntPtr hwnd, int screenX, int screenY)
    {
        var current = hwnd;
        for (var depth = 0; depth < 12; depth++)
        {
            var clientPoint = new Point { X = screenX, Y = screenY };
            if (!ScreenToClient(current, ref clientPoint)) break;
            var child = ChildWindowFromPointEx(current, clientPoint, CwpSkipInvisible | CwpSkipDisabled);
            if (child == IntPtr.Zero || child == current) break;
            current = child;
        }
        return current;
    }

    private static string? GetTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return null;
        var builder = new StringBuilder(length + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam);
    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MsLlHookStruct { public Point Point; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, MouseProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Point point, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
}
