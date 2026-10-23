using Astria.AppServices;
using System.Collections.Immutable;
using Astria.Application;
using Astria.Domain;
using Astria.Infrastructure;
using Astria.Core.Models;
using Astria.Core.Contracts;
namespace Astria.Desktop.Services;

public sealed class DesktopSession:IDisposable
{
    public const int PageCapacity=21;
    public Workspace Engine{get;}
    public CatalogStorage Storage{get;}
    public WindowsLauncher Launcher{get;}=new();
    public DesktopPreferencesStore Preferences{get;}
    public PresentationData Data{get;}
    public PresentationServices Services{get;}
    public PresentationLoadReport LoadReport{get;}=new([]);
    private Catalog? _snapshot;
    private List<AppEntry> _entries=[];
    public DesktopSession(string root)
    {
        Storage=new(root);Engine=new(Storage,Launcher);Data=new(this,root);Services=new(this);Preferences=new(this);
        var old=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlassDesk.Avalonia","Data");
        var current=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Astria","Data");
        if(Path.GetFullPath(root).Equals(current,StringComparison.OrdinalIgnoreCase)&&!File.Exists(Path.Combine(root,"catalog.json"))&&File.Exists(Path.Combine(old,"apps.json")))
        {
            var migrated=new DesktopTransferService(this).RestoreFrom(old);
            if(!migrated.Succeeded)LoadReport=new(["原数据导入未完成，原目录未改动："+migrated.Error]);
        }
    }
    public IEnumerable<AppEntry> Entries
    {
        get
        {
            if(!ReferenceEquals(_snapshot,Engine.Current))
            {
                _snapshot=Engine.Current;
                _entries=Engine.Current.Entries.Where(e=>e.Kind!=EntryKind.Group).Select(ToPresentation).ToList();
            }
            return _entries;
        }
    }
    private static AppEntry ToPresentation(LaunchEntry entry)=>new(){Id=entry.Id.ToString("N"),DisplayName=entry.Name,Target=entry.Target,
        Arguments=entry.Arguments,WorkingDirectory=entry.WorkingDirectory,IconSource=entry.IconPath,CustomIconPath=entry.CustomIconPath,AppUserModelId=entry.ApplicationId,SearchAliases=entry.SearchAliases.ToList(),
        Kind=entry.Kind switch {EntryKind.Application=>AppItemKind.Program,EntryKind.Folder=>AppItemKind.Folder,EntryKind.Shell=>AppItemKind.ShellLocation,_=>AppItemKind.File}};
    public IEnumerable<LauncherSlot> Slots
    {
        get
        {
            var members=Engine.Current.Entries.SelectMany(e=>e.Children).ToHashSet();
            return Engine.Current.Entries.Where(e=>!members.Contains(e.Id)).Select(e=>e.Kind==EntryKind.Group
                ?new LauncherSlot(LauncherSlotKind.Group,Group:new(e.Id.ToString("N"),e.Name,e.Children.Select(id=>id.ToString("N")).ToList()))
                :new LauncherSlot(LauncherSlotKind.App,e.Id.ToString("N")));
        }
    }
    public bool IsPinned(string id)=>Guid.TryParse(id,out var key)&&Engine.Current.Entries.Any(e=>e.Id==key&&e.Favorite);
    public ImportResult Import(IReadOnlyList<ImportRequest> requests)
    {
        var added=new List<AppEntry>();var duplicates=new List<AppEntry>();var failures=new List<ImportFailure>();
        foreach(var request in requests)
        {
            var before=Engine.Current.Entries.Select(e=>e.Id).ToHashSet();var result=Engine.Import([request.Source]);
            if(result.Errors.Count>0){failures.Add(new(request.Source,string.Join("；",result.Errors)));continue;}
            if(result.Added>0)
            {
                var item=Engine.Current.Entries.First(e=>!before.Contains(e.Id));
                if(!string.IsNullOrWhiteSpace(request.DisplayName))Engine.Rename(item.Id,request.DisplayName);
                added.Add(Entries.First(e=>e.Id==item.Id.ToString("N")));
            }
            else if(Entries.FirstOrDefault() is {} duplicate)duplicates.Add(duplicate);
        }
        return new(added,duplicates,failures);
    }
    public void SetPinned(string id,bool value)=>Engine.SetFavorite(Guid.Parse(id),value);
    public void MoveDock(string id,string target)=>Engine.Move(Guid.Parse(id),Guid.Parse(target));
    public void MoveLauncher(string id,string target)=>MoveDock(id,target);
    public void SwapItems(string first,string second)
    {
        var entries=Engine.Current.Entries.ToArray();var a=Array.FindIndex(entries,e=>e.Id==Guid.Parse(first));var b=Array.FindIndex(entries,e=>e.Id==Guid.Parse(second));
        if(a<0||b<0)return;
        if(entries[a].Favorite!=entries[b].Favorite){var firstPinned=entries[a].Favorite;entries[a]=entries[a] with {Favorite=entries[b].Favorite};entries[b]=entries[b] with {Favorite=firstPinned};}
        (entries[a],entries[b])=(entries[b],entries[a]);Engine.ReplaceEntries(entries.ToImmutableArray());
    }
    public string CreateFolder(string first,string second,string name)
    {
        var before=Engine.Current.Entries.Select(e=>e.Id).ToHashSet();Engine.Group(Guid.Parse(first),Guid.Parse(second),name);
        return Engine.Current.Entries.Single(e=>!before.Contains(e.Id)).Id.ToString("N");
    }
    public void RenameFolder(string id,string name)=>Engine.Rename(Guid.Parse(id),name);
    public void DissolveFolder(string id)=>Engine.Remove(Guid.Parse(id));
    public void AddToFolder(string app,string folder)
    {
        var item=Guid.Parse(app);var group=Guid.Parse(folder);
        Engine.ReplaceEntries(Engine.Current.Entries.Select(e=>e with {Members=e.Id==group?e.Children.Remove(item).Add(item):e.Children.Remove(item)}).ToImmutableArray());
    }
    public void RemoveFromFolder(string app,string folder)
    {var item=Guid.Parse(app);var group=Guid.Parse(folder);Engine.ReplaceEntries(Engine.Current.Entries.Select(e=>e.Id==group?e with {Members=e.Children.Remove(item)}:e).ToImmutableArray());}
    public void MoveFolderMember(string app,string folder,string target)=>Engine.Move(Guid.Parse(app),Guid.Parse(target));
    public IReadOnlyList<AppEntry> Search(string query)
    {var ids=Engine.Search(query).Select(e=>e.Id.ToString("N")).ToList();return Entries.Where(e=>ids.Contains(e.Id)).OrderBy(e=>ids.IndexOf(e.Id)).ToArray();}
    public string ClearLibrary()
    {
        var snapshot=Engine.Backup();var cleared=Preferences.Current with {CardLocations=[]};
        Engine.ReplaceImported(Engine.Current with {Entries=[],Settings=Engine.Current.Settings with {PresentationJson=System.Text.Json.JsonSerializer.Serialize(cleared,DesktopJsonContext.Default.DesktopPreferences)}});
        Preferences.Reload();return snapshot;
    }
    public void SaveDefaults()=>Storage.Save(Engine.Current);
    internal void SaveProjectedEntries()
    {
        var map=Entries.ToDictionary(e=>Guid.Parse(e.Id));
        Engine.ReplaceEntries(Engine.Current.Entries.Select(e=>map.TryGetValue(e.Id,out var p)?e with {Name=p.DisplayName,Target=p.Target,Arguments=p.Arguments??"",WorkingDirectory=p.WorkingDirectory??"",IconPath=p.IconSource??"",CustomIconPath=p.CustomIconPath,ApplicationId=p.AppUserModelId,SearchAliases=p.SearchAliases.ToArray()}:e).ToImmutableArray());
    }
    public static string? SlotId(LauncherSlot slot)=>slot.Group?.Id??slot.ItemId;
    public void Dispose(){}
}
public sealed record PresentationLoadReport(IReadOnlyList<string> Notices);
public sealed class PresentationData(DesktopSession session,string root)
{
    public PresentationPaths Paths{get;}=new(root);
    public PresentationLibrary Library{get;}=new(session);
    public PresentationLayouts Layouts{get;}=new(session);
    public bool DeleteFromLibrary(string id){session.Engine.Remove(Guid.Parse(id));return true;}
}
public sealed class PresentationPaths(string root)
{
    public string RootDirectory=>root;public string BackupDirectory=>Path.Combine(root,"backups");
}
public sealed class PresentationLibrary(DesktopSession session)
{
    public IEnumerable<AppEntry> Items=>session.Entries;
    public AppEntry? Find(string id)=>session.Entries.FirstOrDefault(e=>e.Id==id);
    public void Save()=>session.SaveProjectedEntries();
}
public sealed class PresentationLayouts(DesktopSession session)
{
    public PresentationDock Dock=>new(session.Engine.Current.Entries.Where(e=>e.Favorite).Select(e=>e.Id.ToString("N")).ToList());
}
public sealed record PresentationDock(List<string> PinnedItemIds);
public sealed class PresentationServices(DesktopSession session)
{
    public PresentationLibrary Library{get;}=new(session);
    public PresentationEditor Editor{get;}=new(session);
    public PresentationLaunch Launch{get;}=new(session);
    public PresentationRunning Running{get;}=new(session.Launcher);
}
public sealed class PresentationRunning(WindowsLauncher launcher)
{
    public IReadOnlyList<Astria.Platform.Shell.WindowIdentityInfo> LastWindows {get;private set;}=[];
    public void Reconcile()=>LastWindows=launcher.Windows().Select(w=>new Astria.Platform.Shell.WindowIdentityInfo(w.Handle,w.Title,w.Path,w.ApplicationId)).ToArray();
    public bool IsRunning(AppEntry entry)=>LastWindows.Any(w=>!string.IsNullOrWhiteSpace(entry.AppUserModelId)&&!string.IsNullOrWhiteSpace(w.EffectiveAumid)
        ?entry.AppUserModelId.Equals(w.EffectiveAumid,StringComparison.OrdinalIgnoreCase):RunningIdentity.Matches(entry.Target,w.ImagePath??""));
}
public sealed class PresentationEditor(DesktopSession session)
{
    public bool Edit(string id,string name,IReadOnlyList<string> aliases,string? icon)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>80)return false;
        var key=Guid.Parse(id);string? stored=null;
        if(icon is not null)
        {
            if(!File.Exists(icon))return false;
            try{using var check=new Avalonia.Media.Imaging.Bitmap(icon);}catch(ArgumentException){return false;}
            var folder=Path.Combine(session.Storage.DirectoryPath,"icons");Directory.CreateDirectory(folder);
            stored=Path.Combine(folder,Guid.NewGuid().ToString("N")+Path.GetExtension(icon));File.Copy(icon,stored);
        }
        session.Engine.ReplaceEntries(session.Engine.Current.Entries.Select(e=>e.Id==key?e with {Name=name,SearchAliases=aliases.ToArray(),CustomIconPath=stored??e.CustomIconPath}:e).ToImmutableArray());return true;
    }
}
