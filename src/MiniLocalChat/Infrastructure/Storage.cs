using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniLocalChat.Infrastructure;

/// <summary>
/// Small JSON file store in the data folder. Writes are atomic (temp file + rename), and skipped when
/// the content is what this store last wrote.
/// </summary>
public sealed class Storage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, byte[]> _lastWritten = new();

    public T? Load<T>(string fileName) where T : class
    {
        var path = Path.Combine(AppPaths.DataDir, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // Tolerate a UTF-8 BOM, e.g. after the file was edited by hand.
            ReadOnlySpan<byte> bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<T>(bytes.StartsWith("﻿"u8) ? bytes[3..] : bytes, Options);
        }
        catch (JsonException ex)
        {
            // Keep the broken file around instead of silently overwriting the user's history.
            Log.Error($"Corrupt {fileName}", ex);
            File.Copy(path, path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            return null;
        }
    }

    public void Save<T>(string fileName, T value)
    {
        var path = Path.Combine(AppPaths.DataDir, fileName);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);

        // Most saves change only one of the files (e.g. a reaction touches the chats, not the contacts): skip the rest.
        var hash = SHA256.HashData(bytes);
        lock (_lastWritten)
        {
            if (_lastWritten.TryGetValue(fileName, out var last) && last.AsSpan().SequenceEqual(hash) && File.Exists(path))
            {
                return;
            }
        }

        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
        lock (_lastWritten)
        {
            _lastWritten[fileName] = hash;
        }
    }
}
