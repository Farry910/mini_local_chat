using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Network;

namespace MiniLocalChat.Services;

/// <summary>
/// My "Away" status: the mouse pointer hasn't moved for <see cref="Models.AppSettings.AwayAfterMinutes"/>,
/// or the screen is locked. Others see it right away (an extra heartbeat goes out when it changes).
/// </summary>
public sealed partial class ChatService
{
    private static readonly TimeSpan AwayCheckInterval = TimeSpan.FromSeconds(5);

    private DispatcherTimer? _awayTimer;
    private (int X, int Y)? _lastPointer;
    private DateTime _lastPointerMoveUtc = DateTime.UtcNow;

    private void StartAwayDetection()
    {
        _awayTimer ??= CreateAwayTimer();
        _awayTimer.Start();
    }

    private void StopAwayDetection() => _awayTimer?.Stop();

    private DispatcherTimer CreateAwayTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = AwayCheckInterval };
        timer.Tick += (_, _) => UpdateAway();
        return timer;
    }

    private void UpdateAway()
    {
        var now = DateTime.UtcNow;
        if (Win32.CursorPosition() is { } pointer && pointer != _lastPointer)
        {
            _lastPointer = pointer;
            _lastPointerMoveUtc = now;
        }

        var minutes = Settings.AwayAfterMinutes;
        var away = Win32.IsSessionLocked() || (minutes > 0 && now - _lastPointerMoveUtc > TimeSpan.FromMinutes(minutes));
        if (away != Me.IsAway)
        {
            Me.IsAway = away;
            _presence?.Announce(PresenceKinds.Here);
        }
    }
}
