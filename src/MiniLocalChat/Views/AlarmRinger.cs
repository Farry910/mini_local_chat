using System.Windows.Media;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

/// <summary>
/// What a ringing alarm looks and sounds like: a small "Alarm" window per ringing alarm, the screen
/// waving in the alarm's two colors every few seconds, and the Windows alarm sound on a loop.
/// Stops on Snooze / Dismiss, or by itself after five minutes.
/// </summary>
public sealed class AlarmRinger
{
    private static readonly TimeSpan WaveEvery = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);
    private static readonly string SoundFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");

    private readonly AlarmService _alarms;
    private readonly Func<RippleController?> _ripples;
    private readonly Dictionary<Guid, (AlarmRingWindow Window, DateTime Started)> _ringing = new();
    private readonly DispatcherTimer _wave = new() { Interval = WaveEvery };
    private MediaPlayer? _player;

    public AlarmRinger(AlarmService alarms, Func<RippleController?> ripples)
    {
        _alarms = alarms;
        _ripples = ripples;
        _alarms.Ring += Start;
        _wave.Tick += (_, _) => OnWave();
    }

    private void Start(Alarm alarm)
    {
        if (_ringing.ContainsKey(alarm.Id))
        {
            return;
        }

        var window = new AlarmRingWindow(alarm, _ringing.Count);
        window.Snoozed += () =>
        {
            _alarms.Snooze(alarm);
            Stop(alarm);
        };
        window.Dismissed += () =>
        {
            _alarms.Dismiss(alarm);
            Stop(alarm);
        };
        _ringing[alarm.Id] = (window, DateTime.UtcNow);
        window.Show();

        Wave(alarm);
        _wave.Start();
        UpdateSound();
    }

    private void Stop(Alarm alarm)
    {
        if (_ringing.Remove(alarm.Id, out var entry))
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
        foreach (var (id, (window, started)) in _ringing.ToList())
        {
            if (DateTime.UtcNow - started > GiveUpAfter)
            {
                _alarms.Dismiss(window.Alarm);
                Stop(window.Alarm);
            }
        }

        // One wave per tick, taking turns if several alarms ring at once.
        if (_ringing.Count > 0)
        {
            var index = (int)(DateTime.UtcNow.Ticks / WaveEvery.Ticks % _ringing.Count);
            Wave(_ringing.Values.ElementAt(index).Window.Alarm);
        }
    }

    private void Wave(Alarm alarm) =>
        _ripples()?.Play(AvatarPalette.NormalizeHex(alarm.Color1) ?? "#5B5FC7", AvatarPalette.NormalizeHex(alarm.Color2) ?? "#22C55E", fromScreenCenter: true);

    private void UpdateSound()
    {
        var wanted = _ringing.Values.Any(r => r.Window.Alarm.Sound);
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
