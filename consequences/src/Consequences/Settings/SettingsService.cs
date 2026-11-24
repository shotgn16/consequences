using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consequences.Settings;

public class ServerSettings
{
    public string AppName { get; set; } = "Consequences";
    /// <summary>Public URL used for share links and QR codes, e.g. https://consequences.example.com</summary>
    public string PublicUrl { get; set; } = "";
    /// <summary>If set, creating a room requires this PIN (handy when exposed publicly).</summary>
    public string CreateRoomPin { get; set; } = "";
    public int MaxRooms { get; set; } = 50;
    public int MaxPlayersPerRoom { get; set; } = 16;
    public int RoomIdleMinutes { get; set; } = 120;
    public int DefaultTextSeconds { get; set; } = 90;
    public int DefaultDrawSeconds { get; set; } = 180;
    public List<string> ExtraBannedWords { get; set; } = new();
    public Dictionary<string, bool> Features { get; set; } = FeatureCatalog.Defaults();

    public bool On(string key) => Features.TryGetValue(key, out var v) ? v : FeatureCatalog.Defaults().GetValueOrDefault(key);

    public ServerSettings Clone() => JsonSerializer.Deserialize<ServerSettings>(JsonSerializer.Serialize(this))!;
}

/// <summary>
/// Settings are layered: built-in defaults ← Home Assistant /data/options.json ← environment variables ← admin page (settings.json).
/// If the HA options / environment change (e.g. you flip a toggle in the HA app config and restart),
/// those win again and earlier admin-page overrides are dropped, so the most recent change always wins.
/// </summary>
public class SettingsService
{
    private readonly ILogger<SettingsService> _log;
    private readonly object _gate = new();
    private ServerSettings _current;
    private readonly ServerSettings _seed;
    private readonly string _seedHash;

    public string DataPath { get; }
    public string AdminPin { get; }
    public bool RunningInHomeAssistant { get; }
    public event Action<ServerSettings>? Changed;

    private string SettingsFile => Path.Combine(DataPath, "settings.json");

    public SettingsService(ILogger<SettingsService> log, IConfiguration config)
    {
        _log = log;
        DataPath = Environment.GetEnvironmentVariable("CONSEQ_DATA_PATH")
                   ?? (Directory.Exists("/data") ? "/data" : Path.Combine(AppContext.BaseDirectory, "data"));
        Directory.CreateDirectory(DataPath);

        var optionsFile = Environment.GetEnvironmentVariable("CONSEQ_HA_OPTIONS") ?? "/data/options.json";
        RunningInHomeAssistant = File.Exists(optionsFile);

        _seed = new ServerSettings();
        var adminPin = "";
        if (RunningInHomeAssistant)
        {
            try { adminPin = ApplyHomeAssistantOptions(_seed, File.ReadAllText(optionsFile)); }
            catch (Exception ex) { _log.LogWarning(ex, "Could not read Home Assistant options from {File}", optionsFile); }
        }
        adminPin = ApplyEnvironment(_seed, adminPin);
        AdminPin = adminPin;
        _seedHash = Hash(JsonSerializer.Serialize(_seed));

        _current = _seed.Clone();
        LoadOverrides();
    }

    public ServerSettings Current { get { lock (_gate) return _current; } }

    public void Save(ServerSettings updated)
    {
        // never trust unknown feature keys from the client
        var features = FeatureCatalog.Defaults();
        foreach (var k in features.Keys.ToList())
            if (updated.Features.TryGetValue(k, out var v)) features[k] = v;
        updated.Features = features;
        updated.MaxRooms = Math.Clamp(updated.MaxRooms, 1, 1000);
        updated.MaxPlayersPerRoom = Math.Clamp(updated.MaxPlayersPerRoom, 2, 40);
        updated.RoomIdleMinutes = Math.Clamp(updated.RoomIdleMinutes, 5, 7 * 24 * 60);
        updated.DefaultTextSeconds = Math.Clamp(updated.DefaultTextSeconds, 10, 900);
        updated.DefaultDrawSeconds = Math.Clamp(updated.DefaultDrawSeconds, 10, 1800);
        updated.AppName = string.IsNullOrWhiteSpace(updated.AppName) ? "Consequences" : updated.AppName.Trim()[..Math.Min(40, updated.AppName.Trim().Length)];
        updated.PublicUrl = (updated.PublicUrl ?? "").Trim().TrimEnd('/');
        updated.ExtraBannedWords = updated.ExtraBannedWords.Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0).Distinct().Take(500).ToList();

