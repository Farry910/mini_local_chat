using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Network;

namespace MiniLocalChat.Services;

/// <summary>
/// Profile pictures. Everyone announces the id (hash) of their picture with every heartbeat and message.
/// When I see an id I don't have, I ask its owner ("avatar-get") and they send the picture ("avatar").
/// Not queued: if it fails, the next heartbeat (at most once a minute per person) asks again.
/// </summary>
public sealed partial class ChatService
{
    private static readonly TimeSpan AvatarRetry = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, DateTime> _avatarRequests = new();

    private void ApplyAvatar(Contact contact, string? avatarId)
    {
        avatarId = AvatarStore.IsValidId(avatarId) ? avatarId : null;
        contact.AvatarId = avatarId;
        if (avatarId is null || AvatarStore.PathFor(avatarId) is not null)
        {
            return;
        }

        if (_avatarRequests.TryGetValue(contact.Ip, out var last) && DateTime.UtcNow - last < AvatarRetry)
        {
            return;
        }

        _avatarRequests[contact.Ip] = DateTime.UtcNow;
        var header = new WireHeader { Type = WireTypes.AvatarGet, Id = Guid.NewGuid(), Nick = Me.Nickname, Colors = MyColors(), Avatar = Me.AvatarId, Text = avatarId };
        SendDirect(contact.Ip, header, null);
    }

    private void ReceiveAvatarRequest(WireHeader header, Contact sender)
    {
        if (header.Text is { } id && id == Me.AvatarId && AvatarStore.PathFor(id) is { } path)
        {
            var reply = new WireHeader
            {
                Type = WireTypes.Avatar,
                Id = Guid.NewGuid(),
                Nick = Me.Nickname,
                Colors = MyColors(),
                Avatar = Me.AvatarId,
                Kind = MessageKind.Image,
                Text = id,
                FileName = "avatar.png",
                FileSize = new FileInfo(path).Length,
            };
            SendDirect(sender.Ip, reply, path);
        }
    }

    private void ReceiveAvatar(WireHeader header, string? filePath)
    {
        // Only pictures someone actually announced, and only if the bytes match the announced id.
        if (filePath is null || header.Text is not { } id || !Contacts.Any(c => c.AvatarId == id))
        {
            if (filePath is not null)
            {
                AvatarStore.Accept(filePath, ""); // just cleans up the temporary file
            }

            return;
        }

        if (AvatarStore.Accept(filePath, id))
        {
            foreach (var contact in Contacts.Where(c => c.AvatarId == id))
            {
                contact.RefreshAvatar();
            }
        }
    }

    /// <summary>One-off send outside the delivery queue (picture requests and replies, typing).</summary>
    private void SendDirect(string ip, WireHeader header, string? filePath)
    {
        var bind = BindAddress;
        var port = Settings.TcpPort;
        _knownMessageIds.Add(header.Id);
        _ = Task.Run(async () =>
        {
            try
            {
                await PeerClient.SendAsync(bind, ip, port, header, filePath);
            }
            catch (Exception ex)
            {
                Log.Error("Direct send", ex);
            }
        });
    }
}
