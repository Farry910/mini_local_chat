using System.Globalization;
using System.Net;
using System.Windows.Media;

namespace MiniLocalChat.Models;

/// <summary>Teams-style colored initials avatars, stable per person.</summary>
public static class AvatarPalette
{
    private static readonly Brush[] Brushes =
        new[] { "#C4314B", "#E3008C", "#8764B8", "#5B5FC7", "#0078D4", "#038387", "#498205", "#CA5010", "#986F0B", "#4F6BED", "#B146C2", "#00A2AD" }
            .Select(hex =>
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return (Brush)brush;
            })
            .ToArray();

    public static Brush For(string seed)
    {
        var hash = 0;
        foreach (var ch in seed)
        {
            hash = unchecked(hash * 31 + ch);
        }

        return Brushes[(hash & 0x7FFFFFFF) % Brushes.Length];
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

    // Text elements, so a name starting with an emoji doesn't get split mid-surrogate.
    private static string FirstLetter(string word) =>
        StringInfo.GetNextTextElement(word).ToUpper(CultureInfo.CurrentCulture);
}
