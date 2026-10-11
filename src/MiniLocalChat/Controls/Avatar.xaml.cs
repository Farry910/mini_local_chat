using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiniLocalChat.Controls;

/// <summary>Round initials avatar with an optional presence dot; shows a people glyph for groups.</summary>
public partial class Avatar : UserControl
{
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), typeof(string), "");
    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), typeof(Brush), null);
    public static readonly DependencyProperty IsGroupProperty = Register(nameof(IsGroup), typeof(bool), false);
    public static readonly DependencyProperty ShowPresenceProperty = Register(nameof(ShowPresence), typeof(bool), false);
    public static readonly DependencyProperty IsOnlineProperty = Register(nameof(IsOnline), typeof(bool), false);

    private static readonly Brush OnlineBrush = Frozen(Color.FromRgb(0x6B, 0xB7, 0x00));
    private static readonly Brush OfflineBrush = Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD));

    public Avatar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateSize();
        UpdateVisuals();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool IsGroup
    {
        get => (bool)GetValue(IsGroupProperty);
        set => SetValue(IsGroupProperty, value);
    }

    public bool ShowPresence
    {
        get => (bool)GetValue(ShowPresenceProperty);
        set => SetValue(ShowPresenceProperty, value);
    }

    public bool IsOnline
    {
        get => (bool)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(Avatar),
            new PropertyMetadata(defaultValue, (d, _) => ((Avatar)d).UpdateVisuals()));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void UpdateVisuals()
    {
        Circle.Fill = Fill ?? OfflineBrush;
        InitialsText.Text = Text;
        InitialsText.Visibility = IsGroup ? Visibility.Collapsed : Visibility.Visible;
        GroupGlyph.Visibility = IsGroup ? Visibility.Visible : Visibility.Collapsed;
        PresenceDot.Visibility = ShowPresence ? Visibility.Visible : Visibility.Collapsed;
        PresenceDot.Fill = IsOnline ? OnlineBrush : OfflineBrush;
        PresenceDot.ToolTip = IsOnline ? "Online" : "Offline";
    }

    private void UpdateSize()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        InitialsText.FontSize = Math.Max(8, size * 0.36);
        GroupGlyph.FontSize = Math.Max(8, size * 0.45);
        PresenceDot.Width = PresenceDot.Height = Math.Max(9, size * 0.32);
    }
}
