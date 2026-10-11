using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace MiniLocalChat.Network;

/// <summary>
/// UDP heartbeat: who is online and what their nickname is. Sent as LAN broadcast and also
/// directly to every known contact, in case the network drops broadcasts.
/// </summary>
public sealed class PresenceService : IDisposable
{
    private const int SioUdpConnReset = unchecked((int)0x9800000C);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly IPAddress? _bind;
    private readonly int _port;
    private readonly Func<string> _nickname;
    private readonly Func<IReadOnlyCollection<string>> _knownIps;
    private readonly Action<string, PresencePacket> _onPacket;
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;
    private Timer? _timer;

    public PresenceService(IPAddress? bind, int port, Func<string> nickname, Func<IReadOnlyCollection<string>> knownIps, Action<string, PresencePacket> onPacket)
    {
        _bind = bind;
        _port = port;
        _nickname = nickname;
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
        _timer = new Timer(_ => Announce(PresenceKinds.Here), null, Interval, Interval);
    }

    public void Announce(string kind)
    {
        var bytes = Serialize(kind);
        foreach (var target in Targets())
        {
            SendRaw(bytes, target);
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

    private byte[] Serialize(string kind) =>
        JsonSerializer.SerializeToUtf8Bytes(new PresencePacket { Kind = kind, Nick = _nickname() }, Wire.Json);

    private HashSet<IPAddress> Targets()
    {
        var targets = new HashSet<IPAddress>();
        if (_bind is null || !IPAddress.IsLoopback(_bind))
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
