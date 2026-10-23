using System.Collections.Immutable;
using System.Text.Json;
using Astria.Domain;
using Astria.Infrastructure;
using Xunit;
namespace Astria.Tests;

public sealed class StorageTests
{
    [Fact]
    public void RestartLoadsEntriesPinsKeysAndDesktopIconPreference()
    {
        using var folder = new TempFolder(); var storage = new CatalogStorage(folder.Path);
        var catalog = new Catalog(1, [DomainTests.Entry("App") with { Favorite = true }], new() { HideDesktopIconsOnStartup = true, SettingsKey = new(6, 83) });
        storage.Save(catalog); var restored = new CatalogStorage(folder.Path).Load();
        Assert.True(restored.Entries.Single().Favorite); Assert.True(restored.Settings.HideDesktopIconsOnStartup); Assert.Equal(new KeyChord(6, 83), restored.Settings.SettingsKey);
        var backup = storage.CreateBackup(); storage.Save(Catalog.Empty); Assert.Single(storage.ReadBackup(backup).Entries);
    }
    [Fact]
    public void CorruptOrNewerDataIsPreservedAndNeverSilentlyReset()
    {
        using var folder = new TempFolder(); var file = System.IO.Path.Combine(folder.Path, "catalog.json"); var bytes = "{malformed"; File.WriteAllText(file, bytes);
        Assert.Throws<InvalidDataException>(() => new CatalogStorage(folder.Path).Load()); Assert.Equal(bytes, File.ReadAllText(file));
        File.WriteAllText(file, "{\"schemaVersion\":99,\"entries\":[],\"settings\":{}}");
        Assert.Throws<InvalidDataException>(() => new CatalogStorage(folder.Path).Load()); Assert.Contains("99", File.ReadAllText(file));
    }
    [Fact]
    public void PreviousFormatImportsOrderAndPreferencesWithoutWritingSource()
    {
        using var folder = new TempFolder(); var first = Guid.NewGuid().ToString("N"); var second = Guid.NewGuid().ToString("N");
        var apps = JsonSerializer.Serialize(new { items = new[] { new { id = first, displayName = "One", kind = "Program", target = @"C:\one.exe" }, new { id = second, displayName = "Two", kind = "Program", target = @"C:\two.exe" } } });
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "apps.json"), apps);
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "dock-layout.json"), JsonSerializer.Serialize(new { pinnedItemIds = new[] { first } }));
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "launcher-layout.json"), JsonSerializer.Serialize(new { pages = new[] { new { slots = new[] { new { itemId = second }, new { itemId = first } } } } }));
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "desktop.json"), "{\"hideDesktopIconsOnStartup\":true,\"favoriteVisibleCount\":14,\"hotkey\":{\"modifiers\":1,\"virtualKey\":32}}");
        var snapshot = Directory.GetFiles(folder.Path).ToDictionary(p => p, File.ReadAllText);
        var catalog = CompatibilityImport.Read(folder.Path);
        Assert.Equal(Guid.Parse(second), catalog.Entries[0].Id); Assert.True(catalog.Entries[1].Favorite); Assert.Equal(14, catalog.Settings.PageSize); Assert.True(catalog.Settings.HideDesktopIconsOnStartup); Assert.Equal(new KeyChord(1, 32), catalog.Settings.DockKey);
        Assert.All(snapshot, p => Assert.Equal(p.Value, File.ReadAllText(p.Key)));
    }
}
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Astria-tests-" + Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}
