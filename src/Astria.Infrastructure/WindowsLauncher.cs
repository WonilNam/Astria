using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Astria.Application;
using Astria.Domain;
namespace Astria.Infrastructure;

public sealed class WindowsLauncher : ISystemLauncher
{
    public const string Computer = "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    public const string RecycleBin = "shell:::{645FF040-5081-101B-9F08-00AA002F954E}";
    public ResolvedTarget Resolve(string path)
    {
        if (path == Computer || path == RecycleBin) return new(path == Computer ? "此电脑" : "回收站", path, EntryKind.Shell);
        path = Path.GetFullPath(path);
        if (Directory.Exists(path)) return new(new DirectoryInfo(path).Name, path, EntryKind.Folder, IconPath: path);
        if (!File.Exists(path)) throw new FileNotFoundException("路径不存在", path);
        var name = Path.GetFileNameWithoutExtension(path);
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            object? shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                link = ((dynamic)shell!).CreateShortcut(path);
                var target = (string)((dynamic)link).TargetPath;
                if (!string.IsNullOrWhiteSpace(target)) return new(name, target, Directory.Exists(target) ? EntryKind.Folder : EntryKind.Application,
                    (string)((dynamic)link).Arguments, (string)((dynamic)link).WorkingDirectory, path);
            }
            finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
            // Shell links to virtual locations are launched by the Shell itself.
            return new(name, path, EntryKind.File, IconPath: path);
        }
        return new(name, path, Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? EntryKind.Application : EntryKind.File, IconPath: path);
    }
    public void Open(LaunchEntry entry)
    {
        var target = entry.Target;
        if (!File.Exists(target) && !Directory.Exists(target) && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            // Recover a versioned WindowsApps path by exact package identity and relative executable.
            var marker = target.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                var packageRoot = target[..(marker + 13)]; var tail = target[(marker + 13)..]; var slash = tail.IndexOf('\\');
                if (slash > 0 && Directory.Exists(packageRoot))
                    target = Directory.EnumerateDirectories(packageRoot).Select(folder => Path.Combine(folder, tail[(slash + 1)..]))
                        .FirstOrDefault(candidate => File.Exists(candidate) && RunningIdentity.Matches(entry.Target, candidate)) ?? target;
            }
        }
        if (!File.Exists(target) && !Directory.Exists(target) && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("目标已移动或删除，请重新添加。", target);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = entry.Arguments, WorkingDirectory = Directory.Exists(entry.WorkingDirectory) ? entry.WorkingDirectory : "" });
    }
    public IReadOnlyList<AppWindow> Windows()
    {
        var windows = new List<AppWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0) return true;
            if (DwmGetWindowAttribute(handle, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(handle, out var id); if (id == Environment.ProcessId) return true;
            var path = ProcessPath(id); if (path.Length == 0) return true;
            var title = new StringBuilder(512); GetWindowText(handle, title, title.Capacity);
            windows.Add(new(handle, title.ToString(), path, Astria.Platform.Shell.WindowIdentity.TryGetProcessAumid(id) ?? "")); return true;
        }, 0);
        return windows;
    }
    public IReadOnlySet<string> BackgroundExecutables()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses()) using (process)
        { try { var path = ProcessPath((uint)process.Id); if (path.Length > 0) result.Add(path); } catch (InvalidOperationException) { } }
        return result;
    }
    private static string ProcessPath(uint id)
    {
        var handle = OpenProcess(0x1000, false, id); if (handle == 0) return "";
        try { var path = new StringBuilder(32768); var length = path.Capacity; return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : ""; }
        finally { CloseHandle(handle); }
    }
    public bool MinimizeIfForeground(nint window)
    {if(GetForegroundWindow()!=window)return false;ShowWindow(window,6);return true;}
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    public void Activate(nint window) { if (IsIconic(window)) ShowWindow(window, 9); SetForegroundWindow(window); }
    private delegate bool Enumerate(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Enumerate callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint id);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint handle, uint flags, StringBuilder path, ref int size);
}
