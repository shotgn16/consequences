using System.Security.Cryptography;
using System.Text;
using Consequences.Game;
using Consequences.Services;
using Consequences.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Consequences.Hubs;

/// <summary>Real-time game API used by the browser client.</summary>
public class GameHub(RoomManager rooms, SettingsService settings, HallOfFame hallOfFame, ImageStore images) : Hub
{
    // ------------------------------------------------------------------ helpers

    private (Room room, string playerId) Me()
    {
        var m = rooms.MembershipOf(Context.ConnectionId) ?? throw new HubException("You're not in a room.");
        var room = rooms.Get(m.RoomCode) ?? throw new HubException("That room has closed.");
        return (room, m.PlayerId);
    }

    /// <summary>Run a change under the room lock, then push fresh state to everyone.</summary>
    private async Task<T> Act<T>(Func<Room, string, T> action)
    {
        var (room, playerId) = Me();
        T result;
        try
        {
            lock (room.Gate)
            {
                if (room.Find(playerId) == null) throw new GameException("You're no longer in this room.");
                result = action(room, playerId);
                room.Tick(DateTime.UtcNow);
            }
        }
        catch (GameException ex) { throw new HubException(ex.Message); }
        await rooms.Broadcast(room);
        return result;
    }

    private Task Act(Action<Room, string> action) => Act<bool>((r, p) => { action(r, p); return true; });

