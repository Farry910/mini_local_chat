using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Models;

/// <summary>A 1:1 chat ("dm:&lt;ip&gt;") or a group chat ("grp:&lt;id&gt;", the id is shared by all members).</summary>
public sealed class Conversation : ObservableObject
{
    private string? _groupName;
    private int _unreadCount;
    private DateTime _lastActivityUtc;

    public string Id { get; set; } = "";

    public bool IsGroup { get; set; }

    public string? GroupName
    {
        get => _groupName;
        set
        {
            if (Set(ref _groupName, value))
            {
                RefreshDisplay();
            }
        }
    }

    /// <summary>Everyone in the chat except me.</summary>
    public List<string> MemberIps { get; set; } = [];

    public ObservableCollection<ChatMessage> Messages { get; set; } = [];

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (Set(ref _unreadCount, value))
            {
                OnPropertyChanged(nameof(HasUnread));
            }
        }
    }

    /// <summary>Sort key: chats with new messages go to the top of the floating panel.</summary>
    [JsonIgnore]
    public bool HasUnread => UnreadCount > 0;

    public DateTime LastActivityUtc
    {
        get => _lastActivityUtc;
        set => Set(ref _lastActivityUtc, value);
    }

    [JsonIgnore]
    public List<Contact> Members { get; } = [];

    [JsonIgnore]
    public Contact? Peer => IsGroup ? null : Members.FirstOrDefault();

    [JsonIgnore]
    public string Title
    {
        get
        {
            if (!IsGroup)
            {
                return Peer?.DisplayName ?? "Unknown";
            }

            return string.IsNullOrWhiteSpace(GroupName)
                ? string.Join(", ", Members.Select(m => m.DisplayName))
                : GroupName;
        }
    }

    [JsonIgnore]
    public string Subtitle => IsGroup
        ? $"{Members.Count + 1} people: You, " + string.Join(", ", Members.Select(m => m.DisplayName))
        : Peer?.Ip ?? "";

    [JsonIgnore]
    public string AvatarText => IsGroup ? "" : Peer?.Initials ?? "?";

    [JsonIgnore]
    public Brush AvatarBrush => AvatarPalette.For(IsGroup ? Id : Peer?.Ip ?? Id);

    [JsonIgnore]
    public string LastPreview
    {
        get
        {
            if (Messages.Count == 0)
            {
                return IsGroup ? "New group chat" : "Say hi 👋";
            }

            var last = Messages[^1];
            var prefix = last.IsMine ? "You: " : IsGroup ? $"{last.Sender?.DisplayName}: " : "";
            return prefix + last.Preview;
        }
    }

    public void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        LastActivityUtc = DateTime.UtcNow;
        OnPropertyChanged(nameof(LastPreview));
    }

    /// <summary>Call when a member's nickname or the member list changes.</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Peer));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(AvatarText));
        OnPropertyChanged(nameof(LastPreview));
    }
}
