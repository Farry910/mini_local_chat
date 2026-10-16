using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace MiniLocalChat.Infrastructure;

/// <summary>
/// A system-wide keyboard shortcut (works whichever app is in front), e.g. "Ctrl+Alt+M".
/// Listens on a hidden message-only window.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xB00B;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _window;
    private bool _registered;

    public GlobalHotkey()
    {
        // Parent HWND_MESSAGE (-3): an invisible window that only receives messages.
        _window = new HwndSource(new HwndSourceParameters("MiniLocalChat hotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Registers the shortcut (replacing the previous one). Empty text = no shortcut.</summary>
    public bool TrySet(string? text, out string error)
    {
        error = "";
        Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!TryParse(text, out var modifiers, out var key))
        {
            error = $"“{text}” isn't a valid shortcut.";
            return false;
        }

        uint mods = ModNoRepeat;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= ModWin;

        if (!RegisterHotKey(_window.Handle, HotkeyId, mods, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            error = $"{text} is already used by another app. Pick a different combination.";
            return false;
        }

        _registered = true;
        return true;
    }

    public void Clear()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Clear();
        _window.Dispose();
    }

    /// <summary>"Ctrl+Alt+M" style text; Ctrl, Alt, Shift, Win in that order.</summary>
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParse(string text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (part.Length == 1 && char.IsDigit(part[0]))
                    {
                        key = Key.D0 + (part[0] - '0');
                    }
                    else if (!Enum.TryParse(part, ignoreCase: true, out key) || int.TryParse(part, out _))
                    {
                        return false;
                    }

                    break;
            }
        }

        // A shortcut needs at least one modifier besides Shift, and a real key.
        return key != Key.None && (modifiers & ~ModifierKeys.Shift) != ModifierKeys.None;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
