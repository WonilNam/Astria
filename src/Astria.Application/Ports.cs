using Astria.Domain;
namespace Astria.Application;

public interface ICatalogStorage
{
    Catalog Load();
    void Save(Catalog catalog);
    string CreateBackup();
    Catalog ReadBackup(string file);
}
public sealed record AppWindow(nint Handle, string Title, string Path, string ApplicationId = "");
public sealed record ResolvedTarget(string Name, string Target, EntryKind Kind, string Arguments = "", string WorkingDirectory = "", string IconPath = "");
public interface ISystemLauncher
{
    ResolvedTarget Resolve(string path);
    void Open(LaunchEntry entry);
    IReadOnlyList<AppWindow> Windows();
    IReadOnlySet<string> BackgroundExecutables();
    void Activate(nint window);
}
public sealed record ImportSummary(int Added, int Duplicates, IReadOnlyList<string> Errors);
