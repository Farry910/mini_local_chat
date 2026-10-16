using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

/// <summary>The Alarms page of the main window (separate from chat): alarms and countdown timers.</summary>
public partial class AlarmsView : UserControl
{
    private readonly AlarmService _alarms = App.Alarms;
    private readonly TimerService _timers = App.Timers;

    public AlarmsView()
    {
        InitializeComponent();
        AlarmList.ItemsSource = _alarms.Alarms;
        TimerList.ItemsSource = _timers.Timers;
        _alarms.Alarms.CollectionChanged += OnItemsChanged;
        _timers.Timers.CollectionChanged += OnItemsChanged;
        foreach (var alarm in _alarms.Alarms)
        {
            alarm.PropertyChanged += OnItemChanged;
        }

        foreach (var timer in _timers.Timers)
        {
            timer.PropertyChanged += OnItemChanged;
        }

        UpdatePage();
    }

    private bool ShowingTimers => TimersTab.IsChecked == true;

    /// <summary>Opens the Timers tab (e.g. from the tray).</summary>
    public void ShowTimers() => TimersTab.IsChecked = true;

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdatePage();
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<INotifyPropertyChanged>() ?? [])
        {
            item.PropertyChanged += OnItemChanged;
        }

        UpdatePage();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Alarm.NextRing) or nameof(Alarm.Enabled) or nameof(Alarm.NextText)
            or nameof(TimerItem.State) or nameof(TimerItem.StateText))
        {
            UpdatePage();
        }
    }

    private void UpdatePage()
    {
        var timers = ShowingTimers;
        AlarmsPanel.Visibility = timers ? Visibility.Collapsed : Visibility.Visible;
        TimersPanel.Visibility = timers ? Visibility.Visible : Visibility.Collapsed;
        NewButtonText.Text = timers ? "New timer" : "New alarm";

        if (timers)
        {
            var running = _timers.RunningCount;
            NextSummary.Text = _timers.Timers.Count == 0 ? "Count down from a set time"
                : running == 0 ? "No timers running"
                : running == 1 ? "1 timer running" : $"{running} timers running";
            EmptyState.Visibility = _timers.Timers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyGlyph.Text = "";
            EmptyTitle.Text = "No timers yet";
            EmptyText.Text = "Set a duration and start it. When it reaches zero, your screen waves in its colors until you restart or dismiss it.";
        }
        else
        {
            NextSummary.Text = _alarms.Next is { } next
                ? $"Next alarm: {next.NextText}" + (string.IsNullOrWhiteSpace(next.Label) ? "" : $" · {next.Label}")
                : _alarms.Alarms.Count == 0 ? "Wave your screen at a set time" : "No alarms are on";
            EmptyState.Visibility = _alarms.Alarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyGlyph.Text = "";
            EmptyTitle.Text = "No alarms yet";
            EmptyText.Text = "When an alarm rings, your screen waves in its colors until you snooze or dismiss it. Alarms ring while Mini Local Chat is running, also from the tray.";
        }
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        var me = App.Chat.Me;
        if (ShowingTimers)
        {
            var timer = new TimerItem { Color1 = me.EffectiveColor1, Color2 = me.EffectiveColor2 };
            if (TimerEditWindow.Edit(Window.GetWindow(this), timer, isNew: true) is { } start)
            {
                _timers.Save(timer);
                if (start)
                {
                    _timers.Start(timer);
                }
            }

            return;
        }

        var alarm = new Alarm { Color1 = me.EffectiveColor1, Color2 = me.EffectiveColor2 };
        if (AlarmEditWindow.Edit(Window.GetWindow(this), alarm, isNew: true))
        {
            _alarms.Save(alarm);
        }
    }

    // ---- Alarms ---------------------------------------------------------------------------------

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
            if (Confirm($"Delete the {alarm.TimeText} alarm “{alarm.Title}”?", "Delete alarm"))
            {
                _alarms.Delete(alarm);
            }
        }
    }

    // ---- Timers ---------------------------------------------------------------------------------

    private void OnPlayPauseTimer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TimerItem timer })
        {
            e.Handled = true;
            if (timer.State == TimerState.Running)
            {
                _timers.Pause(timer);
            }
            else
            {
                _timers.Start(timer);
            }
        }
    }

    private void OnResetTimer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TimerItem timer })
        {
            e.Handled = true;
            _timers.Reset(timer);
        }
    }

    private void OnDeleteTimer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TimerItem timer })
        {
            e.Handled = true;
            if (Confirm($"Delete the {timer.DurationText} timer “{timer.Title}”?", "Delete timer"))
            {
                _timers.Delete(timer);
            }
        }
    }

    // Editing a running timer would change it under you, so only stopped or paused ones open the editor.
    private void OnEditTimer(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TimerItem { State: not TimerState.Running } timer }
            && TimerEditWindow.Edit(Window.GetWindow(this), timer, isNew: false) is { } start)
        {
            _timers.Save(timer);
            if (start)
            {
                _timers.Start(timer);
            }
        }
    }

    private bool Confirm(string text, string title) =>
        DialogGuard.Run(() => MessageBox.Show(Window.GetWindow(this)!, text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question))
        == MessageBoxResult.OK;
}
