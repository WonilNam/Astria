using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
namespace Astria.Platform.Shell;
public sealed record WindowIdentityInfo(nint Handle,string Title,string? ImagePath,string? EffectiveAumid=null);
public static class WindowIdentity
{
    public static string? TryGetProcessAumid(uint id)
    {
        var handle=OpenProcess(0x1000,false,id);if(handle==0)return null;
        try
        {
            uint length=0;if(GetApplicationUserModelId(handle,ref length,null)!=122||length==0)return null;
            var result=new StringBuilder((int)length);return GetApplicationUserModelId(handle,ref length,result)==0?result.ToString():null;
        }
        finally{CloseHandle(handle);}
    }
    [DllImport("kernel32.dll")]private static extern nint OpenProcess(uint access,bool inherit,uint id);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern int GetApplicationUserModelId(nint handle,ref uint length,StringBuilder? value);
}
public static class ShellExecutor {public static void Open(string target)=>Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}
public sealed record StartMenuShortcut(string Name,string Path,string Group);
public sealed record StartMenuScan(IReadOnlyList<StartMenuShortcut> Shortcuts);
public static class StartMenuScanner
{
    public static StartMenuScan Scan()
    {
        var entries=new List<StartMenuShortcut>();
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)})
        {if(!Directory.Exists(root))continue;foreach(var file in Directory.EnumerateFiles(root,"*.lnk",SearchOption.AllDirectories))entries.Add(new(Path.GetFileNameWithoutExtension(file),file,Path.GetRelativePath(root,Path.GetDirectoryName(file)!)));}
        return new(entries.DistinctBy(e=>e.Path,StringComparer.OrdinalIgnoreCase).OrderBy(e=>e.Name).ToArray());
    }
}
