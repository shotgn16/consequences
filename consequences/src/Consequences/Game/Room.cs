using Consequences.Services;
using Consequences.Settings;

namespace Consequences.Game;

/// <summary>
/// One game room. Not thread-safe by itself: every call must be made while holding <see cref="Gate"/>
/// (RoomManager does this for you).
/// </summary>
public class Room
{
    public static readonly string[] Avatars = { "🦊", "🐸", "🐼", "🦄", "🐙", "🦖", "🐝", "🦉", "🐧", "🐯", "🐨", "🦔", "🐵", "🐶", "🐱", "🐷", "👽", "👻", "🤠", "🧙", "🧛", "🧜", "🥷", "🤡" };
    public static readonly string[] Colors = { "#e63946", "#f4a261", "#e9c46a", "#2a9d8f", "#457b9d", "#8338ec", "#ff006e", "#3a86ff", "#06d6a0", "#fb5607", "#6d597a", "#118ab2" };
    public static readonly string[] Reactions = { "😂", "🤣", "😱", "😍", "🤢", "👏", "🔥", "💀", "🙈", "🤯", "❤️", "🍆" };
    private static readonly string[] BotNames = { "Robo-Nan", "Beep McBoop", "Clanky", "HAL 9001", "Botty McBotface", "R2-Tea2", "Sir Compute-a-lot", "Toaster Steve" };

