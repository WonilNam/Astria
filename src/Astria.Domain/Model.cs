using System.Collections.Immutable;

namespace Astria.Domain;

public enum EntryKind { Application, File, Folder, Shell, Group }
public sealed record LaunchEntry(Guid Id, string Name, string Target, EntryKind Kind,
    string Arguments = "", string WorkingDirectory = "", string IconPath = "", bool Favorite = false,
    bool TrackBackground = false, ImmutableArray<Guid> Members = default)
{
    public string? CustomIconPath { get; init; }
    public string? ApplicationId { get; init; }
    public string[] SearchAliases { get; init; } = [];
    public bool IsSidebar => Kind is EntryKind.Folder or EntryKind.Shell;
    public ImmutableArray<Guid> Children => Members.IsDefault ? [] : Members;
}
public readonly record struct Position(double X, double Y);
public sealed record KeyChord(uint Modifiers, uint Key)
{
    public bool IsValid => Modifiers is > 0 and <= 15 && (Key is >= 0x30 and <= 0x5A or >= 0x70 and <= 0x87 or 0x20 or 0x0D or 0x09) && Key != 0x7B;
    public string Label => string.Join(" + ", new[] { (Modifiers & 2) != 0 ? "Ctrl" : null, (Modifiers & 1) != 0 ? "Alt" : null,
        (Modifiers & 4) != 0 ? "Shift" : null, (Modifiers & 8) != 0 ? "Win" : null,
        Key == 32 ? "Space" : Key == 13 ? "Enter" : Key == 9 ? "Tab" : Key >= 112 ? "F" + (Key - 111) : ((char)Key).ToString() }.OfType<string>());
}
public sealed record Preferences
{
    public string PresentationJson { get; init; } = "{}";
    public string Theme { get; init; } = "deep";
    public string Formation { get; init; } = "orbits";
    public int PageSize { get; init; } = 16;
    public double CardScale { get; init; } = 1;
    public bool ReduceMotion { get; init; }
    public bool AutoHideBar { get; init; } = true;
    public bool HideDesktopIconsOnStartup { get; init; }
    public KeyChord DockKey { get; init; } = new(3, 32);
    public KeyChord SettingsKey { get; init; } = new(3, 83);
    public ImmutableDictionary<Guid, Position> Positions { get; init; } = ImmutableDictionary<Guid, Position>.Empty;
    public Preferences Normalize()
    {
        var dock = DockKey is { IsValid: true } ? DockKey : new(3, 32);
        var settings = SettingsKey is { IsValid: true } && SettingsKey != dock ? SettingsKey : dock == new KeyChord(3, 83) ? new(7, 83) : new(3, 83);
        return this with
        {
            Theme = Theme is "deep" or "aurora" or "violet" or "gold" ? Theme : "deep",
            Formation = Formation is "orbits" or "galaxy" or "matrix" ? Formation : "orbits",
            PageSize = Math.Clamp(PageSize, 1, 24),
            CardScale = double.IsFinite(CardScale) ? Math.Clamp(CardScale, .75, 1.3) : 1,
            DockKey = dock,
            SettingsKey = settings,
            Positions = (Positions ?? ImmutableDictionary<Guid, Position>.Empty).Where(p => double.IsFinite(p.Value.X) && double.IsFinite(p.Value.Y))
                .ToImmutableDictionary(p => p.Key, p => new Position(Math.Clamp(p.Value.X, 0, 1), Math.Clamp(p.Value.Y, 0, 1)))
        };
    }
}
public sealed record Catalog(int SchemaVersion, ImmutableArray<LaunchEntry> Entries, Preferences Settings)
{
    public static Catalog Empty => new(1, [], new());
    public Catalog Normalize()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("当前程序不支持此数据版本，未覆盖原文件。");
        var entries = (Entries.IsDefault ? [] : Entries).Where(e => e is not null && e.Id != Guid.Empty && !string.IsNullOrWhiteSpace(e.Name)
            && Enum.IsDefined(e.Kind) && (e.Kind == EntryKind.Group || !string.IsNullOrWhiteSpace(e.Target))).DistinctBy(e => e.Id).ToImmutableArray();
        var ids = entries.Select(e => e.Id).ToHashSet();
        return this with
        {
            Entries = entries.Select(e => e with
            {
                Members = e.Children.Where(id => id != e.Id && ids.Contains(id)
            && entries.First(x => x.Id == id).Kind != EntryKind.Group).Distinct().ToImmutableArray()
            }).ToImmutableArray(),
            Settings = (Settings ?? new()).Normalize()
        };
    }
}
public static class CatalogRules
{
    public static ImmutableArray<LaunchEntry> Move(ImmutableArray<LaunchEntry> entries, Guid source, Guid target)
    {
        var from = entries.FindIndex(e => e.Id == source); var to = entries.FindIndex(e => e.Id == target);
        if (from < 0 || to < 0 || from == to) return entries;
        var item = entries[from]; return entries.RemoveAt(from).Insert(to, item);
    }
    public static IReadOnlyList<IReadOnlyList<LaunchEntry>> Pages(Catalog catalog)
    {
        var grouped = catalog.Entries.SelectMany(e => e.Children).ToHashSet();
        var available = catalog.Entries.Where(e => !e.IsSidebar && !grouped.Contains(e.Id)).ToList();
        var pages = available.Where(e => e.Favorite).Chunk(catalog.Settings.PageSize).Select(e => (IReadOnlyList<LaunchEntry>)e).ToList();
        pages.AddRange(available.Where(e => !e.Favorite).Chunk(catalog.Settings.PageSize));
        return pages.Count == 0 ? new List<IReadOnlyList<LaunchEntry>> { Array.Empty<LaunchEntry>() } : pages;
    }
    private static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> predicate)
    { for (var i = 0; i < items.Length; i++) if (predicate(items[i])) return i; return -1; }
}
