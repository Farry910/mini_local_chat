using System.Collections.ObjectModel;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Services;

/// <summary>
/// Countdown timers. Ticks only while a timer runs (no cost otherwise). A timer that ran out while the app
/// was closed is reset instead of ringing late.
/// </summary>
public sealed class TimerService
{
    private const string TimersFile = "timers.json";
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(90);

    private readonly Storage _storage = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public TimerService()
    {
        foreach (var timer in _storage.Load<List<TimerItem>>(TimersFile) ?? [])
        {
            if (timer.State == TimerState.Ringing
                || (timer.State == TimerState.Running && (timer.EndsAtUtc is null || DateTime.UtcNow - timer.EndsAtUtc > Grace)))
            {
                timer.State = TimerState.Ready;
                timer.EndsAtUtc = null;
            }

            Timers.Add(timer);
        }

        _tick.Tick += (_, _) => Tick();
        UpdateTicking();
    }

    /// <summary>A timer reached zero (it keeps ringing until restarted or dismissed).</summary>
    public event Action<TimerItem>? Ring;

    public ObservableCollection<TimerItem> Timers { get; } = [];

    public int RunningCount => Timers.Count(t => t.State == TimerState.Running);

    public void Save(TimerItem timer)
    {
        if (!Timers.Contains(timer))
        {
            Timers.Insert(0, timer);
        }

        if (timer.State != TimerState.Running)
        {
            timer.State = TimerState.Ready;
            timer.EndsAtUtc = null;
        }

        timer.Refresh();
        Persist();
    }

    public void Start(TimerItem timer)
    {
        var left = timer.State == TimerState.Paused ? timer.RemainingSecondsWhenPaused : timer.DurationSeconds;
        if (left <= 0)
        {
            return;
        }

        timer.EndsAtUtc = DateTime.UtcNow.AddSeconds(left);
        timer.State = TimerState.Running;
        UpdateTicking();
        Persist();
    }

    public void Pause(TimerItem timer)
    {
        if (timer.State != TimerState.Running)
        {
            return;
        }

        timer.RemainingSecondsWhenPaused = (int)Math.Ceiling(timer.Remaining.TotalSeconds);
        timer.EndsAtUtc = null;
        timer.State = TimerState.Paused;
        UpdateTicking();
        Persist();
    }

    /// <summary>Back to the full duration, stopped (also how a ringing timer is dismissed).</summary>
    public void Reset(TimerItem timer)
    {
        timer.EndsAtUtc = null;
        timer.State = TimerState.Ready;
        UpdateTicking();
        Persist();
    }

    public void Restart(TimerItem timer)
    {
        timer.State = TimerState.Ready;
        Start(timer);
    }

    public void Delete(TimerItem timer)
    {
        Timers.Remove(timer);
        UpdateTicking();
        Persist();
    }

    private void Tick()
    {
        foreach (var timer in Timers.Where(t => t.State == TimerState.Running).ToList())
        {
            if (timer.Remaining <= TimeSpan.Zero)
            {
                timer.EndsAtUtc = null;
                timer.State = TimerState.Ringing;
                Persist();
                Ring?.Invoke(timer);
            }
            else
            {
                timer.Refresh();
            }
        }

        UpdateTicking();
    }

    private void UpdateTicking() => _tick.IsEnabled = Timers.Any(t => t.State == TimerState.Running);

    private void Persist()
    {
        try
        {
            _storage.Save(TimersFile, Timers.ToList());
        }
        catch (IOException ex)
        {
            Log.Error("Saving timers", ex);
        }
    }
}
