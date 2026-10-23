using Astria.Infrastructure;
using Xunit;
namespace Astria.Tests;

public sealed class InstalledPathTests
{
    [Fact]
    public void ResolvesUpdatedPackageWithoutChoosingAnotherPublisher()
    {
        using var folder = new TempFolder();
        var root = Path.Combine(folder.Path, "WindowsApps");
        var old = Path.Combine(root, "OpenAI.Codex_1.0.0.0_x64__publisher", "app", "ChatGPT.exe");
        var wrong = Path.Combine(root, "OpenAI.Codex_2.0.0.0_x64__other", "app", "ChatGPT.exe");
        var current = Path.Combine(root, "OpenAI.Codex_2.0.0.0_x64__publisher", "app", "ChatGPT.exe");
        foreach (var path in new[] { wrong, current })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
        Assert.Equal(current, InstalledPath.Resolve(old));
        Assert.Equal(current, InstalledPath.Resolve(current));
    }

    [Fact]
    public void LeavesMissingOrdinaryPathsUnchanged()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "missing.exe");
        Assert.Equal(path, InstalledPath.Resolve(path));
    }

    [Fact]
    public void ReadsIconFromPreviousVersionOfARealExecutable()
    {
        using var folder = new TempFolder();
        var root = Path.Combine(folder.Path, "WindowsApps");
        var current = Path.Combine(root, "Example_2.0.0.0_x64__publisher", "app.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"), current);
        var old = Path.Combine(root, "Example_1.0.0.0_x64__publisher", "app.exe");
        var pixels = ShellIcons.Read(old);
        Assert.NotNull(pixels);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] != 0);
    }
}
