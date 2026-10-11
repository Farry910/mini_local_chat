using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MiniLocalChat.Infrastructure;

internal static class Win32
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;
    private const uint FlashwTray = 0x2;
    private const uint FlashwTimerNoForeground = 0xC;
    private static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Lets another process (the running instance) bring its window to the foreground.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    /// <summary>Flashes the taskbar button until the window comes to the foreground.</summary>
    public static void FlashTaskbar(Window window)
    {
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Hwnd = new WindowInteropHelper(window).Handle,
            Flags = FlashwTray | FlashwTimerNoForeground,
        };
        FlashWindowEx(ref info);
    }

    /// <summary>Re-asserts topmost z-order without moving or activating the window.</summary>
    public static void KeepOnTop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    /// <summary>Hides a window from Alt+Tab.</summary>
    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow);
    }
}

/// <summary>Tracks modal dialogs so the floating panel doesn't auto-hide while one is open.</summary>
internal static class DialogGuard
{
    private static int _depth;

    public static bool IsOpen => _depth > 0;

    public static T Run<T>(Func<T> show)
    {
        _depth++;
        try
        {
            return show();
        }
        finally
        {
            _depth--;
        }
    }
}
