using System.Windows;
using System.Windows.Media;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Views;

/// <summary>
/// Full-screen ripple in the sender's two avatar colors when a message arrives. No repeats; after a
/// ripple, further messages stay quiet for <see cref="AppSettings.RippleIntervalSeconds"/>.
/// </summary>
public sealed class RippleController
{
    public const int MinIntervalSeconds = 0;
    public const int MaxIntervalSeconds = 3600;

    private readonly ChatService _chat;
    private readonly Func<Point> _origin;
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
        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        foreach (var screen in Screen.AllScreens)
        {
            var b = screen.Bounds;
            var bounds = new Rect(b.Left / dpi.DpiScaleX, b.Top / dpi.DpiScaleY, b.Width / dpi.DpiScaleX, b.Height / dpi.DpiScaleY);
            new RippleWindow(bounds).Play(new Point(origin.X - bounds.Left, origin.Y - bounds.Top), first, second);
        }
    }

    private void OnMessageReceived(Conversation conversation, ChatMessage message)
    {
        var settings = _chat.Settings;

        // Already read (the chat is open on screen), switched off, or still in the quiet period.
        if (!settings.RippleEnabled || conversation.UnreadCount == 0 || message.Sender is not { } sender)
        {
            return;
        }

        var quiet = TimeSpan.FromSeconds(Math.Clamp(settings.RippleIntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds));
        if (DateTime.UtcNow - _lastPlayedUtc < quiet)
        {
            return;
        }

        Play(sender.EffectiveColor1, sender.EffectiveColor2);
    }
}
