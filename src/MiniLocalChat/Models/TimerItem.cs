using System.Text.Json.Serialization;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Models;

public enum TimerState
{
    Ready,
    Running,
    Paused,
    Ringing,
}

/// <summary>A countdown timer (separate from chat): set a duration, start it, and the screen waves when it reaches zero.</summary>
public sealed class TimerItem : ObservableObject
{
    private TimerState _state;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Label { get; set; } = "";

    public int DurationSeconds { get; set; } = 5 * 60;

    public string Color1 { get; set; } = "#5B5FC7";

    public string Color2 { get; set; } = "#22C55E";

    public bool Sound { get; set; } = true;

    public TimerState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>While running: when it reaches zero.</summary>
    public DateTime? EndsAtUtc { get; set; }

    /// <summary>While paused: what was left.</summary>
    public int RemainingSecondsWhenPaused { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

    [JsonIgnore]
    public TimeSpan Remaining => State switch
    {
        TimerState.Running when EndsAtUtc is { } end => end - DateTime.UtcNow is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero,
        TimerState.Paused => TimeSpan.FromSeconds(RemainingSecondsWhenPaused),
        TimerState.Ringing => TimeSpan.Zero,
        _ => Duration,
    };

    /// <summary>Rounded up, so it shows 0:00 only when it's really done.</summary>
    [JsonIgnore]
    public string RemainingText => Format(TimeSpan.FromSeconds(Math.Ceiling(Remaining.TotalSeconds)));

    [JsonIgnore]
    public string DurationText => Format(Duration);

    [JsonIgnore]
    public string Title => string.IsNullOrWhiteSpace(Label) ? "Timer" : Label;

    [JsonIgnore]
    public string StateText => State switch
    {
        TimerState.Running => $"Running · {DurationText} timer",
        TimerState.Paused => $"Paused · {DurationText} timer",
        TimerState.Ringing => "Time's up!",
        _ => $"Ready · {DurationText}",
    };

    /// <summary>0 at the start, 1 when done.</summary>
    [JsonIgnore]
    public double Progress => DurationSeconds <= 0 ? 1 : Math.Clamp(1 - Remaining.TotalSeconds / DurationSeconds, 0, 1);

    [JsonIgnore]
    public bool IsRunning => State == TimerState.Running;

    [JsonIgnore]
    public Brush Brush1 => AvatarPalette.ToBrush(AvatarPalette.NormalizeHex(Color1) ?? "#5B5FC7");

    [JsonIgnore]
    public Brush Brush2 => AvatarPalette.ToBrush(AvatarPalette.NormalizeHex(Color2) ?? "#22C55E");

    public static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}";

    /// <summary>Updates the countdown texts (called every tick while running) and everything after an edit.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Brush1));
        OnPropertyChanged(nameof(Brush2));
    }
}
