using System.Windows;
using System.Windows.Controls;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>Create or edit a countdown timer: duration (counter-style), label, wave colors, sound.</summary>
public partial class TimerEditWindow : Window
{
    private static readonly int[] PresetMinutes = [1, 3, 5, 10, 15, 25, 30, 60];

    private readonly TimerItem _timer;

    private TimerEditWindow(TimerItem timer, bool isNew)
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        _timer = timer;
        Heading.Text = isNew ? "New timer" : "Edit timer";
        Title = Heading.Text;

        var duration = timer.Duration;
        HoursSpinner.Value = (int)duration.TotalHours;
        MinutesSpinner.Value = duration.Minutes;
        SecondsSpinner.Value = duration.Seconds;
        LabelBox.Text = timer.Label;

        foreach (var minutes in PresetMinutes)
        {
            var preset = new Button
            {
                Style = (Style)FindResource("Preset"),
                Content = minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h",
                Tag = minutes,
            };
            preset.Click += (_, _) =>
            {
                HoursSpinner.Value = minutes / 60;
                MinutesSpinner.Value = minutes % 60;
                SecondsSpinner.Value = 0;
            };
            Presets.Children.Add(preset);
        }

        Color1List.ItemsSource = AvatarPalette.Choices;
        Color2List.ItemsSource = AvatarPalette.Choices;
        Color1List.SelectedItem = AvatarPalette.NormalizeHex(timer.Color1) ?? AvatarPalette.Choices[11];
        Color2List.SelectedItem = AvatarPalette.NormalizeHex(timer.Color2) ?? AvatarPalette.Choices[6];
        SoundBox.IsChecked = timer.Sound;
    }

    private bool StartRequested { get; set; }

    /// <summary>Shows the editor. Returns null if cancelled, otherwise whether the user chose Start.</summary>
    public static bool? Edit(Window? owner, TimerItem timer, bool isNew)
    {
        var window = new TimerEditWindow(timer, isNew);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return DialogGuard.Run(() => window.ShowDialog()) == true ? window.StartRequested : null;
    }

    private void OnSave(object sender, RoutedEventArgs e) => Finish(start: false);

    private void OnStart(object sender, RoutedEventArgs e) => Finish(start: true);

    private void Finish(bool start)
    {
        var seconds = HoursSpinner.Value * 3600 + MinutesSpinner.Value * 60 + SecondsSpinner.Value;
        if (seconds <= 0)
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _timer.DurationSeconds = seconds;
        _timer.Label = LabelBox.Text.Trim();
        _timer.Color1 = (string)Color1List.SelectedItem;
        _timer.Color2 = (string)Color2List.SelectedItem;
        _timer.Sound = SoundBox.IsChecked == true;
        StartRequested = start;
        DialogResult = true;
    }
}
