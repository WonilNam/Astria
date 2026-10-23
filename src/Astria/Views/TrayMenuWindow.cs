using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Astria.Desktop.Views;

internal sealed class TrayMenuWindow : Window
{
    public TrayMenuWindow(Action show, Action manage, Action settings, Action exit)
    {
        Width = 220; SizeToContent = SizeToContent.Height;
        SystemDecorations = SystemDecorations.None; CanResize = false; ShowInTaskbar = false; Topmost = true;
        Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var items = new StackPanel { Spacing = 3 };
        items.Children.Add(new TextBlock { Text = "ASTRIA", FontSize = 10, LetterSpacing = 2, Foreground = Brush.Parse("#94A2C3"), Margin = new Thickness(12, 9, 12, 9) });
        void Add(string text, Action action)
        {
            var button = new Button { Content = text, Classes = { "tray-action" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => { Close(); action(); };
            items.Children.Add(button);
        }
        Add("打开应用空间", show); Add("管理应用", manage); Add("偏好设置", settings);
        items.Children.Add(new Border { Height = 1, Background = Brush.Parse("#254F607F"), Margin = new Thickness(10, 6) });
        Add("退出星屿", exit);
        Content = new Border { Padding = new Thickness(7), CornerRadius = new CornerRadius(14), Background = Brush.Parse("#FA1B2234"), BorderBrush = Brush.Parse("#40586683"), BorderThickness = new Thickness(1), Child = items };
        Deactivated += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Escape) Close(); };
    }

    public void OpenAtPointer()
    {
        GetCursorPos(out var point);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(point.X, point.Y);
        Opacity = 0;
        Opened += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            var screen = Screens.ScreenFromPoint(new PixelPoint(point.X, point.Y)) ?? Screens.Primary;
            if (screen is not null)
            {
                var x = point.X - (int)(Bounds.Width * screen.Scaling) + 12;
                var y = point.Y - (int)(Bounds.Height * screen.Scaling) - 8;
                Position = Astria.Desktop.Services.WorkspacePlacement.Fit(screen.WorkingArea, screen.Scaling, Width, Bounds.Height, new PixelPoint(x, y)).Position;
            }
            Opacity = 1; Activate();
        }, global::Avalonia.Threading.DispatcherPriority.Loaded);
        Show();
    }

    public static nint LoadIcon() => LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Astria.ico"), 1, 32, 32, 0x10);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
}
