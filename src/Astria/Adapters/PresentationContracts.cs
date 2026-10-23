namespace Astria.Core.Models
{
    public enum AppItemKind { Program, Shortcut, File, Folder, Url, ShellLocation }
    public sealed class AppEntry
    {
        public string Id {get;set;}=Guid.NewGuid().ToString("N");
        public string DisplayName {get;set;}="";
        public string Target {get;set;}="";
        public AppItemKind Kind {get;set;}
        public string? Arguments {get;set;}
        public string? WorkingDirectory {get;set;}
        public string? IconSource {get;set;}
        public string? CustomIconPath {get;set;}
        public string? AppUserModelId {get;set;}
        public List<string> SearchAliases {get;set;}=[];
        public bool IsTargetMissing {get;set;}
        public bool IsUsable=>!string.IsNullOrWhiteSpace(Target);
    }
    public enum LauncherSlotKind { App, Group }
    public sealed record LauncherGroup(string Id,string Name,List<string> ItemIds);
    public sealed record LauncherSlot(LauncherSlotKind Kind,string? ItemId=null,LauncherGroup? Group=null);
    public static class ShellLocations
    {
        public static string Name(string target)=>target==ThisComputer?"此电脑":target==RecycleBin?"回收站":target;
        public const string ThisComputer=Astria.Infrastructure.WindowsLauncher.Computer;
        public const string RecycleBin=Astria.Infrastructure.WindowsLauncher.RecycleBin;
    }
}
namespace Astria.Core.Contracts
{
    public sealed record ImportRequest {public required string Source{get;init;}public string? DisplayName{get;init;}}
    public sealed record ImportFailure(string Source,string Message);
    public sealed record ImportResult(IReadOnlyList<Astria.Core.Models.AppEntry> Added,IReadOnlyList<Astria.Core.Models.AppEntry> Duplicates,IReadOnlyList<ImportFailure> Failures);
}
namespace Astria.Core.Storage
{
    public sealed class StorageException(string message,Exception? inner=null):IOException(message,inner);
    public sealed record TransferResult
    {
        public bool Succeeded{get;init;}public string? Error{get;init;}public string Directory{get;init;}="";
        public string? SafetyCopyDirectory{get;init;}
    }
}
