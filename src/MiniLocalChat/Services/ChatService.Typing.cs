using System.Windows.Threading;
using MiniLocalChat.Models;
using MiniLocalChat.Network;

namespace MiniLocalChat.Services;

/// <summary>"Alice is typing…" and clearing a chat's history.</summary>
public sealed partial class ChatService
{
    /// <summary>While typing, "typing" is repeated this often…</summary>
    private static readonly TimeSpan TypingRepeat = TimeSpan.FromSeconds(3);

    /// <summary>…and the others stop showing it this long after the last one.</summary>
    private static readonly TimeSpan TypingTimeout = TimeSpan.FromSeconds(6);

    /// <summary>Chats I'm typing in → when I last told them.</summary>
    private readonly Dictionary<string, DateTime> _typingSentUtc = new();

    private DispatcherTimer? _typingTimer;

    /// <summary>
    /// Call on every change of the message box: tells the others in the chat (who are online) that
    /// I'm typing, at most every few seconds; or that I stopped (the box was emptied, or I left the chat).
    /// </summary>
    public void NotifyTyping(Conversation conversation, bool typing)
    {
        var now = DateTime.UtcNow;
        if (typing)
        {
            if (_typingSentUtc.TryGetValue(conversation.Id, out var last) && now - last < TypingRepeat)
            {
                return;
            }

            _typingSentUtc[conversation.Id] = now;
        }
        else if (!_typingSentUtc.Remove(conversation.Id))
        {
            return; // they weren't told I was typing
        }

        var header = NewHeader(WireTypes.Typing, conversation);
        header.On = typing;
        foreach (var ip in Recipients(conversation))
        {
            if (_contactsByIp.TryGetValue(ip, out var contact) && contact.IsOnline)
            {
                SendDirect(ip, header, null);
            }
        }
    }

    /// <summary>Sending the message ends "typing" for the others by itself.</summary>
    private void StoppedTypingBySending(Conversation conversation) => _typingSentUtc.Remove(conversation.Id);

    private void ReceiveTyping(WireHeader header, Contact sender)
    {
        var conversation = string.IsNullOrEmpty(header.GroupId)
            ? _conversationsById.GetValueOrDefault(DirectPrefix + sender.Ip) // don't create a chat just for this
            : FindConversationFor(header, sender.Ip, create: false);
        if (conversation is null || !conversation.MemberIps.Contains(sender.Ip))
        {
            return;
        }

        conversation.SetTyping(sender, header.On, DateTime.UtcNow + TypingTimeout);
        if (conversation.IsTyping && _typingTimer is not { IsEnabled: true })
        {
            _typingTimer ??= CreateTypingTimer();
            _typingTimer.Start();
        }
    }

    private DispatcherTimer CreateTypingTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var anyone = false;
            foreach (var conversation in Conversations)
            {
                anyone |= conversation.ExpireTyping(now);
            }

            if (!anyone)
            {
                timer.Stop();
            }
        };
        return timer;
    }

    /// <summary>Removes every message in the chat from this computer. The others keep theirs.</summary>
    public void ClearHistory(Conversation conversation)
    {
        foreach (var message in conversation.Messages)
        {
            _messageIndex.Remove(message.Id);
        }

        conversation.ClearMessages();
        RecomputeUnread();
        ScheduleSave();
    }
}
