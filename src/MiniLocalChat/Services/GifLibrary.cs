using System.Collections.ObjectModel;
using System.Security.Cryptography;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Services;

public sealed class SavedGif
{
    public string Hash { get; set; } = "";

    /// <summary>The name it had when it was saved, for the filter box.</summary>
    public string Name { get; set; } = "";

    public DateTime LastUsedUtc { get; set; }

    public string Path => System.IO.Path.Combine(AppPaths.GifsDir, Hash + ".gif");
}

/// <summary>
/// "Saved GIFs", like Telegram: every GIF I send is saved automatically, received ones on request.
/// Files live in a content-addressed folder and are never deleted (messages point at them);
/// removing a GIF only takes it off this list. Most recently used first.
/// </summary>
public static class GifLibrary
{
    private const string IndexFile = "gifs.json";
    private const int MaxItems = 300;
    private static readonly Storage Storage = new();
    private static ObservableCollection<SavedGif>? _items;

    public static ObservableCollection<SavedGif> Items => _items ??= Load();

    public static bool IsGif(string? path) =>
        string.Equals(System.IO.Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies a GIF into the store (once per content) and returns the stored path.</summary>
    public static string Store(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))[..16].ToLowerInvariant();
        var stored = System.IO.Path.Combine(AppPaths.GifsDir, hash + ".gif");
        if (!File.Exists(stored))
        {
            File.Copy(path, stored);
        }

        return stored;
    }

    /// <summary>Saves a GIF to the list (or moves it to the front). Returns the stored path.</summary>
    public static string Add(string path)
    {
        var stored = Store(path);
        var hash = System.IO.Path.GetFileNameWithoutExtension(stored);
        var existing = Items.FirstOrDefault(g => g.Hash == hash);
        if (existing is not null)
        {
            Items.Remove(existing);
        }

        Items.Insert(0, new SavedGif
        {
            Hash = hash,
            Name = existing?.Name ?? System.IO.Path.GetFileNameWithoutExtension(path),
            LastUsedUtc = DateTime.UtcNow,
        });
        while (Items.Count > MaxItems)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        Save();
        return stored;
    }

    public static bool Contains(string? path) =>
        path is not null && File.Exists(path) && Items.Any(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase)
                                                          || HashOf(path) == g.Hash);

    public static void Remove(SavedGif gif)
    {
        Items.Remove(gif);
        Save();
    }

    private static string? HashOf(string path)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))[..16].ToLowerInvariant();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static ObservableCollection<SavedGif> Load() =>
        new((Storage.Load<List<SavedGif>>(IndexFile) ?? [])
            .Where(g => File.Exists(g.Path))
            .OrderByDescending(g => g.LastUsedUtc));

    private static void Save()
    {
        try
        {
            Storage.Save(IndexFile, Items.ToList());
        }
        catch (IOException ex)
        {
            Log.Error("Saving GIF list", ex);
        }
    }
}
