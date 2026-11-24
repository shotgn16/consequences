using Consequences.Settings;

namespace Consequences.Game;

/// <summary>
/// Builds the personalised view of a room for one player. Folded content is never sent to clients
/// before it is unfolded, so nobody can cheat by peeking in the browser's dev tools.
/// </summary>
public static class StateBuilder
{
    public static object Build(Room room, string viewerId)
    {
        var me = room.Find(viewerId);
        var gameStars = room.Phase is Phase.Reveal or Phase.Results ? room.GameStars() : new();
        var features = FeatureCatalog.All.ToDictionary(f => f.Key, f => room.On(f.Key));

        return new
        {
            code = room.Code,
            phase = room.Phase.ToString().ToLowerInvariant(),
            hostId = room.HostId,
            you = me == null ? null : new { id = me.Id, name = me.Name, isHost = room.IsHost(me.Id), isSpectator = me.IsSpectator, lateJoiner = me.LateJoiner },
            players = room.Players.Select(p => new
            {
                id = p.Id, name = p.Name, avatar = p.Avatar, color = p.Color,
                connected = p.Connected, isBot = p.IsBot, isSpectator = p.IsSpectator, left = p.Left,
                isHost = room.IsHost(p.Id),
                submitted = room.Phase == Phase.Writing && room.HasSubmitted(p.Id),
                seated = room.Seats.Contains(p.Id),
                score = p.Score + gameStars.GetValueOrDefault(p.Id),
                gameStars = gameStars.GetValueOrDefault(p.Id),
            }),
            settings = new
            {
                roomName = room.Settings.RoomName,
                templateId = room.Settings.TemplateId,
                customSteps = room.Settings.CustomSteps.Select(s => new { s.Prompt, s.Prefix, s.Suffix, s.Drawable }),
                textSeconds = room.Settings.TextSeconds,
                drawSeconds = room.Settings.DrawSeconds,
                peekWords = room.Settings.PeekWords,
                maxPlayers = room.MaxPlayers,
                serverMaxPlayers = room.Server.MaxPlayersPerRoom,
                isPublic = room.Settings.IsPublic,
                hasPassword = room.Settings.Password.Length > 0,
                password = room.IsHost(viewerId) ? room.Settings.Password : null,
                roomFeatures = room.Settings.Features,
            },
            features,
            serverFeatures = FeatureCatalog.All.ToDictionary(f => f.Key, f => room.Server.On(f.Key)),
            deadline = room.Deadline.HasValue ? new DateTimeOffset(room.Deadline.Value, TimeSpan.Zero).ToUnixTimeMilliseconds() : (long?)null,
            serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            game = room.Active == null ? null : new
            {
                templateName = room.Active.Name,
                pictureMode = room.Active.PictureMode,
                round = room.Round,
                totalRounds = room.Active.Steps.Count,
                steps = room.Active.Steps.Select(s => new { s.Prompt, s.Prefix }),
            },
            task = room.Phase == Phase.Writing ? Task(room, viewerId) : null,
            reveal = room.Phase == Phase.Reveal ? Reveal(room, viewerId) : null,
            chat = room.Chat.TakeLast(40),
            results = room.Phase == Phase.Results ? Results(room, viewerId, gameStars) : null,
        };
    }

    private static object? Task(Room room, string viewerId)
    {
        var seat = room.Seats.IndexOf(viewerId);
        if (seat < 0 || room.HasSubmitted(viewerId)) return null;
        var paperIndex = room.PaperForSeat(seat, room.Round);
        var paper = room.Papers[paperIndex];
        var step = room.Active!.Steps[room.Round];
        var prev = room.Round > 0 ? paper.Entries[room.Round - 1] : null;

        string? peekText = null, peekImage = null;
        if (prev != null)
        {
            // Picture mode: the lines over the fold are the whole point, so always show the bottom strip.
            if (room.Active.PictureMode && step.Kind == StepKind.Drawing && prev.ImageId != null) peekImage = "img/" + prev.ImageId;
            else if (room.On("peek") && prev.Text != null)
            {
                var words = prev.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                peekText = (words.Length > room.Settings.PeekWords ? "… " : "") + string.Join(' ', words.TakeLast(room.Settings.PeekWords));
            }
        }

