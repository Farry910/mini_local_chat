using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MiniLocalChat.Controls;

/// <summary>
/// One line of text with color emoji (<see cref="EmojiText"/>), cut with "…" when it doesn't fit.
/// WPF's own trimming leaves emoji pictures standing after the "…", so this cuts the text itself.
/// </summary>
public sealed class EmojiLabel : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmojiLabel), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure));

    // Inherited by the text block inside.
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(EmojiLabel));
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(EmojiLabel));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(EmojiLabel));
    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(EmojiLabel));
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(EmojiLabel));

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap };
    private string? _laidOutText;
    private double _laidOutWidth = -1;
    private double _laidOutFontSize;

    public EmojiLabel()
    {
        ClipToBounds = true;
        AddLogicalChild(_text);
        AddVisualChild(_text);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _text;

    protected override System.Collections.IEnumerator LogicalChildren => new object[] { _text }.GetEnumerator();

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = (Text ?? "").ReplaceLineEndings(" ");
        var width = Math.Floor(availableSize.Width);
        if (text != _laidOutText || width != _laidOutWidth || FontSize != _laidOutFontSize)
        {
            LayOut(text, width);
            _laidOutText = text;
            _laidOutWidth = width;
            _laidOutFontSize = FontSize;
        }

        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(Math.Min(_text.DesiredSize.Width, availableSize.Width), _text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _text.Arrange(new Rect(0, 0, Math.Max(finalSize.Width, _text.DesiredSize.Width), finalSize.Height));
        return finalSize;
    }

    /// <summary>The whole text if it fits; otherwise the longest start of it (whole emoji and letters) that fits with "…".</summary>
    private void LayOut(string text, double width)
    {
        if (Fits(text, width) || double.IsInfinity(width))
        {
            return;
        }

        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        var low = 0;
        var high = elements.Count - 1;
        var best = 0;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (Fits(Cut(elements, middle), width))
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        Fits(Cut(elements, best), width);
    }

    private static string Cut(List<string> elements, int count) => string.Concat(elements.Take(count)).TrimEnd() + "…";

    private bool Fits(string text, double width)
    {
        EmojiText.Fill(_text, text);
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _text.DesiredSize.Width <= width;
    }
}
