using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Astria.Infrastructure;

namespace Astria.Desktop;

public sealed class App : Avalonia.Application
{
    private Services.DesktopController? _coordinator;
    private InstanceChannel? _instance;
    private FileStream? _lease;
    public override void Initialize()=>Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            lifetime.Exit += (_, _) => { _coordinator?.Dispose(); _lease?.Dispose(); _instance?.Dispose(); };
            try
            {
                var args = lifetime.Args ?? []; var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Astria", "Data");
                var settings = false; var background = false; var exit = false;
                for (var i = 0; i < args.Length; i++) switch (args[i])
                    {
                        case "--data-root": if (i + 1 >= args.Length) throw new ArgumentException("--data-root 缺少目录。"); root = Path.GetFullPath(args[++i]); break;
                        case "--settings": settings = true; break;
                        case "--autostart": background = true; break;
                        case "--exit": exit = true; break;
                        default: throw new ArgumentException("未知参数：" + args[i]);
                    }
                root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                if (new[] { settings, background, exit }.Count(x => x) > 1) throw new ArgumentException("启动模式参数不能组合使用。");
                _instance = new(root);
                if (!_instance.Primary)
                {
                    if (!background) _instance.Send(exit ? "exit" : settings ? "settings" : "dock").GetAwaiter().GetResult();
                    Dispatcher.UIThread.Post(() => lifetime.Shutdown()); return;
                }
                if (exit) { Dispatcher.UIThread.Post(() => lifetime.Shutdown()); return; }
                Directory.CreateDirectory(root); _lease = new FileStream(Path.Combine(root, ".session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                _coordinator = new(lifetime, new Services.DesktopSession(root));
                _instance.Command += command => Dispatcher.UIThread.Post(() => { if (command == "exit") lifetime.Shutdown(); else if (command == "settings") _coordinator.ToggleSettings(); else _coordinator.ToggleDock(); });
                _instance.Start();
                if (settings) _coordinator.ShowManager(true); else if (!background) _coordinator.ShowDock();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                var window = new Window { Title = "星屿启动失败", Width = 540, Height = 220, Content = new TextBlock { Text = error.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(28) } };
                window.Closed += (_, _) => lifetime.Shutdown(1); lifetime.MainWindow = window; window.Show();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
