using MiniLocalChat.Models;
using MiniLocalChat.Network;

namespace MiniLocalChat.Services;

/// <summary>Group chats: create, rename, add people, leave, delete.</summary>
public sealed partial class ChatService
{
    public Conversation CreateGroup(IEnumerable<string> ips, string? name)
    {
        var members = ips.Select(NetUtil.Normalize).OfType<string>().Where(ip => !IsSelf(ip)).Distinct().ToList();
        if (members.Count == 0)
        {
            throw new ArgumentException("A chat needs at least one other person.", nameof(ips));
        }

        if (members.Count == 1 && string.IsNullOrWhiteSpace(name))
        {
            return GetOrCreateDirect(members[0]);
        }

        var conversation = new Conversation
        {
            Id = GroupPrefix + Guid.NewGuid().ToString("N"),
            IsGroup = true,
            GroupName = CleanGroupName(name),
            MemberIps = members,
            LastActivityUtc = DateTime.UtcNow,
        };
        Register(conversation);
        ScheduleSave();
        return conversation;
    }

    public void RenameGroup(Conversation conversation, string? name)
    {
        name = CleanGroupName(name);
        if (!conversation.IsGroup || name == conversation.GroupName)
        {
            return;
        }

        conversation.GroupName = name;
        AddSystemMessage(conversation, name is null ? "You removed the group name" : $"You renamed the group to “{name}”");

        var header = NewHeader(WireTypes.Group, conversation);
        header.Action = GroupActions.Rename;
        SendControl(header, Recipients(conversation));
    }

    public void AddPeople(Conversation conversation, IEnumerable<string> ips)
    {
        var added = ips.Select(NetUtil.Normalize).OfType<string>()
            .Where(ip => !IsSelf(ip) && !conversation.MemberIps.Contains(ip))
            .Distinct()
            .ToList();
        if (!conversation.IsGroup || added.Count == 0)
        {
            return;
        }

        foreach (var ip in added)
        {
            conversation.MemberIps.Add(ip);
            conversation.Members.Add(GetOrCreateContact(ip));
        }

        conversation.RefreshDisplay();
        AddSystemMessage(conversation, $"You added {Names(added)}");

        // Everyone, old and new, gets the full member list; new members' apps create the group from it.
        var header = NewHeader(WireTypes.Group, conversation);
        header.Action = GroupActions.Add;
        SendControl(header, Recipients(conversation));
    }

    /// <summary>Tells the others I left, and takes the group off my list. Its messages are ignored from now on.</summary>
    public void LeaveGroup(Conversation conversation)
    {
        if (!conversation.IsGroup)
        {
            return;
        }

        var header = NewHeader(WireTypes.Group, conversation);
        header.Action = GroupActions.Leave;
        SendControl(header, Recipients(conversation));

        Settings.LeftGroupIds.Add(conversation.Id[GroupPrefix.Length..]);
        SaveSettings();
        RemoveConversation(conversation);
    }

    /// <summary>Removes a group chat from my list only; it comes back if someone writes there again.</summary>
    public void DeleteChat(Conversation conversation)
    {
        if (conversation.IsGroup)
        {
            RemoveConversation(conversation);
        }
    }

    private string Names(IEnumerable<string> ips) => string.Join(", ", ips.Select(NameOf));

    private void ReceiveGroupChange(WireHeader header, Contact sender)
    {
        if (string.IsNullOrEmpty(header.GroupId) || !GroupIdPattern().IsMatch(header.GroupId))
        {
            return;
        }

        _conversationsById.TryGetValue(GroupPrefix + header.GroupId, out var conversation);

        // Only members can change a group.
        if (conversation is not null && !conversation.MemberIps.Contains(sender.Ip))
        {
            return;
        }

        switch (header.Action)
        {
            case GroupActions.Add when conversation is null:
                // I was added (maybe back after leaving).
                if ((header.Members ?? []).Select(NetUtil.Normalize).OfType<string>().Any(IsSelf))
                {
                    if (Settings.LeftGroupIds.Remove(header.GroupId))
                    {
                        SaveSettings();
                    }

                    if (FindConversationFor(header, sender.Ip, create: true) is { } created)
                    {
                        AddSystemMessage(created, $"{sender.DisplayName} added you to the group");
                    }
                }

                break;

            case GroupActions.Add:
                var added = WireMembers(header, sender.Ip).Where(ip => !conversation.MemberIps.Contains(ip)).ToList();
                if (added.Count > 0)
                {
                    foreach (var ip in added)
                    {
                        conversation.MemberIps.Add(ip);
                        conversation.Members.Add(GetOrCreateContact(ip));
                    }

                    conversation.RefreshDisplay();
                    AddSystemMessage(conversation, $"{sender.DisplayName} added {Names(added)}");
                }

                break;

            case GroupActions.Rename when conversation is not null:
                var name = CleanGroupName(header.GroupName);
                if (name != conversation.GroupName)
                {
                    conversation.GroupName = name;
                    AddSystemMessage(conversation, name is null
                        ? $"{sender.DisplayName} removed the group name"
                        : $"{sender.DisplayName} renamed the group to “{name}”");
                }

                break;

            case GroupActions.Leave when conversation is not null:
                conversation.MemberIps.Remove(sender.Ip);
                conversation.Members.Remove(sender);
                conversation.RefreshDisplay();
                AddSystemMessage(conversation, $"{sender.DisplayName} left the group");
                break;
        }
    }
}
