using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Services;

public sealed record EmojiSticker(string Name, string Emoji)
{
    public string Path => System.IO.Path.Combine(EmojiStickers.Folder, Name + ".gif");

    /// <summary>What the filter box matches: the name ("thumbs-up") or the emoji itself.</summary>
    public string Keywords => Name.Replace('-', ' ') + " " + Emoji;
}

/// <summary>
/// Built-in animated emoji stickers: Google's Noto Animated Emoji (CC BY 4.0), shrunk to 128 px.
/// They ship inside the exe and are unpacked into the files folder on first use.
/// </summary>
public static class EmojiStickers
{
    public const string Credit = "Animated emoji: Noto Emoji by Google, CC BY 4.0";
    public const string CreditUrl = "https://googlefonts.github.io/noto-emoji-animation/";

    // Must match tools\fetch-emoji.ps1 (the GIF file names).
    public static readonly IReadOnlyList<EmojiSticker> All =
    [
        new("grinning", "😀"), new("joy", "😂"), new("rofl", "🤣"), new("heart-eyes", "😍"),
        new("kiss", "😘"), new("cool", "😎"), new("thinking", "🤔"), new("sob", "😭"),
        new("angry", "😡"), new("scream", "😱"), new("party", "🥳"), new("sleeping", "😴"),
        new("mind-blown", "🤯"), new("eye-roll", "🙄"), new("halo", "😇"), new("hearts", "🥰"),
        new("thumbs-up", "👍"), new("clap", "👏"), new("pray", "🙏"), new("wave", "👋"),
        new("heart", "❤️"), new("fire", "🔥"), new("tada", "🎉"), new("hundred", "💯"),
    ];

    private static bool _unpacked;

    public static string Folder => System.IO.Path.Combine(AppPaths.GifsDir, "emoji");

    /// <summary>The stickers whose files are available (unpacking them the first time).</summary>
    public static IReadOnlyList<EmojiSticker> Available()
    {
        Unpack();
        return All.Where(s => File.Exists(s.Path)).ToList();
    }

    public static bool IsSticker(string? path) =>
        path is not null && string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)), System.IO.Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase);

    private static void Unpack()
    {
        if (_unpacked)
        {
            return;
        }

        _unpacked = true;
        Directory.CreateDirectory(Folder);
        var assembly = typeof(EmojiStickers).Assembly;
        foreach (var sticker in All)
        {
            using var resource = assembly.GetManifestResourceStream($"Emoji.{sticker.Name}.gif");
            if (resource is null || (File.Exists(sticker.Path) && new FileInfo(sticker.Path).Length == resource.Length))
            {
                continue;
            }

            try
            {
                using var file = File.Create(sticker.Path);
                resource.CopyTo(file);
            }
            catch (IOException ex)
            {
                Log.Error($"Unpacking sticker {sticker.Name}", ex);
            }
        }
    }
}
