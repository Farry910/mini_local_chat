using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Network;

/// <summary>Accepts incoming messages. The sender is identified by the connection's remote IP, never by what it claims.</summary>
public sealed class PeerServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _filesDir;
    private readonly Func<IncomingPacket, Task<bool>> _handler;
    private readonly CancellationTokenSource _cts = new();

    public PeerServer(IPAddress bindAddress, int port, string filesDir, Func<IncomingPacket, Task<bool>> handler)
    {
        _listener = new TcpListener(bindAddress, port);
        _filesDir = filesDir;
        _handler = handler;
    }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
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

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            string? partialFile = null;
            try
            {
                var remote = (IPEndPoint)client.Client.RemoteEndPoint!;
                var senderIp = remote.Address.MapToIPv4().ToString();
                var stream = client.GetStream();
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                idle.CancelAfter(Wire.IdleTimeout);

                var prefix = new byte[8];
                await stream.ReadExactlyAsync(prefix, idle.Token);
                if (!prefix.AsSpan(0, 4).SequenceEqual(Wire.Magic))
                {
                    return;
                }

                var headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(4));
                if (headerLength <= 0 || headerLength > Wire.MaxHeaderBytes)
                {
                    return;
                }

                var headerBytes = new byte[headerLength];
                await stream.ReadExactlyAsync(headerBytes, idle.Token);
                var header = JsonSerializer.Deserialize<WireHeader>(headerBytes, Wire.Json);
                if (header is null || header.FileSize < 0)
                {
                    return;
                }

                string? filePath = null;
                if (header.Kind != MessageKind.Text)
                {
                    var dir = Path.Combine(_filesDir, header.Id.ToString("N"));
                    Directory.CreateDirectory(dir);
                    filePath = Path.Combine(dir, Wire.SafeFileName(header.FileName));
                    partialFile = filePath + ".part";
                    await ReceiveFileAsync(stream, partialFile, header.FileSize, idle);
                    File.Move(partialFile, filePath, overwrite: true);
                    partialFile = null;
                }

                if (await _handler(new IncomingPacket(senderIp, header, filePath)))
                {
                    await stream.WriteAsync(new[] { Wire.AckByte }, idle.Token);
                    await stream.FlushAsync(idle.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or JsonException or UnauthorizedAccessException)
            {
                // Broken or slow connection: the sender didn't get an ACK, so it will retry.
            }
            catch (Exception ex)
            {
                Log.Error("Incoming message", ex);
            }
            finally
            {
                if (partialFile is not null)
                {
                    TryDelete(partialFile);
                }
            }
        }
    }

    private static async Task ReceiveFileAsync(Stream stream, string path, long length, CancellationTokenSource idle)
    {
        var buffer = new byte[81920];
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
        var remaining = length;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), idle.Token);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            await file.WriteAsync(buffer.AsMemory(0, read), idle.Token);
            remaining -= read;
            idle.CancelAfter(Wire.IdleTimeout);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
