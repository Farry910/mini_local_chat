using System.Text.Json;
using System.Text.Json.Serialization;
using MiniLocalChat.Models;

namespace MiniLocalChat.Network;

/// <summary>
/// TCP framing, one packet per connection:
///   "MLC1" | int32 LE header length | UTF-8 JSON header | file bytes (header.FileSize, only for files/images)
/// The receiver answers with a single ACK byte once the message is stored.
/// </summary>
public static class Wire
{
    public const string AppId = "MiniLocalChat";
    public const byte AckByte = 0x06;
    public const int MaxHeaderBytes = 1024 * 1024;

    public static readonly byte[] Magic = "MLC1"u8.ToArray();
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Strips any path and invalid characters from a file name received from the network.</summary>
    public static string SafeFileName(string? name)
    {
        var result = Path.GetFileName(name ?? "");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(c, '_');
        }

        result = result.Trim().TrimEnd('.');
        return result.Length == 0 ? "file" : result;
    }
}

public sealed class WireHeader
{
    public string Type { get; set; } = "chat";

    public Guid Id { get; set; }

    /// <summary>Sender's current nickname; lets the receiver keep names up to date.</summary>
    public string? Nick { get; set; }

    /// <summary>Sender's two avatar colors ("#RRGGBB"), also used for the ripple on the receiver's screen.</summary>
    public List<string>? Colors { get; set; }

    /// <summary>Set for group chats; shared by all members.</summary>
    public string? GroupId { get; set; }

    public string? GroupName { get; set; }

    /// <summary>For group chats: every member IP, sender included.</summary>
    public List<string>? Members { get; set; }

    public MessageKind Kind { get; set; }

    public string? Text { get; set; }

    public string? FileName { get; set; }

    public long FileSize { get; set; }

    public DateTime SentUtc { get; set; }
}

public sealed record IncomingPacket(string SenderIp, WireHeader Header, string? FilePath);

public static class PresenceKinds
{
    /// <summary>"I just started, please answer."</summary>
    public const string Hello = "hello";

    /// <summary>Periodic heartbeat, or the answer to a hello.</summary>
    public const string Here = "here";

    public const string Bye = "bye";
}

public sealed class PresencePacket
{
    public string App { get; set; } = Wire.AppId;

    public string Kind { get; set; } = PresenceKinds.Here;

    public string? Nick { get; set; }

    /// <summary>Two avatar colors ("#RRGGBB").</summary>
    public List<string>? Colors { get; set; }
}
