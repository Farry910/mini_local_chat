using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Network;

namespace MiniLocalChat.Services;

/// <summary>
/// Owns all chat state. Everything here runs on the UI thread; network callbacks are marshalled in.
/// Message actions (edit, delete, react, reply, forward) are in ChatService.Actions.cs,
/// group management in ChatService.Groups.cs.
/// </summary>
public sealed partial class ChatService : ObservableObject
{
    public const int MaxNicknameLength = 40;

    private const string SettingsFile = "settings.json";
    private const string ContactsFile = "contacts.json";
    private const string ConversationsFile = "conversations.json";
    private const string OutboxFile = "outbox.json";
    private const string DirectPrefix = "dm:";
    private const string GroupPrefix = "grp:";
    private const int MaxGroupMembers = 256;
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(17);
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"];

    private readonly Dispatcher _ui;
    private readonly Storage _storage = new();
    private readonly Dictionary<string, Contact> _contactsByIp = new();
    private readonly Dictionary<string, Conversation> _conversationsById = new();
    private readonly Dictionary<Guid, (Conversation Conversation, ChatMessage Message)> _messageIndex = new();
    private readonly HashSet<Guid> _knownMessageIds = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _maintenanceTimer;
    private List<PendingControl> _outbox = [];
    private HashSet<string> _localIps = new();
    private volatile string[] _knownIps = [];
    private PeerServer? _server;
    private PresenceService? _presence;
    private DeliveryQueue? _queue;
    private int _totalUnread;
    private int _maintenanceTicks;
    private bool _loading;

