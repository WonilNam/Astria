using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Astria.Desktop.Services;
using Astria.Desktop.Views;
using Astria.Desktop.ViewModels;
using Astria.Domain;
using Xunit;
[assembly:AvaloniaTestApplication(typeof(Astria.Tests.RestoredUiBuilder))]
namespace Astria.Tests;
public static class RestoredUiBuilder
{
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<RestoredTestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false});
}
public sealed class RestoredTestApp:Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant=ThemeVariant.Dark;Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Astria/")){Source=new Uri("avares://Astria/Styles/Theme.axaml")});
    }
}
public sealed class RestoredUiTests
{
    [AvaloniaFact]
    public void OriginalDockRendersAllFavoritesWithItsOriginalShapes()
    {
        using var folder=new TempFolder();using var session=new DesktopSession(folder.Path);
        session.Engine.ReplaceEntries(Enumerable.Range(0,14).Select(i=>DomainTests.Entry("Favorite "+i) with {Favorite=true}).ToImmutableArray());
        session.Preferences.Save(session.Preferences.Current with {FavoriteVisibleCount=14,ReduceMotion=true});
        var model=new WorkspaceViewModel(session);var dock=new DockWindow(session,model,_=>{},(_,_)=>{});
        var path=Path.Combine(Repository(),"artifacts","restored-preview");Directory.CreateDirectory(path);
        try
        {
            dock.Width=1440;dock.Height=960;dock.Show();
            foreach(var shape in new[]{"orbits","galaxy","matrix"})
            {
                model.SetFavoriteShape(shape);dock.ApplyPreferences();using var frame=dock.CaptureRenderedFrame();
                Assert.Equal(14,dock.VisibleRingCapacity);Assert.NotNull(frame);frame.Save(Path.Combine(path,shape+".png"));
            }
            dock.MovePage(1);Assert.Equal(0,dock.AlbumPage);
        }
        finally{dock.Shutdown();}
    }
    [AvaloniaFact]
    public void OriginalManagerRetainsTwentyOneTilesAndTypeToSearch()
    {
        using var folder=new TempFolder();using var session=new DesktopSession(folder.Path);
        session.Engine.ReplaceEntries(Enumerable.Range(0,30).Select(i=>DomainTests.Entry("Application "+i)).ToImmutableArray());
        var model=new WorkspaceViewModel(session);var window=new WorkspaceWindow(session,model,(_,_)=>{});
        var path=Path.Combine(Repository(),"artifacts","restored-preview");Directory.CreateDirectory(path);
        try
        {
            window.ShowSection(WorkspaceSection.Library);Assert.Equal(21,model.VisibleApps.Count);
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(path,"management.png"));}
            model.NextPage();Assert.Equal(9,model.VisibleApps.Count);
            window.ShowSection(WorkspaceSection.Settings);
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(path,"settings.png"));}
            window.ShowSection(WorkspaceSection.Library);window.KeyTextInput("Application 29");
            Assert.Single(model.VisibleApps);Assert.Equal("Application 29",model.VisibleApps[0].Name);
        }
        finally{window.Shutdown();}
    }
    [Fact]
    public void OriginalUiFilesMatchTheApprovedBaselineByteForByte()
    {
        var root=Repository();using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"UI-BASELINE.json")));
        foreach(var file in json.RootElement.GetProperty("files").EnumerateObject())
        {
            var actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"src","Astria",file.Name)))).ToLowerInvariant();
            Assert.Equal(file.Value.GetString(),actual);
        }
    }
    [AvaloniaFact]
    public void CustomIconsAndSearchAliasesRemainAvailableAfterBackupRestore()
    {
        using var folder=new TempFolder();using var session=new DesktopSession(folder.Path);
        session.Engine.ReplaceEntries([DomainTests.Entry("Calculator")]);
        var item=session.Entries.Single();var icon=Path.Combine(Repository(),"src","Astria","Assets","Astria.png");
        Assert.True(session.Services.Editor.Edit(item.Id,"Calculator",["计算工具"],icon));
        Assert.Single(session.Search("计算工具"));var backup=Path.Combine(folder.Path,"snapshot");
        Assert.True(new DesktopTransferService(session).BackupTo(backup).Succeeded);
        session.ClearLibrary();Assert.Empty(session.Entries);
        Assert.True(new DesktopTransferService(session).RestoreFrom(backup).Succeeded);
        Assert.True(File.Exists(session.Entries.Single().CustomIconPath));Assert.Single(session.Search("计算工具"));
    }
    [Fact]
    public void OriginalPresentationPreferencesAndGroupsRoundTripThroughNewStorage()
    {
        using var folder=new TempFolder();
        using(var session=new DesktopSession(folder.Path))
        {
            session.Engine.ReplaceEntries([DomainTests.Entry("One"),DomainTests.Entry("Two")]);
            var entries=session.Entries.ToArray();session.CreateFolder(entries[0].Id,entries[1].Id,"Tools");
            session.Preferences.Save(session.Preferences.Current with {Accent="neon",FavoriteVisibleCount=14,ImmersiveLaunchpad=false,BackgroundRunningIds=[entries[0].Id],CardLayoutVersion=4});
        }
        using var reopened=new DesktopSession(folder.Path);
        Assert.Equal("neon",reopened.Preferences.Current.Accent);Assert.Equal(14,reopened.Preferences.Current.FavoriteVisibleCount);
        Assert.False(reopened.Preferences.Current.ImmersiveLaunchpad);Assert.Single(reopened.Preferences.Current.BackgroundRunningIds);
        Assert.Equal("Tools",reopened.Slots.Single().Group!.Name);Assert.Equal(2,reopened.Slots.Single().Group!.ItemIds.Count);
    }
    private static string Repository(){var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Astria.slnx")))root=root.Parent;return root!.FullName;}
}
