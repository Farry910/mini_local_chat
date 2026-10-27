using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Services;

/// <summary>Picture and file copies in chats that are about to be deleted to free up space, and the messages showing them.</summary>
public sealed class FileCleanup
{
    public List<ChatMessage> Messages { get; } = [];

    /// <summary>The files that exist; messages whose file is already gone are just marked as removed.</summary>
    public List<string> Paths { get; } = [];

    public long Bytes { get; set; }

    public bool IsEmpty => Messages.Count == 0;
}

/// <summary>
/// Disk space (Settings → Manage storage): measuring, the trash of unused files (emptied automatically
/// after <see cref="StorageCleaner.TrashRetention"/>), and deleting copies of files in chats on request
/// or by age. The messages stay; their picture or file shows as removed.
/// </summary>
public sealed partial class ChatService
{
    private static readonly TimeSpan FirstCleanupAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    private DispatcherTimer? _cleanupTimer;

    public Task<StorageReport> MeasureStorageAsync()
    {
        var snapshot = SnapshotStorage();
        return Task.Run(() => StorageCleaner.Measure(snapshot));
    }

    /// <summary>Deletes every unused file now. Returns the bytes freed.</summary>
    public Task<long> EmptyTrashAsync()
    {
        var snapshot = SnapshotStorage();
        return Task.Run(() => StorageCleaner.EmptyTrash(snapshot, emptyAll: true));
    }

    /// <summary>
    /// The copies of pictures and files that can go: in one chat (or all), optionally only those sent before
    /// <paramref name="olderThanUtc"/>. Skipped: stickers, saved GIFs, anything still being delivered, files I
    /// sent from my own folders, and files a message outside the selection still shows (e.g. forwarded).
    /// </summary>
    public FileCleanup PlanFileCleanup(Conversation? only, DateTime? olderThanUtc)
    {
        var users = new Dictionary<string, List<(ChatMessage Message, bool Selected)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var conversation in Conversations)
        {
            foreach (var message in conversation.Messages)
            {
                if (FullPath(message.LocalPath) is not { } path)
                {
                    continue;
                }

                var selected = (only is null || conversation == only)
                               && (olderThanUtc is null || message.SentUtc < olderThanUtc)
                               && message.Kind is MessageKind.Image or MessageKind.File
                               && !message.IsSticker
                               && !message.Delivery.ContainsValue(DeliveryState.Pending)
                               && StorageCleaner.IsChatFileLocation(path);
                if (!users.TryGetValue(path, out var list))
                {
                    users[path] = list = [];
                }

                list.Add((message, selected));
            }
        }

        var plan = new FileCleanup();
        foreach (var (path, list) in users)
        {
            if (!list.TrueForAll(u => u.Selected))
            {
                continue;
            }

            plan.Messages.AddRange(list.Select(u => u.Message));
            var info = new FileInfo(path);
            if (info.Exists)
            {
                plan.Paths.Add(path);
                plan.Bytes += info.Length;
            }
        }

        return plan;
    }

    /// <summary>Marks the messages' files as removed and deletes them. Returns the bytes freed.</summary>
    public Task<long> DeleteFilesAsync(FileCleanup plan)
    {
        foreach (var message in plan.Messages)
        {
            message.LocalPath = null;
        }

        ScheduleSave();
        var paths = plan.Paths.ToList();
        return Task.Run(() => StorageCleaner.Delete(paths));
    }

    /// <summary>Clears a chat's history and deletes the files only it showed. Returns the bytes freed.</summary>
    public Task<long> ClearHistoryAndFilesAsync(Conversation conversation)
    {
        var paths = conversation.Messages
            .Select(m => FullPath(m.LocalPath))
            .OfType<string>()
            .Where(StorageCleaner.IsChatFileLocation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ClearHistory(conversation);

        var stillShown = SnapshotStorage().Files;
        var unused = paths.Where(p => !stillShown.ContainsKey(p)).ToList();
        return Task.Run(() => StorageCleaner.Delete(unused));
    }

    /// <summary>What the app still needs: files messages show, saved GIFs and stickers, everyone's pictures.</summary>
    private StorageSnapshot SnapshotStorage()
    {
        var files = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var conversation in Conversations)
        {
            foreach (var message in conversation.Messages)
            {
                if (FullPath(message.LocalPath) is { } path)
                {
                    files.TryAdd(path, conversation.Id);
                }
            }
        }

        foreach (var gif in GifLibrary.Items)
        {
            files.TryAdd(gif.Path, null);
        }

        foreach (var sticker in StickerLibrary.Items)
        {
            files.TryAdd(sticker.Path, null);
        }

        var avatars = Contacts.Select(c => c.AvatarId).Append(Me.AvatarId).Append(Settings.AvatarId)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new StorageSnapshot(files, avatars);
    }

    private static string? FullPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Shortly after start, then once a day: old chat files (if chosen) and the trash.</summary>
    private void StartAutoCleanup()
    {
        _cleanupTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = FirstCleanupAfter };
        _cleanupTimer.Tick += async (_, _) =>
        {
            _cleanupTimer.Interval = CleanupInterval;
            await AutoCleanupAsync();
        };
        _cleanupTimer.Start();
    }

    private void StopAutoCleanup() => _cleanupTimer?.Stop();

    private async Task AutoCleanupAsync()
    {
        try
        {
            if (Settings.DeleteFilesAfterDays > 0)
            {
                var plan = PlanFileCleanup(null, DateTime.UtcNow.AddDays(-Settings.DeleteFilesAfterDays));
                if (!plan.IsEmpty)
                {
                    await DeleteFilesAsync(plan);
                }
            }

            if (Settings.AutoEmptyTrash)
            {
                var snapshot = SnapshotStorage();
                await Task.Run(() => StorageCleaner.EmptyTrash(snapshot, emptyAll: false));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Automatic cleanup", ex);
        }
    }
}
