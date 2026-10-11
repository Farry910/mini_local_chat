using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace MiniLocalChat.Models;

/// <summary>
/// Avatar colors. Each person picks two in Settings (fill + ring, also the colors of their screen ripple);
/// until they do, both are derived from their IP.
/// </summary>
public static partial class AvatarPalette
{
    /// <summary>The swatches offered in Settings. Repeats between people are fine.</summary>
    public static readonly string[] Choices =
    [
        "#C4314B", "#E81123", "#CA5010", "#FFB900", "#986F0B", "#498205", "#22C55E", "#038387",
        "#00A2AD", "#0078D4", "#4F6BED", "#5B5FC7", "#8764B8", "#B146C2", "#E3008C", "#69797E",
    ];

    private static readonly string[] Defaults =
        ["#C4314B", "#E3008C", "#8764B8", "#5B5FC7", "#0078D4", "#038387", "#498205", "#CA5010", "#986F0B", "#4F6BED", "#B146C2", "#00A2AD"];

    private static readonly Dictionary<string, Brush> BrushCache = new();

    public static string DefaultColor1(string seed) => Defaults[Index(seed)];

    /// <summary>A second default that always differs from the first.</summary>
    public static string DefaultColor2(string seed)
    {
        var first = Index(seed);
        var offset = 1 + (int)((uint)Hash(seed) / (uint)Defaults.Length % (uint)(Defaults.Length - 1));
        return Defaults[(first + offset) % Defaults.Length];
    }

    /// <summary>"#RRGGBB" (upper case) or null if the text isn't a color.</summary>
    public static string? NormalizeHex(string? hex) =>
        hex is not null && HexPattern().IsMatch(hex) ? hex.ToUpperInvariant() : null;

    public static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Brush ToBrush(string hex)
    {
        if (!BrushCache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush(ToColor(hex));
            brush.Freeze();
            BrushCache[hex] = brush;
        }

        return brush;
    }

    public static string Initials(string name)
    {
        if (IPAddress.TryParse(name, out _))
        {
            return name[(name.LastIndexOf('.') + 1)..];
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "?",
            1 => FirstLetter(words[0]),
            _ => FirstLetter(words[0]) + FirstLetter(words[^1]),
        };
    }

    private static int Hash(string seed)
    {
        var hash = 0;
        foreach (var ch in seed)
        {
            hash = unchecked(hash * 31 + ch);
        }

        return hash & 0x7FFFFFFF;
    }

    private static int Index(string seed) => Hash(seed) % Defaults.Length;

    // Text elements, so a name starting with an emoji doesn't get split mid-surrogate.
    private static string FirstLetter(string word) =>
        StringInfo.GetNextTextElement(word).ToUpper(CultureInfo.CurrentCulture);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();
}
