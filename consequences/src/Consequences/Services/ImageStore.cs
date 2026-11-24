using System.Collections.Concurrent;

namespace Consequences.Services;

public record StoredImage(byte[] Bytes, string ContentType, string RoomCode);

/// <summary>Holds drawings in memory for live rooms; they vanish with the room.</summary>
public class ImageStore
{
    public const int MaxBytes = 700 * 1024;
    private readonly ConcurrentDictionary<string, StoredImage> _images = new();

    /// <summary>Decodes a data: URL, checks it really is a PNG/JPEG/WebP and stores it. Returns null if invalid.</summary>
    public string? Add(string roomCode, string? dataUrl)
    {
        var parsed = Parse(dataUrl);
        if (parsed == null) return null;
        var id = Guid.NewGuid().ToString("N");
        _images[id] = new StoredImage(parsed.Value.bytes, parsed.Value.type, roomCode);
        return id;
    }

    public StoredImage? Get(string id) => _images.TryGetValue(id, out var img) ? img : null;

    public void RemoveRoom(string roomCode)
    {
        foreach (var kv in _images.Where(kv => kv.Value.RoomCode == roomCode).ToList())
            _images.TryRemove(kv.Key, out _);
    }

    public int Count => _images.Count;

    public static (byte[] bytes, string type)? Parse(string? dataUrl)
    {
        if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith("data:image/", StringComparison.Ordinal)) return null;
        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl[..comma].EndsWith(";base64", StringComparison.Ordinal)) return null;
        if ((dataUrl.Length - comma) * 3 / 4 > MaxBytes) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]); }
        catch (FormatException) { return null; }
        if (bytes.Length is < 16 or > MaxBytes) return null;

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return (bytes, "image/png");
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return (bytes, "image/jpeg");
        if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return (bytes, "image/webp");
        return null;
    }
}
