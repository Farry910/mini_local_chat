using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniLocalChat.Infrastructure;

/// <summary>Small JSON file store in the data folder. Writes are atomic (temp file + rename).</summary>
public sealed class Storage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

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
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
