using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Services;

/// <summary>What the app still needs, taken on the UI thread so the file work can run in the background.</summary>
/// <param name="Files">Every file a message, the saved GIFs or the sticker list points at (full paths), with the chat
/// of the first message that does (null: only a list does).</param>
/// <param name="AvatarIds">Profile pictures of me and everyone I know.</param>
public sealed record StorageSnapshot(IReadOnlyDictionary<string, string?> Files, IReadOnlySet<string> AvatarIds);

/// <summary>How the data folder's space is used, in bytes.</summary>
public sealed class StorageReport
{
    /// <summary>Chats, contacts, settings, alarms (the JSON files).</summary>
    public long History { get; set; }

    /// <summary>Copies of pictures and files that messages show.</summary>
    public long ChatFiles { get; set; }

    /// <summary>Saved GIFs, stickers and the built-in animated emoji.</summary>
    public long Library { get; set; }

    public long Avatars { get; set; }

    /// <summary>The trash: files nothing uses any more, and old log entries. What "Empty now" frees.</summary>
    public long Unused { get; set; }

    public int UnusedFiles { get; set; }

    /// <summary>Files just received or about to be sent, the recent log, anything the app doesn't know.</summary>
    public long Other { get; set; }

    /// <summary>Conversation id → bytes of the files its messages show.</summary>
    public Dictionary<string, long> ChatFilesByConversation { get; } = new();

    public long Total => History + ChatFiles + Library + Avatars + Unused + Other;
}

/// <summary>
/// The file side of "Storage and history": measures the data folder and deletes what isn't needed.
/// A file nobody uses any more (its message was deleted, its chat cleared or removed, an old profile
/// picture, a half-received download) goes to the "trash": it's remembered from the day it was first
/// found unused and deleted <see cref="TrashRetention"/> later, unless something uses it again; or
/// right away with "Empty now". Only files inside the data folder are ever touched, never files I sent
/// from my own folders. Thread-safe; meant to run in the background.
/// </summary>
public static class StorageCleaner
{
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(30);

    private const string TrashIndexFile = "trash.json";
    private const string LogFile = "log.txt";

    /// <summary>The log keeps its newest entries; anything before is trash.</summary>
    private const long LogKeepBytes = 64 * 1024;

    /// <summary>Automatic cleanup only trims the log once it's this big.</summary>
    private const long LogTrimAtBytes = 1024 * 1024;

    /// <summary>A file this new may still be arriving (or a message about to point at it) and is left alone.</summary>
    private static readonly TimeSpan RecentGuard = TimeSpan.FromMinutes(10);

    /// <summary>Screenshots waiting in a message box, a picture just chosen in Settings that isn't saved yet.</summary>
    private static readonly TimeSpan SlowGuard = TimeSpan.FromDays(1);

    /// <summary>Copies of chat files that couldn't be read: kept a while in case someone wants to rescue them.</summary>
    private static readonly TimeSpan BackupGuard = TimeSpan.FromDays(30);

    private static readonly Storage Storage = new();
    private static readonly Lock Gate = new();

    private enum Use
    {
        History,
        ChatFile,
        Library,
        Avatar,
        Unused,
        Log,
        Other,
    }

    public static StorageReport Measure(StorageSnapshot snapshot)
    {
        var report = new StorageReport();
        var now = DateTime.UtcNow;
        foreach (var file in AllFiles())
        {
            var length = SafeLength(file);
            switch (Classify(file, snapshot, now))
            {
                case Use.History:
                    report.History += length;
                    break;
                case Use.ChatFile:
                    report.ChatFiles += length;
                    if (snapshot.Files[file.FullName] is { } conversationId)
                    {
                        report.ChatFilesByConversation[conversationId] = report.ChatFilesByConversation.GetValueOrDefault(conversationId) + length;
                    }

                    break;
                case Use.Library:
                    report.Library += length;
                    break;
                case Use.Avatar:
                    report.Avatars += length;
                    break;
                case Use.Unused:
                    report.Unused += length;
                    report.UnusedFiles++;
                    break;
                case Use.Log:
                    var old = Math.Max(0, length - LogKeepBytes);
                    report.Unused += old;
                    report.Other += length - old;
                    break;
                default:
                    report.Other += length;
                    break;
            }
        }

        return report;
    }

