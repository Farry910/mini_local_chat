using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Network;

public static class PeerClient
{
    /// <summary>Sends one message. Returns true only when the receiver acknowledged it.</summary>
    public static async Task<bool> SendAsync(IPAddress? localBind, string ip, int port, WireHeader header, string? filePath)
    {
        try
        {
            using var client = localBind is null
                ? new TcpClient(AddressFamily.InterNetwork)
                : new TcpClient(new IPEndPoint(localBind, 0));

            using (var connect = new CancellationTokenSource(Wire.ConnectTimeout))
            {
                await client.ConnectAsync(IPAddress.Parse(ip), port, connect.Token);
            }

            var stream = client.GetStream();
            using var idle = new CancellationTokenSource(Wire.IdleTimeout);

            var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, Wire.Json);
            var prefix = new byte[8];
            Wire.Magic.CopyTo(prefix, 0);
            BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(4), headerBytes.Length);
            await stream.WriteAsync(prefix, idle.Token);
            await stream.WriteAsync(headerBytes, idle.Token);

            if (filePath is not null)
            {
                var buffer = new byte[81920];
                await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, buffer.Length, useAsync: true);
                if (file.Length != header.FileSize)
                {
                    return false;
                }

                int read;
                while ((read = await file.ReadAsync(buffer, idle.Token)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read), idle.Token);
                    idle.CancelAfter(Wire.IdleTimeout);
                }
            }

            await stream.FlushAsync(idle.Token);
            idle.CancelAfter(Wire.IdleTimeout);
            var ack = new byte[1];
            var n = await stream.ReadAsync(ack, idle.Token);
            return n == 1 && ack[0] == Wire.AckByte;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Sending to {ip}", ex);
            return false;
        }
    }
}
