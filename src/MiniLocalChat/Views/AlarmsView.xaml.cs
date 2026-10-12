using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

/// <summary>The Alarms page of the main window (separate from chat).</summary>
public partial class AlarmsView : UserControl
{
    private readonly AlarmService _alarms = App.Alarms;

    public AlarmsView()
    {
        InitializeComponent();
        AlarmList.ItemsSource = _alarms.Alarms;
        _alarms.Alarms.CollectionChanged += OnAlarmsChanged;
        foreach (var alarm in _alarms.Alarms)
        {
            alarm.PropertyChanged += OnAlarmChanged;
        }

        UpdateSummary();
    }

    private void OnAlarmsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var alarm in e.NewItems?.OfType<Alarm>() ?? [])
        {
            alarm.PropertyChanged += OnAlarmChanged;
        }

        UpdateSummary();
    }

    private void OnAlarmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Alarm.NextRing) or nameof(Alarm.Enabled) or nameof(Alarm.NextText))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        EmptyState.Visibility = _alarms.Alarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NextSummary.Text = _alarms.Next is { } next
            ? $"Next alarm: {next.NextText}" + (string.IsNullOrWhiteSpace(next.Label) ? "" : $" · {next.Label}")
            : _alarms.Alarms.Count == 0 ? "Wave your screen at a set time" : "No alarms are on";
    }

    private void OnNewAlarm(object sender, RoutedEventArgs e)
    {
        var colors = App.Chat.Me;
        var alarm = new Alarm { Color1 = colors.EffectiveColor1, Color2 = colors.EffectiveColor2 };
        if (AlarmEditWindow.Edit(Window.GetWindow(this), alarm, isNew: true))
        {
            _alarms.Save(alarm);
        }
    }

    private void OnEditAlarm(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Alarm alarm } && AlarmEditWindow.Edit(Window.GetWindow(this), alarm, isNew: false))
        {
            _alarms.Save(alarm);
        }
    }

    private void OnToggleAlarm(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: Alarm alarm } box)
        {
            _alarms.SetEnabled(alarm, box.IsChecked == true);
            e.Handled = true;
        }
    }

    private void OnDeleteAlarm(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Alarm alarm })
        {
            e.Handled = true;
            var answer = DialogGuard.Run(() => MessageBox.Show(Window.GetWindow(this)!, $"Delete the {alarm.TimeText} alarm “{alarm.Title}”?",
                "Delete alarm", MessageBoxButton.OKCancel, MessageBoxImage.Question));
            if (answer == MessageBoxResult.OK)
            {
                _alarms.Delete(alarm);
            }
        }
    }
}
