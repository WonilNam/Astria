using System.Collections.Immutable;
using System.Text.Json;
using Astria.Core.Storage;
using Astria.Infrastructure;
namespace Astria.Desktop.Services;
public sealed class DesktopTransferService(DesktopSession session)
{
    public TransferResult BackupTo(string destination)
    {
        try
        {
            Directory.CreateDirectory(destination);
            var entries=session.Engine.Current.Entries.ToBuilder();
            for(var i=0;i<entries.Count;i++)if(entries[i].CustomIconPath is {} icon&&File.Exists(icon))
            {
                var relative="icons/"+Path.GetFileName(icon);var path=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(icon,path,true);
                entries[i]=entries[i] with {CustomIconPath=relative};
            }
            new CatalogStorage(destination).Save(session.Engine.Current with {Entries=entries.ToImmutable()});
            return new(){Succeeded=true,Directory=destination};
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return new(){Succeeded=false,Directory=destination,Error=error.Message};}
    }
    public TransferResult RestoreFrom(string source)
    {
        var safety=session.Engine.Backup();
        try
        {
            var catalog=File.Exists(Path.Combine(source,"catalog.json"))?new CatalogStorage(source).Load():CompatibilityImport.Read(source);
            if(File.Exists(Path.Combine(source,"desktop.json")))
            {
                var p=JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(source,"desktop.json")),DesktopJsonContext.Default.DesktopPreferences)?.Normalize()??throw new InvalidDataException("外观配置无效");
                catalog=catalog with {Settings=catalog.Settings with {PresentationJson=JsonSerializer.Serialize(p,DesktopJsonContext.Default.DesktopPreferences)}};
            }
            var icons=Path.Combine(session.Storage.DirectoryPath,"icons");Directory.CreateDirectory(icons);
            catalog=catalog with {Entries=catalog.Entries.Select(entry=>
            {
                if(entry.CustomIconPath is not {} custom)return entry;
                var path=Path.IsPathRooted(custom)?custom:Path.Combine(source,custom);
                if(!File.Exists(path))return entry with {CustomIconPath=null};
                var target=Path.Combine(icons,Guid.NewGuid().ToString("N")+Path.GetExtension(path));File.Copy(path,target);
                return entry with {CustomIconPath=target};
            }).ToImmutableArray()};
            session.Engine.ReplaceImported(catalog);session.Preferences.Reload();return new(){Succeeded=true,Directory=source,SafetyCopyDirectory=safety};
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException){return new(){Succeeded=false,Directory=source,Error=error.Message,SafetyCopyDirectory=safety};}
    }
}
