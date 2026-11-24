using Consequences.Settings;

namespace Consequences.Game;

public enum Phase { Lobby, Writing, Reveal, Results }

public class Player
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Avatar { get; set; } = "🙂";
    public string Color { get; set; } = "#e76f51";
    public bool IsSpectator { get; set; }
    /// <summary>Joined while a game was running; becomes a player when the room returns to the lobby.</summary>
    public bool LateJoiner { get; set; }
    public bool IsBot { get; set; }
    /// <summary>Left (or was kicked) during a game; their folds are auto-filled.</summary>
    public bool Left { get; set; }
    public bool Kicked { get; set; }
    public int Score { get; set; }
    public HashSet<string> Connections { get; } = new();
    public DateTime? DisconnectedAt { get; set; }
    public DateTime JoinedAt { get; } = DateTime.UtcNow;
    public DateTime LastReaction { get; set; } = DateTime.MinValue;
    public DateTime LastChat { get; set; } = DateTime.MinValue;
    public bool Connected => IsBot || Connections.Count > 0;
}

public class Entry
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string? Text { get; set; }
    public string? ImageId { get; set; }
    public bool AutoFilled { get; set; }
    public HashSet<string> StarredBy { get; } = new();
}

public class Paper
{
    public int Index { get; set; }
    public int StarterSeat { get; set; }
    public Entry?[] Entries { get; set; } = Array.Empty<Entry?>();
    public int Unfolded { get; set; }
    public bool SavedToHallOfFame { get; set; }
    public int Stars => Entries.Sum(e => e?.StarredBy.Count ?? 0);
}

public class ChatMessage
{
    public long Ts { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string From { get; set; } = "";
    public string Color { get; set; } = "";
    public string Text { get; set; } = "";
    public bool System { get; set; }
}

public class RoomSettings
{
    public string RoomName { get; set; } = "";
    public string TemplateId { get; set; } = "classic";
    public List<Step> CustomSteps { get; set; } = new();
    public int TextSeconds { get; set; } = 90;
    public int DrawSeconds { get; set; } = 180;
    public int PeekWords { get; set; } = 3;
    public int MaxPlayers { get; set; } = 12;
    public bool IsPublic { get; set; }
    public string Password { get; set; } = "";
    public Dictionary<string, bool> Features { get; set; } = FeatureCatalog.RoomDefaults();
}

/// <summary>Client → server request to change the room settings (host only).</summary>
public class RoomSettingsInput
{
    public string? RoomName { get; set; }
    public string? TemplateId { get; set; }
    public List<Step>? CustomSteps { get; set; }
    public int? TextSeconds { get; set; }
    public int? DrawSeconds { get; set; }
    public int? PeekWords { get; set; }
    public int? MaxPlayers { get; set; }
    public bool? IsPublic { get; set; }
    public string? Password { get; set; }
    public Dictionary<string, bool>? Features { get; set; }
}

public class JoinRequest
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Color { get; set; }
    public string? Password { get; set; }
    public string? Token { get; set; }
    public bool Spectate { get; set; }
    public string? Pin { get; set; }
    public string? RoomName { get; set; }
}

public record JoinResult(string Code, string PlayerId, string Token);

public class GameException(string message) : Exception(message);
