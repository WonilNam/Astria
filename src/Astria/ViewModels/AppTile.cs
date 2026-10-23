using Avalonia.Media;
using Avalonia.Media.Imaging;
using Astria.Core.Models;

namespace Astria.Desktop.ViewModels;

public sealed class AppTile : ObservableObject
{
    private Bitmap? _icon;
    private bool _running;
    private bool _pinned;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public AppEntry? Entry { get; init; }
    public bool IsFolder { get; init; }
    public int MemberCount { get; init; }
    public string Subtitle => IsFolder ? $"{MemberCount} 个应用" : Entry?.IsTargetMissing == true ? "目标已失效" : IsPinned ? "常用" : "";
    public string Initial => IsFolder ? "▦" : string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public IBrush TileBrush => IsFolder ? Brush.Parse("#3D355B") : Brush.Parse("#28354E");
    public Bitmap? Icon { get => _icon; set { if (Set(ref _icon, value)) Notify(nameof(HasIcon)); } }
    public bool HasIcon => Icon is not null;
    public bool IsRunning { get => _running; set => Set(ref _running, value); }
    public bool IsPinned { get => _pinned; set { if (Set(ref _pinned, value)) Notify(nameof(Subtitle)); } }
}
