using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Astria.AppServices;
using Astria.Core.Contracts;
using Astria.Core.Models;
using Astria.Core.Storage;
using Astria.Desktop.Services;
using Astria.Desktop.ViewModels;
using Astria.Platform.Shell;

namespace Astria.Desktop.Views;

public partial class WorkspaceWindow : Window
{
    private readonly DesktopSession _session;
    private readonly WorkspaceViewModel _model;

    private readonly Action<AppTile, bool> _activate;
    private bool _allowClose;
    private bool _recordingHotkey;
    private bool _recordingSettingsHotkey;

    public WorkspaceWindow(DesktopSession session, WorkspaceViewModel model, Action<AppTile, bool> activate)
    {
        _session = session;
        _model = model;
        _activate = activate;
        InitializeComponent();
        DataContext = model;
        Closing += (_, args) => { if (!_allowClose) { args.Cancel = true; Hide(); } };
        KeyDown += OnWindowKeyDown;
        AddHandler(KeyDownEvent, OnHotkeyKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTypeToSearch, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTilePointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTilePointerReleased, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => CancelTileDrag();
        Deactivated += (_, _) => CancelTileDrag();
        PointerWheelChanged += (_, _) => { if (_dragSource is not null) CancelTileDrag(); };
        HotkeyButton.LostFocus += (_, _) => { _recordingHotkey = false; HotkeyButton.Content = "修改快捷键"; };
        SettingsHotkeyButton.LostFocus += (_, _) =>
        { if (_recordingSettingsHotkey) { _recordingHotkey = false; _recordingSettingsHotkey = false; SettingsHotkeyButton.Content = "修改设置快捷键"; } };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnExternalDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnExternalDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnExternalDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => FileDropOverlay.IsVisible = false, RoutingStrategies.Bubble, handledEventsToo: true);
        Opened += (_, _) => { SearchBox.Focus(); FitToScreen(); };
        model.PreferencesChanged += OnThemeChanged;
        Closed += (_, _) => model.PreferencesChanged -= OnThemeChanged;
        ApplyTheme();
        ScalingChanged += (_, _) => FitToScreen();
        Screens.Changed += OnScreensChanged;
        Closed += (_, _) => Screens.Changed -= OnScreensChanged;
    }