        return new
        {
            paperIndex,
            paperNumber = paperIndex + 1,
            starterName = room.Find(room.Seats[paper.StarterSeat])?.Name,
            step = room.Round,
            prompt = step.Prompt,
            prefix = step.Prefix,
            placeholder = step.Placeholder,
            kind = step.Kind.ToString().ToLowerInvariant(),
            canDraw = step.Kind == StepKind.Drawing || (step.Drawable && room.On("drawing")),
            drawHint = step.DrawHint,
            peekText,
            peekImage,
            foldsBelow = room.Round,
        };
    }

    private static object EntryDto(Room room, Template t, int i, Entry? e, string viewerId) => new
    {
        id = e?.Id,
        step = i,
        prefix = t.Steps[i].Prefix,
        suffix = t.Steps[i].Suffix,
        kind = t.Steps[i].Kind.ToString().ToLowerInvariant(),
        text = e?.Text,
        image = e?.ImageId != null ? "img/" + e.ImageId : null,
        author = room.On("showAuthors") || room.Phase == Phase.Results ? e?.AuthorName : null,
        authorId = room.On("showAuthors") || room.Phase == Phase.Results ? e?.AuthorId : null,
        autoFilled = e?.AutoFilled ?? true,
        stars = e?.StarredBy.Count ?? 0,
        starredByMe = e?.StarredBy.Contains(viewerId) ?? false,
        mine = e?.AuthorId == viewerId,
    };

    private static object PaperDto(Room room, Paper p, string viewerId, int upTo) => new
    {
        index = p.Index,
        starter = room.Find(room.Seats[p.StarterSeat])?.Name,
        unfolded = p.Unfolded,
        total = p.Entries.Length,
        saved = p.SavedToHallOfFame,
        stars = p.Stars,
        entries = Enumerable.Range(0, upTo).Select(i => EntryDto(room, room.Active!, i, p.Entries[i], viewerId)),
    };

    private static object Reveal(Room room, string viewerId)
    {
        var paper = room.Papers[room.RevealPaper];
        var readerId = room.ReaderId();
        return new
        {
            paperIndex = room.RevealPaper,
            paperCount = room.Papers.Count,
            readerId,
            readerName = room.Find(readerId)?.Name,
            canControl = viewerId == readerId || room.IsHost(viewerId),
            paper = PaperDto(room, paper, viewerId, paper.Unfolded),
        };
    }

    private static object Results(Room room, string viewerId, Dictionary<string, int> gameStars)
    {
        var t = room.Active!;
        var entries = room.Papers.SelectMany(p => p.Entries.Select((e, i) => (e, i))).Where(x => x.e is { AutoFilled: false }).ToList();
        var bestLine = entries.Where(x => x.e!.Text != null && x.e.StarredBy.Count > 0).OrderByDescending(x => x.e!.StarredBy.Count).FirstOrDefault();
        var bestDrawing = entries.Where(x => x.e!.ImageId != null && x.e.StarredBy.Count > 0).OrderByDescending(x => x.e!.StarredBy.Count).FirstOrDefault();
        var bestPaper = room.Papers.Where(p => p.Stars > 0).OrderByDescending(p => p.Stars).FirstOrDefault();
        var star = gameStars.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).FirstOrDefault();

        return new
        {
            papers = room.Papers.Select(p => PaperDto(room, p, viewerId, p.Entries.Length)),
            awards = new
            {
                bestLine = bestLine.e == null ? null : new { text = bestLine.e.Text, author = bestLine.e.AuthorName, stars = bestLine.e.StarredBy.Count, prefix = t.Steps[bestLine.i].Prefix },
                bestDrawing = bestDrawing.e == null ? null : new { image = "img/" + bestDrawing.e.ImageId, author = bestDrawing.e.AuthorName, stars = bestDrawing.e.StarredBy.Count },
                bestPaper = bestPaper == null ? null : new { index = bestPaper.Index, stars = bestPaper.Stars, starter = room.Find(room.Seats[bestPaper.StarterSeat])?.Name },
                starPlayer = star.Key == null ? null : new { name = room.Find(star.Key)?.Name, stars = star.Value },
            },
        };
    }
}