    public object Gate { get; } = new();
    public string Code { get; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;
    public string HostId { get; private set; } = "";
    public Phase Phase { get; private set; } = Phase.Lobby;
    public RoomSettings Settings { get; } = new();
    public List<Player> Players { get; } = new();
    public List<ChatMessage> Chat { get; } = new();
    public int GamesPlayed { get; private set; }
    public bool Closed { get; set; }

    // --- game state ---
    public Template? Active { get; private set; }
    public List<string> Seats { get; } = new();
    public List<Paper> Papers { get; } = new();
    public int Round { get; private set; }
    private readonly List<int> _offsets = new();
    public DateTime? Deadline { get; private set; }
    private readonly HashSet<string> _submitted = new();
    private readonly Dictionary<string, DateTime> _botDue = new();
    public int RevealPaper { get; private set; }

    private readonly Func<ServerSettings> _server;
    private readonly ImageStore _images;

    public Room(string code, Func<ServerSettings> server, ImageStore images)
    {
        Code = code;
        _server = server;
        _images = images;
        var s = server();
        Settings.TextSeconds = s.DefaultTextSeconds;
        Settings.DrawSeconds = s.DefaultDrawSeconds;
        Settings.MaxPlayers = Math.Min(12, s.MaxPlayersPerRoom);
    }

    public ServerSettings Server => _server();
    public void Touch() => LastActivity = DateTime.UtcNow;

    /// <summary>A feature is on in this room only if it's allowed server-wide AND the host hasn't switched it off.</summary>
    public bool On(string key)
    {
        if (!Server.On(key)) return false;
        if (FeatureCatalog.ByKey.TryGetValue(key, out var f) && f.Scope == FeatureScope.Server) return true;
        return Settings.Features.TryGetValue(key, out var v) ? v : f?.Default ?? false;
    }

    public Player? Find(string id) => Players.FirstOrDefault(p => p.Id == id);
    public IEnumerable<Player> Humans => Players.Where(p => !p.IsBot && !p.Left);
    public IEnumerable<Player> ActivePlayers => Players.Where(p => !p.IsSpectator && !p.Left);
    public bool IsHost(string id) => HostId == id;

    public string Clean(string text) => On("wordFilter") ? WordFilter.Clean(text, Server.ExtraBannedWords) : text;

    private void RequireHost(string playerId)
    {
        if (!IsHost(playerId)) throw new GameException("Only the host can do that.");
    }

    private void RequirePhase(Phase p)
    {
        if (Phase != p) throw new GameException("You can't do that right now.");
    }

    public void SystemChat(string text)
    {
        Chat.Add(new ChatMessage { System = true, Text = text });
        if (Chat.Count > 60) Chat.RemoveAt(0);
    }

    // ------------------------------------------------------------------ players

    public Player AddPlayer(JoinRequest req)
    {
        var name = SanitiseName(req.Name);
        var player = new Player
        {
            Name = UniqueName(name),
            Avatar = Avatars.Contains(req.Avatar) ? req.Avatar! : Avatars[Random.Shared.Next(Avatars.Length)],
            Color = Colors.Contains(req.Color) ? req.Color! : Colors[Players.Count % Colors.Length],
        };

        var wantsSpectate = req.Spectate && On("spectators");
        if (Phase != Phase.Lobby)
        {
            if (!On("spectators")) throw new GameException("A game is in progress and this room doesn't allow late joiners.");
            player.IsSpectator = true;
            player.LateJoiner = !req.Spectate;
        }
        else if (wantsSpectate) player.IsSpectator = true;
        else if (ActivePlayers.Count() >= MaxPlayers)
        {
            if (!On("spectators")) throw new GameException("This room is full.");
            player.IsSpectator = true;
        }
        if (Players.Count >= MaxPlayers + 30) throw new GameException("This room is full.");

        Players.Add(player);
        if (string.IsNullOrEmpty(HostId)) HostId = player.Id;
        SystemChat($"{player.Name} joined{(player.IsSpectator ? " (watching)" : "")}");
        Touch();
        return player;
    }

    public int MaxPlayers => Math.Clamp(Settings.MaxPlayers, 2, Server.MaxPlayersPerRoom);

    public static string SanitiseName(string? name)
    {
        var n = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (n.Length == 0) n = "Player";
        return n.Length > 20 ? n[..20] : n;
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var i = 2; Players.Any(p => !p.Left && string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{name} {i}";
        return candidate;
    }

    public void AddBot(string by)
    {
        RequireHost(by); RequirePhase(Phase.Lobby);
        if (!On("bots")) throw new GameException("Computer players are switched off.");
        if (ActivePlayers.Count() >= MaxPlayers) throw new GameException("The room is full.");
        var used = Players.Select(p => p.Name).ToHashSet();
        var name = BotNames.FirstOrDefault(n => !used.Contains(n)) ?? UniqueName("Bot");
        var bot = new Player { Name = name, Avatar = "🤖", Color = Colors[Players.Count % Colors.Length], IsBot = true };
        Players.Add(bot);
        SystemChat($"{bot.Name} 🤖 joined");
        Touch();
    }

    /// <summary>Kick (host) or leave (self). Returns the removed player's connections so they can be told.</summary>
    public IReadOnlyCollection<string> Remove(string by, string targetId, bool kicked)
    {
        if (kicked) RequireHost(by);
        var p = Find(targetId) ?? throw new GameException("Player not found.");
        if (kicked && p.Id == by) throw new GameException("You can't kick yourself. Use Leave.");
        var conns = p.Connections.ToList();
        p.Connections.Clear();

        if (Phase == Phase.Lobby || p.IsSpectator || !Seats.Contains(p.Id))
            Players.Remove(p);
        else
        {
            p.Left = true;  // keep them in the seating; their folds get auto-filled
            p.Kicked = kicked;
        }
        if (kicked) p.Token = Guid.NewGuid().ToString("N");
        SystemChat(kicked ? $"{p.Name} was kicked out" : $"{p.Name} left");
        if (HostId == p.Id) PickNewHost();
        Touch();
        return conns;
    }

    public void MakeHost(string by, string targetId)
    {
        RequireHost(by);
        var p = Find(targetId);
        if (p == null || p.IsBot || p.Left) throw new GameException("They can't be host.");
        HostId = p.Id;
        SystemChat($"{p.Name} is now the host 👑");
    }

    public bool PickNewHost()
    {
        var next = Humans.Where(p => p.Connected).OrderBy(p => p.IsSpectator).ThenBy(p => p.JoinedAt).FirstOrDefault()
                   ?? Humans.OrderBy(p => p.JoinedAt).FirstOrDefault();
        var changed = next?.Id != HostId;
        HostId = next?.Id ?? "";
        if (changed && next != null) SystemChat($"{next.Name} is now the host 👑");
        return changed;
    }

    public void ToggleSpectate(string playerId)
    {
        RequirePhase(Phase.Lobby);
        if (!On("spectators")) throw new GameException("Spectating is switched off.");
        var p = Find(playerId)!;
        if (p.IsSpectator && ActivePlayers.Count() >= MaxPlayers) throw new GameException("The room is full.");
        p.IsSpectator = !p.IsSpectator;
        p.LateJoiner = false;
    }

    // ------------------------------------------------------------------ settings

    public void UpdateSettings(string by, RoomSettingsInput input)
    {
        RequireHost(by); RequirePhase(Phase.Lobby);
        var s = Settings;
        if (input.RoomName != null)
        {
            var rn = new string(input.RoomName.Where(c => !char.IsControl(c)).ToArray()).Trim();
            s.RoomName = Clean(Trunc(rn, 30));
        }
        if (input.Features != null)
            foreach (var (k, v) in input.Features)
                if (FeatureCatalog.ByKey.TryGetValue(k, out var f) && f.Scope == FeatureScope.Room)
                    s.Features[f.Key] = v;
        if (input.TemplateId != null)
        {
            if (input.TemplateId == "custom" || Templates.BuiltIn.Any(t => t.Id == input.TemplateId)) s.TemplateId = input.TemplateId;
        }
        if (input.CustomSteps != null && On("customTemplates"))
        {
            s.CustomSteps = input.CustomSteps.Take(20).Select(st => new Step
            {
                Prompt = Trunc(Clean(st.Prompt ?? ""), 120),
                Prefix = Trunc(Clean(st.Prefix ?? ""), 40),
                Suffix = Trunc(st.Suffix ?? "", 10),
                Drawable = st.Drawable,
                DrawHint = st.Drawable ? "Draw it!" : "",
                Kind = StepKind.Text,
                Category = "anything",
            }).Where(st => st.Prompt.Length > 0).ToList();
        }
        if (input.TextSeconds is { } ts) s.TextSeconds = Math.Clamp(ts, 10, 900);
        if (input.DrawSeconds is { } ds) s.DrawSeconds = Math.Clamp(ds, 10, 1800);
        if (input.PeekWords is { } pw) s.PeekWords = Math.Clamp(pw, 1, 8);
        if (input.MaxPlayers is { } mp) s.MaxPlayers = Math.Clamp(mp, 2, Server.MaxPlayersPerRoom);
        if (input.IsPublic is { } pub) s.IsPublic = pub && On("publicRooms");
        if (input.Password != null) s.Password = On("roomPasswords") ? Trunc(input.Password.Trim(), 30) : "";
        Touch();
    }

    private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;

    public Template? ChosenTemplate()
    {
        if (Settings.TemplateId == "custom")
            return Settings.CustomSteps.Count == 0 ? null : new Template { Id = "custom", Name = Settings.RoomName.Length > 0 ? Settings.RoomName : "Custom", Steps = Settings.CustomSteps };
        return Templates.Get(Settings.TemplateId);
    }

    // ------------------------------------------------------------------ game flow

    public void StartGame(string by)
    {
        RequireHost(by);
        if (Phase != Phase.Lobby) throw new GameException("A game is already running.");
        var template = ChosenTemplate() ?? throw new GameException("Pick a game mode first (your custom prompts are empty).");
        if (template.Id == "custom" && !On("customTemplates")) throw new GameException("Custom prompts are switched off.");
        if (template.PictureMode && !(On("pictureMode") && On("drawing"))) throw new GameException("Picture Consequences is switched off in this room.");
        var seated = ActivePlayers.ToList();
        if (template.PictureMode && seated.Any(p => p.IsBot)) throw new GameException("Bots can't draw (yet!). Remove the bots or pick another mode.");
        if (seated.Count < 2) throw new GameException("You need at least 2 players. Invite someone, or add a bot.");

        Active = template.Clone();
        Seats.Clear();
        Seats.AddRange(seated.Select(p => p.Id));
        Papers.Clear();
        for (var i = 0; i < Seats.Count; i++)
            Papers.Add(new Paper { Index = i, StarterSeat = i, Entries = new Entry?[Active.Steps.Count] });

        // Pass to the left every round, or jump a random number of seats when shuffling.
        _offsets.Clear();
        var n = Seats.Count;
        for (var r = 0; r < Active.Steps.Count; r++)
        {
            if (r == 0) _offsets.Add(0);
            else _offsets.Add(On("shufflePass") && n > 2 ? _offsets[r - 1] + Random.Shared.Next(1, n) : r);
        }
        GamesPlayed++;
        Phase = Phase.Writing;
        StartRound(0);
        SystemChat("The game has started! ✍️");
        Touch();
    }

    private void StartRound(int round)
    {
        Round = round;
        _submitted.Clear();
        _botDue.Clear();
        var step = Active!.Steps[round];
        var drawingRound = step.Kind == StepKind.Drawing || (step.Drawable && On("drawing"));
        Deadline = On("timer") ? DateTime.UtcNow.AddSeconds(drawingRound ? Settings.DrawSeconds : Settings.TextSeconds) : null;
        foreach (var id in Seats)
            if (Find(id) is { IsBot: true })
                _botDue[id] = DateTime.UtcNow.AddSeconds(Random.Shared.Next(3, 10));
    }

    private static int Mod(int a, int n) => ((a % n) + n) % n;
    public int PaperForSeat(int seat, int round) => Mod(seat - _offsets[round], Seats.Count);

    public bool HasSubmitted(string playerId) => _submitted.Contains(playerId);

    public void Submit(string playerId, string? text, string? imageDataUrl)
    {
        RequirePhase(Phase.Writing);
        var seat = Seats.IndexOf(playerId);
        if (seat < 0) throw new GameException("You're watching this game.");
        if (_submitted.Contains(playerId)) throw new GameException("You've already folded this one.");
        var step = Active!.Steps[Round];
        text = (text ?? "").Trim();
        if (text.Length > 200) text = text[..200];
        text = Clean(new string(text.Where(c => !char.IsControl(c)).ToArray()));

        string? imageId = null;
        var canDraw = step.Kind == StepKind.Drawing || (step.Drawable && On("drawing"));
        if (canDraw && !string.IsNullOrEmpty(imageDataUrl))
        {
            imageId = _images.Add(Code, imageDataUrl) ?? throw new GameException("That drawing couldn't be saved (too big?). Try again.");
        }
        if (step.Kind == StepKind.Drawing && imageId == null) throw new GameException("Draw something first!");
        if (step.Kind == StepKind.Text && text.Length == 0 && imageId == null) throw new GameException("Write something first!");

        Place(seat, new Entry { AuthorId = playerId, AuthorName = Find(playerId)!.Name, Text = text.Length > 0 ? text : null, ImageId = imageId });
        Touch();
    }

    private void Place(int seat, Entry entry)
    {
        var paper = Papers[PaperForSeat(seat, Round)];
        paper.Entries[Round] = entry;
        _submitted.Add(Seats[seat]);
    }

    private void AutoFill(int seat)
    {
        var step = Active!.Steps[Round];
        var p = Find(Seats[seat]);
        var useSuggestion = p?.IsBot == true || On("autoFill");
        var text = step.Kind == StepKind.Drawing ? null : useSuggestion ? Suggestions.Random(step.Category) : null;
        Place(seat, new Entry { AuthorId = Seats[seat], AuthorName = p?.Name ?? "?", Text = text, AutoFilled = p?.IsBot != true });
    }

    /// <summary>Host: stop waiting, fill in everyone still writing.</summary>
    public void SkipWaiting(string by)
    {
        RequireHost(by); RequirePhase(Phase.Writing);
        for (var s = 0; s < Seats.Count; s++)
            if (!_submitted.Contains(Seats[s])) AutoFill(s);
    }

    /// <summary>Called by the hub after submits and by the 1-second ticker. Returns true if something changed.</summary>
    public bool Tick(DateTime now)
    {
        var changed = false;
        if (Phase == Phase.Writing)
        {
            for (var s = 0; s < Seats.Count; s++)
            {
                var id = Seats[s];
                if (_submitted.Contains(id)) continue;
                var p = Find(id);
                if (p == null || p.Left) { AutoFill(s); changed = true; }
                else if (p.IsBot && _botDue.TryGetValue(id, out var due) && now >= due) { AutoFill(s); changed = true; }
                else if (Deadline is { } d && now > d.AddSeconds(4)) { AutoFill(s); changed = true; } // grace for the client's own auto-submit
            }
            if (_submitted.Count >= Seats.Count)
            {
                if (Round + 1 < Active!.Steps.Count) StartRound(Round + 1);
                else StartReveal();
                changed = true;
            }
        }
        return changed;
    }

    public string Inspire(string playerId)
    {
        if (!On("inspire")) throw new GameException("Inspire me is switched off.");
        if (Phase != Phase.Writing) throw new GameException("Not now!");
        return Suggestions.Random(Active!.Steps[Round].Category);
    }

    // ------------------------------------------------------------------ reveal

    private void StartReveal()
    {
        Phase = Phase.Reveal;
        Deadline = null;
        RevealPaper = 0;
        foreach (var p in Papers) p.Unfolded = 0;
        SystemChat("All folded! Time to unfold the papers 📜");
    }

    public string ReaderId()
    {
        if (Phase != Phase.Reveal || !On("rotateReader")) return HostId;
        var starter = Find(Seats[Papers[RevealPaper].StarterSeat]);
        return starter is { IsBot: false, Left: false, Connected: true } ? starter.Id : HostId;
    }

    private void RequireReader(string by)
    {
        if (by != ReaderId() && by != HostId) throw new GameException("It's not your turn to read.");
    }

    public void Unfold(string by, bool all)
    {
        RequirePhase(Phase.Reveal); RequireReader(by);
        var paper = Papers[RevealPaper];
        paper.Unfolded = all ? paper.Entries.Length : Math.Min(paper.Entries.Length, paper.Unfolded + 1);
        Touch();
    }

    public void ChangePaper(string by, int delta)
    {
        RequirePhase(Phase.Reveal); RequireReader(by);
        var target = RevealPaper + delta;
        if (target < 0 || target >= Papers.Count) throw new GameException("No more papers that way.");
        if (delta > 0 && Papers[RevealPaper].Unfolded < Papers[RevealPaper].Entries.Length) throw new GameException("Unfold this one first!");
        RevealPaper = target;
        Touch();
    }

    public void FinishReveal(string by)
    {
        RequirePhase(Phase.Reveal); RequireReader(by);
        foreach (var p in Papers) p.Unfolded = p.Entries.Length;
        Phase = Phase.Results;
        Touch();
    }

    public bool IsVisible(Entry e)
    {
        if (Phase == Phase.Results) return true;
        if (Phase != Phase.Reveal) return false;
        foreach (var p in Papers)
            for (var i = 0; i < p.Unfolded; i++)
                if (p.Entries[i] == e) return true;
        return false;
    }

    public void Star(string playerId, string entryId)
    {
        if (!On("stars")) throw new GameException("Stars are switched off.");
        if (Phase is not (Phase.Reveal or Phase.Results)) throw new GameException("Not now!");
        var entry = Papers.SelectMany(p => p.Entries).FirstOrDefault(e => e?.Id == entryId) ?? throw new GameException("Can't find that.");
        if (!IsVisible(entry)) throw new GameException("Not unfolded yet!");
        if (entry.AuthorId == playerId) throw new GameException("No starring your own work! 😉");
        if (!entry.StarredBy.Remove(playerId)) entry.StarredBy.Add(playerId);
    }

    public Dictionary<string, int> GameStars()
    {
        var d = new Dictionary<string, int>();
        foreach (var e in Papers.SelectMany(p => p.Entries))
            if (e is { AutoFilled: false }) d[e.AuthorId] = d.GetValueOrDefault(e.AuthorId) + e.StarredBy.Count;
        return d;
    }

    public void BackToLobby(string by)
    {
        RequireHost(by);
        if (Phase == Phase.Lobby) return;
        if (Phase == Phase.Results)
            foreach (var (id, stars) in GameStars())
                if (Find(id) is { } p) p.Score += stars;
        Phase = Phase.Lobby;
        Deadline = null;
        Active = null;
        Seats.Clear();
        Papers.Clear();
        Players.RemoveAll(p => p.Left);
        foreach (var p in Players.Where(p => p.LateJoiner))
        {
            p.LateJoiner = false;
            if (ActivePlayers.Count() < MaxPlayers) p.IsSpectator = false;
        }
        Touch();
    }

    public Paper PaperForHallOfFame(string playerId, int index)
    {
        if (!On("hallOfFame")) throw new GameException("The Hall of Fame is switched off.");
        if (index < 0 || index >= Papers.Count) throw new GameException("No such paper.");
        var paper = Papers[index];
        if (Phase is not (Phase.Reveal or Phase.Results) || paper.Unfolded < paper.Entries.Length) throw new GameException("Unfold it first!");
        if (paper.SavedToHallOfFame) throw new GameException("Already in the Hall of Fame!");
        return paper;
    }
}
