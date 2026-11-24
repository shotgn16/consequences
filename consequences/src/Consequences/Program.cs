using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Consequences.Game;
using Consequences.Hubs;
using Consequences.Services;
using Consequences.Settings;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ImageStore>();
builder.Services.AddSingleton<HallOfFame>();
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddHostedService<RoomJanitor>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton<ErrorFilter>();
builder.Services.AddSignalR(o =>
{
    o.AddFilter<ErrorFilter>();
    o.MaximumReceiveMessageSize = 1024 * 1024; // drawings
    o.KeepAliveInterval = TimeSpan.FromSeconds(10);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(40);
}).AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

var app = builder.Build();
var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0";

// Optional sub-path hosting behind a reverse proxy, e.g. CONSEQ_BASE_PATH=/consequences
var basePath = Environment.GetEnvironmentVariable("CONSEQ_BASE_PATH");
if (!string.IsNullOrWhiteSpace(basePath)) app.UsePathBase(basePath.TrimEnd('/'));

// Security headers. Note: no frame-ancestors restriction so the Home Assistant sidebar (ingress iframe) works.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self' ws: wss:; frame-ancestors *";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = c.File.Name.EndsWith(".html") ? "no-cache" : "public, max-age=3600"
});

// -------------------------------------------------------------------------- helpers

// Requests arriving through Home Assistant ingress come from the Supervisor at 172.30.32.2 with an X-Ingress-Path header.
// HA has already authenticated that user, so (if enabled) they can open the admin page without a PIN.
bool ViaIngress(HttpContext ctx) =>
    ctx.Request.Headers.ContainsKey("X-Ingress-Path") &&
    ctx.Connection.RemoteIpAddress is { } ip && (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).Equals(IPAddress.Parse("172.30.32.2"));

bool IsAdmin(HttpContext ctx, SettingsService s)
{
    if (ViaIngress(ctx) && s.Current.On("adminViaIngress")) return true;
    if (s.AdminPin.Length == 0) return false;
    var pin = ctx.Request.Headers["X-Admin-Pin"].FirstOrDefault() ?? "";
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pin), Encoding.UTF8.GetBytes(s.AdminPin));
}

// -------------------------------------------------------------------------- public API

app.MapGet("/api/config", (HttpContext ctx, SettingsService s) =>
{
    var cur = s.Current;
    return Results.Json(new
    {
        appName = cur.AppName,
        version,
        publicUrl = cur.PublicUrl,
        createPinRequired = cur.CreateRoomPin.Length > 0,
        viaIngress = ViaIngress(ctx),
        adminAvailable = (ViaIngress(ctx) && cur.On("adminViaIngress")) || s.AdminPin.Length > 0,
        features = FeatureCatalog.All.ToDictionary(f => f.Key, f => cur.On(f.Key)),
        catalog = FeatureCatalog.All.Select(f => new { f.Key, f.Label, f.Description, f.Icon, scope = f.Scope.ToString().ToLowerInvariant() }),
        templates = Templates.BuiltIn.Select(t => new
        {
            t.Id, t.Name, t.Description, t.PictureMode, folds = t.Steps.Count,
            steps = t.Steps.Select(st => new { st.Prompt, st.Prefix, st.Suffix, st.Drawable }),
        }),
        avatars = Room.Avatars,
        colors = Room.Colors,
        reactions = Room.Reactions,
    });
});

app.MapGet("/api/rooms", (RoomManager rooms, SettingsService s) =>
{
    if (!s.Current.On("publicRooms")) return Results.Json(Array.Empty<object>());
    var list = rooms.Rooms.Select(r =>
    {
        lock (r.Gate)
            return new
            {
                code = r.Code,
                name = r.Settings.RoomName,
                isPublic = r.Settings.IsPublic,
                phase = r.Phase.ToString().ToLowerInvariant(),
                players = r.ActivePlayers.Count(),
                max = r.MaxPlayers,
                host = r.Find(r.HostId)?.Name,
                locked = r.Settings.Password.Length > 0 && r.On("roomPasswords"),
                mode = r.ChosenTemplate()?.Name ?? "Custom",
            };
    }).Where(r => r.isPublic).OrderBy(r => r.phase != "lobby").ThenByDescending(r => r.players).Take(50);
    return Results.Json(list);
});

