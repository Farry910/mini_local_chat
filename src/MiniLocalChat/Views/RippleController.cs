using System.Windows;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Views;

/// <summary>
/// Full-screen ripple in the sender's two avatar colors whenever a message arrives, at most once
/// every <see cref="AppSettings.RippleMinGapSeconds"/>. No repeat reminders.
/// </summary>
public sealed class RippleController
{
    public const int MinGapSeconds = 0;
    public const int MaxGapSeconds = 60;

    private readonly ChatService _chat;
    private readonly Func<Point> _origin;
    private readonly List<RippleWindow> _windows = [];
    private DateTime _lastPlayedUtc = DateTime.MinValue;

    /// <param name="origin">Where the waves start, in screen DIPs (the LED, or the screen center).</param>
    public RippleController(ChatService chat, Func<Point> origin)
    {
        _chat = chat;
        _origin = origin;
        _chat.MessageReceived += OnMessageReceived;
    }

    /// <summary>Plays the ripple right away, e.g. to preview colors in Settings.</summary>
    public void Play(string color1, string color2)
    {
        _lastPlayedUtc = DateTime.UtcNow;
        var first = AvatarPalette.ToColor(color1);
        var second = AvatarPalette.ToColor(color2);
        var origin = _origin();
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

        // Already read (the chat is open on screen) or switched off.
        if (!settings.RippleEnabled || conversation.UnreadCount == 0 || message.Sender is not { } sender)
        {
            return;
        }

        // Don't sweep across a full-screen game or video, a presentation, or a locked screen,
        // and respect Windows' "Animation effects" being turned off. The LED still lights up.
        if (Win32.IsUserBusy() || !SystemParameters.ClientAreaAnimation)
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
