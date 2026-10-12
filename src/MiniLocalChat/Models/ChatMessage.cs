using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Models;

public enum MessageKind
{
    Text,
    File,
    Image,

    /// <summary>A line like "Alice renamed the group"; shown centered, never sent as-is.</summary>
    System,
}

public enum DeliveryState
{
    Pending,
    Sent,
    Failed,
}

/// <summary>What a reply quotes: a snapshot, so it still reads right if the original is edited later.</summary>
public sealed class ReplyInfo
{
    public const int MaxPreviewLength = 200;

    public Guid Id { get; set; }

    public string SenderName { get; set; } = "";

    public string Preview { get; set; } = "";
}

/// <summary>One reaction under a message: the emoji, how many people, whether I'm one of them.</summary>
public sealed record ReactionChip(string Emoji, int Count, bool Mine, string Names);

public sealed class ChatMessage : ObservableObject
{
    /// <summary>Teams' quick reactions.</summary>
    public static readonly string[] QuickReactions = ["👍", "❤️", "😆", "😮", "😢", "😡"];

    private string _text = "";
    private string? _localPath;
    private bool _isEdited;
    private bool _isDeleted;

    public Guid Id { get; set; }

    public string SenderIp { get; set; } = "";

    public bool IsMine { get; set; }

    public DateTime SentUtc { get; set; }

    public MessageKind Kind { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            if (Set(ref _text, value ?? ""))
            {
                OnPropertyChanged(nameof(Preview));
            }
        }
    }

    public string? FileName { get; set; }

    public long FileSize { get; set; }

    /// <summary>For images and files: where the file is on this computer.</summary>
    public string? LocalPath
    {
        get => _localPath;
        set => Set(ref _localPath, value);
    }

    public ReplyInfo? ReplyTo { get; set; }

    public bool IsForwarded { get; set; }

    /// <summary>A built-in animated emoji: shown small, without a bubble. <see cref="Text"/> holds the emoji.</summary>
    public bool IsSticker { get; set; }

    public bool IsEdited
    {
        get => _isEdited;
        set => Set(ref _isEdited, value);
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (Set(ref _isDeleted, value))
            {
                OnPropertyChanged(nameof(Preview));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(HasActions));
            }
        }
    }

    /// <summary>For my own messages: delivery state per recipient IP. Pending entries are retried until delivered.</summary>
    public Dictionary<string, DeliveryState> Delivery { get; set; } = new();

    /// <summary>Emoji → IPs of the people who reacted with it (mine included).</summary>
    public Dictionary<string, List<string>> Reactions { get; set; } = new();

    [JsonIgnore]
    public Contact? Sender { get; set; }

    [JsonIgnore]
    public ObservableCollection<ReactionChip> ReactionChips { get; } = [];

    [JsonIgnore]
    public bool CanEdit => IsMine && Kind == MessageKind.Text && !IsDeleted;

    [JsonIgnore]
    public bool CanDelete => IsMine && Kind != MessageKind.System && !IsDeleted;

    /// <summary>An animated GIF: plays inline in the chat.</summary>
    [JsonIgnore]
    public bool IsGif => Kind == MessageKind.Image
        && (string.Equals(Path.GetExtension(FileName), ".gif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(LocalPath), ".gif", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reply, forward, react…: not for deleted messages or system lines.</summary>
    [JsonIgnore]
    public bool HasActions => Kind != MessageKind.System && !IsDeleted;

    [JsonIgnore]
    public string Preview => IsDeleted
        ? "This message has been deleted"
        : Kind switch
        {
            MessageKind.Image when IsSticker => $"{Text} Sticker",
            MessageKind.Image when IsGif => "GIF",
            MessageKind.Image => "📷 Image",
            MessageKind.File => "📎 " + FileName,
            _ => Text.ReplaceLineEndings(" "),
        };

    [JsonIgnore]
    public string DeliveryText
    {
        get
        {
            if (!IsMine || Delivery.Count == 0 || IsDeleted)
            {
                return "";
            }

            var total = Delivery.Count;
            var sent = Delivery.Values.Count(s => s == DeliveryState.Sent);
            var failed = Delivery.Values.Count(s => s == DeliveryState.Failed);
            if (sent == total)
            {
                return "✓ Delivered";
            }

            if (sent + failed == total)
            {
                return "Couldn't send – the file no longer exists";
            }

            return total == 1
                ? "Waiting – sends when they come online"
                : $"Delivered to {sent} of {total} – the rest get it when online";
        }
    }

    public void SetDelivery(string ip, DeliveryState state)
    {
        Delivery[ip] = state;
        OnPropertyChanged(nameof(DeliveryText));
    }

    public void MarkDeleted()
    {
        Text = "";
        LocalPath = null;
        Reactions.Clear();
        ReactionChips.Clear();
        IsDeleted = true;
        OnPropertyChanged(nameof(DeliveryText));
    }

    /// <summary>Adds or removes one person's reaction. Returns false if nothing changed.</summary>
    public bool SetReaction(string emoji, string ip, bool on)
    {
        if (!Reactions.TryGetValue(emoji, out var people))
        {
            if (!on)
            {
                return false;
            }

            Reactions[emoji] = people = [];
        }

        var changed = on ? !people.Contains(ip) : people.Contains(ip);
        if (on && changed)
        {
            people.Add(ip);
        }
        else if (!on)
        {
            people.Remove(ip);
        }

        if (people.Count == 0)
        {
            Reactions.Remove(emoji);
        }

        return changed;
    }

    public void RefreshReactions(string myIp, Func<string, string> nameOf)
    {
        ReactionChips.Clear();
        foreach (var (emoji, people) in Reactions.Where(r => r.Value.Count > 0))
        {
            var names = string.Join(", ", people.Select(ip => ip == myIp ? "You" : nameOf(ip)));
            ReactionChips.Add(new ReactionChip(emoji, people.Count, people.Contains(myIp), names));
        }
    }
}
