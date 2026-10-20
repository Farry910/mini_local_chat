using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MiniLocalChat.Infrastructure;
using SkiaSharp.Skottie;

namespace MiniLocalChat.Services;

public sealed class SavedSticker
{
    public string Hash { get; set; } = "";

    /// <summary>The file name it had when it was added, for the filter box.</summary>
    public string Name { get; set; } = "";

    public DateTime AddedUtc { get; set; }

    /// <summary>Ships with the app: always listed, can't be removed.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    [JsonIgnore]
    public bool CanRemove => !IsBuiltIn;

    public string Path => System.IO.Path.Combine(AppPaths.StickersDir, Hash + ".tgs");
}

/// <summary>
/// Telegram animated stickers (.tgs files: gzipped Lottie animations), like Telegram's sticker panel.
/// The built-in ones come first; stickers I send or save from a chat (or add from disk) follow, newest first.
/// Files live in a content-addressed folder and are never deleted, since messages point at them;
/// removing a sticker only takes it off this list.
/// </summary>
public static class StickerLibrary
{
    /// <summary>Telegram's limit is 64 KB; anything much bigger isn't a sticker.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private const string IndexFile = "stickers.json";
    private const int MaxDecompressedBytes = 16 * 1024 * 1024;
    private const int MaxItems = 300;
    private static readonly Storage Storage = new();
    private static ObservableCollection<SavedSticker>? _items;

    public static ObservableCollection<SavedSticker> Items => _items ??= Load();

    public static bool IsTgs(string? path) =>
        string.Equals(System.IO.Path.GetExtension(path), ".tgs", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a .tgs file into a Lottie animation, or null if it isn't a valid sticker.</summary>
    public static Animation? Open(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxFileBytes)
            {
                return null;
            }

            using var gzip = new GZipStream(file.OpenRead(), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer)) > 0)
            {
                if (json.Length + read > MaxDecompressedBytes)
                {
                    return null;
                }

                json.Write(buffer, 0, read);
            }

            return Animation.TryParse(System.Text.Encoding.UTF8.GetString(json.GetBuffer(), 0, (int)json.Length), out var animation)
                ? animation
                : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsValid(string path)
    {
        using var animation = Open(path);
        return animation is not null;
    }

    /// <summary>Copies a sticker into the store (once per content) and returns the stored path.</summary>
    public static string Store(string path) => StoreBytes(File.ReadAllBytes(path));

    /// <summary>Saves a valid sticker to the list (or moves it to the front of mine). Returns the stored path, or null if it isn't a sticker.</summary>
    public static string? Add(string path)
    {
        if (!IsValid(path))
        {
            return null;
        }

        var stored = Store(path);
        var hash = System.IO.Path.GetFileNameWithoutExtension(stored);
        if (Items.Any(s => s.IsBuiltIn && s.Hash == hash))
        {
            return stored;
        }

        var existing = Items.FirstOrDefault(s => s.Hash == hash);
        if (existing is not null)
        {
            Items.Remove(existing);
        }

        Items.Insert(Items.Count(s => s.IsBuiltIn), new SavedSticker
        {
            Hash = hash,
            Name = existing?.Name ?? System.IO.Path.GetFileNameWithoutExtension(path),
            AddedUtc = DateTime.UtcNow,
        });
        while (Items.Count > MaxItems && !Items[^1].IsBuiltIn)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        Save();
        return stored;
    }

    public static bool Contains(string? path) =>
        path is not null && File.Exists(path) && HashOf(path) is { } hash && Items.Any(s => s.Hash == hash);

    public static void Remove(SavedSticker sticker)
    {
        if (sticker.IsBuiltIn)
        {
            return;
        }

        Items.Remove(sticker);
        Save();
    }

    private static string StoreBytes(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        var stored = System.IO.Path.Combine(AppPaths.StickersDir, hash + ".tgs");
        if (!File.Exists(stored))
        {
            Directory.CreateDirectory(AppPaths.StickersDir);
            File.WriteAllBytes(stored, bytes);
        }

        return stored;
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

    private static ObservableCollection<SavedSticker> Load()
    {
        var items = new ObservableCollection<SavedSticker>(BuiltIn());
        foreach (var sticker in (Storage.Load<List<SavedSticker>>(IndexFile) ?? [])
                 .Where(s => File.Exists(s.Path) && items.All(b => b.Hash != s.Hash))
                 .OrderByDescending(s => s.AddedUtc))
        {
            items.Add(sticker);
        }

        return items;
    }

    /// <summary>The stickers that ship inside the exe (Assets\Stickers), unpacked into the store.</summary>
    private static IEnumerable<SavedSticker> BuiltIn()
    {
        var assembly = typeof(StickerLibrary).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Sticker.", StringComparison.Ordinal)).Order())
        {
            SavedSticker? sticker = null;
            try
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var stored = StoreBytes(memory.ToArray());
                sticker = new SavedSticker
                {
                    Hash = System.IO.Path.GetFileNameWithoutExtension(stored),
                    Name = System.IO.Path.GetFileNameWithoutExtension(resource["Sticker.".Length..]).Replace('-', ' '),
                    IsBuiltIn = true,
                };
            }
            catch (IOException ex)
            {
                Log.Error($"Unpacking sticker {resource}", ex);
            }

            if (sticker is not null)
            {
                yield return sticker;
            }
        }
    }

    private static void Save()
    {
        try
        {
            Storage.Save(IndexFile, Items.Where(s => !s.IsBuiltIn).ToList());
        }
        catch (IOException ex)
        {
            Log.Error("Saving sticker list", ex);
        }
    }
}
