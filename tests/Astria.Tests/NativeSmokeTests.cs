using System.Diagnostics;
using System.Reflection;
using Astria.Domain;
using Astria.Infrastructure;
using Xunit;
namespace Astria.Tests;

public sealed class NativeSmokeTests
{
    [Fact]
    public async Task NativeAppStartsHiddenAndAcceptsTheExitCommandWithoutChangingDesktopState()
    {
        using var folder = new TempFolder(); using var icons = new DesktopIcons(); var before = icons.Visible;
        new CatalogStorage(folder.Path).Save(new(1, [], new() { HideDesktopIconsOnStartup = before == false, DockKey = new(7, 131), SettingsKey = new(7, 132) }));
        var repository = new DirectoryInfo(AppContext.BaseDirectory); while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Astria.slnx"))) repository = repository.Parent;
        var configuration = typeof(Astria.Desktop.App).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var executable = Path.Combine(repository!.FullName, "src", "Astria", "bin", configuration, "net10.0-windows10.0.26100.0", "Astria.exe");
        executable = Environment.GetEnvironmentVariable("ASTRIA_SMOKE_EXE") ?? executable;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "--autostart", "--data-root", folder.Path }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                if (Mutex.TryOpenExisting("Local\\" + InstanceChannel.Identity(folder.Path), out var existing)) { existing.Dispose(); break; }
                await Task.Delay(100);
            }
            using var channel = new InstanceChannel(folder.Path);
            if (channel.Primary)
            {
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
                Assert.Fail("Native application did not acquire the instance lock: " + await process.StandardError.ReadToEndAsync());
            }
            await channel.Send("exit");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(before, icons.Visible);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
}
