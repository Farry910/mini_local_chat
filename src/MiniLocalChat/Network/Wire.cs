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
    public string Type { get; set; } = WireTypes.Chat;

    public Guid Id { get; set; }

    /// <summary>Sender's current nickname; lets the receiver keep names up to date.</summary>
    public string? Nick { get; set; }

    /// <summary>Sender's two avatar colors ("#RRGGBB"), also used for the ripple on the receiver's screen.</summary>
    public List<string>? Colors { get; set; }

    /// <summary>Sender's profile picture id (null = none). Receivers without it ask with "avatar-get".</summary>
    public string? Avatar { get; set; }

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

    /// <summary>"chat": the message this one replies to.</summary>
    public ReplyInfo? ReplyTo { get; set; }

    /// <summary>"chat": a forwarded copy of another message.</summary>
    public bool Forwarded { get; set; }

    /// <summary>"chat": a built-in emoji sticker (the GIF is the payload, the emoji is in Text).</summary>
    public bool Sticker { get; set; }

    /// <summary>"edit" / "delete" / "react": the message acted on.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>"react": add (true) or remove (false) the emoji in <see cref="Text"/>.</summary>
    public bool On { get; set; }

    /// <summary>"group": one of <see cref="GroupActions"/>.</summary>
    public string? Action { get; set; }

}

/// <summary>
/// Packet types. Everything but "chat" changes something that already exists. Older app versions
/// ignore types they don't know (and still acknowledge them).
/// </summary>
public static class WireTypes
{
    public const string Chat = "chat";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string React = "react";
    public const string Group = "group";

    /// <summary>"I've seen the chat up to message <c>TargetId</c>" (read receipt, the double tick).</summary>
    public const string Seen = "seen";

    /// <summary>"I'm typing in this chat" (<c>On</c> = true) or "I stopped". Sent directly, never queued.</summary>
    public const string Typing = "typing";

    /// <summary>"Please send me your picture with id <c>Text</c>."</summary>
    public const string AvatarGet = "avatar-get";

    /// <summary>A profile picture (the file payload), id in <c>Text</c>.</summary>
    public const string Avatar = "avatar";

    /// <summary>"Please send me your app, version <c>Text</c>" (LAN auto-update).</summary>
    public const string UpdateGet = "update-get";

    /// <summary>The app exe (payload), its version in <c>Text</c>.</summary>
    public const string Update = "update";
}

public static class GroupActions
{
    public const string Rename = "rename";
    public const string Add = "add";
    public const string Leave = "leave";
}

/// <summary>An edit/delete/reaction/group change still waiting to reach someone; kept across restarts.</summary>
public sealed class PendingControl
{
    public string RecipientIp { get; set; } = "";

    public WireHeader Header { get; set; } = new();
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

    /// <summary>Profile picture id, or null.</summary>
    public string? Avatar { get; set; }

    /// <summary>Running but away: no mouse movement for a while, or the screen is locked.</summary>
    public bool Away { get; set; }

    /// <summary>The app version this computer can hand out (LAN auto-update), or null.</summary>
    public string? Update { get; set; }
}
