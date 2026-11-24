using System.Collections.Concurrent;
using Consequences.Game;
using Consequences.Hubs;
using Consequences.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Consequences.Services;

public record Membership(string RoomCode, string PlayerId);

public class RoomManager
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I or O
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Membership> _connections = new();
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _createLog = new();
    private readonly IHubContext<GameHub> _hub;
    private readonly SettingsService _settings;
    private readonly ImageStore _images;
    private readonly ILogger<RoomManager> _log;

    public long TotalGamesPlayed;

    public RoomManager(IHubContext<GameHub> hub, SettingsService settings, ImageStore images, ILogger<RoomManager> log)
    {
        _hub = hub; _settings = settings; _images = images; _log = log;
    }

    public IEnumerable<Room> Rooms => _rooms.Values;
    public Room? Get(string? code) => code != null && _rooms.TryGetValue(code.Trim(), out var r) && !r.Closed ? r : null;
    public Membership? MembershipOf(string connectionId) => _connections.TryGetValue(connectionId, out var m) ? m : null;

    public Room Create(string ip)
    {
        var s = _settings.Current;
        if (_rooms.Count >= s.MaxRooms) throw new GameException("The server is full right now. Try again later.");
        var log = _createLog.GetOrAdd(ip, _ => new Queue<DateTime>());
        lock (log)
        {
            while (log.Count > 0 && log.Peek() < DateTime.UtcNow.AddMinutes(-10)) log.Dequeue();
            if (log.Count >= 10) throw new GameException("Slow down! You've created a lot of rooms. Try again in a few minutes.");
            log.Enqueue(DateTime.UtcNow);
        }
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var len = attempt < 100 ? 4 : 5;
            var code = new string(Enumerable.Range(0, len).Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]).ToArray());
            var room = new Room(code, () => _settings.Current, _images);
            if (_rooms.TryAdd(code, room))
            {
                _log.LogInformation("Room {Code} created", code);
                return room;
            }
        }
        throw new GameException("Couldn't create a room, please try again.");
    }

    public void Attach(string connectionId, Room room, Player player)
    {
        // a connection belongs to one room at a time
        Detach(connectionId);
        player.Connections.Add(connectionId);
        player.DisconnectedAt = null;
        _connections[connectionId] = new Membership(room.Code, player.Id);
    }

    /// <summary>Forget a connection. Returns the room it was in (if any).</summary>
    public Room? Detach(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var m)) return null;
        var room = Get(m.RoomCode);
        if (room == null) return null;
        lock (room.Gate)
        {
            var p = room.Find(m.PlayerId);
            if (p != null && p.Connections.Remove(connectionId) && p.Connections.Count == 0) p.DisconnectedAt = DateTime.UtcNow;
        }
        return room;
    }

    public void ForgetConnections(IEnumerable<string> connectionIds)
    {
        foreach (var c in connectionIds) _connections.TryRemove(c, out _);
    }

    public void Close(Room room, string reason)
    {
        List<string> conns;
        lock (room.Gate)
        {
            room.Closed = true;
            conns = room.Players.SelectMany(p => p.Connections).ToList();
        }
        _rooms.TryRemove(room.Code, out _);
        _images.RemoveRoom(room.Code);
        ForgetConnections(conns);
        if (conns.Count > 0) _ = _hub.Clients.Clients(conns).SendAsync("roomClosed", reason);
        _log.LogInformation("Room {Code} closed: {Reason}", room.Code, reason);
    }

    /// <summary>Send everyone in the room their personalised state. Call without holding the room lock.</summary>
    public async Task Broadcast(Room room)
    {
        List<(List<string> conns, object state)> sends;
        lock (room.Gate)
        {
            sends = room.Players.Where(p => p.Connections.Count > 0)
                .Select(p => (p.Connections.ToList(), StateBuilder.Build(room, p.Id))).ToList();
        }
        foreach (var (conns, state) in sends)
        {
            try { await _hub.Clients.Clients(conns).SendAsync("state", state); }
            catch (Exception ex) { _log.LogDebug(ex, "Broadcast failed"); }
        }
    }

    public Task SendToRoom(Room room, string method, object arg)
    {
        List<string> conns;
        lock (room.Gate) conns = room.Players.SelectMany(p => p.Connections).ToList();
        return conns.Count == 0 ? Task.CompletedTask : _hub.Clients.Clients(conns).SendAsync(method, arg);
    }

    public object Stats() => new
    {
        rooms = _rooms.Count,
        players = _rooms.Values.Sum(r => { lock (r.Gate) return r.Players.Count(p => !p.IsBot && p.Connected); }),
        gamesInProgress = _rooms.Values.Count(r => r.Phase != Phase.Lobby),
        gamesPlayed = Interlocked.Read(ref TotalGamesPlayed),
        images = _images.Count,
    };
}

/// <summary>Once a second: timers, bots, absent players, host hand-over and tidying up idle rooms.</summary>
public class RoomJanitor(RoomManager rooms, ILogger<RoomJanitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            foreach (var room in rooms.Rooms.ToList())
            {
                try { await Sweep(room); }
                catch (Exception ex) { log.LogError(ex, "Janitor failed for room {Code}", room.Code); }
            }
        }
    }

    private async Task Sweep(Room room)
    {
        var now = DateTime.UtcNow;
        bool changed, close = false;
        string reason = "";
        lock (room.Gate)
        {
            changed = room.Tick(now);

            // lobby: drop people who have been gone for a few minutes
            if (room.Phase == Phase.Lobby)
            {
                var gone = room.Players.Where(p => !p.IsBot && !p.Connected && p.DisconnectedAt < now.AddMinutes(-3)).ToList();
                foreach (var p in gone) { room.Players.Remove(p); room.SystemChat($"{p.Name} wandered off"); changed = true; }
            }

            // hand over hosting if the host vanished
            var host = room.Find(room.HostId);
            if (host == null || host.Left || (!host.Connected && host.DisconnectedAt < now.AddSeconds(-30)))
                if (room.Humans.Any(p => p.Connected) && room.PickNewHost()) changed = true;

            var anyoneHere = room.Humans.Any(p => p.Connected);
            var lastSeen = room.Humans.Select(p => p.DisconnectedAt ?? now).DefaultIfEmpty(room.LastActivity).Max();
            if (!room.Humans.Any() || (!anyoneHere && lastSeen < now.AddMinutes(-10)))
            { close = true; reason = "Everyone left"; }
            else if (room.LastActivity < now.AddMinutes(-room.Server.RoomIdleMinutes))
            { close = true; reason = "The room was idle for too long"; }
        }
        if (close) rooms.Close(room, reason);
        else if (changed) await rooms.Broadcast(room);
    }
}
