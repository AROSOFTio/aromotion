using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows.Input;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class InputHookService : IAsyncDisposable
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmMouseWheel = 0x020A;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly Stopwatch _clock = new();
    private readonly LowLevelHookProc _mouseProc;
    private readonly LowLevelHookProc _keyboardProc;

    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private Channel<CaptureEvent>? _channel;
    private StreamWriter? _writer;
    private Task? _writerTask;
    private long _lastMouseMoveMs = -1000;

    public InputHookService()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    public bool IsRunning => _clock.IsRunning;

    public Task StartAsync(string eventsPath)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Input capture is already running.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(eventsPath)!);

        _channel = Channel.CreateUnbounded<CaptureEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _writer = new StreamWriter(
            new FileStream(eventsPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        _writerTask = WriterLoopAsync(_channel.Reader, _writer);
        _clock.Restart();
        _lastMouseMoveMs = -1000;

        try
        {
            InstallHooks();
        }
        catch
        {
            _clock.Stop();
            _channel.Writer.TryComplete();
            _writer.Dispose();
            _writer = null;
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning && _mouseHook == IntPtr.Zero && _keyboardHook == IntPtr.Zero)
        {
            return;
        }

        _clock.Stop();
        RemoveHooks();

        if (_channel is not null)
        {
            _channel.Writer.TryComplete();
        }

        if (_writerTask is not null)
        {
            await _writerTask;
        }

        if (_writer is not null)
        {
            await _writer.FlushAsync();
            _writer.Dispose();
        }

        _channel = null;
        _writer = null;
        _writerTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private void InstallHooks()
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = GetModuleHandle(currentModule?.ModuleName);

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, moduleHandle, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Unable to install mouse hook. Win32 error: {Marshal.GetLastWin32Error()}");
        }

        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            throw new InvalidOperationException($"Unable to install keyboard hook. Win32 error: {Marshal.GetLastWin32Error()}");
        }
    }

    private void RemoveHooks()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRunning && _channel is not null)
        {
            var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            var message = unchecked((int)(long)wParam);
            var now = _clock.ElapsedMilliseconds;

            switch (message)
            {
                case WmMouseMove:
                    // Keep enough samples for smooth cursor reconstruction while
                    // avoiding thousands of duplicate events per second.
                    if (now - _lastMouseMoveMs >= 8)
                    {
                        _lastMouseMoveMs = now;
                        Publish(new CaptureEvent(now, "mouse_move", data.Point.X, data.Point.Y));
                    }
                    break;

                case WmLButtonDown:
                    Publish(new CaptureEvent(now, "mouse_click", data.Point.X, data.Point.Y, Button: "left"));
                    break;

                case WmRButtonDown:
                    Publish(new CaptureEvent(now, "mouse_click", data.Point.X, data.Point.Y, Button: "right"));
                    break;

                case WmMButtonDown:
                    Publish(new CaptureEvent(now, "mouse_click", data.Point.X, data.Point.Y, Button: "middle"));
                    break;

                case WmMouseWheel:
                    var delta = (short)((data.MouseData >> 16) & 0xffff);
                    Publish(new CaptureEvent(now, "mouse_wheel", data.Point.X, data.Point.Y, Delta: delta));
                    break;
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRunning && _channel is not null)
        {
            var message = unchecked((int)(long)wParam);
            if (message is WmKeyDown or WmSysKeyDown)
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var vk = unchecked((int)data.VirtualKeyCode);
                var modifiers = GetModifiers();

                // Privacy by design: do not store ordinary typed characters.
                // Only combinations useful as tutorial shortcut overlays and F-keys
                // are captured.
                if (ShouldCaptureShortcut(vk, modifiers))
                {
                    var key = KeyInterop.KeyFromVirtualKey(vk).ToString();
                    Publish(new CaptureEvent(
                        _clock.ElapsedMilliseconds,
                        "shortcut",
                        Key: key,
                        Modifiers: modifiers));
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void Publish(CaptureEvent captureEvent)
    {
        _channel?.Writer.TryWrite(captureEvent);
    }

    private static async Task WriterLoopAsync(ChannelReader<CaptureEvent> reader, StreamWriter writer)
    {
        await foreach (var captureEvent in reader.ReadAllAsync())
        {
            var json = JsonSerializer.Serialize(captureEvent);
            await writer.WriteLineAsync(json);
        }
    }

    private static string GetModifiers()
    {
        var values = new List<string>(4);

        if (IsPressed(VkControl)) values.Add("Ctrl");
        if (IsPressed(VkMenu)) values.Add("Alt");
        if (IsPressed(VkShift)) values.Add("Shift");
        if (IsPressed(VkLWin) || IsPressed(VkRWin)) values.Add("Win");

        return string.Join('+', values);
    }

    private static bool ShouldCaptureShortcut(int virtualKey, string modifiers)
    {
        var hasCommandModifier = modifiers.Contains("Ctrl", StringComparison.Ordinal)
                                 || modifiers.Contains("Alt", StringComparison.Ordinal)
                                 || modifiers.Contains("Win", StringComparison.Ordinal);

        var isFunctionKey = virtualKey is >= 0x70 and <= 0x87; // F1-F24
        return hasCommandModifier || isFunctionKey;
    }

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelHookProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