    public void Shutdown() { _allowClose = true; Close(); }
    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();
    public void ApplyTheme()
    {
        var palette = DesktopPalette.For(_model.Accent);
        WindowFrame.Background = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new(Avalonia.Media.Color.Parse("#F4" + palette.Surface[1..]), 0),
                new(Avalonia.Media.Color.Parse("#F40F1421"), .62), new(Avalonia.Media.Color.Parse("#F0" + palette.Surface[1..]), 1) }
        };
        WindowFrame.BorderBrush = Avalonia.Media.Brush.Parse("#40" + palette.Accent[1..]);
    }

    public void ShowSection(WorkspaceSection section)
    {
        _model.Navigate(section);
        Show(); WindowState = WindowState.Normal; Activate();
        if (section != WorkspaceSection.Settings) SearchBox.Focus();
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var bounds = WorkspacePlacement.Fit(screen.WorkingArea, screen.Scaling, Width, Height, Position);
        MinWidth = Math.Min(950, bounds.Width);
        MinHeight = Math.Min(640, bounds.Height);
        Width = bounds.Width; Height = bounds.Height; Position = bounds.Position;
    }

    private void OnScreensChanged(object? sender, EventArgs e) => FitToScreen();

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); }
    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnHide(object? sender, RoutedEventArgs e) => Hide();
    private void OnLibrary(object? sender, RoutedEventArgs e) => _model.Navigate(WorkspaceSection.Library);
    private void OnBack(object? sender, RoutedEventArgs e) => _model.LeaveFolder();
    private void OnPrevious(object? sender, RoutedEventArgs e) => _model.PreviousPage();
    private void OnNext(object? sender, RoutedEventArgs e) => _model.NextPage();
    private void OnAccent(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string accent }) _model.SetAccent(accent); }
    private void OnShape(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string shape }) _model.SetFavoriteShape(shape); }
    private void OnOpenData(object? sender, RoutedEventArgs e) => ShellExecutor.Open(_model.DataDirectory);

    private void OnAddApps(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor)
            ApplicationPicker.Open(anchor, folders => RunAsync(async () => await ImportAsync(await ApplicationPicker.SelectAsync(StorageProvider, folders))),
                target => RunAsync(() => ImportAsync([target])));
    }

    private void OnRecordHotkey(object? sender, RoutedEventArgs e)
    {
        _recordingSettingsHotkey = false;
        _recordingHotkey = true;
        HotkeyButton.Content = "请按组合键，Esc 取消";
        HotkeyButton.Focus();
    }

    private void OnResetHotkey(object? sender, RoutedEventArgs e)
    { _recordingHotkey = false; HotkeyButton.Content = "修改快捷键"; _model.SetHotkey(new()); }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_recordingHotkey) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { _recordingHotkey = false; HotkeyButton.Content = "修改快捷键"; SettingsHotkeyButton.Content = "修改设置快捷键"; return; }
        if (LaunchHotkey.Capture(e.Key, e.KeyModifiers) is not { } hotkey) return;
        if (_recordingSettingsHotkey ? _model.SetSettingsHotkey(hotkey) : _model.SetHotkey(hotkey))
        { _recordingHotkey = false; HotkeyButton.Content = "修改快捷键"; SettingsHotkeyButton.Content = "修改设置快捷键"; }
    }
    private void OnRecordSettingsHotkey(object? sender, RoutedEventArgs e)
    {
        SettingsHotkeyButton.Focus();
        _recordingSettingsHotkey = true; _recordingHotkey = true;
        SettingsHotkeyButton.Content = "请按组合键，Esc 取消";
    }
    private void OnResetSettingsHotkey(object? sender, RoutedEventArgs e)
    { _recordingHotkey = false; SettingsHotkeyButton.Content = "修改设置快捷键"; _model.SetSettingsHotkey(new(3, 83)); }

    private async void OnScanApps(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            _model.Status = "正在扫描开始菜单…";
            var scan = await Task.Run(StartMenuScanner.Scan);
            var selected = await Dialogs.PickImportsAsync(this, scan.Shortcuts);
            await ImportRequestsAsync(selected.Select(item => new ImportRequest { Source = item.Path, DisplayName = item.Name }).ToList());
        });
    }

    private async void OnClearLibrary(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            if (_model.AppCount == 0) { _model.Status = "应用库已经为空"; return; }
            if (!await Dialogs.ConfirmAsync(this, "清空应用库", $"移除应用库中的全部 {_model.AppCount} 个应用及分组、常用和卡片位置？\n原文件、文件夹和已安装程序会保留。清空前自动备份，可在设置中恢复。")) return;
            CancelTileDrag();
            var backup = _session.ClearLibrary();
            _model.Navigate(WorkspaceSection.Library); _model.Refresh();
            _model.Status = "应用库已清空。恢复备份：" + backup;
        });
    }

    public Task ImportAsync(IEnumerable<string> paths) => ImportRequestsAsync(paths.Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => new ImportRequest { Source = path }).ToList());

    private async Task ImportRequestsAsync(List<ImportRequest> requests)
    {
        if (requests.Count == 0) { _model.Status = "未添加应用"; return; }
        var added = new List<AppEntry>();
        var failures = new List<ImportFailure>();
        var duplicates = 0;
        foreach (var batch in requests.Chunk(8))
        {
            var result = _session.Import(batch);
            added.AddRange(result.Added); failures.AddRange(result.Failures); duplicates += result.Duplicates.Count;
            _model.Status = $"正在添加：{Math.Min(added.Count + duplicates + failures.Count, requests.Count)} / {requests.Count}";
            await Task.Delay(1);
        }
        _model.Refresh();
        _model.Status = $"已添加 {added.Count} 项 · 已存在 {duplicates} 项 · 失败 {failures.Count} 项";
        if (failures.Count > 0)
            await Dialogs.ConfirmAsync(this, "部分项目未能添加", string.Join('\n', failures.Take(8).Select(failure => $"{Path.GetFileName(failure.Source)}：{failure.Message}")));
    }

    private async void OnBackup(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择备份保存位置" });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } parent) return;
            var target = Path.Combine(parent, "Astria-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            var result = new DesktopTransferService(_session).BackupTo(target);
            if (!result.Succeeded) throw new IOException(result.Error);
            _model.Status = "备份已保存到 " + target;
        });
    }

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择 Astria 备份目录" });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } source) return;
            if (!await Dialogs.ConfirmAsync(this, "恢复备份", "当前应用库和布局将替换为备份内容。恢复前会保存安全副本。继续恢复？")) return;
            var result = new DesktopTransferService(_session).RestoreFrom(source);
            if (!result.Succeeded) throw new IOException(result.Error);
            _model.ApplyPreferences(_session.Preferences.Current);
            _model.Refresh(); _model.RefreshRunning();
            _model.Status = "备份已恢复。安全副本：" + result.SafetyCopyDirectory;
        });
    }

    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (_dragSource is not null || sender is not Button { DataContext: AppTile tile }) return;
        if (tile.IsFolder) _model.OpenFolder(tile.Id); else _activate(tile, false);
    }

    private void OnExternalDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        e.DragEffects = _model.IsBusy ? DragDropEffects.None : DragDropEffects.Copy;
        FileDropOverlay.IsVisible = !_model.IsBusy;
        e.Handled = true;
    }
    private async void OnExternalDrop(object? sender, DragEventArgs e)
    {
        FileDropOverlay.IsVisible = false;
        if (_model.IsBusy || e.DataTransfer.TryGetFiles() is not { } files) return;
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) { _model.Status = "无法读取拖入项目的本地路径，请使用添加应用。"; return; }
        e.Handled = true;
        e.DragEffects = DragDropEffects.Copy;
        CancelTileDrag();
        await RunAsync(async () => { _model.Navigate(WorkspaceSection.Library); await ImportAsync(paths); });
    }

    private void ShowTileMenu(Button button, AppTile tile)
    {
        var menu = new ContextMenu();
        void Add(string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await RunAsync(action);
            menu.Items.Add(item);
        }
        if (tile.IsFolder)
        {
            Add("重命名文件夹", async () => { var name = await Dialogs.TextAsync(this, "重命名文件夹", tile.Name); if (name is not null) { _session.RenameFolder(tile.Id, name); _model.Refresh(); } });
            Add("解散文件夹（保留应用）", () => { _session.DissolveFolder(tile.Id); _model.Refresh(); return Task.CompletedTask; });
        }
        else
        {
            Add(tile.IsPinned ? "取消常用" : "设为常用", () => { _model.TogglePin(tile); return Task.CompletedTask; });
            if (tile.Entry?.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                Add(_model.IncludesBackground(tile.Id) ? "✓ 后台驻留也标记运行（点击关闭）" : "后台驻留也标记运行", () => { _model.ToggleBackgroundRunning(tile); return Task.CompletedTask; });
            Add("重命名应用…", async () =>
            {
                var name = await Dialogs.TextAsync(this, "重命名应用", tile.Name);
                if (name is not null && tile.Entry is { } entry)
                {
                    if (!_session.Services.Editor.Edit(tile.Id, name, entry.SearchAliases, null)) throw new InvalidOperationException("应用名称未能保存。");
                    _model.Refresh();
                }
            });
            Add("更换图标…", async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择自定义图标", FileTypeFilter = [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.ico", "*.bmp"] }] });
                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && tile.Entry is { } entry)
                {
                    if (!_session.Services.Editor.Edit(tile.Id, entry.DisplayName, entry.SearchAliases, path)) throw new InvalidOperationException("图标格式不支持或无法读取。");
                    _model.Refresh();
                }
            });
            Add("打开新实例", () => { var result = _session.Services.Launch.LaunchItem(tile.Id); _model.Status = result.Succeeded ? "已打开 " + tile.Name : result.Message ?? "打开失败"; return Task.CompletedTask; });
            Add("从应用库移除", async () =>
            {
                if (!await Dialogs.ConfirmAsync(this, "移除卡片", $"从应用库移除「{tile.Name}」？原文件和程序会保留。")) return;
                if (!_session.Data.DeleteFromLibrary(tile.Id)) throw new InvalidOperationException("移除未能完成。");
                _model.Refresh();
            });
            if (_model.InFolder)
                Add("移出文件夹", () => { _session.RemoveFromFolder(tile.Id, _model.FolderId!); _model.Refresh(); return Task.CompletedTask; });
            else if (_session.Slots.Any(slot => slot.ItemId == tile.Id))
                Add("与其他应用组成文件夹…", async () =>
                {
                    var topIds = _session.Slots.Where(slot => slot.Kind == LauncherSlotKind.App && slot.ItemId != tile.Id).Select(slot => slot.ItemId).ToHashSet();
                    var partner = await Dialogs.PickPartnerAsync(this, _session.Entries.Where(entry => topIds.Contains(entry.Id)));
                    if (partner is null) return;
                    var name = await Dialogs.TextAsync(this, "新建文件夹", "我的工具");
                    if (name is not null) { _session.CreateFolder(tile.Id, partner.Id, name); _model.Refresh(); }
                });
            foreach (var folder in _session.Slots.Where(slot => slot.Group is not null).Select(slot => slot.Group!))
                Add("移入「" + folder.Name + "」", () => { _session.AddToFolder(tile.Id, folder.Id); _model.Refresh(); return Task.CompletedTask; });
        }
        button.ContextMenu = menu;
        menu.Open(button);
    }

    public async Task RunAsync(Func<Task> action)
    {
        if (_model.IsBusy) return;
        _model.IsBusy = true;
        try { await action(); }
        catch (Exception error) when (error is StorageException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { _model.Status = "操作未完成：" + error.Message; _model.Refresh(); }
        finally { _model.IsBusy = false; }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragSource is not null) { CancelTileDrag(); _model.Status = "已取消排序"; e.Handled = true; return; }
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        { if (_model.IsSettings) _model.Navigate(WorkspaceSection.Library); SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape) { if (_model.Query.Length > 0) _model.Query = ""; else if (_model.InFolder) _model.LeaveFolder(); else Hide(); e.Handled = true; }
    }
    private void OnTypeToSearch(object? sender, TextInputEventArgs e)
    {
        if (_model.IsSettings || _recordingHotkey || _dragSource is not null || string.IsNullOrWhiteSpace(e.Text)) return;
        var source = e.Source as Visual;
        if (source is TextBox || source?.GetVisualAncestors().OfType<TextBox>().Any() == true) return;
        SearchBox.Focus();
        SearchBox.Text = (_model.Query ?? "") + e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        _model.Query = SearchBox.Text;
        e.Handled = true;
    }
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _model.VisibleApps.FirstOrDefault() is { } tile)
        { if (tile.IsFolder) _model.OpenFolder(tile.Id); else _activate(tile, false); e.Handled = true; }
        else if (e.Key == Key.Down && _model.VisibleApps.Count > 0) { AppList.SelectedIndex = 0; AppList.Focus(); e.Handled = true; }
    }
    private void OnAppsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AppList.SelectedItem is AppTile tile)
        { if (tile.IsFolder) _model.OpenFolder(tile.Id); else _activate(tile, false); e.Handled = true; }
        else if (e.Key == Key.PageDown) { _model.NextPage(); e.Handled = true; }
        else if (e.Key == Key.PageUp) { _model.PreviousPage(); e.Handled = true; }
    }
}