app.MapGet("/img/{id}", (string id, ImageStore images) =>
{
    var img = images.Get(id);
    return img == null ? Results.NotFound() : Results.Bytes(img.Bytes, img.ContentType);
});

app.MapGet("/api/halloffame", (SettingsService s, HallOfFame hof) =>
    s.Current.On("hallOfFame") ? Results.Json(hof.List()) : Results.Json(Array.Empty<object>()));

app.MapGet("/halloffame/img/{file}", (string file, HallOfFame hof) =>
{
    var img = hof.Image(file);
    return img == null ? Results.NotFound() : Results.File(img.Value.path, img.Value.type);
});

// For Home Assistant REST sensors: rooms, players online, games in progress.
app.MapGet("/api/stats", (SettingsService s, RoomManager rooms) =>
    s.Current.On("statsApi") ? Results.Json(rooms.Stats()) : Results.NotFound());

app.MapGet("/health", () => Results.Text("ok"));

// -------------------------------------------------------------------------- admin API

var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
{
    var s = ctx.HttpContext.RequestServices.GetRequiredService<SettingsService>();
    if (!IsAdmin(ctx.HttpContext, s))
    {
        await Task.Delay(400); // slow down PIN guessing
        return Results.Json(new { error = s.AdminPin.Length == 0 && !ViaIngress(ctx.HttpContext) ? "Admin is disabled: set an admin PIN in the app options." : "Wrong PIN." }, statusCode: 401);
    }
    return await next(ctx);
});

admin.MapGet("/settings", (SettingsService s) => Results.Json(new
{
    settings = s.Current,
    runningInHomeAssistant = s.RunningInHomeAssistant,
    dataPath = s.DataPath,
}));

admin.MapPost("/settings", async (HttpContext ctx, SettingsService s, RoomManager rooms) =>
{
    var updated = await ctx.Request.ReadFromJsonAsync<ServerSettings>();
    if (updated == null) return Results.BadRequest();
    s.Save(updated);
    foreach (var r in rooms.Rooms.ToList()) await rooms.Broadcast(r); // features may have changed
    return Results.Json(s.Current);
});

admin.MapPost("/reset", async (SettingsService s, RoomManager rooms) =>
{
    s.ResetToDefaults();
    foreach (var r in rooms.Rooms.ToList()) await rooms.Broadcast(r);
    return Results.Json(s.Current);
});

admin.MapGet("/rooms", (RoomManager rooms) => Results.Json(rooms.Rooms.Select(r =>
{
    lock (r.Gate)
        return new
        {
            code = r.Code, name = r.Settings.RoomName, phase = r.Phase.ToString().ToLowerInvariant(),
            players = r.Players.Where(p => !p.Left).Select(p => new { p.Name, p.IsBot, p.IsSpectator, connected = p.Connected }),
            created = r.CreatedAt, lastActivity = r.LastActivity, games = r.GamesPlayed, isPublic = r.Settings.IsPublic,
        };
})));

admin.MapPost("/rooms/{code}/close", (string code, RoomManager rooms) =>
{
    var r = rooms.Get(code);
    if (r == null) return Results.NotFound();
    rooms.Close(r, "The room was closed by the server admin");
    return Results.Ok();
});

admin.MapDelete("/halloffame/{id}", (string id, HallOfFame hof) => hof.Delete(id) ? Results.Ok() : Results.NotFound());

admin.MapGet("/stats", (RoomManager rooms) => Results.Json(rooms.Stats()));

app.MapHub<GameHub>("/hub");

app.Logger.LogInformation("Consequences {Version} starting. Data folder: {Data}", version, app.Services.GetRequiredService<SettingsService>().DataPath);
app.Run();
