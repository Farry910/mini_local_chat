namespace MiniLocalChat.Models;

public sealed class AppSettings
{
    public string Nickname { get; set; } = "";

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

    public bool TrayHintShown { get; set; }
}
