using System.ComponentModel;
using System.Diagnostics;

namespace MiniLocalChat.Services;

/// <summary>
/// Lets other computers reach this app through Windows Firewall. If the first-run firewall prompt was
/// dismissed, Windows silently blocks the app and nobody can see this computer.
/// </summary>
public static class FirewallSetup
{
    private const string RuleName = "Mini Local Chat";

    /// <summary>Replaces any rules for this exe (including "block" rules from a dismissed prompt) with an allow rule. Asks for admin rights.</summary>
    public static bool AllowThisApp()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return false;
        }

        var commands =
            $"netsh advfirewall firewall delete rule name=all program=\"{exe}\" & " +
            $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" enable=yes profile=any";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {commands}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // The user declined the admin prompt.
            return false;
        }
    }
}
