namespace MiniLocalChat.Infrastructure;

public static class AppPaths
{
    public static string DataDir { get; private set; } = "";

    /// <summary>Received files and pasted images live here, one sub-folder per message.</summary>
    public static string FilesDir => Path.Combine(DataDir, "Files");

    public static void Initialize(string? customDataDir)
    {
        DataDir = string.IsNullOrWhiteSpace(customDataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniLocalChat")
            : Path.GetFullPath(customDataDir);

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(FilesDir);
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