    private string ClientIp()
    {
        var http = Context.GetHttpContext();
        if (http == null) return "unknown";
        return http.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
               ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool SecretEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    // ------------------------------------------------------------------ joining

    public async Task<JoinResult> CreateRoom(JoinRequest req)
    {
        var s = settings.Current;
        if (s.CreateRoomPin.Length > 0 && !SecretEquals(req.Pin ?? "", s.CreateRoomPin))
            throw new HubException("That room-creation PIN isn't right.");
        Room room;
        Player player;
        try
        {
            room = rooms.Create(ClientIp());
            lock (room.Gate)
            {
                req.Spectate = false;
                player = room.AddPlayer(req);
                if (!string.IsNullOrWhiteSpace(req.RoomName)) room.UpdateSettings(player.Id, new RoomSettingsInput { RoomName = req.RoomName });
                rooms.Attach(Context.ConnectionId, room, player);
            }
        }
        catch (GameException ex) { throw new HubException(ex.Message); }
        await rooms.Broadcast(room);
        return new JoinResult(room.Code, player.Id, player.Token);
    }

    public async Task<JoinResult> JoinRoom(JoinRequest req)
    {
        var room = rooms.Get(req.Code) ?? throw new HubException($"There's no room called {req.Code?.ToUpperInvariant()}. Check the code?");
        Player player;
        try
        {
            lock (room.Gate)
            {
                // Rejoining with a saved token (refresh, lost signal, switched app...)
                var existing = !string.IsNullOrEmpty(req.Token) ? room.Players.FirstOrDefault(p => !p.IsBot && SecretEquals(p.Token, req.Token!)) : null;
                if (existing != null)
                {
                    player = existing;
                    if (player.Left && !player.Kicked)
                    {
                        player.Left = false;
                        room.SystemChat($"{player.Name} is back!");
                    }
                }
                else
                {
                    if (room.Settings.Password.Length > 0 && room.On("roomPasswords") && !SecretEquals(req.Password ?? "", room.Settings.Password))
                        throw new GameException(string.IsNullOrEmpty(req.Password) ? "PASSWORD_REQUIRED" : "Wrong password.");
                    player = room.AddPlayer(req);
                }
                rooms.Attach(Context.ConnectionId, room, player);
                if (string.IsNullOrEmpty(room.HostId) || room.Find(room.HostId) is null or { Left: true }) room.PickNewHost();
            }
        }
        catch (GameException ex) { throw new HubException(ex.Message); }
        await rooms.Broadcast(room);
        return new JoinResult(room.Code, player.Id, player.Token);
    }

    public async Task Leave()
    {
        var m = rooms.MembershipOf(Context.ConnectionId);
        if (m == null) return;
        var room = rooms.Get(m.RoomCode);
        if (room == null) return;
        IReadOnlyCollection<string> conns;
        lock (room.Gate)
        {
            try { conns = room.Remove(m.PlayerId, m.PlayerId, kicked: false); }
            catch (GameException) { return; }
        }
        rooms.ForgetConnections(conns);
        await rooms.Broadcast(room);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = rooms.Detach(Context.ConnectionId);
        if (room != null) await rooms.Broadcast(room);
        await base.OnDisconnectedAsync(exception);
    }

    // ------------------------------------------------------------------ lobby

    public Task UpdateSettings(RoomSettingsInput input) => Act((r, p) => r.UpdateSettings(p, input));
    public Task AddBot() => Act((r, p) => r.AddBot(p));
    public Task MakeHost(string playerId) => Act((r, p) => r.MakeHost(p, playerId));
    public Task ToggleSpectate() => Act((r, p) => r.ToggleSpectate(p));

    public async Task Kick(string playerId)
    {
        var conns = await Act((r, p) => r.Remove(p, playerId, kicked: true));
        rooms.ForgetConnections(conns);
        if (conns.Count > 0) await Clients.Clients(conns).SendAsync("kicked");
    }

    public async Task StartGame()
    {
        await Act((r, p) => r.StartGame(p));
        Interlocked.Increment(ref rooms.TotalGamesPlayed);
        var (room, _) = Me();
        await rooms.SendToRoom(room, "sound", "start");
    }

    // ------------------------------------------------------------------ writing

    public Task Submit(string? text, string? image) => Act((r, p) => r.Submit(p, text, image));
    public Task SkipWaiting() => Act((r, p) => r.SkipWaiting(p));
    public Task<string> Inspire() => Act((r, p) => r.Inspire(p));

    // ------------------------------------------------------------------ reveal

    public async Task Unfold(bool all)
    {
        await Act((r, p) => r.Unfold(p, all));
        var (room, _) = Me();
        await rooms.SendToRoom(room, "sound", "unfold");
    }

    public Task NextPaper() => Act((r, p) => r.ChangePaper(p, +1));
    public Task PrevPaper() => Act((r, p) => r.ChangePaper(p, -1));

    public async Task FinishReveal()
    {
        await Act((r, p) => r.FinishReveal(p));
        var (room, _) = Me();
        await rooms.SendToRoom(room, "sound", "fanfare");
    }

    public Task Star(string entryId) => Act((r, p) => r.Star(p, entryId));
    public Task BackToLobby() => Act((r, p) => r.BackToLobby(p));

    public async Task SaveToHallOfFame(int paperIndex)
    {
        var (room, playerId) = Me();
        SavedStory story;
        try
        {
            lock (room.Gate)
            {
                var paper = room.PaperForHallOfFame(playerId, paperIndex);
                var t = room.Active!;
                story = new SavedStory
                {
                    TemplateName = t.Name,
                    PictureMode = t.PictureMode,
                    RoomCode = room.Code,
                    SavedBy = room.Find(playerId)?.Name ?? "",
                    Folds = paper.Entries.Select((e, i) => new SavedFold
                    {
                        Prefix = t.Steps[i].Prefix, Suffix = t.Steps[i].Suffix,
                        Text = e?.Text, ImageId = e?.ImageId, Author = e?.AuthorName ?? "",
                        PictureStrip = t.Steps[i].Kind == StepKind.Drawing,
                    }).ToList(),
                };
                var words = string.Join(' ', story.Folds.Where(f => f.Text != null).Select(f => f.Text));
                story.Title = t.PictureMode ? (story.Folds.LastOrDefault(f => f.Text != null)?.Text ?? "A mystery creature")
                                            : (words.Length > 70 ? words[..70] + "…" : words);
                paper.SavedToHallOfFame = true;
                room.SystemChat($"{story.SavedBy} saved paper {paperIndex + 1} to the Hall of Fame 🏆");
            }
        }
        catch (GameException ex) { throw new HubException(ex.Message); }
        hallOfFame.Save(story, images);
        await rooms.Broadcast(room);
    }

    // ------------------------------------------------------------------ fun stuff

    public async Task React(string emoji)
    {
        var (room, playerId) = Me();
        object payload;
        lock (room.Gate)
        {
            if (!room.On("reactions") || !Room.Reactions.Contains(emoji)) return;
            var p = room.Find(playerId);
            if (p == null || (DateTime.UtcNow - p.LastReaction).TotalMilliseconds < 250) return;
            p.LastReaction = DateTime.UtcNow;
            payload = new { emoji, from = p.Name, color = p.Color };
        }
        await rooms.SendToRoom(room, "reaction", payload);
    }

    public async Task Chat(string text)
    {
        var (room, playerId) = Me();
        lock (room.Gate)
        {
            if (!room.On("chat")) throw new HubException("Chat is switched off.");
            var p = room.Find(playerId);
            text = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (p == null || text.Length == 0) return;
            if ((DateTime.UtcNow - p.LastChat).TotalMilliseconds < 500) throw new HubException("Slow down a bit!");
            p.LastChat = DateTime.UtcNow;
            if (text.Length > 300) text = text[..300];
            room.Chat.Add(new ChatMessage { From = p.Name, Color = p.Color, Text = room.Clean(text) });
            if (room.Chat.Count > 60) room.Chat.RemoveAt(0);
        }
        await rooms.Broadcast(room);
    }
}
