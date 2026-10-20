using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Views;

/// <summary>
/// Full-screen ripple in the sender's two avatar colors whenever a message arrives, at most once
/// every <see cref="AppSettings.RippleMinGapSeconds"/>. While messages stay unread, a small reminder
/// ripple at the mouse pointer every <see cref="ReminderInterval"/>, so a missed ripple isn't the end of it.
/// Ripples off, or blocked for the sender, applies to both.
/// </summary>
public sealed class RippleController
{
    public const int MinGapSeconds = 0;
    public const int MaxGapSeconds = 60;

    public static readonly TimeSpan ReminderInterval = TimeSpan.FromSeconds(10);

    /// <summary>Size of the little overlay that follows the pointer for the reminder, in DIPs.</summary>
    private const double PointerWindowSize = 200;

    private readonly ChatService _chat;
    private readonly Func<Point> _origin;
    private readonly List<RippleWindow> _windows = [];
    private DateTime _lastPlayedUtc = DateTime.MinValue;
    private readonly DispatcherTimer _reminder = new(DispatcherPriority.Background) { Interval = ReminderInterval };
    private RippleWindow? _pointerWindow;

    /// <param name="origin">Where the waves start, in screen DIPs (the LED, or the screen center).</param>
    public RippleController(ChatService chat, Func<Point> origin)
    {
        _chat = chat;
        _origin = origin;
        _chat.MessageReceived += OnMessageReceived;
        _reminder.Tick += (_, _) => Remind();
        _chat.PropertyChanged += OnChatPropertyChanged;
        UpdateReminder();
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatService.TotalUnread))
        {
            UpdateReminder();
        }
    }

    // The timer only runs while something is unread.
    private void UpdateReminder()
    {
        if (_chat.TotalUnread > 0)
        {
            if (!_reminder.IsEnabled)
            {
                _reminder.Start();
            }
        }
        else
        {
            _reminder.Stop();
        }
    }

    /// <summary>A small ripple at the pointer, in the colors of whoever wrote the newest unread message.</summary>
    private void Remind()
    {
        var settings = _chat.Settings;
        if (!settings.RippleEnabled || !settings.RippleReminder || _chat.Me.IsAway || !CanRippleNow(settings))
        {
            return;
        }

        // A full ripple just played (a new message): that was the reminder.
        if (DateTime.UtcNow - _lastPlayedUtc < ReminderInterval / 2)
        {
            return;
        }

        if (NewestUnreadSender() is not { } sender || Win32.CursorPosition() is not { } pointer)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        var center = new Point(pointer.X / dpi.DpiScaleX, pointer.Y / dpi.DpiScaleY);
        var bounds = new Rect(center.X - PointerWindowSize / 2, center.Y - PointerWindowSize / 2, PointerWindowSize, PointerWindowSize);
        _pointerWindow ??= new RippleWindow(bounds);
        _pointerWindow.MoveTo(bounds);
        _pointerWindow.Play(new Point(PointerWindowSize / 2, PointerWindowSize / 2),
            AvatarPalette.ToColor(sender.EffectiveColor1), AvatarPalette.ToColor(sender.EffectiveColor2), small: true);
    }

    /// <summary>Who wrote the newest unread message, skipping people whose ripples I blocked.</summary>
    private Contact? NewestUnreadSender() =>
        _chat.Conversations
            .Where(c => c.UnreadCount > 0)
            .OrderByDescending(c => c.LastActivityUtc)
            .Select(c => c.Messages
                .Skip(Math.Max(0, c.Messages.Count - c.UnreadCount))
                .LastOrDefault(m => !m.IsMine && m.Kind != MessageKind.System && m.Sender is { RippleBlocked: false })?.Sender)
            .FirstOrDefault(s => s is not null);

    /// <summary>Not on a locked screen or during a presentation, Windows animations on, and full-screen apps allowed.</summary>
    private static bool CanRippleNow(AppSettings settings) =>
        !Win32.IsAwayOrPresenting() && SystemParameters.ClientAreaAnimation
        && !(settings.RippleSkipFullScreen && Win32.IsFullScreenAppRunning());

    /// <summary>Plays the ripple right away (e.g. a colors preview, or an alarm from the screen center).</summary>
    public void Play(string color1, string color2, bool fromScreenCenter = false)
    {
        _lastPlayedUtc = DateTime.UtcNow;
        var first = AvatarPalette.ToColor(color1);
        var second = AvatarPalette.ToColor(color2);
        var area = SystemParameters.WorkArea;
        var origin = fromScreenCenter ? new Point(area.Left + area.Width / 2, area.Top + area.Height / 2) : _origin();
        foreach (var window in OverlayWindows())
        {
            window.Play(new Point(origin.X - window.Bounds.Left, origin.Y - window.Bounds.Top), first, second);
        }
    }

    /// <summary>One overlay per monitor, kept between ripples; rebuilt only when the monitor layout changes.</summary>
    private List<RippleWindow> OverlayWindows()
    {
        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        var bounds = Screen.AllScreens
            .Select(s => new Rect(s.Bounds.Left / dpi.DpiScaleX, s.Bounds.Top / dpi.DpiScaleY, s.Bounds.Width / dpi.DpiScaleX, s.Bounds.Height / dpi.DpiScaleY))
            .ToList();

        if (!bounds.SequenceEqual(_windows.Select(w => w.Bounds)))
        {
            foreach (var old in _windows)
            {
                old.Close();
            }

            _windows.Clear();
            _windows.AddRange(bounds.Select(b => new RippleWindow(b)));
        }

        return _windows;
    }

    private void OnMessageReceived(Conversation conversation, ChatMessage message)
    {
        var settings = _chat.Settings;

        // Already read (the chat is open on screen), switched off, or I've blocked ripples from this person.
        if (!settings.RippleEnabled || conversation.UnreadCount == 0 || message.Sender is not { RippleBlocked: false } sender)
        {
            return;
        }

        // Not on a locked screen or during a presentation, and respect Windows' "Animation effects" being off.
        // Full-screen apps (e.g. Chrome Remote Desktop) still get the ripple unless the user opted out.
        // The LED lights up either way.
        if (!CanRippleNow(settings))
        {
            return;
        }

        // A burst of messages: only one ripple per gap.
        var gap = TimeSpan.FromSeconds(Math.Clamp(settings.RippleMinGapSeconds, MinGapSeconds, MaxGapSeconds));
        if (DateTime.UtcNow - _lastPlayedUtc < gap)
        {
            return;
        }

        Play(sender.EffectiveColor1, sender.EffectiveColor2);
    }
}
