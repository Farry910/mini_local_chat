using System.Windows.Media;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

/// <summary>Something ringing: an alarm (Snooze / Dismiss) or a finished timer (Restart / Dismiss).</summary>
public sealed record RingRequest(
    Guid Id,
    string Heading,
    string Title,
    string Color1,
    string Color2,
    bool Sound,
    string SecondaryLabel,
    Action Secondary,
    Action Dismiss);

/// <summary>
/// What a ringing alarm or timer looks and sounds like: a small card per ringing item, the screen waving in
/// its two colors every few seconds, and the Windows alarm sound on a loop. Stops when handled, or by itself
/// after five minutes.
/// </summary>
public sealed class AlarmRinger
{
    private static readonly TimeSpan WaveEvery = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);
    private static readonly string SoundFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");

    private readonly Func<RippleController?> _ripples;
    private readonly Dictionary<Guid, (AlarmRingWindow Window, DateTime Started)> _ringing = new();
    private readonly DispatcherTimer _wave = new() { Interval = WaveEvery };
    private MediaPlayer? _player;

    public AlarmRinger(AlarmService alarms, TimerService timers, Func<RippleController?> ripples)
    {
        _ripples = ripples;
        _wave.Tick += (_, _) => OnWave();

        alarms.Ring += alarm => Start(new RingRequest(
            alarm.Id, alarm.TimeText, alarm.Title, alarm.Color1, alarm.Color2, alarm.Sound,
            "Snooze 5 min", () => alarms.Snooze(alarm), () => alarms.Dismiss(alarm)));

        timers.Ring += timer => Start(new RingRequest(
            timer.Id, "Time's up", $"{timer.Title} · {timer.DurationText}", timer.Color1, timer.Color2, timer.Sound,
            "Restart", () => timers.Restart(timer), () => timers.Reset(timer)));
    }

    private void Start(RingRequest request)
    {
        if (_ringing.ContainsKey(request.Id))
        {
            return;
        }

        var window = new AlarmRingWindow(request, _ringing.Count);
        window.SecondaryClicked += () =>
        {
            request.Secondary();
            Stop(request);
        };
        window.Dismissed += () =>
        {
            request.Dismiss();
            Stop(request);
        };
        _ringing[request.Id] = (window, DateTime.UtcNow);
        window.Show();

        Wave(request);
        _wave.Start();
        UpdateSound();
    }

    private void Stop(RingRequest request)
    {
        if (_ringing.Remove(request.Id, out var entry))
        {
            entry.Window.Close();
        }

        if (_ringing.Count == 0)
        {
            _wave.Stop();
        }

        UpdateSound();
    }

    private void OnWave()
    {
        foreach (var (window, started) in _ringing.Values.ToList())
        {
            if (DateTime.UtcNow - started > GiveUpAfter)
            {
                window.Request.Dismiss();
                Stop(window.Request);
            }
        }

        // One wave per tick, taking turns if several ring at once.
        if (_ringing.Count > 0)
        {
            var index = (int)(DateTime.UtcNow.Ticks / WaveEvery.Ticks % _ringing.Count);
            Wave(_ringing.Values.ElementAt(index).Window.Request);
        }
    }

    private void Wave(RingRequest request) =>
        _ripples()?.Play(AvatarPalette.NormalizeHex(request.Color1) ?? "#5B5FC7", AvatarPalette.NormalizeHex(request.Color2) ?? "#22C55E", fromScreenCenter: true);

    private void UpdateSound()
    {
        var wanted = _ringing.Values.Any(r => r.Window.Request.Sound);
        if (wanted && _player is null && File.Exists(SoundFile))
        {
            _player = new MediaPlayer();
            _player.MediaEnded += (_, _) =>
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            };
            _player.MediaFailed += (_, e) => Log.Error("Alarm sound", e.ErrorException);
            _player.Open(new Uri(SoundFile));
            _player.Play();
        }
        else if (!wanted && _player is not null)
        {
            _player.Stop();
            _player.Close();
            _player = null;
        }
    }
}
