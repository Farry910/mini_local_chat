using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MiniLocalChat.Controls;

/// <summary>
/// The text of a message: color emoji (<see cref="EmojiText"/>) and selectable like Teams. Drag to
/// select part of it, double-click a word, triple-click for all; Ctrl+C or right-click → Copy.
/// A text block draws it (light and fast, sizes to its text); the selection is drawn behind it.
/// </summary>
public sealed class MessageText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MessageText), new PropertyMetadata("", (d, _) => ((MessageText)d).Rebuild()));

    // Inherited by the text block inside.
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(MessageText));
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(MessageText));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(MessageText));
    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(MessageText));

    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x5B, 0x5F, 0xC7)));

    /// <summary>The message text that currently has a selection (only one at a time).</summary>
    private static MessageText? _selectionOwner;

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<TextAtom> _atoms = [];
    private List<Rect>? _rects;

    // Selection, as positions between atoms (0 … atom count).
    private int _anchor;
    private int _active;

    static MessageText()
    {
        FocusableProperty.OverrideMetadata(typeof(MessageText), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(MessageText), new FrameworkPropertyMetadata(null));
        CursorProperty.OverrideMetadata(typeof(MessageText), new FrameworkPropertyMetadata(Cursors.IBeam));
    }

    public MessageText()
    {
        AddLogicalChild(_text);
        AddVisualChild(_text);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            (_, e) => CopySelection(), (_, e) => e.CanExecute = HasSelection));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, _) => Select(0, _atoms.Count)));
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

    public bool HasSelection => _anchor != _active;

    /// <summary>The selected part of the text (emoji included), or "" if nothing is selected.</summary>
    public string SelectedText
    {
        get
        {
            var (start, end) = SelectionRange;
            var text = new StringBuilder();
            for (var i = start; i < end; i++)
            {
                text.Append(_atoms[i].Source);
            }

            return text.ToString();
        }
    }

    /// <summary>The text selected in this message, if any (for the message menu's Copy).</summary>
    public static string? SelectionIn(object? dataContext) =>
        _selectionOwner is { HasSelection: true } owner && ReferenceEquals(owner.DataContext, dataContext) ? owner.SelectedText : null;

    private (int Start, int End) SelectionRange => (Math.Min(_anchor, _active), Math.Max(_anchor, _active));

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _text;

    protected override System.Collections.IEnumerator LogicalChildren => new object[] { _text }.GetEnumerator();

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(availableSize);
        return _text.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _text.Arrange(new Rect(finalSize));
        _rects = null; // where each character is depends on the layout
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Transparent background so the gaps between words can be clicked and dragged across too.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (!HasSelection)
        {
            return;
        }

        // One highlight per line, behind the text.
        var rects = Rects();
        var (start, end) = SelectionRange;
        Rect? line = null;
        for (var i = start; i < end; i++)
        {
            var rect = rects[i];
            if (line is { } current && Math.Abs(current.Top - rect.Top) < 1)
            {
                line = Rect.Union(current, rect);
                continue;
            }

            if (line is { } done)
            {
                drawingContext.DrawRectangle(SelectionBrush, null, done);
            }

            line = rect;
        }

        if (line is { } last)
        {
            drawingContext.DrawRectangle(SelectionBrush, null, last);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_atoms.Count == 0)
        {
            return;
        }

        Focus();
        var index = IndexAt(e.GetPosition(this));
        switch (e.ClickCount)
        {
            case 2:
                SelectWordAt(index);
                break;
            case >= 3:
                Select(0, _atoms.Count);
                break;
            default:
                Select(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? _anchor : index, index);
                CaptureMouse();
                break;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            Select(_anchor, IndexAt(e.GetPosition(this)));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    // Clicking elsewhere clears the selection, but opening the message menu doesn't, so its Copy can use it.
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (e.NewFocus is not DependencyObject focus || !IsInMenu(focus))
        {
            Select(0, 0);
        }
    }

    private void Rebuild()
    {
        if (ReferenceEquals(_selectionOwner, this))
        {
            _selectionOwner = null;
        }

        _anchor = _active = 0;
        _atoms.Clear();
        _rects = null;
        EmojiText.Fill(_text, Text, _atoms);
        InvalidateVisual();
    }

    private void Select(int anchor, int active)
    {
        _anchor = Math.Clamp(anchor, 0, _atoms.Count);
        _active = Math.Clamp(active, 0, _atoms.Count);
        if (HasSelection)
        {
            if (_selectionOwner is { } other && !ReferenceEquals(other, this))
            {
                other.Select(0, 0);
            }

            _selectionOwner = this;
        }
        else if (ReferenceEquals(_selectionOwner, this))
        {
            _selectionOwner = null;
        }

        InvalidateVisual();
    }

    private void SelectWordAt(int index)
    {
        static bool IsWordPart(string source) => source.Length > 0 && (char.IsLetterOrDigit(source, 0) || source[0] == '_');

        var at = Math.Min(index, _atoms.Count - 1);
        if (!IsWordPart(_atoms[at].Source))
        {
            Select(at, at + 1); // a space, punctuation or emoji on its own
            return;
        }

        var start = at;
        while (start > 0 && IsWordPart(_atoms[start - 1].Source))
        {
            start--;
        }

        var end = at + 1;
        while (end < _atoms.Count && IsWordPart(_atoms[end].Source))
        {
            end++;
        }

        Select(start, end);
    }

    private void CopySelection()
    {
        if (!HasSelection)
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedText);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            Infrastructure.Log.Error("Copy", ex);
        }
    }

    /// <summary>Where each atom is drawn, in this control's coordinates.</summary>
    private List<Rect> Rects()
    {
        if (_rects is not null)
        {
            return _rects;
        }

        UpdateLayout();
        _rects = new List<Rect>(_atoms.Count);
        foreach (var atom in _atoms)
        {
            TextPointer start, end;
            if (atom.Inline is Run run)
            {
                start = run.ContentStart.GetPositionAtOffset(atom.Offset, LogicalDirection.Forward)!;
                end = run.ContentStart.GetPositionAtOffset(atom.Offset + atom.Length, LogicalDirection.Backward)!;
            }
            else
            {
                start = atom.Inline.ElementStart;
                end = atom.Inline.ElementEnd;
            }

            var a = start.GetCharacterRect(LogicalDirection.Forward);
            var b = end.GetCharacterRect(LogicalDirection.Backward);
            Rect rect;
            if (atom.Inline is LineBreak || a.IsEmpty || b.IsEmpty)
            {
                // Line ends: a small block after the last character of the line.
                rect = a.IsEmpty ? Rect.Empty : new Rect(a.Left, a.Top, 4, a.Height);
            }
            else if (Math.Abs(a.Top - b.Top) > 1 || b.Left < a.Left)
            {
                // Wrapped right at this character: keep it on the line where it starts.
                rect = new Rect(a.Left, a.Top, Math.Max(4, RenderSize.Width - a.Left), a.Height);
            }
            else
            {
                rect = new Rect(a.Left, Math.Min(a.Top, b.Top), b.Left - a.Left, Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top));
            }

            _rects.Add(rect);
        }

        return _rects;
    }

    /// <summary>The position between atoms nearest to a point.</summary>
    private int IndexAt(Point point)
    {
        var rects = Rects();
        if (rects.Count == 0)
        {
            return 0;
        }

        // The line: the one under the point, or the nearest.
        var lineIndex = -1;
        var lineDistance = double.MaxValue;
        for (var i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            if (rect.IsEmpty)
            {
                continue;
            }

            var distance = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
            if (distance < lineDistance)
            {
                lineDistance = distance;
                lineIndex = i;
            }
        }

        if (lineIndex < 0)
        {
            return 0;
        }

        var lineTop = rects[lineIndex].Top;
        var lastOnLine = -1;
        for (var i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            if (rect.IsEmpty || Math.Abs(rect.Top - lineTop) > 1)
            {
                continue;
            }

            if (_atoms[i].Inline is LineBreak || point.X < rect.Left + rect.Width / 2)
            {
                return i;
            }

            lastOnLine = i;
        }

        return lastOnLine + 1;
    }

    private static bool IsInMenu(DependencyObject element)
    {
        for (var node = element; node is not null;
             node = (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.ContextMenu or MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
