using System.Windows;
using System.Windows.Media.Animation;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>The card shown while an alarm rings: time, label, Snooze 5 min, Dismiss. Top center of the screen.</summary>
public partial class AlarmRingWindow : Window
{
    public AlarmRingWindow(Alarm alarm, int stackIndex)
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        Alarm = alarm;
        Title = $"Alarm – {alarm.Title}";
        TimeText.Text = alarm.TimeText;
        LabelText.Text = alarm.Title;
        Color1Strip.Background = alarm.Brush1;
        Color2Strip.Background = alarm.Brush2;

        // Top center of the main screen; several ringing alarms stack downwards.
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + 24 + stackIndex * (ActualHeight + 8);
        };

        // The bell swings while ringing.
        BellSwing.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, new DoubleAnimation(-14, 14, TimeSpan.FromMilliseconds(180))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    public event Action? Snoozed;

    public event Action? Dismissed;

    public Alarm Alarm { get; }

    private void OnSnooze(object sender, RoutedEventArgs e) => Snoozed?.Invoke();

    private void OnDismiss(object sender, RoutedEventArgs e) => Dismissed?.Invoke();
}
