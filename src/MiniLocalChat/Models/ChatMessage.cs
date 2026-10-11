using System.Text.Json.Serialization;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Models;

public enum MessageKind
{
    Text,
    File,
    Image,
}

public enum DeliveryState
{
    Pending,
    Sent,
    Failed,
}

public sealed class ChatMessage : ObservableObject
{
    private string? _localPath;

    public Guid Id { get; set; }

    public string SenderIp { get; set; } = "";

    public bool IsMine { get; set; }

    public DateTime SentUtc { get; set; }

    public MessageKind Kind { get; set; }

    public string Text { get; set; } = "";

    public string? FileName { get; set; }

    public long FileSize { get; set; }

    /// <summary>For images and files: where the file is on this computer.</summary>
    public string? LocalPath
    {
        get => _localPath;
        set => Set(ref _localPath, value);
    }

    /// <summary>For my own messages: delivery state per recipient IP. Pending entries are retried until delivered.</summary>
    public Dictionary<string, DeliveryState> Delivery { get; set; } = new();

    [JsonIgnore]
    public Contact? Sender { get; set; }

    [JsonIgnore]
    public string Preview => Kind switch
    {
        MessageKind.Image => "📷 Image",
        MessageKind.File => "📎 " + FileName,
        _ => Text.ReplaceLineEndings(" "),
    };

    [JsonIgnore]
    public string DeliveryText
    {
        get
        {
            if (!IsMine || Delivery.Count == 0)
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
}
