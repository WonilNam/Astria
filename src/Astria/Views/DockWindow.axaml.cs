using System.Collections.Specialized;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using Astria.AppServices;
using Astria.Core.Contracts;
using Astria.Core.Models;
using Astria.Desktop.Services;
using Astria.Desktop.ViewModels;

namespace Astria.Desktop.Views;

public partial class DockWindow : Window
{
    private readonly WorkspaceViewModel _model;
    private readonly DesktopSession _session;
    private readonly Action<WorkspaceSection> _show;
    private readonly Action<AppTile, bool> _activate;
    private readonly List<Button> _cards = [];
    private string _searchQuery = "";
    private bool _rebuildPending;
    private readonly DispatcherTimer _barHideTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _barVisible = true;
    private bool _barActive;
    private string? _appliedTheme;
    private IBrush _cardAccent = FocusedBorderBrush;
    private bool _searchActive;
    private int _ringStart;
    private int _outerStart;
    private int _dockPage;
    private (double Radius, double Scale, int Count, string Shape, int Pinned, Size Bounds) _capacityKey;
    private int _capacityResult;
    private double _shapeRadius;
    private double _favoriteFit = 1;
    private double _ordinaryRadius;
    private double _ordinaryFit = 1;
    private (Size Bounds, double Scale, double Zoom, int Count, string Shape)? _ordinaryLayoutKey;
    private readonly List<Point> _satelliteSlots = [];
    private static bool IsSidebarCard(Control card) => card.DataContext is AppTile
    {
        IsFolder: true
    } or AppTile
    {
        Entry.Kind: AppItemKind.ShellLocation or AppItemKind.Folder
    };
    public int VisibleCardCapacity => _cards.Count(IsSidebarCard)
        + Math.Min(_cards.Count(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: true }), VisibleRingCapacity)
        + Math.Min(_cards.Count(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: false }), _satelliteSlots.Count);
    private const int SearchPageSize = 4;
    private int _selected = -1;
    private bool _allowClose;
    private readonly OrbitMotion _orbit = new();
    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Point _pressPoint, _lastPoint;
    private bool _pressed, _dragged;
    private bool _rotating;
    private bool _hovering;
    private readonly StarRingScene _scene;
    private readonly long _floatEpoch = Stopwatch.GetTimestamp();
    private PixelPoint _windowOrigin, _screenPress;
    private Point _cardOrigin;
    private readonly Dictionary<string, CardLocation> _locations;
    public const int CardsPerPage = 8;
    private AppTile? _pressedTile;
    private AppTile? _dropTargetTile;
    private bool _dragHadInitialLocation;
    private CardLocation? _dragInitialLocation;
    private long _lastInput, _lastTick;
    private static readonly IBrush NormalBorderBrush = Brush.Parse("#65FFFFFF");
    private static readonly IBrush FocusedBorderBrush = Brush.Parse("#C8D8FF");

    public DockWindow(DesktopSession session, WorkspaceViewModel model, Action<WorkspaceSection> show, Action<AppTile, bool> activate)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session; _model = model; _show = show; _activate = activate;
        _locations = session.Preferences.Current.CardLayoutVersion == 4 ? new(session.Preferences.Current.CardLocations, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        InitializeComponent();
        _barHideTimer.Tick += (_, _) =>
        {
            _barHideTimer.Stop();
            if (!_barActive && !ControlBar.IsPointerOver && !ControlBar.IsKeyboardFocusWithin) SetBarVisible(false);
        };
        ControlBar.PointerEntered += (_, _) => UpdateControlBarVisibility(true);
        Closed += (_, _) => _barHideTimer.Stop();
        _scene = new StarRingScene(_orbit);
        AddAppButton.Click += OnAddAppClick;
        LibraryButton.Click += (_, _) => _show(WorkspaceSection.Library);
        ResetButton.Click += (_, _) => ResetLayout();
        DesktopIconsButton.Click += (_, _) => _model.ToggleDesktopIcons();
        DesktopIconsText.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(WorkspaceViewModel.DesktopIconsLabel)) { Source = _model });
        DesktopIconsButton.Bind(Button.IsEnabledProperty, new Avalonia.Data.Binding(nameof(WorkspaceViewModel.CanToggleDesktopIcons)) { Source = _model });
        SettingsButton.Click += (_, _) => _show(WorkspaceSection.Settings);
        PrevButton.Click += (_, _) => MovePage(-1);
        NextButton.Click += (_, _) => MovePage(1);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnExternalDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnExternalDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnExternalDrop, RoutingStrategies.Bubble, handledEventsToo: true);

        var manage = new MenuItem { Header = "管理应用" };
        manage.Click += (_, _) => _show(WorkspaceSection.Library);
        var settings = new MenuItem { Header = "偏好设置" };
        settings.Click += (_, _) => _show(WorkspaceSection.Settings);
        var previous = new MenuItem { Header = "上一页" }; previous.Click += (_, _) => MovePage(-1);
        var next = new MenuItem { Header = "下一页" }; next.Click += (_, _) => MovePage(1);
        var reset = new MenuItem { Header = "重置空间布局" };
        reset.Click += (_, _) => ResetLayout();
        Gallery.ContextMenu = new ContextMenu { Items = { previous, next, reset, manage, settings } };
        model.DockApps.CollectionChanged += OnAppsChanged;
        model.TilesChanged += OnTilesRefreshed;
        model.PreferencesChanged += OnLayoutPreferencesChanged;
        Closed += (_, _) => { model.TilesChanged -= OnTilesRefreshed; model.PreferencesChanged -= OnLayoutPreferencesChanged; };
        Opened += (_, _) => { Place(); if (!_model.ReduceMotion) { _lastTick = Stopwatch.GetTimestamp(); _motionTimer.Start(); } };
        SizeChanged += (_, _) => ArrangeCards();
        PointerWheelChanged += OnWheel;
        PointerEntered += (_, _) => { _hovering = true; if (!_session.Preferences.Current.ImmersiveLaunchpad) _orbit.Stop(); };
        PointerExited += (_, _) => { _hovering = false; UpdateControlBarVisibility(false); };
        PointerMoved += OnPointerMoved;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTypeToSearch, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnOrbitPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnOrbitMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnOrbitReleased, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => { if (_pressed) { _pressed = false; _dragged = false; _pressedTile = null; _dropTargetTile = null; _orbit.Stop(); ResumeMotion(); ArrangeCards(); } };
        _motionTimer.Tick += OnMotionTick;
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) { _orbit.Stop(); _motionTimer.Stop(); } };
        Screens.Changed += OnScreensChanged;
        ScalingChanged += (_, _) => { if (!_pressed) Place(); else ArrangeCards(); };
        Deactivated += (_, _) => { if (_session.Preferences.Current.ImmersiveLaunchpad && IsVisible) Hide(); };
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { _motionTimer.Stop(); model.DockApps.CollectionChanged -= OnAppsChanged; Screens.Changed -= OnScreensChanged; };
        Rebuild();
    }

    public int SelectedIndex => _selected;
    private int FavoritePageCount => Math.Max(1, (_cards.Count(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: true }) + Math.Max(1, VisibleRingCapacity) - 1) / Math.Max(1, VisibleRingCapacity));
    private bool IsFavoritePage => _dockPage < FavoritePageCount;
    public int AlbumPage => _searchActive ? Math.Max(0, _selected) / SearchPageSize : _dockPage;
    public int AlbumPageCount => _searchActive ? Math.Max(1, (_cards.Count + SearchPageSize - 1) / SearchPageSize)
        : FavoritePageCount + (_cards.Count(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: false }) + Math.Max(1, _satelliteSlots.Count) - 1) / Math.Max(1, _satelliteSlots.Count);
    public int VisibleRingCapacity
    {
        get
        {
            var scale = Math.Min(_model.DockScale, Math.Min(Bounds.Width / 1000, Bounds.Height / 620));
            var radius = RingRadius(scale);
            var key = (radius, scale, _model.FavoriteVisibleCount, _model.FavoriteShape, _cards.Count(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: true }), Bounds.Size);
            if (_capacityKey == key) return _capacityResult;
            var count = Math.Min(_model.FavoriteVisibleCount, key.Item5);
            (_shapeRadius, _favoriteFit) = FitFormation(count, scale);
            _capacityKey = key;
            return _capacityResult = count;
        }
    }
    private double RingRadius(double scale) => Math.Min(430 * scale * _orbit.Zoom, Math.Min((Bounds.Width - 150 * scale) * .42, (Bounds.Height - 172 * scale) * .40));

    private (double Radius, double Fit) FitFormation(int count, double scale)
    {
        if (count <= 0) return (0, 1);
        var spacing = (Math.Sqrt(150 * 150 + 172 * 172) + 16) * scale;
        var needed = _model.FavoriteShape == "matrix" ? Math.Max(188 / .58, 210 / .68) * scale
            : ConstellationLayout.MinimumRadius(count, spacing, _model.FavoriteShape);
        var candidate = Math.Max(needed, RingRadius(scale) * .75);
        var phases = _model.FavoriteShape is "orbits" or "galaxy" ? 24 : 1;
        var points = Enumerable.Range(0, phases).SelectMany(phase => Enumerable.Range(0, count)
            .Select(index => ConstellationLayout.Position(index, count, candidate, phase * Math.Tau / phases, _model.FavoriteShape))).ToArray();
        var extentX = points.Max(point => Math.Abs(point.X)) + 90 * scale;
        var extentY = points.Max(point => Math.Abs(point.Y)) + 100 * scale;
        var sidebarCount = _cards.Count(IsSidebarCard);
        var sidebarRows = Math.Max(1, (int)((Bounds.Height - 180 * scale) / (88 * scale)));
        var sidebarCols = sidebarCount <= 0 ? 0 : (sidebarCount + sidebarRows - 1) / sidebarRows;
        var reservedLeft = sidebarCount <= 0 ? 0 : 56 * scale + sidebarCols * 196 * scale;
        var maxHalfWidth = Math.Max(1, (Bounds.Width - reservedLeft) / 2 - 24);
        var fit = Math.Min(1, Math.Min(maxHalfWidth / extentX, Math.Max(1, Bounds.Height / 2 - 76) / extentY));
        return (candidate * fit, fit);
    }

    private void BuildSatelliteSlots(double scale)
    {
        var count = Math.Min(_model.FavoriteVisibleCount, _cards.Count(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: false }));
        var key = (Bounds.Size, scale, _orbit.Zoom, count, _model.FavoriteShape);
        if (_ordinaryLayoutKey == key) return;
        _ordinaryLayoutKey = key;
        _satelliteSlots.Clear();
        for (var index = 0; index < count; index++) _satelliteSlots.Add(default);
        (_ordinaryRadius, _ordinaryFit) = FitFormation(count, scale);
    }
    public double OrbitYaw => _orbit.Yaw;
    public double OrbitPitch => _orbit.Pitch;
    public bool IsOrbitPaused => (!_session.Preferences.Current.ImmersiveLaunchpad && _hovering)
        || _pressed
        || !IsVisible
        || _model.ReduceMotion;
    public void Shutdown() { _allowClose = true; Close(); }
    public void ApplyPreferences()
    {
        ApplyTheme();
        _locations.Clear();
        if (_session.Preferences.Current.CardLayoutVersion == 4)
            foreach (var pair in _session.Preferences.Current.CardLocations) _locations.Add(pair.Key, pair.Value);
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        if (_model.ReduceMotion) { _orbit.Stop(); _motionTimer.Stop(); }
        else if (IsVisible) { _lastTick = Stopwatch.GetTimestamp(); _motionTimer.Start(); }
        UpdateControlBarVisibility(false);
        Place(); ArrangeCards();
    }
    public void Reveal()
    {
        if (_searchActive) ExitSearch();
        _dockPage = 0; _ringStart = 0; _outerStart = 0;
        _selected = Math.Max(0, _cards.FindIndex(card => card.DataContext is AppTile { IsPinned: true }));
        if (_rebuildPending) { _rebuildPending = false; Rebuild(); }
        Show();
        Place();
        Activate();
        DockSearch.Focus();
        if (!_model.ReduceMotion) { _lastTick = Stopwatch.GetTimestamp(); _motionTimer.Start(); }
    }

    public void Place()
    {
        var screen = ResolveTargetScreen();
        if (screen is null) return;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        if (_session.Preferences.Current.ImmersiveLaunchpad)
        {
            Width = Math.Max(740, screen.WorkingArea.Width / scaling);
            Height = Math.Max(480, screen.WorkingArea.Height / scaling);
            Position = screen.WorkingArea.Position;
        }
        else
        {
            Width = Math.Min(1060 * _model.DockScale, screen.WorkingArea.Width / scaling - 24);
            Height = Math.Min(620 * _model.DockScale, screen.WorkingArea.Height / scaling - 24);
            var defaultX = screen.WorkingArea.X + (screen.WorkingArea.Width - (int)(Width * scaling)) / 2;
            var defaultY = screen.WorkingArea.Y + (screen.WorkingArea.Height - (int)(Height * scaling)) / 2;
            Position = new PixelPoint(_session.Preferences.Current.GalleryLeft ?? defaultX, _session.Preferences.Current.GalleryTop ?? defaultY);
            KeepInWorkArea();
        }
        ArrangeCards();
    }

    private Screen? ResolveTargetScreen()
    {
        return Screens.ScreenFromWindow(this)
            ?? (_session.Preferences.Current.GalleryLeft is { } px && _session.Preferences.Current.GalleryTop is { } py ? Screens.ScreenFromPoint(new PixelPoint(px, py)) : null)
            ?? Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
    }

    public void MoveSelection(int delta)
    {
        var candidates = _cards.Where(card => _searchActive || (!IsSidebarCard(card) && card.DataContext is AppTile app && app.IsPinned == IsFavoritePage)).ToList();
        if (candidates.Count == 0) candidates = _cards.Where(IsSidebarCard).ToList();
        if (candidates.Count == 0) return;
        var current = _selected >= 0 && _selected < _cards.Count ? Math.Max(0, candidates.IndexOf(_cards[_selected])) : 0;
        var next = (current + delta % candidates.Count + candidates.Count) % candidates.Count;
        _selected = _cards.IndexOf(candidates[next]);
        if (!_searchActive && !IsSidebarCard(candidates[next]))
            _dockPage = IsFavoritePage ? next / Math.Max(1, VisibleRingCapacity) : FavoritePageCount + next / Math.Max(1, _satelliteSlots.Count);
        ArrangeCards();
        ResumeMotion();
    }

    public void MovePage(int delta)
    {
        if (!_searchActive)
        {
            var targetPage = Math.Clamp(_dockPage + delta, 0, AlbumPageCount - 1);
            if (targetPage == _dockPage) { ResumeMotion(); return; }
            _dockPage = targetPage;
            _ringStart = IsFavoritePage ? _dockPage * Math.Max(1, VisibleRingCapacity) : 0;
            _outerStart = Math.Max(0, _dockPage - FavoritePageCount) * Math.Max(1, _satelliteSlots.Count);
            var group = _cards.Where(card => !IsSidebarCard(card) && card.DataContext is AppTile app && app.IsPinned == IsFavoritePage).ToList();
            _selected = group.Count == 0 ? -1 : _cards.IndexOf(group[Math.Min(IsFavoritePage ? _ringStart : _outerStart, group.Count - 1)]);
            ArrangeCards(); ResumeMotion(); return;
        }
        _selected = Math.Clamp(AlbumPage + delta, 0, AlbumPageCount - 1) * SearchPageSize;
        _orbit.Focus(0, 1); ArrangeCards();
    }

    private void ResumeMotion()
    {
        if (!IsVisible || _model.ReduceMotion || _pressed) return;
        _lastTick = Stopwatch.GetTimestamp();
        _motionTimer.Start();
    }

    private void KeepInWorkArea()
    {
        if (_session.Preferences.Current.ImmersiveLaunchpad) return;
        var screen = ResolveTargetScreen();
        if (screen is null) return;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        Position = WorkspacePlacement.Fit(screen.WorkingArea, scaling, Width, Height, Position).Position;
    }

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRebuild();
    private void OnTilesRefreshed(object? sender, EventArgs e) => QueueRebuild();
    private void OnLayoutPreferencesChanged(object? sender, EventArgs e)
    { _capacityKey = default; ApplyPreferences(); }
    private void QueueRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_rebuildPending || !IsVisible || _allowClose) return;
            _rebuildPending = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }
    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_scene is null) return;
        _searchQuery = DockSearch.Text?.Trim() ?? "";
        if (!_searchActive && _searchQuery.Length > 0) { BeginSearch(); return; }
        _selected = 0;
        Rebuild();
    }

    private void OnTypeToSearch(object? sender, TextInputEventArgs e)
    {
        if (_searchActive || string.IsNullOrWhiteSpace(e.Text)) return;
        BeginSearch();
        if (e.Source != DockSearch) { DockSearch.Text = e.Text; DockSearch.CaretIndex = e.Text.Length; e.Handled = true; }
    }

    private void BeginSearch()
    {
        if (_searchActive) { DockSearch.Focus(); return; }
        _searchActive = true;
        SearchPanel.Opacity = 1; SearchPanel.IsHitTestVisible = true;
        RingBackdrop.Children.Clear(); RingBackdrop.IsVisible = true;
        foreach (var card in _cards.Where(card => card.IsVisible))
        {
            if (card.DataContext is not AppTile tile) continue;
            var echo = new Border { Width = card.Width, Height = card.Height, CornerRadius = new CornerRadius(26), Background = Brush.Parse("#D0252D43"),
                Child = tile.HasIcon ? new Image { Source = tile.Icon, Width = 58, Height = 58 } : new TextBlock { Text = tile.Initial, FontSize = 38, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Canvas.SetLeft(echo, Canvas.GetLeft(card)); Canvas.SetTop(echo, Canvas.GetTop(card));
            RingBackdrop.Children.Add(echo);
        }
        _selected = 0; Rebuild(); DockSearch.Focus();
    }

    private void ExitSearch()
    {
        _dockPage = 0; _ringStart = 0; _outerStart = 0;
        _searchActive = false; _searchQuery = ""; DockSearch.Text = "";
        SearchPanel.Opacity = 0; SearchPanel.IsHitTestVisible = false;
        RingBackdrop.IsVisible = false; RingBackdrop.Children.Clear();
        _selected = 0; Rebuild(); DockSearch.Focus();
    }
    private void OnScreensChanged(object? sender, EventArgs e) { if (!_pressed) Place(); }

    private void Rebuild()
    {
        Gallery.Children.Clear(); _cards.Clear();
        Gallery.Children.Add(_scene);
        var matches = _searchQuery.Length == 0 ? null : _session.Search(_searchQuery).Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var tile in _model.DockApps.Where(tile => matches is null || matches.Contains(tile.Id)))
        {
            var icon = new Image { Width = 58, Height = 58, Stretch = Stretch.Uniform };
            icon.Bind(Image.SourceProperty, new Binding(nameof(AppTile.Icon)) { Source = tile });
            icon.Bind(IsVisibleProperty, new Binding(nameof(AppTile.HasIcon)) { Source = tile });
            var fallback = new TextBlock { Text = tile.Initial, FontSize = 48, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            fallback.Bind(IsVisibleProperty, new Binding("!HasIcon") { Source = tile });
            var name = new TextBlock { Text = tile.Name, FontSize = 13, FontWeight = FontWeight.Medium,
                Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 120 };
            var indicator = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = Brush.Parse("#B9F3DC"), HorizontalAlignment = HorizontalAlignment.Center };
            indicator.Bind(IsVisibleProperty, new Binding(nameof(AppTile.IsRunning)) { Source = tile });
            var content = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Children = { new Grid { Width = 64, Height = 64, Children = { fallback, icon } }, name, indicator } };
            var surface = new Border { CornerRadius = new CornerRadius(26), Padding = new Thickness(0),
                BorderThickness = new Thickness(1), BorderBrush = Brush.Parse("#65FFFFFF"),
                Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#E02A344C"), 0), new GradientStop(Color.Parse("#C012172A"), 1) } },
                Child = new Viewbox { Stretch = Stretch.Uniform, Child = new Border { Width = 150, Height = 172, Padding = new Thickness(18), Child = content } } };
            var button = new Button { Content = surface, Padding = new Thickness(0), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(26), FocusAdorner = null, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch, Classes = { "album-card" }, DataContext = tile };
            if (IsSidebarCard(button))
            {
                var iconHost = (Grid)content.Children[0];
                content.Children.Clear();
                iconHost.Width = iconHost.Height = 36;
                icon.Width = icon.Height = 34;
                fallback.FontSize = 26;
                name.FontSize = 12; name.MaxWidth = 116;
                name.HorizontalAlignment = HorizontalAlignment.Left;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextWrapping = TextWrapping.Wrap;
                name.MaxLines = 2;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), Children = { iconHost, name } };
                Grid.SetColumn(name, 1);
                surface.Child = new Viewbox { Stretch = Stretch.Uniform, Child = new Border
                {
                    Width = 184, Height = 76, Padding = new Thickness(14, 12), Child = row
                } };
            }
            AutomationProperties.SetName(button, tile.Name);
            button.Click += OnCardClick;
            var pin = new MenuItem { Header = tile.IsPinned ? "取消常用" : "设为常用" };
            pin.Click += (_, _) =>
            {
                try { _model.TogglePin(tile); }
                catch (Exception error) when (error is Astria.Core.Storage.StorageException or InvalidOperationException) { _model.Status = error.Message; }
            };
            button.ContextMenu = new ContextMenu { Items = { pin } };
            if (tile.Entry?.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            {
                var background = new MenuItem { Header = "后台驻留也标记运行", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _model.IncludesBackground(tile.Id) };
                background.Click += (_, _) => { _model.ToggleBackgroundRunning(tile); background.IsChecked = _model.IncludesBackground(tile.Id); };
                button.ContextMenu.Items.Add(background);
            }
            _cards.Add(button); Gallery.Children.Add(button);
        }
        _appliedTheme = null; ApplyTheme();
        if (_cards.Count == 0)
        {
            var add = new Button { Content = _searchQuery.Length > 0 ? "无匹配应用\n清空搜索" : "+", FontSize = _searchQuery.Length > 0 ? 14 : 34, Foreground = Brush.Parse("#BFFFFFFF"), Width = 120, Height = 160,
                CornerRadius = new CornerRadius(24), Background = Brush.Parse("#55242A3C"), BorderBrush = Brush.Parse("#55FFFFFF"), BorderThickness = new Thickness(1) };
            ToolTip.SetTip(add, "添加应用到相册");
            add.Click += (_, _) => { if (_searchActive) ExitSearch(); else _show(WorkspaceSection.Library); };
            Gallery.Children.Add(add);
        }
        _capacityKey = default;
        _ordinaryLayoutKey = null;
        _selected = _selected < 0 ? Math.Min(2, Math.Max(0, _cards.Count - 1)) : Math.Clamp(_selected, 0, Math.Max(0, _cards.Count - 1));
        _ringStart = _cards.Count == 0 ? 0 : _ringStart % _cards.Count;
        var pinnedCount = _cards.Count(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: true });
        var favoriteCapacity = Math.Max(1, VisibleRingCapacity);
        _ringStart = Math.Clamp(_ringStart / favoriteCapacity, 0, Math.Max(0, (pinnedCount - 1) / favoriteCapacity)) * favoriteCapacity;
        _dockPage = Math.Clamp(_dockPage, 0, AlbumPageCount - 1);
        if (_searchActive) _orbit.Focus(0, 1);
        SearchHint.IsVisible = _searchQuery.Length > 0;
        SearchHint.Text = $"{_cards.Count} 个结果 · Enter 打开 · Esc 返回常用";
        ArrangeCards();
    }

    private void ArrangeCards()
    {
        if (Bounds.Width < 1 || Bounds.Height < 1) return;
        var scale = Math.Min(_model.DockScale, Math.Min(Bounds.Width / 1000, Bounds.Height / 620));
        BuildSatelliteSlots(scale);
        _scene.Width = Bounds.Width; _scene.Height = Bounds.Height;
        _scene.SceneScale = scale;
        _scene.Immersive = _session.Preferences.Current.ImmersiveLaunchpad;
        _scene.Phase = _model.ReduceMotion ? 0 : Stopwatch.GetElapsedTime(_floatEpoch).TotalSeconds;
        _scene.IsVisible = true;
        _scene.InvalidateVisual();

        var sidebar = _cards.Where(IsSidebarCard).ToList();
        var pinned = _cards.Where(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: true }).ToList();
        var other = _cards.Where(card => !IsSidebarCard(card) && card.DataContext is AppTile { IsPinned: false }).ToList();
        if (!_searchActive)
        {
            _dockPage = Math.Clamp(_dockPage, 0, AlbumPageCount - 1);
            _ringStart = IsFavoritePage ? _dockPage * Math.Max(1, VisibleRingCapacity) : 0;
            _outerStart = Math.Max(0, _dockPage - FavoritePageCount) * Math.Max(1, _satelliteSlots.Count);
        }
        const double sidebarFit = 1;
        var sidebarCardW = 184 * scale;
        var sidebarCardH = 76 * scale;
        var sidebarGapX = 12 * scale;
        var sidebarGapY = 12 * scale;
        var sidebarRows = Math.Max(1, (int)((Bounds.Height - 180 * scale) / (88 * scale)));
        var sidebarCols = sidebar.Count == 0 ? 0 : (sidebar.Count + sidebarRows - 1) / sidebarRows;
        var wingWidth = sidebarCols == 0 ? 0 : sidebarCols * sidebarCardW + (sidebarCols - 1) * sidebarGapX;
        var wingGap = sidebar.Count > 0 && !_searchActive ? 56 * scale : 0;

        var formationSlots = IsFavoritePage ? VisibleRingCapacity : _satelliteSlots.Count;
        var formationRadius = IsFavoritePage ? _shapeRadius : _ordinaryRadius;
        var formationCardW = 150 * scale * (IsFavoritePage ? _favoriteFit : _ordinaryFit);
        var maxSlotX = formationSlots <= 0 ? 90 * scale
            : _model.FavoriteShape is "orbits" or "galaxy" && formationSlots > 2 ? formationRadius
            : Enumerable.Range(0, formationSlots).Max(slot => Math.Abs(ConstellationLayout.Position(slot, formationSlots, formationRadius, _orbit.Yaw + _orbit.Roll, _model.FavoriteShape).X));
        var formationHalfSpanX = maxSlotX + formationCardW / 2;
        var totalSpan = wingWidth + wingGap + formationHalfSpanX * 2;
        var sidebarLeftPad = Math.Max(28 * scale, (Bounds.Width - totalSpan) / 2);
        var formationCenterX = sidebar.Count > 0 && !_searchActive
            ? Math.Min(Bounds.Width - 24 * scale - formationHalfSpanX, sidebarLeftPad + wingWidth + wingGap + formationHalfSpanX)
            : Bounds.Width / 2;
        var firstColRows = Math.Min(sidebarRows, sidebar.Count);
        var firstColHeight = firstColRows <= 0 ? 0 : firstColRows * sidebarCardH + (firstColRows - 1) * sidebarGapY;

        _scene.Formation = !_searchActive ? _model.FavoriteShape : "";
        _scene.FormationCount = formationSlots;
        _scene.FormationRadius = formationRadius;
        _scene.FormationRotation = _orbit.Yaw + _orbit.Roll;
        _scene.FormationOffsetX = formationCenterX - Bounds.Width / 2;
        _scene.SidebarAnchor = sidebar.Count > 0 && !_searchActive ? new Point(sidebarLeftPad + wingWidth / 2, Bounds.Height / 2) : null;
        _scene.SidebarSpanY = firstColHeight;
        _scene.SidebarArcDepth = 0;
        EmptyFavorites.IsVisible = !_searchActive && _dockPage == 0 && pinned.Count == 0;
        var pageCount = Math.Max(1, Math.Min(SearchPageSize, _cards.Count - AlbumPage * SearchPageSize));        var t = Stopwatch.GetElapsedTime(_floatEpoch).TotalSeconds;


        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            var isSelected = i == _selected;
            var isSidebar = IsSidebarCard(card);
            var isPinned = card.DataContext is AppTile { IsPinned: true };
            var cardScale = scale * (_searchActive ? 1 : isSidebar ? sidebarFit : isPinned ? _favoriteFit : _ordinaryFit);
            card.Width = (isSidebar ? 184 : 150) * cardScale; card.Height = (isSidebar ? 76 : 172) * cardScale;
            if (card.Content is Border cardSurface) cardSurface.CornerRadius = new CornerRadius((isSidebar ? 18 : 26) * cardScale);
            var ringSlot = isSidebar ? sidebar.IndexOf(card) : isPinned ? pinned.IndexOf(card) - _ringStart : other.IndexOf(card) - _outerStart;
            card.IsVisible = _searchActive ? i / SearchPageSize == AlbumPage : isSidebar || (isPinned == IsFavoritePage && ringSlot >= 0 && ringSlot < (isPinned ? VisibleRingCapacity : _satelliteSlots.Count));
            if (!card.IsVisible) continue;

            card.RenderTransformOrigin = RelativePoint.Center;
            card.Transitions = null;

            var isDragging = _pressed && card.DataContext is AppTile activeTile && ReferenceEquals(_pressedTile, activeTile);
            var isDropTarget = _pressed && _dragged && card.DataContext is AppTile targetTile && ReferenceEquals(_dropTargetTile, targetTile);

            double baseX, baseY;
            var radius = isPinned ? _shapeRadius : _ordinaryRadius;
            var floatRadial = _model.ReduceMotion ? 0 : Math.Sin(t * .62 + i * .4) * 3 * scale;
            var floatRot = _model.ReduceMotion || _searchActive ? 0 : Math.Sin(t * .45 + i * 1.25) * 3;
            if (_searchActive)
            {
                var column = i % SearchPageSize;
                var gap = 16 * scale;
                baseX = (Bounds.Width - pageCount * card.Width - (pageCount - 1) * gap) / 2 + column * (card.Width + gap);
                baseY = Bounds.Height / 2 + 68;
            }
            else if (card.DataContext is AppTile app && _locations.TryGetValue(app.Id, out var location))
            {
                baseX = location.X * Math.Max(1, Bounds.Width - card.Width) + (_model.ReduceMotion ? 0 : Math.Sin(t * .6 + i) * 5 * scale);
                baseY = location.Y * Math.Max(1, Bounds.Height - card.Height) + (_model.ReduceMotion ? 0 : Math.Cos(t * .5 + i) * 5 * scale);
            }
            else if (isSidebar)
            {
                var col = ringSlot / sidebarRows;
                var row = ringSlot % sidebarRows;
                var rowsInCol = Math.Min(sidebarRows, sidebar.Count - col * sidebarRows);
                var colHeight = rowsInCol * card.Height + (rowsInCol - 1) * sidebarGapY;
                baseX = sidebarLeftPad + col * (card.Width + sidebarGapX);
                baseY = Math.Max(24 * scale, (Bounds.Height - 44 * scale - colHeight) / 2) + row * (card.Height + sidebarGapY);
                if (!_model.ReduceMotion)
                {
                    baseX += Math.Sin(t * .55 + i * .7) * 2.5 * scale;
                    baseY += Math.Cos(t * .48 + i * .9) * 3.5 * scale;
                }
                floatRot = 0;
            }
            else
            {
                var rotation = _orbit.Yaw + _orbit.Roll;
                var position = ConstellationLayout.Position(ringSlot, isPinned ? VisibleRingCapacity : _satelliteSlots.Count, radius + floatRadial, rotation, _model.FavoriteShape);
                baseX = formationCenterX - card.Width / 2 + position.X;
                baseY = (Bounds.Height - card.Height) / 2 + position.Y;
                if (!_model.ReduceMotion)
                {
                    baseX += Math.Sin(t * .6 + i) * 2 * scale;
                    baseY += Math.Cos(t * .5 + i) * 3 * scale;
                }
                if (_model.FavoriteShape == "matrix" && !_model.ReduceMotion)
                {
                    baseY += Math.Sin(t * .7 + ringSlot * .85) * 7 * scale;
                    floatRot *= .25;
                }
            }
            double floatX = 0, floatY = 0;
            var posX = isDragging ? _cardOrigin.X + _lastPoint.X - _pressPoint.X : baseX + floatX;
            var posY = isDragging ? _cardOrigin.Y + _lastPoint.Y - _pressPoint.Y : baseY + floatY;
            Canvas.SetLeft(card, Math.Clamp(posX, 0, Math.Max(0, Bounds.Width - card.Width)));
            Canvas.SetTop(card, Math.Clamp(posY, 0, Math.Max(0, Bounds.Height - card.Height)));
            var factor = isDragging ? 1.08 : isDropTarget ? 1.06 : isSelected ? 1.05 : 1;
            if (card.RenderTransform is not TransformGroup transforms)
            {
                transforms = new TransformGroup();
                transforms.Children.Add(new ScaleTransform());
                transforms.Children.Add(new RotateTransform());
                card.RenderTransform = transforms;
            }
            var scaling = (ScaleTransform)transforms.Children[0];
            scaling.ScaleX = scaling.ScaleY = factor;
            ((RotateTransform)transforms.Children[1]).Angle = floatRot;
            card.ZIndex = isDragging ? 9999 : 4000 + i + (isSelected || isDropTarget ? 300 : 0);
            card.Opacity = isDragging || isSelected || isDropTarget ? 1 : .92;
            if (card.Content is Border surface)
            {
                surface.BorderBrush = isSelected || isDragging || isDropTarget ? _cardAccent : NormalBorderBrush;
                surface.BorderThickness = isDragging || isDropTarget ? new Thickness(2.2) : isSelected ? new Thickness(1.8) : new Thickness(1);
            }
        }
        if (_cards.Count == 0 && Gallery.Children.Count > 1)
        {
            Canvas.SetLeft(Gallery.Children[1], (Bounds.Width - 120) / 2);
            Canvas.SetTop(Gallery.Children[1], _searchActive ? Bounds.Height / 2 + 68 : (Bounds.Height - 160) / 2);
        }
        PageText.Text = _searchActive ? $"{AlbumPage + 1} / {AlbumPageCount}" : IsFavoritePage ? $"常用 · {_dockPage + 1} / {FavoritePageCount}" : $"应用 · {_dockPage - FavoritePageCount + 1} / {AlbumPageCount - FavoritePageCount}";
        PrevButton.IsEnabled = AlbumPage > 0;
        NextButton.IsEnabled = AlbumPage + 1 < AlbumPageCount;
        PagePanel.IsVisible = AlbumPageCount > 1;
        PageSeparator.IsVisible = PagePanel.IsVisible;
    }

    private void ResetLayout()
    {
        _locations.Clear();
        _orbit.Focus(0, 1);
        ArrangeCards();
        try { _session.Preferences.Save(_session.Preferences.Current with { CardLocations = [], CardLayoutVersion = 4 }); }
        catch (Astria.Core.Storage.StorageException error) { _model.Status = error.Message; }
    }

    private void OnAddAppClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor) ApplicationPicker.Open(anchor, AddItemsAsync, target =>
        {
            try { ImportAndPin([target]); }
            catch (Exception error) when (error is Astria.Core.Storage.StorageException or InvalidOperationException) { _model.Status = error.Message; }
            return Task.CompletedTask;
        });
    }

    private async Task AddItemsAsync(bool folders)
    {
        try
        {
            var paths = await ApplicationPicker.SelectAsync(StorageProvider, folders);
            if (paths.Count == 0) return;
            ImportAndPin(paths);
        }
        catch (Exception error) when (error is Astria.Core.Storage.StorageException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _model.Status = error.Message;
        }
    }

    private void ImportAndPin(IReadOnlyList<string> paths)
    {
        var requests = paths.Select(p => new ImportRequest { Source = p }).ToList();
        var result = _session.Import(requests);
        _model.Refresh();
        _model.Status = $"已添加 {result.Added.Count} 项 · 已存在 {result.Duplicates.Count} 项 · 失败 {result.Failures.Count} 项";
    }

    private void OnExternalDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        e.DragEffects = _model.IsBusy ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnExternalDrop(object? sender, DragEventArgs e)
    {
        if (_model.IsBusy || e.DataTransfer.TryGetFiles() is not { } files) return;
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        e.Handled = true;
        if (paths.Count > 0)
        {
            try { ImportAndPin(paths); }
            catch (Exception error) when (error is Astria.Core.Storage.StorageException or InvalidOperationException or IOException or UnauthorizedAccessException)
            { _model.Status = "拖入未完成：" + error.Message; _show(WorkspaceSection.Library); }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var nearBottom = point.Y >= Bounds.Height - (_barVisible ? 125 : 70) || ControlBar.IsPointerOver;
        UpdateControlBarVisibility(nearBottom);
    }

    public void UpdateControlBarVisibility(bool active)
    {
        _barActive = active;
        if (!_session.Preferences.Current.AutoHideDock || active || ControlBar.IsPointerOver || ControlBar.IsKeyboardFocusWithin)
        { _barHideTimer.Stop(); SetBarVisible(true); return; }
        if (_barVisible && !_barHideTimer.IsEnabled) _barHideTimer.Start();
    }

    private void SetBarVisible(bool show)
    {
        if (_barVisible == show) return;
        _barVisible = show;
        ControlBar.Opacity = show ? 1 : 0;
        ControlBar.IsHitTestVisible = show;
        ControlBar.RenderTransform = global::Avalonia.Media.Transformation.TransformOperations.Parse(
            show ? "translateY(0px)" : "translateY(10px)");
    }

    private void ApplyTheme()
    {
        if (_appliedTheme == _model.Accent) return;
        _appliedTheme = _model.Accent;
        var palette = DesktopPalette.For(_model.Accent);
        _cardAccent = Brush.Parse(palette.Accent);
        _scene.SetTheme(_model.Accent);
        ControlBar.Background = Brush.Parse("#E0" + palette.Surface[1..]);
        ControlBar.BorderBrush = Brush.Parse("#50" + palette.Accent[1..]);
        SearchPanel.Background = Brush.Parse("#F0" + palette.Surface[1..]);
        foreach (var card in _cards)
            if (card.Content is Border surface)
                surface.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#E0" + palette.Surface[1..]), 0), new GradientStop(Color.Parse("#C012172A"), 1) }
                };
    }

    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AppTile tile } button) return;
        _selected = _cards.IndexOf(button);
        ArrangeCards();
        _activate(tile, true);
    }

    private void OnOrbitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var source = e.Source as Visual;
        // Disabled toolbar buttons route input through their parent; never start a scene drag there.
        var barOrigin = ControlBar.TranslatePoint(default, this);
        if (source == ControlBar || source?.GetVisualAncestors().Contains(ControlBar) == true
            || barOrigin is { } origin && new Rect(origin, ControlBar.Bounds.Size).Contains(e.GetPosition(this))) return;
        if (source == SearchPanel || source?.GetVisualAncestors().Contains(SearchPanel) == true) return;
        var button = source as Button ?? source?.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        if (button is not null && button.DataContext is not AppTile) return;
        _pressedTile = button?.DataContext as AppTile;
        _dropTargetTile = null;
        _dragHadInitialLocation = _pressedTile is { } pressed && _locations.TryGetValue(pressed.Id, out _dragInitialLocation);
        var point = e.GetPosition(this);
        var nearEdge = point.X < 32 || point.Y < 32 || point.X > Bounds.Width - 32 || point.Y > Bounds.Height - 32;
        _rotating = e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            || (_pressedTile is null && !_searchActive && !nearEdge && !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _pressPoint = _lastPoint = e.GetPosition(this);
        _windowOrigin = Position;
        _screenPress = this.PointToScreen(_pressPoint);
        if (button is not null)
        {
            var left = Canvas.GetLeft(button);
            var top = Canvas.GetTop(button);
            _cardOrigin = double.IsFinite(left) && double.IsFinite(top)
                ? new Point(left, top)
                : button.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        }
        _lastInput = Stopwatch.GetTimestamp();
        _pressed = true; _dragged = false;
        _orbit.Stop(); _motionTimer.Stop();
        Gallery.Focus();
        e.Pointer.Capture(Gallery);
        e.Handled = true;
    }

    private void OnOrbitMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed) return;
        var point = e.GetPosition(this);
        if (!_dragged && Point.Distance(point, _pressPoint) < 6) return;
        _dragged = true;
        if (_searchActive) { e.Handled = true; return; }
        var now = Stopwatch.GetTimestamp();
        if (_rotating)
        {
            _orbit.Drag(point.X - _lastPoint.X, point.Y - _lastPoint.Y,
                Stopwatch.GetElapsedTime(_lastInput, now).TotalSeconds, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            ArrangeCards();
        }
        else if (_pressedTile is { } tile)
        {
            var card = _cards.First(item => ReferenceEquals(item.DataContext, tile));
            var x = Math.Clamp(_cardOrigin.X + point.X - _pressPoint.X, 0, Math.Max(0, Bounds.Width - card.Width));
            var y = Math.Clamp(_cardOrigin.Y + point.Y - _pressPoint.Y, 0, Math.Max(0, Bounds.Height - card.Height));
            _locations[tile.Id] = new CardLocation(x / Math.Max(1, Bounds.Width - card.Width), y / Math.Max(1, Bounds.Height - card.Height));
            _dropTargetTile = FindDropTarget(card, new Point(x + card.Width / 2, y + card.Height / 2), point);
            ArrangeCards();
        }
        else if (!_session.Preferences.Current.ImmersiveLaunchpad)
        {
            var screenPoint = this.PointToScreen(point);
            Position = new PixelPoint(_windowOrigin.X + screenPoint.X - _screenPress.X, _windowOrigin.Y + screenPoint.Y - _screenPress.Y);
        }
        _lastPoint = point; _lastInput = now;
        e.Handled = true;
    }

    private void OnOrbitReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        e.Pointer.Capture(null);
        if (_searchActive && _dragged) { e.Handled = true; return; }
        if (_dragged)
        {
            if (_rotating)
            {
                if (_model.ReduceMotion || Stopwatch.GetElapsedTime(_lastInput).TotalMilliseconds > 100) _orbit.Stop();
                else { _lastTick = Stopwatch.GetTimestamp(); _motionTimer.Start(); }
            }
            else if (_pressedTile is { } tile)
            {
                var targetTile = _dropTargetTile;
                _dropTargetTile = null;
                if (targetTile is not null && targetTile.Id != tile.Id)
                {
                    if (_locations.TryGetValue(targetTile.Id, out var targetLocation)) _locations[tile.Id] = targetLocation;
                    else _locations.Remove(tile.Id);
                    if (_dragHadInitialLocation && _dragInitialLocation is not null) _locations[targetTile.Id] = _dragInitialLocation;
                    else _locations.Remove(targetTile.Id);
                    try
                    {
                        _session.SwapItems(tile.Id, targetTile.Id);
                        _model.Refresh();
                        if (_rebuildPending) { _rebuildPending = false; Rebuild(); }
                        _selected = _cards.FindIndex(card => card.DataContext is AppTile item && item.Id == tile.Id);
                    }
                    catch (Exception error) when (error is Astria.Core.Storage.StorageException or InvalidOperationException)
                    { _model.Status = error.Message; }
                }
                try { _session.Preferences.Save(_session.Preferences.Current with { CardLocations = new(_locations, StringComparer.Ordinal), CardLayoutVersion = 4 }); }
                catch (Astria.Core.Storage.StorageException error) { _model.Status = error.Message; }
            }
            else if (!_session.Preferences.Current.ImmersiveLaunchpad)
            {
                KeepInWorkArea();
                try { _session.Preferences.Save(_session.Preferences.Current with { CardLocations = new(_locations, StringComparer.Ordinal), CardLayoutVersion = 4, GalleryLeft = Position.X, GalleryTop = Position.Y }); }
                catch (Astria.Core.Storage.StorageException error) { _model.Status = error.Message; }
            }
        }
        else if (_pressedTile is { } tile)
        {
            _selected = _cards.FindIndex(card => ReferenceEquals(card.DataContext, tile));
            ArrangeCards();
            _activate(tile, true);
        }
        else if (_session.Preferences.Current.ImmersiveLaunchpad && _pressedTile is null && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            var point = e.GetPosition(this);
            if (ControlBar.Bounds.Contains(point)) return;
            // 单击全屏星空空白区域直接收起退出
            Hide();
            return;
        }
        if (!_model.ReduceMotion && IsVisible) { _lastTick = Stopwatch.GetTimestamp(); _motionTimer.Start(); }
        ArrangeCards();
        e.Handled = true;
    }

    private AppTile? FindDropTarget(Button draggedCard, Point cardCenter, Point pointer)
    {
        Button? best = null;
        var bestDistance = double.MaxValue;
        foreach (var candidate in _cards)
        {
            if (!candidate.IsVisible || ReferenceEquals(candidate, draggedCard) || candidate.DataContext is not AppTile) continue;
            var left = Canvas.GetLeft(candidate);
            var top = Canvas.GetTop(candidate);
            if (!double.IsFinite(left) || !double.IsFinite(top)) continue;
            var rect = new Rect(left, top, candidate.Width, candidate.Height);
            var distance = Math.Min(Point.Distance(rect.Center, pointer), Point.Distance(rect.Center, cardCenter));
            var hitRadius = Math.Min(candidate.Width, candidate.Height) * 0.55;
            if ((rect.Deflate(12).Contains(pointer) || distance <= hitRadius) && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best?.DataContext as AppTile;
    }

    private void OnMotionTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds;
        _lastTick = now;
        if (!IsVisible || _model.ReduceMotion || _cards.Count == 0)
        {
            _motionTimer.Stop();
            return;
        }
        if (_pressed) return;

        if (_orbit.IsMoving)
        {
            _orbit.Advance(elapsed);
        }
        else if (!_searchActive && !_cards.Any(card => card.IsVisible && card.IsPointerOver))
        {
            _orbit.Revolve(elapsed);
        }
        ArrangeCards();
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_searchActive && DockSearch.IsFocused) return;
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : -e.Delta.Y;
        if (Math.Abs(delta) < .01) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _orbit.ChangeZoom(-delta); ArrangeCards(); }
        else MovePage(Math.Sign(delta));
        e.Handled = true;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { BeginSearch(); DockSearch.SelectAll(); e.Handled = true; return; }
        if (e.Key == Key.Escape && _searchActive) { ExitSearch(); e.Handled = true; return; }
        if (_searchActive && DockSearch.IsFocused && e.Key is not (Key.Enter or Key.Escape or Key.Down or Key.Up or Key.PageDown or Key.PageUp)) return;
        if (e.Key == Key.Home) { _orbit.Focus(0, 1); ArrangeCards(); }
        else if (e.Key == Key.PageDown || e.Key == Key.Down) MovePage(1);
        else if (e.Key == Key.PageUp || e.Key == Key.Up) MovePage(-1);
        else if (e.Key == Key.Left || (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Shift))) MoveSelection(-1);
        else if (e.Key == Key.Right || e.Key == Key.Tab) MoveSelection(1);
        else if (e.Key == Key.Escape) Hide();
        else if (e.Key == Key.Enter && _cards.Count > 0 && _selected >= 0 && _selected < _cards.Count && _cards[_selected].DataContext is AppTile tile) _activate(tile, true);
        else return;
        e.Handled = true;
    }
}
