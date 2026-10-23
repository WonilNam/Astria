using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Astria.Core.Models;
using Astria.Platform.Shell;

namespace Astria.Desktop.Services;

public static class DesktopRunningState
{
    public static IReadOnlyDictionary<string, string?> CaptureBackgroundProcesses()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            nint handle;
            try { handle = OpenProcess(0x1000, false, process.Id); }
            catch (InvalidOperationException) { continue; }
            if (handle == 0) continue;
            try
            {
                var buffer = new char[32768]; var length = buffer.Length;
                if (QueryFullProcessImageName(handle, 0, buffer, ref length))
                    result.TryAdd(new string(buffer, 0, length), WindowIdentity.TryGetProcessAumid((uint)process.Id));
            }
            finally { CloseHandle(handle); }
        }
        return result;
    }

    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(nint handle, int flags, [Out] char[] path, ref int size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    public static string? FindUpdatedTarget(AppEntry entry, IEnumerable<string> candidates)
    {
        var identity = PackageExecutableIdentity(NormalizePath(entry.Target));
        if (identity is null) return null;
        return candidates.FirstOrDefault(path => string.Equals(identity, PackageExecutableIdentity(NormalizePath(path)), StringComparison.OrdinalIgnoreCase));
    }

    public static string? FindInstalledTarget(AppEntry entry)
    {
        var path = NormalizePath(entry.Target);
        var marker = path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;
        var start = marker + "\\WindowsApps\\".Length;
        var end = path.IndexOf('\\', start);
        if (end < 0) return null;
        try
        {
            var candidates = Directory.EnumerateDirectories(path[..start]).Select(folder => Path.Combine(folder, path[(end + 1)..]))
                .Where(File.Exists);
            return FindUpdatedTarget(entry, candidates);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
    public static IReadOnlyDictionary<string, string?> CaptureWindows(IEnumerable<WindowIdentityInfo> windows)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.Title)) continue;
            if (!string.IsNullOrWhiteSpace(window.ImagePath)) result.TryAdd(window.ImagePath, window.EffectiveAumid);
            if (!string.IsNullOrWhiteSpace(window.EffectiveAumid)) result.TryAdd("aumid:" + window.EffectiveAumid, window.EffectiveAumid);
        }
        return result;
    }

    public static bool Matches(AppEntry entry, IReadOnlyDictionary<string, string?> processes)
    {
        if (!string.IsNullOrWhiteSpace(entry.AppUserModelId)
            && processes.Values.Any(identity => string.Equals(entry.AppUserModelId, identity, StringComparison.OrdinalIgnoreCase))) return true;
        var target = NormalizePath(entry.Target);
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var package = PackageExecutableIdentity(target);
        foreach (var process in processes)
        {
            // A shared executable with conflicting explicit identities is not the same application.
            if (!string.IsNullOrWhiteSpace(entry.AppUserModelId) && !string.IsNullOrWhiteSpace(process.Value)
                && !string.Equals(entry.AppUserModelId, process.Value, StringComparison.OrdinalIgnoreCase)) continue;
            var path = NormalizePath(process.Key);
            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase)) return true;
            if (package is not null && string.Equals(package, PackageExecutableIdentity(path), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string NormalizePath(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).Replace('/', '\\');

    private static string? PackageExecutableIdentity(string path)
    {
        const string root = "\\WindowsApps\\";
        var start = path.IndexOf(root, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += root.Length;
        var end = path.IndexOf('\\', start);
        if (end < 0) return null;
        var package = Regex.Match(path[start..end], @"^(?<name>.+)_(?<version>\d+(?:\.\d+){3})_(?<architecture>[^_]+)_(?<resource>[^_]*)_(?<publisher>[^_]+)$", RegexOptions.CultureInvariant);
        if (!package.Success) return null;
        return string.Join('|', path[..start], package.Groups["name"].Value, package.Groups["architecture"].Value,
            package.Groups["resource"].Value, package.Groups["publisher"].Value, path[(end + 1)..]);
    }

}
