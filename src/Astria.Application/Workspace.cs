using System.Collections.Immutable;
using Astria.Domain;
namespace Astria.Application;

public sealed class Workspace
{
    private readonly ICatalogStorage _storage;
    private readonly ISystemLauncher _launcher;
    private readonly SearchIndex _search = new();
    public Catalog Current { get; private set; }
    public event EventHandler? Changed;
    public Workspace(ICatalogStorage storage, ISystemLauncher launcher)
    { _storage = storage; _launcher = launcher; Current = storage.Load().Normalize(); }
    private void Commit(Catalog next)
    {
        next = next.Normalize(); _storage.Save(next); Current = next; Changed?.Invoke(this, EventArgs.Empty);
    }
    public ImportSummary Import(IEnumerable<string> sources)
    {
        var entries = Current.Entries.ToBuilder(); var errors = new List<string>(); var duplicates = 0; var added = 0;
        foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var target = _launcher.Resolve(source);
                if (entries.Any(e => e.Target.Equals(target.Target, StringComparison.OrdinalIgnoreCase) && e.Arguments == target.Arguments)) { duplicates++; continue; }
                entries.Add(new(Guid.NewGuid(), target.Name, target.Target, target.Kind, target.Arguments, target.WorkingDirectory, target.IconPath)); added++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
            { errors.Add(Path.GetFileName(source) + "：" + error.Message); }
        }
        if (added > 0) Commit(Current with { Entries = entries.ToImmutable() });
        return new(added, duplicates, errors);
    }
    public void SetFavorite(Guid id, bool value) => Update(id, e => e with { Favorite = value });
    public void SetBackground(Guid id, bool value) => Update(id, e => e with { TrackBackground = value });
    public void Rename(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) throw new ArgumentException("名称须为 1–80 个字符。");
        Update(id, e => e with { Name = name.Trim() });
    }
    public void Move(Guid id, Guid target)
    {
        var group = Current.Entries.FirstOrDefault(e => e.Kind == EntryKind.Group && e.Children.Contains(id) && e.Children.Contains(target));
        if (group is null) { Commit(Current with { Entries = CatalogRules.Move(Current.Entries, id, target) }); return; }
        var from = group.Children.IndexOf(id); var to = group.Children.IndexOf(target);
        Update(group.Id, e => e with { Members = e.Children.RemoveAt(from).Insert(to, id) });
    }
    public void Remove(Guid id) => Commit(Current with
    {
        Entries = Current.Entries.Where(e => e.Id != id).Select(e => e with { Members = e.Children.Remove(id) }).ToImmutableArray(),
        Settings = Current.Settings with { Positions = Current.Settings.Positions.Remove(id) }
    });
    public void Group(Guid first, Guid second, string name)
    {
        if (first == second) throw new ArgumentException("请选择两个不同的应用。");
        var members = Current.Entries.Where(e => e.Id == first || e.Id == second).ToArray();
        if (members.Length != 2 || members.Any(e => e.Kind == EntryKind.Group)) throw new ArgumentException("当前选择不能组成文件夹。");
        var group = new LaunchEntry(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? "新分组" : name.Trim(), "", EntryKind.Group, Members: [first, second]);
        var cleared = Current.Entries.Select(e => e with { Members = e.Children.Remove(first).Remove(second) }).ToImmutableArray();
        Commit(Current with { Entries = cleared.Add(group) });
    }
    public void ReplaceEntries(ImmutableArray<LaunchEntry> entries) => Commit(Current with { Entries = entries });
    public void SetSettings(Preferences settings, IReadOnlySet<Guid>? background = null) => Commit(Current with {
        Settings = settings, Entries = background is null ? Current.Entries : Current.Entries.Select(e=>e with {TrackBackground=background.Contains(e.Id)}).ToImmutableArray()
    });
    public void Place(Guid id, Position position) => SetSettings(Current.Settings with { Positions = Current.Settings.Positions.SetItem(id, position) });
    public string Clear()
    {
        var backup = _storage.CreateBackup(); Commit(Current with { Entries = [], Settings = Current.Settings with { Positions = ImmutableDictionary<Guid, Position>.Empty } }); return backup;
    }
    public string Backup() => _storage.CreateBackup();
    public void Restore(string file) { var next = _storage.ReadBackup(file).Normalize(); _storage.CreateBackup(); Commit(next); }
    public void ReplaceImported(Catalog imported) { _storage.CreateBackup(); Commit(imported); }
    public IReadOnlyList<LaunchEntry> Search(string text) => _search.Find(Current.Entries, text).ToArray();
    public IReadOnlyList<LaunchEntry> Library(string query, Guid? group = null)
    {
        if (!string.IsNullOrWhiteSpace(query)) return Search(query);
        if (group is { } id) return Current.Entries.FirstOrDefault(e => e.Id == id)?.Children.Select(x => Current.Entries.First(e => e.Id == x)).ToArray() ?? [];
        var members = Current.Entries.SelectMany(e => e.Children).ToHashSet(); return Current.Entries.Where(e => !members.Contains(e.Id)).ToArray();
    }
    public void Launch(LaunchEntry entry, IReadOnlyList<AppWindow> windows, bool forceNew = false)
    {
        if (entry.Kind == EntryKind.Group) return;
        var window = windows.FirstOrDefault(w => RunningIdentity.Matches(entry, w));
        if (!forceNew && window is not null) _launcher.Activate(window.Handle); else _launcher.Open(entry);
    }
    private void Update(Guid id, Func<LaunchEntry, LaunchEntry> change) => Commit(Current with { Entries = Current.Entries.Select(e => e.Id == id ? change(e) : e).ToImmutableArray() });
}
public static class RunningIdentity
{
    public static bool Matches(LaunchEntry entry, AppWindow window)
    {
        if(!string.IsNullOrWhiteSpace(entry.ApplicationId)&&!string.IsNullOrWhiteSpace(window.ApplicationId))
            return entry.ApplicationId.Equals(window.ApplicationId,StringComparison.OrdinalIgnoreCase);
        return Matches(entry.Target,window.Path);
    }
    public static bool Matches(string target, string executable)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(executable)) return false;
        if (target.Equals(executable, StringComparison.OrdinalIgnoreCase)) return true;
        static string? Family(string path)
        {
            var match = System.Text.RegularExpressions.Regex.Match(path, @"^(.*\\WindowsApps\\)([^\\]+?)_\d+\.\d+\.\d+\.\d+_([^\\]+)(\\.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value + match.Groups[2].Value + "_" + match.Groups[3].Value + match.Groups[4].Value : null;
        }
        var a = Family(target); return a is not null && a.Equals(Family(executable), StringComparison.OrdinalIgnoreCase);
    }
    public static bool IsRunning(LaunchEntry entry, IReadOnlyList<AppWindow> windows, IReadOnlySet<string> processes) =>
        entry.Kind == EntryKind.Application && (windows.Any(w => Matches(entry,w)) || entry.TrackBackground && processes.Any(p => Matches(entry.Target, p)));
}
