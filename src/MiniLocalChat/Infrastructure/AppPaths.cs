namespace MiniLocalChat.Infrastructure;

public static class AppPaths
{
    public static string DataDir { get; private set; } = "";

    /// <summary>Received files and pasted images live here, one sub-folder per message.</summary>
    public static string FilesDir => Path.Combine(DataDir, "Files");

    /// <summary>Profile pictures (mine and everyone's), named by content hash.</summary>
    public static string AvatarsDir => Path.Combine(DataDir, "Avatars");

    /// <summary>GIFs that were sent or saved, named by content hash; never deleted, since messages point at them.</summary>
    public static string GifsDir => Path.Combine(FilesDir, "gifs");

    /// <summary>Telegram stickers (.tgs) that were sent, saved or built in, named by content hash; never deleted.</summary>
    public static string StickersDir => Path.Combine(FilesDir, "stickers");

    public static void Initialize(string? customDataDir)
    {
        DataDir = string.IsNullOrWhiteSpace(customDataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniLocalChat")
            : Path.GetFullPath(customDataDir);

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(FilesDir);
        Directory.CreateDirectory(AvatarsDir);
        Directory.CreateDirectory(GifsDir);
        Directory.CreateDirectory(StickersDir);
    }
}

public static class Log
{
    public static void Error(string context, Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "log.txt"), $"{DateTime.Now:u} {context}: {ex}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
