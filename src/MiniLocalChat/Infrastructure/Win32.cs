using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MiniLocalChat.Infrastructure;

internal static class Win32
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExTransparent = 0x20;
    private const int WsExNoActivate = 0x08000000;
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

    /// <summary>Overlay mode: mouse clicks go to whatever is underneath, never takes focus, not in Alt+Tab.</summary>
    public static void MakeClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>
    /// Per-pixel transparent overlay without WPF's AllowsTransparency. WPF then renders straight to the GPU and
    /// Windows' compositor blends it, instead of WPF copying the whole frame back to the CPU every frame.
    /// No border or rounded corners. Call once the window has a handle (SourceInitialized).
    /// </summary>
    public static void MakeCompositedOverlay(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = System.Windows.Media.Colors.Transparent;
        }

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var doNotRound = DwmCornerDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmaWindowCornerPreference, ref doNotRound, sizeof(int));
        var noBorder = DwmColorNone;
        DwmSetWindowAttribute(hwnd, DwmaBorderColor, ref noBorder, sizeof(int));
    }

    /// <summary>
    /// Click-through for a <see cref="MakeCompositedOverlay"/> window: needs WS_EX_LAYERED with WS_EX_TRANSPARENT.
    /// WPF strips WS_EX_LAYERED from windows without AllowsTransparency, so a message hook keeps the flags in
    /// place whenever the style changes. Constant full opacity keeps GPU rendering.
    /// </summary>
    public static void MakeClickThroughOverlay(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(KeepOverlayStyle);
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | OverlayExStyle);
        SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
    }

    private const int OverlayExStyle = WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
    private const int WmStyleChanging = 0x007C;

    [StructLayout(LayoutKind.Sequential)]
    private struct StyleStruct
    {
        public int StyleOld;
        public int StyleNew;
    }

    private static IntPtr KeepOverlayStyle(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmStyleChanging && wParam.ToInt32() == GwlExStyle)
        {
            var styles = Marshal.PtrToStructure<StyleStruct>(lParam);
            styles.StyleNew |= OverlayExStyle;
            Marshal.StructureToPtr(styles, lParam, false);
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// True while the user shouldn't be interrupted: a full-screen app or game, presentation mode,
    /// or the screen is locked / screensaver.
    /// </summary>
    public static bool IsUserBusy()
    {
        if (SHQueryUserNotificationState(out var state) != 0)
        {
            return false;
        }

        // 1 not present (locked), 2 busy (full screen), 3 Direct3D full screen, 4 presentation mode, 7 full-screen store app.
        return state is 1 or 2 or 3 or 4 or 7;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private const int WsExLayered = 0x80000;
    private const uint LwaAlpha = 0x2;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaBorderColor = 34;
    private const int DwmCornerDoNotRound = 1;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
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