    public ChatService(Dispatcher ui)
    {
        _ui = ui;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => SaveNow();
        _maintenanceTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(5) };
        _maintenanceTimer.Tick += (_, _) => Maintenance();
    }

    /// <summary>A new chat message arrived (not edits, reactions or group changes).</summary>
    public event Action<Conversation, ChatMessage>? MessageReceived;

    public AppSettings Settings { get; private set; } = new();

    public ObservableCollection<Contact> Contacts { get; } = [];

    public ObservableCollection<Conversation> Conversations { get; } = [];

    public Contact Me { get; } = new();

    /// <summary>Test mode: listen and send only on this address, and use it as my identity.</summary>
    public IPAddress? BindAddress { get; private set; }

    /// <summary>Set by the UI: is this conversation on screen right now (so incoming messages are already read)?</summary>
    public Func<Conversation, bool> IsConversationVisible { get; set; } = _ => false;

    public int TotalUnread
    {
        get => _totalUnread;
        private set => Set(ref _totalUnread, value);
    }

    public void Load(string? bindIp)
    {
        Settings = _storage.Load<AppSettings>(SettingsFile) ?? new AppSettings();
        _localIps = NetUtil.GetLocalIPv4Strings();

        if (NetUtil.Normalize(bindIp) is { } bind)
        {
            BindAddress = IPAddress.Parse(bind);
            Me.Ip = bind;
        }
        else
        {
            Me.Ip = ResolveMyIp(Settings.MyIp);
        }

        Me.Nickname = Settings.Nickname;
        Me.Color1 = Settings.AvatarColor1;
        Me.Color2 = Settings.AvatarColor2;
        Me.AvatarId = Settings.AvatarId;

        foreach (var contact in _storage.Load<List<Contact>>(ContactsFile) ?? [])
        {
            if (NetUtil.Normalize(contact.Ip) is { } ip && !IsSelf(ip) && !_contactsByIp.ContainsKey(ip))
            {
                contact.Ip = ip;
                AddContact(contact);
            }
        }

        _loading = true;
        foreach (var conversation in _storage.Load<List<Conversation>>(ConversationsFile) ?? [])
        {
            if (!_conversationsById.ContainsKey(conversation.Id))
            {
                Register(conversation);
            }
        }

        _loading = false;

        // Everyone known gets a chat entry, even if nobody has written yet.
        foreach (var contact in Contacts.ToList())
        {
            EnsureDirect(contact.Ip);
        }

        _outbox = _storage.Load<List<PendingControl>>(OutboxFile) ?? [];
        RecomputeUnread();
    }

    public void Start()
    {
        _server = new PeerServer(BindAddress ?? IPAddress.Any, Settings.TcpPort, AppPaths.FilesDir,
            packet => _ui.InvokeAsync(() => HandleIncoming(packet)).Task);
        _server.Start();

        _presence = new PresenceService(BindAddress, Settings.UdpPort,
            () => new PresencePacket { Nick = Me.Nickname, Colors = MyColors(), Avatar = Me.AvatarId, Away = Me.IsAway, Update = ServableVersion }, () => _knownIps,
            (ip, packet) => _ui.InvokeAsync(() => HandlePresence(ip, packet)));
        _presence.Start();

        var port = Settings.TcpPort;
        _queue = new DeliveryQueue(
            item => PeerClient.SendAsync(BindAddress, item.RecipientIp, port, item.Header, item.FilePath),
            (item, state) => _ui.InvokeAsync(() => OnDeliveryCompleted(item, state)));

        // Anything still undelivered from last time goes back in the queue, in the original order.
        var pending = new List<(DateTime When, OutgoingItem Item)>();
        foreach (var conversation in Conversations)
        {
            foreach (var message in conversation.Messages.Where(m => m.IsMine))
            {
                foreach (var ip in message.Delivery.Where(d => d.Value == DeliveryState.Pending).Select(d => d.Key).ToList())
                {
                    pending.Add((message.SentUtc, CreateOutgoing(conversation, message, ip)));
                }
            }
        }

        pending.AddRange(_outbox.Select(p => (p.Header.SentUtc, new OutgoingItem(null, p.RecipientIp, p.Header, null))));
        foreach (var (_, item) in pending.OrderBy(p => p.When))
        {
            _queue.Enqueue(item);
        }

        _maintenanceTimer.Start();
        StartAwayDetection();
        StartUpdates();
        _presence.Announce(PresenceKinds.Hello, sweepSubnet: true);
    }

    public void Shutdown()
    {
        _maintenanceTimer.Stop();
        StopAwayDetection();
        _presence?.Announce(PresenceKinds.Bye);
        _presence?.Dispose();
        _server?.Dispose();
        _presence = null;
        _server = null;
        SaveNow();
    }

    // ---- Profile -------------------------------------------------------------------------------

    public static string CleanNickname(string? nickname)
    {
        var clean = (nickname ?? "").ReplaceLineEndings(" ").Trim();
        return clean.Length > MaxNicknameLength ? clean[..MaxNicknameLength] : clean;
    }

    public void ApplyProfile(string nickname, string? myIp, string? color1, string? color2, string? avatarId)
    {
        Settings.Nickname = CleanNickname(nickname);
        Settings.MyIp = NetUtil.Normalize(myIp);
        Settings.AvatarColor1 = AvatarPalette.NormalizeHex(color1);
        Settings.AvatarColor2 = AvatarPalette.NormalizeHex(color2);
        Settings.AvatarId = AvatarStore.PathFor(avatarId) is null ? null : avatarId;
        SaveSettings();

        Me.Nickname = Settings.Nickname;
        Me.Color1 = Settings.AvatarColor1;
        Me.Color2 = Settings.AvatarColor2;
        Me.AvatarId = Settings.AvatarId;
        if (BindAddress is null)
        {
            Me.Ip = ResolveMyIp(Settings.MyIp);
        }

        _presence?.Announce(PresenceKinds.Here);
    }

    /// <summary>The global "ripples on/off" switch changed (from Settings, the rail, the floating panel or the LED menu).</summary>
    public event Action? RippleSettingChanged;

    public void SetRippleEnabled(bool enabled)
    {
        if (Settings.RippleEnabled == enabled)
        {
            return;
        }

        Settings.RippleEnabled = enabled;
        SaveSettings();
        RippleSettingChanged?.Invoke();
    }

    public void SaveSettings()
    {
        try
        {
            _storage.Save(SettingsFile, Settings);
        }
        catch (IOException ex)
        {
            Log.Error("Saving settings", ex);
        }
    }

    // ---- Contacts & conversations ---------------------------------------------------------------

    public Contact GetOrCreateContact(string ip)
    {
        if (_contactsByIp.TryGetValue(ip, out var contact))
        {
            return contact;
        }

        contact = new Contact { Ip = ip };
        AddContact(contact);
        if (!_loading)
        {
            EnsureDirect(ip);
        }

        ScheduleSave();
        return contact;
    }

    /// <summary>Adds someone by IP (e.g. when broadcasts don't reach them) and pings them.</summary>
    public Contact? AddContactByIp(string text)
    {
        if (NetUtil.Normalize(text.Trim()) is not { } ip || IsSelf(ip))
        {
            return null;
        }

        var contact = GetOrCreateContact(ip);
        _presence?.SendTo(ip, PresenceKinds.Hello);
        return contact;
    }

    /// <summary>Me, or (in normal mode) any loopback address: a real peer on the LAN never has one.</summary>
    public bool IsSelf(string ip) =>
        ip == Me.Ip || (BindAddress is null && (_localIps.Contains(ip) || IPAddress.IsLoopback(IPAddress.Parse(ip))));

    public string NameOf(string ip) =>
        ip == Me.Ip ? "You" : _contactsByIp.TryGetValue(ip, out var contact) ? contact.DisplayName : ip;

    public Conversation GetOrCreateDirect(string ip) =>
        _conversationsById.TryGetValue(DirectPrefix + ip, out var existing)
            ? existing
            : CreateDirect(ip, DateTime.UtcNow);

    /// <summary>
    /// Discovered people get an empty 1:1 entry so they show up in the chat list automatically.
    /// No activity time, so they sort below real chats.
    /// </summary>
    private void EnsureDirect(string ip)
    {
        if (!_conversationsById.ContainsKey(DirectPrefix + ip))
        {
            CreateDirect(ip, default);
        }
    }

    private Conversation CreateDirect(string ip, DateTime lastActivityUtc)
    {
        var conversation = new Conversation
        {
            Id = DirectPrefix + ip,
            MemberIps = [ip],
            LastActivityUtc = lastActivityUtc,
        };
        Register(conversation);
        ScheduleSave();
        return conversation;
    }

    public Conversation? MostRecentUnread() =>
        Conversations.Where(c => c.UnreadCount > 0).MaxBy(c => c.LastActivityUtc);

    public void MarkRead(Conversation conversation)
    {
        if (conversation.UnreadCount != 0)
        {
            conversation.UnreadCount = 0;
            ScheduleSave();
        }

        ReportSeen(conversation);
    }

    /// <summary>
    /// Read receipt: tells the others in the chat I've seen everything up to the newest message from them,
    /// so their messages show "✓✓ Seen". Sent once per new message.
    /// </summary>
    private void ReportSeen(Conversation conversation)
    {
        var newest = conversation.Messages.LastOrDefault(m => !m.IsMine && m.Kind != MessageKind.System);
        if (newest is null || conversation.LastSeenReported == newest.Id)
        {
            return;
        }

        conversation.LastSeenReported = newest.Id;
        var header = NewHeader(WireTypes.Seen, conversation);
        header.TargetId = newest.Id;
        SendControl(header, Recipients(conversation));
    }

    private void ReceiveSeen(WireHeader header, Contact sender)
    {
        if (TargetOf(header, sender) is not { } entry)
        {
            return;
        }

        // Everything up to that message, in this chat, was seen by them.
        var (conversation, target) = entry;
        var last = conversation.Messages.IndexOf(target);
        var changed = false;
        for (var i = 0; i <= last; i++)
        {
            changed |= conversation.Messages[i].MarkSeenBy(sender.Ip);
        }

        if (changed)
        {
            conversation.RefreshStatusMarkers();
        }
    }

    public void ToggleFavorite(Conversation conversation)
    {
        conversation.IsFavorite = !conversation.IsFavorite;
        ScheduleSave();
    }

    // ---- Sending --------------------------------------------------------------------------------

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private void Post(Conversation conversation, ChatMessage message)
    {
        message.Id = Guid.NewGuid();
        message.IsMine = true;
        message.SenderIp = Me.Ip;
        message.SentUtc = DateTime.UtcNow;
        message.Sender = Me;
        StoppedTypingBySending(conversation);

        var recipients = Recipients(conversation);
        foreach (var ip in recipients)
        {
            message.Delivery[ip] = DeliveryState.Pending;
        }

        _knownMessageIds.Add(message.Id);
        AddToConversation(conversation, message);
        foreach (var ip in recipients)
        {
            _queue?.Enqueue(CreateOutgoing(conversation, message, ip));
        }

        ScheduleSave();
    }

    private List<string> Recipients(Conversation conversation) =>
        conversation.MemberIps.Where(ip => !IsSelf(ip)).ToList();

    private OutgoingItem CreateOutgoing(Conversation conversation, ChatMessage message, string recipientIp)
    {
        var header = NewHeader(WireTypes.Chat, conversation);
        header.Id = message.Id;
        header.SentUtc = message.SentUtc;
        header.Kind = message.Kind;
        header.Text = message.Kind == MessageKind.Text || message.IsSticker ? message.Text : null;
        header.FileName = message.FileName;
        header.FileSize = message.FileSize;
        header.ReplyTo = message.ReplyTo;
        header.Forwarded = message.IsForwarded;
        header.Sticker = message.IsSticker;

        return new OutgoingItem(message, recipientIp, header, message.Kind == MessageKind.Text ? null : message.LocalPath);
    }

    /// <summary>A header with my identity and, for groups, the group's id, name and members.</summary>
    private WireHeader NewHeader(string type, Conversation conversation)
    {
        var header = new WireHeader
        {
            Type = type,
            Id = Guid.NewGuid(),
            Nick = Me.Nickname,
            Colors = MyColors(),
            Avatar = Me.AvatarId,
            SentUtc = DateTime.UtcNow,
        };

        if (conversation.IsGroup)
        {
            header.GroupId = conversation.Id[GroupPrefix.Length..];
            header.GroupName = conversation.GroupName;
            header.Members = [.. conversation.MemberIps, Me.Ip];
        }

        return header;
    }

    /// <summary>Queues an edit/delete/reaction/group change for everyone in the chat; survives restarts.</summary>
    private void SendControl(WireHeader header, IEnumerable<string> recipients)
    {
        _knownMessageIds.Add(header.Id);
        foreach (var ip in recipients.Where(ip => !IsSelf(ip)).Distinct())
        {
            _outbox.Add(new PendingControl { RecipientIp = ip, Header = header });
            _queue?.Enqueue(new OutgoingItem(null, ip, header, null));
        }

        ScheduleSave();
    }

    private void OnDeliveryCompleted(OutgoingItem item, DeliveryState state)
    {
        if (item.Message is { } message)
        {
            message.SetDelivery(item.RecipientIp, state);
            if (_messageIndex.TryGetValue(message.Id, out var entry))
            {
                entry.Conversation.RefreshStatusMarkers();
            }
        }
        else
        {
            _outbox.RemoveAll(p => p.Header.Id == item.Header.Id && p.RecipientIp == item.RecipientIp);
        }

        if (state == DeliveryState.Sent && _contactsByIp.TryGetValue(item.RecipientIp, out var contact))
        {
            MarkSeen(contact);
        }

        ScheduleSave();
    }

    // ---- Receiving ------------------------------------------------------------------------------

    private bool HandleIncoming(IncomingPacket packet)
    {
        var header = packet.Header;
        if (NetUtil.Normalize(packet.SenderIp) is not { } senderIp || IsSelf(senderIp))
        {
            return true;
        }

        var sender = GetOrCreateContact(senderIp);
        if (CleanNickname(header.Nick) is { Length: > 0 } nick)
        {
            sender.Nickname = nick;
        }

        ApplyColors(sender, header.Colors);
        ApplyAvatar(sender, header.Avatar);
        MarkSeen(sender);

        // Already have it: the sender missed our ACK and retried.
        if (!_knownMessageIds.Add(header.Id))
        {
            return true;
        }

        switch (header.Type)
        {
            case WireTypes.Chat:
                ReceiveChat(header, sender, packet.FilePath);
                break;
            case WireTypes.Edit:
                ReceiveEdit(header, sender);
                break;
            case WireTypes.Delete:
                ReceiveDelete(header, sender);
                break;
            case WireTypes.React:
                ReceiveReaction(header, sender);
                break;
            case WireTypes.Group:
                ReceiveGroupChange(header, sender);
                break;
            case WireTypes.Seen:
                ReceiveSeen(header, sender);
                break;
            case WireTypes.Typing:
                ReceiveTyping(header, sender);
                return true; // nothing to save
            case WireTypes.AvatarGet:
                ReceiveAvatarRequest(header, sender);
                return true;
            case WireTypes.Avatar:
                ReceiveAvatar(header, packet.FilePath);
                return true;
            case WireTypes.UpdateGet:
                ReceiveUpdateRequest(header, sender);
                return true;
            case WireTypes.Update:
                ReceiveUpdate(header, sender, packet.FilePath);
                return true;
        }

        ScheduleSave();
        return true;
    }

    private void ReceiveChat(WireHeader header, Contact sender, string? filePath)
    {
        if (FindConversationFor(header, sender.Ip, create: true) is not { } conversation)
        {
            return;
        }

        var message = new ChatMessage
        {
            Id = header.Id,
            SenderIp = sender.Ip,
            SentUtc = header.SentUtc == default ? DateTime.UtcNow : header.SentUtc,
            Kind = header.Kind == MessageKind.System ? MessageKind.Text : header.Kind,
            Text = header.Text ?? "",
            FileName = header.FileName is null ? null : Wire.SafeFileName(header.FileName),
            FileSize = header.FileSize,
            LocalPath = filePath,
            ReplyTo = CleanReply(header.ReplyTo),
            IsForwarded = header.Forwarded,
            IsSticker = header.Sticker && header.Kind == MessageKind.Image && filePath is not null,
            Sender = sender,
        };
        if (message.IsSticker)
        {
            message.Text = Truncate(header.Text, 16);
        }

        AddToConversation(conversation, message);
        conversation.SetTyping(sender, false, default); // they sent it, so they're done typing
        if (IsConversationVisible(conversation))
        {
            ReportSeen(conversation); // it's on screen right now
        }
        else
        {
            conversation.UnreadCount++;
        }

        MessageReceived?.Invoke(conversation, message);
    }

    /// <summary>
    /// The chat a packet belongs to: the sender's 1:1 chat, or the group named in it. Messages for a
    /// group I left are dropped. A group I don't have yet is created from the packet when allowed.
    /// </summary>
    private Conversation? FindConversationFor(WireHeader header, string senderIp, bool create)
    {
        if (string.IsNullOrEmpty(header.GroupId))
        {
            return GetOrCreateDirect(senderIp);
        }

        if (!GroupIdPattern().IsMatch(header.GroupId) || Settings.LeftGroupIds.Contains(header.GroupId))
        {
            return null;
        }

        if (_conversationsById.TryGetValue(GroupPrefix + header.GroupId, out var conversation))
        {
            // Someone I didn't know was in the group: they must have been added while I was away.
            if (!conversation.MemberIps.Contains(senderIp))
            {
                conversation.MemberIps.Add(senderIp);
                conversation.Members.Add(GetOrCreateContact(senderIp));
                conversation.RefreshDisplay();
            }

            return conversation;
        }

        if (!create)
        {
            return null;
        }

        conversation = new Conversation
        {
            Id = GroupPrefix + header.GroupId,
            IsGroup = true,
            GroupName = CleanGroupName(header.GroupName),
            MemberIps = WireMembers(header, senderIp),
            LastActivityUtc = DateTime.UtcNow,
        };
        Register(conversation);
        return conversation;
    }

    /// <summary>The members listed in a packet (minus me), plus its sender.</summary>
    private List<string> WireMembers(WireHeader header, string senderIp) =>
        (header.Members ?? [])
            .Take(MaxGroupMembers)
            .Select(NetUtil.Normalize)
            .OfType<string>()
            .Append(senderIp)
            .Where(ip => !IsSelf(ip))
            .Distinct()
            .ToList();

    private static string? CleanGroupName(string? name)
    {
        var clean = name?.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrEmpty(clean) ? null : clean.Length > 60 ? clean[..60] : clean;
    }

    private static ReplyInfo? CleanReply(ReplyInfo? reply) =>
        reply is null ? null : new ReplyInfo
        {
            Id = reply.Id,
            SenderName = Truncate(reply.SenderName, MaxNicknameLength),
            Preview = Truncate(reply.Preview, ReplyInfo.MaxPreviewLength),
        };

    private static string Truncate(string? text, int max) =>
        text is null ? "" : text.Length > max ? text[..max] : text;

    private void HandlePresence(string rawIp, PresencePacket packet)
    {
        if (NetUtil.Normalize(rawIp) is not { } ip || IsSelf(ip))
        {
            return;
        }

        var contact = GetOrCreateContact(ip);
        if (CleanNickname(packet.Nick) is { Length: > 0 } nick)
        {
            contact.Nickname = nick;
        }

        ApplyColors(contact, packet.Colors);
        ApplyAvatar(contact, packet.Avatar);
        if (packet.Kind == PresenceKinds.Bye)
        {
            contact.IsOnline = false;
            return;
        }

        MarkSeen(contact);
        contact.IsAway = packet.Away;
        ConsiderUpdate(contact, packet.Update);
        if (packet.Kind == PresenceKinds.Hello)
        {
            _presence?.SendTo(ip, PresenceKinds.Here);
        }
    }

    private List<string> MyColors() => [Me.EffectiveColor1, Me.EffectiveColor2];

    /// <summary>Takes the colors the person chose; anything malformed is ignored.</summary>
    private static void ApplyColors(Contact contact, List<string>? colors)
    {
        if (colors is [var first, var second, ..]
            && AvatarPalette.NormalizeHex(first) is { } color1
            && AvatarPalette.NormalizeHex(second) is { } color2)
        {
            contact.Color1 = color1;
            contact.Color2 = color2;
        }
    }

    private void MarkSeen(Contact contact)
    {
        contact.LastSeenUtc = DateTime.UtcNow;
        if (!contact.IsOnline)
        {
            contact.IsOnline = true;
            _queue?.Kick(contact.Ip);
        }
    }

    // ---- Bookkeeping ----------------------------------------------------------------------------

    private string ResolveMyIp(string? preferred) =>
        NetUtil.Normalize(preferred) is { } ip && _localIps.Contains(ip)
            ? ip
            : NetUtil.DetectPrimaryIPv4() ?? IPAddress.Loopback.ToString();

    private void AddContact(Contact contact)
    {
        _contactsByIp[contact.Ip] = contact;
        contact.PropertyChanged += OnContactPropertyChanged;
        Contacts.Add(contact);
        _knownIps = [.. _contactsByIp.Keys];
    }

    private void Register(Conversation conversation)
    {
        conversation.MemberIps = conversation.MemberIps
            .Select(NetUtil.Normalize).OfType<string>().Where(ip => !IsSelf(ip)).Distinct().ToList();
        if (conversation.MemberIps.Count == 0)
        {
            return;
        }

        conversation.Members.Clear();
        conversation.Members.AddRange(conversation.MemberIps.Select(GetOrCreateContact));
        foreach (var message in conversation.Messages)
        {
            message.Sender = message.IsMine ? Me : message.Kind == MessageKind.System ? null : GetOrCreateContact(message.SenderIp);
            _knownMessageIds.Add(message.Id);
            _messageIndex[message.Id] = (conversation, message);
            message.RefreshReactions(Me.Ip, NameOf);
        }

        conversation.RefreshStatusMarkers();
        conversation.PropertyChanged += OnConversationPropertyChanged;
        _conversationsById[conversation.Id] = conversation;
        Conversations.Add(conversation);
        conversation.RefreshDisplay();
    }

    private void AddToConversation(Conversation conversation, ChatMessage message)
    {
        _messageIndex[message.Id] = (conversation, message);
        conversation.AddMessage(message);
    }

    /// <summary>A gray centered line in the chat, e.g. "Alice left the group". Local only.</summary>
    private void AddSystemMessage(Conversation conversation, string text)
    {
        var message = new ChatMessage { Id = Guid.NewGuid(), Kind = MessageKind.System, Text = text, SentUtc = DateTime.UtcNow };
        _knownMessageIds.Add(message.Id);
        AddToConversation(conversation, message);
    }

    /// <summary>Takes a chat off my list (delete chat / leave group).</summary>
    private void RemoveConversation(Conversation conversation)
    {
        conversation.PropertyChanged -= OnConversationPropertyChanged;
        _conversationsById.Remove(conversation.Id);
        foreach (var message in conversation.Messages)
        {
            _messageIndex.Remove(message.Id);
        }

        Conversations.Remove(conversation);
        RecomputeUnread();
        ScheduleSave();
    }

    private void OnContactPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Contact contact)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(Contact.RippleBlocked):
                ScheduleSave();
                break;
            case nameof(Contact.IsOnline):
                foreach (var conversation in Conversations.Where(c => c.Members.Contains(contact)))
                {
                    conversation.RefreshPresence();
                }

                break;
            case nameof(Contact.Nickname) or nameof(Contact.Color1) or nameof(Contact.Color2) or nameof(Contact.AvatarId) or nameof(Contact.AvatarImage):
                foreach (var conversation in Conversations.Where(c => c.Members.Contains(contact)))
                {
                    conversation.RefreshDisplay();
                }

                ScheduleSave();
                break;
        }
    }

    private void OnConversationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Conversation.UnreadCount))
        {
            RecomputeUnread();
        }
    }

    private void RecomputeUnread() => TotalUnread = Conversations.Sum(c => c.UnreadCount);

    private void Maintenance()
    {
        var cutoff = DateTime.UtcNow - OfflineAfter;
        foreach (var contact in Contacts.Where(c => c.IsOnline && c.LastSeenUtc < cutoff))
        {
            contact.IsOnline = false;
        }

        _maintenanceTicks++;
        if (_maintenanceTicks % 3 == 0)
        {
            _queue?.KickAll();
        }

        if (_maintenanceTicks % 12 == 0)
        {
            _localIps = NetUtil.GetLocalIPv4Strings();
        }
    }

    private void ScheduleSave()
    {
        if (!_saveTimer.IsEnabled)
        {
            _saveTimer.Start();
        }
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _storage.Save(ContactsFile, Contacts.ToList());
            _storage.Save(ConversationsFile, Conversations.ToList());
            _storage.Save(OutboxFile, _outbox);
        }
        catch (IOException ex)
        {
            Log.Error("Saving chats", ex);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex GroupIdPattern();
}
