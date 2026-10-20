using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Services;

/// <summary>
/// Profile pictures: a square PNG up to 640×640 (big enough to view full size, like Telegram), named by
/// the hash of its bytes ("avatar id"). The id travels in every heartbeat and message; a computer that
/// doesn't have that picture yet asks its owner for it once.
/// </summary>
public static partial class AvatarStore
{
    /// <summary>Stored size of new pictures (older ones are 128 px).</summary>
    public const int PixelSize = 640;
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>Avatars in lists and chats are small: decode just this much (sharp up to ~56 px at 250 % DPI).</summary>
    private const int ThumbnailPixels = 160;

    private static readonly Dictionary<string, ImageSource> Cache = new();

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    public static string? PathFor(string? id)
    {
        if (!IsValidId(id))
        {
            return null;
        }

        var path = Path.Combine(AppPaths.AvatarsDir, id + ".png");
        return File.Exists(path) ? path : null;
    }

    public static ImageSource? ImageFor(string? id)
    {
        if (id is null || PathFor(id) is not { } path)
        {
            return null;
        }

        if (Cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = ThumbnailPixels;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return Cache[id] = bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The picture at full size, for the viewer (not cached).</summary>
    public static BitmapSource? FullImageFor(string? id)
    {
        if (PathFor(id) is not { } path)
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Turns any picture (png, jpg, gif, bmp…) into my avatar: center square, 128 px. Returns its id.</summary>
    public static string Import(string sourcePath)
    {
        BitmapSource source;
        using (var stream = File.OpenRead(sourcePath))
        {
            source = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        }

        var side = Math.Min(source.PixelWidth, source.PixelHeight);
        var square = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));
        var scale = Math.Min(1.0, (double)PixelSize / side); // never blow a small picture up
        BitmapSource scaled = scale < 1 ? new TransformedBitmap(square, new ScaleTransform(scale, scale)) : square;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var output = new MemoryStream();
        encoder.Save(output);
        var bytes = output.ToArray();

        var id = IdOf(bytes);
        File.WriteAllBytes(Path.Combine(AppPaths.AvatarsDir, id + ".png"), bytes);
        return id;
    }

    /// <summary>
    /// Keeps a received picture if it really is the one with this id (hash matches) and is a small image.
    /// The temporary file is removed either way.
    /// </summary>
    public static bool Accept(string receivedPath, string id)
    {
        try
        {
            var info = new FileInfo(receivedPath);
            if (!IsValidId(id) || !info.Exists || info.Length > MaxBytes)
            {
                return false;
            }

            var bytes = File.ReadAllBytes(receivedPath);
            if (IdOf(bytes) != id)
            {
                return false;
            }

            using (var stream = new MemoryStream(bytes))
            {
                // Throws if it isn't an image.
                _ = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }

            File.WriteAllBytes(Path.Combine(AppPaths.AvatarsDir, id + ".png"), bytes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or FileFormatException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            TryDeleteFolderOf(receivedPath);
        }
    }

    private static string IdOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();

    private static void TryDeleteFolderOf(string path)
    {
        try
        {
            if (Path.GetDirectoryName(path) is { } dir && dir.StartsWith(AppPaths.FilesDir, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex IdPattern();
}
