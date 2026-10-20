using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace MiniLocalChat.Controls;

/// <summary>One piece of displayed text and the inline that shows it (for selecting and copying).</summary>
public readonly record struct TextAtom(Inline Inline, int Offset, int Length, string Source);

/// <summary>
/// Color emoji. WPF draws font emoji in black and white, so emoji become small pictures instead:
/// Emoji.Wpf draws each one once from the Segoe UI Emoji color layers, and the picture is reused.
/// In XAML, use <c>c:EmojiText.Text="{Binding …}"</c> on a TextBlock instead of <c>Text</c>.
/// </summary>
public static class EmojiText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(EmojiText),
        new PropertyMetadata(null, (d, e) =>
        {
            if (d is TextBlock textBlock)
            {
                Fill(textBlock, (string?)e.NewValue);
            }
        }));

    // Emoji pictures have padding around the glyph, so they're drawn larger than the font
    // and the extra is taken back with negative margins: the line height stays the same.
    private const double Height = 1.7;
    private const double Width = 1.75;

    private static readonly Dictionary<string, ImageSource?> Images = new();
    private static readonly IValueConverter HeightOf = new EmSize(Height);
    private static readonly IValueConverter WidthOf = new EmSize(Width);
    private static readonly IValueConverter MarginOf = new EmMargin();

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    /// <summary>Shows <paramref name="text"/> in the text block with color emoji. Optionally lists every character / emoji shown.</summary>
    public static void Fill(TextBlock textBlock, string? text, List<TextAtom>? atoms = null)
    {
        var inlines = textBlock.Inlines;
        inlines.Clear();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var pending = new StringBuilder();
        var pendingAtoms = new List<(int Offset, int Length, string Source)>();

        void FlushRun()
        {
            if (pending.Length == 0)
            {
                return;
            }

            var run = new Run(pending.ToString());
            inlines.Add(run);
            foreach (var (offset, length, source) in pendingAtoms)
            {
                atoms?.Add(new TextAtom(run, offset, length, source));
            }

            pending.Clear();
            pendingAtoms.Clear();
        }

        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element is "\n" or "\r\n" or "\r")
            {
                FlushRun();
                var lineBreak = new LineBreak();
                inlines.Add(lineBreak);
                atoms?.Add(new TextAtom(lineBreak, 0, 0, Environment.NewLine));
            }
            else if (ImageFor(element) is { } image)
            {
                FlushRun();
                var emoji = Picture(image, textBlock);
                inlines.Add(emoji);
                atoms?.Add(new TextAtom(emoji, 0, 0, element));
            }
            else
            {
                pendingAtoms.Add((pending.Length, element.Length, element));
                pending.Append(element);
            }
        }

        FlushRun();
    }

    /// <summary>The color picture for one emoji (a single text element), or null if it isn't one.</summary>
    public static ImageSource? ImageFor(string element)
    {
        // Fast path: plain letters, digits and punctuation.
        if (element.Length == 1 && element[0] < 0x2000)
        {
            return null;
        }

        if (Images.TryGetValue(element, out var cached))
        {
            return cached;
        }

        ImageSource? source = null;
        try
        {
            if (KnownForm(element) is { } known)
            {
                var image = new Image();
                Emoji.Wpf.Image.SetSource(image, known);
                source = image.Source;
                if (source is { CanFreeze: true })
                {
                    source.Freeze();
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Error("Emoji", ex);
        }

        Images[element] = source;
        return source;
    }

    // "❤" and "❤️" (with the emoji variation selector) are the same emoji.
    private static string? KnownForm(string element)
    {
        var lookup = Emoji.Wpf.EmojiData.LookupByText;
        if (lookup.ContainsKey(element))
        {
            return element;
        }

        if (element.EndsWith('️'))
        {
            var bare = element[..^1];
            return lookup.ContainsKey(bare) ? bare : null;
        }

        var qualified = element + "️";
        return lookup.ContainsKey(qualified) ? qualified : null;
    }

    private static InlineUIContainer Picture(ImageSource source, TextBlock owner)
    {
        var image = new Image { Source = source, Stretch = Stretch.Uniform };
        image.SetBinding(FrameworkElement.HeightProperty, new Binding(nameof(TextBlock.FontSize)) { Source = owner, Converter = HeightOf });
        image.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(TextBlock.FontSize)) { Source = owner, Converter = WidthOf });
        image.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(TextBlock.FontSize)) { Source = owner, Converter = MarginOf });
        return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Baseline };
    }

    private sealed class EmSize(double ems) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double size ? Math.Round(size * ems) : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class EmMargin : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double size
                ? new Thickness(-0.17 * size, -0.5 * size, -0.17 * size, -0.25 * size)
                : new Thickness();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
