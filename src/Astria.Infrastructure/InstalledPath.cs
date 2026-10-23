using Astria.Application;

namespace Astria.Infrastructure;

public static class InstalledPath
{
    public static string Resolve(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return path;
        var marker = path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return path;
        var root = path[..(marker + 13)];
        var tail = path[(marker + 13)..];
        var slash = tail.IndexOf('\\');
        if (slash <= 0 || !Directory.Exists(root)) return path;
        try
        {
            return Directory.EnumerateDirectories(root)
                .Select(folder => Path.Combine(folder, tail[(slash + 1)..]))
                .FirstOrDefault(candidate => File.Exists(candidate) && RunningIdentity.Matches(path, candidate)) ?? path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }
}
