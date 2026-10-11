using Microsoft.Win32;

namespace MiniLocalChat.Services;

/// <summary>"Start with Windows" via the per-user Run key. Starts straight into the tray.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MiniLocalChat";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(ValueName, $"\"{exe}\" --tray");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
