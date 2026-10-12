using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>Create or edit an alarm: time, label, repeat days, wave colors, sound.</summary>
public partial class AlarmEditWindow : Window
{
    private readonly Alarm _alarm;
    private readonly Dictionary<DayOfWeek, ToggleButton> _days = new();

    private AlarmEditWindow(Alarm alarm, bool isNew)
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        _alarm = alarm;
        Heading.Text = isNew ? "New alarm" : "Edit alarm";
        Title = Heading.Text;
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToList();
        MinuteBox.ItemsSource = Enumerable.Range(0, 60).Select(m => m.ToString("00")).ToList();
        HourBox.SelectedIndex = alarm.Hour;
        MinuteBox.SelectedIndex = alarm.Minute;
        HourBox.SelectionChanged += (_, _) => UpdateHints();
        MinuteBox.SelectionChanged += (_, _) => UpdateHints();
        LabelBox.Text = alarm.Label;

        var names = CultureInfo.CurrentCulture.DateTimeFormat;
        foreach (var day in Alarm.MondayFirst())
        {
            var toggle = new ToggleButton
            {
                Style = (Style)FindResource("DayToggle"),
                Content = names.GetShortestDayName(day),
                ToolTip = names.GetDayName(day),
                IsChecked = alarm.Days.Contains(day),
            };
            toggle.Click += (_, _) => UpdateHints();
            _days[day] = toggle;
            DayPanel.Children.Add(toggle);
        }

        Color1List.ItemsSource = AvatarPalette.Choices;
        Color2List.ItemsSource = AvatarPalette.Choices;
        Color1List.SelectedItem = AvatarPalette.NormalizeHex(alarm.Color1) ?? AvatarPalette.Choices[11];
        Color2List.SelectedItem = AvatarPalette.NormalizeHex(alarm.Color2) ?? AvatarPalette.Choices[6];
        SoundBox.IsChecked = alarm.Sound;
        PreviewButton.Visibility = App.Current.Ripples is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateHints();
    }

    public bool Deleted { get; private set; }

    /// <summary>Shows the editor; true if the alarm was saved (the alarm object is updated).</summary>
    public static bool Edit(Window? owner, Alarm alarm, bool isNew)
    {
        var window = new AlarmEditWindow(alarm, isNew);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var saved = DialogGuard.Run(() => window.ShowDialog()) == true;
        if (window.Deleted)
        {
            App.Alarms.Delete(alarm);
            return false;
        }

        return saved;
    }

    private List<DayOfWeek> SelectedDays => _days.Where(d => d.Value.IsChecked == true).Select(d => d.Key).ToList();

    private void UpdateHints()
    {
        var time = DateTime.Today.AddHours(HourBox.SelectedIndex).AddMinutes(MinuteBox.SelectedIndex);
        TimePreview.Text = time.ToString("t", CultureInfo.CurrentCulture);
        RepeatHint.Text = SelectedDays.Count == 0
            ? $"Rings once, {(time > DateTime.Now ? "today" : "tomorrow")}. Pick days to repeat it."
            : "Rings on the selected days.";
    }

    private void OnPreview(object sender, RoutedEventArgs e) =>
        App.Current.Ripples?.Play((string)Color1List.SelectedItem, (string)Color2List.SelectedItem, fromScreenCenter: true);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _alarm.Hour = HourBox.SelectedIndex;
        _alarm.Minute = MinuteBox.SelectedIndex;
        _alarm.Label = LabelBox.Text.Trim();
        _alarm.Days = SelectedDays;
        _alarm.Color1 = (string)Color1List.SelectedItem;
        _alarm.Color2 = (string)Color2List.SelectedItem;
        _alarm.Sound = SoundBox.IsChecked == true;
        _alarm.Enabled = true;
        DialogResult = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        Deleted = true;
        DialogResult = false;
    }
}
