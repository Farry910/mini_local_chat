using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Models;

/// <summary>A 1:1 chat ("dm:&lt;ip&gt;") or a group chat ("grp:&lt;id&gt;", the id is shared by all members).</summary>
public sealed class Conversation : ObservableObject
{
    /// <summary>Chat list sections, top to bottom.</summary>
    public static readonly string[] Sections = ["Favorites", "Online", "Offline"];

    private string? _groupName;
    private int _unreadCount;
    private DateTime _lastActivityUtc;
    private bool _isFavorite;

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

    /// <summary>Sort key: chats with new messages go to the top of their section.</summary>
    [JsonIgnore]
    public bool HasUnread => UnreadCount > 0;

    public DateTime LastActivityUtc
    {
        get => _lastActivityUtc;
        set => Set(ref _lastActivityUtc, value);
    }

    /// <summary>Pinned to the Favorites section at the top of the chat list.</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (Set(ref _isFavorite, value))
            {
                RefreshPresence();
            }
        }
    }

    [JsonIgnore]
    public List<Contact> Members { get; } = [];

    [JsonIgnore]
    public Contact? Peer => IsGroup ? null : Members.FirstOrDefault();

    /// <summary>1:1: the person is online. Group: anyone in it is.</summary>
    [JsonIgnore]
    public bool IsOnline => Members.Any(m => m.IsOnline);

    /// <summary>Favorites, Online or Offline.</summary>
    [JsonIgnore]
    public string Section => Sections[SectionOrder];

    [JsonIgnore]
    public int SectionOrder => IsFavorite ? 0 : IsOnline ? 1 : 2;

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
    public Brush AvatarBrush => !IsGroup && Peer is { } peer ? peer.AvatarBrush : AvatarPalette.ToBrush(AvatarPalette.DefaultColor1(Id));

    /// <summary>The person's second color as a ring; groups have none.</summary>
    [JsonIgnore]
    public Brush? AvatarRing => IsGroup ? null : Peer?.RingBrush;

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
            var prefix = last.Kind == MessageKind.System ? ""
                : last.IsMine ? "You: "
                : IsGroup ? $"{last.Sender?.DisplayName}: "
                : "";
            return prefix + last.Preview;
        }
    }

    /// <summary>Matches the search box: the chat's title or any member's name.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Members.Any(m => m.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) || m.Ip.Contains(search));

    public void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        LastActivityUtc = DateTime.UtcNow;
        OnPropertyChanged(nameof(LastPreview));
    }

    /// <summary>Call when a member's nickname or colors, or the member list, change.</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Peer));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(AvatarText));
        OnPropertyChanged(nameof(AvatarBrush));
        OnPropertyChanged(nameof(AvatarRing));
        OnPropertyChanged(nameof(LastPreview));
        RefreshPresence();
    }

    /// <summary>Call when someone in the chat goes online or offline.</summary>
    public void RefreshPresence()
    {
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(SectionOrder));
        OnPropertyChanged(nameof(Section));
    }

    /// <summary>Call when a message was edited or deleted.</summary>
    public void RefreshPreview() => OnPropertyChanged(nameof(LastPreview));
}
