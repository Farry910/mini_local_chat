using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Models;

/// <summary>
/// An alarm (separate from chat): rings at a time of day, once or on chosen weekdays, by waving the
/// screen in its two colors until dismissed or snoozed.
/// </summary>
public sealed class Alarm : ObservableObject
{
    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private bool _enabled = true;
    private DateTime? _nextRing;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Label { get; set; } = "";

    public int Hour { get; set; } = 7;

    public int Minute { get; set; }

    /// <summary>Days it repeats on; empty = rings once (on <see cref="OnceDate"/>).</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    /// <summary>For one-time alarms: the day it rings (local date).</summary>
    public DateTime? OnceDate { get; set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
            {
                Refresh();
            }
        }
    }

    public string Color1 { get; set; } = "#5B5FC7";

    public string Color2 { get; set; } = "#22C55E";

    public bool Sound { get; set; } = true;

    /// <summary>When it rings next (local time), or null if it won't. Kept up to date by the alarm service.</summary>
    [JsonIgnore]
    public DateTime? NextRing
    {
        get => _nextRing;
        set
        {
            if (Set(ref _nextRing, value))
            {
                OnPropertyChanged(nameof(NextText));
            }
        }
    }

    [JsonIgnore]
    public string TimeText => DateTime.Today.AddHours(Hour).AddMinutes(Minute).ToString("t", CultureInfo.CurrentCulture);

    [JsonIgnore]
    public string Title => string.IsNullOrWhiteSpace(Label) ? "Alarm" : Label;

    [JsonIgnore]
    public string RepeatText
    {
        get
        {
            if (Days.Count == 0)
            {
                return "Once";
            }

            if (Days.Count == 7)
            {
                return "Every day";
            }

            if (Days.Count == 5 && Weekdays.All(Days.Contains))
            {
                return "Weekdays";
            }

            if (Days.Count == 2 && Days.Contains(DayOfWeek.Saturday) && Days.Contains(DayOfWeek.Sunday))
            {
                return "Weekends";
            }

            var names = CultureInfo.CurrentCulture.DateTimeFormat;
            return string.Join(", ", MondayFirst().Where(Days.Contains).Select(d => names.GetAbbreviatedDayName(d)));
        }
    }

    [JsonIgnore]
    public string NextText
    {
        get
        {
            if (!Enabled)
            {
                return "Off";
            }

            if (NextRing is not { } next)
            {
                return "Won't ring again";
            }

            var left = next - DateTime.Now;
            var when = next.Date == DateTime.Today ? "Today"
                : next.Date == DateTime.Today.AddDays(1) ? "Tomorrow"
                : next.ToString("ddd", CultureInfo.CurrentCulture);
            var inText = left.TotalMinutes < 1 ? "in less than a minute"
                : left.TotalHours < 1 ? $"in {(int)left.TotalMinutes} min"
                : left.TotalDays < 1 ? $"in {(int)left.TotalHours} h {left.Minutes} min"
                : $"in {(int)left.TotalDays} d {left.Hours} h";
            return $"{when} {next.ToString("t", CultureInfo.CurrentCulture)} · {inText}";
        }
    }

    [JsonIgnore]
    public Brush Brush1 => AvatarPalette.ToBrush(AvatarPalette.NormalizeHex(Color1) ?? "#5B5FC7");

    [JsonIgnore]
    public Brush Brush2 => AvatarPalette.ToBrush(AvatarPalette.NormalizeHex(Color2) ?? "#22C55E");

    /// <summary>Monday-first week, the order the day buttons are shown in.</summary>
    public static IEnumerable<DayOfWeek> MondayFirst() =>
        Enumerable.Range(1, 7).Select(i => (DayOfWeek)(i % 7));

    /// <summary>Call after editing the alarm's settings.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(RepeatText));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(Brush1));
        OnPropertyChanged(nameof(Brush2));
        OnPropertyChanged(nameof(Sound));
    }
}
