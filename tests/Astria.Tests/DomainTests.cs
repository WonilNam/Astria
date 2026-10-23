using System.Collections.Immutable;
using Astria.Domain;
using Xunit;
namespace Astria.Tests;

public sealed class DomainTests
{
    public static IEnumerable<object[]> LayoutCases() => from shape in new[] { "orbits", "galaxy", "matrix" }
                                                         from count in Enumerable.Range(1, 24)
                                                         from phase in new[] { 0d, 3d, 17d }
                                                         select new object[] { shape, count, phase };
    [Theory, MemberData(nameof(LayoutCases))]
    public void EveryConfiguredCardHasAVisibleNonoverlappingSlot(string shape, int count, double phase)
    {
        var poses = Formations.Arrange(shape, count, 1000, 700, phase);
        Assert.Equal(count, poses.Count);
        foreach (var p in poses)
        { Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && p.Scale > 0); Assert.InRange(p.X - 75 * p.Scale, -1, 1001); Assert.InRange(p.X + 75 * p.Scale, -1, 1001); Assert.InRange(p.Y - 86 * p.Scale, -5, 705); Assert.InRange(p.Y + 86 * p.Scale, -5, 705); }
        for (var i = 0; i < count; i++) for (var j = i + 1; j < count; j++)
        { var a = poses[i]; var b = poses[j]; Assert.True(Math.Abs(a.X - b.X) >= 75 * (a.Scale + b.Scale) || Math.Abs(a.Y - b.Y) >= 86 * (a.Scale + b.Scale), $"{shape}/{count}: {i} overlaps {j}"); }
    }
    [Fact]
    public void FourteenCardsRemainReadableInADesktopComposition()
    {
        foreach (var shape in new[] { "orbits", "galaxy", "matrix" })
            Assert.All(Formations.Arrange(shape, 14, 1360, 790, 0), pose => Assert.True(pose.Scale > .72, $"{shape}: {pose.Scale}"));
    }
    [Fact]
    public void FavoritePagesStaySeparateFromOtherAppsAndSidebarDoesNotConsumeSlots()
    {
        var entries = Enumerable.Range(0, 34).Select(i => Entry(i.ToString()) with { Favorite = i < 14 }).Append(Entry("Folder") with { Kind = EntryKind.Folder }).ToImmutableArray();
        var pages = CatalogRules.Pages(new(1, entries, new() { PageSize = 8 }));
        Assert.Equal(new[] { 8, 6, 8, 8, 4 }, pages.Select(p => p.Count));
        Assert.All(pages.Take(2).SelectMany(p => p), e => Assert.True(e.Favorite));
        Assert.All(pages.Skip(2).SelectMany(p => p), e => Assert.False(e.Favorite));
        Assert.Equal(34, pages.SelectMany(p => p).DistinctBy(e => e.Id).Count());
    }
    [Fact]
    public void MovingAcrossPagesUsesTheFinalTargetIndex()
    {
        var entries = Enumerable.Range(0, 45).Select(i => Entry(i.ToString())).ToImmutableArray();
        var moved = CatalogRules.Move(entries, entries[0].Id, entries[35].Id);
        Assert.Equal(entries[0].Id, moved[35].Id); Assert.Equal(45, moved.DistinctBy(e => e.Id).Count());
        Assert.Equal(entries[1].Id, moved[0].Id);
    }
    [Fact]
    public void InvalidPreferencesNormalizeWithoutAllowingConflictingKeys()
    {
        var p = new Preferences { CardScale = double.NaN, PageSize = 200, DockKey = new(3, 83), SettingsKey = new(3, 83), Formation = "unknown" }.Normalize();
        Assert.Equal(24, p.PageSize); Assert.Equal(1, p.CardScale); Assert.NotEqual(p.DockKey, p.SettingsKey); Assert.False(new KeyChord(3, 0x7B).IsValid);
    }
    internal static LaunchEntry Entry(string name) => new(Guid.NewGuid(), name, @"C:\Apps\" + name + ".exe", EntryKind.Application);
}
