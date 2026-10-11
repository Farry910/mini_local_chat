using System.Reflection;

namespace MiniLocalChat.Infrastructure;

public static class AppInfo
{
    public const string Name = "Mini Local Chat";

    /// <summary>
    /// e.g. "1.1.0 (d6d0f88)". The SDK appends "+&lt;git commit&gt;" to the informational version,
    /// so the title shows exactly which commit a build came from.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    public static string Title => $"{Name} {Version}";

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = info.IndexOf('+');
        if (plus < 0)
        {
            return info;
        }

        var commit = info[(plus + 1)..];
        return $"{info[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }
}