    /// <summary>
    /// Sweeps the trash. Unused files are remembered from the first time they're found; with
    /// <paramref name="emptyAll"/> they're all deleted now, otherwise only those unused for <see cref="TrashRetention"/>.
    /// Returns the bytes freed.
    /// </summary>
    public static long EmptyTrash(StorageSnapshot snapshot, bool emptyAll)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            var firstSeen = Storage.Load<Dictionary<string, DateTime>>(TrashIndexFile) ?? [];
            var stillWaiting = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            long freed = 0;
            foreach (var file in AllFiles())
            {
                switch (Classify(file, snapshot, now))
                {
                    case Use.Unused:
                        var key = Path.GetRelativePath(AppPaths.DataDir, file.FullName);
                        var since = firstSeen.TryGetValue(key, out var seen) ? seen : now;
                        if (emptyAll || now - since >= TrashRetention)
                        {
                            freed += TryDelete(file);
                        }
                        else
                        {
                            stillWaiting[key] = since;
                        }

                        break;
                    case Use.Log when emptyAll || file.Length > LogTrimAtBytes:
                        freed += TrimLog(file);
                        break;
                }
            }

            RemoveEmptyMessageFolders();
            try
            {
                Storage.Save(TrashIndexFile, stillWaiting);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Saving trash list", ex);
            }

            return freed;
        }
    }

    /// <summary>Deletes chat files the user chose to remove (they're no longer used). Returns the bytes freed.</summary>
    public static long Delete(IEnumerable<string> paths)
    {
        lock (Gate)
        {
            long freed = 0;
            foreach (var path in paths)
            {
                if (IsChatFileLocation(path))
                {
                    freed += TryDelete(new FileInfo(path));
                }
            }

            RemoveEmptyMessageFolders();
            return freed;
        }
    }

    /// <summary>
    /// Where the app keeps its own copies of files in chats (received files, pasted screenshots): the
    /// Files folder, but not the saved GIFs and stickers, which have their own lists.
    /// </summary>
    public static bool IsChatFileLocation(string? path) =>
        path is not null && IsUnder(path, AppPaths.FilesDir) && !IsUnder(path, AppPaths.GifsDir) && !IsUnder(path, AppPaths.StickersDir);

    public static bool IsUnder(string path, string folder) =>
        path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static Use Classify(FileInfo file, StorageSnapshot snapshot, DateTime now)
    {
        var path = file.FullName;
        if (IsUnder(path, AppPaths.AvatarsDir))
        {
            return snapshot.AvatarIds.Contains(Path.GetFileNameWithoutExtension(path)) ? Use.Avatar : Unused(file, SlowGuard, now);
        }

        if (IsUnder(path, EmojiStickers.Folder))
        {
            return Use.Library;
        }

        if (IsUnder(path, AppPaths.GifsDir) || IsUnder(path, AppPaths.StickersDir))
        {
            return snapshot.Files.ContainsKey(path) ? Use.Library : Unused(file, RecentGuard, now);
        }

        if (IsUnder(path, AppPaths.FilesDir))
        {
            return snapshot.Files.ContainsKey(path) ? Use.ChatFile
                : Unused(file, IsUnder(path, AppPaths.PastedDir) ? SlowGuard : RecentGuard, now);
        }

        if (string.Equals(file.DirectoryName, AppPaths.DataDir, StringComparison.OrdinalIgnoreCase))
        {
            var name = file.Name;
            if (name.Equals(LogFile, StringComparison.OrdinalIgnoreCase))
            {
                return Use.Log;
            }

            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return Unused(file, RecentGuard, now);
            }

            if (name.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase))
            {
                return Unused(file, BackupGuard, now);
            }

            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return Use.History;
            }
        }

        return Use.Other; // never deleted
    }

    private static Use Unused(FileInfo file, TimeSpan guard, DateTime now) =>
        now - file.LastWriteTimeUtc > guard ? Use.Unused : Use.Other;

    private static IEnumerable<FileInfo> AllFiles()
    {
        var root = new DirectoryInfo(AppPaths.DataDir);
        if (!root.Exists)
        {
            return [];
        }

        return root.EnumerateFiles("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // don't wander off through links
        });
    }

    /// <summary>Received files live in one folder per message; once its files are gone, so is the folder.</summary>
    private static void RemoveEmptyMessageFolders()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(AppPaths.FilesDir))
            {
                if (Path.GetFileName(dir) is { Length: 32 } name && Guid.TryParseExact(name, "N", out _)
                    && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Keeps the newest part of the log. Returns the bytes freed.</summary>
    private static long TrimLog(FileInfo log)
    {
        try
        {
            var length = log.Length;
            if (length <= LogKeepBytes)
            {
                return 0;
            }

            byte[] tail;
            using (var stream = log.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(-LogKeepBytes, SeekOrigin.End);
                tail = new byte[LogKeepBytes];
                stream.ReadExactly(tail);
            }

            // Start at a whole line.
            var start = Array.IndexOf(tail, (byte)'\n') + 1;
            File.WriteAllBytes(log.FullName, tail[start..]);
            return length - (tail.Length - start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long TryDelete(FileInfo file)
    {
        try
        {
            var length = file.Length;
            file.Delete();
            return length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0; // in use (e.g. a GIF on screen) or already gone: next time
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
