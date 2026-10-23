using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astria.Application;
using Astria.Domain;
namespace Astria.Infrastructure;

public sealed class CatalogStorage : ICatalogStorage
{
    public string DirectoryPath { get; }
    private string FilePath => Path.Combine(DirectoryPath, "catalog.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public CatalogStorage(string directory) { DirectoryPath = Path.GetFullPath(directory); Directory.CreateDirectory(DirectoryPath); }
    public Catalog Load()
    {
        if (!File.Exists(FilePath)) return Catalog.Empty;
        try { return ReadBackup(FilePath); }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        { throw new InvalidDataException("应用数据无法读取，原文件已保留。请从备份恢复，或使用独立 --data-root 启动。", error); }
    }
    public void Save(Catalog catalog)
    {
        catalog = catalog.Normalize(); var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, Json);
        var temporary = Path.Combine(DirectoryPath, ".write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", true); else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string CreateBackup()
    {
        var folder = Path.Combine(DirectoryPath, "backups"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
        if (File.Exists(FilePath)) File.Copy(FilePath, path); else File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(Catalog.Empty, Json));
        return path;
    }
    public Catalog ReadBackup(string file) => (JsonSerializer.Deserialize<Catalog>(File.ReadAllBytes(file), Json) ?? throw new InvalidDataException("备份为空。")).Normalize();
}

public static class CompatibilityImport
{
    // A one-way reader for a published JSON data format; never writes to that directory.
    public static Catalog Read(string directory)
    {
        using var library = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "apps.json")));
        var entries = new List<LaunchEntry>(); var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dock = Path.Combine(directory, "dock-layout.json");
        if (File.Exists(dock)) { using var doc = JsonDocument.Parse(File.ReadAllBytes(dock)); if (doc.RootElement.TryGetProperty("pinnedItemIds", out var array)) foreach (var id in array.EnumerateArray()) pins.Add(id.GetString() ?? ""); }
        foreach (var e in library.RootElement.GetProperty("items").EnumerateArray())
        {
            string Text(string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (!Guid.TryParse(Text("id"), out var id) || string.IsNullOrWhiteSpace(Text("target"))) continue;
            var target = Text("target"); var kind = Text("kind") switch { "Folder" => EntryKind.Folder, "ShellLocation" => EntryKind.Shell, "Program" or "Shortcut" => EntryKind.Application, _ => EntryKind.File };
            entries.Add(new(id, Text("displayName"), target, kind, Text("arguments"), Text("workingDirectory"), Text("iconSource"), pins.Contains(Text("id"))) {
                CustomIconPath=string.IsNullOrWhiteSpace(Text("customIconPath"))?null:Text("customIconPath"),
                ApplicationId=string.IsNullOrWhiteSpace(Text("appUserModelId"))?null:Text("appUserModelId"),
                SearchAliases=e.TryGetProperty("searchAliases",out var aliases)&&aliases.ValueKind==JsonValueKind.Array?aliases.EnumerateArray().Select(x=>x.GetString()??"").Where(x=>x.Length>0).ToArray():[]
            });
        }
        var layout = Path.Combine(directory, "launcher-layout.json");
        if (File.Exists(layout))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(layout)); var ordered = new List<Guid>();
            if (doc.RootElement.TryGetProperty("pages", out var pages)) foreach (var page in pages.EnumerateArray())
            {
                if (!page.TryGetProperty("slots", out var slots)) continue;
                foreach (var slot in slots.EnumerateArray())
                {
                    if (slot.TryGetProperty("itemId", out var item) && Guid.TryParse(item.GetString(), out var itemId)) ordered.Add(itemId);
                    if (slot.TryGetProperty("group", out var group) && group.TryGetProperty("id", out var groupId) && Guid.TryParse(groupId.GetString(), out var id))
                    {
                        var children = group.TryGetProperty("itemIds", out var ids) ? ids.EnumerateArray().Select(x => Guid.TryParse(x.GetString(), out var value) ? value : Guid.Empty).Where(x => x != Guid.Empty).ToImmutableArray() : [];
                        entries.Add(new(id, group.TryGetProperty("name", out var name) ? name.GetString() ?? "分组" : "分组", "", EntryKind.Group, Favorite: pins.Contains(id.ToString("N")), Members: children)); ordered.Add(id);
                    }
                }
            }
            entries = ordered.Distinct().Select(id => entries.FirstOrDefault(e => e.Id == id)).OfType<LaunchEntry>().Concat(entries.Where(e => !ordered.Contains(e.Id))).ToList();
        }
        var preferences = new Preferences(); var file = Path.Combine(directory, "desktop.json");
        if (File.Exists(file))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file)); var p = doc.RootElement;
            KeyChord Key(string name, KeyChord fallback) => p.TryGetProperty(name, out var k) && k.TryGetProperty("modifiers", out var m) && k.TryGetProperty("virtualKey", out var v) ? new(m.GetUInt32(), v.GetUInt32()) : fallback;
            preferences = preferences with
            {
                DockKey = Key("hotkey", preferences.DockKey),
                SettingsKey = Key("settingsHotkey", preferences.SettingsKey),
                PageSize = p.TryGetProperty("favoriteVisibleCount", out var n) ? n.GetInt32() : 16,
                Formation = p.TryGetProperty("favoriteShape", out var f) ? f.GetString() ?? "orbits" : "orbits",
                CardScale = p.TryGetProperty("dockScale", out var s) ? s.GetDouble() : 1,
                HideDesktopIconsOnStartup = p.TryGetProperty("hideDesktopIconsOnStartup", out var h) && h.GetBoolean(),
                ReduceMotion = p.TryGetProperty("reduceMotion", out var motion) && motion.GetBoolean(),
                AutoHideBar = p.TryGetProperty("autoHideDock", out var autoHide) && autoHide.GetBoolean(),
                Theme = p.TryGetProperty("accent", out var accent) ? accent.GetString() switch { "aurora" or "mint" => "aurora", "lavender" or "neon" => "violet", "gold" => "gold", _ => "deep" } : "deep"
            };
            if (p.TryGetProperty("cardLocations", out var locations))
            {
                var positions = ImmutableDictionary.CreateBuilder<Guid, Position>();
                foreach (var location in locations.EnumerateObject()) if (Guid.TryParse(location.Name, out var id) && location.Value.TryGetProperty("x", out var x) && location.Value.TryGetProperty("y", out var y)) positions[id] = new(x.GetDouble(), y.GetDouble());
                preferences = preferences with { Positions = positions.ToImmutable() };
            }
            if (p.TryGetProperty("backgroundRunningIds", out var background))
            { var ids = background.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase); entries = entries.Select(e => e with { TrackBackground = ids.Contains(e.Id.ToString("N")) }).ToList(); }
        }
        return new Catalog(1, entries.ToImmutableArray(), preferences).Normalize();
    }
}