        lock (_gate)
        {
            _current = updated;
            var doc = new JsonObject
            {
                ["seedHash"] = _seedHash,
                ["settings"] = JsonSerializer.SerializeToNode(updated)
            };
            File.WriteAllText(SettingsFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Changed?.Invoke(updated);
    }

    public void ResetToDefaults()
    {
        lock (_gate)
        {
            _current = _seed.Clone();
            if (File.Exists(SettingsFile)) File.Delete(SettingsFile);
        }
        Changed?.Invoke(_current);
    }

    private void LoadOverrides()
    {
        if (!File.Exists(SettingsFile)) return;
        try
        {
            var doc = JsonNode.Parse(File.ReadAllText(SettingsFile))!.AsObject();
            if ((string?)doc["seedHash"] != _seedHash)
            {
                _log.LogInformation("App options changed since the admin page was last saved; using the new options.");
                File.Delete(SettingsFile);
                return;
            }
            var s = doc["settings"].Deserialize<ServerSettings>();
            if (s != null)
            {
                foreach (var (k, v) in FeatureCatalog.Defaults())
                    s.Features.TryAdd(k, _seed.Features.GetValueOrDefault(k, v));
                _current = s;
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Ignoring unreadable settings.json"); }
    }

    /// <summary>Reads the flat Home Assistant options (admin_pin, feature_drawing, ...). Returns the admin pin.</summary>
    private static string ApplyHomeAssistantOptions(ServerSettings s, string json)
    {
        var o = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        string Str(string k, string d) => o[k] is JsonValue v && v.TryGetValue<string>(out var r) ? r : d;
        int Int(string k, int d) => o[k] is JsonValue v && v.TryGetValue<int>(out var r) ? r : d;

        s.AppName = Str("app_name", s.AppName);
        s.PublicUrl = Str("public_url", s.PublicUrl).Trim().TrimEnd('/');
        s.CreateRoomPin = Str("create_room_pin", s.CreateRoomPin);
        s.MaxRooms = Int("max_rooms", s.MaxRooms);
        s.MaxPlayersPerRoom = Int("max_players_per_room", s.MaxPlayersPerRoom);
        s.RoomIdleMinutes = Int("room_idle_minutes", s.RoomIdleMinutes);
        s.DefaultTextSeconds = Int("default_text_seconds", s.DefaultTextSeconds);
        s.DefaultDrawSeconds = Int("default_draw_seconds", s.DefaultDrawSeconds);
        if (o["extra_banned_words"] is JsonArray arr)
            s.ExtraBannedWords = arr.Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToList();

        foreach (var f in FeatureCatalog.All)
        {
            var key = "feature_" + FeatureCatalog.ToSnake(f.Key);
            if (o[key] is JsonValue v && v.TryGetValue<bool>(out var b)) s.Features[f.Key] = b;
        }
        return Str("admin_pin", "");
    }

    /// <summary>Environment variables (Docker): CONSEQ_ADMIN_PIN, CONSEQ_PUBLIC_URL, CONSEQ_FEATURE_DRAWING=false ...</summary>
    private static string ApplyEnvironment(ServerSettings s, string adminPin)
    {
        string? E(string k) => Environment.GetEnvironmentVariable("CONSEQ_" + k);
        if (E("APP_NAME") is { } an) s.AppName = an;
        if (E("PUBLIC_URL") is { } pu) s.PublicUrl = pu.Trim().TrimEnd('/');
        if (E("CREATE_ROOM_PIN") is { } cp) s.CreateRoomPin = cp;
        if (int.TryParse(E("MAX_ROOMS"), out var mr)) s.MaxRooms = mr;
        if (int.TryParse(E("MAX_PLAYERS_PER_ROOM"), out var mp)) s.MaxPlayersPerRoom = mp;
        if (int.TryParse(E("ROOM_IDLE_MINUTES"), out var ri)) s.RoomIdleMinutes = ri;
        if (int.TryParse(E("DEFAULT_TEXT_SECONDS"), out var ts)) s.DefaultTextSeconds = ts;
        if (int.TryParse(E("DEFAULT_DRAW_SECONDS"), out var ds)) s.DefaultDrawSeconds = ds;
        if (E("EXTRA_BANNED_WORDS") is { } bw) s.ExtraBannedWords = bw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var f in FeatureCatalog.All)
        {
            var v = E("FEATURE_" + FeatureCatalog.ToSnake(f.Key).ToUpperInvariant());
            if (bool.TryParse(v, out var b)) s.Features[f.Key] = b;
        }
        return E("ADMIN_PIN") ?? adminPin;
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
