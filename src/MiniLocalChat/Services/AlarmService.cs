using System.Collections.ObjectModel;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Services;

/// <summary>
/// Keeps the alarms and decides when they ring. Separate from chat. Works while the app runs (also in the
/// tray); an alarm whose time passed while the app was closed doesn't ring late.
/// </summary>
public sealed class AlarmService
{
    public static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(5);

    private const string AlarmsFile = "alarms.json";

    /// <summary>A missed tick (sleep, busy UI) still rings if it's at most this late.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(90);

    private readonly Storage _storage = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<Guid, DateTime> _snoozedUntil = new();
    private DateTime _lastRefresh;

    public AlarmService()
    {
        foreach (var alarm in _storage.Load<List<Alarm>>(AlarmsFile) ?? [])
        {
            Alarms.Add(alarm);
        }

        Reschedule();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>An alarm started ringing (it keeps ringing until snoozed or dismissed).</summary>
    public event Action<Alarm>? Ring;

    public ObservableCollection<Alarm> Alarms { get; } = [];

    /// <summary>The alarm that rings soonest, if any.</summary>
    public Alarm? Next => Alarms.Where(a => a.NextRing is not null).MinBy(a => a.NextRing);

    public void Save(Alarm alarm)
    {
        if (alarm.Days.Count == 0)
        {
            // One-time alarm: today if that time is still ahead, otherwise tomorrow.
            var today = DateTime.Today.AddHours(alarm.Hour).AddMinutes(alarm.Minute);
            alarm.OnceDate = (today > DateTime.Now ? today : today.AddDays(1)).Date;
        }

        if (!Alarms.Contains(alarm))
        {
            Alarms.Add(alarm);
        }

        _snoozedUntil.Remove(alarm.Id);
        alarm.Refresh();
        Reschedule();
        Persist();
    }

    public void Delete(Alarm alarm)
    {
        Alarms.Remove(alarm);
        _snoozedUntil.Remove(alarm.Id);
        Persist();
    }

    public void SetEnabled(Alarm alarm, bool enabled)
    {
        if (enabled && alarm.Days.Count == 0 && NextFor(alarm, DateTime.Now) is null)
        {
            Save(alarm); // a one-time alarm that already rang: re-arm it for its next occurrence
        }

        alarm.Enabled = enabled;
        Reschedule();
        Persist();
    }

    public void Snooze(Alarm alarm)
    {
        _snoozedUntil[alarm.Id] = DateTime.Now + SnoozeLength;
    }

    public void Dismiss(Alarm alarm)
    {
        _snoozedUntil.Remove(alarm.Id);
    }

    /// <summary>When an alarm rings next after <paramref name="from"/> (local time).</summary>
    public static DateTime? NextFor(Alarm alarm, DateTime from)
    {
        if (!alarm.Enabled)
        {
            return null;
        }

        var time = TimeSpan.FromHours(alarm.Hour) + TimeSpan.FromMinutes(alarm.Minute);
        if (alarm.Days.Count == 0)
        {
            var once = alarm.OnceDate?.Date + time;
            return once > from ? once : null;
        }

        for (var i = 0; i <= 7; i++)
        {
            var candidate = from.Date.AddDays(i) + time;
            if (candidate > from && alarm.Days.Contains(candidate.DayOfWeek))
            {
                return candidate;
            }
        }

        return null;
    }

    private void Tick()
    {
        var now = DateTime.Now;
        foreach (var alarm in Alarms.ToList())
        {
            if (alarm.NextRing is { } due && now >= due)
            {
                if (now - due <= Grace)
                {
                    Ring?.Invoke(alarm);
                }

                if (alarm.Days.Count == 0)
                {
                    alarm.Enabled = false; // one-time alarms switch off after ringing
                    Persist();
                }

                alarm.NextRing = NextFor(alarm, now);
            }

            if (_snoozedUntil.TryGetValue(alarm.Id, out var snoozed) && now >= snoozed)
            {
                _snoozedUntil.Remove(alarm.Id);
                Ring?.Invoke(alarm);
            }
        }

        // Keep "in 5 min" texts current.
        if (now - _lastRefresh > TimeSpan.FromSeconds(30))
        {
            _lastRefresh = now;
            foreach (var alarm in Alarms)
            {
                alarm.Refresh();
            }
        }
    }

    private void Reschedule()
    {
        var now = DateTime.Now;
        foreach (var alarm in Alarms)
        {
            alarm.NextRing = NextFor(alarm, now);
        }
    }

    private void Persist()
    {
        try
        {
            _storage.Save(AlarmsFile, Alarms.ToList());
        }
        catch (IOException ex)
        {
            Log.Error("Saving alarms", ex);
        }
    }
}
