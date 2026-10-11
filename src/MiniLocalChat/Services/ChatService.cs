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
/// </summary>
public sealed partial class ChatService : ObservableObject
{
    public const int MaxNicknameLength = 40;

    private const string SettingsFile = "settings.json";
    private const string ContactsFile = "contacts.json";
    private const string ConversationsFile = "conversations.json";
    private const string DirectPrefix = "dm:";
    private const string GroupPrefix = "grp:";
    private const int MaxGroupMembers = 256;
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(17);
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"];

    private readonly Dispatcher _ui;
    private readonly Storage _storage = new();
    private readonly Dictionary<string, Contact> _contactsByIp = new();
    private readonly Dictionary<string, Conversation> _conversationsById = new();
    private readonly HashSet<Guid> _knownMessageIds = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _maintenanceTimer;
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

        RecomputeUnread();
    }

    public void Start()
    {
        _server = new PeerServer(BindAddress ?? IPAddress.Any, Settings.TcpPort, AppPaths.FilesDir,
            packet => _ui.InvokeAsync(() => HandleIncoming(packet)).Task);
        _server.Start();

        _presence = new PresenceService(BindAddress, Settings.UdpPort, () => Me.Nickname, () => _knownIps,
            (ip, packet) => _ui.InvokeAsync(() => HandlePresence(ip, packet)));
        _presence.Start();

        var port = Settings.TcpPort;
        _queue = new DeliveryQueue(
            item => PeerClient.SendAsync(BindAddress, item.RecipientIp, port, item.Header, item.FilePath),
            (item, state) => _ui.InvokeAsync(() => OnDeliveryCompleted(item, state)));

        // Anything still undelivered from last time goes back in the queue.
        foreach (var conversation in Conversations)
        {
            foreach (var message in conversation.Messages.Where(m => m.IsMine))
            {
                foreach (var ip in message.Delivery.Where(d => d.Value == DeliveryState.Pending).Select(d => d.Key).ToList())
                {
                    _queue.Enqueue(CreateOutgoing(conversation, message, ip));
                }
            }
        }

        _maintenanceTimer.Start();
        _presence.Announce(PresenceKinds.Hello, sweepSubnet: true);
    }

    public void Shutdown()
    {
        _maintenanceTimer.Stop();
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

    public void ApplyProfile(string nickname, string? myIp)
    {
        Settings.Nickname = CleanNickname(nickname);
        Settings.MyIp = NetUtil.Normalize(myIp);
        SaveSettings();

        Me.Nickname = Settings.Nickname;
        if (BindAddress is null)
        {
            Me.Ip = ResolveMyIp(Settings.MyIp);
        }

        _presence?.Announce(PresenceKinds.Here);
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
            GroupName = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            MemberIps = members,
            LastActivityUtc = DateTime.UtcNow,
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
    }

    // ---- Sending --------------------------------------------------------------------------------

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public void SendText(Conversation conversation, string text)
    {
        text = text.Trim();
        if (text.Length > 0)
        {
            Post(conversation, new ChatMessage { Kind = MessageKind.Text, Text = text });
        }
    }

    public void SendFile(Conversation conversation, string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return;
        }

        Post(conversation, new ChatMessage
        {
            Kind = IsImageFile(path) ? MessageKind.Image : MessageKind.File,
            FileName = info.Name,
            FileSize = info.Length,
            LocalPath = info.FullName,
        });
    }

    private void Post(Conversation conversation, ChatMessage message)
    {
        message.Id = Guid.NewGuid();
        message.IsMine = true;
        message.SenderIp = Me.Ip;
        message.SentUtc = DateTime.UtcNow;
        message.Sender = Me;

        var recipients = conversation.MemberIps.Where(ip => !IsSelf(ip)).ToList();
        foreach (var ip in recipients)
        {
            message.Delivery[ip] = DeliveryState.Pending;
        }

        _knownMessageIds.Add(message.Id);
        conversation.AddMessage(message);
        foreach (var ip in recipients)
        {
            _queue?.Enqueue(CreateOutgoing(conversation, message, ip));
        }

        ScheduleSave();
    }

    private OutgoingItem CreateOutgoing(Conversation conversation, ChatMessage message, string recipientIp)
    {
        var header = new WireHeader
        {
            Id = message.Id,
            Nick = Me.Nickname,
            Kind = message.Kind,
            Text = message.Kind == MessageKind.Text ? message.Text : null,
            FileName = message.FileName,
            FileSize = message.FileSize,
            SentUtc = message.SentUtc,
        };

        if (conversation.IsGroup)
        {
            header.GroupId = conversation.Id[GroupPrefix.Length..];
            header.GroupName = conversation.GroupName;
            header.Members = [.. conversation.MemberIps, Me.Ip];
        }

        return new OutgoingItem(message, recipientIp, header, message.Kind == MessageKind.Text ? null : message.LocalPath);
    }

    private void OnDeliveryCompleted(OutgoingItem item, DeliveryState state)
    {
        item.Message.SetDelivery(item.RecipientIp, state);
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

        MarkSeen(sender);

        // Already have it: the sender missed our ACK and retried.
        if (header.Type != "chat" || !_knownMessageIds.Add(header.Id))
        {
            return true;
        }

        Conversation conversation;
        if (string.IsNullOrEmpty(header.GroupId))
        {
            conversation = GetOrCreateDirect(senderIp);
        }
        else if (GroupIdPattern().IsMatch(header.GroupId))
        {
            conversation = GetOrCreateGroupFromWire(header, senderIp);
        }
        else
        {
            return true;
        }

        var message = new ChatMessage
        {
            Id = header.Id,
            SenderIp = senderIp,
            SentUtc = header.SentUtc == default ? DateTime.UtcNow : header.SentUtc,
            Kind = header.Kind,
            Text = header.Text ?? "",
            FileName = header.FileName is null ? null : Wire.SafeFileName(header.FileName),
            FileSize = header.FileSize,
            LocalPath = packet.FilePath,
            Sender = sender,
        };

        conversation.AddMessage(message);
        if (!IsConversationVisible(conversation))
        {
            conversation.UnreadCount++;
        }

        MessageReceived?.Invoke(conversation, message);
        ScheduleSave();
        return true;
    }

    private Conversation GetOrCreateGroupFromWire(WireHeader header, string senderIp)
    {
        var id = GroupPrefix + header.GroupId;
        var members = (header.Members ?? [])
            .Take(MaxGroupMembers)
            .Select(NetUtil.Normalize)
            .OfType<string>()
            .Append(senderIp)
            .Where(ip => !IsSelf(ip))
            .Distinct()
            .ToList();
        var groupName = string.IsNullOrWhiteSpace(header.GroupName) ? null : header.GroupName.Trim();

        if (_conversationsById.TryGetValue(id, out var conversation))
        {
            foreach (var ip in members.Where(ip => !conversation.MemberIps.Contains(ip)))
            {
                conversation.MemberIps.Add(ip);
                conversation.Members.Add(GetOrCreateContact(ip));
            }

            if (groupName is not null)
            {
                conversation.GroupName = groupName;
            }

            conversation.RefreshDisplay();
            return conversation;
        }

        conversation = new Conversation
        {
            Id = id,
            IsGroup = true,
            GroupName = groupName,
            MemberIps = members,
            LastActivityUtc = DateTime.UtcNow,
        };
        Register(conversation);
        return conversation;
    }

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

        if (packet.Kind == PresenceKinds.Bye)
        {
            contact.IsOnline = false;
            return;
        }

        MarkSeen(contact);
        if (packet.Kind == PresenceKinds.Hello)
        {
            _presence?.SendTo(ip, PresenceKinds.Here);
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
            message.Sender = message.IsMine ? Me : GetOrCreateContact(message.SenderIp);
            _knownMessageIds.Add(message.Id);
        }

        conversation.PropertyChanged += OnConversationPropertyChanged;
        _conversationsById[conversation.Id] = conversation;
        Conversations.Add(conversation);
        conversation.RefreshDisplay();
    }

    private void OnContactPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Contact.Nickname) && sender is Contact contact)
        {
            foreach (var conversation in Conversations.Where(c => c.Members.Contains(contact)))
            {
                conversation.RefreshDisplay();
            }

            ScheduleSave();
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
        }
        catch (IOException ex)
        {
            Log.Error("Saving chats", ex);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex GroupIdPattern();
}
