using System.Collections.Immutable;
using Astria.Domain;
using Astria.Application;
using Xunit;
namespace Astria.Tests;

public sealed class ApplicationTests
{
    [Fact]
    public void FailedSaveDoesNotChangeInMemoryStateOrNotify()
    {
        var storage = new MemoryStorage(); var workspace = new Workspace(storage, new FakeLauncher()); var changes = 0; workspace.Changed += (_, _) => changes++;
        storage.Fail = true; Assert.Throws<IOException>(() => workspace.Import(["one.exe"])); Assert.Empty(workspace.Current.Entries); Assert.Equal(0, changes);
    }
    [Fact]
    public void ImportDeduplicatesTargetsAndPreservesFoldersAndShellLocations()
    {
        var workspace = new Workspace(new MemoryStorage(), new FakeLauncher());
        var result = workspace.Import(["one.exe", "ONE.exe", "folder", "shell:computer"]);
        Assert.Equal(3, result.Added); Assert.Contains(workspace.Current.Entries, e => e.Kind == EntryKind.Folder); Assert.Contains(workspace.Current.Entries, e => e.Kind == EntryKind.Shell);
    }
    [Theory]
    [InlineData("微信")]
    [InlineData("weixin")]
    [InlineData("wx")]
    public void SearchAcceptsChinesePinyinAndInitials(string query)
    {
        var search = new SearchIndex(); var wechat = DomainTests.Entry("微信"); var result = search.Find([DomainTests.Entry("Excel"), wechat], query); Assert.Equal(wechat, Assert.Single(result));
    }
    [Fact]
    public void GroupSortingAndDissolvingPreserveMembers()
    {
        var workspace = new Workspace(new MemoryStorage(), new FakeLauncher()); workspace.Import(["one.exe", "two.exe"]);
        var first = workspace.Current.Entries[0]; var second = workspace.Current.Entries[1]; workspace.Group(first.Id, second.Id, "Tools");
        var group = Assert.Single(workspace.Current.Entries.Where(e => e.Kind == EntryKind.Group)); Assert.Single(workspace.Library(""));
        workspace.Move(second.Id, first.Id); Assert.Equal(second.Id, workspace.Library("", group.Id)[0].Id);
        workspace.Remove(group.Id); Assert.Equal(2, workspace.Library("").Count);
    }
    [Fact]
    public void ClearCreatesBackupBeforeRemovingEntries()
    {
        var storage = new MemoryStorage(); var workspace = new Workspace(storage, new FakeLauncher()); workspace.Import(["one.exe"]);
        workspace.Clear(); Assert.Equal(1, storage.Backups); Assert.Empty(workspace.Current.Entries);
    }
    [Fact]
    public void WindowActivationReusesExactIdentityAndBackgroundDetectionIsOptIn()
    {
        var launcher = new FakeLauncher(); var workspace = new Workspace(new MemoryStorage(), launcher); var entry = DomainTests.Entry("WeBee");
        var windows = new[] { new AppWindow(12, "WeBee", entry.Target) }; workspace.Launch(entry, windows); Assert.Equal((nint)12, launcher.Activated); Assert.Equal(0, launcher.Opened);
        workspace.Launch(entry, windows, true); Assert.Equal(1, launcher.Opened);
        var background = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entry.Target };
        Assert.False(RunningIdentity.IsRunning(entry, [], background)); Assert.True(RunningIdentity.IsRunning(entry with { TrackBackground = true }, [], background));
        Assert.True(RunningIdentity.IsRunning(entry, windows, new HashSet<string>()));
    }
    [Fact]
    public void PackageVersionChangesMatchButPublisherOrExecutableChangesDoNot()
    {
        const string original = @"C:\Program Files\WindowsApps\Example.App_1.2.3.4_x64__publisher\bin\app.exe";
        Assert.True(RunningIdentity.Matches(original, original.Replace("1.2.3.4", "2.3.4.5")));
        Assert.False(RunningIdentity.Matches(original, original.Replace("publisher", "other")));
        Assert.False(RunningIdentity.Matches(original, original.Replace("app.exe", "helper.exe")));
    }
}
internal sealed class MemoryStorage : ICatalogStorage
{
    public Catalog Value = Catalog.Empty; public bool Fail; public int Backups;
    public Catalog Load() => Value; public void Save(Catalog catalog) { if (Fail) throw new IOException("disk full"); Value = catalog; }
    public string CreateBackup() { Backups++; return "backup.json"; }
    public Catalog ReadBackup(string path) => Value;
}
internal sealed class FakeLauncher : ISystemLauncher
{
    public nint Activated; public int Opened;
    public ResolvedTarget Resolve(string path) => new(path, path, path == "folder" ? EntryKind.Folder : path.StartsWith("shell:") ? EntryKind.Shell : EntryKind.Application);
    public void Open(LaunchEntry entry) => Opened++;
    public IReadOnlyList<AppWindow> Windows() => []; public IReadOnlySet<string> BackgroundExecutables() => new HashSet<string>(); public void Activate(nint handle) => Activated = handle;
}
