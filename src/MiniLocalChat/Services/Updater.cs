using System.Diagnostics;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Services;

/// <summary>
/// The file side of the LAN auto-update. A release is just the published exe. An exe from another
/// computer is used if it is a Mini Local Chat exe (product name) with a higher version than mine.
///
/// Installing: the new exe is staged as "MiniLocalChat.exe.new"; then the running exe is renamed to
/// ".old" (Windows allows renaming a running exe), the new one takes its name, and the app restarts.
/// The leftover ".old" is deleted on the next start.
/// </summary>
public static class Updater
{

    public static readonly Version CurrentVersion = Normalize(typeof(Updater).Assembly.GetName().Version);

    public static string? ExePath { get; } = Environment.ProcessPath;

    /// <summary>
    /// A published single-file exe (not a development build, which is a small launcher next to many DLLs
    /// and can neither be handed out nor replaced by one file). An empty assembly Location is exactly
    /// what marks a single-file app, so the IL3000 warning about it is expected here.
    /// </summary>
#pragma warning disable IL3000
    public static bool IsInstalledBuild =>
        ExePath is not null && string.IsNullOrEmpty(typeof(Updater).Assembly.Location);
#pragma warning restore IL3000

    private static string StagedPath => ExePath + ".new";

    private static string OldPath => ExePath + ".old";

    public static Version? Parse(string? text) =>
        Version.TryParse(text, out var version) ? Normalize(version) : null;

    /// <summary>
    /// Checks a downloaded exe (a Mini Local Chat exe, with a version higher than mine) and puts it next to
    /// mine, ready to install. Returns its version, or null if it isn't an update (the download is deleted).
    /// Throws if the app folder isn't writable.
    /// </summary>
    public static Version? Stage(string downloaded)
    {
        var info = FileVersionInfo.GetVersionInfo(downloaded);
        if (!IsInstalledBuild || info.ProductName != AppInfo.Name
            || Parse(info.FileVersion) is not { } version || version <= CurrentVersion)
        {
            TryDelete(downloaded);
            return null;
        }

        File.Move(downloaded, StagedPath, overwrite: true);
        return version;
    }
    /// <summary>Swaps the staged exe in. The running app keeps going until it restarts.</summary>
    public static bool Install()
    {
        if (!IsInstalledBuild || !File.Exists(StagedPath))
        {
            return false;
        }

        try
        {
            TryDelete(OldPath);
            File.Move(ExePath!, OldPath);
            try
            {
                File.Move(StagedPath, ExePath!);
            }
            catch
            {
                File.Move(OldPath, ExePath!); // put mine back
                throw;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Installing update", ex);
            return false;
        }
    }

    /// <summary>Starts the (new) exe with the same options, once this process has exited.</summary>
    public static void Restart(IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(ExePath!) { UseShellExecute = false };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.ArgumentList.Add("--after-update");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(start);
    }

    /// <summary>On start: the previous exe is no longer needed; a half-done download is dropped.</summary>
    public static void CleanUp()
    {
        if (!IsInstalledBuild)
        {
            return;
        }

        TryDelete(OldPath);
        TryDelete(StagedPath);
        TryDelete(ExePath + ".sig"); // from the earlier, signed updater
    }

    /// <summary>Waits for the process that just installed this update to finish closing.</summary>
    public static void WaitForExit(string? processId)
    {
        if (!int.TryParse(processId, out var id))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(id);
            previous.WaitForExit(TimeSpan.FromSeconds(20));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    // "1.4" and "1.4.0.0" are the same version.
    private static Version Normalize(Version? version) =>
        version is null ? new Version(0, 0, 0) : new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
