namespace MiniLocalChat.Models;

public sealed class AppSettings
{
    public string Nickname { get; set; } = "";

    /// <summary>My avatar fill and ring, also my ripple colors on other screens. Null = default from my IP.</summary>
    public string? AvatarColor1 { get; set; }

    public string? AvatarColor2 { get; set; }

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

    public bool TrayHintShown { get; set; }
}
