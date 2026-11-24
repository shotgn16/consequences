namespace Consequences.Settings;

/// <summary>Where a feature can be switched: server-wide only, or server-wide AND per room by the host.</summary>
public enum FeatureScope { Server, Room }

public record FeatureInfo(string Key, string Label, string Description, bool Default, FeatureScope Scope, string Icon);

/// <summary>
/// Every optional feature of the app. Each one can be switched off server-wide (admin page / Home Assistant
/// app options / environment variables). Room-scoped features can additionally be switched off by the room host.
/// </summary>
public static class FeatureCatalog
{
    public static readonly IReadOnlyList<FeatureInfo> All = new List<FeatureInfo>
    {
        new("drawing",        "Drawing",                 "Draw the people, outfits and meeting places right on the paper.", true,  FeatureScope.Room, "🎨"),
        new("pictureMode",    "Picture Consequences",    "The drawing version: head, body, legs and feet drawn by different players.", true, FeatureScope.Room, "🧍"),
        new("peek",           "Peek over the fold",      "Next player sees the last few words of the previous fold, like a badly folded paper. (Picture mode always shows the lines over the fold.)", true, FeatureScope.Room, "👀"),
        new("timer",          "Round timer",             "Put a countdown on every fold.", true, FeatureScope.Room, "⏱️"),
        new("autoFill",       "Auto-fill",               "Fill in for slow or departed players with a random silly answer.", true, FeatureScope.Room, "🪄"),
        new("inspire",        "Inspire me",              "A dice button that suggests something to write.", true, FeatureScope.Room, "🎲"),
        new("chat",           "Room chat",               "Text chat between players in a room.", true, FeatureScope.Room, "💬"),
        new("reactions",      "Emoji reactions",         "Throw emoji at the screen during the big reveal.", true, FeatureScope.Room, "😂"),
        new("stars",          "Stars & awards",          "Star your favourite lines and drawings; awards and scores at the end.", true, FeatureScope.Room, "⭐"),
        new("readAloud",      "Read aloud",              "Text-to-speech reads each fold as it is unfolded.", true, FeatureScope.Room, "🔊"),
        new("rotateReader",   "Take turns reading",      "Each paper is unfolded by a different player, like passing the papers round to read.", true, FeatureScope.Room, "📜"),
        new("showAuthors",    "Show who wrote what",     "Show the author's name on each fold during the reveal.", true, FeatureScope.Room, "🏷️"),
        new("bots",           "Computer players",        "Add bots to make up the numbers.", true, FeatureScope.Room, "🤖"),
        new("spectators",     "Spectators & late joiners", "Let people watch, or join a game already in progress as a spectator.", true, FeatureScope.Room, "🍿"),
        new("customTemplates","Custom prompts",          "Hosts can write their own set of folds.", true, FeatureScope.Room, "✏️"),
        new("shufflePass",    "Shuffle passing",         "Papers jump to a random player instead of always passing left.", true, FeatureScope.Room, "🔀"),
        new("sounds",         "Sound effects",           "Paper rustles, dings and fanfares.", true, FeatureScope.Room, "🔔"),
        new("wordFilter",     "Family-friendly filter",  "Masks rude words.", false, FeatureScope.Room, "🧼"),
        new("exportImage",    "Save & share images",     "Download or share a finished story as a picture.", true, FeatureScope.Room, "🖼️"),
        new("hallOfFame",     "Hall of Fame",            "Save favourite stories permanently to the server's gallery.", true, FeatureScope.Room, "🏆"),

        new("publicRooms",    "Public room browser",     "List open rooms on the home screen so anyone can join.", true, FeatureScope.Server, "🚪"),
        new("roomPasswords",  "Room passwords",          "Hosts can lock a room with a password.", true, FeatureScope.Server, "🔒"),
        new("qrCode",         "QR join codes",           "Show a QR code so phones can join in a snap.", true, FeatureScope.Server, "📱"),
        new("statsApi",       "Stats API",               "Expose /api/stats for Home Assistant REST sensors.", true, FeatureScope.Server, "📊"),
        new("adminViaIngress","Admin via HA sidebar",    "Open the admin page without a PIN when coming through the Home Assistant sidebar (ingress).", true, FeatureScope.Server, "🏠"),
    };

    public static readonly IReadOnlyDictionary<string, FeatureInfo> ByKey =
        All.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Server level: room features are allowed by default (hosts decide); server features use their default.</summary>
    public static Dictionary<string, bool> Defaults() => All.ToDictionary(f => f.Key, f => f.Scope == FeatureScope.Room || f.Default);

    public static Dictionary<string, bool> RoomDefaults() =>
        All.Where(f => f.Scope == FeatureScope.Room).ToDictionary(f => f.Key, f => f.Default);

    /// <summary>"pictureMode" -> "picture_mode" (Home Assistant option names).</summary>
    public static string ToSnake(string key) =>
        string.Concat(key.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));
}
