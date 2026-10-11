using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Views;

/// <summary>
/// Full-screen ripple when a message arrives, then again every <see cref="AppSettings.RippleIntervalSeconds"/>
/// while anything is still unread. Stops as soon as everything has been read.
/// </summary>
public sealed class RippleController
{
    public const int MinIntervalSeconds = 5;
    public const int MaxIntervalSeconds = 3600;
    private static readonly TimeSpan BurstGuard = TimeSpan.FromSeconds(3);

    private readonly ChatService _chat;
    private readonly Func<Point> _origin;
    private readonly DispatcherTimer _repeat = new();
    private DateTime _lastPlayedUtc;

    /// <param name="origin">Where the rings start, in screen DIPs (the LED, or the screen center).</param>
    public RippleController(ChatService chat, Func<Point> origin)
    {
        _chat = chat;
        _origin = origin;
        _repeat.Tick += (_, _) => OnRepeat();
        _chat.MessageReceived += OnMessageReceived;
        _chat.PropertyChanged += OnChatPropertyChanged;
        ApplySettings();
    }

    private bool Enabled => _chat.Settings.RippleEnabled;

    /// <summary>Call after settings change; also picks up messages that were already unread at startup.</summary>
    public void ApplySettings()
    {
        _repeat.Stop();
        if (Enabled && _chat.TotalUnread > 0)
        {
            RestartTimer();
        }
    }

    private void OnMessageReceived(Conversation conversation, ChatMessage message)
    {
        // Already read (the chat is open on screen): nothing to announce.
        if (!Enabled || conversation.UnreadCount == 0)
        {
            return;
        }

        Play();
        RestartTimer();
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatService.TotalUnread) && _chat.TotalUnread == 0)
        {
            _repeat.Stop();
        }
    }

    private void OnRepeat()
    {
        if (Enabled && _chat.TotalUnread > 0)
        {
            Play();
        }
        else
        {
            _repeat.Stop();
        }
    }

    private void RestartTimer()
    {
        _repeat.Stop();
        _repeat.Interval = TimeSpan.FromSeconds(Math.Clamp(_chat.Settings.RippleIntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds));
        _repeat.Start();
    }

    private void Play()
    {
        // A burst of messages gives one ripple, not one per message.
        if (DateTime.UtcNow - _lastPlayedUtc < BurstGuard)
        {
            return;
        }

        _lastPlayedUtc = DateTime.UtcNow;
        var origin = _origin();
        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        foreach (var screen in Screen.AllScreens)
        {
            var b = screen.Bounds;
            var bounds = new Rect(b.Left / dpi.DpiScaleX, b.Top / dpi.DpiScaleY, b.Width / dpi.DpiScaleX, b.Height / dpi.DpiScaleY);
            new RippleWindow(bounds).Play(new Point(origin.X - bounds.Left, origin.Y - bounds.Top));
        }
    }
}
