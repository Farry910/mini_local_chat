using MiniLocalChat.Models;
using MiniLocalChat.Network;

namespace MiniLocalChat.Services;

/// <summary>Teams-style message actions: send (with reply), forward, edit, delete, react.</summary>
public sealed partial class ChatService
{
    public const int MaxTextLength = 20000;
    private const int MaxEmojiLength = 16;
    private const int MaxDistinctReactions = 30;

    public void SendText(Conversation conversation, string text, ChatMessage? replyTo = null)
    {
        text = text.Trim();
        if (text.Length > 0)
        {
            Post(conversation, new ChatMessage
            {
                Kind = MessageKind.Text,
                Text = text.Length > MaxTextLength ? text[..MaxTextLength] : text,
                ReplyTo = replyTo is null ? null : QuoteOf(replyTo),
            });
        }
    }

    public void SendFile(Conversation conversation, string path, bool forwarded = false)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return;
        }

        // A Telegram sticker file goes out as a sticker (and into my stickers), not as a file.
        if (StickerLibrary.IsTgs(path) && StickerLibrary.Add(path) is { } stickerPath)
        {
            SendSticker(conversation, stickerPath);
            return;
        }

        // Like Telegram: GIFs I send go to my saved GIFs, and the message points at the stored copy.
        if (GifLibrary.IsGif(path))
        {
            var name = info.Name;
            info = new FileInfo(GifLibrary.Add(path));
            Post(conversation, new ChatMessage { Kind = MessageKind.Image, FileName = name, FileSize = info.Length, LocalPath = info.FullName, IsForwarded = forwarded });
            return;
        }

        Post(conversation, new ChatMessage
        {
            Kind = IsImageFile(path) ? MessageKind.Image : MessageKind.File,
            FileName = info.Name,
            FileSize = info.Length,
            LocalPath = info.FullName,
            IsForwarded = forwarded,
        });
    }

    /// <summary>A built-in animated emoji, sent as a sticker (not added to saved GIFs).</summary>
    public void SendSticker(Conversation conversation, EmojiSticker sticker) => SendSticker(conversation, sticker.Path, sticker.Emoji);

    /// <summary>
    /// A sticker: an animated emoji GIF, or a Telegram sticker (.tgs) from my stickers. Shown without a
    /// bubble. <paramref name="emoji"/> is what chat previews show ("😂 Sticker").
    /// </summary>
    public void SendSticker(Conversation conversation, string path, string emoji = "")
    {
        var info = new FileInfo(path);
        if (info.Exists)
        {
            Post(conversation, new ChatMessage
            {
                Kind = MessageKind.Image,
                Text = emoji,
                FileName = info.Name,
                FileSize = info.Length,
                LocalPath = info.FullName,
                IsSticker = true,
            });
        }
    }

    /// <summary>Sends a copy of a message to other chats. Returns false if its file is no longer on this computer.</summary>
    public bool Forward(ChatMessage message, IEnumerable<Conversation> targets)
    {
        if (!message.HasActions)
        {
            return false;
        }

        if (message.Kind != MessageKind.Text && (message.LocalPath is null || !File.Exists(message.LocalPath)))
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (message.IsSticker)
            {
                Post(target, new ChatMessage
                {
                    Kind = MessageKind.Image, Text = message.Text, FileName = message.FileName, FileSize = message.FileSize,
                    LocalPath = message.LocalPath, IsSticker = true, IsForwarded = true,
                });
            }
            else if (message.Kind == MessageKind.Text)
            {
                Post(target, new ChatMessage { Kind = MessageKind.Text, Text = message.Text, IsForwarded = true });
            }
            else
            {
                SendFile(target, message.LocalPath!, forwarded: true);
            }
        }

        return true;
    }

    public void EditMessage(ChatMessage message, string newText)
    {
        newText = newText.Trim();
        if (!message.CanEdit || newText.Length == 0 || newText == message.Text
            || !_messageIndex.TryGetValue(message.Id, out var entry))
        {
            return;
        }

        message.Text = newText.Length > MaxTextLength ? newText[..MaxTextLength] : newText;
        message.IsEdited = true;
        entry.Conversation.RefreshPreview();

        var header = NewHeader(WireTypes.Edit, entry.Conversation);
        header.TargetId = message.Id;
        header.Text = message.Text;
        SendControl(header, Recipients(entry.Conversation));
    }

    public void DeleteMessage(ChatMessage message)
    {
        if (!message.CanDelete || !_messageIndex.TryGetValue(message.Id, out var entry))
        {
            return;
        }

        message.MarkDeleted();
        entry.Conversation.RefreshPreview();
        entry.Conversation.RefreshStatusMarkers();

        var header = NewHeader(WireTypes.Delete, entry.Conversation);
        header.TargetId = message.Id;
        SendControl(header, Recipients(entry.Conversation));
    }

    /// <summary>Adds my reaction, or removes it if I already reacted with that emoji.</summary>
    public void ToggleReaction(ChatMessage message, string emoji)
    {
        if (!message.HasActions || !IsValidEmoji(emoji) || !_messageIndex.TryGetValue(message.Id, out var entry))
        {
            return;
        }

        var on = !(message.Reactions.TryGetValue(emoji, out var people) && people.Contains(Me.Ip));
        if (on && !message.Reactions.ContainsKey(emoji) && message.Reactions.Count >= MaxDistinctReactions)
        {
            return;
        }

        message.SetReaction(emoji, Me.Ip, on);
        message.RefreshReactions(Me.Ip, NameOf);

        var header = NewHeader(WireTypes.React, entry.Conversation);
        header.TargetId = message.Id;
        header.Text = emoji;
        header.On = on;
        SendControl(header, Recipients(entry.Conversation));
        ScheduleSave();
    }

    private ReplyInfo QuoteOf(ChatMessage message)
    {
        var preview = message.Preview;
        return new ReplyInfo
        {
            Id = message.Id,
            SenderName = message.IsMine ? Me.DisplayName : message.Sender?.DisplayName ?? NameOf(message.SenderIp),
            Preview = preview.Length > ReplyInfo.MaxPreviewLength ? preview[..ReplyInfo.MaxPreviewLength] : preview,
        };
    }

    private static bool IsValidEmoji(string? emoji) =>
        !string.IsNullOrWhiteSpace(emoji) && emoji.Length <= MaxEmojiLength;

    // ---- Receiving ------------------------------------------------------------------------------

    /// <summary>The message a packet acts on, if it exists and the sender is in that chat.</summary>
    private (Conversation Conversation, ChatMessage Message)? TargetOf(WireHeader header, Contact sender)
    {
        if (header.TargetId is not { } id || !_messageIndex.TryGetValue(id, out var entry)
            || !entry.Conversation.MemberIps.Contains(sender.Ip))
        {
            return null;
        }

        return entry;
    }

    private void ReceiveEdit(WireHeader header, Contact sender)
    {
        // Only the original author can edit, and only text that still exists.
        if (TargetOf(header, sender) is not { } entry || string.IsNullOrWhiteSpace(header.Text))
        {
            return;
        }

        var (conversation, message) = entry;
        if (message.SenderIp != sender.Ip || message.IsMine || message.IsDeleted || message.Kind != MessageKind.Text)
        {
            return;
        }

        message.Text = header.Text.Length > MaxTextLength ? header.Text[..MaxTextLength] : header.Text;
        message.IsEdited = true;
        conversation.RefreshPreview();
    }

    private void ReceiveDelete(WireHeader header, Contact sender)
    {
        if (TargetOf(header, sender) is not { } entry)
        {
            return;
        }

        var (conversation, message) = entry;
        if (message.SenderIp != sender.Ip || message.IsMine || message.Kind == MessageKind.System)
        {
            return;
        }

        message.MarkDeleted();
        conversation.RefreshPreview();
    }

    private void ReceiveReaction(WireHeader header, Contact sender)
    {
        if (TargetOf(header, sender) is not { } entry || !entry.Message.HasActions || !IsValidEmoji(header.Text))
        {
            return;
        }

        var message = entry.Message;

        var emoji = header.Text!;
        if (header.On && !message.Reactions.ContainsKey(emoji) && message.Reactions.Count >= MaxDistinctReactions)
        {
            return;
        }

        if (message.SetReaction(emoji, sender.Ip, header.On))
        {
            message.RefreshReactions(Me.Ip, NameOf);
        }
    }
}
