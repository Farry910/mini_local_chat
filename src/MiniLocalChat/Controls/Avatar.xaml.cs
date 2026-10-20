using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniLocalChat.Models;
using MiniLocalChat.Views;

namespace MiniLocalChat.Controls;

/// <summary>
/// Round initials avatar with an optional presence dot (green online, amber away, gray offline); shows a
/// people glyph for groups. With <see cref="Person"/> set, clicking a profile picture opens it full size.
/// </summary>
public partial class Avatar : UserControl
{
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), typeof(string), "");
    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), typeof(Brush), null);
    public static readonly DependencyProperty ImageProperty = Register(nameof(Image), typeof(ImageSource), null);
    public static readonly DependencyProperty IsGroupProperty = Register(nameof(IsGroup), typeof(bool), false);
    public static readonly DependencyProperty ShowPresenceProperty = Register(nameof(ShowPresence), typeof(bool), false);
    public static readonly DependencyProperty IsOnlineProperty = Register(nameof(IsOnline), typeof(bool), false);
    public static readonly DependencyProperty IsAwayProperty = Register(nameof(IsAway), typeof(bool), false);
    public static readonly DependencyProperty PersonProperty = Register(nameof(Person), typeof(Contact), null);

    private static readonly Brush OnlineBrush = Frozen(Color.FromRgb(0x6B, 0xB7, 0x00));
    private static readonly Brush OfflineBrush = Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD));
    private static readonly Brush AwayBrush = Frozen(Color.FromRgb(0xF2, 0xA9, 0x00));

    public Avatar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateSize();
        MouseLeftButtonUp += OnClick;
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

    /// <summary>Profile picture; replaces the initials and the fill color.</summary>
    public ImageSource? Image
    {
        get => (ImageSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
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

    /// <summary>Online but away (amber dot).</summary>
    public bool IsAway
    {
        get => (bool)GetValue(IsAwayProperty);
        set => SetValue(IsAwayProperty, value);
    }

    /// <summary>Whose avatar this is: clicking their picture opens it full size, like Telegram.</summary>
    public Contact? Person
    {
        get => (Contact?)GetValue(PersonProperty);
        set => SetValue(PersonProperty, value);
    }

    private bool CanView => !IsGroup && Image is not null && Person is not null;

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (CanView)
        {
            e.Handled = true;
            AvatarViewer.Show(Window.GetWindow(this), Person!);
        }
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
        var picture = IsGroup ? null : Image;
        if (picture is not null)
        {
            var brush = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            Circle.Fill = brush;
        }
        else
        {
            Circle.Fill = Fill ?? OfflineBrush;
        }

        InitialsText.Text = Text;
        InitialsText.Visibility = IsGroup || picture is not null ? Visibility.Collapsed : Visibility.Visible;
        GroupGlyph.Visibility = IsGroup ? Visibility.Visible : Visibility.Collapsed;
        PresenceDot.Visibility = ShowPresence ? Visibility.Visible : Visibility.Collapsed;
        PresenceDot.Fill = !IsOnline ? OfflineBrush : IsAway ? AwayBrush : OnlineBrush;
        PresenceDot.ToolTip = !IsOnline ? "Offline" : IsAway ? "Away" : "Online";
        Cursor = CanView ? Cursors.Hand : null;
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
