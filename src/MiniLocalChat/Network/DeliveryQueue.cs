using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Network;

/// <param name="Message">The chat message being delivered, or null for an edit/delete/reaction/group change.</param>
public sealed record OutgoingItem(ChatMessage? Message, string RecipientIp, WireHeader Header, string? FilePath);

/// <summary>
/// Per-recipient FIFO of undelivered messages. Each recipient drains in order; on the first failure
/// (recipient offline) it stops and waits for <see cref="Kick"/> (they came online, or the retry timer).
/// </summary>
public sealed class DeliveryQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<OutgoingItem>> _pending = new();
    private readonly HashSet<string> _draining = new();
    private readonly Func<OutgoingItem, Task<bool>> _send;
    private readonly Action<OutgoingItem, DeliveryState> _completed;

    public DeliveryQueue(Func<OutgoingItem, Task<bool>> send, Action<OutgoingItem, DeliveryState> completed)
    {
        _send = send;
        _completed = completed;
    }

    public void Enqueue(OutgoingItem item)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(item.RecipientIp, out var list))
            {
                _pending[item.RecipientIp] = list = [];
            }

            list.Add(item);
        }

        Kick(item.RecipientIp);
    }

    public void KickAll()
    {
        string[] ips;
        lock (_gate)
        {
            ips = _pending.Where(p => p.Value.Count > 0).Select(p => p.Key).ToArray();
        }

        foreach (var ip in ips)
        {
            Kick(ip);
        }
    }

    public void Kick(string ip)
    {
        lock (_gate)
        {
            if (_draining.Contains(ip) || !_pending.TryGetValue(ip, out var list) || list.Count == 0)
            {
                return;
            }

            _draining.Add(ip);
        }

        _ = Task.Run(() => DrainAsync(ip));
    }

    private async Task DrainAsync(string ip)
    {
        try
        {
            while (true)
            {
                OutgoingItem item;
                lock (_gate)
                {
                    if (!_pending.TryGetValue(ip, out var list) || list.Count == 0)
                    {
                        _draining.Remove(ip);
                        return;
                    }

                    item = list[0];
                }

                if (item.FilePath is not null && !File.Exists(item.FilePath))
                {
                    Remove(item);
                    _completed(item, DeliveryState.Failed);
                    continue;
                }

                if (!await _send(item))
                {
                    lock (_gate)
                    {
                        _draining.Remove(ip);
                    }

                    return;
                }

                Remove(item);
                _completed(item, DeliveryState.Sent);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Delivery to {ip}", ex);
            lock (_gate)
            {
                _draining.Remove(ip);
            }
        }
    }

    private void Remove(OutgoingItem item)
    {
        lock (_gate)
        {
            _pending[item.RecipientIp].Remove(item);
        }
    }
}
