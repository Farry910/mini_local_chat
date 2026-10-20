namespace MiniLocalChat.Models;

public sealed class AppSettings
{
    public string Nickname { get; set; } = "";

    /// <summary>My avatar fill and ring, also my ripple colors on other screens. Null = default from my IP.</summary>
    public string? AvatarColor1 { get; set; }

    public string? AvatarColor2 { get; set; }

    /// <summary>My profile picture (see AvatarStore); null = initials.</summary>
    public string? AvatarId { get; set; }

    /// <summary>The IP that identifies me to others. Null means detect automatically.</summary>
    public string? MyIp { get; set; }

    /// <summary>Every computer must use the same ports.</summary>
    public int TcpPort { get; set; } = 50505;

    public int UdpPort { get; set; } = 50506;

    public string LedSide { get; set; } = "Right";

    /// <summary>LED distance from the top of the monitor's work area, in DIPs. Default stays clear of title-bar buttons.</summary>
    public double LedOffsetY { get; set; } = 120;

    /// <summary>Device name of the monitor the LED was last placed on.</summary>
    public string? LedScreen { get; set; }

    public bool StartWithWindows { get; set; }

    /// <summary>Ripple across the whole screen, in the sender's colors, when a message arrives.</summary>
    public bool RippleEnabled { get; set; } = true;

    /// <summary>Every message ripples, but at most once per this many seconds, so a burst gives a few ripples, not dozens.</summary>
    public int RippleMinGapSeconds { get; set; } = 3;

    /// <summary>Opt-in: no ripples while a full-screen app is in front (games, videos). Off = ripple over everything,
    /// including Chrome Remote Desktop in full screen.</summary>
    public bool RippleSkipFullScreen { get; set; }

    /// <summary>While messages are unread, a small ripple at the mouse pointer every <c>ReminderSeconds</c>.</summary>
    public bool RippleReminder { get; set; } = true;

    /// <summary>Others see me as Away after this many minutes without the mouse moving (0 = only when locked).</summary>
    public int AwayAfterMinutes { get; set; } = 5;

    /// <summary>The floating panel stays open (doesn't hide when the mouse leaves).</summary>
    public bool FloatingPinned { get; set; }

    /// <summary>System-wide shortcut that shows / hides the app, e.g. "Ctrl+Alt+M". Empty = none.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+M";

    public bool TrayHintShown { get; set; }

    /// <summary>Group chats I left: their messages are ignored unless someone adds me back.</summary>
    public List<string> LeftGroupIds { get; set; } = [];
}
