using System.Text.Json;
using Consequences.Settings;

namespace Consequences.Services;

public class SavedFold
{
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public string? Text { get; set; }
    public string? ImageId { get; set; }
    public string Author { get; set; } = "";
    public bool PictureStrip { get; set; }
}

public class SavedStory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public bool PictureMode { get; set; }
    public string RoomCode { get; set; } = "";
    public string SavedBy { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public List<SavedFold> Folds { get; set; } = new();
}

/// <summary>Permanent gallery of favourite stories, stored as JSON + image files in the data folder.</summary>
public class HallOfFame
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly string _file;
    private List<SavedStory> _stories = new();
    private const int MaxStories = 500;

    public HallOfFame(SettingsService settings)
    {
        _dir = Path.Combine(settings.DataPath, "halloffame");
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "stories.json");
        if (File.Exists(_file))
        {
            try { _stories = JsonSerializer.Deserialize<List<SavedStory>>(File.ReadAllText(_file)) ?? new(); }
            catch { _stories = new(); }
        }
    }

    public IReadOnlyList<SavedStory> List() { lock (_gate) return _stories.OrderByDescending(s => s.SavedAt).ToList(); }

    public bool Contains(string roomCode, string title) { lock (_gate) return _stories.Any(s => s.RoomCode == roomCode && s.Title == title); }

    public SavedStory Save(SavedStory story, ImageStore images)
    {
        foreach (var f in story.Folds.Where(f => f.ImageId != null))
        {
            var img = images.Get(f.ImageId!);
            if (img == null) { f.ImageId = null; continue; }
            var newId = Guid.NewGuid().ToString("N") + Ext(img.ContentType);
            File.WriteAllBytes(Path.Combine(_dir, newId), img.Bytes);
            f.ImageId = newId;
        }
        lock (_gate)
        {
            _stories.Add(story);
            while (_stories.Count > MaxStories)
            {
                var oldest = _stories.OrderBy(s => s.SavedAt).First();
                RemoveFiles(oldest);
                _stories.Remove(oldest);
            }
            Persist();
        }
        return story;
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var s = _stories.FirstOrDefault(x => x.Id == id);
            if (s == null) return false;
            RemoveFiles(s);
            _stories.Remove(s);
            Persist();
            return true;
        }
    }

    public (string path, string type)? Image(string file)
    {
        if (file.Contains('/') || file.Contains('\\') || file.Contains("..")) return null;
        var path = Path.Combine(_dir, file);
        if (!File.Exists(path)) return null;
        var type = Path.GetExtension(file) switch { ".png" => "image/png", ".jpg" => "image/jpeg", ".webp" => "image/webp", _ => null };
        return type == null ? null : (path, type);
    }

    private void RemoveFiles(SavedStory s)
    {
        foreach (var f in s.Folds.Where(f => f.ImageId != null))
        {
            var p = Path.Combine(_dir, f.ImageId!);
            if (File.Exists(p)) File.Delete(p);
        }
    }

    private void Persist()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_stories));
        File.Move(tmp, _file, true);
    }

    private static string Ext(string type) => type switch { "image/jpeg" => ".jpg", "image/webp" => ".webp", _ => ".png" };
}
