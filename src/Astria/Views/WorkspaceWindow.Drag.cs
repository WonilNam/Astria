using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Astria.Desktop.ViewModels;

namespace Astria.Desktop.Views;

public partial class WorkspaceWindow
{
    private Button? _dragSource, _dropTarget;
    private Point _dragStart;
    private bool _tileDragging;
    private Border? _dragPreview;
    private IPointer? _dragPointer;
    private readonly List<(Button Button, Rect Bounds)> _dragSlots = [];
    private TranslateTransform? _previewTransform;
    private Rect _dragArea;
    private AppTile? _dragTile;
    private readonly DispatcherTimer _pageHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private int _pageHoverDirection;
    private bool _pageHoverHooked;

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var button = e.Source as Button ?? (e.Source as Visual)?.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        if (button?.DataContext is not AppTile tile || _model.IsBusy) return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { ShowTileMenu(button, tile); return; }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        CancelTileDrag();
        _dragSource = button; _dragTile = tile; _dragStart = e.GetPosition(this); _dragPointer = e.Pointer;
        if (!_pageHoverHooked) { _pageHoverTimer.Tick += OnDragPageHover; _pageHoverHooked = true; }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTile is not { } tile || _dragSource is null) return;
        var point = e.GetPosition(this);
        if (!_tileDragging && Point.Distance(point, _dragStart) < 8) return;
        if (!string.IsNullOrWhiteSpace(_model.Query)) { _tileDragging = true; _model.Status = "清空搜索后可拖拽排序。"; return; }
        if (!_tileDragging)
        {
            _tileDragging = true;
            _dragSource.Opacity = 0;
            _dragSlots.Clear();
            _dragArea = new Rect(AppList.TranslatePoint(default, this) ?? default, AppList.Bounds.Size);
            foreach (var item in AppList.GetVisualDescendants().OfType<Button>().Where(item => item.DataContext is AppTile))
                if (item.TranslatePoint(default, this) is { } origin) _dragSlots.Add((item, new Rect(origin, item.Bounds.Size)));
            _dragPreview = new Border
            {
                Width = _dragSource.Bounds.Width, Height = _dragSource.Bounds.Height, Padding = new Thickness(10), CornerRadius = new CornerRadius(14),
                Background = Brush.Parse("#ED20283E"), BorderBrush = Brush.Parse("#B8ADFF"), BorderThickness = new Thickness(1),
                BoxShadow = BoxShadows.Parse("0 6 16 0 #50000000"),
                Child = new StackPanel { Spacing = 8, Children =
                {
                    new Grid { Width = 46, Height = 46, Children =
                    {
                        new Border { CornerRadius = new CornerRadius(12), Background = tile.TileBrush, IsVisible = !tile.HasIcon,
                            Child = new TextBlock { Text = tile.Initial, FontSize = 25, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
                        new Image { Source = tile.Icon, Width = 42, Height = 42, IsVisible = tile.HasIcon }
                    } },
                    new TextBlock { Text = tile.Name, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center },
                    new TextBlock { Text = tile.Subtitle, FontSize = 10, Foreground = Brush.Parse("#B8ADFF"), TextAlignment = TextAlignment.Center }
                } }
            };
            var sourceOrigin = _dragSource.TranslatePoint(default, DragLayer) ?? default;
            Canvas.SetLeft(_dragPreview, sourceOrigin.X); Canvas.SetTop(_dragPreview, sourceOrigin.Y);
            _dragPreview.RenderTransform = _previewTransform = new TranslateTransform();
            DragLayer.Children.Add(_dragPreview);
        }
        _previewTransform!.X = point.X - _dragStart.X;
        _previewTransform.Y = point.Y - _dragStart.Y;
        UpdatePageHover(point);
        Button? targetButton = null;
        if (_dragArea.Contains(point) && _dragSlots.Count > 0)
        {
            var nearest = _dragSlots.MinBy(slot => Point.Distance(slot.Bounds.Center, point));
            if (nearest.Button.DataContext is AppTile app && app.Id != tile.Id) targetButton = nearest.Button;
        }
        if (targetButton != _dropTarget)
        {
            _dropTarget?.Classes.Remove("drop-target"); _dropTarget = targetButton;
            _dropTarget?.Classes.Add("drop-target");
            var from = _dragSlots.FindIndex(slot => slot.Button.DataContext is AppTile app && app.Id == tile.Id);
            var to = _dragSlots.FindIndex(slot => slot.Button == _dropTarget);
            for (var i = 0; i < _dragSlots.Count; i++)
            {
                var slot = _dragSlots[i];
                if (slot.Button.DataContext is AppTile app && app.Id == tile.Id) continue;
                var destination = to < 0 || from < 0 ? i : from < to && i > from && i <= to ? i - 1 : from > to && i >= to && i < from ? i + 1 : i;
                var offset = _dragSlots[destination].Bounds.Position - slot.Bounds.Position;
                var host = (Control?)slot.Button.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() ?? slot.Button;
                if (host.Transitions is null)
                {
                    host.RenderTransform = TransformOperations.Parse("translate(0px, 0px)");
                    host.Transitions = new Transitions { new TransformOperationsTransition
                    {
                        Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut()
                    } };
                }
                host.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translate({offset.X}px, {offset.Y}px)"));
            }
            _model.Status = _dropTarget?.DataContext is AppTile target ? $"松开移到「{target.Name}」的位置" : "拖到目标卡片后松开；Esc 取消";
        }
        e.Handled = true;
    }

    private async void OnTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragTile is not { } tile || _dragSource is null) return;
        var dragged = _tileDragging;
        var target = _dropTarget?.DataContext as AppTile;
        var source = _dragSource;
        var origin = source.TranslatePoint(default, this);
        var clickInside = origin is { } p && new Rect(p, source.Bounds.Size).Contains(e.GetPosition(this));
        CancelTileDrag();
        e.Handled = true;
        if (!dragged)
        { if (clickInside) { if (tile.IsFolder) _model.OpenFolder(tile.Id); else _activate(tile, false); } return; }
        if (target is null) { _model.Status = "已取消排序"; return; }
        await RunAsync(() =>
        {
            if (_model.InFolder) _session.MoveFolderMember(tile.Id, _model.FolderId!, target.Id);
            else _session.MoveLauncher(tile.Id, target.Id);
            _model.Refresh(); _model.Status = "顺序已保存，星环同步更新";
            return Task.CompletedTask;
        });
    }

    private void CancelTileDrag()
    {
        _pageHoverTimer.Stop(); _pageHoverDirection = 0; _dragTile = null;
        if (_dragSource is not null) _dragSource.Opacity = 1;
        foreach (var slot in _dragSlots)
        {
            slot.Button.Opacity = 1;
            var host = (Control?)slot.Button.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() ?? slot.Button;
            host.ClearValue(RenderTransformProperty); host.ClearValue(TransitionsProperty);
        }
        _dragSlots.Clear(); _previewTransform = null;
        _dropTarget?.Classes.Remove("drop-target");
        _dragSource = _dropTarget = null; _tileDragging = false;
        DragLayer.Children.Clear(); _dragPreview = null;
        var pointer = _dragPointer; _dragPointer = null;
        pointer?.Capture(null);
    }

    private void UpdatePageHover(Point point)
    {
        bool Contains(Control control) => control.IsVisible && control.IsEnabled && control.TranslatePoint(default, this) is { } p && new Rect(p, control.Bounds.Size).Inflate(18).Contains(point);
        var direction = Contains(NextPageButton) ? 1 : Contains(PreviousPageButton) ? -1 : 0;
        if (_pageHoverDirection == direction) return;
        _pageHoverTimer.Stop(); _pageHoverDirection = direction;
        if (direction == 0) return;
        _model.Status = "停留片刻自动翻页，继续按住拖到目标应用后松开";
        _pageHoverTimer.Start();
    }

    private void OnDragPageHover(object? sender, EventArgs e)
    {
        _pageHoverTimer.Stop();
        if (!_tileDragging || _dragTile is null || _pageHoverDirection == 0) return;
        foreach (var slot in _dragSlots)
        {
            var host = (Control?)slot.Button.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() ?? slot.Button;
            host.ClearValue(RenderTransformProperty); host.ClearValue(TransitionsProperty);
            slot.Button.Opacity = 1;
        }
        _dropTarget?.Classes.Remove("drop-target"); _dropTarget = null;
        if (_pageHoverDirection > 0) _model.NextPage(); else _model.PreviousPage();
        _dragSlots.Clear();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_tileDragging || _dragTile is null) return;
            UpdateLayout();
            _dragArea = new Rect(AppList.TranslatePoint(default, this) ?? default, AppList.Bounds.Size);
            foreach (var item in AppList.GetVisualDescendants().OfType<Button>().Where(item => item.DataContext is AppTile))
            {
                item.Opacity = item.DataContext is AppTile app && app.Id == _dragTile.Id ? 0 : 1;
                if (item.TranslatePoint(default, this) is { } origin) _dragSlots.Add((item, new Rect(origin, item.Bounds.Size)));
            }
            _model.Status = $"已翻到第 {_model.Page + 1} 页，拖到目标卡片后松开";
            if (_pageHoverDirection > 0 ? _model.HasNext : _model.HasPrevious) _pageHoverTimer.Start();
        }, DispatcherPriority.Loaded);
    }
}
