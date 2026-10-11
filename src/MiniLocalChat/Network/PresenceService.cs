using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace MiniLocalChat.Network;

/// <summary>
/// UDP heartbeat: who is online and what their nickname is. Sent as LAN broadcast and directly to
/// every known contact. In case the network drops broadcasts, a "hello" also goes to every address
/// on the local subnet at startup and once a minute; whoever runs the app answers.
/// </summary>
public sealed class PresenceService : IDisposable
{
    private const int SioUdpConnReset = unchecked((int)0x9800000C);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int SweepEveryTicks = 12;

    private readonly IPAddress? _bind;
    private readonly int _port;
    private readonly Func<PresencePacket> _describeSelf;
    private readonly Func<IReadOnlyCollection<string>> _knownIps;
    private readonly Action<string, PresencePacket> _onPacket;
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;
    private Timer? _timer;
    private int _ticks;

    /// <param name="describeSelf">My current nickname and colors; called from a timer thread.</param>
    public PresenceService(IPAddress? bind, int port, Func<PresencePacket> describeSelf, Func<IReadOnlyCollection<string>> knownIps, Action<string, PresencePacket> onPacket)
    {
        _bind = bind;
        _port = port;
        _describeSelf = describeSelf;
        _knownIps = knownIps;
        _onPacket = onPacket;
    }

    public void Start()
    {
        var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        // Otherwise an ICMP "port unreachable" from an offline peer makes the next receive throw.
        udp.Client.IOControl(SioUdpConnReset, new byte[4], null);
        udp.Client.Bind(new IPEndPoint(_bind ?? IPAddress.Any, _port));
        _udp = udp;

        _ = ReceiveLoopAsync();
        _timer = new Timer(_ => OnTick(), null, Interval, Interval);
    }

    public void Announce(string kind, bool sweepSubnet = false)
    {
        var bytes = Serialize(kind);
        foreach (var target in Targets())
        {
            SendRaw(bytes, target);
        }

        if (sweepSubnet && !IsLoopbackOnly)
        {
            var hello = Serialize(PresenceKinds.Hello);
            foreach (var host in NetUtil.GetSubnetHosts())
            {
                SendRaw(hello, host);
            }
        }
    }

    public void SendTo(string ip, string kind)
    {
        if (IPAddress.TryParse(ip, out var address))
        {
            SendRaw(Serialize(kind), address);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cts.Cancel();
        _udp?.Dispose();
        _cts.Dispose();
    }

    private bool IsLoopbackOnly => _bind is not null && IPAddress.IsLoopback(_bind);

    private void OnTick()
    {
        _ticks++;
        Announce(PresenceKinds.Here, sweepSubnet: _ticks % SweepEveryTicks == 0);
    }

    private byte[] Serialize(string kind)
    {
        var packet = _describeSelf();
        packet.Kind = kind;
        return JsonSerializer.SerializeToUtf8Bytes(packet, Wire.Json);
    }

    private HashSet<IPAddress> Targets()
    {
        var targets = new HashSet<IPAddress>();
        if (!IsLoopbackOnly)
        {
            targets.Add(IPAddress.Broadcast);
            targets.UnionWith(NetUtil.GetBroadcastAddresses());
        }

        foreach (var ip in _knownIps())
        {
            if (IPAddress.TryParse(ip, out var address))
            {
                targets.Add(address);
            }
        }

        return targets;
    }

    private void SendRaw(byte[] bytes, IPAddress address)
    {
        try
        {
            _udp?.Send(bytes, bytes.Length, new IPEndPoint(address, _port));
        }
        catch (SocketException)
        {
            // Unreachable network or broadcast not allowed; the heartbeat simply tries again later.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp!.ReceiveAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            PresencePacket? packet;
            try
            {
                packet = JsonSerializer.Deserialize<PresencePacket>(result.Buffer, Wire.Json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (packet?.App == Wire.AppId)
            {
                _onPacket(result.RemoteEndPoint.Address.MapToIPv4().ToString(), packet);
            }
        }
    }
}
