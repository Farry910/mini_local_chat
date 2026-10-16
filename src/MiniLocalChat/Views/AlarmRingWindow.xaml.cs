using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>The card shown while an alarm or timer rings. Top center of the screen; several stack downwards.</summary>
public partial class AlarmRingWindow : Window
{
    public AlarmRingWindow(RingRequest request, int stackIndex)
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        Request = request;
        Title = $"{request.Heading} – {request.Title}";
        TimeText.Text = request.Heading;
        LabelText.Text = request.Title;
        SecondaryButton.Content = request.SecondaryLabel;
        Color1Strip.Background = AvatarPalette.ToBrush(AvatarPalette.NormalizeHex(request.Color1) ?? "#5B5FC7");
        Color2Strip.Background = AvatarPalette.ToBrush(AvatarPalette.NormalizeHex(request.Color2) ?? "#22C55E");

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 24 + stackIndex * (ActualHeight + 8);
        };

        // The bell swings while ringing.
        var swing = new DoubleAnimation(-14, 14, TimeSpan.FromMilliseconds(180)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(swing, 30);
        BellSwing.BeginAnimation(RotateTransform.AngleProperty, swing);
    }

    public event Action? SecondaryClicked;

    public event Action? Dismissed;

    public RingRequest Request { get; }

    private void OnSecondary(object sender, RoutedEventArgs e) => SecondaryClicked?.Invoke();

    private void OnDismiss(object sender, RoutedEventArgs e) => Dismissed?.Invoke();
}
