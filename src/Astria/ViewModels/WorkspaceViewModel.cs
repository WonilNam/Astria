using System.Collections.ObjectModel;
using Astria.Core.Contracts;
using Astria.Core.Models;
using Astria.Desktop.Services;

namespace Astria.Desktop.ViewModels;

public enum WorkspaceSection { Library, Pinned, Running, Settings }

public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly DesktopSession _session;
    private readonly Dictionary<string, AppTile> _tiles = new(StringComparer.Ordinal);
    private string _query = "";
    private string _status = "准备就绪 · 所有搜索均在本机完成";
    private string? _folderId;
    private int _page;
    private int _pageCount = 1;
    private bool _busy;
    private WorkspaceSection _section;
    private IReadOnlyDictionary<string, string?> _runningProcesses = new Dictionary<string, string?>();
    private IReadOnlyDictionary<string, string?> _backgroundProcesses = new Dictionary<string, string?>();
    public bool IncludesBackground(string id) => _session.Preferences.Current.BackgroundRunningIds.Contains(id, StringComparer.Ordinal);
    public void ToggleBackgroundRunning(AppTile tile)
    {
        var ids = _session.Preferences.Current.BackgroundRunningIds.ToList();
        if (!ids.Remove(tile.Id)) ids.Add(tile.Id);
        SavePreferences(_session.Preferences.Current with { BackgroundRunningIds = ids });
        RefreshRunning();
        Status = IncludesBackground(tile.Id) ? $"{tile.Name}：后台驻留也显示运行点" : $"{tile.Name}：仅有应用窗口时显示运行点";
    }
    private bool IsEntryRunning(Astria.Core.Models.AppEntry entry) => _session.Services.Running.IsRunning(entry)
        || DesktopRunningState.Matches(entry, _runningProcesses)
        || IncludesBackground(entry.Id) && DesktopRunningState.Matches(entry, _backgroundProcesses);

    public WorkspaceViewModel(DesktopSession session)
    {
        _session = session;
        Refresh();
        var notices = session.LoadReport.Notices.Append(session.Preferences.Notice).Where(text => !string.IsNullOrWhiteSpace(text));
        if (notices.Any()) Status = string.Join(" ", notices);
    }

    public ObservableCollection<AppTile> VisibleApps { get; } = [];
    public ObservableCollection<AppTile> DockApps { get; } = [];
    public string Query { get => _query; set { if (Set(ref _query, value)) { _page = 0; RefreshVisible(); } } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) Notify(nameof(CanInteract)); } }
    public bool CanInteract => !IsBusy;
    public WorkspaceSection Section => _section;
    public bool IsSettings => _section == WorkspaceSection.Settings;
    public bool IsLibrary => !IsSettings;
    public bool IsEmpty => VisibleApps.Count == 0;
    public bool InFolder => _folderId is not null;
    public string? FolderId => _folderId;
    public int AppCount => _session.Entries.Count();
    public int PinnedCount => _session.Data.Layouts.Dock.PinnedItemIds.Count;
    public int RunningCount => _tiles.Values.Count(tile => tile.IsRunning);
    public int Page => _page;
    public int PageCount => _pageCount;
    public string PageLabel => $"{_page + 1:00} / {_pageCount:00}";
    public bool HasPages => _pageCount > 1;
    public bool HasPrevious => _page > 0;
    public bool HasNext => _page + 1 < _pageCount;
    public string Heading => IsSettings ? "偏好设置" : _folderId is not null
        ? _session.Slots.FirstOrDefault(slot => slot.Group?.Id == _folderId)?.Group?.Name ?? "文件夹"
        : _section switch { WorkspaceSection.Pinned => "常用应用", WorkspaceSection.Running => "正在运行", _ => "应用管理" };
    public string Description => IsSettings ? "让外观、交互和性能，恰好适合你的节奏。"
        : _folderId is not null ? "专注这一组。右键应用，可移出文件夹。"
        : _section switch { WorkspaceSection.Pinned => "你最常用的工具，始终停靠在这里。", WorkspaceSection.Running => "切回已有窗口，接着刚才的事情。", _ => "少一点寻找，多一点专注。输入名称、拼音或首字母，即刻出发。" };
    public string ResultLabel => string.IsNullOrWhiteSpace(Query) ? InFolder ? "文件夹内容" : "应用空间" : $"“{Query}”的搜索结果";
    public string EmptyTitle => Query.Length > 0 ? "没有找到匹配的应用" : _section == WorkspaceSection.Pinned ? "把常用工具留在手边" : _section == WorkspaceSection.Running ? "此刻没有运行中的应用" : "从你的第一款应用开始";
    public string EmptyDescription => Query.Length > 0 ? "试试应用名称、中文拼音或首字母。" : "点击添加应用，或将快捷方式、文件和文件夹拖到这里。";
    public string DataDirectory => _session.Data.Paths.RootDirectory;
    public bool UseAcrylic { get => _session.Preferences.Current.UseAcrylic; set => SavePreferences(_session.Preferences.Current with { UseAcrylic = value }); }
    public bool HideDesktopIconsOnStartup
    {
        get => _session.Preferences.Current.HideDesktopIconsOnStartup;
        set
        {
            SavePreferences(_session.Preferences.Current with { HideDesktopIconsOnStartup = value });
            if (HideDesktopIconsOnStartup == value) ApplyDesktopIconPreference();
        }
    }
    public void ApplyDesktopIconPreference()
    {
        if (WriteDesktopIcons is null) return;
        var error = WriteDesktopIcons(!HideDesktopIconsOnStartup);
        RefreshDesktopIcons();
        if (error is not null) Status = "桌面图标设置已保存，但应用失败：" + error;
    }
    public bool AutoHideDock { get => _session.Preferences.Current.AutoHideDock; set => SavePreferences(_session.Preferences.Current with { AutoHideDock = value }); }
    public bool ImmersiveLaunchpad { get => _session.Preferences.Current.ImmersiveLaunchpad; set => SavePreferences(_session.Preferences.Current with { ImmersiveLaunchpad = value }); }
    public bool ReduceMotion { get => _session.Preferences.Current.ReduceMotion; set => SavePreferences(_session.Preferences.Current with { ReduceMotion = value }); }
    public double DockScale { get => _session.Preferences.Current.DockScale; set => SavePreferences(_session.Preferences.Current with { DockScale = value }); }
    public int FavoriteVisibleCount { get => _session.Preferences.Current.FavoriteVisibleCount; set => SavePreferences(_session.Preferences.Current with { FavoriteVisibleCount = value }); }
    public string FavoriteShape => _session.Preferences.Current.FavoriteShape;
    public string FavoriteShapeName => FavoriteShape switch { "galaxy" => "银河旋臂", "matrix" => "悬浮矩阵", _ => "双层轨道" };
    public string FavoriteShapeDescription => FavoriteShape switch
    {
        "galaxy" => "双旋臂紧凑展开 · 缓慢摆动与独立漂浮",
        "matrix" => "居中排列，独立漂浮 · 适合较多常用应用",
        _ => "内外错位分层 · 反向公转 · 适合少量精选应用"
    };
    public void SetFavoriteShape(string shape) => SavePreferences(_session.Preferences.Current with { FavoriteShape = shape, CardLocations = [] });
    public string FavoriteLayoutLabel => $"最多 {FavoriteVisibleCount} 张 · {(FavoriteShape == "auto" ? ConstellationLayout.Name(FavoriteVisibleCount) : FavoriteShapeName)} · 共 {PinnedCount} 个常用";
    public string Accent => _session.Preferences.Current.Accent;
    public string ThemeName => Accent switch { "aurora" => "极光", "neon" => "赛博霓虹", "abyss" => "深海", "gold" => "暖金", "ocean" => "晴空蓝", "mint" => "薄荷绿", _ => "薰衣草紫" };
    public string HotkeyLabel => _session.Preferences.Current.Hotkey.Label;
    public string SettingsHotkeyLabel => _session.Preferences.Current.SettingsHotkey.Label;
    private bool? _desktopIconsVisible;
    public Func<bool?>? ReadDesktopIcons { get; set; }
    public Func<bool, string?>? WriteDesktopIcons { get; set; }
    public bool CanToggleDesktopIcons => _desktopIconsVisible.HasValue;
    public string DesktopIconsLabel => _desktopIconsVisible == false ? "显示桌面图标" : "隐藏桌面图标";
    public void RefreshDesktopIcons()
    {
        _desktopIconsVisible = ReadDesktopIcons?.Invoke();
        Notify(nameof(CanToggleDesktopIcons)); Notify(nameof(DesktopIconsLabel));
    }
    public void ToggleDesktopIcons()
    {
        RefreshDesktopIcons();
        if (_desktopIconsVisible is not bool visible || WriteDesktopIcons is null)
        { Status = "暂时无法读取 Windows 桌面图标状态。"; return; }
        var error = WriteDesktopIcons(!visible);
        RefreshDesktopIcons();
        Status = error is not null ? "切换桌面图标失败：" + error
            : _desktopIconsVisible == !visible ? (visible ? "桌面图标已隐藏" : "桌面图标已显示")
            : "桌面图标状态尚未更新，请重试。";
    }
    public Func<LaunchHotkey, string?>? RegisterSettingsHotkey { get; set; }
    public bool SetSettingsHotkey(LaunchHotkey hotkey)
    {
        if (!hotkey.IsValid || hotkey == _session.Preferences.Current.Hotkey) { Status = "设置快捷键须有效且与 Dock 快捷键不同。"; return false; }
        var previous = _session.Preferences.Current.SettingsHotkey;
        if (RegisterSettingsHotkey?.Invoke(hotkey) is { } registrationError) { Status = registrationError; return false; }
        try { _session.Preferences.Save(_session.Preferences.Current with { SettingsHotkey = hotkey }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Astria.Core.Storage.StorageException)
        { RegisterSettingsHotkey?.Invoke(previous); Status = "设置快捷键未能保存：" + error.Message; return false; }
        Notify(nameof(SettingsHotkeyLabel)); Status = "设置快捷键已更新：" + SettingsHotkeyLabel;
        return true;
    }
    public Func<LaunchHotkey, string?>? RegisterHotkey { get; set; }

    public bool SetHotkey(LaunchHotkey hotkey)
    {
        if (!hotkey.IsValid) { Status = "请使用修饰键加字母、数字、F1–F24、空格、Enter 或 Tab。"; return false; }
        if (hotkey == _session.Preferences.Current.SettingsHotkey) { Status = "Dock 快捷键不能与设置快捷键相同。"; return false; }
        var previous = _session.Preferences.Current.Hotkey;
        if (RegisterHotkey?.Invoke(hotkey) is { } registrationError) { Status = registrationError; return false; }
        try { _session.Preferences.Save(_session.Preferences.Current with { Hotkey = hotkey }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Astria.Core.Storage.StorageException)
        { RegisterHotkey?.Invoke(previous); Status = "快捷键未能保存：" + error.Message; return false; }
        Notify(nameof(HotkeyLabel)); Status = "唤出快捷键已更新：" + HotkeyLabel;
        return true;
    }
    public bool StartWithWindows
    {
        get
        {
            try { return EditionStartup.IsEnabled(DataDirectory); }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { return false; }
        }
        set
        {
            try { EditionStartup.SetEnabled(DataDirectory, value); Status = value ? "已启用当前用户登录启动" : "已关闭当前用户登录启动"; }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { Status = error.Message; }
            Notify();
        }
    }
    public event EventHandler? PreferencesChanged;
    public event EventHandler? TilesChanged;

    public void Navigate(WorkspaceSection section)
    {
        _section = section;
        _folderId = null;
        _page = 0;
        _query = "";
        Notify(nameof(Query));
        Notify(nameof(Section));
        Notify(nameof(IsSettings));
        Notify(nameof(IsLibrary));
        RefreshVisible();
    }

    public void OpenFolder(string id)
    {
        _folderId = id;
        _page = 0;
        Query = "";
        RefreshVisible();
    }

    public void LeaveFolder() { _folderId = null; _page = 0; RefreshVisible(); }
    public void NextPage() { if (HasNext) { _page++; RefreshVisible(); } }
    public void PreviousPage() { if (HasPrevious) { _page--; RefreshVisible(); } }

    public void Refresh()
    {
        foreach (var entry in _session.Entries)
        {
            if (!_tiles.TryGetValue(entry.Id, out var tile) || !ReferenceEquals(tile.Entry, entry))
                _tiles[entry.Id] = tile = new AppTile { Id = entry.Id, Name = entry.DisplayName, Entry = entry };
            tile.IsPinned = _session.IsPinned(entry.Id);
            tile.IsRunning = IsEntryRunning(entry);
        }
        foreach (var stale in _tiles.Keys.Where(id => _session.Services.Library.Find(id) is null).ToArray()) _tiles.Remove(stale);
        var orderedIds = _session.Slots.SelectMany(slot => slot.Group is { } group ? (IEnumerable<string>)group.ItemIds : new[] { slot.ItemId ?? "" }).ToList();
        Replace(DockApps, orderedIds.Where(_session.IsPinned).Concat(orderedIds).Concat(_session.Entries.Select(entry => entry.Id))
            .Distinct(StringComparer.Ordinal).Select(id => _tiles.GetValueOrDefault(id)).OfType<AppTile>());
        Notify(nameof(AppCount));
        Notify(nameof(PinnedCount));
        Notify(nameof(FavoriteLayoutLabel));
        RefreshVisible();
    }

    public void RefreshRunning(IReadOnlyDictionary<string, string?>? processes = null, IReadOnlyDictionary<string, string?>? background = null)
    {
        if (processes is not null) _runningProcesses = processes;
        if (background is not null) _backgroundProcesses = background;
        foreach (var tile in _tiles.Values) tile.IsRunning = tile.Entry is not null && IsEntryRunning(tile.Entry);
        Notify(nameof(RunningCount));
        if (_section == WorkspaceSection.Running) RefreshVisible();
    }

    public void TogglePin(AppTile tile)
    {
        _session.SetPinned(tile.Id, !tile.IsPinned);
        Status = tile.IsPinned ? $"已取消常用：{tile.Name}" : $"已设为常用：{tile.Name}";
        Refresh();
    }

    public void SetAccent(string accent) => SavePreferences(_session.Preferences.Current with { Accent = accent });
    public void ApplyPreferences(DesktopPreferences value) => SavePreferences(value);

    private void SavePreferences(DesktopPreferences value)
    {
        try
        {
            _session.Preferences.Save(value);
            Status = "设置已保存，即时生效";
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Astria.Core.Storage.StorageException)
        { Status = "设置未能保存：" + error.Message; }
        Notify(nameof(UseAcrylic)); Notify(nameof(HideDesktopIconsOnStartup)); Notify(nameof(AutoHideDock)); Notify(nameof(ImmersiveLaunchpad)); Notify(nameof(ReduceMotion)); Notify(nameof(DockScale)); Notify(nameof(Accent)); Notify(nameof(ThemeName)); Notify(nameof(HotkeyLabel)); Notify(nameof(SettingsHotkeyLabel)); Notify(nameof(FavoriteVisibleCount)); Notify(nameof(FavoriteLayoutLabel)); Notify(nameof(FavoriteShape)); Notify(nameof(FavoriteShapeName)); Notify(nameof(FavoriteShapeDescription));
    }

    private void RefreshVisible()
    {
        if (_folderId is not null && !_session.Slots.Any(slot => slot.Group?.Id == _folderId)) _folderId = null;
        IEnumerable<AppTile> source;
        if (!string.IsNullOrWhiteSpace(Query))
        {
            source = _session.Search(Query).Select(entry => _tiles.GetValueOrDefault(entry.Id)).OfType<AppTile>();
            if (_folderId is not null)
            {
                var members = _session.Slots.First(slot => slot.Group?.Id == _folderId).Group!.ItemIds;
                source = source.Where(tile => members.Contains(tile.Id));
            }
        }
        else if (_folderId is not null)
            source = _session.Slots.First(slot => slot.Group?.Id == _folderId).Group!.ItemIds.Select(id => _tiles.GetValueOrDefault(id)).OfType<AppTile>();
        else if (_section is WorkspaceSection.Pinned or WorkspaceSection.Running)
            source = _session.Slots.SelectMany(slot => slot.Group is { } group ? (IEnumerable<string>)group.ItemIds : new[] { slot.ItemId ?? "" })
                .Concat(_tiles.Keys).Distinct(StringComparer.Ordinal).Select(id => _tiles.GetValueOrDefault(id)).OfType<AppTile>();
        else source = _session.Slots.Select(slot => slot.Kind == LauncherSlotKind.Group && slot.Group is { } group
            ? new AppTile { Id = group.Id, Name = group.Name, IsFolder = true, MemberCount = group.ItemIds.Count }
            : _tiles.GetValueOrDefault(slot.ItemId ?? "")).OfType<AppTile>();
        if (_section == WorkspaceSection.Pinned) source = source.Where(tile => tile.IsPinned);
        if (_section == WorkspaceSection.Running) source = source.Where(tile => tile.IsRunning);
        var all = source.ToList();
        _pageCount = Math.Max(1, (all.Count + DesktopSession.PageCapacity - 1) / DesktopSession.PageCapacity);
        _page = Math.Clamp(_page, 0, _pageCount - 1);
        Replace(VisibleApps, all.Skip(_page * DesktopSession.PageCapacity).Take(DesktopSession.PageCapacity));
        foreach (var property in new[] { nameof(Heading), nameof(Description), nameof(ResultLabel), nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyDescription), nameof(InFolder), nameof(Page), nameof(PageCount), nameof(PageLabel), nameof(HasPages), nameof(HasNext), nameof(HasPrevious) }) Notify(property);
        TilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        var materialized = items.ToList();
        if (collection.SequenceEqual(materialized)) return;
        collection.Clear();
        foreach (var item in materialized) collection.Add(item);
    }
}
