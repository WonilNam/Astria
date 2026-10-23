using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Astria.Core.Models;
using Astria.Platform.Shell;

namespace Astria.Desktop.Views;

internal static class Dialogs
{
    private static Window Create(Window owner, string title, double width = 480)
    {
        var dialog = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = 650,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None, Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Icon = owner.Icon,
            ShowInTaskbar = false
        };
        dialog.Opened += (_, _) =>
        {
            var body = dialog.Content;
            dialog.Content = null;
            var close = new Button { Content = "×", Classes = { "window-control", "close" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 8, 0) };
            close.Click += (_, _) => dialog.Close();
            var header = new Border { Height = 38, Background = Brushes.Transparent, Child = close };
            header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed && !close.IsPointerOver) dialog.BeginMoveDrag(e); };
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(header);
            var content = new ContentControl { Content = body }; Grid.SetRow(content, 1); grid.Children.Add(content);
            dialog.Content = new Border { CornerRadius = new CornerRadius(18), ClipToBounds = true, BorderBrush = Brush.Parse("#40586683"), BorderThickness = new Thickness(1), Background = Brush.Parse("#192033"), Child = grid };
        };
        dialog.KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Escape) dialog.Close(); };
        return dialog;
    }
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var dialog = Create(owner, title);
        var yes = new Button { Content = "确认", Classes = { "primary" } };
        var no = new Button { Content = "取消", Classes = { "outline" } };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(26), Spacing = 22,
            Children = { new TextBlock { Text = title, FontSize = 21 }, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { no, yes } } }
        };
        return await dialog.ShowDialog<bool>(owner);
    }

    public static async Task<string?> TextAsync(Window owner, string title, string initial)
    {
        var dialog = Create(owner, title);
        var input = new TextBox { Text = initial, MaxLength = 40, Watermark = "输入名称" };
        var save = new Button { Content = "保存", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.Close(input.Text.Trim()); };
        dialog.Content = new StackPanel { Margin = new Thickness(26), Spacing = 20, Children = { new TextBlock { Text = title, FontSize = 21 }, input, save } };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await dialog.ShowDialog<string?>(owner);
    }

    public static async Task<AppEntry?> PickPartnerAsync(Window owner, IEnumerable<AppEntry> entries)
    {
        var dialog = Create(owner, "选择另一个应用");
        var choices = entries.ToList();
        var search = new TextBox { Watermark = "搜索应用名称…" };
        var list = new ListBox { Classes = { "partner-list" }, ItemsSource = choices, MaxHeight = 300,
            ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<AppEntry>((entry, _) => new TextBlock { Text = entry?.DisplayName, Margin = new Thickness(10, 7), FontSize = 13 }) };
        var button = new Button { Content = "组成文件夹", Classes = { "primary" }, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right };
        list.SelectionChanged += (_, _) => button.IsEnabled = list.SelectedItem is AppEntry;
        search.TextChanged += (_, _) => list.ItemsSource = choices.Where(entry => entry.DisplayName.Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToList();
        button.Click += (_, _) => { if (list.SelectedItem is AppEntry entry) dialog.Close(entry); };
        dialog.Content = new StackPanel { Margin = new Thickness(26), Spacing = 18, Children = { new TextBlock { Text = "选择另一个应用", FontSize = 19 }, search, list, button } };
        return await dialog.ShowDialog<AppEntry?>(owner);
    }

    public static async Task<IReadOnlyList<StartMenuShortcut>> PickImportsAsync(Window owner, IReadOnlyList<StartMenuShortcut> shortcuts)
    {
        var dialog = Create(owner, "添加已安装应用", 600);
        var selected = new HashSet<StartMenuShortcut>();
        var search = new TextBox { Watermark = "筛选名称…" };
        var list = new StackPanel { Spacing = 3 };
        var count = new TextBlock { Text = $"发现 {shortcuts.Count} 项 · 选择要添加的应用", Classes = { "muted" } };
        var import = new Button { Content = "添加所选应用", Classes = { "primary" }, IsEnabled = false };
        var all = new Button { Content = "全选当前结果", Classes = { "outline" } };
        var clear = new Button { Content = "清空选择", Classes = { "quiet" } };
        IEnumerable<StartMenuShortcut> Filter() => shortcuts.Where(item => item.Name.Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase));
        void UpdateCount() { count.Text = $"发现 {shortcuts.Count} 项 · 已选择 {selected.Count} 项"; import.IsEnabled = selected.Count > 0; }
        void Populate()
        {
            list.Children.Clear();
            foreach (var item in Filter())
            {
                var check = new CheckBox { Content = item.Name, IsChecked = selected.Contains(item), Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Stretch };
                ToolTip.SetTip(check, item.Group);
                check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) selected.Add(item); else selected.Remove(item); UpdateCount(); };
                list.Children.Add(check);
            }
            UpdateCount();
        }
        search.TextChanged += (_, _) => Populate();
        all.Click += (_, _) => { foreach (var item in Filter()) selected.Add(item); Populate(); };
        clear.Click += (_, _) => { selected.Clear(); Populate(); };
        import.Click += (_, _) => dialog.Close(shortcuts.Where(selected.Contains).ToList());
        Populate();
        dialog.Content = new StackPanel { Margin = new Thickness(25), Spacing = 16, Children = {
            new TextBlock { Text = "把熟悉的工具，带到这里", FontSize = 22 }, search, count,
            new ScrollViewer { Content = list, Height = 320 },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Children = { all, clear, import } } } };
        return await dialog.ShowDialog<IReadOnlyList<StartMenuShortcut>?>(owner) ?? [];
    }
}
